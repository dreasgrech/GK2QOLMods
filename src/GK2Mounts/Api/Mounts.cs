using System;
using System.Collections.Generic;

namespace GK2Mounts.Api
{
    // The two ways to use the mount: sit on the donkey, or ride it while it pulls its cart.
    public enum MountStyle
    {
        Ride,
        Cart,
    }

    // The answer to TryMount / SwitchStyle: Ok, or the reason it was refused (the text the keeper's bubble shows).
    public readonly struct MountResult
    {
        public bool Ok { get; }
        public string Reason { get; }

        private MountResult(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static MountResult Success => new MountResult(true, null);

        public static MountResult Refused(string reason) => new MountResult(false, reason ?? "refused");

        public override string ToString() => Ok ? "ok" : "refused: " + Reason;
    }

    // Public API of GK2 Mounts for other mods (docs/mounts-api-design.md).
    //
    // Reference GK2Mounts.dll and declare [BepInDependency("dreas.gk2.mounts")]. Call from the Unity main thread only
    // (Update, event handlers, Harmony patches of game code). Everything goes through the core's own safety checks:
    // rules added here can refuse more, never allow what the core refuses (co-op, indoors, busy, building, saves).
    public static class Mounts
    {
        // The API's own version, separate from the mod version: minor for additions, major for breaking changes.
        public static readonly Version ApiVersion = new Version(0, 1, 0);

        // Owner id the core uses for its built-in mount key (until the showcase mod takes over the keys).
        public const string BuiltInKeyOwner = "dreas.gk2.mounts.key";

        // False when the core is switched off (General.Enabled) or disabled itself after a game update.
        public static bool IsAvailable => Plugin.Patched && Plugin.Enabled != null && Plugin.Enabled.Value;

        public static bool IsMounted => Mount.IsMounted;

        // The style being ridden; meaningless while not mounted.
        public static MountStyle Style => Mount.CurrentStyle;

        // Id of the mod that started the current ride (null while not mounted).
        public static string Owner => Mount.IsMounted ? owner : null;

        // Summon the donkey under the keeper in this style. Refused (with the reason) when the core or a rule says no,
        // when already mounted, or outside a loaded game. showMessage: say the reason in the keeper's bubble.
        public static MountResult TryMount(string ownerId, MountStyle style, bool showMessage = true)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (!IsAvailable)
            {
                return MountResult.Refused("GK2 Mounts is disabled.");
            }
            if (Mount.IsMounted)
            {
                return MountResult.Refused("Already mounted.");
            }
            owner = ownerId; // before the attempt: Mounted handlers may read Owner
            string reason = Mount.TryMountFromApi(style, showMessage);
            if (reason != null)
            {
                owner = null;
                return MountResult.Refused(reason);
            }
            return MountResult.Success;
        }

        // End the ride (cargo is handed back to the keeper first). Does nothing while not mounted.
        public static void Dismount(string reason = null)
        {
            if (Mount.IsMounted)
            {
                Mount.DismountFromApi(string.IsNullOrEmpty(reason) ? "dismounted" : reason);
            }
        }

        // Mount again in the other style, in place. The rules for the new style are asked first; if one refuses,
        // the ride goes on unchanged and the refusal is returned.
        public static MountResult SwitchStyle(MountStyle style, bool showMessage = true)
        {
            if (!Mount.IsMounted)
            {
                return MountResult.Refused("Not mounted.");
            }
            if (Mount.CurrentStyle == style)
            {
                return MountResult.Success;
            }
            string reason = MountRules.WhyCannotMount(style);
            if (reason != null)
            {
                if (showMessage)
                {
                    Mount.Say(reason);
                }
                return MountResult.Refused(reason);
            }
            string keepOwner = owner;
            Mount.DismountFromApi("style change");
            MountResult result = TryMount(keepOwner ?? BuiltInKeyOwner, style, showMessage);
            return result;
        }

        public static void RingBell()
        {
            if (Mount.IsMounted)
            {
                MountSounds.RingBell();
            }
        }

        public static void Bray()
        {
            if (Mount.IsMounted)
            {
                MountSounds.Bray();
            }
        }

        // A rule asked before every mount: return null to allow, or the reason to refuse (shown in the keeper's
        // bubble). Rules from all mods must allow. Dispose the returned handle to remove the rule.
        public static IDisposable AddMountRule(string ownerId, Func<MountStyle, string> whyNot)
        {
            return MountRules.AddMount(ownerId, whyNot);
        }

        // A rule asked about twice a second while riding: return null to keep riding, or the reason to dismount
        // (shown in the keeper's bubble). Dispose the returned handle to remove the rule.
        public static IDisposable AddStayRule(string ownerId, Func<string> whyDismount)
        {
            return MountRules.AddStay(ownerId, whyDismount);
        }

        // Removes every rule this owner added.
        public static void RemoveRules(string ownerId)
        {
            MountRules.RemoveAll(ownerId);
        }

        // After the donkey is summoned (argument: the style). Handlers that throw are logged and skipped.
        public static event Action<MountStyle> Mounted;

        // After the ride ended, cargo handed back (argument: the reason, e.g. "dismounted", "indoors", a rule's text).
        public static event Action<string> Dismounted;

        // ------------------------------------------------------------------ core side

        private static string owner;

        internal static void RaiseMounted(MountStyle style)
        {
            Raise(Mounted, style, "Mounted");
        }

        internal static void RaiseDismounted(string reason)
        {
            owner = null;
            Raise(Dismounted, reason, "Dismounted");
        }

        private static void Raise<T>(Action<T> handlers, T arg, string name)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)d)(arg);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"API: a {name} handler ({d.Method.DeclaringType?.Assembly.GetName().Name}) failed: {e.Message}");
                }
            }
        }
    }

    // Rules added by other mods (internal; reached through Mounts.Add*Rule).
    internal static class MountRules
    {
        private sealed class Rule
        {
            public string Owner;
            public Func<MountStyle, string> Mount;
            public Func<string> Stay;
            public bool Warned;
        }

        private sealed class Handle : IDisposable
        {
            private Rule rule;

            public Handle(Rule r)
            {
                rule = r;
            }

            public void Dispose()
            {
                if (rule != null)
                {
                    rules.Remove(rule);
                    rule = null;
                }
            }
        }

        private static readonly List<Rule> rules = new List<Rule>();
        private static float nextStayCheck;

        internal static IDisposable AddMount(string ownerId, Func<MountStyle, string> f)
        {
            return Add(new Rule { Owner = Check(ownerId, f), Mount = f });
        }

        internal static IDisposable AddStay(string ownerId, Func<string> f)
        {
            return Add(new Rule { Owner = Check(ownerId, f), Stay = f });
        }

        internal static void RemoveAll(string ownerId)
        {
            rules.RemoveAll(r => r.Owner == ownerId);
        }

        private static string Check(string ownerId, object f)
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                throw new ArgumentException("An owner id (your plugin GUID) is required.", nameof(ownerId));
            }
            if (f == null)
            {
                throw new ArgumentNullException("rule");
            }
            return ownerId;
        }

        private static IDisposable Add(Rule r)
        {
            rules.Add(r);
            return new Handle(r);
        }

        // Null = every rule allows. A rule that throws allows (it is logged once): the rules only restrict, the
        // core's safety checks never depend on them.
        internal static string WhyCannotMount(MountStyle style)
        {
            foreach (Rule r in rules.ToArray())
            {
                if (r.Mount == null)
                {
                    continue;
                }
                string why = Ask(r, () => r.Mount(style));
                if (why != null)
                {
                    return why;
                }
            }
            return null;
        }

        // Polled from the mount's frame update; asks the stay rules at most twice a second.
        internal static string WhyMustDismountIfDue()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < nextStayCheck)
            {
                return null;
            }
            nextStayCheck = now + 0.5f;
            foreach (Rule r in rules.ToArray())
            {
                if (r.Stay == null)
                {
                    continue;
                }
                string why = Ask(r, r.Stay);
                if (why != null)
                {
                    return why;
                }
            }
            return null;
        }

        private static string Ask(Rule r, Func<string> f)
        {
            try
            {
                string why = f();
                return string.IsNullOrEmpty(why) ? null : why;
            }
            catch (Exception e)
            {
                if (!r.Warned)
                {
                    r.Warned = true;
                    Plugin.Log.LogWarning($"API: a rule of {r.Owner} failed and is ignored (it allows): {e.Message}");
                }
                return null;
            }
        }
    }
}
