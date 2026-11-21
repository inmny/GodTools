using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private List<ScriptInstance> _packageDrafts;
    private readonly HashSet<ScriptInstance> _packageSelection = new();
    private bool _exportingPackage;
    private string _packageName;
    private ScriptScenario _packageScenario;
    private WorldScriptPackages.Import _packageImport;
    private int _packageRevision;
    private readonly HashSet<string> _packagePublic = new();

    private void OpenPackageExport()
    {
        if (WorldScriptEngine.Scenario(Draft?.ScenarioID) is ScriptScenario scenario)
        {
            OpenScenarioExport(scenario);
            return;
        }
        _exportingPackage = true;
        _packageScenario = null;
        _packageName = Draft.Name;
        _packageDrafts = WorldScripts.Scripts.Where(script => script != null)
            .Select(script => _drafts.TryGetValue(script, out var draft) ? draft : script).ToList();
        _packageSelection.Clear();
        _packageSelection.Add(Draft);
        Enter(Page.Package);
    }

    private void OpenScenarioExport(ScriptScenario scenario)
    {
        _exportingPackage = true;
        _packageScenario = scenario;
        _packageRevision = Math.Max(1, scenario.PackageRevision + 1);
        _packageName = scenario.Name;
        _packageDrafts = WorldScripts.Scripts.Where(script => script.ScenarioID == scenario.ID)
            .Select(script => _drafts.TryGetValue(script, out var draft) ? draft : script).ToList();
        _packageSelection.Clear();
        _packageSelection.UnionWith(_packageDrafts);
        _packagePublic.Clear();
        _packagePublic.UnionWith(scenario.Parameters.Keys);
        Enter(Page.Package);
    }

    private void OpenPackages()
    {
        try
        {
            var files = WorldScriptPackages.List().ToList();
            if (files.Count == 0)
            {
                TemplateNotice(string.Format(L("packages.empty", "Place script package files in {0}."), WorldScriptPackages.DirectoryPath));
                return;
            }
            OpenPicker(L("packages.import", "Import script package"), files.Select(path =>
                new Option(Path.GetFileNameWithoutExtension(path), () => OpenPackage(path), NativeIcon("iconSaveLocal"))));
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not list WorldScript packages: {exception}");
            TemplateNotice(L("packages.failed", "Could not open the script package."), true);
        }
    }

    private void OpenPackage(string path)
    {
        try
        {
            _packageImport = WorldScriptPackages.Load(path);
            _packageDrafts = _packageImport.Scripts;
            _packageScenario = _packageImport.Scenario;
            _packageName = Path.GetFileNameWithoutExtension(path);
            _exportingPackage = false;
            Enter(Page.Package);
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not read WorldScript package: {exception}");
            TemplateNotice(L("packages.failed", "Could not open the script package."), true);
        }
    }

    private void BuildPackage()
    {
        if (_exportingPackage)
        {
            Heading(L("packages.export", "Export script package"));
            InputAt(FieldRow(L("field.name", "Name")), _packageName, 206, value => _packageName = value);
            if (_packageScenario != null)
            {
                Number("packages.revision", "Revision", _packageRevision, value => _packageRevision = Math.Max(1, value));
                if (_packageScenario.Parameters.Count > 0)
                    Heading(L("packages.public_parameters", "Parameters editable on import"));
                foreach (string parameter in _packageScenario.Parameters.Keys)
                {
                    var control = FieldRow(ScenarioParameterName(parameter));
                    SwitchAt(control, _packagePublic.Contains(parameter), ScenarioParameterName(parameter), () =>
                    {
                        if (!_packagePublic.Add(parameter)) _packagePublic.Remove(parameter);
                        Render();
                    });
                }
            }
            foreach (var script in _packageDrafts)
            {
                var row = Row(_body, EditorWidth, 23);
                SwitchAt(row, _packageSelection.Contains(script), script.Name, () =>
                {
                    if (!_packageSelection.Add(script)) _packageSelection.Remove(script);
                    Render();
                });
                Label(row, script.Name, EditorWidth - 30);
            }
        }
        else
        {
            Heading(_packageName);
            BuildPackageOverview();
            foreach (var script in _packageDrafts)
            {
                if (!FlowFold(script, script.Name, "iconWorldLaws")) continue;
                BuildPendingBindings(script, Page.Package);
                var trigger = WorldScriptRegistry.GetTrigger(script.TriggerCondition.TriggerID);
                if (trigger != null)
                    BuildParameters(trigger.ID, script.TriggerCondition.Parameters, trigger.Parameters, returnTo: Page.Package);
                else
                    BuildMissingDefinition(script.TriggerCondition.TriggerID);

                foreach (var field in WorldScriptTemplates.EditableFields(script))
                {
                    if (field.Definition == "script.schedule")
                        BuildPackageFollowUp(script, field.Parameters);
                    var action = WorldScriptRegistry.GetAction(field.Definition);
                    var condition = WorldScriptRegistry.GetCondition(field.Definition);
                    var parameters = action?.Parameters ?? condition?.Parameters;
                    if (parameters == null)
                    {
                        if (!string.IsNullOrEmpty(field.Definition) && WorldScriptRegistry.GetTarget(field.Definition) == null &&
                            WorldScriptRegistry.GetTrigger(field.Definition) == null)
                            BuildMissingDefinition(field.Definition);
                        continue;
                    }
                    var fields = parameters.Where(parameter => parameter.Key is "log_icon" or "delay" ||
                        condition != null && parameter.Key is "operator" or "value").ToArray();
                    if (fields.Length == 0) continue;
                    Heading(action != null ? HistoryActionName(action.ID)
                        : DefinitionName("condition", condition.ID, condition.Name));
                    BuildParameters(field.Definition, field.Parameters, fields, returnTo: Page.Package);
                }
                foreach (var step in script.Flow?.Steps ?? Enumerable.Empty<ScriptStep>())
                    if (step.ResultCondition is ScriptCountRule rule)
                        Number("flow.result_threshold", "Threshold", rule.Value, value => rule.Value = Math.Max(0, value));
            }
        }

        if (FlowFold((_packageDrafts, "preflight"), L("packages.preflight", "Compatibility check"), "iconInspect"))
        {
            var entries = _exportingPackage ? _packageDrafts.Where(_packageSelection.Contains) : _packageDrafts;
            foreach (var item in WorldScriptEngine.Preflight(entries, _packageScenario, _exportingPackage))
            {
                Message(item.Script.Name, false);
                if (item.Errors.Count == 0) Message(L("packages.preflight_passed", "Validation passed"), false, indent: 16);
                foreach (string error in item.Errors)
                    Message(ErrorLabel(error), true, indent: 16);
            }
        }

        var commands = Row(_body, EditorWidth, 24);
        ButtonAt(commands, L("command.cancel", "Cancel"), 102, () => Enter(Page.Rules));
        string command = _exportingPackage ? L("packages.save", "Save package")
            : _packageImport.Previous != null ? L("packages.upgrade", "Apply upgrade") : L("packages.add", "Create disabled drafts");
        ButtonAt(commands, command, 154, () =>
        {
            if (!_exportingPackage && _packageImport.Previous != null)
                Confirm(L("packages.upgrade_confirm", "Import new rules as disabled drafts and carry over progress and pending follow-ups?"),
                    FinishPackage, () => Enter(Page.Package));
            else FinishPackage();
        }, NativeIcon("iconSaveLocal"), surface: Surface.Primary)
            .interactable = !_exportingPackage || _packageSelection.Count > 0;
    }

    private void BuildPackageOverview()
    {
        if (_packageScenario == null) return;
        var previous = _packageImport.Previous;
        string revision = previous == null ? _packageScenario.PackageRevision.ToString()
            : $"{previous.PackageRevision} → {_packageScenario.PackageRevision}";
        Message($"{_packageScenario.Name} · {L("packages.revision", "Revision")} {revision}", false);
        foreach (string id in _packageImport.MissingDefinitions) BuildMissingDefinition(id);
        foreach (string parameter in _packageImport.PublicParameters)
            InputAt(FieldRow(ScenarioParameterName(parameter)),
                _packageScenario.Parameters[parameter].ToString(CultureInfo.InvariantCulture), 120, value =>
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                        _packageScenario.Parameters[parameter] = amount;
                }, UnityEngine.UI.InputField.CharacterValidation.Integer);
        if (previous == null) return;
        ButtonAt(_body, L("packages.rehearse_upgrade", "Rehearse upgrade"), 150, () =>
            CreateRehearsal(_packageName, _packageDrafts, _packageScenario, _packageImport.Removed.Select(script => script.ID)),
            NativeIcon("iconInspect"), surface: Surface.Quiet);
        foreach (var incoming in _packageDrafts)
        {
            if (!_packageImport.Replacements.TryGetValue(incoming.ID, out var old))
                Message($"{incoming.Name} · {L("packages.added_script", "New script")}", false);
            else if (FlowFold((incoming, "changes"), $"{incoming.Name} · {L("packages.compare", "Compare rules")}", "iconInspect"))
                BuildRuleChanges(old, incoming);
        }
        foreach (var removed in _packageImport.Removed)
            Message($"{removed.Name} · {L("packages.archived_script", "Archive as disabled")}", false);
    }

    private void BuildRuleChanges(ScriptInstance old, ScriptInstance incoming)
    {
        string TriggerOf(ScriptInstance script) => TriggerSummary(WorldScriptRegistry.GetTrigger(script.TriggerCondition.TriggerID), script.TriggerCondition.Parameters);
        string before = TriggerOf(old);
        string after = TriggerOf(incoming);
        if (before != after) Message($"{L("field.trigger", "Trigger")} · {before} → {after}", false, indent: 16);
        var earlier = old.Flow?.Steps ?? new List<ScriptStep>();
        var later = incoming.Flow?.Steps ?? new List<ScriptStep>();
        for (int index = 0; index < later.Count; index++)
        {
            var next = later[index];
            var prior = earlier.Find(step => step.ID == next.ID);
            if (prior == null || !Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JObject.FromObject(prior),
                    Newtonsoft.Json.Linq.JObject.FromObject(next)))
            {
                string afterStep = NamedStepSummary(next);
                string beforeStep = prior == null ? null : NamedStepSummary(prior);
                string change = beforeStep == afterStep ? afterStep + " · " + L("packages.step_changed", "Step settings changed")
                    : beforeStep == null ? afterStep : beforeStep + " → " + afterStep;
                Message($"{index + 1}. {change}", false, indent: 16);
            }
        }
        foreach (var removed in earlier.Where(step => later.All(other => other.ID != step.ID)))
            Message($"{NamedStepSummary(removed)} · {L("packages.removed_step", "Removed step")}", false, indent: 16);
        if (old.Flow?.Query != null && incoming.Flow?.Query != null &&
            !Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JObject.FromObject(old.Flow.Query),
                Newtonsoft.Json.Linq.JObject.FromObject(incoming.Flow.Query)))
        {
            string filter = L("flow.filter", "Target filters");
            string beforeQuery = ScopeLabel(old.Flow.Query.Scope) + " · " + TreeTitle(filter, old.Flow.Query.Filter);
            string afterQuery = ScopeLabel(incoming.Flow.Query.Scope) + " · " + TreeTitle(filter, incoming.Flow.Query.Filter);
            Message(beforeQuery == afterQuery ? afterQuery + " · " + L("packages.step_changed", "Step settings changed")
                : beforeQuery + " → " + afterQuery, false, indent: 16);
        }
    }

    private void BuildMissingDefinition(string id) =>
        Message(string.Format(L("packages.missing_definition", "Unavailable definition {0}"), id), true);

    private void BuildPackageFollowUp(ScriptInstance script, Newtonsoft.Json.Linq.JObject parameters)
    {
        string label = L("parameter.script_id", "Follow-up script");
        var candidates = _packageDrafts.Concat(WorldScripts.Scripts)
            .Where(candidate => candidate != null && candidate.ID != script.ID &&
                candidate.TriggerCondition.TriggerID == WorldScriptRegistry.TriggerDelayed).ToList();
        string current = candidates.FirstOrDefault(candidate => candidate.ID == WorldScriptRegistry.GetString(parameters, "script_id"))?.Name
            ?? L("value.unset", "Unset");
        FieldAt(FieldRow(label), current, 206, () => OpenPicker(label, candidates.Select(candidate =>
            new Option(candidate.Name, () => { parameters["script_id"] = candidate.ID; Enter(Page.Package); })), Page.Package));
    }

    private void FinishPackage()
    {
        try
        {
            if (_exportingPackage)
            {
                string path = WorldScriptPackages.Save(_packageName, _packageDrafts.Where(_packageSelection.Contains),
                    _packageScenario, _packageRevision, _packagePublic);
                TemplateNotice(string.Format(L("packages.saved", "Script package saved to {0}"), path));
            }
            else
            {
                WorldScriptPackages.Install(_packageImport);
                if (_packageScenario == null) SelectScript(_packageDrafts[0]);
                else OpenScenario(_packageImport.Previous ?? _packageScenario);
            }
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not finish WorldScript package: {exception}");
            TemplateNotice(L("packages.failed", "Could not complete the script package."), true);
        }
    }
}
