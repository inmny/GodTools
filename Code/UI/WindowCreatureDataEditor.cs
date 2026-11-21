using System.Collections.Generic;
using GodTools.Abstract;
using GodTools.UI.CreatureDataEditorGrids;
using GodTools.UI.Prefabs;
using GodTools.Utils;
using NeoModLoader.api;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowCreatureDataEditor : AbstractWideWindow<WindowCreatureDataEditor>
{
    internal const string BackpackPage = "backpack";
    private Actor _selected_actor;
    public RectTransform contentRect;
    public RectTransform contentScrollRect;
    public RectTransform pageListContent;
    protected override void Init()
    {
        contentRect = ContentTransform.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new(0, 0);
        contentRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var content_layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
        content_layout.SetSimpleVertLayout(8);
        content_layout.padding = new RectOffset(4, 4, 4, 8);
        content_layout.childControlWidth = true;
        content_layout.childForceExpandWidth = true;

        contentScrollRect = ContentTransform.parent.parent.GetComponent<RectTransform>();
        ConfigureScrollView(contentScrollRect, new Vector2(38, 0), new Vector2(200, 215), true);
        if (GetComponent<ScrollWindow>().scrollingGradient is Image gradient)
            gradient.enabled = false;

        SimpleLine line = Instantiate(SimpleLine.Prefab, BackgroundTransform);
        line.transform.localPosition = new Vector3(-64, 0);
        line.SetSize(new Vector2(2, 215));

        RectTransform page_list_scroll_view = Instantiate(contentScrollRect, BackgroundTransform);
        page_list_scroll_view.name = "Page List Scroll View";
        ConfigureScrollView(page_list_scroll_view, new Vector2(-110, 0), new Vector2(70, 215), false);

        pageListContent = page_list_scroll_view.Find("Viewport/Content").GetComponent<RectTransform>();
        RegisterGrid<BackpackResourceEditor>(BackpackPage);
#if 一米_中文名
        RegisterGrid<FamilyNameEditor>("chinese_name");
#endif
        #if INMNY_CUSTOMMODT001
        RegisterGrid<WudaoTalentEditor>("inmny_custommodt001");
        RegisterGrid<WudaoLevelEditor>("inmny_custommodt001");
        RegisterGrid<WudaoExpEditor>("inmny_custommodt001");
        #endif
        #if CULTIWAY
        RegisterGrid<PowerLevelEditor>("cultiway");
        RegisterGrid<ElementRootEditor>("cultiway");
        RegisterGrid<JindanStrengthEditor>("cultiway");
        RegisterGrid<JindanStageEditor>("cultiway");
        RegisterGrid<YuanyingStrengthEditor>("cultiway");
        RegisterGrid<YuanyingStageEditor>("cultiway");
        #endif
        #if ZHETIAN
        RegisterGrid<ZheTianTalentEditor>("zhetian");
        RegisterGrid<ZheTianLevelEditor>("zhetian");
        #endif
        _current_page = BackpackPage;
    }

    internal static void OpenBackpack()
    {
        Instance._current_page = BackpackPage;
        ScrollWindow.showWindow(WindowId);
    }

    private static void ConfigureScrollView(RectTransform scroll_view, Vector2 position, Vector2 size,
        bool show_scrollbar)
    {
        scroll_view.anchorMin = new Vector2(0.5f, 0.5f);
        scroll_view.anchorMax = new Vector2(0.5f, 0.5f);
        scroll_view.pivot = new Vector2(0.5f, 0.5f);
        scroll_view.anchoredPosition = position;
        scroll_view.localScale = Vector3.one;
        scroll_view.sizeDelta = size;

        var viewport = scroll_view.Find("Viewport").GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(0.5f, 0.5f);
        viewport.offsetMin = new Vector2(4, 4);
        viewport.offsetMax = new Vector2(show_scrollbar ? -12 : -4, -4);

        var scroll_rect = scroll_view.GetComponent<ScrollRect>();
        scroll_rect.horizontal = false;
        scroll_rect.vertical = true;
        scroll_rect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var scrollbar_mask = scroll_view.Find("Scrollbar Vertical Mask").GetComponent<RectTransform>();
        scrollbar_mask.gameObject.SetActive(show_scrollbar);
        if (!show_scrollbar) return;

        scrollbar_mask.anchorMin = new Vector2(1, 0);
        scrollbar_mask.anchorMax = new Vector2(1, 1);
        scrollbar_mask.pivot = new Vector2(1, 0.5f);
        scrollbar_mask.anchoredPosition = new Vector2(-2, 0);
        scrollbar_mask.localScale = Vector3.one;
        scrollbar_mask.sizeDelta = new Vector2(8, -8);

        var scrollbar = scroll_rect.verticalScrollbar.GetComponent<RectTransform>();
        scrollbar.anchorMin = Vector2.zero;
        scrollbar.anchorMax = Vector2.one;
        scrollbar.pivot = new Vector2(0.5f, 0.5f);
        scrollbar.offsetMin = Vector2.zero;
        scrollbar.offsetMax = Vector2.zero;
        scrollbar.localScale = Vector3.one;

        var track = scrollbar.Find("Background").GetComponent<RectTransform>();
        track.anchorMin = Vector2.zero;
        track.anchorMax = Vector2.one;
        track.offsetMin = track.offsetMax = Vector2.zero;
        var handle = scroll_rect.verticalScrollbar.handleRect;
        var slide = (RectTransform)handle.parent;
        slide.anchorMin = Vector2.zero;
        slide.anchorMax = Vector2.one;
        slide.offsetMin = new Vector2(1, 2);
        slide.offsetMax = new Vector2(-1, -2);
        handle.offsetMin = handle.offsetMax = Vector2.zero;
    }

    private void RegisterGrid<T>(string page_name) where T : CreatureDataEditorGrid
    {
        AddPage(page_name);
        var grid = Instantiate(CreatureDataEditorGrid.GetPrefab<T>(), contentRect);
        grid.gameObject.SetActive(false);
        grid.Setup();
        _pages[page_name].Add(grid);
    }
    private void AddPage(string page_name)
    {
        if (_pages.ContainsKey(page_name)) return;
        _pages.Add(page_name, new List<CreatureDataEditorGrid>());
        var page_entry = Instantiate(TextButton.Prefab, pageListContent);
        page_entry.name = page_name;
        page_entry.Setup($"{C.mod_prefix}.ui.data_editor.{page_name}", () =>
        {
            _current_page = page_name;
            UpdatePage();
        });
    }

    private void UpdatePage()
    {
        foreach (var list in _pages.Values)
        {
            foreach (var grid in list)
            {
                grid.gameObject.SetActive(false);
            }
        }

        foreach (TextButton entry in pageListContent.GetComponentsInChildren<TextButton>())
        {
            entry.Button.interactable = entry.name != _current_page;
        }

        if (string.IsNullOrEmpty(_current_page) ||
            !_pages.TryGetValue(_current_page, out var curr_list)) return;
        foreach (var grid in curr_list)
        {
            grid.gameObject.SetActive(true);
            grid.EnabledWith(_selected_actor);
        }
    }

    private Dictionary<string, List<CreatureDataEditorGrid>> _pages = new();
    private string _current_page;
    public override void OnNormalEnable()
    {
        _selected_actor = SelectedUnit.unit;
        UpdatePage();
    }
}
