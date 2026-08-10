#if CULTIWAY
using Cultiway.Content.Components;
using Cultiway.Utils.Extension;
using GodTools.UI.Prefabs;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

/// <summary>
/// 金丹编辑器：编辑金丹强度(strength)与转数(stage)。
/// Jindan 的 jindan_type 为 readonly，类型不可直接改，故只编辑数值字段。
/// </summary>
public class JindanStrengthEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.cultiway.jindan.strength";
    }

    protected override void OnSetup()
    {
        TextInput.input.characterValidation = InputField.CharacterValidation.Decimal;
    }

    protected override void UpdateValue(string value)
    {
        if (!float.TryParse(value, out float parsed)) return;
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Jindan>()) return;
        ref var jindan = ref ae.GetComponent<Jindan>();
        if (!jindan.formation.IsValid) return;
        jindan.strength = Mathf.Max(0, parsed);
        ae.MarkCultiwayStatsDirty();
    }

    protected override string GetInitValue()
    {
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Jindan>()) return "-";
        var jindan = ae.GetComponent<Jindan>();
        return jindan.formation.IsValid ? jindan.strength.ToString("F2") : "-";
    }
}

public class JindanStageEditor : SimpleInputEditor
{
    protected override string GetTitleKey()
    {
        return "inmny.godtools.ui.data_editor.cultiway.jindan.stage";
    }

    protected override void UpdateValue(string value)
    {
        if (!int.TryParse(value, out int parsed)) return;
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Jindan>()) return;
        ref var jindan = ref ae.GetComponent<Jindan>();
        if (!jindan.formation.IsValid) return;
        jindan.stage = Mathf.Max(0, parsed);
        ae.MarkCultiwayStatsDirty();
    }

    protected override string GetInitValue()
    {
        var ae = Actor.GetExtend();
        if (!ae.HasComponent<Jindan>()) return "-";
        var jindan = ae.GetComponent<Jindan>();
        return jindan.formation.IsValid ? jindan.stage.ToString() : "-";
    }
}
#endif
