// Builds a mod's dependency manifest from its IL (docs/dependency-check.md): every member reference into the game
// assemblies, every type named, every [HarmonyPatch] target and every AccessTools lookup by name. A lookup that
// resolves to nothing is an error (the mod names a member the game does not have), which fails the build. Each entry
// records the mod's own top-level types that use it (DepEntry.UsedBy).
// Identical copies live in GK2QOLMods and GK2DonkeyAPI (tools/DepManifest): change both and keep them byte-identical.
using System.Text;
using GK2Shared;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DepBuilder
{
    public static DepManifest Build(ModuleDefinition mod, Func<string, ModuleDefinition> gameModule, List<string> errors)
    {
        DepManifest m = new DepManifest
        {
            GameMvid = gameModule("Assembly-CSharp").Mvid,
            LazyBearMvid = gameModule("LazyBearTechnology").Mvid,
        };
        Dictionary<string, DepEntry> entries = new Dictionary<string, DepEntry>(StringComparer.Ordinal);
        Dictionary<string, SortedSet<string>> usedBy = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        string owner = null;                       // the top-level type being read (nested types count for it)
        void Add(char kind, TypeReference scopeOf, string key, string hash)
        {
            string scope = ScopeName(scopeOf);
            string k = kind + " " + scope + " " + key;
            entries[k] = new DepEntry { Kind = kind, Scope = scope, Key = key, Hash = hash ?? "" };
            if (!usedBy.TryGetValue(k, out SortedSet<string> set)) usedBy[k] = set = new SortedSet<string>(StringComparer.Ordinal);
            if (owner != null) set.Add(owner);
        }
        void AddType(TypeReference t)
        {
            TypeReference e = Element(t);
            if (e == null || !IsGame(e)) return;
            Add('T', e, e.FullName, "");
        }
        void AddMethod(MethodDefinition d) => Add('M', d.DeclaringType, d.FullName, DepHash.Method(d));
        void AddField(FieldDefinition d) => Add('F', d.DeclaringType, d.FullName, DepHash.Field(d));
        // Everything declared on T with this name: methods (every overload, or those with the given parameter types),
        // fields, properties (their accessors), nested types (AccessTools.Inner).
        bool AddNamed(TypeDefinition t, string name, TypeReference[] parameters, string where)
        {
            int n = 0;
            foreach (TypeDefinition d in t.NestedTypes.Where(x => x.Name == name)) { Add('T', d, d.FullName, ""); n++; }
            foreach (MethodDefinition d in t.Methods.Where(x => x.Name == name && (parameters == null || SameParameters(x, parameters)))) { AddMethod(d); n++; }
            foreach (FieldDefinition d in t.Fields.Where(x => x.Name == name)) { AddField(d); n++; }
            foreach (PropertyDefinition p in t.Properties.Where(x => x.Name == name))
            {
                if (p.GetMethod != null) { AddMethod(p.GetMethod); n++; }
                if (p.SetMethod != null) { AddMethod(p.SetMethod); n++; }
            }
            if (n == 0) errors.Add($"{where}: {t.FullName} has no member named \"{name}\"" + (parameters == null ? "" : " with parameters (" + string.Join(", ", parameters.Select(p => p?.FullName)) + ")"));
            return n > 0;
        }

        foreach (TypeDefinition t in mod.GetTypes())
        {
            TypeDefinition top = t;
            while (top.DeclaringType != null) top = top.DeclaringType;
            owner = top.FullName;
            // Types named in signatures (fields, locals, parameters) and in IL (ldtoken, casts, newobj's type).
            foreach (FieldDefinition f in t.Fields) AddType(f.FieldType);
            foreach (MethodDefinition me in t.Methods)
            {
                AddType(me.ReturnType);
                foreach (ParameterDefinition p in me.Parameters) AddType(p.ParameterType);
                if (!me.HasBody) continue;
                foreach (VariableDefinition v in me.Body.Variables) AddType(v.VariableType);
                Mono.Collections.Generic.Collection<Instruction> il = me.Body.Instructions;
                for (int i = 0; i < il.Count; i++)
                {
                    object o = il[i].Operand;
                    if (o is TypeReference tr) { AddType(tr); continue; }
                    if (o is MemberReference mr && mr.DeclaringType != null)
                    {
                        // Not the pseudo-methods of an array of a game type (BuildCellData[,]::Address/Get/Set): they
                        // belong to the runtime, not to the game; the element type is already noted above.
                        if (IsGame(Element(mr.DeclaringType)) && !(mr.DeclaringType is ArrayType))
                        {
                            IMemberDefinition d = null;
                            try { d = mr is MethodReference mref ? (IMemberDefinition)mref.Resolve() : mr is FieldReference fref ? fref.Resolve() : null; } catch { }
                            if (d is MethodDefinition md) AddMethod(md);
                            else if (d is FieldDefinition fd) AddField(fd);
                            else errors.Add($"{me.FullName}: reference {mr.FullName} does not resolve in the game");
                        }
                        // A by-name lookup: AccessTools.X(typeof(T), "name", ...) or AccessTools.X<T, ...>("name").
                        if (mr is MethodReference call && call.DeclaringType.FullName == "HarmonyLib.AccessTools" && (il[i].OpCode == OpCodes.Call || il[i].OpCode == OpCodes.Callvirt))
                        {
                            ByName(call, il, i, me, AddNamed, errors);
                        }
                    }
                }
            }
            foreach (CustomAttribute a in t.CustomAttributes) HarmonyTarget(a, t.FullName, AddMethod, errors);
            foreach (MethodDefinition me in t.Methods)
                foreach (CustomAttribute a in me.CustomAttributes) HarmonyTarget(a, me.FullName, AddMethod, errors);
        }
        foreach (KeyValuePair<string, DepEntry> kv in entries)
        {
            DepEntry e = kv.Value;
            e.UsedBy = string.Join(",", usedBy[kv.Key]);
            m.Entries.Add(e);
        }
        return m;
    }

    // [HarmonyPatch(typeof(T), "name")], [HarmonyPatch(typeof(T), "name", new[] { typeof(A) })],
    // [HarmonyPatch(typeof(T), MethodType.Constructor, new Type[] { ... })], [HarmonyPatch(typeof(T), "Prop", MethodType.Getter)].
    // Resolved the way Harmony resolves them: declared on T, matching the argument types when given.
    private static void HarmonyTarget(CustomAttribute a, string where, Action<MethodDefinition> add, List<string> errors)
    {
        if (a.AttributeType.Name != "HarmonyPatch") return;
        TypeReference type = null; string name = null; int methodType = 0; TypeReference[] args = null;
        foreach (CustomAttributeArgument ca in a.ConstructorArguments)
        {
            if (ca.Value is TypeReference tr && type == null) type = tr;
            else if (ca.Value is string s) name = s;
            else if (ca.Type.Name == "MethodType" && ca.Value is int mt) methodType = mt;
            else if (ca.Value is CustomAttributeArgument[] arr && ca.Type is ArrayType) args = arr.Select(x => x.Value as TypeReference).ToArray();
        }
        if (type == null) return;                      // [HarmonyPatch] alone, or the target is in a TargetMethod(s) (a by-name lookup, found above)
        TypeDefinition t = null;
        try { t = Element(type)?.Resolve(); } catch { }
        if (t == null || !IsGame(t)) { if (t == null) errors.Add($"{where}: [HarmonyPatch] target type {type.FullName} does not resolve"); return; }
        // HarmonyLib.MethodType: Normal 0, Getter 1, Setter 2, Constructor 3, StaticConstructor 4, Enumerator 5, Async 6
        string methodName = methodType == 3 ? ".ctor" : methodType == 4 ? ".cctor" : methodType == 1 ? "get_" + name : methodType == 2 ? "set_" + name : name;
        if (methodName == null) { errors.Add($"{where}: [HarmonyPatch] on {t.FullName} names no method"); return; }
        List<MethodDefinition> found = t.Methods.Where(x => x.Name == methodName).ToList();
        if (args != null)
        {
            found = found.Where(x => x.Parameters.Count == args.Length && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(args.Select(y => y?.FullName))).ToList();
        }
        if (found.Count == 0) { errors.Add($"{where}: [HarmonyPatch] target {t.FullName}::{methodName}({(args == null ? "…" : string.Join(",", args.Select(x => x?.FullName)))}) not found"); return; }
        foreach (MethodDefinition d in found) add(d);
    }

    // The (type, name) of an AccessTools call. Generic form: AccessTools.FieldRefAccess<T, F>("name") takes T from the
    // call's generic arguments. (Type, string, ...) form: the name is the ldstr reached from the call backwards through
    // nothing but the IL that builds the later arguments (type arrays, nulls, numbers); anything else on the way (a
    // parameter, a local, a call) means the name is computed, which the manifest cannot follow, so it is an error and
    // the code names the member in a literal. The type is the typeof(T) right before the name (ldtoken T; call
    // Type.GetTypeFromHandle); a computed type (x.GetType()) is an error the same way. A lookup the code means to
    // survive when the member is absent uses System.Type's own reflection, which is not read here.
    private static void ByName(MethodReference call, Mono.Collections.Generic.Collection<Instruction> il, int at, MethodDefinition caller,
        Func<TypeDefinition, string, TypeReference[], string, bool> addNamed, List<string> errors)
    {
        MethodReference def = call is GenericInstanceMethod g ? g.ElementMethod : call;
        int stringParam = -1;
        for (int p = 0; p < def.Parameters.Count; p++)
        {
            if (def.Parameters[p].ParameterType.FullName == "System.String") { stringParam = p; break; }
        }
        if (stringParam < 0) return;                   // e.g. StaticFieldRefAccess<F>(FieldInfo): its FieldInfo came from a lookup seen on its own
        TypeReference type = null;
        if (call is GenericInstanceMethod gi && def.Parameters.Count > 0 && stringParam == 0)
        {
            type = gi.GenericArguments[0];             // FieldRefAccess<T, F>(string), StructFieldRefAccess<T, F>(string), StaticFieldRefAccess<T, F>(string)
        }
        string name = null;
        int callAt = at;
        for (int i = at - 1; i >= 0; i--)
        {
            if (il[i].OpCode == OpCodes.Ldstr) { name = (string)il[i].Operand; at = i; break; }
            if (!BuildsArgument(il[i])) break;
        }
        if (name == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name} with no string literal (the member name is computed; name it in a literal)"); return; }
        if (type == null)
        {
            int j = at - 1;
            while (j >= 0 && il[j].OpCode == OpCodes.Nop) j--;
            if (j >= 1 && il[j].OpCode == OpCodes.Call && il[j].Operand is MethodReference gt && gt.Name == "GetTypeFromHandle" && il[j - 1].OpCode == OpCodes.Ldtoken && il[j - 1].Operand is TypeReference tr)
            {
                type = tr;
            }
        }
        if (type == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name}(…, \"{name}\") with no typeof(T) right before the name (a computed type cannot be followed; write typeof)"); return; }
        TypeDefinition t = null;
        try { t = Element(type)?.Resolve(); } catch { }
        if (t == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name}(typeof({type.FullName}), \"{name}\"): the type does not resolve"); return; }
        if (!IsGame(t)) return;                        // a lookup into Unity, BepInEx, …: not the game's code
        // A Type[] after the name (AccessTools.Method's parameters): the first array's typeofs, so only those
        // overloads are listed. (A second array would be the generics; the lookups here use none.)
        List<TypeReference> parameters = null;
        for (int i = at + 1; i < callAt; i++)
        {
            if (il[i].OpCode == OpCodes.Newarr) { if (parameters != null) break; parameters = new List<TypeReference>(); }
            else if (parameters != null && il[i].OpCode == OpCodes.Ldtoken && il[i].Operand is TypeReference pt) parameters.Add(pt);
        }
        addNamed(t, name, parameters?.ToArray(), $"{caller.FullName}: AccessTools.{def.Name}");
    }

    private static bool SameParameters(MethodDefinition m, TypeReference[] parameters)
    {
        if (m.Parameters.Count != parameters.Length) return false;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i] == null || m.Parameters[i].ParameterType.FullName != parameters[i].FullName) return false;
        }
        return true;
    }

    // IL that only builds the arguments after the name of an AccessTools call: null, a Type[] of typeofs, numbers.
    private static bool BuildsArgument(Instruction x)
    {
        OpCode op = x.OpCode;
        if (op == OpCodes.Nop || op == OpCodes.Ldnull || op == OpCodes.Dup || op == OpCodes.Newarr || op == OpCodes.Ldtoken || op == OpCodes.Stelem_Ref || op == OpCodes.Stelem_Any) return true;
        if (op.Code >= Code.Ldc_I4_M1 && op.Code <= Code.Ldc_I4) return true;
        return op == OpCodes.Call && x.Operand is MethodReference m && m.Name == "GetTypeFromHandle";
    }

    // The definition behind a reference: generic instance -> its open type, array/byref/pointer -> the element.
    private static TypeReference Element(TypeReference t)
    {
        while (t != null)
        {
            if (t is GenericInstanceType gi) t = gi.ElementType;
            else if (t is TypeSpecification ts) t = ts.ElementType;   // arrays, byref, pointers, modifiers
            else break;
        }
        return t is GenericParameter ? null : t;
    }

    private static bool IsGame(TypeReference t) => t != null && t.Scope != null && DepManifest.GameScopes.Contains(ScopeName(t));

    private static string ScopeName(TypeReference t)
    {
        IMetadataScope s = t.Scope;
        return s is AssemblyNameReference an ? an.Name : s is ModuleDefinition md ? md.Assembly.Name.Name : s.Name.Replace(".dll", "");
    }
}
