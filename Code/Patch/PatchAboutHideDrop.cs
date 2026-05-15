using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace GodTools.Patch
{
    internal static class PatchAboutHideDrop
    {
        [HarmonyTranspiler, HarmonyPatch(typeof(PowerLibrary), nameof(PowerLibrary.spawnDrops))]
        private static IEnumerable<CodeInstruction> PowerLibrary_spawnDrops_transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var index = list.FindIndex(x => (x.opcode == OpCodes.Callvirt) && (x.operand as MethodInfo).Name == nameof(DropManager.spawn));
            list[index + 1].opcode = OpCodes.Call;
            list[index + 1].operand = AccessTools.Method(typeof(PatchAboutHideDrop), nameof(HideDrop));
            list[index + 2].opcode = OpCodes.Nop;

            return list;
        }
        [HarmonyTranspiler, HarmonyPatch(typeof(DropManager), nameof(DropManager.landDrop))]
        private static IEnumerable<CodeInstruction> DropManager_landDrop_transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var index = list.FindIndex(x => (x.opcode == OpCodes.Call) && (x.operand as MethodInfo).Name == "get_world");
            var label = list[index].labels[0];
            var new_label = new Label();
            var old_code = list[index];
            list.InsertRange(index, new[] { 
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(CustomGodToolsConfig), nameof(CustomGodToolsConfig.HideDrop))), 
                new CodeInstruction(OpCodes.Brfalse_S, new_label),
                new CodeInstruction(OpCodes.Ret)
            });
            old_code.MoveLabelsTo(list[index]);
            old_code.labels.Add(new_label);
            return list;
        }
        [HarmonyPrefix, HarmonyPatch(typeof(DropsLibrary), nameof(DropsLibrary.flash))]
        private static bool DropsLibrary_flash_prefix()
        {
            return !CustomGodToolsConfig.HideDrop;
        }
        private static void HideDrop(Drop drop)
        {
            if (CustomGodToolsConfig.HideDrop)
            {
                drop.setScale(new Vector3(0.0001f, 0.0001f, 0.0001f));
            }
        }
    }
}