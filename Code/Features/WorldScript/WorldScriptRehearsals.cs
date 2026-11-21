using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptRehearsals
{
    private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "world_script_rehearsals");

    internal sealed class Case
    {
        public string Name;
        public double StartTime;
        public string[] Resources;
        public string[] Scripts;
        [JsonIgnore] public string Folder;
    }

    internal sealed class Result
    {
        public double Time;
        public List<ScriptResult> Scripts = new();
        public List<InventoryResult> Inventory = new();
    }

    internal sealed class ScriptResult
    {
        public string ID;
        public string Name;
        public int Targets;
        public int Pending;
        public string Outcome;
        public List<ScriptTargetSample> Samples;
    }

    internal sealed class InventoryResult
    {
        public long City;
        public string Name;
        public string Resource;
        public int Amount;
    }

    internal static IEnumerable<Case> List() => Directory.Exists(DirectoryPath)
        ? Directory.EnumerateDirectories(DirectoryPath).OrderByDescending(Path.GetFileName).Select(TryReadCase).Where(item => item != null)
        : Enumerable.Empty<Case>();

    private static Case TryReadCase(string path)
    {
        string manifest = Path.Combine(path, "case.json");
        if (!File.Exists(manifest)) return null;
        try
        {
            var item = JsonConvert.DeserializeObject<Case>(File.ReadAllText(manifest));
            if (item == null) return null;
            item.Folder = path;
            return item;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    internal static Case Create(string name, IEnumerable<ScriptInstance> scriptDrafts,
        ScriptScenario scenario = null, IEnumerable<string> disabled = null)
    {
        if (World.world == null || SmoothLoader.isLoading()) throw new InvalidOperationException("No world is ready");
        var drafts = scriptDrafts.ToDictionary(script => script.ID);
        var originals = WorldScripts.Scripts.Where(script => drafts.ContainsKey(script.ID)).ToArray();
        var entry = new Case
        {
            Name = name, StartTime = World.world.getCurWorldTime(), Scripts = drafts.Keys.ToArray(),
            Folder = Path.Combine(DirectoryPath, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")),
            Resources = originals.Concat(drafts.Values)
                .SelectMany(WorldScriptTemplates.EditableFields)
                .Select(field => WorldScriptRegistry.GetString(field.Parameters, "resource_id"))
                .Where(id => !string.IsNullOrEmpty(id) && AssetManager.resources.dict.ContainsKey(id)).Distinct().ToArray()
        };

        string baseline = Seed(entry, false);
        string variant = Seed(entry, true);
        Directory.CreateDirectory(baseline);
        SaveManager.saveMapData(baseline);
        if (!File.Exists(Path.Combine(baseline, "map.wbox"))) throw new IOException("Could not save rehearsal map");
        var source = WorldScripts.ReadSnapshot(baseline);
        foreach (var script in source.Scripts) script.Runtime.History.Clear();
        WorldScripts.WriteSnapshot(baseline, source);
        CopyFolder(baseline, variant);

        var changed = WorldScripts.ReadSnapshot(variant);
        foreach (var item in drafts.Values)
        {
            var copy = WorldScripts.Clone(item);
            int index = changed.Scripts.FindIndex(script => script.ID == copy.ID);
            copy.Runtime = index >= 0 ? changed.Scripts[index].Runtime : new ScriptRuntimeState();
            // A changed rule uses the same active state as its baseline counterpart inside the copy.
            copy.Enabled = index >= 0 ? changed.Scripts[index].Enabled : item.Enabled;
            if (index >= 0) changed.Scripts[index] = copy;
            else changed.Scripts.Add(copy);
        }
        if (disabled != null)
        {
            var ids = new HashSet<string>(disabled);
            foreach (var script in changed.Scripts.Where(script => ids.Contains(script.ID))) script.Enabled = false;
        }
        if (scenario != null)
        {
            var installed = changed.Scenarios.Find(item => item.ID == scenario.ID);
            if (installed != null)
            {
                installed.Parameters = new Dictionary<string, int>(scenario.Parameters);
                installed.PackageRevision = scenario.PackageRevision;
            }
        }
        WorldScripts.Normalize(changed.Scripts);
        WorldScripts.WriteSnapshot(variant, changed);
        File.WriteAllText(Path.Combine(entry.Folder, "case.json"), JsonConvert.SerializeObject(entry, Formatting.Indented));
        return entry;
    }

    internal static void Open(Case entry, bool draft)
    {
        string work = Path.Combine(entry.Folder, (draft ? "work-draft-" : "work-saved-") + Guid.NewGuid().ToString("N"));
        CopyFolder(Seed(entry, draft), work);
        Config.paused = true;
        SaveManager.setCurrentPathAndId(work, -1);
        World.world.save_manager.loadWorld(work);
    }

    internal static bool? ActiveBranch(Case entry)
    {
        string path = WorldScripts.ActiveSaveFolder;
        if (string.IsNullOrEmpty(path) || SmoothLoader.isLoading()) return null;
        var active = new DirectoryInfo(path);
        if (!string.Equals(active.Parent?.FullName, entry.Folder, StringComparison.Ordinal)) return null;
        if (active.Name.StartsWith("work-draft-", StringComparison.Ordinal)) return true;
        if (active.Name.StartsWith("work-saved-", StringComparison.Ordinal)) return false;
        return null;
    }

    internal static void Capture(Case entry)
    {
        bool draft = ActiveBranch(entry) ?? throw new InvalidOperationException("Open the rehearsal copy first");
        var ids = new HashSet<string>(entry.Scripts);
        var result = new Result { Time = World.world.getCurWorldTime() };
        foreach (var script in WorldScripts.Scripts.Where(script => ids.Contains(script.ID)))
        {
            var records = script.Runtime.History;
            result.Scripts.Add(new ScriptResult
            {
                ID = script.ID, Name = script.Name, Targets = records.Sum(record => record.TargetCount),
                Pending = (script.Runtime.DelayedRuns?.Count ?? 0) + (script.Runtime.Batch != null ? 1 : 0),
                Outcome = records.LastOrDefault()?.Outcome,
                Samples = records.SelectMany(record => record.Steps ?? Enumerable.Empty<ScriptStepExecution>())
                    .SelectMany(Samples).GroupBy(sample => (sample.Kind, sample.MetaID, sample.ActorID, sample.X, sample.Y))
                    .Select(group => group.First()).Take(16).ToList()
            });
        }
        foreach (var city in World.world.cities.Where(city => city.isAlive()))
            foreach (string resource in entry.Resources ?? Array.Empty<string>())
                result.Inventory.Add(new InventoryResult { City = city.id, Name = city.name,
                    Resource = resource, Amount = city.getResourcesAmount(resource) });
        string path = ResultPath(entry, draft);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(result, Formatting.Indented));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);

        static IEnumerable<ScriptTargetSample> Samples(ScriptStepExecution step) => step == null ? Enumerable.Empty<ScriptTargetSample>()
            : (step.Targets ?? Enumerable.Empty<ScriptTargetSample>())
                .Concat((step.Groups ?? Enumerable.Empty<ScriptStepExecution>()).SelectMany(Samples));
    }

    internal static Result ReadResult(Case entry, bool draft)
    {
        string path = ResultPath(entry, draft);
        return File.Exists(path) ? JsonConvert.DeserializeObject<Result>(File.ReadAllText(path)) : null;
    }

    private static string Seed(Case entry, bool draft) => Path.Combine(entry.Folder, draft ? "draft" : "saved");
    private static string ResultPath(Case entry, bool draft) => Path.Combine(entry.Folder, draft ? "draft-result.json" : "saved-result.json");

    internal static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string folder in Directory.EnumerateDirectories(source))
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }
}
