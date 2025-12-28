using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;

namespace GodTools.Patch
{
    internal static class PatchAboutHideCursor
    {
        [HarmonyPrefix, HarmonyPatch(typeof(MapBox), nameof(MapBox.highlightCursor))]
        private static bool highlightCursor_prefix()
        {
            return !CustomGodToolsConfig.HideCursor;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(MapIconLibrary), nameof(MapIconLibrary.drawCursorSprite))]
        private static bool drawCursorSprite_prefix()
        {
            return !CustomGodToolsConfig.HideCursor;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(PowerLibrary), nameof(PowerLibrary.flashPixel), new[] { typeof(WorldTile), typeof(string) })]
        private static bool PowerLibrary_flashPixel_prefix_1()
        {
            return !CustomGodToolsConfig.HideCursor;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(PowerLibrary), nameof(PowerLibrary.flashPixel), new[] { typeof(WorldTile), typeof(GodPower) })]
        private static bool PowerLibrary_flashPixel_prefix_2()
        {
            return !CustomGodToolsConfig.HideCursor;
        }
        [HarmonyTranspiler, HarmonyPatch(typeof(PowerLibrary), nameof(PowerLibrary.drawTiles))]
        private static IEnumerable<CodeInstruction> PowerLibrary_drawTiles_transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var index = list.FindIndex(x => (x.opcode == OpCodes.Call) && (x.operand as MethodInfo).Name == "get_world");

            var jump_index = list.FindIndex(x => (x.opcode == OpCodes.Callvirt) && (x.operand as MethodInfo).Name == "flashPixel");
            var label = new Label();
            list[jump_index].labels.Add(label);

            list.InsertRange(index, new[] { 
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(CustomGodToolsConfig), nameof(CustomGodToolsConfig.HideCursor))), 
                new CodeInstruction(OpCodes.Brfalse_S, label)
            });

            return list;
        }
    }
}