using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;

namespace GK2OwnedCount
{
    // Every item tooltip in the game is assembled by this one method; it fills a list of widget data the caller
    // then shows. Appending here reaches inventory, chest, vendor, craft and dialogue tooltips alike.
    // addHeader is true when the tooltip is the item's own, false when the item is embedded in another tooltip.
    [HarmonyPatch(typeof(UITooltip), "AddItemWidgets")]
    internal static class UITooltip_AddItemWidgets_Patch
    {
        private static bool errorLogged;

        private static void Postfix(List<LazyWidgetDataBase> widgetData, ItemDef itemDef, bool addHeader)
        {
            if (!Plugin.Enabled.Value || widgetData == null || itemDef == null)
            {
                return;
            }
            if (Plugin.OnlyOnOwnTooltip.Value && !addHeader)
            {
                return;
            }
            try
            {
                if (MainGame.Instance == null || MainGame.PlayerData == null)
                {
                    return; // no game loaded (main menu)
                }
                Totals t = Counter.Count(itemDef);
                UITooltip tooltip = Access.TooltipInstance();
                TextStyle style = tooltip != null ? Access.SmallDescriptionStyle(tooltip) : null;

                // Tooltips are narrow: keep each line short, breakdown on its own line.
                string line = "Owned: " + t.Total;
                if (t.OtherGrades >= 0 && t.OtherGrades != t.Total)
                {
                    line += "  (all grades: " + t.OtherGrades + ")";
                }
                if (widgetData.Count > 0 && !(widgetData[widgetData.Count - 1] is UITooltipSeparatorWidgetData))
                {
                    widgetData.Add(new UITooltipSeparatorWidgetData());
                }
                widgetData.Add(new UITooltipTextWidgetData(line, TextAlignmentOptions.Center, style));
                if (Plugin.ShowBreakdown.Value)
                {
                    string detail = "on you " + t.OnPlayer + " · in storage " + t.InStorage +
                                    (t.ChestsWithItem > 0 ? " (" + t.ChestsWithItem + (t.ChestsWithItem == 1 ? " chest)" : " chests)") : "");
                    widgetData.Add(new UITooltipTextWidgetData(detail, TextAlignmentOptions.Center, style));
                }
                if (Plugin.ShowPerArea.Value && t.PerArea.Count > 0)
                {
                    string[] parts = new string[t.PerArea.Count];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        parts[i] = t.PerArea[i].Key + " " + t.PerArea[i].Value;
                    }
                    widgetData.Add(new UITooltipTextWidgetData(string.Join("  ·  ", parts), TextAlignmentOptions.Center, style));
                }
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogError($"Owned count failed; tooltips shown without it. {e}");
                }
            }
        }
    }
}
