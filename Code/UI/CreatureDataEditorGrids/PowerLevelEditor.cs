
#if CULTIWAY
using Cultiway.Core.Components;
using Cultiway.Utils.Extension;
using UnityEngine;

namespace GodTools.UI.CreatureDataEditorGrids;

public class PowerLevelEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.cultiway.power_level";
    }

    protected override void UpdateValue(string value)
    {
        if (!int.TryParse(value, out int parsed_value)) return;
        var ae = Actor.GetExtend();
        // Friflo 的 AddComponent 在组件已存在时会抛异常，正常修炼的生物都已有 PowerLevel，
        // 因此这里走“存在则改 ref，不存在才添加”的安全写入路径。
        float clamped = Mathf.Clamp(parsed_value, 0, 20);
        if (ae.HasComponent<PowerLevel>())
        {
            ae.GetComponent<PowerLevel>().value = clamped;
        }
        else
        {
            ae.AddComponent(new PowerLevel() { value = clamped });
        }
        ae.MarkCultiwayStatsDirty();
    }
    protected override string GetInitValue()
    {
        return ((int)Actor.GetExtend().GetPowerLevel()).ToString();
    }
}
#endif