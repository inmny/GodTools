using GodTools.UI;
using HarmonyLib;

namespace GodTools.Patch;

internal static class PatchWorldScriptMapPicker
{
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.updateControls))]
    private static bool BeforeControls(PlayerControl __instance) =>
        WindowWorldScript.Instance == null || WindowWorldScript.Instance.HandleMapSelection(__instance);
}
