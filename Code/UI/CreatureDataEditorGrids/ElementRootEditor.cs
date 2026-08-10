#if CULTIWAY
using System.Collections.Generic;
using Cultiway.Const;
using Cultiway.Core.Components;
using Cultiway.Utils.Extension;
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

    // 8 个属性的索引与对应的本地化 key 后缀
    private static readonly int[] _indices =
    {
        ElementIndex.Iron, ElementIndex.Wood, ElementIndex.Water, ElementIndex.Fire,
        ElementIndex.Earth, ElementIndex.Neg, ElementIndex.Pos, ElementIndex.Entropy
    };
    private static readonly string[] _key_suffixes =
    {
        "iron", "wood", "water", "fire", "earth", "neg", "pos", "entropy"
    };

    public override void Setup()
    {
        TitleKey = "inmny.godtools.ui.data_editor.cultiway.element_root";
        GetComponent<VerticalLayoutGroup>().spacing = 2;
        for (int i = 0; i < _indices.Length; i++)
        {
            int idx = _indices[i];
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
        var ae = _actor.GetExtend();
        if (!ae.HasElementRoot())
        {
            // 没有灵根时无法逐项编辑，直接忽略（赋予灵根由 Roll 按钮负责）
            return;
        }

        float[] composition = new float[8];
        var root = ae.GetElementRoot();
        for (int i = 0; i < 8; i++) composition[i] = root[_indices[i]];
        composition[slot] = Mathf.Max(0, parsed);

        // 整体替换：移除旧组件，添加由新数组构造的灵根
        ae.E.RemoveComponent<ElementRoot>();
        ae.E.AddComponent(new ElementRoot(composition));
        ae.MarkCultiwayStatsDirty();
    }

    public override void EnabledWith(Actor actor)
    {
        _actor = actor;
        var ae = actor.GetExtend();
        if (!ae.HasElementRoot())
        {
            for (int i = 0; i < _inputs.Count; i++)
            {
                int captured = i;
                _inputs[i].Setup("-", value => OnValueChanged(captured, value));
                _inputs[i].input.interactable = false;
            }
            return;
        }

        var root = ae.GetElementRoot();
        for (int i = 0; i < _inputs.Count; i++)
        {
            int captured = i;
            float v = root[_indices[i]];
            _inputs[i].Setup(v.ToString("F2"), value => OnValueChanged(captured, value));
            _inputs[i].input.interactable = true;
        }
    }
}
#endif
