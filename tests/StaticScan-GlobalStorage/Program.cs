using System; using System.IO; using System.Linq; using System.Reflection;
var managed = @"C:\Games\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed";
var paths = Directory.GetFiles(managed, "*.dll");
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "mscorlib");
var asm = mlc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
Console.WriteLine($"MVID {asm.ManifestModule.ModuleVersionId}");
const BindingFlags all = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
Type T(string n) => asm.GetType(n, true)!;
void Check(string label, MethodBase[] matches, string[] needParams) {
  if (matches.Length != 1) { Console.WriteLine($"FAIL {label}: {matches.Length} matches"); return; }
  var m = matches[0]; var ps = m.GetParameters();
  var names = ps.Select(p => p.Name).ToArray();
  var missing = needParams.Where(n => !names.Contains(n)).ToArray();
  int il = m.GetMethodBody()?.GetILAsByteArray()?.Length ?? -1;
  Console.WriteLine($"{(missing.Length==0?"OK  ":"FAIL")} {label}  params=({string.Join(", ", ps.Select(p=>p.ParameterType.Name+" "+p.Name))})  IL={il} bytes  virtual={m.IsVirtual}{(missing.Length>0?"  MISSING "+string.Join(",",missing):"")}");
}
var wgo = T("WgoData"); var mi = T("MultiInventory");
Check("WgoData.GetCraftableMultiInventory", wgo.GetMethods(all).Where(m=>m.Name=="GetCraftableMultiInventory").ToArray(), new[]{"excludeWorkerInventory"});
Check("MultiInventory(PlayerData,bool)", mi.GetConstructors(all).Where(c=>{var p=c.GetParameters();return p.Length==2&&p[0].ParameterType.Name=="PlayerData"&&p[1].ParameterType.Name=="Boolean";}).ToArray(), new[]{"playerData","addCurrentPlayerWorldZone"});
Check("MultiInventory.Sort", mi.GetMethods(all).Where(m=>m.Name=="Sort").ToArray(), new string[0]);
Check("UIMultiInventoryWindowData ctor", T("UIMultiInventoryWindowData").GetConstructors(all).Where(c=>c.GetParameters().Length==6).ToArray(), new[]{"itemsAvailableCondition"});
var ze = T("ZombieEquipmentInventoryWidgetData");
Check("ZombieEquip.OnArmorCellPressed2", ze.GetMethods(all).Where(m=>m.Name=="OnArmorCellPressed2").ToArray(), new string[0]);
Check("ZombieEquip.OnInstrumentCellPressed2", ze.GetMethods(all).Where(m=>m.Name=="OnInstrumentCellPressed2").ToArray(), new string[0]);
Check("PlayerOrderExecutor()", T("PlayerOrderExecutor").GetConstructors(all).Where(c=>c.GetParameters().Length==0).ToArray(), new string[0]);
// Fields/properties the plugin reads
foreach (var (t,n) in new[]{("WgoData","IsHidden"),("WgoData","IsInteractable"),("WgoData","Inventory"),("WgoData","Worker"),("WorldZoneData","IsContainer"),("WorldZoneData","IsActive"),("Item","Inventory"),("Item","IsEmpty"),("Item","InventorySize")})
  Console.WriteLine($"{(T(t).GetProperty(n, all)!=null?"OK  ":"FAIL")} property {t}.{n}");
foreach (var (t,n) in new[]{("MultiInventory","inventoryList"),("WorldZoneData","wgoDataList"),("GameSceneData","worldZones"),("WorldData","gameSceneDataList"),("WGODef","inventorySize"),("WGODef","conveyorType")})
  Console.WriteLine($"{(T(t).GetField(n, all)!=null?"OK  ":"FAIL")} field {t}.{n}");
// Which overrides of GetCraftableMultiInventory call base? (would make the base patch reach them)
foreach (var sub in new[]{"ZombieWgoData","ConveyorWgoData"}) {
  var m = T(sub).GetMethods(all).First(x=>x.Name=="GetCraftableMultiInventory");
  var il = m.GetMethodBody()!.GetILAsByteArray()!; bool callsBase=false;
  for (int i=0;i<il.Length-4;i++) if (il[i]==0x28) { try { var target = m.Module.ResolveMethod(BitConverter.ToInt32(il,i+1)); if (target?.DeclaringType?.Name=="WgoData" && target.Name=="GetCraftableMultiInventory") callsBase=true; } catch {} }
  Console.WriteLine($"{(callsBase?"WARN":"OK  ")} {sub}.GetCraftableMultiInventory calls base: {callsBase}");
}
