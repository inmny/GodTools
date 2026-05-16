using HarmonyLib;

namespace GodTools.Patch;

internal class PatchWorldAgeEffects
{
    [HarmonyPrefix, HarmonyPatch(typeof(WorldAgeEffects), "updateEffects")]
    private static void WorldAgeEffects_updateEffects_prefix(ref float pElapsed)
    {
        pElapsed *= CustomGodToolsConfig.AgeLightChangeSpeed;
    }
}
