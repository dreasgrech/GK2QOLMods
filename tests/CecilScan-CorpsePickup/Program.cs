using Mono.Cecil; using Mono.Cecil.Cil;
var managed = GamePaths.Managed;
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(managed); res.AddSearchDirectory(GamePaths.BepInExCore);
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", "GK2CorpsePickup", "bin", "Release", "netstandard2.1", "GK2CorpsePickup.dll"), rp);
var game = ModuleDefinition.ReadModule(managed + "Assembly-CSharp.dll", rp);
Console.WriteLine("Game MVID " + game.Mvid);
int fails = 0, ok = 0;
// The game's own code and the visual-scripting library whose port types the patch takes.
var gameScopes = new HashSet<string> { "Assembly-CSharp", "LazyBearTechnology", "FlowCanvas" };
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
void Field(string type, string name, string ftype) { var td = game.GetType(type); var f = td?.Fields.FirstOrDefault(x => x.Name == name);
  bool good = f != null && f.FieldType.FullName == ftype; Console.WriteLine((good?"PASS":"FAIL") + $" field {type}.{name} : {f?.FieldType.FullName}"); if (!good) fails++; }
void Method(string type, string name, int pars, int overloads = 1, string firstParam = null) { var td = game.GetType(type); var ms = td?.Methods.Where(x => x.Name == name).ToList();
  var m = ms?.FirstOrDefault(x => x.Parameters.Count == pars && (firstParam == null || x.Parameters[0].ParameterType.FullName == firstParam));
  bool good = ms != null && ms.Count == overloads && m != null && m.HasBody; Console.WriteLine((good?"PASS":"FAIL") + $" method {type}.{name} overloads={ms?.Count} params={pars} found={m != null}"); if (!good) fails++; }
// The patched node and the private ports Harmony injects into the prefix (___item, ___yes).
Method("GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet", "TryToAddCorpseToPallet", 1, 1, "FlowCanvas.Flow");
Field("GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet", "item", "FlowCanvas.ValueInput`1<Item>");
Field("GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet", "yes", "FlowCanvas.FlowOutput");
// The donkey's walks: Flow_GoTo.GoTo and the protected fields the prefix takes; the WGO lookup it calls by name.
Method("GK2.FlowCanvasNodes.Flow_GoTo", "GoTo", 1, 1, "FlowCanvas.Flow");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "out", "FlowCanvas.FlowOutput");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "fireEventOnFinish", "FlowCanvas.ValueInput`1<System.String>");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "gdPointData", "FlowCanvas.ValueInput`1<GDPointData>");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "gdPointId", "FlowCanvas.ValueInput`1<System.String>");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "useGdDataInsteadId", "System.Boolean");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "onFinish", "FlowCanvas.FlowOutput");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "navigation", "FlowCanvas.ValueInput`1<MovementType>");
Field("GK2.FlowCanvasNodes.Flow_GoTo", "speed", "FlowCanvas.ValueInput`1<System.Single>");
// The walk out: the node's own StartPath overload (9 params, GDGraph) and the data it needs.
Method("MovementComponent", "StartPath", 9, 7, "UnityEngine.Vector3");
Method("WgoData", "get_MovementComponent", 0);
Method("WgoData", "get_WorldId", 0);
Method("GKCustomFlowNodeWithWgoData", "GetWgoData", 0);
Method("WgoData", "AddDelayedEvent", 2);
Method("QuestSystemData", "IsQuestInStatus", 2);
// The premise for LeaveHere: a walk to the point the WGO already stands on ends at once with only onFinish.
Method("MovementComponent", "TryFinishAlreadyAtDestination", 3);
// The graph's own body drops (fallbacks of the arrival chain) and the script component that names the graph's WGO.
Method("GK2.FlowCanvasNodes.Flow_DropItem", "Drop", 1, 1, "FlowCanvas.Flow");
Field("GK2.FlowCanvasNodes.Flow_DropItem", "out", "FlowCanvas.FlowOutput");
Field("GK2.FlowCanvasNodes.Flow_DropItem", "item", "FlowCanvas.ValueInput`1<Item>");
Field("GK2.FlowCanvasNodes.Flow_DropItem", "gdPointDataDropOn", "FlowCanvas.ValueInput`1<GDPointData>");
Field("GK2.FlowCanvasNodes.Flow_DropItem", "dropBody", "System.Boolean");
Field("WgoDataScript", "wgoData", "WgoData");
Method("WorldData", "GetWgoDataListByGroup", 1);
// The notification: the string overload is patched; the parameterless one forwards to it (2 overloads expected).
Method("UINotificator", "ShowNewBodyNotification", 1, 2, "System.String");
Method("UINotificator", "ShowSimpleTextWithIconNotification", 2);
// What the redirect calls.
Method("GdPointsData", "GetGDPointDataById", 1);
Method("DropSystem", "DropItem", 4);
Method("WgoData", "FireEvent", 1);
Method("WorldData", "GetWgoData", 1, 2, "System.String");
Method("WorldData", "GetWgoDataByCustomTag", 1);
// The vanilla pallet code must still read the port inside its pallet loop (the "read item.value once" note in the
// prefix rests on the port re-evaluating per read): the node's handler references ValueInput`1::get_value.
var handler = game.GetType("GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet")!.Methods.First(x => x.Name == "TryToAddCorpseToPallet");
var closure = game.GetType("GK2.FlowCanvasNodes.Flow_TryToAddCorpseToPallet")!.NestedTypes.SelectMany(n => n.Methods).Concat(handler.DeclaringType.Methods);
bool readsValue = closure.Where(x => x.HasBody).SelectMany(x => x.Body.Instructions).Any(i => i.Operand is MethodReference r && r.Name == "get_value" && r.DeclaringType.Name.StartsWith("ValueInput"));
Console.WriteLine((readsValue ? "PASS" : "FAIL") + " the node still reads item.value (ValueInput`1.get_value)"); if (!readsValue) fails++;

// The framework-integration checks (patch list, Unity message names, GK2.Framework isolation) are in
// tests/FrameworkScan, shared by every integrated mod.

Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
