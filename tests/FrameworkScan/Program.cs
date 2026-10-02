using Mono.Cecil;
using Mono.Cecil.Cil;

// The optional GK2 Mod Framework integration, checked in every mod that has it (docs/gk2-mod-framework.md).
// One DLL must load and run without GK2.Framework.dll, so for each mod:
//  1. every [HarmonyPatch] class is in Plugin.PatchClasses (patched by class; PatchAll(assembly) would list the
//     framework-typed class and make Harmony warn when the framework is absent);
//  2. Plugin has no method Unity calls by name (a helper named Start once ran twice and patched everything twice);
//  3. only FrameworkLink.Register and the classes nested in FrameworkLink touch GK2.Framework: base types,
//     interfaces, attributes, fields, signatures, locals and every IL operand are checked;
//  4. Register is NoInlining, and Plugin has a soft BepInDependency on the framework.
// Usage: dotnet run -c Release [ModName ...]   (default: every mod below; each must be built in Release first)
string[] integrated = { "GK2OwnedCount", "GK2GlobalStorage", "GK2MoveBuilding", "GK2BuiltCount" };
string[] mods = args.Length > 0 ? args : integrated;

var res = new DefaultAssemblyResolver();
res.AddSearchDirectory(GamePaths.Managed);
res.AddSearchDirectory(GamePaths.BepInExCore);
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root

int fails = 0;
foreach (string name in mods)
{
    Console.WriteLine($"=== {name}");
    var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", name, "bin", "Release", "netstandard2.1", name + ".dll"), rp);
    fails += Check(mod, name);
}
Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;

static int Check(ModuleDefinition mod, string ns)
{
    int fails = 0;
    void Result(bool good, string what) { Console.WriteLine((good ? "PASS " : "FAIL ") + what); if (!good) fails++; }

    var plugin = mod.GetType(ns + ".Plugin");
    var link = mod.GetType(ns + ".FrameworkLink");
    Result(plugin != null, "Plugin class found");
    Result(link != null, "FrameworkLink class found");
    if (plugin == null || link == null)
    {
        return fails;
    }

    // 1. patch list
    var cctor = plugin.Methods.FirstOrDefault(m => m.Name == ".cctor");
    var listed = cctor == null ? new HashSet<string>() : cctor.Body.Instructions
        .Where(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference)
        .Select(i => ((TypeReference)i.Operand).FullName).ToHashSet();
    var patchClasses = mod.GetTypes().Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")).ToList();
    // A mod without patches has nothing to list; the PatchAll(Assembly) check below still applies.
    Console.WriteLine($"INFO {patchClasses.Count} [HarmonyPatch] classes found");
    foreach (var t in patchClasses)
    {
        Result(listed.Contains(t.FullName), $"patch class {t.Name} in Plugin.PatchClasses");
    }
    bool patchAllAssembly = plugin.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
        .Any(i => i.Operand is MethodReference r && r.Name == "PatchAll" && r.Parameters.Count == 1 && r.Parameters[0].ParameterType.FullName == "System.Reflection.Assembly");
    Result(!patchAllAssembly, "Plugin never calls PatchAll(Assembly)");

    // 2. Unity messages
    foreach (var m in new[] { "Start", "OnEnable", "OnDisable", "FixedUpdate", "LateUpdate", "OnApplicationQuit" })
    {
        if (plugin.Methods.Any(x => x.Name == m))
        {
            Result(false, $"Plugin has a method named {m} (Unity would call it)");
        }
    }
    Result(true, "Plugin has no unintended Unity message methods (Start, OnEnable, OnDisable, ...)");

    // 3. framework isolation
    int registerUses = 0;
    foreach (var t in mod.GetTypes())
    {
        if (IsNestedIn(t, link))
        {
            continue; // the framework-facing mod class (and its compiler-generated closures)
        }
        void Bad(string what) => Result(false, $"{t.FullName} uses GK2.Framework: {what}");
        if (Fw(t.BaseType)) Bad("base type");
        foreach (var i in t.Interfaces) if (Fw(i.InterfaceType)) Bad("interface");
        foreach (var a in t.CustomAttributes) if (Fw(a.AttributeType)) Bad("attribute");
        foreach (var f in t.Fields) if (Fw(f.FieldType)) Bad("field " + f.Name);
        foreach (var m in t.Methods)
        {
            bool uses = Fw(m.ReturnType) || m.Parameters.Any(p => Fw(p.ParameterType))
                || (m.HasBody && (m.Body.Variables.Any(v => Fw(v.VariableType)) || m.Body.Instructions.Any(i => FwOperand(i.Operand))));
            if (!uses) continue;
            if (t == link && m.Name == "Register") registerUses++;
            else Bad("method " + m.Name);
        }
    }
    Result(registerUses == 1, "GK2.Framework is used only by FrameworkLink.Register and its nested mod class");

    // 4. inlining and load order
    var register = link.Methods.FirstOrDefault(m => m.Name == "Register");
    Result(register != null && register.ImplAttributes.HasFlag(MethodImplAttributes.NoInlining), "FrameworkLink.Register is NoInlining");
    Result(plugin.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency"
            && (string)a.ConstructorArguments[0].Value == "ru.superman4eg.gk2.framework"
            && Convert.ToInt32(a.ConstructorArguments[1].Value) == 2),
        "soft BepInDependency on ru.superman4eg.gk2.framework");
    return fails;
}

static bool IsNestedIn(TypeDefinition t, TypeDefinition outer)
{
    for (var d = t.DeclaringType; d != null; d = d.DeclaringType)
    {
        if (d == outer) return true;
    }
    return false;
}

static bool Fw(TypeReference? r)
{
    if (r == null) return false;
    if (r is GenericInstanceType g && g.GenericArguments.Any(Fw)) return true;
    if (r is TypeSpecification ts) return Fw(ts.ElementType);
    return r.Scope?.Name == "GK2.Framework";
}

static bool FwOperand(object? o) => o switch
{
    TypeReference tr => Fw(tr),
    MethodReference mr => Fw(mr.DeclaringType) || Fw(mr.ReturnType) || mr.Parameters.Any(p => Fw(p.ParameterType))
        || (mr is GenericInstanceMethod gm && gm.GenericArguments.Any(Fw)),
    FieldReference fr => Fw(fr.DeclaringType) || Fw(fr.FieldType),
    _ => false,
};
