using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private WorldScriptRehearsals.Case _rehearsal;

    private void OpenRehearsal(WorldScriptRehearsals.Case entry)
    {
        _rehearsal = entry;
        Enter(Page.Rehearsal);
    }

    private void CreateRehearsal(string name, IEnumerable<ScriptInstance> drafts, ScriptScenario scenario = null,
        IEnumerable<string> disabled = null)
    {
        try { OpenRehearsal(WorldScriptRehearsals.Create(name, drafts, scenario, disabled)); }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not create rehearsal: {exception}");
            Enter(Page.Rehearsal);
            _notice = L("rehearsal.failed", "Rehearsal operation failed. Check the game log.");
            Render();
        }
    }

    private void BuildRehearsal()
    {
        if (_notice != null) Message(_notice, true);
        if (_rehearsal == null)
        {
            Heading(L("rehearsal.list", "Rehearsals"));
            foreach (var entry in WorldScriptRehearsals.List())
                ButtonAt(_body, $"{entry.Name} · {entry.StartTime:0.##}", EditorWidth, () => OpenRehearsal(entry),
                    NativeIcon("iconInspect"), surface: Surface.List);
            return;
        }

        var current = _rehearsal;
        Heading(current.Name);
        var commands = Row(_body, EditorWidth, 24);
        ButtonAt(commands, L("rehearsal.open_saved", "Open saved rules copy"), 124, () => LoadCopy(false), surface: Surface.Quiet);
        ButtonAt(commands, L("rehearsal.open_draft", "Open draft rules copy"), 124, () => LoadCopy(true), surface: Surface.Quiet);
        if (WorldScriptRehearsals.ActiveBranch(current).HasValue)
            ButtonAt(commands, L("rehearsal.capture", "Record result"), 120, () =>
            {
                try { WorldScriptRehearsals.Capture(current); Render(); }
                catch (Exception exception) { Report(exception); }
            }, NativeIcon("iconSaveLocal"), surface: Surface.Primary);

        try
        {
            var saved = WorldScriptRehearsals.ReadResult(current, false);
            var draft = WorldScriptRehearsals.ReadResult(current, true);
            if (saved == null || draft == null)
                Message(L("rehearsal.capture_both", "Run the same case in both copies and record each result."), false);
            else BuildRehearsalComparison(current, saved, draft);
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not read rehearsal results: {exception}");
            Message(L("rehearsal.failed", "Rehearsal operation failed. Check the game log."), true);
        }

        void LoadCopy(bool draft) => Confirm(L("rehearsal.switch", "Open a fresh test copy? Save any current world changes first."),
            () =>
            {
                try
                {
                    Enter(Page.Rehearsal);
                    WorldScriptRehearsals.Open(current, draft);
                }
                catch (Exception exception) { Report(exception); }
            }, () => Enter(Page.Rehearsal));

        void Report(Exception exception)
        {
            Main.LogWarning($"Could not use rehearsal: {exception}");
            _notice = L("rehearsal.failed", "Rehearsal operation failed. Check the game log.");
            Render();
        }
    }

    private void BuildRehearsalComparison(WorldScriptRehearsals.Case entry,
        WorldScriptRehearsals.Result saved, WorldScriptRehearsals.Result draft)
    {
        Message(string.Format(L("rehearsal.times", "Saved rules at {0:0.##} · Draft rules at {1:0.##}"), saved.Time, draft.Time),
            Math.Abs(saved.Time - draft.Time) > 0.01d);
        var before = saved.Scripts.ToDictionary(script => script.ID);
        var after = draft.Scripts.ToDictionary(script => script.ID);
        foreach (string id in entry.Scripts)
        {
            before.TryGetValue(id, out var first);
            after.TryGetValue(id, out var second);
            string title = second?.Name ?? first?.Name ?? id;
            if (!FlowFold((entry, id), title, "iconWorldLaws")) continue;
            Message(string.Format(L("rehearsal.script_result", "Processed {0} → {1} · Pending {2} → {3}"),
                first?.Targets ?? 0, second?.Targets ?? 0, first?.Pending ?? 0, second?.Pending ?? 0), false);
            Message($"{Outcome(first)} → {Outcome(second)}", false);
            if (first?.Samples?.Count > 0 || second?.Samples?.Count > 0)
                Message($"{Targets(first)} → {Targets(second)}", false);
        }

        var previousStock = saved.Inventory.ToDictionary(item => (item.City, item.Resource));
        var newStock = draft.Inventory.ToDictionary(item => (item.City, item.Resource));
        var stockChanges = previousStock.Keys.Union(newStock.Keys).Where(key =>
            previousStock.GetValueOrDefault(key)?.Amount != newStock.GetValueOrDefault(key)?.Amount).ToArray();
        if (FlowFold((entry, "stock"), string.Format(L("rehearsal.stock", "{0} stock differences"), stockChanges.Length), "iconCityList"))
            foreach (var key in stockChanges.Take(80))
            {
                var old = previousStock.GetValueOrDefault(key);
                var item = newStock.GetValueOrDefault(key);
                string resource = ParameterDisplayName("city.resources.change", "resource_id", key.Resource);
                Message($"{item?.Name ?? old?.Name} · {resource} · {old?.Amount.ToString() ?? "—"} → {item?.Amount.ToString() ?? "—"}", false);
            }

        static string Outcome(WorldScriptRehearsals.ScriptResult result) => result?.Outcome == null ? "—" : RunOutcome(result.Outcome);
        static string Targets(WorldScriptRehearsals.ScriptResult result) =>
            result?.Samples?.Count > 0 ? string.Join(" / ", result.Samples.Select(SampleLabel)) : "—";
    }
}
