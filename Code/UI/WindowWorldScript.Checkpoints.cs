using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features.WorldScript;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private void OpenCheckpoints(ScriptScenario scenario)
    {
        _checkpointScenarioID = scenario?.ID;
        Enter(Page.Checkpoints);
    }

    private void SaveCheckpoint(ScriptScenario scenario, ScriptScene scene, ScriptSceneMark mark)
    {
        try
        {
            string name = string.IsNullOrEmpty(_checkpointName) ? scene.Name : _checkpointName;
            WorldScriptCheckpoints.Save(scenario, scene, name, mark);
            _checkpointName = "";
            WorldTip.showNow(L("scene.checkpoint_saved", "Checkpoint saved"), false, "top");
            OpenCheckpoints(scenario);
        }
        catch (Exception exception) { CheckpointError(exception); }
    }

    private void BuildCheckpoints()
    {
        if (_checkpointScenarioID != null && _scenario?.ID == _checkpointScenarioID)
            ButtonAt(_body, _scenario.Name, 150, () => Enter(Page.Scenario),
                NativeIcon("iconArrowLeft"), surface: Surface.Quiet);
        Heading(L("scene.checkpoints", "Checkpoints"));
        if (_notice != null) Message(_notice, true);
        try
        {
            var selected = WorldScriptCheckpoints.SelectedEndings();
            var entries = WorldScriptCheckpoints.List()
                .Where(item => _checkpointScenarioID == null || _checkpointScenarioID == item.ScenarioID).ToArray();
            if (entries.Length == 0)
                Message(L("scene.no_checkpoints", "Save a checkpoint from a scene to list it here."), false);
            foreach (var entry in entries.OrderByDescending(entry => selected.GetValueOrDefault(entry.ScenarioID) == entry.ID))
            {
                bool ending = selected.GetValueOrDefault(entry.ScenarioID) == entry.ID;
                string title = $"{entry.Name} · {SceneYear(Date.getYear(entry.Time))}";
                if (ending) title = $"{L("scene.ending", "Selected ending")} · {title}";
                if (!FlowFold((entry.ID, "checkpoint"), title, ending ? "iconCheckmark" : "iconSaveLocal")) continue;
                Message($"{entry.ScenarioName} · {entry.SceneName}", false);
                var row = Row(_body, EditorWidth, 24);
                ButtonAt(row, L("scene.open_checkpoint", "Open working copy"), 150, () =>
                    Confirm(L("scene.checkpoint_confirm", "Open a fresh working copy? Save your current world first."), () =>
                    {
                        try
                        {
                            Enter(Page.Checkpoints);
                            WorldScriptCheckpoints.Open(entry);
                            CloseToMap();
                        }
                        catch (Exception exception) { CheckpointError(exception); }
                    }, () => Enter(Page.Checkpoints)), NativeIcon("iconPlay"), surface: Surface.Primary);
                if (!ending)
                    ButtonAt(row, L("scene.select_ending", "Select as ending"), 146, () =>
                    {
                        try { WorldScriptCheckpoints.SelectEnding(entry); Render(); }
                        catch (Exception exception) { CheckpointError(exception); }
                    }, NativeIcon("iconCheckmark"), surface: Surface.Quiet);
            }
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not read checkpoints: {exception}");
            Message(L("scene.checkpoint_failed", "Checkpoint operation failed. Check the game log."), true);
        }
    }

    private void CheckpointError(Exception exception)
    {
        Main.LogWarning($"Could not use checkpoint: {exception}");
        _notice = L("scene.checkpoint_failed", "Checkpoint operation failed. Check the game log.");
        Render();
    }
}
