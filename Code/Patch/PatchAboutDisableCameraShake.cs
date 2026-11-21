using HarmonyLib;
using UnityEngine;

namespace GodTools.Patch;

internal static class PatchAboutDisableCameraShake
{
    [HarmonyPrefix, HarmonyPatch(typeof(MapBox), nameof(MapBox.startShake))]
    private static bool MapBox_startShake_prefix()
    {
        return !CustomGodToolsConfig.DisableCameraShake;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MapBox), nameof(MapBox.updateShake))]
    private static bool MapBox_updateShake_prefix(MapBox __instance)
    {
        if (!CustomGodToolsConfig.DisableCameraShake) return true;

        StopShake(__instance);
        return false;
    }

    internal static void StopShake(MapBox world)
    {
        if (world == null) return;

        world._shake_timer = 0f;
        world._shake_interval_timer = 0f;
        world._shake_intensity = 0f;
        if (world._shake_camera != null)
            world._shake_camera.position = Vector3.zero;
    }
}
