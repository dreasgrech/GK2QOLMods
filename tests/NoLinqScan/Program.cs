using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

// No LINQ in anything that runs in the game (the user, 2026-10-25: "disallow ALL LINQ usage going forward"; every call
// allocates garbage and the game pauses for Unity's collections). The build already refuses it (Directory.Build.targets:
// the banned-API analyzer, RS0030); this checks the built DLLs as well, for what the analyzer can't see (a method group,
// a lambda the compiler lifted, an expression a generator wrote): every netstandard2.1 project of the repo, its own
// assembly from bin/Release or bin/Debug (the newer), every IL operand, local, field, signature, base type and attribute.
// Usage: dotnet run -c Release      (build the mods first; a project not built is said, not failed)
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
int fails = 0, scanned = 0;
foreach (string dir in new[] { "src", "tools" })
{
    string root = Path.Combine(repo, dir);
    if (!Directory.Exists(root)) continue;
    foreach (string proj in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
    {
        string text = File.ReadAllText(proj);
        if (!text.Contains("<TargetFramework>netstandard2.1</TargetFramework>")) continue;
        Match an = Regex.Match(text, "<AssemblyName>([^<]+)</AssemblyName>");
        string name = an.Success ? an.Groups[1].Value : Path.GetFileNameWithoutExtension(proj);
        string projDir = Path.GetDirectoryName(proj)!;
        string? dll = null;
        foreach (string cfg in new[] { "Release", "Debug" }) // the newer of the two: a stale Release isn't what was just built
        {
            string p = Path.Combine(projDir, "bin", cfg, "netstandard2.1", name + ".dll");
            if (File.Exists(p) && (dll == null || File.GetLastWriteTimeUtc(p) > File.GetLastWriteTimeUtc(dll))) dll = p;
        }
        string rel = Path.GetRelativePath(repo, proj);
        if (dll == null) { Console.WriteLine($"NOT BUILT {rel} ({name}.dll): build it, then scan again"); continue; }
        scanned++;
        int found = Scan(dll, out List<string> sites);
        Console.WriteLine((found == 0 ? "PASS " : "FAIL ") + $"{name} ({Path.GetRelativePath(repo, dll)}): {(found == 0 ? "no LINQ" : found + " LINQ reference(s)")}");
        foreach (string s in sites) Console.WriteLine("     " + s);
        fails += found;
    }
}
Console.WriteLine(fails == 0 ? $"ALL PASS ({scanned} assemblies)" : $"FAILURES: {fails} LINQ reference(s)");
return fails == 0 ? 0 : 1;

static int Scan(string dll, out List<string> sites)
{
    var found = new List<string>();
    using var mod = ModuleDefinition.ReadModule(dll, new ReaderParameters { ReadSymbols = false });
    void Note(string where, TypeReference? t)
    {
        if (Linq(t)) found.Add($"{where}: {t!.FullName}");
    }
    foreach (TypeDefinition t in mod.GetTypes())
    {
        Note(t.FullName + " (base)", t.BaseType);
        foreach (InterfaceImplementation i in t.Interfaces) Note(t.FullName + " (interface)", i.InterfaceType);
        foreach (CustomAttribute a in t.CustomAttributes) Note(t.FullName + " (attribute)", a.AttributeType);
        foreach (FieldDefinition f in t.Fields) Note($"{t.FullName}.{f.Name} (field)", f.FieldType);
        foreach (MethodDefinition m in t.Methods)
        {
            string at = $"{t.FullName}.{m.Name}";
            Note(at + " (returns)", m.ReturnType);
            foreach (ParameterDefinition p in m.Parameters) Note(at + " (parameter)", p.ParameterType);
            if (!m.HasBody) continue;
            foreach (VariableDefinition v in m.Body.Variables) Note(at + " (local)", v.VariableType);
            foreach (Instruction ins in m.Body.Instructions)
            {
                switch (ins.Operand)
                {
                    case MethodReference mr:
                        if (Linq(mr.DeclaringType) || Linq(mr.ReturnType) || (mr is GenericInstanceMethod gm && gm.GenericArguments.Any(Linq)))
                            found.Add($"{at} (IL {ins.OpCode} at {ins.Offset}): {mr.FullName}");
                        break;
                    case FieldReference fr:
                        if (Linq(fr.DeclaringType) || Linq(fr.FieldType)) found.Add($"{at} (IL {ins.OpCode} at {ins.Offset}): {fr.FullName}");
                        break;
                    case TypeReference tr:
                        if (Linq(tr)) found.Add($"{at} (IL {ins.OpCode} at {ins.Offset}): {tr.FullName}");
                        break;
                }
            }
        }
    }
    sites = found;
    return found.Count;
}

static bool Linq(TypeReference? r)
{
    if (r == null) return false;
    if (r is GenericInstanceType g && g.GenericArguments.Any(Linq)) return true;
    if (r is TypeSpecification ts) return Linq(ts.ElementType);
    if (r.DeclaringType != null) return Linq(r.DeclaringType);
    return r.Namespace == "System.Linq" || r.Namespace.StartsWith("System.Linq.", StringComparison.Ordinal);
}
