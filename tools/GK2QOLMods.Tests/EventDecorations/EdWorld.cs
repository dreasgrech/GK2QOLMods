using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GK2QOLMods.Tests
{
    // The world as GK2 Event Decorations' tests read it, by this file's own code and names: nothing here asks the mod
    // what an area or a kind is. The names and the counts are the audit's of game 1.012 (docs/seasonal-events.md,
    // "The decorations' layout", read from the game's bundle with tools/game-bundles.py); a game that changes them
    // fails the layout test, which is the moment to read the bundle again.
    internal static class EdWorld
    {
        internal const string ContentName = "RuinedTemple_ContentPart_6_Halloween";
        internal const string StaticRoot = "HalloweenDecorationsStatic";
        internal const string FxGroup = "HalloweenFX";

        // The areas: the setting's key (the group is "Halloween" + key) and how many items the audit counted in it.
        internal static readonly string[] AreaKeys = { "Village", "Port", "PlayerZone", "PlayerInterior", "TavernInterior", "RestaurantInterior", "Barracks", "BarracksInterior", "AstrologerInterior", "WarehouseInterior", "Dungeons" };
        internal static readonly int[] AreaItems = { 50, 16, 22, 3, 18, 9, 8, 7, 1, 3, 3 };
        // The kinds: the setting's key, the items' name prefix, how many the audit counted. (Bats is the FX group.)
        internal static readonly string[] KindKeys = { "Pumpkins", "Garlands", "Scarecrows", "Pies", "Plants", "OtherProps" };
        internal static readonly string[] KindPrefixes = { "common_hw_pumpkin", "common_hw_flags", "common_hw_scarecrow", "common_hw_pie", "common_hw_plants", "common_hw_0" };
        internal static readonly int[] KindItems = { 69, 30, 2, 8, 3, 28 };
        internal const string BatsKey = "Bats";
        internal const int Groups = 12, Items = 140, Glows = 44, Blockers = 104, FxItems = 14;

        // ------------------------------------------------------------------ the decorations' instance

        // The scene config that owns the decorations (RuinedTemple), whether they are loaded or not.
        internal static GameSceneConfig Owner()
        {
            List<GameSceneConfig> configs = MainGame.Instance != null ? MainGame.Instance.gameSceneConfigs : null;
            if (configs == null) return null;
            foreach (GameSceneConfig c in configs)
            {
                if (c != null && c.GetContentDataRef(ContentName) != null) return c;
            }
            return null;
        }

        // The loaded decorations; null while they are down.
        internal static GameObject Content()
        {
            GameSceneConfig owner = Owner();
            return owner != null && owner.TryGetLoadedContent(ContentName, out GameObject go) && go != null ? go : null;
        }

        internal static bool Up => Content() != null;

        // How many copies of the decorations stand in the scenes (not the prefab the game keeps in memory): one while
        // up, none while down; a second one would be a live switch that didn't clean up after itself.
        internal static int Copies()
        {
            int n = 0;
            foreach (SceneWgoContentData d in Resources.FindObjectsOfTypeAll<SceneWgoContentData>())
            {
                if (d != null && d.gameObject.scene.IsValid() && d.name.StartsWith(ContentName, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ what stands there

        internal sealed class Survey
        {
            internal Transform Root;
            internal readonly List<Transform> GroupList = new List<Transform>();
            internal readonly List<Transform> ItemList = new List<Transform>();   // the items of every group but the bats'
            internal readonly List<Transform> GlowList = new List<Transform>();
            internal readonly List<Collider> BlockerList = new List<Collider>();  // on "Obstacle" objects
            internal readonly List<Collider> OtherColliders = new List<Collider>(); // the bats' trigger zones

            internal Transform Group(string name)
            {
                foreach (Transform g in GroupList) if (g.name == name) return g;
                return null;
            }

            internal Transform Fx => Group(FxGroup);
        }

        internal static Survey Look()
        {
            GameObject content = Content();
            if (content == null) return null;
            var s = new Survey { Root = content.transform.Find(StaticRoot) };
            if (s.Root == null) return s;
            for (int g = 0; g < s.Root.childCount; g++)
            {
                Transform group = s.Root.GetChild(g);
                s.GroupList.Add(group);
                if (group.name == FxGroup) continue;
                for (int i = 0; i < group.childCount; i++) s.ItemList.Add(group.GetChild(i));
            }
            foreach (Transform t in s.Root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("DecorLight", StringComparison.Ordinal) || t.name.StartsWith("Decor Light", StringComparison.Ordinal)) s.GlowList.Add(t);
            }
            foreach (Collider c in s.Root.GetComponentsInChildren<Collider>(true))
            {
                if (c.gameObject.name.StartsWith("Obstacle", StringComparison.Ordinal)) s.BlockerList.Add(c);
                else s.OtherColliders.Add(c);
            }
            return s;
        }

        // The area an item's group is (index into AreaKeys), -1 for a group that is none.
        internal static int AreaOf(Transform group)
        {
            string n = group != null ? group.name : "";
            for (int k = 0; k < AreaKeys.Length; k++) if (n == "Halloween" + AreaKeys[k]) return k;
            return -1;
        }

        // The kind an item is (index into KindKeys), -1 for none.
        internal static int KindOf(Transform item)
        {
            for (int k = 0; k < KindPrefixes.Length; k++) if (item.name.StartsWith(KindPrefixes[k], StringComparison.Ordinal)) return k;
            return -1;
        }

        // Everything the mod can switch that is switched off, as the names of what is: "g:<group>",
        // "i:<group>/<item>#<n>", "l:<n> glows", "c:<n> blockers". Two looks alike have the same list.
        internal static List<string> Off(Survey s)
        {
            var off = new List<string>();
            if (s == null || s.Root == null) return off;
            foreach (Transform g in s.GroupList) if (!g.gameObject.activeSelf) off.Add("g:" + g.name);
            for (int k = 0; k < s.ItemList.Count; k++)
            {
                Transform i = s.ItemList[k];
                if (!i.gameObject.activeSelf) off.Add("i:" + i.parent.name + "/" + i.name + "#" + i.GetSiblingIndex());
            }
            int glows = 0, blockers = 0;
            foreach (Transform t in s.GlowList) if (!t.gameObject.activeSelf) glows++;
            foreach (Collider c in s.BlockerList) if (!c.enabled) blockers++;
            if (glows > 0) off.Add($"l:{glows} glow(s)");
            if (blockers > 0) off.Add($"c:{blockers} blocker(s)");
            // not the bats' zones (OtherColliders): the mod never switches them, and the game may itself
            return off;
        }

        internal static string Some(List<string> names, int most = 6)
        {
            if (names == null || names.Count == 0) return "";
            var first = names.Count > most ? names.GetRange(0, most) : names;
            return ": " + string.Join(", ", first) + (names.Count > most ? $" and {names.Count - most} more" : "");
        }

        // The mod's own line for what shows ("Halloween decorations: 11 of 12 groups, ..."), as this file counts it.
        internal static string LineFor(int groupsOn, int itemsOn, int glowsOn, int blockersOn, Survey s) =>
            $"Halloween decorations: {groupsOn} of {s.GroupList.Count} groups, {itemsOn} of {s.ItemList.Count} items in the shown groups and kinds, " +
            $"{glowsOn} of {s.GlowList.Count} glows, {blockersOn} of {s.BlockerList.Count} blocking colliders on.";

        // ------------------------------------------------------------------ the save's side of the scene

        // What the save holds of the scene the decorations stand in: the decorations must add nothing to it.
        internal static string SaveCounts()
        {
            GameSceneConfig owner = Owner();
            WorldData world = MainGame.Instance != null && MainGame.Instance.GameSave != null ? MainGame.Instance.GameSave.worldData : null;
            if (owner == null || world == null) return "?";
            GameSceneData d = world.GetGameSceneDataById(owner.name);
            if (d == null) return "no scene data";
            return $"{world.gameSceneDataList.Count} scenes; {d.id}: {d.wgoDataList.Count} objects, {d.wsoDataList.Count} simple objects, {d.worldZones.Count} zones, " +
                   $"{d.loadedContentsList.Count} contents, {d.fightingLevels.Count} fighting levels, {d.droppedItems.Count} drops";
        }

        // ------------------------------------------------------------------ the ghost

        internal const string GhostId = "npc_ghost", HomePoint = "RT_spot", VillagePoint = "village_brothers_jeffry_02";

        internal sealed class Ghost
        {
            internal WgoData Data;
            internal Vector3 Home;
            internal bool HasScript;

            internal float FromHome => (Data.Position - Home).magnitude;
            internal bool AtHome => FromHome <= 0.5f;
            internal bool Moving => Data.MovementComponent != null && Data.MovementComponent.IsMoving;

            // On a walk or still working out its path (the game's private state: IsMoving alone is false while the
            // path is built, and a walk that starts after the test thought it stood would undo what the test did).
            internal bool Busy
            {
                get
                {
                    if (Data.MovementComponent == null) return false;
                    try
                    {
                        FieldInfo status = AccessTools.Field(typeof(MovementComponent), "status");
                        if (status != null) return !Equals(status.GetValue(Data.MovementComponent), MovementComponent.Status.None);
                    }
                    catch
                    {
                        // the field renamed: what the game says in public
                    }
                    return Data.MovementComponent.IsMoving;
                }
            }
        }

        // The ghost of this save, its home point and whether its script runs (without one the game's FireEvent only
        // writes an error). Null: this save has none, or no home point.
        internal static Ghost FindGhost()
        {
            WorldData world = MainGame.Instance != null && MainGame.Instance.GameSave != null ? MainGame.Instance.GameSave.worldData : null;
            if (world == null) return null;
            List<GDPointData> points = world.gdPointsData.GetGDPointsDataById(HomePoint);
            if (points == null || points.Count == 0 || points[0] == null) return null;
            WgoData ghost = null;
            foreach (GameSceneData scene in world.gameSceneDataList)
            {
                if (scene == null || scene.wgoDataList == null) continue;
                foreach (WgoData w in scene.wgoDataList)
                {
                    if (w != null && w.id == GhostId) { ghost = w; break; }
                }
                if (ghost != null) break;
            }
            if (ghost == null) return null;
            var g = new Ghost { Data = ghost, Home = points[0].Position };
            try
            {
                // the game's own list of running scripts (private): a ghost without one can't be told anything
                FieldInfo running = AccessTools.Field(typeof(WgoDataScriptsManager), "runningScripts");
                g.HasScript = running != null && running.GetValue(WgoDataScriptsManager.Instance) is IDictionary scripts && scripts.Contains(ghost.UniqueId);
            }
            catch
            {
                g.HasScript = false;
            }
            return g;
        }

        // The two rows of the game's balance that start the ghost's walk in the evening and send it home in the
        // morning, each behind the condition IsHalloweenEvent() (the game's expression function).
        internal static GameLogicDef GhostRow(string id)
        {
            if (GameBalance.Me == null || GameBalance.Me.gameLogicsDefs == null) return null;
            foreach (GameLogicDef d in GameBalance.Me.gameLogicsDefs)
            {
                if (d != null && d.id == id) return d;
            }
            return null;
        }
    }
}
