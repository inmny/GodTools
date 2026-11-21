using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Features;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptCheckpoints
{
    private static string Root => Path.Combine(Application.persistentDataPath, "world_script_checkpoints");
    private static string Archive => Path.Combine(Root, "saved");
    private static string Work => Path.Combine(Root, "work");
    private static string SelectedEndsFile => Path.Combine(Root, "selected_endings.json");
    private static (string Path, Checkpoint Entry) _pendingView;

    internal sealed class Checkpoint
    {
        public string ID;
        public string Name;
        public string ScenarioID;
        public string ScenarioName;
        public string SceneName;
        public double Time;
        public int? X;
        public int? Y;
        public float Zoom;
        [JsonIgnore] public string Folder;
    }

    internal static IEnumerable<Checkpoint> List() => Directory.Exists(Archive)
        ? Directory.EnumerateDirectories(Archive).OrderByDescending(Path.GetFileName)
            .Select(Read).Where(item => item != null)
        : Enumerable.Empty<Checkpoint>();

    private static Checkpoint Read(string folder)
    {
        string path = Path.Combine(folder, "checkpoint.json");
        if (!File.Exists(path)) return null;
        try
        {
            var item = JsonConvert.DeserializeObject<Checkpoint>(File.ReadAllText(path));
            if (item == null || !Guid.TryParseExact(item.ID, "N", out _)) return null;
            item.Folder = folder;
            return item;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static Checkpoint Save(ScriptScenario scenario, ScriptScene scene, string name, ScriptSceneMark mark)
    {
        if (World.world?.map_stats == null || SmoothLoader.isLoading())
            throw new InvalidOperationException("No world is ready");
        var camera = World.world.move_camera;
        var position = camera.transform.position;
        var entry = new Checkpoint
        {
            ID = Guid.NewGuid().ToString("N"), Name = name, ScenarioID = scenario.ID, ScenarioName = scenario.Name,
            SceneName = scene?.Name, Time = World.world.getCurWorldTime(),
            X = mark?.X ?? (int)Mathf.Clamp(Mathf.Round(position.x), 0, MapBox.width - 1),
            Y = mark?.Y ?? (int)Mathf.Clamp(Mathf.Round(position.y), 0, MapBox.height - 1), Zoom = camera.getTargetZoom()
        };
        entry.Folder = Path.Combine(Archive, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + entry.ID);
        string seed = Path.Combine(entry.Folder, "map");
        Directory.CreateDirectory(seed);
        SaveManager.saveMapData(seed);
        ValidateSeed(seed, entry);
        WriteJson(Path.Combine(entry.Folder, "checkpoint.json"), entry);
        return entry;
    }

    private static void ValidateSeed(string seed, Checkpoint entry)
    {
        if (!File.Exists(Path.Combine(seed, "map.wbox"))) throw new IOException("Missing checkpoint map");
        if (!WorldScripts.ReadSnapshot(seed).Scenarios.Any(scenario => scenario.ID == entry.ScenarioID))
            throw new InvalidDataException("Missing checkpoint scenario");
    }

    internal static void Open(Checkpoint entry)
    {
        if (World.world == null || SmoothLoader.isLoading()) throw new InvalidOperationException("No world is ready");
        string seed = Path.Combine(entry.Folder, "map");
        ValidateSeed(seed, entry);
        string destination = Path.Combine(Work, Guid.NewGuid().ToString("N"));
        WorldScriptRehearsals.CopyFolder(seed, destination);
        _pendingView = (destination, entry);
        Config.paused = true;
        SaveManager.setCurrentPathAndId(destination, -1);
        World.world.save_manager.loadWorld(destination);
    }

    internal static bool IsWorkingCopy(string path) => path != null &&
        string.Equals(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)), Work, StringComparison.Ordinal);

    internal static void RestoreView(string path)
    {
        var (expected, entry) = _pendingView;
        _pendingView = default;
        if (entry == null || !string.Equals(expected, path?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)) return;
        WorldScriptScenes.Locate(entry.X, entry.Y, entry.Zoom);
        World.world.move_camera.skipResetZoom();
    }

    internal static Dictionary<string, string> SelectedEndings() => File.Exists(SelectedEndsFile)
        ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(SelectedEndsFile)) ?? new()
        : new();

    internal static void SelectEnding(Checkpoint entry)
    {
        ValidateSeed(Path.Combine(entry.Folder, "map"), entry);
        var endings = SelectedEndings();
        endings[entry.ScenarioID] = entry.ID;
        WriteJson(SelectedEndsFile, endings);
    }

    private static void WriteJson(string path, object data)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(data, Formatting.Indented));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
