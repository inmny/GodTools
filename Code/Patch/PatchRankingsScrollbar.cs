using GodTools.UI;
using HarmonyLib;

namespace GodTools.Patch;

internal static class PatchRankingsScrollbar
{
    [HarmonyPrefix, HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.toggleScrollbar))]
    private static bool Toggle(ScrollWindow __instance) => __instance.screen_id != nameof(WindowRankings);

    [HarmonyPrefix, HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.updateScrollbar))]
    private static bool Update(ScrollWindow __instance) => __instance.screen_id != nameof(WindowRankings);
}
