using System; using System.Linq; using Mono.Cecil;
var dir = @"C:\Games\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed\";
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(dir);
var mod = ModuleDefinition.ReadModule(dir+"Assembly-CSharp.dll", new ReaderParameters{AssemblyResolver=res});
Console.WriteLine("MVID "+mod.Mvid);
var w = mod.GetType("UIBuildingWidget");
Console.WriteLine("base: "+w.BaseType.FullName);
foreach (var m in w.Methods.Where(m=>m.Name=="Redraw")) Console.WriteLine($"Redraw virtual={m.IsVirtual} abstract={m.IsAbstract} params={m.Parameters.Count} body={m.HasBody}");
foreach (var f in w.Fields.Where(f=>f.Name=="nameLabel")) Console.WriteLine($"nameLabel: {f.FieldType.FullName} in {f.FieldType.Scope}");
var bt = w.BaseType.Resolve(); 
for (var t = bt; t != null; t = t.BaseType?.Resolve()) { Console.WriteLine("type "+t.FullName+" ("+t.Module.Name+")"); foreach (var f in t.Fields) Console.WriteLine($"   field {f.Name}: {f.FieldType.FullName} {(f.IsPublic?"public":f.IsFamily?"protected":"private")}"); foreach(var p in t.Properties) Console.WriteLine($"   prop {p.Name}: {p.PropertyType.FullName}"); if (t.FullName.StartsWith("UnityEngine")) break; }
var d = mod.GetType("UIBuildingWidgetData"); foreach (var p in d.Properties) Console.WriteLine($"data prop {p.Name}: {p.PropertyType.FullName} getPublic={p.GetMethod?.IsPublic}");
var wz = mod.GetType("WorldZoneData"); foreach (var f in wz.Fields.Where(f=>f.Name=="wgoDataList")) Console.WriteLine($"wgoDataList {f.FieldType.FullName} public={f.IsPublic}");
var wd = mod.GetType("WgoData"); foreach (var f in wd.Fields.Where(f=>f.Name=="isTempObject"||f.Name=="id")) Console.WriteLine($"WgoData.{f.Name} {f.FieldType.FullName} public={f.IsPublic}");
foreach (var p in wd.Properties.Where(p=>p.Name=="Definition"||p.Name=="isTempObject")) Console.WriteLine($"WgoData prop {p.Name} {p.PropertyType.FullName}");
for (var t = wd; t!=null; t=t.BaseType?.Resolve()) { foreach (var f in t.Fields.Where(f=>f.Name=="id"||f.Name=="isTempObject")) Console.WriteLine($"  {t.Name}.{f.Name} {f.FieldType.FullName} public={f.IsPublic}"); foreach (var p in t.Properties.Where(p=>p.Name=="Definition")) Console.WriteLine($"  {t.Name}.Definition {p.PropertyType.FullName}"); if (t.FullName=="System.Object") break; }
var def = mod.GetType("WGODef"); foreach (var f in def.Fields.Where(f=>f.Name=="replaceToWgoOnDie")) Console.WriteLine($"replaceToWgoOnDie {f.FieldType.FullName}");
var gs = mod.GetType("GameSave"); foreach (var f in gs.Fields.Where(f=>f.Name.ToLower()=="worlddata")) Console.WriteLine($"GameSave.{f.Name} {f.FieldType.FullName} public={f.IsPublic}"); foreach (var p in gs.Properties.Where(p=>p.Name.ToLower()=="worlddata")) Console.WriteLine($"GameSave prop {p.Name}");
