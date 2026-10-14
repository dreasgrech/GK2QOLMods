// tools/DepManifest: the dependency manifest of a mod DLL (docs/dependency-check.md).
//   embed  <mod.dll> <Managed\> <BepInEx\core\>   compute the manifest from the DLL's IL and embed it as gk2deps.txt
//                                                 (rewrites the DLL in place; unchanged DLL when the manifest is the same)
//   verify <mod.dll> <Managed\>                   compare the embedded manifest with that game: exit 0 identical,
//                                                 2 changed, 1 missing or no manifest
//   show   <mod.dll>                              print the embedded manifest
//   embed-text <mod.dll> <manifest.txt>           tests only: embed this text as the manifest, whatever it says (a hash
//                                                 altered by hand makes a deployed DLL take the changed path in game)
// Identical copies live in GK2QOLMods and GK2DonkeyAPI (tools/DepManifest): change both and keep them byte-identical.
using System.Text;
using GK2Shared;
using Mono.Cecil;

if (args.Length < 2) { Console.Error.WriteLine("usage: embed <mod.dll> <Managed> <BepInExCore> | verify <mod.dll> <Managed> | show <mod.dll> | embed-text <mod.dll> <manifest.txt>"); return 64; }
string dll = Path.GetFullPath(args[1]);
switch (args[0])
{
    case "show":
    {
        using ModuleDefinition mod = ModuleDefinition.ReadModule(dll);
        Console.Write(DepManifest.ReadFrom(mod).Text());
        return 0;
    }
    case "verify":
    {
        string managed = args[2];
        using ModuleDefinition mod = ModuleDefinition.ReadModule(dll);
        DepManifest m = DepManifest.ReadFrom(mod);
        Func<string, ModuleDefinition> game = DepCompare.ModulesIn(managed, out IDisposable handle);
        using (handle)
        {
            Guid gameMvid = game("Assembly-CSharp").Mvid, lbtMvid = game("LazyBearTechnology").Mvid;
            bool recompiled = gameMvid != m.GameMvid || lbtMvid != m.LazyBearMvid;
            DepVerdict v = DepCompare.Compare(m, game);
            Console.WriteLine($"{Path.GetFileName(dll)}: manifest for game {m.GameMvid} / {m.LazyBearMvid}; installed {gameMvid} / {lbtMvid} ({(recompiled ? "recompiled" : "the same build")}); {v.Checked} entries: {v.Kind}");
            foreach (string s in v.Missing) Console.WriteLine("  MISSING " + s);
            foreach (string s in v.Changed) Console.WriteLine("  CHANGED " + s);
            return v.Missing.Count > 0 ? 1 : v.Changed.Count > 0 ? 2 : 0;
        }
    }
    case "embed-text":
    {
        string text = File.ReadAllText(args[2]);
        DepManifest m = DepManifest.Parse(text);
        string pdb = Path.ChangeExtension(dll, ".pdb");
        bool symbols = File.Exists(pdb);
        DefaultAssemblyResolver res = new DefaultAssemblyResolver();
        res.AddSearchDirectory(Path.GetDirectoryName(dll));
        using (ModuleDefinition mod = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = res, ReadSymbols = symbols, ReadWrite = true }))
        {
            EmbeddedResource old = mod.Resources.OfType<EmbeddedResource>().FirstOrDefault(r => r.Name == DepManifest.ResourceName);
            if (old != null) mod.Resources.Remove(old);
            mod.Resources.Add(new EmbeddedResource(DepManifest.ResourceName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(text)));
            mod.Write(new WriterParameters { WriteSymbols = symbols });
            Console.WriteLine($"{Path.GetFileName(dll)}: manifest replaced by {Path.GetFileName(args[2])} ({m.Entries.Count} entries, game {m.GameMvid}); symbols {(symbols ? "kept" : "none")}. TEST BUILD: a normal build restores the real manifest.");
            return 0;
        }
    }
    case "embed":
    {
        string managed = args[2], core = args[3];
        string pdb = Path.ChangeExtension(dll, ".pdb");
        bool symbols = File.Exists(pdb);
        DefaultAssemblyResolver res = new DefaultAssemblyResolver();
        res.AddSearchDirectory(managed); res.AddSearchDirectory(core); res.AddSearchDirectory(Path.GetDirectoryName(dll));
        ReaderParameters rp = new ReaderParameters { AssemblyResolver = res, ReadSymbols = symbols, ReadWrite = true };
        Func<string, ModuleDefinition> game = DepCompare.ModulesIn(managed, out IDisposable handle);
        using (handle)
        using (ModuleDefinition mod = ModuleDefinition.ReadModule(dll, rp))
        {
            List<string> errors = new List<string>();
            DepManifest m = DepBuilder.Build(mod, game, errors);
            if (errors.Count > 0)
            {
                foreach (string e in errors) Console.Error.WriteLine("error: " + e);
                return 1;
            }
            string text = m.Text();
            EmbeddedResource old = mod.Resources.OfType<EmbeddedResource>().FirstOrDefault(r => r.Name == DepManifest.ResourceName);
            if (old != null && Encoding.UTF8.GetString(old.GetResourceData()) == text)
            {
                Console.WriteLine($"{Path.GetFileName(dll)}: manifest unchanged ({m.Entries.Count} entries, game {m.GameMvid})");
                return 0;
            }
            if (old != null) mod.Resources.Remove(old);
            mod.Resources.Add(new EmbeddedResource(DepManifest.ResourceName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(text)));
            Guid mvidBefore = mod.Mvid;
            mod.Write(new WriterParameters { WriteSymbols = symbols });
            Console.WriteLine($"{Path.GetFileName(dll)}: manifest embedded ({m.Entries.Count} entries: {m.Entries.Count(e => e.Kind == 'T')} types, {m.Entries.Count(e => e.Kind == 'M')} methods, {m.Entries.Count(e => e.Kind == 'F')} fields; game {m.GameMvid}; symbols {(symbols ? "kept" : "none")}; module id {(mod.Mvid == mvidBefore ? "kept" : "CHANGED")})");
            return 0;
        }
    }
}
Console.Error.WriteLine("unknown mode " + args[0]);
return 64;
