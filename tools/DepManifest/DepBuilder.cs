// Builds a mod's dependency manifest from its IL (docs/dependency-check.md): every member reference into the game
// assemblies, every type named, every [HarmonyPatch] target and every AccessTools lookup by name. A lookup that
// resolves to nothing is an error (the mod names a member the game does not have), which fails the build.
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
        void Add(char kind, TypeReference scopeOf, string key, string hash)
        {
            string scope = ScopeName(scopeOf);
            entries[kind + " " + scope + " " + key] = new DepEntry { Kind = kind, Scope = scope, Key = key, Hash = hash ?? "" };
        }
        void AddType(TypeReference t)
        {
            TypeReference e = Element(t);
            if (e == null || !IsGame(e)) return;
            Add('T', e, e.FullName, "");
        }
        void AddMethod(MethodDefinition d) => Add('M', d.DeclaringType, d.FullName, DepHash.Method(d));
        void AddField(FieldDefinition d) => Add('F', d.DeclaringType, d.FullName, DepHash.Field(d));
        // Everything declared on T with this name: methods (every overload), fields, properties (their accessors).
        bool AddNamed(TypeDefinition t, string name, string where)
        {
            int n = 0;
            foreach (MethodDefinition d in t.Methods.Where(x => x.Name == name)) { AddMethod(d); n++; }
            foreach (FieldDefinition d in t.Fields.Where(x => x.Name == name)) { AddField(d); n++; }
            foreach (PropertyDefinition p in t.Properties.Where(x => x.Name == name))
            {
                if (p.GetMethod != null) { AddMethod(p.GetMethod); n++; }
                if (p.SetMethod != null) { AddMethod(p.SetMethod); n++; }
            }
            if (n == 0) errors.Add($"{where}: {t.FullName} has no member named \"{name}\"");
            return n > 0;
        }

        foreach (TypeDefinition t in mod.GetTypes())
        {
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
        m.Entries.AddRange(entries.Values);
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
    // call's generic arguments. (Type, string, ...) form: walking back from the call, the first ldstr is the name and
    // the first ldtoken before it is the type (an argument-types array after the name holds ldtokens too, but they
    // come after the ldstr, so the walk passes them before it reaches the name).
    private static void ByName(MethodReference call, Mono.Collections.Generic.Collection<Instruction> il, int at, MethodDefinition caller,
        Func<TypeDefinition, string, string, bool> addNamed, List<string> errors)
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
        for (int i = at - 1; i >= 0; i--)
        {
            if (il[i].OpCode == OpCodes.Ldstr) { name = (string)il[i].Operand; at = i; break; }
        }
        if (name == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name} with no string literal (the member name is computed; name it in a literal)"); return; }
        if (type == null)
        {
            for (int i = at - 1; i >= 0; i--)
            {
                if (il[i].OpCode == OpCodes.Ldtoken && il[i].Operand is TypeReference tr) { type = tr; break; }
            }
        }
        if (type == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name}(…, \"{name}\") with no typeof(T) before it"); return; }
        TypeDefinition t = null;
        try { t = Element(type)?.Resolve(); } catch { }
        if (t == null) { errors.Add($"{caller.FullName}: AccessTools.{def.Name}(typeof({type.FullName}), \"{name}\"): the type does not resolve"); return; }
        if (!IsGame(t)) return;                        // a lookup into Unity, BepInEx, …: not the game's code
        addNamed(t, name, $"{caller.FullName}: AccessTools.{def.Name}");
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
