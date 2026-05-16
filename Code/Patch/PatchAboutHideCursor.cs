using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace GodTools.Patch;

internal static class PatchAboutHideCursor
{
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerControl), "highlightCursor")]
    private static bool PlayerControl_highlightCursor_prefix()
    {
        return !CustomGodToolsConfig.HideCursor;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(QuantumSpriteLibrary), "drawCursorSprite")]
    private static bool QuantumSpriteLibrary_drawCursorSprite_prefix()
    {
        return !CustomGodToolsConfig.HideCursor;
    }
    [HarmonyPrefix, HarmonyPatch(typeof(PowerLibrary), "drawingCursorEffect")]
    private static bool PowerLibrary_drawingCursorEffect()
    {
        return !CustomGodToolsConfig.HideCursor;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PowerLibrary), "flashPixel", new[] { typeof(WorldTile), typeof(string) })]
    private static bool PowerLibrary_flashPixel_prefix_1()
    {
        return !CustomGodToolsConfig.HideCursor;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PowerLibrary), "flashPixel", new[] { typeof(WorldTile), typeof(GodPower) })]
    private static bool PowerLibrary_flashPixel_prefix_2()
    {
        return !CustomGodToolsConfig.HideCursor;
    }

    [HarmonyTranspiler, HarmonyPatch(typeof(PowerLibrary), "drawTiles")]
    private static IEnumerable<CodeInstruction> PowerLibrary_drawTiles_transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        return ReplaceFlashPixelCall(
            instructions,
            AccessTools.Method(typeof(PatchAboutHideCursor), nameof(FlashPixelUnlessCursorHidden)),
            nameof(PowerLibrary_drawTiles_transpiler));
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

    private static void FlashPixelUnlessCursorHidden(PixelFlashEffects effects, WorldTile tile, int value,
        ColorType colorType)
    {
        if (CustomGodToolsConfig.HideCursor) return;
        effects.flashPixel(tile, value, colorType);
    }
}
