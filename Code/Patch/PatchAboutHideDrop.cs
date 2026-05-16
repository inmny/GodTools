using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GodTools.Patch;

internal static class PatchAboutHideDrop
{
    private static readonly Vector3 HiddenScale = new(0.0001f, 0.0001f, 0.0001f);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(DropManager), nameof(DropManager.spawn),
        new[] { typeof(WorldTile), typeof(DropAsset), typeof(float), typeof(float), typeof(bool), typeof(long) })]
    private static void DropManager_spawn_postfix(Drop __result)
    {
        HideDrop(__result);
    }

    [HarmonyTranspiler, HarmonyPatch(typeof(DropManager), nameof(DropManager.landDrop))]
    private static IEnumerable<CodeInstruction> DropManager_landDrop_transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceFlashPixelCall(
            instructions,
            AccessTools.Method(typeof(PatchAboutHideDrop), nameof(FlashPixelUnlessDropHidden)),
            nameof(DropManager_landDrop_transpiler));
    }

    [HarmonyPrefix, HarmonyPatch(typeof(DropsLibrary), nameof(DropsLibrary.flash))]
    private static bool DropsLibrary_flash_prefix()
    {
        return !CustomGodToolsConfig.HideDrop;
    }

    private static IEnumerable<CodeInstruction> ReplaceFlashPixelCall(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo replacement,
        string patchName)
    {
        var list = instructions.ToList();
        var flash_pixel = AccessTools.Method(typeof(PixelFlashEffects), nameof(PixelFlashEffects.flashPixel),
            new[] { typeof(WorldTile), typeof(int), typeof(ColorType) });
        var count = 0;

        foreach (var code in list)
        {
            if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) &&
                code.operand is MethodInfo method && method == flash_pixel)
            {
                code.opcode = OpCodes.Call;
                code.operand = replacement;
                count++;
            }
        }

        if (count == 0)
        {
            throw new MissingMethodException(
                $"{patchName} could not find PixelFlashEffects.flashPixel in the current game source.");
        }

        return list;
    }

    private static void HideDrop(Drop drop)
    {
        if (!CustomGodToolsConfig.HideDrop || drop == null) return;
        drop.setScale(HiddenScale);
    }

    private static void FlashPixelUnlessDropHidden(PixelFlashEffects effects, WorldTile tile, int value,
        ColorType colorType)
    {
        if (CustomGodToolsConfig.HideDrop) return;
        effects.flashPixel(tile, value, colorType);
    }
}
