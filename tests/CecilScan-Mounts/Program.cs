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
// Cargo proof of concept (Cargo.cs, SaveSystem_Save_Patch)
Method("SaveSystem", "Save", 6);
Method("Inventory", "RemoveItemFromInventoryByUID", 2);
Method("SpecialPhysicsCastUtils", "GetPlayerDropPosition", 3);
Method("DropSystem", "DropItem", 4);
Method("DropViewAtomMesh", "Activate", 1);
Method("PlayerData", "get_HasFreeOverheadSlot", 0);
Method("PlayerData", "TryGetOverheadItem", 2);
Method("PlayerData", "AddOverheadItem", 1);
// Setting a cart item down with the interact press (PlayerInputHandler_UpdateInput_Patch).
Method("PlayerInputHandler", "UpdateInput", 0);
Method("PlayerInputHandler", "TryCallFireEvent", 1);
Field("PlayerInputHandler", "playerController", "PlayerController");
Field("PlayerInputHandler", "curInteractionHandler", "IWGOInteractionHandler");
Method("PlayerInteractionComponent", "get_BigDropUnderInteraction", 0);
Method("PlayerInteractionComponent", "get_WgoUnderInteraction", 0);
Method("DropView", "get_InteractionHandler", 0);
Method("Wgo", "get_InteractionHandler", 0);
{ var it = game.GetType("PlayerInputHandler")?.NestedTypes.FirstOrDefault(n => n.Name == "InteractionType"); var f = it?.Fields.FirstOrDefault(x => x.Name == "Interaction1"); bool good = f != null && System.Convert.ToInt32(f.Constant) == 0; Console.WriteLine((good?"PASS":"FAIL") + " enum PlayerInputHandler.InteractionType.Interaction1 == 0"); if (!good) fails++; }
{ var td = game.GetType("PlayerData"); var ms = td?.Methods.Where(x => x.Name == "RemoveOverheadItem").ToList(); bool good = ms != null && ms.Count == 2 && ms.Any(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "Item" && m.HasBody); Console.WriteLine((good?"PASS":"FAIL") + $" method PlayerData.RemoveOverheadItem overloads={ms?.Count} (need 0 and Item)"); if (!good) fails++; }
Method("LazyExpressionEvaluationScope", "GetOverheadItemForMutation", 0);
Method("Inventory", "Create", 5);
{ var td = game.GetType("Inventory"); var ms = td?.Methods.Where(x => x.Name == "AddItemToInventory").ToList(); bool good = ms != null && ms.Count == 2 && ms.Any(m => m.Parameters.Count == 3 && m.HasBody); Console.WriteLine((good?"PASS":"FAIL") + $" method Inventory.AddItemToInventory overloads={ms?.Count} (need the 3-param one)"); if (!good) fails++; }
Method("Item", "set_Count", 1);
Method("FlowScriptAssetLoadPolicy", "Load", 1);
Method("FlowScriptAssetLoadPolicy", "ReleaseGraph", 1);
Field("QuestCheck", "triggerType", "GlobalEventsSystem/Event/Type");
Field("QuestCheck", "condExpressions", "System.Collections.Generic.List`1<LazyExpression>");
Field("GameBalance", "questDefs", "System.Collections.Generic.List`1<QuestDef>");
Field("WGODef", "attachedScript", "System.String");
// Round 11 (save-safety audit): definition fields the mount refuses on, the teleport control flag, the sheet abort.
Field("WGODef", "townQuality", "System.Int32");
Field("WGODef", "npcLifeSimGroup", "System.String");
Field("WGODef", "startItems", "System.Collections.Generic.List`1<NeedItemData>");
Method("PlayerController", "get_IsControlsEnabled", 0);
Method("Inventory", "Clear", 0);
Method("PlayerData", "AddOverheadItem", 1);
Method("WeatherSystem", "get_WindValue", 0);
Method("WeatherSystem", "get_Instance", 0);
Field("GlobalShaderParameters", "idSunLight", "System.Int32", true);
Method("PlayerData", "PlayOverheadItemTakeSound", 1);
Method("PlayerData", "RemoveInteractingItem", 0);
Method("PlayerData", "get_HasInteractingItem", 0);
Method("GlobalEventsSystem", "FireTrigger", 2);
Method("PlayerData", "get_OverheadStackLimit", 0);
Method("PlayerData", "get_OverheadCount", 0);
{ var td = game.GetType("PlayerData"); var ms = td?.Methods.Where(x => x.Name == "DropOverheadItem").ToList(); bool good = ms != null && ms.Count == 2 && ms.Any(m => m.Parameters.Count == 0 && m.HasBody); Console.WriteLine((good?"PASS":"FAIL") + $" method PlayerData.DropOverheadItem overloads={ms?.Count} (need 0 and Item)"); if (!good) fails++; }
{ var td = game.GetType("PlayerData"); var ms = td?.Methods.Where(x => x.Name == "InsertOverheadItemTo").ToList(); bool good = ms != null && ms.Count == 2 && ms.Any(m => m.Parameters.Count == 2 && m.HasBody); Console.WriteLine((good?"PASS":"FAIL") + $" method PlayerData.InsertOverheadItemTo overloads={ms?.Count} (need WgoData and WgoData,Item)"); if (!good) fails++; }
// Inventory.CanAddItemToInventory and PlayerData.InsertOverheadItemTo have two overloads each; the IL scan above covers them.
{ var pa = game.GetType("PlayerAnimation"); bool good = pa?.BaseType?.Name == "AnimationComponent" && game.GetType("AnimationComponent")?.BaseType?.Name == "AnimationComponentBase"; Console.WriteLine((good?"PASS":"FAIL") + " PlayerAnimation : AnimationComponent : AnimationComponentBase"); if (!good) fails++; }
Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
