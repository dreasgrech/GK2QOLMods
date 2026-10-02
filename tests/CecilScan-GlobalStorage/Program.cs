using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
var mod = ModuleDefinition.ReadModule(GamePaths.Managed + "Assembly-CSharp.dll");
var interesting = new[]{"HasItemQuantityInInventory","GetTotalCountInInventory","RemoveItemById","GetCraftableMultiInventory","IsEnoughRes"};
void Dump(string typeName, bool nested=true) {
  var t = mod.GetType(typeName); var types = new[]{t}.Concat(nested ? t.NestedTypes : Enumerable.Empty<TypeDefinition>());
  foreach (var ty in types) foreach (var m in ty.Methods.Where(m=>m.HasBody)) {
    var hits = m.Body.Instructions.Where(i=>(i.OpCode==OpCodes.Call||i.OpCode==OpCodes.Callvirt) && i.Operand is MethodReference r && interesting.Contains(r.Name))
      .Select(i=>{var r=(MethodReference)i.Operand; return $"{i.OpCode} {r.DeclaringType.Name}.{r.Name}({string.Join(",",r.Parameters.Select(p=>p.ParameterType.Name))})";}).ToList();
    if (hits.Count>0) Console.WriteLine($"{ty.Name}::{m.Name} IL={m.Body.CodeSize}\n    "+string.Join("\n    ",hits));
  }
}
Console.WriteLine("=== base-call re-check (previous tool was vacuous)");
Dump("ZombieWgoData", false); Dump("ConveyorWgoData", false);
Console.WriteLine("(no GetCraftableMultiInventory call listed above = overrides never call base)");
Console.WriteLine("=== zombie unequip (since game 1.008 one private method for armour, tool and weapon)");
{ var m = mod.GetType("ZombieEquipmentInventoryWidgetData").Methods.FirstOrDefault(x=>x.Name=="UnequipToPlayer" && x.HasBody);
  var refs = m?.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToList() ?? new();
  bool ctor = refs.Any(r=>r.DeclaringType.Name=="MultiInventory" && r.Name==".ctor" && r.Parameters.Count>=1 && r.Parameters[0].ParameterType.Name=="PlayerData");
  bool add = refs.Any(r=>r.DeclaringType.Name=="MultiInventory" && r.Name=="AddItem");
  Console.WriteLine($"{(m!=null&&ctor&&add?"PASS":"FAIL")} ZombieEquipmentInventoryWidgetData.UnequipToPlayer gives back through new MultiInventory(PlayerData).AddItem (found={m!=null} ctor={ctor} add={add})"); }
Console.WriteLine("=== quest/dialogue item call sites");
Dump("UIMultiAnswerOption"); Dump("UIMultiAnswerIcon"); Dump("UIQuestInfoWindow"); Dump("QuestFinishCheck");
