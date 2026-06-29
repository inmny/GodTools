using HarmonyLib;
using strings;

namespace GodTools.Patch;

internal static class PatchAboutHideSelectionEffects
{
    [HarmonyPrefix, HarmonyPatch(typeof(QuantumSpriteLibrary), "drawSelectedUnits")]
    private static bool QuantumSpriteLibrary_drawSelectedUnits_prefix()
    {
        return !CustomGodToolsConfig.HideUnitSelectionCircle;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitsToBeSelectedBySquareTool")]
    private static bool QuantumSpriteLibrary_drawUnitsToBeSelectedBySquareTool_prefix()
    {
        return !CustomGodToolsConfig.HideUnitSelectionCircle;
    }
    [HarmonyPrefix, HarmonyPatch(typeof(QuantumSpriteLibrary), "drawSquareSelection")]
    private static bool QuantumSpriteLibrary_drawSquareSelection_prefix()
    {
        return !CustomGodToolsConfig.HideUnitSelectionCircle;
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnAt),
        new[] { typeof(string), typeof(UnityEngine.Vector3), typeof(float) })]
    private static bool EffectsLibrary_spawnAt_Vector3_prefix(string pID, ref BaseEffect __result)
    {
        if (!CustomGodToolsConfig.HideMoveDestinationEffect || pID != S_Effect.fx_move)
        {
            return true;
        }

        __result = null;
        return false;
    }
}
