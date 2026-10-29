using System;
using HarmonyLib;

namespace GK2TechTreeSearch
{
    // The page is built once (CharacterWindow.Init → its pages' Init); the box is added to it then.
    [HarmonyPatch(typeof(TechTreePageWidget), nameof(TechTreePageWidget.Init))]
    internal static class TechTreePageWidget_Init_Patch
    {
        private static void Postfix(TechTreePageWidget __instance)
        {
            try
            {
                SearchBox.Create(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Tech search box could not be added; the Tech Tree works as normal without it. {e}");
            }
        }
    }

    // Opening the page (CharacterWindow.SwitchPage → Draw → Redraw), and redraws while it's open.
    [HarmonyPatch(typeof(TechTreePageWidget), nameof(TechTreePageWidget.Redraw))]
    internal static class TechTreePageWidget_Redraw_Patch
    {
        private static bool errorLogged;

        private static void Postfix()
        {
            try
            {
                SearchBox.Instance?.OnPageShown();
            }
            catch (Exception e)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Plugin.Log.LogError($"Tech search box failed to show; the Tech Tree works as normal. {e}");
                }
            }
        }
    }

    // Leaving the page or closing the window: close the list and give the game its keys back.
    [HarmonyPatch(typeof(TechTreePageWidget), nameof(TechTreePageWidget.Hide))]
    internal static class TechTreePageWidget_Hide_Patch
    {
        private static void Postfix()
        {
            try
            {
                SearchBox.Instance?.OnPageHidden();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Tech search box failed to close. {e}");
            }
        }
    }
}
