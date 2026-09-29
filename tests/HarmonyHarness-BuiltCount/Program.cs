using System; using System.IO; using System.Linq; using System.Reflection; using HarmonyLib;
// Loads the game assemblies and the built mod in a .NET Framework process (GK2.Framework.dll deliberately not
// resolvable, as for a player without the framework) and applies the mod's patches the way Plugin does: class by class
// from Plugin.PatchClasses. Assembly.GetTypes() fails for FrameworkLink's framework-typed class here; that is expected
// and is the reason the plugin never calls PatchAll(assembly). Anything else failing to load is a FAIL.
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

    Type[] types;
    try { types = asm.GetTypes(); Console.WriteLine("INFO all types load (GK2.Framework.dll was resolvable)"); }
    catch (ReflectionTypeLoadException e) {
      types = e.Types.Where(t => t != null).ToArray();
      var unexpected = e.LoaderExceptions.Where(x => !(x is FileNotFoundException f && (f.FileName ?? "").StartsWith("GK2.Framework"))).ToList();
      bool good = unexpected.Count == 0;
      Console.WriteLine((good?"PASS":"FAIL")+$" without GK2.Framework.dll only framework types fail to load ({e.LoaderExceptions.Length} loader exception(s), {unexpected.Count} unexpected)");
      foreach (var x in unexpected) Console.WriteLine("     "+x.GetType().Name+": "+x.Message);
      if (!good) fails++;
    }

    var plugin = asm.GetType("GK2BuiltCount.Plugin", true);
    var mvid = (Guid)plugin.GetField("AuditedGameMvid", BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    Console.WriteLine((mvid==game.ManifestModule.ModuleVersionId?"PASS":"FAIL")+" audited MVID "+mvid); if (mvid!=game.ManifestModule.ModuleVersionId) fails++;

    var patchClasses = (Type[])plugin.GetField("PatchClasses", BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    var attributed = types.Where(t=>t.GetCustomAttributes(typeof(HarmonyPatch), false).Length>0).ToList();
    bool same = attributed.All(patchClasses.Contains) && patchClasses.All(attributed.Contains);
    Console.WriteLine((same?"PASS":"FAIL")+$" Plugin.PatchClasses ({patchClasses.Length}) = the [HarmonyPatch] classes ({attributed.Count})"); if (!same) fails++;
    foreach (var t in patchClasses) {
      try { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle); } catch (Exception e) { Console.WriteLine("FAIL cctor "+t.Name+": "+e.InnerException?.Message); fails++; }
    }
    var h = new Harmony("test."+asm.GetName().Name);
    try { foreach (var t in patchClasses) h.PatchAll(t); } catch (Exception e) { Console.WriteLine("FAIL PatchAll: "+e); fails++; continue; }
    foreach (var m in h.GetPatchedMethods().OrderBy(m=>m.DeclaringType.Name+m.Name)) {
      var i = Harmony.GetPatchInfo(m);
      string sig = m.DeclaringType.Name+"."+m.Name+"("+string.Join(",", m.GetParameters().Select(p=>p.ParameterType.Name))+")";
      Console.WriteLine($"PASS {sig}  pre={i.Prefixes.Count(p=>p.owner==h.Id)} post={i.Postfixes.Count(p=>p.owner==h.Id)} fin={i.Finalizers.Count(p=>p.owner==h.Id)}");
    }
  }
  Console.WriteLine(fails==0?"ALL PASS":"FAILURES: "+fails); return fails; } }
