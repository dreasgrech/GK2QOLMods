using Mono.Cecil;

// The public API of GK2 Mounts (namespace GK2Mounts.Api), written as one line per type and member and compared with
// the committed snapshot GK2Mounts.Api.txt. Any difference fails, so an API change is always a reviewed diff:
// additions bump Mounts.ApiVersion's minor, changes or removals its major (docs/mounts-api-design.md).
// Usage: dotnet run -c Release            compare (build GK2Mounts in Release first)
//        dotnet run -c Release -- --update  write the snapshot from the current build
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var dll = Path.Combine(repo, "src", "GK2Mounts", "bin", "Release", "netstandard2.1", "GK2Mounts.dll");
var snapshot = Path.Combine(repo, "tests", "ApiSurface", "GK2Mounts.Api.txt");
var mod = ModuleDefinition.ReadModule(dll);

var lines = new List<string>();
foreach (var t in mod.Types.Where(t => t.IsPublic && t.Namespace == "GK2Mounts.Api").OrderBy(t => t.FullName))
{
    string kind = t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsInterface ? "interface" : (t.IsAbstract && t.IsSealed) ? "static class" : "class";
    lines.Add($"{kind} {t.FullName}");
    if (t.IsEnum)
    {
        foreach (var f in t.Fields.Where(f => f.IsStatic && f.IsPublic))
            lines.Add($"  {f.Name} = {f.Constant}");
        continue;
    }
    foreach (var f in t.Fields.Where(f => f.IsPublic).OrderBy(f => f.Name))
        lines.Add($"  field {(f.IsStatic ? "static " : "")}{(f.IsLiteral ? "const " : f.IsInitOnly ? "readonly " : "")}{Name(f.FieldType)} {f.Name}{(f.IsLiteral ? " = " + f.Constant : "")}");
    foreach (var p in t.Properties.Where(p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false)).OrderBy(p => p.Name))
        lines.Add($"  property {((p.GetMethod ?? p.SetMethod)!.IsStatic ? "static " : "")}{Name(p.PropertyType)} {p.Name} {{{(p.GetMethod?.IsPublic == true ? " get;" : "")}{(p.SetMethod?.IsPublic == true ? " set;" : "")} }}");
    foreach (var e in t.Events.Where(e => e.AddMethod?.IsPublic ?? false).OrderBy(e => e.Name))
        lines.Add($"  event {(e.AddMethod.IsStatic ? "static " : "")}{Name(e.EventType)} {e.Name}");
    foreach (var m in t.Methods.Where(m => m.IsPublic && !m.IsGetter && !m.IsSetter && !m.IsAddOn && !m.IsRemoveOn && m.Name != ".cctor")
                 .OrderBy(m => m.Name).ThenBy(m => m.Parameters.Count))
        lines.Add($"  method {(m.IsStatic ? "static " : "")}{Name(m.ReturnType)} {m.Name}({string.Join(", ", m.Parameters.Select(p => Name(p.ParameterType) + " " + p.Name + (p.HasConstant ? " = " + (p.Constant ?? "null") : "")))})");
}

string text = string.Join("\n", lines) + "\n";
if (args.Contains("--update"))
{
    File.WriteAllText(snapshot, text);
    Console.WriteLine($"Snapshot written: {lines.Count} lines -> {snapshot}");
    return 0;
}
string old = File.Exists(snapshot) ? File.ReadAllText(snapshot).Replace("\r\n", "\n") : "";
if (old == text)
{
    Console.WriteLine($"PASS public API unchanged ({lines.Count} lines)");
    Console.WriteLine("ALL PASS");
    return 0;
}
var oldLines = old.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
var newLines = lines.ToHashSet();
foreach (var l in oldLines.Except(newLines)) Console.WriteLine("REMOVED  " + l);
foreach (var l in newLines.Except(oldLines)) Console.WriteLine("ADDED    " + l);
Console.WriteLine("FAIL the public API differs from tests/ApiSurface/GK2Mounts.Api.txt. Review, bump Mounts.ApiVersion, then run with --update.");
return 1;

static string Name(TypeReference t)
{
    if (t is GenericInstanceType g)
        return Name(g.ElementType).Split('`')[0] + "<" + string.Join(", ", g.GenericArguments.Select(Name)) + ">";
    return t.FullName switch
    {
        "System.Void" => "void", "System.String" => "string", "System.Boolean" => "bool", "System.Int32" => "int",
        "System.Single" => "float", "System.Object" => "object",
        _ => t.FullName,
    };
}
