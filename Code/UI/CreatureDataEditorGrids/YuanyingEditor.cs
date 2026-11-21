using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

public class YuanyingStrengthEditor : SimpleInputEditor
{
    protected override string GetTitleKey() => "inmny.godtools.ui.data_editor.cultiway.yuanying.strength";

    protected override void OnSetup() => TextInput.input.characterValidation = InputField.CharacterValidation.Decimal;

    protected override void UpdateValue(string value)
    {
        if (float.TryParse(value, out float strength))
            CultiwayFormationEditor.Set(Actor, "Yuanying", "strength", Mathf.Max(0, strength));
    }

    protected override string GetInitValue() =>
        CultiwayFormationEditor.Read(Actor, "Yuanying", "strength") is float value ? value.ToString("F2") : "-";
}

public class YuanyingStageEditor : SimpleInputEditor
{
    protected override string GetTitleKey() => "inmny.godtools.ui.data_editor.cultiway.yuanying.stage";

    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int stage))
            CultiwayFormationEditor.Set(Actor, "Yuanying", "stage", Mathf.Max(0, stage));
    }

    protected override string GetInitValue() =>
        CultiwayFormationEditor.Read(Actor, "Yuanying", "stage")?.ToString() ?? "-";
}
