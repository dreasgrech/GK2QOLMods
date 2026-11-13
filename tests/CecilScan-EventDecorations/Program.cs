using Mono.Cecil; using Mono.Cecil.Cil;
var managed = GamePaths.Managed;
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(managed); res.AddSearchDirectory(GamePaths.BepInExCore);
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", "GK2EventDecorations", "bin", "Release", "netstandard2.1", "GK2EventDecorations.dll"), rp);
var game = ModuleDefinition.ReadModule(managed + "Assembly-CSharp.dll", rp);
Console.WriteLine("Game MVID " + game.Mvid);
int fails = 0, ok = 0;
var gameScopes = new HashSet<string> { "Assembly-CSharp", "LazyBearTechnology", "AstarPathfindingProject" };
var seen = new HashSet<string>();
foreach (var t in mod.GetTypes()) foreach (var m in t.Methods.Where(m => m.HasBody)) foreach (var ins in m.Body.Instructions) {
  if (ins.Operand is MemberReference mr && !(mr is TypeReference) && mr.DeclaringType != null && !(mr.DeclaringType is ArrayType) && gameScopes.Contains(mr.DeclaringType.Scope.Name)) {
    if (!seen.Add(mr.FullName)) continue;
    IMemberDefinition d = null;
    try { d = mr is MethodReference meth ? meth.Resolve() : mr is FieldReference f ? f.Resolve() : null; } catch {}
    if (d == null) { Console.WriteLine("FAIL unresolved: " + mr.FullName); fails++; } else ok++;
  }
}
Console.WriteLine($"IL references into game assemblies: {ok} resolved, {fails} unresolved");
void Check(bool good, string what) { Console.WriteLine((good ? "PASS " : "FAIL ") + what); if (!good) fails++; }

// The gate: both overloads, static bool, the second taking the date (docs/seasonal-events.md).
var dates = game.GetType("TemporaryEventDates");
Check(dates != null, "type TemporaryEventDates");
var gates = dates?.Methods.Where(x => x.Name == "IsHalloweenEvent").ToList() ?? new List<MethodDefinition>();
Check(gates.Count == 2 && gates.All(g => g.IsStatic && g.ReturnType.FullName == "System.Boolean"), $"TemporaryEventDates.IsHalloweenEvent: {gates.Count} static bool overloads");
Check(gates.Any(g => g.Parameters.Count == 0), "IsHalloweenEvent()");
Check(gates.Any(g => g.Parameters.Count == 1 && g.Parameters[0].ParameterType.FullName == "System.DateTime"), "IsHalloweenEvent(DateTime)");

// Every event gate the game has. A new one is not a failure (the mod gives it a Mode row by itself), but its content
// should be looked at: tools/game-bundles.py, docs/seasonal-events.md "On a game update".
var events = dates?.Methods.Where(x => x.IsStatic && x.IsPublic && x.ReturnType.FullName == "System.Boolean" && x.Name.StartsWith("Is") && x.Name.EndsWith("Event") && x.Name.Length > 7)
  .Select(x => x.Name.Substring(2, x.Name.Length - 7)).Distinct().OrderBy(x => x).ToList() ?? new List<string>();
Console.WriteLine("INFO events in the game: " + string.Join(", ", events));
foreach (var e in events.Where(e => e != "Halloween")) Console.WriteLine("NOTE new event " + e + ": handled generically; look at its content");

// The ghost's check: the load event and the walk flag.
var mainGame = game.GetType("MainGame");
var started = mainGame?.Fields.FirstOrDefault(x => x.Name == "OnGameStarted");
Check(started != null && started.IsStatic && started.IsPublic && started.FieldType.FullName == "System.Action", "MainGame.OnGameStarted: public static Action");
var moving = game.GetType("MovementComponent")?.Properties.FirstOrDefault(x => x.Name == "IsMoving");
Check(moving != null && moving.PropertyType.FullName == "System.Boolean", "MovementComponent.IsMoving");

// The framework-integration checks (patch list, Unity message names, GK2.Framework isolation) are in
// tests/FrameworkScan, shared by every integrated mod.

Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
