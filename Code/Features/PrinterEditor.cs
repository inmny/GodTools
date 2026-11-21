using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ai.behaviours;
using GodTools.Abstract;
using HarmonyLib;
using UnityEngine;

namespace GodTools.Features;

public class PrinterEditor : IManager
{
    private const string PlanKey = C.mod_prefix + ".printer.settings.v1";
    private const int MaxDimension = 1024;
    private const int MaxArea = 262144;

    internal enum Order { Default, InsideToOutside, OutsideToInside }
    internal enum Terrain { Default, Base, Top }

    internal struct Settings(float width, float height, Order direction, Terrain layer, string tile) : IEquatable<Settings>
    {
        internal static readonly Settings Default = new(1, 1, Order.Default, Terrain.Default, "");
        internal float Width = width;
        internal float Height = height;
        internal Order Direction = direction;
        internal Terrain Layer = layer;
        internal string Tile = tile;

        internal readonly bool NativeShape => Width == 1 && Height == 1 && Direction == Order.Default;
        private readonly (float, float, Order, Terrain, string) Values => (Width, Height, Direction, Layer, Tile);
        public readonly bool Equals(Settings other) => Values.Equals(other.Values);
        public override readonly bool Equals(object obj) => obj is Settings other && Equals(other);
        public override readonly int GetHashCode() => Values.GetHashCode();
        public static bool operator ==(Settings left, Settings right) => left.Equals(right);
        public static bool operator !=(Settings left, Settings right) => !left.Equals(right);
    }

    private sealed class Job
    {
        internal PrintTemplate Plan;
        internal TileTypeBase Tile;
    }

    private static ConditionalWeakTable<ActorData, Job> _jobs = new();
    private static readonly Dictionary<(PrintTemplate, Settings), PrintTemplate> Plans = new();
    internal static Settings Applied { get; private set; } = Settings.Default;

    public void Initialize() => Harmony.CreateAndPatchAll(typeof(PrinterEditor));

    internal static IEnumerable<PrintTemplate> Templates =>
        PrintLibrary._instance == null ? Enumerable.Empty<PrintTemplate>() :
        PrintLibrary._instance.list.Where(template => IsPrinterTemplate(template.name) && template.graphics != null);

    private static bool IsPrinterTemplate(string name) =>
        !string.IsNullOrEmpty(name) && !name.Contains("quake");

