using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2EventDecorations
{
    // Halloween's ghost (npc_ghost) lives in every save from game 1.011 on and waits at the point RT_spot (where the game
    // places it: both at (-199.68, 0, 0) in RuinedTemple, read from the bundles 2026-11-11). During
    // the event its script walks it through the village at night; every step asks the event gate, and when the event is
    // off the step teleports it home. The walk home in the morning is the exception: its last step (the teleport) is a
    // callback the save doesn't keep, so a save made during that walk loads with the ghost walking to the village edge
    // and staying there for as long as the event is off (the plain game does the same after 7 November).
    // docs/seasonal-events.md, "Save safety". While Halloween is off this sends a ghost that stands away from home one
    // step event, and its own script takes it home.
    internal static class GhostHome
    {
        private const string GhostId = "npc_ghost";
        private const string HomePointId = "RT_spot";
        private const string StepEvent = "roll_action";
        private const float CheckEverySeconds = 10f;
        private const float AtHomeDistance = 0.5f;
        private const int MaxAttemptsPerLoad = 2; // a step that doesn't take it home (a changed script) stops the check

        private static int attempts;

        private static WorldData world; // the world the cached ghost belongs to; another one = a different save
        private static WgoData ghost;
        private static Vector3 home;
        private static bool found;
        private static float nextCheck;
        private static bool errorLogged;

        // MainGame.OnGameStarted: a save (or a new game) has finished loading.
        internal static void OnGameStarted()
        {
            world = null;
            nextCheck = 0f;
            Tick();
        }

        internal static void Tick()
        {
            if (!Plugin.Enabled.Value || !Plugin.SendGhostHome.Value || Time.unscaledTime < nextCheck)
            {
                return;
            }
            nextCheck = Time.unscaledTime + CheckEverySeconds;
            try
            {
                MainGame game = MainGame.Instance;
                if (game == null || game.gameState != MainGame.GameState.InGame || game.GameSave == null)
                {
                    return;
                }
                if (game.GameSave.worldData != world)
                {
                    Find(game.GameSave.worldData);
                }
                if (!found || TemporaryEventDates.IsHalloweenEvent() || ghost.MovementComponent.IsMoving)
                {
                    return;
                }
                if ((ghost.Position - home).sqrMagnitude <= AtHomeDistance * AtHomeDistance || attempts > MaxAttemptsPerLoad)
                {
                    return;
                }
                if (attempts == MaxAttemptsPerLoad)
                {
                    attempts++;
                    Plugin.Log.LogWarning($"Halloween's ghost still stands at {ghost.Position} after {MaxAttemptsPerLoad} steps home; left as it is until the next load.");
                    return;
                }
                attempts++;
                Plugin.Log.LogInfo($"Halloween is off and the ghost stands in the village at {ghost.Position}: sent home.");
                ghost.FireEvent(StepEvent);
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogWarning($"Checking on Halloween's ghost failed; it is left as it is. {e.GetType().Name}: {e.Message}");
                }
            }
        }

        private static void Find(WorldData data)
        {
            world = data;
            ghost = null;
            found = false;
            attempts = 0;
            if (data == null)
            {
                return;
            }
            // the quiet lookup: GetGDPointDataById warns when a point is missing (a game without the ghost)
            List<GDPointData> points = data.gdPointsData.GetGDPointsDataById(HomePointId);
            if (points == null || points.Count == 0 || points[0] == null)
            {
                return;
            }
            home = points[0].Position;
            List<GameSceneData> scenes = data.gameSceneDataList;
            for (int s = 0; s < scenes.Count && ghost == null; s++)
            {
                List<WgoData> wgos = scenes[s]?.wgoDataList;
                if (wgos == null)
                {
                    continue;
                }
                for (int w = 0; w < wgos.Count; w++)
                {
                    if (wgos[w] != null && wgos[w].id == GhostId)
                    {
                        ghost = wgos[w];
                        break;
                    }
                }
            }
            found = ghost != null;
        }
    }
}
