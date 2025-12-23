using System;
using System.Collections.Generic;
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
        
        
        MusicBox.playSound("event:/SFX/UNIQUE/PrinterStep", pActor.currentTile, false, false);
        if (WindowPrinterEditor.SelectedTileType is TileType tile_type)
            MapAction.terraformMain(pActor.currentTile, tile_type, AssetManager.terraform.get("destroy"));
        else if (WindowPrinterEditor.SelectedTileType is TopTileType top_tile_type)
            MapAction.terraformTop(pActor.currentTile, top_tile_type, AssetManager.terraform.get("destroy"));
        BehaviourActionBase<Actor>.world.setTileDirty(pActor.currentTile);
        BehaviourActionBase<Actor>.world.conwayLayer.remove(pActor.currentTile);
        return false;
    }

    internal static void RecalcAllPrintSteps(WindowPrinterEditor.PrintDirection direction)
    {
        foreach (var template in World.world.printLibrary.list)
        {
            if (!template.name.Contains("quake"))
            {
                switch (direction)
                {
                    case WindowPrinterEditor.PrintDirection.Default:
                        calcSteps(template);
                        break;
                    case WindowPrinterEditor.PrintDirection.InsideToOutside:
                        calcI2OSteps(template);
                        break;
                    case WindowPrinterEditor.PrintDirection.OutsideToInside:
                        calcO2ISteps(template);
                        break;
                }
            }
        }
    }

    private static void calcSteps(PrintTemplate template)
    {
        var print_library = World.world.printLibrary;
        List<PrintStep> list = new List<PrintStep>();
        int width = (int)(template.graphics.width * WindowPrinterEditor.WidthScale);
        int height = (int)(template.graphics.height * WindowPrinterEditor.HeightScale);
        for (int i = 1; i < width - 1; i++)
        {
            for (int j = 1; j < height - 1; j++)
            {
                var ii = (int)(i / WindowPrinterEditor.WidthScale);
                var jj = (int)(j / WindowPrinterEditor.HeightScale);
                ii = Math.Min(ii, template.graphics.width - 1);
                jj = Math.Min(jj, template.graphics.height - 1);
                Color pixel = template.graphics.GetPixel(ii, jj);
                if (pixel == print_library.color0) continue;
                PrintStep print_step = new PrintStep
                {
                    x = i - 1 - width / 2,
                    y = j - 1 - height / 2,
                    action = 1
                };
                list.Add(print_step);
                if (pixel == print_library.color2)
                {
                    list.Add(print_step);
                }
                else if (pixel == print_library.color3)
                {
                    list.Add(print_step);
                    list.Add(print_step);
                }
            }
        }
        template.steps = list.ToArray();
        template.stepsPerTick = (int)((float)template.steps.Length * 0.005f + 1f);
    }
    private static void calcI2OSteps(PrintTemplate template)
    {
        // 由内向外绘制，从中心开始，向外一圈一圈遍历
        var print_library = World.world.printLibrary;
        List<PrintStep> list = new List<PrintStep>();
        int width = (int)(template.graphics.width * WindowPrinterEditor.WidthScale);
        int height = (int)(template.graphics.height * WindowPrinterEditor.HeightScale);
        int centerX = width / 2;
        int centerY = height / 2;
        int maxRadius = Math.Max(centerX, centerY);

        // 从里到外遍历每一层圆环
        for (int r = 0; r <= maxRadius; r++)
        {
            // 为了避免重复，记录本圈访问过的点
            HashSet<(int, int)> visited = new HashSet<(int, int)>();
            // 遍历所有点
            for (int i = 1; i < width - 1; i++)
            {
                for (int j = 1; j < height - 1; j++)
                {
                    int dx = i - centerX;
                    int dy = j - centerY;
                    int ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (ring != r) continue;
                    if (visited.Contains((i, j))) continue;
                    visited.Add((i, j));

                    var ii = (int)(i / WindowPrinterEditor.WidthScale);
                    var jj = (int)(j / WindowPrinterEditor.HeightScale);
                    ii = Math.Min(ii, template.graphics.width - 1);
                    jj = Math.Min(jj, template.graphics.height - 1);
                    Color pixel = template.graphics.GetPixel(ii, jj);
                    if (pixel == print_library.color0) continue;
                    PrintStep print_step = new PrintStep
                    {
                        x = i - 1 - width / 2,
                        y = j - 1 - height / 2,
                        action = 1
                    };
                    list.Add(print_step);
                    if (pixel == print_library.color2)
                    {
                        list.Add(print_step);
                    }
                    else if (pixel == print_library.color3)
                    {
                        list.Add(print_step);
                        list.Add(print_step);
                    }
                }
            }
        }
        template.steps = list.ToArray();
        template.stepsPerTick = (int)((float)template.steps.Length * 0.005f + 1f);
    }

    private static void calcO2ISteps(PrintTemplate template)
    {
    }
}