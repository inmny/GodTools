#if CULTIWAY
using Cultiway.Content.Components;
using Cultiway.Utils.Extension;
using GodTools.UI.Prefabs;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

/// <summary>
/// 元婴编辑器：编辑元婴强度(strength)与转数(stage)。
/// Yuanying 的 yuanying_type 为 readonly，类型不可直接改，故只编辑数值字段。
/// </summary>
public class YuanyingStrengthEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.cultiway.yuanying.strength";
    }

    protected override void OnSetup()
    {
        TextInput.input.characterValidation = InputField.CharacterValidation.Decimal;
    }

    protected override void UpdateValue(string value)
    {
        if (!float.TryParse(value, out float parsed)) return;
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Yuanying>()) return;
        ref var yuanying = ref ae.GetComponent<Yuanying>();
        if (!yuanying.formation.IsValid) return;
        yuanying.strength = Mathf.Max(0, parsed);
        ae.MarkCultiwayStatsDirty();
    }

    protected override string GetInitValue()
    {
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Yuanying>()) return "-";
        var yuanying = ae.GetComponent<Yuanying>();
        return yuanying.formation.IsValid ? yuanying.strength.ToString("F2") : "-";
    }
}

public class YuanyingStageEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.cultiway.yuanying.stage";
    }

    protected override void UpdateValue(string value)
    {
        if (!int.TryParse(value, out int parsed)) return;
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Yuanying>()) return;
        ref var yuanying = ref ae.GetComponent<Yuanying>();
        if (!yuanying.formation.IsValid) return;
        yuanying.stage = Mathf.Max(0, parsed);
        ae.MarkCultiwayStatsDirty();
    }

    protected override string GetInitValue()
    {
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Yuanying>()) return "-";
        var yuanying = ae.GetComponent<Yuanying>();
        return yuanying.formation.IsValid ? yuanying.stage.ToString() : "-";
    }
}
#endif
