using GodTools.UI;
using HarmonyLib;

namespace GodTools.Patch;

internal static class PatchRankingsScrollbar
{
    [HarmonyPrefix, HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.toggleScrollbar))]
    private static bool Toggle(ScrollWindow __instance) => UseNativeManagement(__instance);

    [HarmonyPrefix, HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.updateScrollbar))]
    private static bool Update(ScrollWindow __instance) => UseNativeManagement(__instance);

    private static bool UseNativeManagement(ScrollWindow window) =>
        window.screen_id != nameof(WindowRankings) && window.screen_id != nameof(WindowCreatureSavedList);
}
