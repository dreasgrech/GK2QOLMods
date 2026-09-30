using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using FlowCanvas;

namespace GK2Mounts
{
    // Development aid: dumps a WGO flow script (the visual-scripting graph attached to a world object definition,
    // e.g. the donkey's "npc_donkey" delivery routine) to a JSON file, and can send a named event to the running
    // script of a real world object. Used to learn the real donkey's route and its trigger events.
    internal static class ScriptDump
    {
        public static string DumpWgoScript(string scriptName)
        {
            FlowGraph graph = FlowScriptAssetLoadPolicy.Load(WgoDataScript.WGO_DATA_SCRIPTS_RELATIVE_PATH + scriptName);
            if (graph == null)
            {
                return "graph not found: " + scriptName;
            }
            try
            {
                string json = FindSerializedJson(graph);
                string path = Path.Combine(Paths.BepInExRootPath, "GK2-script-" + scriptName + ".json");
                File.WriteAllText(path, json ?? "(no serialized graph string found)");
                var sb = new StringBuilder();
                sb.Append($"script {scriptName}: {(json == null ? 0 : json.Length)} chars -> {path}");
                if (json != null)
                {
                    sb.Append(" | node types: ");
                    var counts = new Dictionary<string, int>();
                    foreach (Match m in Regex.Matches(json, "\\\\?\"\\$type\\\\?\":\\\\?\"([A-Za-z0-9_.]+)"))
                    {
                        string t = m.Groups[1].Value;
                        int dot = t.LastIndexOf('.');
                        t = dot >= 0 ? t.Substring(dot + 1) : t;
                        counts[t] = counts.TryGetValue(t, out int c) ? c + 1 : 1;
                    }
                    foreach (KeyValuePair<string, int> kv in counts)
                    {
                        sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                    }
                    sb.Append("| string values: ");
                    var seen = new HashSet<string>();
                    foreach (Match m in Regex.Matches(json, "\\\\?\"(?:eventName|EventName|eventId|gdPointId|Id|Phrase|GameresName|_value)\\\\?\":\\s*(?:\\{\\\\?\"\\$content\\\\?\":)?\\\\?\"([^\"\\\\]+)"))
                    {
                        if (seen.Add(m.Groups[1].Value))
                        {
                            sb.Append(m.Groups[1].Value).Append(' ');
                        }
                    }
                }
                return sb.ToString();
            }
            finally
            {
                try
                {
                    FlowScriptAssetLoadPolicy.ReleaseGraph(graph);
                }
                catch
                {
                }
            }
        }

        // NodeCanvas graphs keep their content as a serialized JSON string field; find the longest JSON-looking
        // string on the instance without depending on the private field's name.
        internal static string FindSerializedJson(object graph)
        {
            string best = null;
            for (Type t = graph.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType != typeof(string))
                    {
                        continue;
                    }
                    string v = f.GetValue(graph) as string;
                    if (v != null && v.TrimStart().StartsWith("{") && (best == null || v.Length > best.Length))
                    {
                        best = v;
                    }
                }
            }
            return best;
        }

        // While Time.frameCount is at or below this, the Flow_Body prefix (Patches.cs) forces the body roll to
        // succeed so a manually started delivery always drives the route. Reset by time, so a graph that never
        // reaches the roll cannot leave it armed.
        internal static int ForceBodyRollUntilFrame = -1;

        // Starts the game donkey's own delivery routine, exactly as the game does: the "full_cycle" event on the
        // npc_donkey script (found in the dumped graph). Its chain is: GetGameRes global_lock_on_drop_bodies (true =
        // stop) -> quest 33_base_complain_go InProgress? (yes = walk to the morgue instead) -> Body Roll (morgue
        // capacity + drop chance; forced to succeed here so we always see the cart move) -> puff FX, set direction,
        // put the body on the cart, teleport to GD point donkey_01 -> GoTo donkey_05 -> "full_cycle_back" (drop
        // bodies at the morgue, then GoTo donkey_01 -> "Disable" -> teleport to RT_spot). Debug aid only; nothing
        // here is saved beyond what the game's own routine saves.
        public static string StartDelivery()
        {
            try
            {
                if (MainGame.Instance?.GameSave?.worldData == null || MainGame.PlayerData == null)
                {
                    return "no game loaded";
                }
                List<WgoData> list = MainGame.WorldData.GetWgoDataList("npc_donkey");
                WgoData donkey = list != null && list.Count > 0 ? list[0] : MainGame.WorldData.GetWgoDataByCustomTag("npc_donkey");
                if (donkey == null)
                {
                    return "no npc_donkey in the world";
                }
                var sb = new StringBuilder();
                float lock_ = MainGame.PlayerData.GetRes("global_lock_on_drop_bodies");
                bool complain = MainGame.Instance.GameSave.questSystemData.IsQuestInStatus("33_base_complain_go", QuestStatus.InProgress);
                int bodies = MainGame.PlayerData.GetResInt("cur_bodies_count");
                float morgue = MainGame.WorldData.GetWorldZoneDataById("morgue")?.GetTotalQuality() ?? -1f;
                sb.Append($"preconditions: global_lock_on_drop_bodies={lock_} (must be 0), quest 33_base_complain_go InProgress={complain} (true = goes to the morgue instead), cur_bodies_count={bodies}, morgue capacity={morgue}, donkey_ready={MainGame.PlayerData.GetRes("donkey_ready")}; ");
                sb.Append($"donkey at {donkey.Position} in world [{donkey.WorldId}]. ");
                ForceBodyRollUntilFrame = UnityEngine.Time.frameCount + 2;
                donkey.FireEvent("full_cycle");
                sb.Append("Sent 'full_cycle' with the body roll forced to succeed. The game's own log lines (Body Roll / Started body drop) follow if the script reacted; it appears at GD point donkey_01 and walks to donkey_05.");
                return sb.ToString();
            }
            catch (Exception e)
            {
                return "failed: " + e;
            }
        }

        // Sends an event to the running script of every world object with this id (WgoData.FireEvent is what the
        // game's own flow nodes use). Returns what happened.
        public static string FireEventOnWgo(string wgoId, string eventName)
        {
            try
            {
                List<WgoData> list = MainGame.WorldData.GetWgoDataList(wgoId);
                if (list == null || list.Count == 0)
                {
                    return "no " + wgoId + " in the world";
                }
                int n = 0;
                foreach (WgoData d in list)
                {
                    d.FireEvent(eventName);
                    n++;
                }
                return $"event '{eventName}' sent to {n} {wgoId} (see the game's own FireEvent log line for the script's reaction)";
            }
            catch (Exception e)
            {
                return "failed: " + e.Message;
            }
        }
    }
}
