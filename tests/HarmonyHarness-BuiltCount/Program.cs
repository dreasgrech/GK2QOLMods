using System; using System.IO; using System.Linq; using System.Reflection; using HarmonyLib;
class P { static int Main() {
  var dirs = new[]{GamePaths.Managed, GamePaths.BepInExCore};
  AppDomain.CurrentDomain.AssemblyResolve += (s,e)=>{ var n=new AssemblyName(e.Name).Name+".dll"; foreach(var d in dirs){var p=Path.Combine(d,n); if(File.Exists(p)) return Assembly.LoadFrom(p);} return null; };
  var game = Assembly.LoadFrom(Path.Combine(dirs[0],"Assembly-CSharp.dll"));
  Console.WriteLine("Game MVID "+game.ManifestModule.ModuleVersionId);
  int fails = 0;
  var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net48 -> repo root
  foreach (var path in new[]{Path.Combine(repo, "src", "GK2BuiltCount", "bin", "Release", "netstandard2.1", "GK2BuiltCount.dll")}) {
    var asm = Assembly.LoadFrom(path);
    Console.WriteLine("== "+asm.GetName().Name);
    var mvidField = asm.GetTypes().Select(t=>t.GetField("AuditedGameMvid", BindingFlags.NonPublic|BindingFlags.Static)).FirstOrDefault(f=>f!=null);
    var mvid = (Guid)mvidField.GetValue(null);
    Console.WriteLine((mvid==game.ManifestModule.ModuleVersionId?"PASS":"FAIL")+" audited MVID "+mvid); if (mvid!=game.ManifestModule.ModuleVersionId) fails++;
    foreach (var t in asm.GetTypes().Where(t=>t.GetCustomAttributes(typeof(HarmonyPatch), false).Length>0)) {
      try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); } catch (Exception e) { Console.WriteLine("FAIL cctor "+t.Name+": "+e.InnerException?.Message); fails++; }
    }
    var h = new Harmony("test."+asm.GetName().Name);
    try { h.PatchAll(asm); } catch (Exception e) { Console.WriteLine("FAIL PatchAll: "+e); fails++; continue; }
    foreach (var m in h.GetPatchedMethods().OrderBy(m=>m.DeclaringType.Name+m.Name)) {
      var i = Harmony.GetPatchInfo(m);
      string sig = m.DeclaringType.Name+"."+m.Name+"("+string.Join(",", m.GetParameters().Select(p=>p.ParameterType.Name))+")";
      Console.WriteLine($"PASS {sig}  pre={i.Prefixes.Count(p=>p.owner==h.Id)} post={i.Postfixes.Count(p=>p.owner==h.Id)} fin={i.Finalizers.Count(p=>p.owner==h.Id)}");
    }
  }
  Console.WriteLine(fails==0?"ALL PASS":"FAILURES: "+fails); return fails; } }
