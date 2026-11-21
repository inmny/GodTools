using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

public class JindanStrengthEditor : SimpleInputEditor
{
    protected override string GetTitleKey() => "inmny.godtools.ui.data_editor.cultiway.jindan.strength";

    protected override void OnSetup() => TextInput.input.characterValidation = InputField.CharacterValidation.Decimal;

    protected override void UpdateValue(string value)
    {
        if (float.TryParse(value, out float strength))
            CultiwayFormationEditor.Set(Actor, "Jindan", "strength", Mathf.Max(0, strength));
    }

    protected override string GetInitValue() =>
        CultiwayFormationEditor.Read(Actor, "Jindan", "strength") is float value ? value.ToString("F2") : "-";
}

public class JindanStageEditor : SimpleInputEditor
{
    protected override string GetTitleKey() => "inmny.godtools.ui.data_editor.cultiway.jindan.stage";

    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int stage))
            CultiwayFormationEditor.Set(Actor, "Jindan", "stage", Mathf.Max(0, stage));
    }

    protected override string GetInitValue() =>
        CultiwayFormationEditor.Read(Actor, "Jindan", "stage")?.ToString() ?? "-";
}
