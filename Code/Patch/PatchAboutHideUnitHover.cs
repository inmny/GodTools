using HarmonyLib;

namespace GodTools.Patch;

internal static class PatchAboutHideUnitHover
{
    [HarmonyPrefix, HarmonyPatch(typeof(UnitSelectionEffect), nameof(UnitSelectionEffect.visibleCheck))]
    private static bool UnitSelectionEffect_visibleCheck_prefix(ref bool __result)
    {
        if (!CustomGodToolsConfig.HideUnitHover) return true;

        UnitSelectionEffect.setLastActor(null);
        __result = false;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CursorTooltipHelper), nameof(CursorTooltipHelper.updateGameplayTooltip))]
    private static bool CursorTooltipHelper_updateGameplayTooltip_prefix(ref bool __result)
    {
        if (!CustomGodToolsConfig.HideUnitHover) return true;

        __result = false;
        return false;
    }
}
