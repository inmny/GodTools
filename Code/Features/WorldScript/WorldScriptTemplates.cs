using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptTemplates
{
    private const string BindingKey = "$template_affiliation";
    private const string BoundKey = "$template_bound_affiliation";
    private const int Version = 1;
    private static string FilePath => Path.Combine(Application.persistentDataPath, "world_script_templates.json");

    private sealed class Document
    {
        public int schemaVersion;
        public List<ScriptInstance> Templates = new();
    }

    internal sealed class Binding
    {
        public string Kind;
        public string Name;
        public string SourceID;
        public JObject Parameters;
    }

    internal static IReadOnlyList<ScriptInstance> List() => Read().Templates;

    internal static string Save(ScriptInstance draft)
    {
        var document = Read();
        ScriptInstance template = Prepare(draft);
        string baseName = template.Name.Trim();
        var names = new HashSet<string>(document.Templates.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        template.Name = baseName;
        for (int number = 2; names.Contains(template.Name); number++)
            template.Name = $"{baseName} ({number})";
        document.Templates.Add(template);
        string path = FilePath;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(document, Formatting.Indented));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return template.Name;
    }

    internal static ScriptInstance Prepare(ScriptInstance draft, bool preserveID = false)
    {
        var template = NewDraft(draft);
        if (preserveID) template.ID = draft.ID;
        foreach (var (definition, parameters) in AffiliationFields(template))
        {
            if (preserveID && parameters[BoundKey] is JObject origin)
            {
                parameters[BindingKey] = origin.DeepClone();
                parameters.Remove(BoundKey);
                parameters["id"] = "";
                continue;
            }
            if (HasBinding(parameters) ||
                !long.TryParse(WorldScriptRegistry.GetString(parameters, "id"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long sourceID) ||
                sourceID == -1 && WorldScriptAffiliations.CanClear(definition)) continue;
            string kind = WorldScriptAffiliations.Kind(definition);
            string original = WorldScriptAffiliations.Name(kind, sourceID);
            if (sourceID == -1 && original == null) continue;
            parameters[BindingKey] = new JObject
            {
                ["name"] = original, ["source_id"] = sourceID.ToString(CultureInfo.InvariantCulture)
            };
            parameters["id"] = "";
        }
        return template;
    }

    internal static ScriptInstance Instantiate(ScriptInstance template)
    {
        ScriptInstance script = NewDraft(template);
        var bindings = Pending(script).Where(binding => !string.IsNullOrWhiteSpace(binding.Name));
        var nameGroups = bindings.GroupBy(binding => string.Concat(binding.Kind, "\n", binding.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var named in nameGroups)
        {
            if (named.Select(binding => binding.SourceID).Distinct(StringComparer.Ordinal).Skip(1).Any()) continue;
            Binding reference = named.First();
            var candidates = WorldScriptAffiliations.Named(reference.Kind)
                .Where(item => string.Equals(item.Name?.Trim(), reference.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (candidates.Length != 1) continue;
            string id = candidates[0].ID.ToString(CultureInfo.InvariantCulture);
            foreach (Binding binding in named) Bind(binding.Parameters, id);
        }
        return script;
    }

    internal static bool HasBinding(JObject parameters) => parameters?[BindingKey] is JObject;

    internal static void Bind(JObject parameters, string id)
    {
        if (parameters[BindingKey] is JObject source) parameters[BoundKey] = source.DeepClone();
        parameters["id"] = id;
        parameters.Remove(BindingKey);
    }

    internal static void RestorePackageBindings(ScriptInstance previous, ScriptInstance incoming)
    {
        var prior = AffiliationFields(previous).Where(field => field.Parameters[BoundKey] is JObject)
            .GroupBy(field => Key(field.Definition, (JObject)field.Parameters[BoundKey]))
            .ToDictionary(group => group.Key, group => group.Select(field => WorldScriptRegistry.GetString(field.Parameters, "id"))
                .Distinct().ToArray());
        foreach (var (definition, parameters) in AffiliationFields(incoming))
        {
            var origin = (parameters[BindingKey] ?? parameters[BoundKey]) as JObject;
            if (origin == null || !prior.TryGetValue(Key(definition, origin), out var ids) || ids.Length != 1) continue;
            if (long.TryParse(ids[0], out long id) && WorldScriptAffiliations.Exists(WorldScriptAffiliations.Kind(definition), id))
                Bind(parameters, ids[0]);
        }

        static (string Kind, string ID, string Name) Key(string definition, JObject value) =>
            (WorldScriptAffiliations.Kind(definition), value.Value<string>("source_id"), value.Value<string>("name"));
    }

    internal static IEnumerable<Binding> Pending(ScriptInstance script)
    {
        foreach (var (definition, parameters) in AffiliationFields(script))
            if (parameters[BindingKey] is JObject marker)
                yield return new Binding
                {
                    Kind = WorldScriptAffiliations.Kind(definition), Name = marker.Value<string>("name"),
                    SourceID = marker.Value<string>("source_id"), Parameters = parameters
                };
    }

    private static ScriptInstance NewDraft(ScriptInstance source)
    {
        var copy = WorldScripts.Clone(source);
        copy.ID = Guid.NewGuid().ToString("N");
        copy.ScenarioID = null;
        copy.PackageScriptID = null;
        copy.Enabled = false;
        copy.IsDraft = true;
        copy.Runtime = new ScriptRuntimeState();
        return copy;
    }

    private static Document Read()
    {
        if (!File.Exists(FilePath)) return new Document { schemaVersion = Version };
        var document = JsonConvert.DeserializeObject<Document>(File.ReadAllText(FilePath));
        if (document?.schemaVersion != Version || document.Templates == null ||
            document.Templates.Any(item => item == null || string.IsNullOrWhiteSpace(item.Name)))
            throw new InvalidDataException("Invalid or unsupported world script templates");
        return document;
    }

    private static IEnumerable<(string Definition, JObject Parameters)> AffiliationFields(ScriptInstance script) =>
        EditableFields(script).Where(field => WorldScriptAffiliations.Kind(field.Definition) != null);

    internal static IEnumerable<(string Definition, JObject Parameters)> EditableFields(ScriptInstance script) =>
        (script.Flow == null
            ? Scope(script.TargetQuery).Concat(Conditions(script.Conditions)).Concat(Actions(script.Actions))
            : FlowFields(script.Flow))
        .Append((Definition: script.TriggerCondition?.TriggerID, Parameters: script.TriggerCondition?.Parameters))
        .Where(field => field.Parameters != null);

    private static IEnumerable<(string Definition, JObject Parameters)> FlowFields(ScriptFlow flow)
    {
        foreach (var field in Query(flow.Query).Concat(Conditions(flow.Gate))) yield return field;
        foreach (var step in flow.Steps ?? Enumerable.Empty<ScriptStep>())
        {
            if (step == null) continue;
            foreach (var field in Query(step.Query).Concat(Actions(new[] { step.Action, step.Branch?.Otherwise }))
                .Concat(Conditions(step.Branch?.Condition)).Concat(Conditions(step.Relation?.Filter))
                .Concat(Conditions(step.Relation?.Next?.Filter)))
                yield return field;
        }
    }

    private static IEnumerable<(string Definition, JObject Parameters)> Scope(TargetSelector scope)
    {
        if (scope != null) yield return (scope.TargetID, scope.Parameters);
    }

    private static IEnumerable<(string Definition, JObject Parameters)> Query(ScriptQuery query) =>
        Scope(query?.Scope).Concat(Scope(query?.DistanceOrigin)).Concat(Conditions(query?.Filter));

    private static IEnumerable<(string Definition, JObject Parameters)> Conditions(ConditionNode node)
    {
        if (node == null) yield break;
        if (node.Operator == "condition") yield return (node.ConditionID, node.Parameters);
        foreach (var child in node.Children ?? Enumerable.Empty<ConditionNode>())
            foreach (var field in Conditions(child)) yield return field;
    }

    private static IEnumerable<(string Definition, JObject Parameters)> Actions(IEnumerable<ScriptAction> actions)
    {
        foreach (var item in actions ?? Enumerable.Empty<ScriptAction>())
        {
            if (item == null) continue;
            if (item.Operator == "action") yield return (item.ActionID, item.Parameters);
            foreach (var field in Actions(item.Children)) yield return field;
        }
    }
}
