using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2EventDecorations
{
    // The Halloween decorations in the world: one content part the game instantiates while a save loads (only when
    // ShouldLoad says so) and keeps loaded (keepLoaded): plain scenery, nothing of it in the save, its objects never
    // registered with the chunk streamer (docs/seasonal-events.md). So taking it down or putting it up is the game's own
    // GameSceneConfig.TryUnloadSceneDataContent / TryLoadSceneDataContentByName, and the area/kind switches are
    // SetActive on its groups and items:
    //   RuinedTemple_ContentPart_6_HalloweenData(Clone)
    //     HalloweenDecorationsStatic
    //       Halloween<Area>  (Village, Port, ..., and HalloweenFX: the bats and glowing eyes)
    //         <item>         (common_hw_pumpkin_06 (3), ...; inside: the picture, "DecorLight"/"Decor Light (...)"
    //                         glows, "Obstacle" colliders)
    internal static class Decorations
    {
        private const string ContentName = "RuinedTemple_ContentPart_6_Halloween"; // the game appends "Data"
        private const string StaticRoot = "HalloweenDecorationsStatic";
        private const string GroupPrefix = "Halloween";
        private const string FxGroup = "HalloweenFX";
        private const string ObstacleName = "Obstacle";

        private static bool errorLogged;
        private static readonly List<Collider> colliders = new List<Collider>();
        private static readonly List<Transform> transforms = new List<Transform>();

        // WorldData.AddMissingConditionalContent (while a save loads, before the scene and its navigation are built):
        // the decorations were just instantiated; switch them now, so the navigation scan sees only what is shown.
        internal static void AfterGameAdded(List<GameSceneConfig> configs)
        {
            try
            {
                GameObject instance = configs != null ? Find(configs, out _) : null;
                if (instance != null)
                {
                    Apply(instance);
                }
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogWarning($"Switching the Halloween decorations' areas and kinds failed; they show as the game made them. {e.GetType().Name}: {e.Message}");
                }
            }
        }

        // MainGame.OnGameStarted: once more, in case anything changed during the load.
        internal static void OnGameStarted()
        {
            Run(sync: false);
        }

        // A setting changed (the cfg file, the Mods menu).
        internal static void OnSettingChanged(object sender, EventArgs e)
        {
            Run(sync: true);
        }

        private static void Run(bool sync)
        {
            try
            {
                MainGame game = MainGame.Instance;
                if (game == null || game.gameState != MainGame.GameState.InGame || game.gameSceneConfigs == null)
                {
                    return;
                }
                if (sync)
                {
                    Sync(game.gameSceneConfigs);
                }
                GameObject instance = Find(game.gameSceneConfigs, out _);
                if (instance != null)
                {
                    Apply(instance);
                }
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogWarning($"Changing the Halloween decorations failed; they stay as they are until the next load. {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private static GameObject Find(List<GameSceneConfig> configs, out GameSceneConfig owner)
        {
            for (int i = 0; i < configs.Count; i++)
            {
                GameSceneConfig c = configs[i];
                if (c != null && c.TryGetLoadedContent(ContentName, out GameObject go) && go != null)
                {
                    owner = c;
                    return go;
                }
            }
            owner = null;
            return null;
        }

        // Make the world match the settings: up when they say on, down when off (SwitchWithoutReload).
        private static void Sync(List<GameSceneConfig> configs)
        {
            if (!Halloween.LiveSwitch.Value)
            {
                return;
            }
            bool want = Halloween.DecorationsOn();
            GameObject instance = Find(configs, out GameSceneConfig owner);
            if (!want && instance != null)
            {
                if (owner.TryUnloadSceneDataContent(ContentName))
                {
                    Plugin.Log.LogInfo("Halloween decorations taken down.");
                }
                else
                {
                    Plugin.Log.LogWarning("The Halloween decorations could not be taken down; they go at the next load.");
                }
            }
            else if (want && instance == null)
            {
                for (int i = 0; i < configs.Count; i++)
                {
                    // returns false for every config without this content; the part's own ShouldLoad asks DecorationsOn
                    if (configs[i] != null && configs[i].TryLoadSceneDataContentByName(ContentName, out SceneWgoContentData _))
                    {
                        Plugin.Log.LogInfo("Halloween decorations put up.");
                        return;
                    }
                }
                Plugin.Log.LogWarning("The Halloween decorations could not be put up (no scene has them in this game version); they follow at the next load if the game has them.");
            }
        }

        private static void Apply(GameObject instance)
        {
            Transform root = instance.transform.Find(StaticRoot);
            if (root == null)
            {
                return; // a game version with a different layout: everything as the game made it
            }
            // the mod switched off (General.Enabled): every group, item, glow and blocker as the game made it
            bool mod = Plugin.Enabled.Value;
            for (int g = 0; g < root.childCount; g++)
            {
                Transform group = root.GetChild(g);
                string groupName = group.name;
                bool groupOn = true;
                if (groupName == FxGroup)
                {
                    Halloween.Kind bats = Halloween.KindByKey("Bats");
                    groupOn = !mod || bats == null || bats.Entry.Value;
                }
                else if (groupName.StartsWith(GroupPrefix, StringComparison.Ordinal))
                {
                    Halloween.Area area = Halloween.AreaByKey(groupName.Substring(GroupPrefix.Length));
                    groupOn = !mod || area == null || area.Entry.Value;
                }
                SetActive(group.gameObject, groupOn);
                if (groupName == FxGroup)
                {
                    continue;
                }
                for (int i = 0; i < group.childCount; i++)
                {
                    Transform item = group.GetChild(i);
                    Halloween.Kind kind = Halloween.KindOf(item.name);
                    SetActive(item.gameObject, !mod || kind == null || kind.Entry.Value);
                }
            }

            // the glows inside the items (lanterns, candles, lit pumpkins)
            transforms.Clear();
            root.GetComponentsInChildren(true, transforms);
            bool glow = !mod || Halloween.Glow.Value;
            for (int i = 0; i < transforms.Count; i++)
            {
                string n = transforms[i].name;
                if (n.StartsWith("DecorLight", StringComparison.Ordinal) || n.StartsWith("Decor Light", StringComparison.Ordinal))
                {
                    SetActive(transforms[i].gameObject, glow);
                }
            }
            transforms.Clear();

            // the blockers: every collider on an "Obstacle" object (104 in game 1.012). Not by isTrigger: the bats'
            // BirdsTriggerZone boxes are plain colliders too (m_IsTrigger false), and must keep working.
            colliders.Clear();
            root.GetComponentsInChildren(true, colliders);
            bool block = !mod || Halloween.BlockWay.Value;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider c = colliders[i];
                if (c != null && c.enabled != block && c.gameObject.name.StartsWith(ObstacleName, StringComparison.Ordinal))
                {
                    c.enabled = block;
                }
            }
            colliders.Clear();
        }

        private static void SetActive(GameObject go, bool on)
        {
            if (go.activeSelf != on)
            {
                go.SetActive(on);
            }
        }
    }
}
