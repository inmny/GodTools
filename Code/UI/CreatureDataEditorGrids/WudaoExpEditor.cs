using GodTools.UI.Prefabs;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;
using UPersian.Utils;

namespace GodTools.UI.CreatureDataEditorGrids;

public class WudaoExpEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.inmny_custommodt001.wudao_exp";
    }

    protected override void UpdateValue(string value)
    {
        if (float.TryParse(value, out float parsed_value))
        {
            Actor.data.set("inmny.custommodt001.wudao_exp", parsed_value);
            Actor.setStatsDirty();
        }
    }

    protected override string GetInitValue()
    {
        Actor.data.get("inmny.custommodt001.wudao_exp", out int value);
        return value.ToString();
    }
}
