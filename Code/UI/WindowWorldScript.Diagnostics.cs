using System;
using System.Collections.Generic;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private bool _showValidation;

    private void BuildValidationErrors(IEnumerable<string> errors)
    {
        foreach (string error in errors)
        {
            var steps = Draft?.Flow?.Steps;
            int end = error.IndexOfAny(new[] { '.', ':' });
            if (steps != null && error.StartsWith("step ", StringComparison.Ordinal) && end > 5 &&
                int.TryParse(error.Substring(5, end - 5), out int step) && step >= 1 && step <= steps.Count)
            {
                string id = steps[step - 1].ID;
                var row = Row(_body, EditorWidth, 23);
                ButtonAt(row, $"{step}. {ErrorLabel(error.Substring(end + 1).Trim())}", EditorWidth - 24,
                    () => JumpToStep(id), NativeIcon("iconEdit"), surface: Surface.List);
            }
            else Message(ErrorLabel(error), true);
        }
    }

    private void BuildPreviewDecisions(WorldScriptPreviewResult preview)
    {
        int count = preview.Decisions.Count + preview.OmittedDecisions;
        if (count == 0) return;
        if (!FlowFold((Draft, "preview_decisions"), string.Format(L("scenario.preview_skipped", "{0} filtered out"), count), "iconInspect")) return;
        foreach (var item in preview.Decisions)
        {
            string location = item.Step == 0 ? L("flow.main_selected", "Main query")
                : string.Format(L("history.limit_step", "Step {0}"), item.Step);
            string label = item.Group == null ? SampleLabel(item.Subject) : SampleLabel(item.Group) + " › " + SampleLabel(item.Subject);
            string detail = L("scenario.preview_reason." + item.Reason, item.Reason);
            if (item.Rule != null)
            {
                string name = item.Rule.Operator == "condition"
                    ? ConditionSummary(item.Rule, WorldScriptRegistry.GetCondition(item.Rule.ConditionID))
                    : TreeTitle(GroupLabel(item.Rule.Operator), item.Rule);
                detail += " · " + name;
            }
            var row = Row(_body, EditorWidth, 22);
            Label(row, $"{location} · {label} · {detail}", EditorWidth - 25);
            AddLocateButton(row, item.Subject);
        }
        if (preview.OmittedDecisions > 0)
            Message(string.Format(L("editor.extra_targets", "+ {0} more targets"), preview.OmittedDecisions), false);
    }
}
