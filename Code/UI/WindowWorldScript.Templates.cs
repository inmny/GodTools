using System;
using System.Linq;
using GodTools.Features;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private void SaveTemplate()
    {
        ScriptInstance draft = Draft;
        if (string.IsNullOrWhiteSpace(draft?.Name))
        {
            TemplateNotice(L("editor.name_required", "Enter a script name."), true);
            return;
        }
        try
        {
            string name = WorldScriptTemplates.Save(draft);
            TemplateNotice(string.Format(L("templates.saved", "Saved template {0}."), name));
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not save WorldScript templates: {exception}");
            TemplateNotice(L("templates.save_failed", "Could not save the template."), true);
        }
    }

    private void OpenTemplateLibrary()
    {
        try
        {
            var templates = WorldScriptTemplates.List();
            if (templates.Count == 0)
            {
                TemplateNotice(L("templates.empty", "Save a script as a template to use it in another world."));
                return;
            }
            OpenPicker(L("templates.library", "My templates"),
                templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(template => new Option(template.Name, () => UseTemplate(template), NativeIcon("iconWorldLaws"))));
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not read WorldScript templates: {exception}");
            TemplateNotice(L("templates.load_failed", "Could not read the templates."), true);
        }
    }

    private void UseTemplate(ScriptInstance template)
    {
        try
        {
            var copy = WorldScriptTemplates.Instantiate(template);
            SelectScript(WorldScripts.AddScript(copy));
            if (WorldScriptTemplates.Pending(Draft).Any())
            {
                _expanded.Add((Draft, "bindings"));
                Render();
            }
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not use WorldScript template '{template.Name}': {exception}");
            TemplateNotice(L("templates.load_failed", "Could not read the templates."), true);
        }
    }

    private void TemplateNotice(string text, bool error = false)
    {
        _page = Page.Rules;
        _notice = text;
        _noticeIsError = error;
        if (Draft == null) WorldTip.showNowTop(text, false);
        Render();
    }

    private void BuildPendingBindings(ScriptInstance draft, Page returnTo = Page.Rules)
    {
        var groups = WorldScriptTemplates.Pending(draft)
            .GroupBy(binding => (binding.Kind, binding.Name, binding.SourceID))
            .ToArray();
        if (groups.Length == 0) return;
        string title = string.Format(L("templates.pending", "{0} affiliations to bind"), groups.Length);
        if (!FlowFold((draft, "bindings"), title, "iconArrowAttackTarget")) return;
        foreach (var group in groups)
        {
            string kind = L("affiliation." + group.Key.Kind, group.Key.Kind);
            string label = string.IsNullOrWhiteSpace(group.Key.Name) ? kind : $"{kind} · {group.Key.Name}";
            if (!string.IsNullOrEmpty(group.Key.SourceID))
                label += " · " + string.Format(L("templates.source_id", "Original ID {0}"), group.Key.SourceID);
            ButtonAt(_body, label, EditorWidth, () =>
                OpenPicker(label, ParameterOptions(group.Key.Kind switch
                {
                    "city" => WorldScriptRegistry.TargetCityByID,
                    "region" => WorldScriptRegistry.TargetCityRegion,
                    _ => "actor." + group.Key.Kind
                }, "id", "", id =>
                {
                    foreach (var binding in group) WorldScriptTemplates.Bind(binding.Parameters, id);
                    Enter(returnTo);
                }), returnTo), surface: Surface.Field, arrow: NativeIcon("iconArrowDOWN"));
        }
    }
}
