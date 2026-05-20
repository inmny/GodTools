using System;
using System.Collections.Generic;
using System.Linq;
using ai.behaviours;
using GodTools.Abstract;
using GodTools.UI;
using HarmonyLib;
using UnityEngine;

namespace GodTools.Features;

public class PrinterEditor : IManager
{
    public void Initialize()
    {
        Harmony.CreateAndPatchAll(typeof(PrinterEditor));
    }
    [HarmonyPrefix, HarmonyPatch(typeof(BehPrinterStep), nameof(BehPrinterStep.printTile))]
    private static bool BehPrinterStep_printTile(Actor pActor)
    {
        if (WindowPrinterEditor.SelectedTileType == null) return true;

        var tile = pActor.current_tile;
        if (tile == null) return true;

        MusicBox.playSound("event:/SFX/UNIQUE/PrinterStep", tile, false, false);
        var destroy = AssetManager.terraform.get("destroy");
        switch (WindowPrinterEditor.SelectedTileType)
        {
            case TileType tile_type:
                MapAction.terraformMain(tile, tile_type, destroy);
                break;
            case TopTileType top_tile_type:
                MapAction.terraformTop(tile, top_tile_type, destroy);
                break;
            default:
                return true;
        }
        BehaviourActionBase<Actor>.world.setTileDirty(tile);
        BehaviourActionBase<Actor>.world.conway_layer.remove(tile);
        return false;
    }

    internal static void RecalcAllPrintSteps(WindowPrinterEditor.PrintDirection direction)
    {
        foreach (var template in PrintLibrary._instance.list)
        {
            if (!template.name.Contains("quake"))
            {
                switch (direction)
                {
                    case WindowPrinterEditor.PrintDirection.Default:
                        CalcSteps(template, StepOrder.Default);
                        break;
                    case WindowPrinterEditor.PrintDirection.InsideToOutside:
                        CalcSteps(template, StepOrder.InsideToOutside);
                        break;
                    case WindowPrinterEditor.PrintDirection.OutsideToInside:
                        CalcSteps(template, StepOrder.OutsideToInside);
                        break;
                }
            }
        }
    }

    private static void CalcSteps(PrintTemplate template, StepOrder order)
    {
        var print_library = PrintLibrary._instance;
        List<OrderedPrintStep> list = new List<OrderedPrintStep>();
        int width = Math.Max(3, (int)(template.graphics.width * WindowPrinterEditor.WidthScale));
        int height = Math.Max(3, (int)(template.graphics.height * WindowPrinterEditor.HeightScale));
        int center_x = width / 2;
        int center_y = height / 2;

        for (int i = 1; i < width - 1; i++)
        {
            for (int j = 1; j < height - 1; j++)
            {
                var ii = (int)(i / WindowPrinterEditor.WidthScale);
                var jj = (int)(j / WindowPrinterEditor.HeightScale);
                ii = Math.Min(ii, template.graphics.width - 1);
                jj = Math.Min(jj, template.graphics.height - 1);
                Color pixel = template.graphics.GetPixel(ii, jj);
                if (pixel == print_library._color_0) continue;

                PrintStep print_step = new PrintStep
                {
                    x = i - 1 - width / 2,
                    y = j - 1 - height / 2,
                    action = 1
                };

                int ring = Math.Max(Math.Abs(i - center_x), Math.Abs(j - center_y));
                AddWeightedStep(list, print_step, pixel, print_library, ring);
            }
        }

        if (order != StepOrder.Default)
        {
            list.Sort((a, b) =>
            {
                int result = order == StepOrder.InsideToOutside
                    ? a.Ring.CompareTo(b.Ring)
                    : b.Ring.CompareTo(a.Ring);
                return result != 0 ? result : a.Sequence.CompareTo(b.Sequence);
            });
        }

        template.steps = list.Select(x => x.Step).ToArray();
        template.steps_per_tick = (int)((float)template.steps.Length * 0.005f + 1f);
    }

    private static void AddWeightedStep(List<OrderedPrintStep> list, PrintStep step, Color pixel,
        PrintLibrary printLibrary, int ring)
    {
        int repeat = 1;
        if (pixel == printLibrary._color_2)
        {
            repeat = 2;
        }
        else if (pixel == printLibrary._color_3)
        {
            repeat = 3;
        }

        for (int i = 0; i < repeat; i++)
        {
            list.Add(new OrderedPrintStep
            {
                Step = step,
                Ring = ring,
                Sequence = list.Count
            });
        }
    }

    private enum StepOrder
    {
        Default,
        InsideToOutside,
        OutsideToInside
    }

    private struct OrderedPrintStep
    {
        public PrintStep Step;
        public int Ring;
        public int Sequence;
    }
}
