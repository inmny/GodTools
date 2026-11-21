using GodTools.Features.Rankings;
using UnityEngine;

namespace GodTools.UI.CreatureDataEditorGrids;

public class PowerLevelEditor : SimpleInputEditor
{
    protected override string GetTitleKey() => "inmny.godtools.ui.data_editor.cultiway.power_level";

    protected override void UpdateValue(string value)
    {
        if (int.TryParse(value, out int level))
            CultiwayCompatibility.Find().SetPower(Actor, Mathf.Clamp(level, 0, 20));
    }

    protected override string GetInitValue() => ((int)CultiwayCompatibility.Find().Power(Actor)).ToString();
}
