using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace GK2Mounts
{
    // A tuning value: the player's cfg entry plus an optional session-only override set by another mod through the API
    // (Mounts.Tuning). Value answers, in order: the bound instance's own override (Mount.Current, an instance spawned
    // with its own settings, see docs/mounts-instances.md), the session override, the cfg. GlobalValue skips the
    // instance. Setting Value writes the cfg (the panel, the tuning keys), as before. Overrides are never written
    // anywhere: they end with the session or Clear.
    //
    // Converts implicitly to and from ConfigEntry<T>, so StyleSettings binds exactly as before and every panel helper
    // that takes a ConfigEntry<T> edits the real entry unchanged.
    internal interface ITuned
    {
        string Key { get; }
        Type ValueType { get; }
        ConfigEntryBase EntryBase { get; }
        bool HasOverride { get; }
        string OverrideOwner { get; }
        object Current { get; }
        object SetOverride(object value, string owner);
        object Clamp(object value);
        void ClearOverride();
    }

    internal sealed class Tuned<T> : ITuned
    {
        // Every tuning value by its cfg entry, for the panel (a row whose value is overridden is marked).
        internal static readonly Dictionary<ConfigEntryBase, ITuned> ByEntry = Tuned.ByEntry;

        public readonly ConfigEntry<T> Entry;
        private bool hasOverride;
        private T overrideValue;
        private string owner;

        private readonly string key;

        private Tuned(ConfigEntry<T> entry)
        {
            Entry = entry;
            key = entry.Definition.Key;
            Tuned.ByEntry[entry] = this;
        }

        public T Value
        {
            get
            {
                Mount.Instance i = Mount.Current;
                if (i != null && i.Overrides.Count > 0 && i.Overrides.TryGetValue(key, out object o) && o is T t)
                {
                    return t;
                }
                return hasOverride ? overrideValue : Entry.Value;
            }
            set => Entry.Value = value;
        }

        // Without any instance's override: what Mounts.Tuning and the panel show as the player's (or a mod's) value.
        public T GlobalValue => hasOverride ? overrideValue : Entry.Value;

        public static implicit operator Tuned<T>(ConfigEntry<T> entry) => entry == null ? null : new Tuned<T>(entry);

        public static implicit operator ConfigEntry<T>(Tuned<T> t) => t?.Entry;

        public string Key => key;
        public Type ValueType => typeof(T);
        public ConfigEntryBase EntryBase => Entry;
        public bool HasOverride => hasOverride;
        public string OverrideOwner => hasOverride ? owner : null;
        public object Current => GlobalValue;

        // An instance's override for this key, clamped like the session override. Returns the value applied.
        public object Clamp(object value)
        {
            if (!(value is T))
            {
                throw new ArgumentException($"{Key} takes a {typeof(T).Name}, not a {(value == null ? "null" : value.GetType().Name)}.");
            }
            return Entry.Description?.AcceptableValues != null ? Entry.Description.AcceptableValues.Clamp(value) : value;
        }

        // Clamped to the entry's acceptable range (the same limits the panel has). Returns the value applied.
        public object SetOverride(object value, string by)
        {
            if (!(value is T))
            {
                throw new ArgumentException($"{Key} takes a {typeof(T).Name}, not a {(value == null ? "null" : value.GetType().Name)}.");
            }
            object v = Entry.Description?.AcceptableValues != null ? Entry.Description.AcceptableValues.Clamp(value) : value;
            overrideValue = (T)v;
            hasOverride = true;
            owner = by;
            return overrideValue;
        }

        public void ClearOverride()
        {
            hasOverride = false;
            overrideValue = default;
            owner = null;
        }
    }

    internal static class Tuned
    {
        internal static readonly Dictionary<ConfigEntryBase, ITuned> ByEntry = new Dictionary<ConfigEntryBase, ITuned>();

        // For the panel: the owner of an override on this entry, or null.
        internal static string OverriddenBy(ConfigEntryBase e)
        {
            return e != null && ByEntry.TryGetValue(e, out ITuned t) && t.HasOverride ? t.OverrideOwner : null;
        }

        internal static int OverrideCount()
        {
            int n = 0;
            foreach (ITuned t in ByEntry.Values)
            {
                if (t.HasOverride)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