    [HarmonyPostfix, HarmonyPatch(typeof(ActorManager), nameof(ActorManager.spawnNewUnitByPlayer))]
    private static void RememberNewPrinter(Actor __result)
    {
        if (__result?.asset.id == "printer") WriteSettings(__result.data, Applied);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.currentWorldToSavedMap))]
    private static void SavePrinters(SavedMap __result)
    {
        // 原版跳过打印蚁。把尚未完成的普通打印任务按原版 ActorData 方式写入存档。
        foreach (var actor in World.world.units)
        {
            if (actor.asset.id != "printer" || !actor.asset.skip_save || !actor.isAlive()) continue;
            actor.data.get("template", out string template, null);
            if (!IsPrinterTemplate(template)) continue;
            actor.data.get("step", out int step, -1);
            actor.data.get("steps", out int length, -1);
            if (length >= 0 && step >= length) continue;
            actor.prepareForSave();
            __result.actors_data.Add(actor.data);
        }
    }

    internal static bool CanApply(Settings settings) => Templates.All(template => Fits(template, settings));

    internal static bool Apply(Settings settings)
    {
        if (!CanApply(settings)) return false;
        Applied = settings;
        return true;
    }

    private static bool ValidScale(float value) => value >= 0.1f && !float.IsInfinity(value);

    internal static bool TryScale(string value, out float scale) =>
        (float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out scale) ||
         float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale)) && ValidScale(scale);

    internal static TileTypeBase GetTile(Terrain layer, string id) => layer switch
    {
        Terrain.Base => AssetManager.tiles.getSimple(id),
        Terrain.Top => AssetManager.top_tiles.getSimple(id),
        _ => null
    };

    private static bool Fits(PrintTemplate template, Settings settings)
    {
        if (!ValidScale(settings.Width) || !ValidScale(settings.Height)) return false;
        if (settings.NativeShape) return true;
        double width = Math.Max(3, (double)template.graphics.width * settings.Width);
        double height = Math.Max(3, (double)template.graphics.height * settings.Height);
        return width <= MaxDimension && height <= MaxDimension && (int)width * (int)height <= MaxArea;
    }

    internal static PrintTemplate BuildPlan(PrintTemplate source, Settings settings)
    {
        if (!Fits(source, settings)) return null;
        if (settings.NativeShape) return source;

        int width = Math.Max(3, (int)(source.graphics.width * settings.Width));
        int height = Math.Max(3, (int)(source.graphics.height * settings.Height));
        Color32[] pixels = source.graphics.GetPixels32();
        var library = PrintLibrary._instance;
        Color32 empty = library._color_0;
        Color32 twice = library._color_2;
        Color32 thrice = library._color_3;
        var steps = new List<(PrintStep Step, int Ring, int Repeat)>();
        for (int x = 1; x < width - 1; x++)
        {
            int sampleX = Mathf.Min((int)(x / settings.Width), source.graphics.width - 1);
            for (int y = 1; y < height - 1; y++)
            {
                int sampleY = Mathf.Min((int)(y / settings.Height), source.graphics.height - 1);
                Color32 pixel = pixels[sampleX + sampleY * source.graphics.width];
                if (pixel.Equals(empty)) continue;
                var step = new PrintStep { x = x - 1 - width / 2, y = y - 1 - height / 2, action = 1 };
                int ring = Math.Max(Math.Abs(x - width / 2), Math.Abs(y - height / 2));
                int repeat = pixel.Equals(twice) ? 2 : pixel.Equals(thrice) ? 3 : 1;
                steps.Add((step, ring, repeat));
            }
        }

        var ordered = settings.Direction switch
        {
            Order.InsideToOutside => steps.OrderBy(step => step.Ring),
            Order.OutsideToInside => steps.OrderByDescending(step => step.Ring),
            _ => (IEnumerable<(PrintStep Step, int Ring, int Repeat)>)steps
        };
        var result = new List<PrintStep>(steps.Count);
        foreach (var item in ordered)
            for (int repeat = 0; repeat < item.Repeat; repeat++) result.Add(item.Step);

        return new PrintTemplate
        {
            name = source.name, graphics = source.graphics, steps = result.ToArray(),
            steps_per_tick = (int)(result.Count * 0.005f + 1f)
        };
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void ClearJobs()
    {
        _jobs = new ConditionalWeakTable<ActorData, Job>();
        Plans.Clear();
    }

    // 原版的启动和步进仍使用同一模板，模板从当前蚂蚁已保存的设置取得。
    [HarmonyTranspiler, HarmonyPatch(typeof(BehPrinterSetup), nameof(BehPrinterSetup.execute))]
    private static IEnumerable<CodeInstruction> RouteSetup(IEnumerable<CodeInstruction> instructions) =>
        RouteTemplate(instructions);

    [HarmonyTranspiler, HarmonyPatch(typeof(BehPrinterStep), nameof(BehPrinterStep.execute))]
    private static IEnumerable<CodeInstruction> RouteStep(IEnumerable<CodeInstruction> instructions) =>
        RouteTemplate(instructions);

    private static IEnumerable<CodeInstruction> RouteTemplate(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(PrintLibrary), nameof(PrintLibrary.getTemplate));
        var replacement = AccessTools.Method(typeof(PrinterEditor), nameof(TemplateFor));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                var actor = new CodeInstruction(OpCodes.Ldarg_1);
                actor.labels.AddRange(instruction.labels);
                actor.blocks.AddRange(instruction.blocks);
                yield return actor;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            else yield return instruction;
        }
    }

    private static PrintTemplate TemplateFor(string template, Actor actor)
    {
        var source = PrintLibrary.getTemplate(template);
        if (_jobs.TryGetValue(actor.data, out var current)) return current.Plan;

        actor.data.get(PlanKey, out string snapshot, null);
        actor.data.get("step", out int step, -1);
        if (snapshot == null && step >= 0) return source;

        var settings = snapshot == null ? Applied : ReadSettings(snapshot);
        if (!IsPrinterTemplate(template) || settings == Settings.Default || !Fits(source, settings)) return source;
        if (snapshot == null) WriteSettings(actor.data, settings);

        // 地块不会影响步骤尺寸和顺序，多个同样的打印任务可以共享步骤数组。
        var shape = settings with { Layer = Terrain.Default, Tile = "" };
        var key = (source, shape);
        if (!Plans.TryGetValue(key, out var plan))
        {
            if (Plans.Count >= 16) Plans.Clear();
            plan = BuildPlan(source, shape);
            Plans[key] = plan;
        }
        _jobs.Add(actor.data, new Job { Plan = plan, Tile = GetTile(settings.Layer, settings.Tile) });
        return plan;
    }

    private static void WriteSettings(ActorData data, Settings settings) =>
        data.set(PlanKey, FormattableString.Invariant(
            $"{settings.Width:R};{settings.Height:R};{(int)settings.Direction};{(int)settings.Layer};{settings.Tile}"));

    private static Settings ReadSettings(string snapshot)
    {
        var parts = snapshot.Split(new[] { ';' }, 5);
        if (parts.Length != 5 ||
            !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float width) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float height) ||
            !int.TryParse(parts[2], out int direction) || direction < 0 || direction > (int)Order.OutsideToInside ||
            !int.TryParse(parts[3], out int layer) || layer < 0 || layer > (int)Terrain.Top) return Settings.Default;
        return new Settings(width, height, (Order)direction, (Terrain)layer, parts[4]);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(BehPrinterStep), nameof(BehPrinterStep.printTile))]
    private static bool PrintTerrain(Actor pActor)
    {
        if (!_jobs.TryGetValue(pActor.data, out var job) || job.Tile == null || pActor.current_tile == null) return true;
        var tile = pActor.current_tile;
        MusicBox.playSound("event:/SFX/UNIQUE/PrinterStep", tile, false, false);
        var destroy = AssetManager.terraform.get("destroy");
        if (job.Tile is TileType ground) MapAction.terraformMain(tile, ground, destroy);
        else if (job.Tile is TopTileType overlay) MapAction.terraformTop(tile, overlay, destroy);
        BehaviourActionBase<Actor>.world.setTileDirty(tile);
        BehaviourActionBase<Actor>.world.conway_layer.remove(tile);
        return false;
    }
}
