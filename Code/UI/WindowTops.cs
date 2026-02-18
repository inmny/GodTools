using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DG.Tweening;
using GodTools.Abstract;
using GodTools.UI.Prefabs;
using GodTools.UI.Prefabs.TopElementPrefabs;
using GodTools.UI.WindowTopComponents;
using GodTools.Utils;
using NeoModLoader.api;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using NeoModLoader.General.Game.extensions;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowTops : AbstractWideWindow<WindowTops>
{
    private const float single_element_height = 40;
    private const float start_y               = -single_element_height / 2;
    private const int   segment_chart_expand_threshold = 8;
    private const float segment_chart_default_width = 190f;
    private const float segment_chart_expanded_width = 600f;
    private const float segment_chart_height = 44f;
    private const float segment_chart_bar_max_height = 18f;
    private const float segment_chart_min_bar_width = 8f;

    private readonly List<SortKey>                     sort_keys = new();
    
    private readonly HashSet<FilterSetting> last_and_filter_settings = new();
    private readonly HashSet<FilterSetting> last_or_filter_settings = new();
    private readonly HashSet<FilterSetting> last_not_filter_settings = new();
    private readonly List<FilterSetting> all_filter_settings = new();
    
    private          List<Actor>                       _list;
    private          ObjectPoolGenericMono<TopElementActor> _pool;
    private          RectTransform                     content_rect;
    private          MonoObjPool<FilterButton>         filter_button_pool;

    private RectTransform filter_content_rect;
    private RectTransform keyword_content_rect;
    private int           last_view_end = -1;

    private int           last_view_start = 9999;
    private RectTransform scroll_view_rect;

    private SingleRowGrid selected_filters;
    private SingleRowGrid selected_keywords;

    private MonoObjPool<SortKeyButton> sort_button_pool;
    private bool                       ui_filters_dirty = true;

    private bool ui_sort_keys_dirty = true;

    private RectTransform segment_chart_rect;
    private RectTransform segment_chart_bars_rect;
    private Text          segment_chart_hint_text;

    private void Update()
    {
        if (Initialized) OnUpdate();
    }

    protected override void Init()
    {
        content_rect = ContentTransform.GetComponent<RectTransform>();
        scroll_view_rect = content_rect.parent.parent.GetComponent<RectTransform>();
        scroll_view_rect.localPosition = new Vector3(0, 10);
        scroll_view_rect.sizeDelta = new Vector2(200, 230);
        scroll_view_rect.Find("Scrollbar Vertical Mask").gameObject.SetActive(false);
        content_rect.pivot = new Vector2(0.5f,        1);
        BackgroundTransform.Find("Scrollgradient").localPosition = new(105, -106);
        BackgroundTransform.Find("Scrollgradient").GetComponent<RectTransform>().sizeDelta = new(40, 210);
/*
        SimpleLine line1 = Instantiate(SimpleLine.Prefab, BackgroundTransform);
        line1.transform.localPosition = new Vector3(-100, 0);
        line1.SetSize(new Vector2(2, 250));
        SimpleLine line2 = Instantiate(SimpleLine.Prefab, BackgroundTransform);
        line2.transform.localPosition = new Vector3(100, 0);
        line2.SetSize(new Vector2(2, 250));
*/
        RectTransform filter_scroll_view = Instantiate(scroll_view_rect, BackgroundTransform);
        filter_scroll_view.localPosition = new Vector3(-200, 0);
        filter_scroll_view.name = "Filter Scroll View";
        RectTransform keyword_scroll_view = Instantiate(scroll_view_rect, BackgroundTransform);
        keyword_scroll_view.localPosition = new Vector3(200, 0);
        keyword_scroll_view.name = "Keyword Scroll View";
        keyword_scroll_view.sizeDelta = new Vector3(170, 230);

        
        scroll_view_rect.Find("Scrollbar Vertical Mask").gameObject.SetActive(true);
        scroll_view_rect.Find("Scrollbar Vertical Mask").transform.localPosition = new(307.5f, 0, 0);

        filter_content_rect = filter_scroll_view.Find("Viewport/Content").GetComponent<RectTransform>();
        keyword_content_rect = keyword_scroll_view.Find("Viewport/Content").GetComponent<RectTransform>();

        filter_content_rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.MinSize;
        keyword_content_rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.MinSize;
        filter_content_rect.gameObject.AddComponent<VerticalLayoutGroup>().SetSimpleVertLayout();
        keyword_content_rect.gameObject.AddComponent<VerticalLayoutGroup>().SetSimpleVertLayout();


        selected_filters = Instantiate(SingleRowGrid.Prefab,  filter_content_rect);
        selected_keywords = Instantiate(SingleRowGrid.Prefab, keyword_content_rect);

        selected_filters.Setup($"{C.mod_prefix}.ui.filter", new Vector2(180,   35), new Vector2(28, 28));
        selected_keywords.Setup($"{C.mod_prefix}.ui.keyword", new Vector2(180, 35), new Vector2(28, 28));
        
        
        CreateGrid_VANILLA();
#if INMNY_CUSTOMMODT001
        CreateGrid_INMNY_CUSTOMMODT001();
#endif
#if INMNY_CUSTOMMODT002
        CreateGrid_INMNY_CUSTOMMODT002();
#endif
#if CULTIWAY
        CreateGrid_CULTIWAY();
#endif
#if WITCHCRAFT_WORLDBOX
        CreateGrid_WITCHCRAFT_WORLDBOX();
#endif
#if WITCHCRAFT_KNIGHT
        CreateGrid_WITCHCRAFT_KNIGHT();
#endif
#if XULIE
        CreateGrid_XULIE();
#endif
#if ZHETIAN
        CreateGrid_ZHETIAN();
#endif
#if DOUPOCANGQIONG
        CreateGrid_DOUPOCANGQIONG();
#endif
#if WARRIOR
        CreateGrid_WARRIOR();
#endif
#if XIAN_TU
        CreateGrid_XIAN_TU();
#endif
#if XIAN_NI_MOD
        CreateGrid_XIANNI();
#endif
#if Thefantasyworld
        CreateGrid_FANTASY_WORLD();
#endif
#if CHEVALIER
        CreateGrid_CHEVALIER();
#endif
        var to_top_button = RawLocalizedText.Instantiate(BackgroundTransform, pName: "ToTop");
        to_top_button.SetSize(new(30,10));
        to_top_button.Setup($"{C.mod_prefix}.ui.to_top", alignment: TextAnchor.MiddleLeft);
        to_top_button.transform.localPosition = new(-70, -120);
        to_top_button.gameObject.AddComponent<Button>().onClick.AddListener(ToTop);
        var to_bottom_button = RawLocalizedText.Instantiate(BackgroundTransform, pName: "ToBottom");
        to_bottom_button.SetSize(new(30,10));
        to_bottom_button.Setup($"{C.mod_prefix}.ui.to_bottom", alignment: TextAnchor.MiddleRight);
        to_bottom_button.transform.localPosition = new(70, -120);
        to_bottom_button.gameObject.AddComponent<Button>().onClick.AddListener(ToBottom);

        var jump_input = TextInput.Instantiate(BackgroundTransform, pName: "Jump");
        jump_input.Setup("", CheckJump);
        jump_input.SetSize(new(80, 20));
        jump_input.text.alignment = TextAnchor.MiddleCenter;
        jump_input.input.characterValidation = InputField.CharacterValidation.Integer;
        jump_input.transform.localPosition = new(0, -118);
        var placeholder_text = RawLocalizedText.Instantiate(jump_input.input.transform, pName: "Placeholder");
        placeholder_text.Setup($"{C.mod_prefix}.ui.jump_to", new Color(1,1,1,0.3f),alignment: TextAnchor.MiddleCenter);
        placeholder_text.SetSize(new(60, 20));
        placeholder_text.transform.localPosition = new(33, 0);
        jump_input.input.placeholder = placeholder_text.Text;

        CreateSegmentChart();
        RefreshSegmentChart();

        _pool = new ObjectPoolGenericMono<TopElementActor>(TopElementActor.Prefab, ContentTransform);
        sort_button_pool = new MonoObjPool<SortKeyButton>(SortKeyButton.Prefab, selected_keywords.Grid.transform);
        filter_button_pool = new MonoObjPool<FilterButton>(FilterButton.Prefab, selected_filters.Grid.transform);
    }

    [Hotfixable]
    public void ApplySort()
    {
        _list = World.world.units.getSimpleList().FindAll([Hotfixable](x)=>x!=null && x.data != null&& x.isAlive() && x.asset.can_be_inspected);

        if (last_and_filter_settings.Count > 0)
        {
            foreach (FilterSetting setting in last_and_filter_settings)
            {
                _list = _list.FindAll(setting.FilterFunc.Invoke);
            }
        }
        if (last_or_filter_settings.Count > 0)
        {
            List<Actor> or_list = new();
            foreach (FilterSetting setting in last_or_filter_settings)
            {
                or_list.AddRange(_list.FindAll(setting.FilterFunc.Invoke));
            }
            if (last_and_filter_settings.Count > 0)
                _list = _list.Intersect(or_list).ToList();
            else
                _list = or_list;
        }
        if (last_not_filter_settings.Count > 0)
        {
            foreach (FilterSetting setting in last_not_filter_settings)
            {
                _list = _list.FindAll(x => !setting.FilterFunc.Invoke(x));
            }
        }
        
        content_rect.sizeDelta = new Vector2(0, (_list.Count + 1) * single_element_height);
        _list.Sort((a, b) =>
        {
            foreach (SortKey key in sort_keys)
            {
                var result = key.Compare(a, b);
                if (result != 0)
                    return result;
            }

            return 0;
        });
        last_view_start = 9999;
        last_view_end = -1;
        _pool.clear();
        RefreshSegmentChart();
    }

    private void CreateSegmentChart()
    {
        var chart_obj = new GameObject("SegmentChart", typeof(RectTransform), typeof(Image));
        chart_obj.transform.SetParent(BackgroundTransform);
        chart_obj.transform.localScale = Vector3.one;

        segment_chart_rect = chart_obj.GetComponent<RectTransform>();
        segment_chart_rect.pivot = new Vector2(0.5f, 0.5f);
        segment_chart_rect.sizeDelta = new Vector2(segment_chart_default_width, segment_chart_height);
        segment_chart_rect.localPosition = new Vector3(0, -170);

        var chart_bg = chart_obj.GetComponent<Image>();
        chart_bg.color = new Color(0, 0, 0, 0.35f);
        chart_bg.raycastTarget = false;

        var bars_obj = new GameObject("Bars", typeof(RectTransform));
        bars_obj.transform.SetParent(segment_chart_rect);
        bars_obj.transform.localScale = Vector3.one;
        segment_chart_bars_rect = bars_obj.GetComponent<RectTransform>();
        segment_chart_bars_rect.pivot = new Vector2(0.5f, 0.5f);
        segment_chart_bars_rect.sizeDelta = new Vector2(segment_chart_default_width - 4, segment_chart_height - 2);
        segment_chart_bars_rect.localPosition = Vector3.zero;

        var hint_obj = new GameObject("Hint", typeof(Text));
        hint_obj.transform.SetParent(segment_chart_rect);
        hint_obj.transform.localScale = Vector3.one;
        hint_obj.transform.localPosition = Vector3.zero;
        segment_chart_hint_text = hint_obj.GetComponent<Text>();
        segment_chart_hint_text.rectTransform.sizeDelta = new Vector2(segment_chart_default_width - 8, segment_chart_height - 2);
        segment_chart_hint_text.font = LocalizedTextManager.current_font;
        segment_chart_hint_text.color = new Color(1, 1, 1, 0.7f);
        segment_chart_hint_text.alignment = TextAnchor.MiddleCenter;
        segment_chart_hint_text.resizeTextForBestFit = true;
        segment_chart_hint_text.resizeTextMaxSize = 9;
        segment_chart_hint_text.resizeTextMinSize = 1;
        segment_chart_hint_text.horizontalOverflow = HorizontalWrapMode.Wrap;
        segment_chart_hint_text.verticalOverflow = VerticalWrapMode.Truncate;
        segment_chart_hint_text.raycastTarget = false;
    }

    [Hotfixable]
    private void RefreshSegmentChart()
    {
        if (segment_chart_rect == null || segment_chart_bars_rect == null || segment_chart_hint_text == null)
        {
            return;
        }

        for (var i = segment_chart_bars_rect.childCount - 1; i >= 0; i--)
        {
            Destroy(segment_chart_bars_rect.GetChild(i).gameObject);
        }

        if (_list == null || _list.Count == 0)
        {
            segment_chart_hint_text.gameObject.SetActive(true);
            segment_chart_hint_text.text = LM.Get($"{C.mod_prefix}.ui.segment_chart.no_data");
            return;
        }

        if (sort_keys.Count == 0)
        {
            segment_chart_hint_text.gameObject.SetActive(true);
            segment_chart_hint_text.text = LM.Get($"{C.mod_prefix}.ui.segment_chart.select_keyword");
            return;
        }

        var segments = BuildSegmentDistribution(sort_keys[0]);
        if (segments.Count == 0)
        {
            segment_chart_hint_text.gameObject.SetActive(true);
            segment_chart_hint_text.text = LM.Get($"{C.mod_prefix}.ui.segment_chart.no_data");
            return;
        }

        var chart_width = segments.Count > segment_chart_expand_threshold
            ? segment_chart_expanded_width
            : segment_chart_default_width;
        var max_column_count = GetMaxColumnCountByChartWidth(chart_width);
        if (segments.Count > max_column_count)
        {
            segments = MergeSegments(segments, max_column_count);
        }
        SetSegmentChartWidth(chart_width);

        segment_chart_hint_text.gameObject.SetActive(false);

        var max_count = segments.Max(x => x.Count);
        var bar_count = segments.Count;
        var total_width = segment_chart_bars_rect.sizeDelta.x;
        var gap = Mathf.Max(1f, total_width * 0.01f);
        var bar_width = Mathf.Max(4f, (total_width - gap * (bar_count - 1)) / bar_count);
        var start_x = -total_width / 2 + bar_width / 2;
        var axis_y = -8f;

        for (var i = 0; i < bar_count; i++)
        {
            var segment = segments[i];
            var bar_height = Mathf.Max(1f, segment.Count / (float)max_count * segment_chart_bar_max_height);
            var column_x = start_x + i * (bar_width + gap);

            var column_obj = new GameObject($"SegmentColumn{i}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
            column_obj.transform.SetParent(segment_chart_bars_rect);
            column_obj.transform.localScale = Vector3.one;

            var column_rect = column_obj.GetComponent<RectTransform>();
            column_rect.pivot = new Vector2(0.5f, 0f);
            column_rect.sizeDelta = new Vector2(bar_width, segment_chart_bar_max_height + 2f);
            column_rect.localPosition = new Vector3(column_x, axis_y);
            var column_hit_image = column_obj.GetComponent<Image>();
            column_hit_image.color = new Color(1f, 1f, 1f, 0.001f);

            var bar_obj = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar_obj.transform.SetParent(column_rect);
            bar_obj.transform.localScale = Vector3.one;

            var bar_rect = bar_obj.GetComponent<RectTransform>();
            bar_rect.pivot = new Vector2(0.5f, 0f);
            bar_rect.sizeDelta = new Vector2(bar_width, bar_height);
            bar_rect.localPosition = Vector3.zero;

            var bar_color_lerp = bar_count == 1 ? 0 : i / (float)(bar_count - 1);
            bar_obj.GetComponent<Image>().color = Color.Lerp(new Color(0.2f, 0.75f, 1f, 0.95f),
                new Color(0.95f, 0.55f, 0.22f, 0.95f), bar_color_lerp);

            var tip_key = $"{C.mod_prefix}.ui.segment_chart.dynamic.{i}";
            LM.AddToCurrentLocale(tip_key, $"{segment.Label}: {segment.Count}");
            column_obj.GetComponent<TipButton>().textOnClick = tip_key;

            var label_obj = new GameObject($"XAxisLabel{i}", typeof(Text));
            label_obj.transform.SetParent(segment_chart_bars_rect);
            label_obj.transform.localScale = Vector3.one;
            label_obj.transform.localPosition = new Vector3(column_x, axis_y - 8f);
            var label = label_obj.GetComponent<Text>();
            label.font = LocalizedTextManager.current_font;
            label.alignment = TextAnchor.UpperCenter;
            label.color = new Color(1f, 1f, 1f, 0.85f);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 1;
            label.resizeTextMaxSize = 9;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.rectTransform.sizeDelta = new Vector2(bar_width + 8f, 10f);
            label.text = FormatXAxisLabel(segment.Label);
            label.raycastTarget = false;
        }
    }

    [Hotfixable]
    private List<SegmentDistribution> BuildSegmentDistribution(SortKey major_sort_key)
    {
        var segments = BuildDisplaySegments(major_sort_key);
        if (segments.Count == 0)
        {
            segments = BuildRankSegments();
        }

        if (!major_sort_key.IsAscend)
        {
            segments.Reverse();
        }

        return MergeSegmentsByDisplayedLabel(segments);
    }

    private static string FormatXAxisLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return "-";
        }

        var trimmed = label.Trim();
        if (trimmed.Length == 0)
        {
            return "-";
        }

        if (!char.IsDigit(trimmed[0]))
        {
            return trimmed;
        }

        var num_end = 0;
        var has_dot = false;
        while (num_end < trimmed.Length)
        {
            var c = trimmed[num_end];
            if (char.IsDigit(c))
            {
                num_end++;
                continue;
            }

            if (c == '.' && !has_dot)
            {
                has_dot = true;
                num_end++;
                continue;
            }

            break;
        }

        if (num_end <= 0)
        {
            return trimmed;
        }

        var num_text = trimmed.Substring(0, num_end);
        if (double.TryParse(num_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.TryParse(num_text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return ((int)value).ToString();
        }

        return trimmed;
    }

    private List<SegmentDistribution> BuildDisplaySegments(SortKey major_sort_key)
    {
        var segment_counts = new Dictionary<string, int>();
        var segment_orders = new List<string>();

        foreach (var actor in _list)
        {
            var label = major_sort_key.GetMajorKeyDisplay(actor).Trim();
            if (string.IsNullOrEmpty(label))
            {
                continue;
            }

            if (segment_counts.TryGetValue(label, out var count))
            {
                segment_counts[label] = count + 1;
            }
            else
            {
                segment_counts[label] = 1;
                segment_orders.Add(label);
            }
        }

        return segment_orders.Select(label => new SegmentDistribution(label, segment_counts[label])).ToList();
    }

    private List<SegmentDistribution> BuildRankSegments()
    {
        var result = new List<SegmentDistribution>();
        var segment_count = Math.Min(segment_chart_expand_threshold, _list.Count);
        if (segment_count <= 0)
        {
            return result;
        }

        var segment_size = Mathf.CeilToInt(_list.Count / (float)segment_count);
        for (var i = 0; i < segment_count; i++)
        {
            var start = i * segment_size;
            if (start >= _list.Count)
            {
                break;
            }

            var end = Math.Min(_list.Count - 1, start + segment_size - 1);
            result.Add(new SegmentDistribution($"{start + 1}-{end + 1}", end - start + 1));
        }

        return result;
    }

    private static List<SegmentDistribution> MergeSegmentsByDisplayedLabel(IReadOnlyList<SegmentDistribution> segments)
    {
        var merged_counts = new Dictionary<string, int>();
        var merged_orders = new List<string>();

        foreach (var segment in segments)
        {
            var display_label = FormatXAxisLabel(segment.Label);
            if (merged_counts.TryGetValue(display_label, out var count))
            {
                merged_counts[display_label] = count + segment.Count;
            }
            else
            {
                merged_counts[display_label] = segment.Count;
                merged_orders.Add(display_label);
            }
        }

        return merged_orders.Select(label => new SegmentDistribution(label, merged_counts[label])).ToList();
    }

    private void SetSegmentChartWidth(float width)
    {
        segment_chart_rect.sizeDelta = new Vector2(width, segment_chart_height);
        segment_chart_bars_rect.sizeDelta = new Vector2(width - 4f, segment_chart_height - 2f);
        segment_chart_hint_text.rectTransform.sizeDelta = new Vector2(width - 8f, segment_chart_height - 2f);
    }

    private static int GetMaxColumnCountByChartWidth(float chart_width)
    {
        var bars_width = chart_width - 4f;
        var gap = Mathf.Max(1f, bars_width * 0.01f);
        return Mathf.Max(1, Mathf.FloorToInt((bars_width + gap) / (segment_chart_min_bar_width + gap)));
    }

    private static List<SegmentDistribution> MergeSegments(IReadOnlyList<SegmentDistribution> segments, int max_segment_count)
    {
        if (segments.Count <= max_segment_count)
        {
            return segments.ToList();
        }

        var merged_segments = new List<SegmentDistribution>(max_segment_count);
        var group_size = Mathf.CeilToInt(segments.Count / (float)max_segment_count);
        for (var i = 0; i < segments.Count; i += group_size)
        {
            var end = Math.Min(segments.Count - 1, i + group_size - 1);
            var count = 0;
            for (var j = i; j <= end; j++)
            {
                count += segments[j].Count;
            }

            var label = i == end ? segments[i].Label : $"{segments[i].Label}~{segments[end].Label}";
            merged_segments.Add(new SegmentDistribution(label, count));
        }

        return merged_segments;
    }

    private class SegmentDistribution
    {
        public readonly string Label;
        public readonly int    Count;

        public SegmentDistribution(string label, int count)
        {
            Label = label;
            Count = count;
        }
    }

    private TitledGrid new_filter_grid(string filter_type)
    {
        TitledGrid grid = Instantiate(TitledGrid.Prefab, filter_content_rect);
        var group_id = $"{C.mod_prefix}.ui.filter.{filter_type}";
        grid.Setup(group_id, 180, new Vector2(28, 28), new Vector2(4, 4));
        grid.Title.gameObject.AddComponent<Button>().onClick.AddListener([Hotfixable]() =>
        {
            grid.Grid.gameObject.SetActive(!grid.Grid.gameObject.activeSelf);
            LayoutRebuilder.MarkLayoutForRebuild(filter_content_rect);
        });
        grid.Title.gameObject.AddComponent<TipButton>().textOnClick = $"{C.mod_prefix}.ui.filter.expand_or_retract";
        return grid;
    }

    private TitledGrid new_keyword_grid(string keyword_type)
    {
        TitledGrid grid = Instantiate(TitledGrid.Prefab, keyword_content_rect);
        grid.Setup($"{C.mod_prefix}.ui.keyword.{keyword_type}", 180, new Vector2(28, 28), new Vector2(4, 4));
        grid.Title.gameObject.AddComponent<Button>().onClick.AddListener([Hotfixable]() =>
        {
            grid.Grid.gameObject.SetActive(!grid.Grid.gameObject.activeSelf);
            LayoutRebuilder.MarkLayoutForRebuild(keyword_content_rect);
        });
        grid.Title.gameObject.AddComponent<TipButton>().textOnClick = $"{C.mod_prefix}.ui.filter.expand_or_retract";
        return grid;
    }
    private Dictionary<TitledGrid, MonoObjPool<FilterButtonInGrid>> filter_button_in_grid_pool_dict = new();
    
    private FilterButtonInGrid new_filter(TitledGrid grid, string filter_id, string icon_path, Func<Actor, bool> filter_func)
    {
        if (!filter_button_in_grid_pool_dict.ContainsKey(grid))
        {
            filter_button_in_grid_pool_dict[grid] = new MonoObjPool<FilterButtonInGrid>(FilterButtonInGrid.Prefab, grid.Grid.transform);
        }
        var button = filter_button_in_grid_pool_dict[grid].GetNext();
        button.Setup($"{grid.Title.GetComponent<LocalizedText>().key}.{filter_id}", icon_path, filter_func, all_filter_settings);
        return button;
    }private FilterButtonInGrid new_filter(TitledGrid grid, FilterSetting filter_setting)
    {
        if (!filter_button_in_grid_pool_dict.ContainsKey(grid))
        {
            filter_button_in_grid_pool_dict[grid] = new MonoObjPool<FilterButtonInGrid>(FilterButtonInGrid.Prefab, grid.Grid.transform);
        }
        var button = filter_button_in_grid_pool_dict[grid].GetNext();
        var id_replaced_filter_setting =
            new FilterSetting($"{grid.Title.GetComponent<LocalizedText>().key}.{filter_setting.ID}",
                filter_setting.Icon, filter_setting.FilterFunc)
            {
                InnerIcon = filter_setting.InnerIcon,
                IconColor = filter_setting.IconColor,
                InnerColor = filter_setting.InnerColor,
                Type = filter_setting.Type
            };
        button.Setup(id_replaced_filter_setting, all_filter_settings);
        return button;
    }

    private void new_keyword(TitledGrid grid, string keyword_id, string icon_path, Func<Actor, Actor, int> compare_func, Func<Actor,string> major_key_disp = null)
    {
        var obj = new GameObject(keyword_id, typeof(Image), typeof(Button), typeof(TipButton));
        obj.transform.SetParent(grid.Grid.GetComponent<RectTransform>());
        obj.transform.localScale = Vector3.one;
        var icon = obj.GetComponent<Image>();
        icon.sprite = SpriteTextureLoader.getSprite(icon_path);
        var tip_button = obj.GetComponent<TipButton>();
        keyword_id = $"{grid.Title.GetComponent<LocalizedText>().key}.{keyword_id}";
        tip_button.textOnClick = keyword_id;
        var button = obj.GetComponent<Button>();
        var local_keyword_id = keyword_id;
        var local_compare = compare_func;
        var local_disp = major_key_disp;
        Sprite local_sprite = icon.sprite;
        button.onClick.AddListener(() =>
        {
            var key_idx = sort_keys.FindIndex(x => x.ID == local_keyword_id);
            if (key_idx == -1)
            {
                var key = new SortKey(local_keyword_id, local_compare, local_sprite, local_disp);
                sort_keys.Add(key);
            }
            else
            {
                sort_keys.RemoveAt(key_idx);
            }

            ui_sort_keys_dirty = true;
            ApplySort();
        });
    }
    [Hotfixable]
    public override void OnNormalEnable()
    {
        CheckDynamicGrid_VANILLA();
        ApplySort();
    }

    [Hotfixable]
    public void ToTop()
    {
        ContentTransform.DOLocalMoveY(0, 1);
    }
    [Hotfixable]
    public void ToBottom()
    {
        ContentTransform.DOLocalMoveY(ContentTransform.GetComponent<RectTransform>().sizeDelta.y - scroll_view_rect.sizeDelta.y, 1);
    }
    [Hotfixable]
    private void CheckJump(string num)
    {
        if (int.TryParse(num, out var idx))
        {
            idx = Math.Min(_list.Count - 1, Math.Max(0, idx));
            ContentTransform.DOLocalMoveY(idx * single_element_height, 1);
        }
    }
    [Hotfixable]
    private void OnUpdate()
    {
        CheckFilters();
        Vector3 curr_position = ContentTransform.localPosition;

        var view_y_start = curr_position.y;
        var view_y_end = curr_position.y + scroll_view_rect.sizeDelta.y;

        var view_start_idx = (int)(view_y_start / single_element_height);
        var view_end_idx = Math.Min((int)(view_y_end / single_element_height) + 1, _list.Count - 1);

        var actual_start_y = start_y - view_start_idx * single_element_height;
        var actual_end_y = start_y   - view_end_idx   * single_element_height;
        foreach (TopElementActor elm in _pool._elements_total)
        {
            GameObject game_object = elm.gameObject;
            if (!game_object.activeSelf) continue;

            var y = game_object.transform.localPosition.y;
            if (y > actual_start_y || y < actual_end_y)
            {
                game_object.SetActive(false);
                _pool._elements_inactive.Enqueue(elm);
            }
        }

        SortKey major_sort_key = null;
        if (sort_keys.Count > 0)
        {
            major_sort_key = sort_keys[0];
        }

        for (var i = view_start_idx; i <= view_end_idx; i++)
            if (i < last_view_start || i > last_view_end)
            {
                // 需要显示
                Actor actor = _list[i];
                TopElementActor comp = _pool.getNext();
                comp.Setup(actor, i, major_sort_key?.GetMajorKeyDisplay(actor) ?? "");
                comp.transform.localPosition = new Vector3(0, start_y - i * single_element_height);
            }

        last_view_start = view_start_idx;
        last_view_end = view_end_idx;

        if (ui_sort_keys_dirty)
        {
            sort_button_pool.Clear();
            foreach (SortKey key in sort_keys)
            {
                SortKeyButton button = sort_button_pool.GetNext();
                button.Setup(key);
            }

            ui_sort_keys_dirty = false;
        }

        if (ui_filters_dirty)
        {
            filter_button_pool.Clear();
            foreach (FilterSetting filter in all_filter_settings)
            {
                FilterButton button = filter_button_pool.GetNext();
                button.Setup(filter);
            }

            ui_filters_dirty = false;
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(filter_content_rect);
    }
    [Hotfixable]
    private void CheckFilters()
    {
        bool need_refresh = false;
        if (last_not_filter_settings.Count + last_and_filter_settings.Count + last_or_filter_settings.Count !=
            all_filter_settings.Count ||  all_filter_settings.Any(x => x.ToBeRemoved)) 
        {
            ui_filters_dirty = true;
            need_refresh = true;
        }
        need_refresh |= last_not_filter_settings.Any(x => x.Type != FilterType.Not);
        need_refresh |= last_and_filter_settings.Any(x => x.Type != FilterType.And);
        need_refresh |= last_or_filter_settings.Any(x => x.Type != FilterType.Or);
        
        last_not_filter_settings.Clear();
        last_and_filter_settings.Clear();
        last_or_filter_settings.Clear();
        all_filter_settings.RemoveAll(x => x.ToBeRemoved);
        foreach (var setting in all_filter_settings)
        {
            switch (setting.Type)
            {
                case FilterType.Not:
                    last_not_filter_settings.Add(setting);
                    break;
                case FilterType.And:
                    last_and_filter_settings.Add(setting);
                    break;
                case FilterType.Or:
                    last_or_filter_settings.Add(setting);
                    break;
            }
        }

        if (need_refresh)
        {
            ApplySort();
        }
    }

    private class SortKey
    {
        private readonly Func<Actor, Actor, int> _compare;
        public readonly  Sprite                  Icon;
        private readonly Func<Actor, string> _major_key_disp;

        public readonly string ID;

        public SortKey(string id, Func<Actor, Actor, int> compare, Sprite icon, Func<Actor, string> major_key_disp)
        {
            ID = id;
            _compare = compare;
            _major_key_disp = major_key_disp;
            Icon = icon;
        }

        public bool IsAscend { get; private set; } = true;

        public int Compare(Actor a, Actor b)
        {
            return _compare(a, b) * (IsAscend ? 1 : -1);
        }
        public string GetMajorKeyDisplay(Actor actor)
        {
            return _major_key_disp?.Invoke(actor) ?? "";
        }

        public void Switch()
        {
            IsAscend = !IsAscend;
        }
    }

    private class SortKeyButton : APrefab<SortKeyButton>
    {
        public void Setup(SortKey key)
        {
            GetComponent<Image>().sprite = key.Icon;
            GetComponent<Button>().onClick.RemoveAllListeners();
            GetComponent<Button>().onClick.AddListener(() =>
            {
                key.Switch();
                Instance.ApplySort();
                Instance.ui_sort_keys_dirty = true;
            });
            GetComponent<TipButton>().textOnClick = key.ID;
            GetComponent<TipButton>().textOnClickDescription = key.IsAscend
                ? $"{C.mod_prefix}.ui.sort.ascending"
                : $"{C.mod_prefix}.ui.sort.descending";
            transform.Find("Arrow").GetComponent<Image>().sprite =
                SpriteTextureLoader.getSprite(key.IsAscend ? "ui/icons/iconArrowDOWN" : "ui/icons/iconArrowUP");
        }

        private static void _init()
        {
            var obj = new GameObject(nameof(SortKeyButton), typeof(Image), typeof(Button), typeof(TipButton),
                typeof(HorizontalLayoutGroup));
            obj.transform.SetParent(Main.prefabs);

            var arrow = new GameObject("Arrow", typeof(Image));
            arrow.transform.SetParent(obj.transform);

            Prefab = obj.AddComponent<SortKeyButton>();
        }
    }
}
