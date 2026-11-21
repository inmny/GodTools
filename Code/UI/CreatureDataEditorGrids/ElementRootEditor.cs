using System.Collections.Generic;
using GodTools.Features.Rankings;
using GodTools.UI.Prefabs;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

/// <summary>
/// 灵根(天赋)编辑器：编辑金木水火土/阴/阳/熵共 8 个属性。
/// ElementRoot 的字段为 private set、indexer setter 也是 private，跨程序集无法直接赋值，
/// 因此采用“读出当前 8 值 → 构造全新 ElementRoot(float[8]) → 移除旧组件再添加新组件”的整体替换路径。
/// </summary>
public class ElementRootEditor : CreatureDataEditorGrid
{
    private Actor _actor;
    private readonly List<TextInput> _inputs = new();

    private static readonly string[] _key_suffixes =
    {
        "iron", "wood", "water", "fire", "earth", "neg", "pos", "entropy"
    };

    public override void Setup()
    {
        TitleKey = "inmny.godtools.ui.data_editor.cultiway.element_root";
        GetComponent<VerticalLayoutGroup>().spacing = 2;
        for (int i = 0; i < _key_suffixes.Length; i++)
        {
            string suffix = _key_suffixes[i];
            var row = new GameObject($"Row_{suffix}", typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(transform, false);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(176, 18);
            var row_layout = row.GetComponent<HorizontalLayoutGroup>();
            row_layout.spacing = 4;
            row_layout.childAlignment = TextAnchor.MiddleCenter;
            row_layout.childControlHeight = false;
            row_layout.childControlWidth = false;
            row_layout.childForceExpandHeight = false;
            row_layout.childForceExpandWidth = false;
            row.GetComponent<LayoutElement>().preferredHeight = 18;

            var label = RawLocalizedText.Instantiate(row.transform, pName: $"Label_{suffix}");
            label.Setup($"inmny.godtools.ui.data_editor.cultiway.element_root.{suffix}",
                alignment: TextAnchor.MiddleRight);
            label.SetSize(new Vector2(36, 18));

            var input = TextInput.Instantiate(row.transform, pName: $"Input_{suffix}");
            int captured = i;
            input.Setup("", value => OnValueChanged(captured, value));
            input.SetSize(new(136, 18));
            input.GetComponent<RectTransform>().pivot = new(0.5f, 1);
            input.input.characterValidation = InputField.CharacterValidation.Decimal;
            _inputs.Add(input);
        }
    }

    private void OnValueChanged(int slot, string value)
    {
        if (_actor == null) return;
        if (!float.TryParse(value, out float parsed)) return;
        var api = CultiwayCompatibility.Find();
        var root = api.Component(_actor, "ElementRoot");
        if (root == null) return;
        float[] composition = new float[_key_suffixes.Length];
        for (int i = 0; i < composition.Length; i++) composition[i] = CultiwayCompatibility.RootElement(root, i);
        composition[slot] = Mathf.Max(0, parsed);
        api.SetComponent(_actor, api.CreateRoot(composition));
    }

    public override void EnabledWith(Actor actor)
    {
        _actor = actor;
        var root = CultiwayCompatibility.Find().Component(actor, "ElementRoot");
        if (root == null)
        {
            for (int i = 0; i < _inputs.Count; i++)
            {
                int captured = i;
                _inputs[i].Setup("-", value => OnValueChanged(captured, value));
                _inputs[i].input.interactable = false;
            }
            return;
        }

        for (int i = 0; i < _inputs.Count; i++)
        {
            int captured = i;
            float v = CultiwayCompatibility.RootElement(root, i);
            _inputs[i].Setup(v.ToString("F2"), value => OnValueChanged(captured, value));
            _inputs[i].input.interactable = true;
        }
    }
}
