using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptPackages
{
    internal static string DirectoryPath => Path.Combine(Application.persistentDataPath, "world_script_packages");

    private sealed class Package
    {
        public int Version = 1;
        public List<ScriptInstance> Scripts;
        public string PackageID;
        public int Revision;
        public string ScenarioName;
        public Dictionary<string, int> Parameters;
        public List<string> PublicParameters;
        public List<string> Dependencies;
    }

    internal sealed class Import
    {
        public List<ScriptInstance> Scripts;
        public ScriptScenario Scenario;
        public ScriptScenario Previous;
        public List<string> PublicParameters = new();
        public List<string> MissingDefinitions = new();
        public List<ScriptInstance> Removed = new();
        public Dictionary<string, ScriptInstance> Replacements = new();
    }

    internal static IEnumerable<string> List() => Directory.Exists(DirectoryPath)
        ? Directory.EnumerateFiles(DirectoryPath, "*.json").OrderBy(Path.GetFileName)
        : Enumerable.Empty<string>();

    internal static string Save(string name, IEnumerable<ScriptInstance> scripts, ScriptScenario scenario = null,
        int revision = 1, IEnumerable<string> publicParameters = null)
    {
        string baseName = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(baseName)) throw new ArgumentException("Enter a package name");
        if (baseName.Length > 80) baseName = baseName.Substring(0, 80);
        var sources = scripts.ToList();
        if (sources.Count == 0) throw new ArgumentException("Choose scripts to include");
        var package = new Package
        {
            Scripts = sources.Select(script => WorldScriptTemplates.Prepare(script, preserveID: true)).ToList()
        };
        if (scenario != null)
        {
            package.Version = 2;
            package.PackageID = scenario.PackageID ?? scenario.ID;
            package.Revision = Math.Max(1, revision);
            package.ScenarioName = scenario.Name;
            package.Parameters = new Dictionary<string, int>(scenario.Parameters);
            package.PublicParameters = (publicParameters ?? scenario.Parameters.Keys).Where(scenario.Parameters.ContainsKey).ToList();
            package.Dependencies = sources.SelectMany(script =>
                WorldScriptTemplates.EditableFields(script).Select(field => field.Definition)
                    .Append(script.TriggerCondition?.TriggerID))
                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().OrderBy(id => id).ToList();

            var mapping = sources.ToDictionary(script => script.ID, script => script.PackageScriptID ?? script.ID);
            foreach (var copy in package.Scripts)
            {
                copy.ID = mapping[copy.ID];
                RemapFollowUps(copy, mapping);
            }
        }

        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, baseName + ".json");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(DirectoryPath, $"{baseName} ({i}).json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(package, Formatting.Indented));
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return path;
    }

    internal static Import Load(string path)
    {
        var package = JsonConvert.DeserializeObject<Package>(File.ReadAllText(path));
        if (package?.Version is not (1 or 2) || package.Scripts?.Count is not > 0 ||
            package.Scripts.Any(script => string.IsNullOrWhiteSpace(script?.ID)) ||
            package.Scripts.Select(script => script.ID).Distinct().Count() != package.Scripts.Count ||
            package.Version == 2 && (string.IsNullOrWhiteSpace(package.PackageID) ||
                string.IsNullOrWhiteSpace(package.ScenarioName) || package.Revision < 1))
            throw new InvalidDataException("Invalid or unsupported WorldScript package");

        WorldScripts.Normalize(package.Scripts);
        var result = new Import { Scripts = package.Scripts.Select(WorldScriptTemplates.Instantiate).ToList() };
        if (package.Version == 2)
        {
            var choices = WorldScripts.Scenarios.Where(scenario => scenario.PackageID == package.PackageID).ToArray();
            if (choices.Length == 1 && choices[0].PackageRevision < package.Revision) result.Previous = choices[0];
            var previous = result.Previous;
            var scenario = result.Scenario = new ScriptScenario
            {
                ID = previous?.ID ?? Guid.NewGuid().ToString("N"),
                Name = previous?.Name ?? package.ScenarioName,
                PackageID = package.PackageID, PackageRevision = package.Revision,
                Parameters = package.Parameters ?? new Dictionary<string, int>()
            };
            result.PublicParameters = (package.PublicParameters ?? scenario.Parameters.Keys.ToList())
                .Where(scenario.Parameters.ContainsKey).Distinct().ToList();
            result.MissingDefinitions = (package.Dependencies ?? new List<string>())
                .Where(id => WorldScriptRegistry.GetAction(id) == null && WorldScriptRegistry.GetCondition(id) == null &&
                    WorldScriptRegistry.GetTarget(id) == null && WorldScriptRegistry.GetTrigger(id) == null).Distinct().ToList();
            if (previous != null)
                foreach (var entry in previous.Parameters) scenario.Parameters[entry.Key] = entry.Value;

            var oldScripts = previous == null ? new List<ScriptInstance>() : WorldScripts.Scripts
                .Where(script => script.ScenarioID == previous.ID && script.PackageScriptID != null).ToList();
            for (int i = 0; i < result.Scripts.Count; i++)
            {
                var copy = result.Scripts[i];
                copy.PackageScriptID = package.Scripts[i].ID;
                copy.ScenarioID = scenario.ID;
                var old = oldScripts.Find(script => script.PackageScriptID == copy.PackageScriptID);
                if (old == null) continue;
                copy.ID = old.ID;
                WorldScriptTemplates.RestorePackageBindings(old, copy);
                result.Replacements.Add(copy.ID, old);
            }
            result.Removed = oldScripts.Where(script => !result.Replacements.ContainsKey(script.ID)).ToList();
        }

        var ids = package.Scripts.Zip(result.Scripts, (original, copy) => (original.ID, NewID: copy.ID))
            .ToDictionary(item => item.ID, item => item.NewID);
        foreach (var copy in result.Scripts) RemapFollowUps(copy, ids);
        return result;
    }

    internal static void Install(Import import)
    {
        if (import.Previous == null)
        {
            WorldScripts.AddPackage(import.Scripts, import.Scenario);
            return;
        }

        var scenario = import.Previous;
        if (!WorldScripts.Scenarios.Contains(scenario) || scenario.PackageRevision >= import.Scenario.PackageRevision ||
            import.Replacements.Values.Any(script => !WorldScripts.Scripts.Contains(script)))
            throw new InvalidOperationException("The installed scenario changed. Open the import again.");

        // Retain stable script identities and the live runtime objects they own.
        WorldScripts.Normalize(import.Scripts);
        foreach (var draft in import.Scripts)
        {
            if (import.Replacements.TryGetValue(draft.ID, out var original))
            {
                draft.Runtime = original.Runtime;
                WorldScripts.Scripts[WorldScripts.Scripts.IndexOf(original)] = draft;
            }
            else WorldScripts.Scripts.Add(draft);
        }
        foreach (var script in import.Removed) script.Enabled = false;
        scenario.PackageRevision = import.Scenario.PackageRevision;
        scenario.Parameters = import.Scenario.Parameters;
        WorldScripts.NotifyChanged();
    }

    private static void RemapFollowUps(ScriptInstance script, Dictionary<string, string> ids)
    {
        foreach (var field in WorldScriptTemplates.EditableFields(script).Where(field => field.Definition == "script.schedule"))
        {
            string id = WorldScriptRegistry.GetString(field.Parameters, "script_id");
            field.Parameters["script_id"] = ids.GetValueOrDefault(id, "");
        }
    }
}
