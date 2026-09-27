using Mono.Cecil; using Mono.Cecil.Cil;
var managed = @"C:\Games\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed\";
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(managed); res.AddSearchDirectory(@"C:\Games\Graveyard Keeper 2\BepInEx\core\");
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", "GK2MoveBuilding", "bin", "Release", "netstandard2.1", "GK2MoveBuilding.dll"), rp);
var game = ModuleDefinition.ReadModule(managed + "Assembly-CSharp.dll", rp);
Console.WriteLine("Game MVID " + game.Mvid);
int fails = 0, ok = 0;
var gameScopes = new HashSet<string> { "Assembly-CSharp", "LazyBearTechnology" };
var seen = new HashSet<string>();
foreach (var t in mod.GetTypes()) foreach (var m in t.Methods.Where(m => m.HasBody)) foreach (var ins in m.Body.Instructions) {
  if (ins.Operand is MemberReference mr && mr.DeclaringType != null && !(mr.DeclaringType is ArrayType) && gameScopes.Contains(mr.DeclaringType.Scope.Name)) {
    if (!seen.Add(mr.FullName)) continue;
    IMemberDefinition? d = null;
    try { d = mr is MethodReference meth ? meth.Resolve() : mr is FieldReference f ? f.Resolve() : null; } catch {}
    if (d == null) { Console.WriteLine("FAIL unresolved: " + mr.FullName); fails++; } else ok++;
  }
}
Console.WriteLine($"IL references into game assemblies: {ok} resolved, {fails} unresolved");
// by-name reflection targets
void Field(string type, string name, string ftype, bool isStatic=false) { var td = game.GetType(type); var f = td?.Fields.FirstOrDefault(x => x.Name == name);
  bool good = f != null && f.FieldType.FullName == ftype && f.IsStatic == isStatic; Console.WriteLine((good?"PASS":"FAIL") + $" field {type}.{name} : {f?.FieldType.FullName} static={f?.IsStatic}"); if (!good) fails++; }
void Method(string type, string name, int pars) { var td = game.GetType(type); var ms = td?.Methods.Where(x => x.Name == name).ToList();
  bool good = ms != null && ms.Count == 1 && ms[0].Parameters.Count == pars; Console.WriteLine((good?"PASS":"FAIL") + $" method {type}.{name} overloads={ms?.Count} params={ms?.FirstOrDefault()?.Parameters.Count}"); if (!good) fails++; }
Field("BuildManager", "currentBuildDesk", "Wgo");
Field("BuildManager", "additionalInventories", "System.Collections.Generic.List`1<Inventory>");
Field("BuildController", "buildPointer", "BuildPointer");
Field("BuildController", "isBuildModeInputLocked", "System.Boolean");
Field("WgoBuildPointer", "canTakeResources", "System.Func`1<System.Boolean>");
Field("BuildPointerObject", "shownAsActive", "System.Boolean");
Field("RemovePointer", "currentRemovingSelection", "IBuildRemovable", isStatic: true);
Method("Wgo", "TryRegisterInChunkManager", 0);
Method("Wgo", "TryUnregisterInChunkManager", 0);
Method("UIBuildingWidgetData", "set_Name", 1);
Method("Wgo", "RegisterGDPointsFromBakedData", 0);
Method("Wgo", "TryRegisterWorkbenchExtensionDelayed", 1);
Method("ConditionalDrawer", "OnParentCraftStatusChanged", 1);
// patch targets: unique, non-abstract, with a body
Method("UIBuildingWindowData", ".ctor", 6);
Method("UIBuildingWidgetData", ".ctor", 7);
Method("BuildManager", "EnableBuildMode", 4);
Method("RemovePointer", "TryDoBuildAction", 0);
Method("BuildPointer", "TryBuildActionInput", 0);
Method("BuildSelectionCell", "OverlapBoxNonAlloc", 2);
Method("WgoBuildPointer", "SetTarget", 5);
Field("BuildCellData", "state", "System.Int32");
Method("RemovePointer", "UpdateSelectionRect", 0);
Method("RemovePointer", "OnPointerDisable", 0);
Method("BuildGrid3D", "Draw", 5);
Method("Wgo", "SetSelectionTint", 2);
Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
