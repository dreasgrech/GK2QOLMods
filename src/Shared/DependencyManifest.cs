// The dependency manifest: which game members a mod uses, with a hash of each, and the comparison of that list with
// an installed game. One source file, compiled into the mods (at start, against the running game), into
// tools/DepManifest (writes the manifest after a build, verifies a dist DLL offline) and into the test harness, so
// every side hashes the same way by construction. Mono.Cecil 0.10.4 from BepInEx/core; APIs limited to what
// netstandard2.1, net48 and net8 all have. Design: docs/dependency-check.md.
// Identical copies live in GK2QOLMods and GK2DonkeyAPI (src/Shared): change both and keep them byte-identical.
#nullable disable // linked into projects with and without nullable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GK2Shared
{
    // One line of the manifest. Kind 'T': a type that must exist (Hash empty); 'M' a method, 'F' a field, each with
    // the hash its body/shape had in the audited game. Scope: the assembly's name (Assembly-CSharp, LazyBearTechnology).
    // UsedBy: the mod's own top-level types that use the member, comma-separated, after a tab (the Donkey API decides
    // per feature what a changed member means; the QOL mods ignore the column).
    internal struct DepEntry
    {
        public char Kind;
        public string Scope;
        public string Key;   // Cecil FullName of the type, method or field
        public string Hash;
        public string UsedBy;

        public override string ToString() => Kind + " " + Scope + " " + (Hash.Length == 0 ? "-" : Hash) + " " + Key + (string.IsNullOrEmpty(UsedBy) ? "" : "	" + UsedBy);
    }

    internal sealed class DepManifest
    {
        public const string ResourceName = "gk2deps.txt";
        public static readonly string[] GameScopes = { "Assembly-CSharp", "LazyBearTechnology" };

        public Guid GameMvid;
        public Guid LazyBearMvid;
        public readonly List<DepEntry> Entries = new List<DepEntry>();

        public string Text()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("game-mvid ").Append(GameMvid.ToString()).Append('\n');
            sb.Append("lazybear-mvid ").Append(LazyBearMvid.ToString()).Append('\n');
            int[] order = new int[Entries.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }
            Array.Sort(order, (a, b) =>
            {
                DepEntry x = Entries[a], y = Entries[b];
                int c = x.Kind.CompareTo(y.Kind);
                if (c == 0)
                {
                    c = StringComparer.Ordinal.Compare(x.Scope, y.Scope);
                }
                if (c == 0)
                {
                    c = StringComparer.Ordinal.Compare(x.Key, y.Key);
                }
                return c != 0 ? c : a.CompareTo(b);
            });
            foreach (int i in order)
            {
                sb.Append(Entries[i].ToString()).Append('\n');
            }
            return sb.ToString();
        }

        // SHA-1 of the text: names the manifest in the mods' cfg cache.
        public string Sha1() => DepHash.Sha1Hex(Text());

        public static DepManifest Parse(string text)
        {
            DepManifest m = new DepManifest();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.StartsWith("game-mvid ", StringComparison.Ordinal))
                {
                    m.GameMvid = new Guid(line.Substring(10).Trim());
                    continue;
                }
                if (line.StartsWith("lazybear-mvid ", StringComparison.Ordinal))
                {
                    m.LazyBearMvid = new Guid(line.Substring(14).Trim());
                    continue;
                }
                // "<kind> <scope> <hash|-> <key with spaces>[<tab><used by>]"
                string usedBy = "";
                int tab = line.IndexOf('	');
                if (tab >= 0)
                {
                    usedBy = line.Substring(tab + 1);
                    line = line.Substring(0, tab);
                }
                int s1 = line.IndexOf(' ');
                int s2 = line.IndexOf(' ', s1 + 1);
                int s3 = line.IndexOf(' ', s2 + 1);
                if (s1 != 1 || s2 < 0 || s3 < 0)
                {
                    throw new FormatException("Bad manifest line: " + line);
                }
                string hash = line.Substring(s2 + 1, s3 - s2 - 1);
                m.Entries.Add(new DepEntry
                {
                    Kind = line[0],
                    Scope = line.Substring(2, s2 - 2),
                    Hash = hash == "-" ? "" : hash,
                    Key = line.Substring(s3 + 1),
                    UsedBy = usedBy
                });
            }
            if (m.GameMvid == Guid.Empty || m.LazyBearMvid == Guid.Empty)
            {
                throw new FormatException("The manifest names no game build.");
            }
            return m;
        }

        // The manifest embedded in a loaded assembly (no Cecil needed: the mods' fast path reads only the MVIDs).
        public static DepManifest ReadFrom(System.Reflection.Assembly assembly)
        {
            using (Stream s = assembly.GetManifestResourceStream(ResourceName))
            {
                if (s == null)
                {
                    throw new FileNotFoundException("No " + ResourceName + " resource in " + assembly.GetName().Name + ": the DLL was built without tools/DepManifest.");
                }
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                {
                    return Parse(r.ReadToEnd());
                }
            }
        }

        // The manifest embedded in a module read with Cecil (the tool's verify/show, the harness).
        public static DepManifest ReadFrom(ModuleDefinition module)
        {
            EmbeddedResource res = null;
            foreach (Resource r in module.Resources)
            {
                if (r is EmbeddedResource er && er.Name == ResourceName)
                {
                    res = er;
                    break;
                }
            }
            if (res == null)
            {
                throw new FileNotFoundException("No " + ResourceName + " resource in " + module.Name + ": the DLL was built without tools/DepManifest.");
            }
            return Parse(Encoding.UTF8.GetString(res.GetResourceData()));
        }
    }

    internal static class DepHash
    {
        // Compiler-generated names carry numbers that shift when members are added to a type: <Foo>b__12_0 ->
        // <Foo>b__13_0, <>c__DisplayClass12_0, <>9__12_0, local functions <Foo>g__Bar|12_0. Normalised, a method whose
        // only change is such a renumbering hashes the same (tests/GameDiff hashes the same way).
        private static readonly Regex Lambda = new Regex(@"(<[^<>]*>[a-zA-Z]__)\d+(_\d+)?", RegexOptions.Compiled);
        private static readonly Regex DisplayClass = new Regex(@"<>c__DisplayClass\d+_\d+", RegexOptions.Compiled);
        private static readonly Regex Cached = new Regex(@"<>9__\d+_\d+", RegexOptions.Compiled);
        private static readonly Regex LocalFunction = new Regex(@"\|\d+_\d+", RegexOptions.Compiled);

        public static string Norm(string s)
        {
            s = Lambda.Replace(s, "$1N");
            s = DisplayClass.Replace(s, "<>c__DisplayClassN");
            s = Cached.Replace(s, "<>9__N");
            s = LocalFunction.Replace(s, "|N");
            return s;
        }

        // The method's attributes and its IL with operands resolved to names (not metadata tokens, which shift
        // between builds), branch targets as offsets, locals and parameters as indices. Nothing here may depend on
        // the runtime doing the hashing (net8 in the tool, .NET Framework in the harness, Unity's Mono in the game):
        // attributes as their number, not Enum.ToString (flag names come out in a different order per runtime),
        // floating-point operands as their bits, not as text.
        public static string Method(MethodDefinition me)
        {
            StringBuilder body = new StringBuilder(((int)me.Attributes).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (me.HasBody)
            {
                foreach (Instruction i in me.Body.Instructions)
                {
                    body.Append(i.OpCode.Name).Append(' ');
                    body.Append(OperandText(i.Operand)).Append(';');
                }
            }
            return Sha1Hex(body.ToString());
        }

        private static string OperandText(object o)
        {
            switch (o)
            {
                case null: return "";
                case MemberReference r: return Norm(r.FullName);
                case Instruction j: return "@" + j.Offset;
                case Instruction[] js: return Offsets(js);
                case VariableDefinition v: return "v" + v.Index;
                case ParameterDefinition pd: return "p" + pd.Index;
                case string s: return "\"" + s + "\"";
                case double d: return "d" + BitConverter.ToString(BitConverter.GetBytes(d));
                case float f: return "f" + BitConverter.ToString(BitConverter.GetBytes(f));
                case IFormattable n: return n.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
                case CallSite cs: return "calli " + cs.FullName;
                default: return o.GetType().Name + ":" + o;
            }
        }

        private static string Offsets(Instruction[] js)
        {
            string[] offsets = new string[js.Length];
            for (int k = 0; k < js.Length; k++)
            {
                offsets[k] = js[k].Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return string.Join(",", offsets);
        }

        public static string Field(FieldDefinition f)
        {
            return Sha1Hex(f.FieldType.FullName + (f.IsStatic ? " static" : "") + " " + ((int)f.Attributes).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static string Sha1Hex(string s)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h)
                {
                    sb.Append(b.ToString("X2"));
                }
                return sb.ToString();
            }
        }
    }

    internal sealed class DepVerdict
    {
        public readonly List<string> Missing = new List<string>();   // "M Type::Name(...)" that no longer exists (or is ambiguous)
        public readonly List<string> Changed = new List<string>();   // members whose hash differs
        public int Checked;
        public bool Identical => Missing.Count == 0 && Changed.Count == 0;

        public string Kind => Missing.Count > 0 ? "missing" : Changed.Count > 0 ? "changed" : "identical";
    }

    internal static class DepCompare
    {
        // Looks every manifest entry up in the given game modules (by scope name) and hashes it the same way the
        // manifest was made. A member that is not found, or found twice, is Missing; a different hash is Changed.
        public static DepVerdict Compare(DepManifest manifest, Func<string, ModuleDefinition> moduleOfScope)
        {
            DepVerdict v = new DepVerdict();
            foreach (DepEntry e in manifest.Entries)
            {
                v.Checked++;
                ModuleDefinition module = moduleOfScope(e.Scope);
                if (module == null)
                {
                    v.Missing.Add(e.Kind + " " + e.Key + " (assembly " + e.Scope + " not found)");
                    continue;
                }
                string hash = HashOf(module, e, out string problem);
                if (hash == null)
                {
                    v.Missing.Add(e.Kind + " " + e.Key + (problem == null ? "" : " (" + problem + ")"));
                }
                else if (hash != e.Hash)
                {
                    v.Changed.Add(e.Kind + " " + e.Key);
                }
            }
            return v;
        }

        // The hash the entry has in this module, or null when it is not there (problem says why).
        public static string HashOf(ModuleDefinition module, DepEntry e, out string problem)
        {
            problem = null;
            if (e.Kind == 'T')
            {
                return module.GetType(e.Key) != null ? "" : null;
            }
            int sep = e.Key.IndexOf("::", StringComparison.Ordinal);
            if (sep < 0)
            {
                problem = "bad key";
                return null;
            }
            string declaring = e.Key.Substring(0, sep);
            int sp = declaring.LastIndexOf(' ');          // "ReturnType Declaring" for methods and fields
            if (sp >= 0)
            {
                declaring = declaring.Substring(sp + 1);
            }
            TypeDefinition type = module.GetType(declaring);
            if (type == null)
            {
                problem = "type " + declaring + " not found";
                return null;
            }
            if (e.Kind == 'M')
            {
                MethodDefinition found = null;
                int count = 0;
                foreach (MethodDefinition m in type.Methods)
                {
                    if (m.FullName == e.Key)
                    {
                        found = found ?? m;
                        count++;
                    }
                }
                if (count != 1)
                {
                    problem = count == 0 ? "no such method" : "ambiguous";
                    return null;
                }
                return DepHash.Method(found);
            }
            if (e.Kind == 'F')
            {
                FieldDefinition found = null;
                int count = 0;
                foreach (FieldDefinition f in type.Fields)
                {
                    if (f.FullName == e.Key)
                    {
                        found = found ?? f;
                        count++;
                    }
                }
                if (count != 1)
                {
                    problem = count == 0 ? "no such field" : "ambiguous";
                    return null;
                }
                return DepHash.Field(found);
            }
            problem = "unknown kind " + e.Kind;
            return null;
        }

        // Opens the game assemblies from a Managed folder (the tool, the harness) with a resolver on that folder.
        public static Func<string, ModuleDefinition> ModulesIn(string managedDir, out IDisposable handle)
        {
            DefaultAssemblyResolver res = new DefaultAssemblyResolver();
            res.AddSearchDirectory(managedDir);
            ReaderParameters rp = new ReaderParameters { AssemblyResolver = res };
            Dictionary<string, ModuleDefinition> open = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
            handle = new Modules(open, res);
            return scope =>
            {
                if (open.TryGetValue(scope, out ModuleDefinition m))
                {
                    return m;
                }
                string path = Path.Combine(managedDir, scope + ".dll");
                m = File.Exists(path) ? ModuleDefinition.ReadModule(path, rp) : null;
                open[scope] = m;
                return m;
            };
        }

        private sealed class Modules : IDisposable
        {
            private readonly Dictionary<string, ModuleDefinition> open;
            private readonly IDisposable resolver;
            public Modules(Dictionary<string, ModuleDefinition> open, IDisposable resolver) { this.open = open; this.resolver = resolver; }
            public void Dispose()
            {
                foreach (ModuleDefinition m in open.Values)
                {
                    m?.Dispose();
                }
                resolver.Dispose();
            }
        }
    }
}
