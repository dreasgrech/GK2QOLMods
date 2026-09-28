using Mono.Cecil; using Mono.Cecil.Cil;
var managed = @"C:\Games\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed\";
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(managed); res.AddSearchDirectory(@"C:\Games\Graveyard Keeper 2\BepInEx\core\");
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", "GK2Mounts", "bin", "Release", "netstandard2.1", "GK2Mounts.dll"), rp);
var game = ModuleDefinition.ReadModule(managed + "Assembly-CSharp.dll", rp);
Console.WriteLine("Game MVID " + game.Mvid);
int fails = 0, ok = 0;
var gameScopes = new HashSet<string> { "Assembly-CSharp", "LazyBearTechnology" };
var seen = new HashSet<string>();
foreach (var t in mod.GetTypes()) foreach (var m in t.Methods.Where(m => m.HasBody)) foreach (var ins in m.Body.Instructions) {
  if (ins.Operand is MemberReference mr && !(mr is TypeReference) && mr.DeclaringType != null && !(mr.DeclaringType is ArrayType) && gameScopes.Contains(mr.DeclaringType.Scope.Name)) {
    if (!seen.Add(mr.FullName)) continue;
    IMemberDefinition? d = null;
    try { d = mr is MethodReference meth ? meth.Resolve() : mr is FieldReference f ? f.Resolve() : null; } catch {}
    if (d == null) { Console.WriteLine("FAIL unresolved: " + mr.FullName); fails++; } else ok++;
  }
}
Console.WriteLine($"IL references into game assemblies: {ok} resolved, {fails} unresolved");
void Field(string type, string name, string ftype, bool isStatic=false) { var td = game.GetType(type); var f = td?.Fields.FirstOrDefault(x => x.Name == name);
  bool good = f != null && f.FieldType.FullName == ftype && f.IsStatic == isStatic; Console.WriteLine((good?"PASS":"FAIL") + $" field {type}.{name} : {f?.FieldType.FullName} static={f?.IsStatic}"); if (!good) fails++; }
void Method(string type, string name, int pars) { var td = game.GetType(type); var ms = td?.Methods.Where(x => x.Name == name).ToList();
  bool good = ms != null && ms.Count == 1 && ms[0].Parameters.Count == pars && ms[0].HasBody; Console.WriteLine((good?"PASS":"FAIL") + $" method {type}.{name} overloads={ms?.Count} params={ms?.FirstOrDefault()?.Parameters.Count}"); if (!good) fails++; }
Method("WgoDataScriptsManager", "CreateScript", 2);
Method("Wgo", "Spawn", 6);
Method("Wgo", "UpdateChunkVisibility", 1);
Method("Wgo", "SetInteractableCollidersState", 1);
Method("Wgo", "SetLayerToAllColliders", 1);
Method("WgoData", "SetStateToAnimator", 1);
Method("AnimationComponentBase", "SetWalkAnimationSpeedMultiplier", 1);
{ var td = game.GetType("AnimationComponentBase"); var m = td?.Methods.FirstOrDefault(x => x.Name == "SetDirection" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.Name == "Vector2"); bool good = m != null && m.HasBody && !m.IsVirtual; Console.WriteLine((good?"PASS":"FAIL") + " method AnimationComponentBase.SetDirection(Vector2) found=" + (m != null)); if (!good) fails++; }
Method("GK2.FlowCanvasNodes.Flow_Body", "DoAction", 1);
Field("GK2.FlowCanvasNodes.Flow_Body", "success", "FlowCanvas.FlowOutput");
// Cart cargo investigation (CarryProbe.cs): the delivery script's own inventory path and the drawer that reacts to it.
Field("WGODef", "inventorySize", "System.Int32");
Field("ItemDef", "itemSize", "ItemSize");
Field("ItemDef", "iconId", "System.String");
Field("GameBalance", "itemDefs", "System.Collections.Generic.List`1<ItemDef>");
Method("Inventory", "RemoveItemById", 5);
Method("PlayerData", "TryAddOverheadItemNoReplace", 1);
Method("ConditionalDrawer", "UpdateVisualsByConditions", 0);
// Inventory.CanAddItemToInventory and PlayerData.InsertOverheadItemTo have two overloads each; the IL scan above covers them.
{ var pa = game.GetType("PlayerAnimation"); bool good = pa?.BaseType?.Name == "AnimationComponent" && game.GetType("AnimationComponent")?.BaseType?.Name == "AnimationComponentBase"; Console.WriteLine((good?"PASS":"FAIL") + " PlayerAnimation : AnimationComponent : AnimationComponentBase"); if (!good) fails++; }
Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
