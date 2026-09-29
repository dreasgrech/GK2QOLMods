using Mono.Cecil; using Mono.Cecil.Cil;
var managed = GamePaths.Managed;
var res = new DefaultAssemblyResolver(); res.AddSearchDirectory(managed); res.AddSearchDirectory(GamePaths.BepInExCore);
var rp = new ReaderParameters { AssemblyResolver = res };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")); // tests/X/bin/Release/net8.0 -> repo root
var mod = ModuleDefinition.ReadModule(Path.Combine(repo, "src", "GK2OwnedCount", "bin", "Release", "netstandard2.1", "GK2OwnedCount.dll"), rp);
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
Field("UITooltip", "instance", "UITooltip", isStatic: true);
Field("UITooltip", "smallDescriptionTextStyle", "LazyBearTechnology.TextStyle");
Method("UITooltip", "AddItemWidgets", 7);

// Patches are applied class by class from Plugin.PatchClasses (not PatchAll(assembly)): every [HarmonyPatch] class
// must be listed there, or it would silently never be applied.
var pluginType = mod.GetType("GK2OwnedCount.Plugin");
var listed = pluginType.Methods.First(m => m.Name == ".cctor").Body.Instructions
  .Where(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference).Select(i => ((TypeReference)i.Operand).FullName).ToHashSet();
foreach (var t in mod.GetTypes().Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))) {
  bool good = listed.Contains(t.FullName); Console.WriteLine((good?"PASS":"FAIL") + $" patch class {t.FullName} in Plugin.PatchClasses"); if (!good) fails++; }

// Unity calls these by name on every MonoBehaviour; Plugin must only have the ones it means (Awake, Update,
// OnDestroy). A helper once named Start ran a second time on the first frame and patched everything twice.
foreach (var name in new[] { "Start", "OnEnable", "OnDisable", "FixedUpdate", "LateUpdate", "OnGUI", "OnApplicationQuit" }) {
  bool good = !pluginType.Methods.Any(m => m.Name == name); if (!good) { Console.WriteLine($"FAIL Plugin has a method named {name} (Unity would call it)"); fails++; } }
Console.WriteLine("PASS Plugin has no unintended Unity message methods (Start, OnEnable, ...)");

// Optional GK2 Mod Framework: only FrameworkLink.Register and the nested mod class may touch GK2.Framework types,
// so the DLL loads and runs without GK2.Framework.dll. Everything else is checked: base types, interfaces,
// attributes, fields, method signatures, locals and every IL operand.
bool Fw(TypeReference? r) {
  if (r == null) return false;
  if (r is GenericInstanceType g && g.GenericArguments.Any(Fw)) return true;
  if (r is TypeSpecification ts) return Fw(ts.ElementType);
  return r.Scope?.Name == "GK2.Framework";
}
bool FwMember(object? o) => o switch {
  TypeReference tr => Fw(tr),
  MethodReference mr => Fw(mr.DeclaringType) || Fw(mr.ReturnType) || mr.Parameters.Any(p => Fw(p.ParameterType))
    || (mr is GenericInstanceMethod gm && gm.GenericArguments.Any(Fw)),
  FieldReference fr => Fw(fr.DeclaringType) || Fw(fr.FieldType),
  _ => false };
int fwUses = 0;
foreach (var t in mod.GetTypes()) {
  bool linkMod = t.DeclaringType?.FullName == "GK2OwnedCount.FrameworkLink"; // the nested mod class: allowed
  if (linkMod) continue;
  void Bad(string what) { Console.WriteLine($"FAIL {t.FullName} uses GK2.Framework: {what}"); fails++; }
  if (Fw(t.BaseType)) Bad("base type");
  foreach (var i in t.Interfaces) if (Fw(i.InterfaceType)) Bad("interface");
  foreach (var a in t.CustomAttributes) if (Fw(a.AttributeType)) Bad("attribute");
  foreach (var f in t.Fields) if (Fw(f.FieldType)) Bad("field " + f.Name);
  foreach (var m in t.Methods) {
    bool allowed = t.FullName == "GK2OwnedCount.FrameworkLink" && m.Name == "Register";
    bool uses = Fw(m.ReturnType) || m.Parameters.Any(p => Fw(p.ParameterType))
      || (m.HasBody && (m.Body.Variables.Any(v => Fw(v.VariableType)) || m.Body.Instructions.Any(i => FwMember(i.Operand))));
    if (uses && allowed) fwUses++;
    else if (uses) Bad("method " + m.Name);
  }
}
{ bool good = fwUses == 1; Console.WriteLine((good?"PASS":"FAIL") + $" GK2.Framework used only by FrameworkLink.Register (and its mod class); Register uses it: {fwUses == 1}"); if (!good) fails++; }
var register = mod.GetType("GK2OwnedCount.FrameworkLink").Methods.First(m => m.Name == "Register");
{ bool good = register.ImplAttributes.HasFlag(MethodImplAttributes.NoInlining); Console.WriteLine((good?"PASS":"FAIL") + " FrameworkLink.Register is NoInlining"); if (!good) fails++; }
{ bool good = pluginType.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency" && (string)a.ConstructorArguments[0].Value == "ru.superman4eg.gk2.framework" && Convert.ToInt32(a.ConstructorArguments[1].Value) == 2);
  Console.WriteLine((good?"PASS":"FAIL") + " soft BepInDependency on ru.superman4eg.gk2.framework"); if (!good) fails++; }
Console.WriteLine(fails == 0 ? "ALL PASS" : "FAILURES: " + fails);
return fails;
