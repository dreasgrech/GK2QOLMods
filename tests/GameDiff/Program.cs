using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// The first step of every game update: which members of the game's code were removed, added or changed between
// two installs. Compares Assembly-CSharp and LazyBearTechnology member by member (fields and properties by
// type, methods by a hash of their IL with operands resolved to names). Removed members named <...> are
// compiler-generated (lambdas, closures, state machines) and get renumbered by any rebuild; the rest is what to
// check against every patch target and every game member the mods use.
//
// dotnet run -c Release -- <old game folder> [new game folder, default: GameDir] [output file, default: %TEMP%\gamediff.txt]
if (args.Length < 1)
{
    Console.WriteLine("usage: GameDiff <old game folder> [new game folder] [output file]");
    return 2;
}
string Managed(string gameDir) => Path.Combine(gameDir, "GraveyardKeeper2_Data", "Managed") + Path.DirectorySeparatorChar;
var oldDir = Managed(args[0]);
var newDir = Managed(args.Length > 1 ? args[1] : GamePaths.GameDir);
var outPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "gamediff.txt"); // not into the repo
var sb = new StringBuilder();
sb.AppendLine("old: " + oldDir);
sb.AppendLine("new: " + newDir);

foreach (var asm in new[] { "Assembly-CSharp.dll", "LazyBearTechnology.dll" })
{
    var a = Index(oldDir + asm, out Guid oldMvid);
    var b = Index(newDir + asm, out Guid newMvid);
    var removed = a.Keys.Where(k => !b.ContainsKey(k)).OrderBy(k => k).ToList();
    var added = b.Keys.Where(k => !a.ContainsKey(k)).OrderBy(k => k).ToList();
    var changed = a.Keys.Where(k => b.ContainsKey(k) && a[k] != b[k]).OrderBy(k => k).ToList();
    int named = removed.Count(k => !k.Contains('<'));
    sb.AppendLine($"##### {asm}: MVID {oldMvid} -> {newMvid}; {a.Count} -> {b.Count} members; removed {removed.Count} ({named} not compiler-generated), added {added.Count}, changed {changed.Count}");
    sb.AppendLine("--- REMOVED"); removed.ForEach(x => sb.AppendLine("  " + x));
    sb.AppendLine("--- ADDED"); added.ForEach(x => sb.AppendLine("  " + x));
    sb.AppendLine("--- CHANGED"); changed.ForEach(x => sb.AppendLine("  " + x));
}
File.WriteAllText(outPath, sb.ToString());
foreach (var line in sb.ToString().Split('\n').Where(l => l.StartsWith("#####")))
{
    Console.WriteLine(line.TrimEnd());
}
Console.WriteLine("Full list: " + Path.GetFullPath(outPath));
return 0;

static Dictionary<string, string> Index(string path, out Guid mvid)
{
    var res = new DefaultAssemblyResolver();
    res.AddSearchDirectory(Path.GetDirectoryName(path));
    var m = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = res });
    mvid = m.Mvid;
    var d = new Dictionary<string, string>();
    foreach (var t in m.GetTypes())
    {
        foreach (var f in t.Fields) d["F " + f.FullName] = f.FieldType.FullName + (f.IsStatic ? " static" : "") + " " + f.Attributes;
        foreach (var p in t.Properties) d["P " + p.FullName] = p.PropertyType.FullName;
        foreach (var me in t.Methods)
        {
            var body = new StringBuilder(me.Attributes.ToString());
            if (me.HasBody)
            {
                foreach (var i in me.Body.Instructions)
                {
                    body.Append(i.OpCode.Name).Append(' ');
                    body.Append(i.Operand switch
                    {
                        MemberReference r => r.FullName,
                        Instruction j => "@" + j.Offset,
                        Instruction[] js => string.Join(",", js.Select(x => x.Offset)),
                        VariableDefinition v => "v" + v.Index,
                        ParameterDefinition pd => "p" + pd.Index,
                        null => "",
                        var o => o.ToString()
                    }).Append(';');
                }
            }
            d["M " + me.FullName] = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(body.ToString())));
        }
    }
    return d;
}
