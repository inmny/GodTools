using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.UI.Prefabs;
using GodTools.Utils;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowWorldScript
{
    private static readonly Color TitleColor = new(1f, 0.65f, 0.17f);
    private static readonly Color ErrorColor = new(1f, 0.55f, 0.4f);
    private static readonly Color SecondaryText = new(0.83f, 0.81f, 0.72f);
    private static readonly Color EnabledText = new(0.78f, 0.9f, 0.56f);
    private static GameObject _nativeTab;
    private static GameObject _nativeSwitch;
    private static UnfoldButton _nativeUnfold;

    private enum Surface { Command, Primary, Quiet, Field, List, Destructive }
    private enum PickerLayout { List, Traits, Icons }
    private sealed class Option
    {
        public readonly string Label;
        public readonly Action Apply;
        public readonly Sprite Icon;
        public readonly bool Selected;
        public readonly string GroupID;

        public Option(string label, Action apply, Sprite icon = null, bool selected = false, string groupID = null)
        {
            Label = label;
            Apply = apply;
            Icon = icon;
            Selected = selected;
            GroupID = groupID;
        }
    }

    private static RectTransform ConfigureScroll(ScrollRect scroll, Vector2 center, Vector2 size)
    {
        var rect = (RectTransform)scroll.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = center;
        rect.sizeDelta = size;
        scroll.horizontal = false;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        if (scroll.verticalScrollbar != null)
        {
            var bar = (RectTransform)scroll.verticalScrollbar.transform;
            var mask = (RectTransform)bar.parent;
            if (mask != rect)
            {
                mask.SetParent(rect, false);
                mask.anchorMin = new Vector2(1, 0);
                mask.anchorMax = new Vector2(1, 1);
                mask.pivot = new Vector2(1, 0.5f);
                mask.sizeDelta = new Vector2(8, -6);
                mask.anchoredPosition = new Vector2(-1, 0);
                bar.anchorMin = Vector2.zero;
                bar.anchorMax = Vector2.one;
                bar.offsetMin = Vector2.zero;
                bar.offsetMax = Vector2.zero;
            }
        }
        RectTransform viewport = scroll.viewport;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(-10, 0);
        ConfigureColumn(scroll.content, true);
        return scroll.content;
    }

    private static void ConfigureColumn(RectTransform rect, bool pane = false)
    {
        if (pane)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
        var fitter = rect.GetComponent<ContentSizeFitter>() ?? rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var layout = rect.GetComponent<VerticalLayoutGroup>() ?? rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.SetSimpleVertLayout(3);
        layout.childControlHeight = true;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.padding = new RectOffset(2, 2, 1, 4);
    }

    private static RectTransform Row(Transform parent, float width, float height, string name = "Row")
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 4;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return rect;
    }

    private static Button ButtonAt(Transform parent, string title, float width, Action click,
        Sprite icon = null, bool selected = false, float height = 19, Surface surface = Surface.Command,
        string tooltip = null, Sprite arrow = null)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        var image = go.GetComponent<Image>();
        PaintSurface(image, surface, selected);
        bool iconOnly = tooltip != null;
        bool left = surface is Surface.List or Surface.Field;
        if (!iconOnly)
            Caption(rect, title, selected ? TitleColor : Color.white,
                left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, icon == null ? 8 : 22,
                arrow == null ? 6 : 21);
        if (icon != null) AddIcon(rect, icon, iconOnly ? 0.5f : 0f, iconOnly ? 0 : 11, 13);
        if (arrow != null) AddIcon(rect, arrow, 1, -11, 11);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        if (surface is Surface.List or Surface.Quiet)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, surface == Surface.List && !selected ? 0.5f : 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.82f, 0.85f, 0.74f);
            button.colors = colors;
        }
        if (tooltip != null) SetTooltip(go.AddComponent<TipButton>(), tooltip);
        if (surface is not Surface.List and not Surface.Field)
        {
            var animation = go.AddComponent<ButtonAnimation>();
            button.onClick.AddListener(animation.clickAnimation);
        }
        button.onClick.AddListener(() => click());
        return button;
    }

    private static Sprite NativeIcon(string id) => SpriteTextureLoader.getSprite("ui/icons/" + id);

    private static void PaintSurface(Image image, Surface surface, bool selected = false)
    {
        string name = surface switch
        {
            Surface.Primary => "special_buttonGrayLight",
            Surface.Quiet => "special_buttonGray_insides",
            Surface.Field => "darkInputFieldEmpty",
            Surface.List => selected ? "windowInnerSlicedRound" : "windowInnerSlicedRoundWhite",
            Surface.Destructive => "special_buttonRed_insides",
            _ => "special_buttonGray"
        };
        image.sprite = SpriteTextureLoader.getSprite("ui/special/" + name) ?? GeneralTools.get_button();
        image.type = Image.Type.Sliced;
        image.color = surface switch
        {
            Surface.Primary => new Color(0.74f, 0.91f, 0.56f),
            Surface.List when !selected => new Color(0.22f, 0.25f, 0.21f, 0.65f),
            Surface.List => new Color(0.87f, 0.96f, 0.76f),
            Surface.Field => new Color(1f, 1f, 1f, 0.78f),
            _ => Color.white
        };
    }

    private static RawText Caption(RectTransform parent, string text, Color color, TextAnchor alignment,
        float left, float right, int size = 9)
    {
        var label = RawText.Instantiate(parent);
        label.Setup(text, color, alignment);
        label.Text.fontSize = size;
        label.Text.resizeTextForBestFit = true;
        label.Text.resizeTextMaxSize = size;
        label.Text.resizeTextMinSize = 7;
        label.Text.raycastTarget = false;
        label.Text.supportRichText = false;
        label.RectTransform.anchorMin = Vector2.zero;
        label.RectTransform.anchorMax = Vector2.one;
        label.RectTransform.offsetMin = new Vector2(left, 1);
        label.RectTransform.offsetMax = new Vector2(-right, -1);
        return label;
    }

    private static void AddIcon(RectTransform parent, Sprite sprite, float anchor, float x, float size,
        float y = 0, string name = "Icon")
    {
        if (sprite == null) return;
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(anchor, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(size, size);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void SetTooltip(TipButton tip, string title)
    {
        tip.showOnClick = false;
        tip.setHoverAction(() => Tooltip.show(tip.gameObject, "normal", new TooltipData
        {
            tip_name = title
        }), false);
    }

    private static void TabAt(Transform parent, string title, float width, string icon, bool selected, Action click)
    {
        _nativeTab ??= Resources.Load<GameObject>("ui/TabToggleGeneric");
        if (_nativeTab == null)
        {
            ButtonAt(parent, title, width, click, NativeIcon(icon), selected);
            return;
        }
        var slot = Row(parent, width, 29, title);
        var frame = Instantiate(_nativeTab, slot, false);
        var rect = (RectTransform)frame.transform;
        rect.sizeDelta = new Vector2(26, 26);
        frame.GetComponent<Image>().raycastTarget = false;
        var tab = frame.GetComponent<TabToggleContainer>().toggle;
        tab.gameObject.name = title;
        tab.icon.sprite = NativeIcon(icon);
        SetTooltip(tab.GetComponent<TipButton>(), title);
        tab.action = click.Invoke;
        if (selected) tab.select();
        else tab.unselect();
        var label = Label(slot, title, width - 30, 19, selected ? TitleColor : SecondaryText, 8);
        label.Text.raycastTarget = true;
        var labelButton = label.gameObject.AddComponent<Button>();
        labelButton.targetGraphic = label.Text;
        labelButton.onClick.AddListener(tab.click);
    }

    private static void SwitchAt(Transform parent, bool value, string title, Action change)
    {
        _nativeSwitch ??= Resources.Load<GameObject>("ui/SettingButton")?.GetComponent<OptionButton>()?.button_switch?.gameObject;
        if (_nativeSwitch == null)
        {
            ButtonAt(parent, title, 55, change, NativeIcon(value ? "iconOn" : "iconOff"), selected: value);
            return;
        }
        var go = Instantiate(_nativeSwitch, parent, false);
        go.name = title;
        go.SetActive(true);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(51, 19);
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.preferredHeight = 19;
        PowerButton source = go.GetComponent<PowerButton>();
        source.icon.sprite = NativeIcon(value ? "iconOn" : "iconOff");
        go.transform.Find("Text").GetComponent<LocalizedText>().setKeyAndUpdate(value ? "short_on" : "short_off");
        Button button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        SetTooltip(go.GetComponent<TipButton>(), title);
        button.onClick.AddListener(change.Invoke);
        go.GetComponent<CanvasGroup>().alpha = value ? 1f : 0.82f;
    }

    private static void IconCommand(Transform parent, string title, string icon, Action click,
        Surface surface = Surface.Quiet, float width = 22)
    {
        ButtonAt(parent, title, width, click, NativeIcon(icon), height: 20, surface: surface, tooltip: title);
    }

    private static void RuleLabel(RectTransform row, string title, string icon, Color color = default)
    {
        var cell = (RectTransform)new GameObject(title, typeof(RectTransform)).transform;
        cell.SetParent(row, false);
        cell.sizeDelta = new Vector2(88, 19);
        AddIcon(cell, NativeIcon(icon), 0, 8, 14);
        Caption(cell, title, color == default ? SecondaryText : color, TextAnchor.MiddleLeft, 20, 0);
    }

    private static void UnfoldAt(Transform parent, bool open, Action click)
    {
        _nativeUnfold ??= Resources.Load<UnfoldButton>("ui/UnfoldButton");
        if (_nativeUnfold == null)
        {
            ButtonAt(parent, open ? "−" : "+", 20, click, surface: Surface.Quiet);
            return;
        }
        var unfold = Instantiate(_nativeUnfold, parent, false);
        ((RectTransform)unfold.transform).sizeDelta = new Vector2(20, 20);
        unfold.setText(open ? "−" : "+");
        unfold.setCallback(_ => click());
    }

    private static Button FieldAt(Transform parent, string title, float width, Action click, bool? expanded = null,
        Sprite icon = null) =>
        ButtonAt(parent, title, width, click, icon, surface: Surface.Field,
            arrow: NativeIcon(expanded == true ? "iconArrowUP" : "iconArrowDOWN"));

    private static void ScriptRow(Transform parent, string name, string status, float width, bool enabled,
        bool draft, bool selected, Action click)
    {
        var button = ButtonAt(parent, name + "\n" + status, width, click, selected: selected, height: 29,
            surface: Surface.List);
        var rect = (RectTransform)button.transform;
        button.GetComponentInChildren<RawText>().gameObject.SetActive(false);
        AddIcon(rect, NativeIcon(enabled ? "iconOn" : draft ? "iconEdit" : "iconOff"), 0, 12, 12);
        var caption = Caption(rect, name, selected ? TitleColor : Color.white, TextAnchor.MiddleLeft, 23, 4);
        caption.RectTransform.anchorMin = new Vector2(0, 0.38f);
        var state = Caption(rect, status, enabled ? EnabledText : SecondaryText, TextAnchor.MiddleLeft, 23, 4, 7);
        state.RectTransform.anchorMax = new Vector2(1, 0.43f);
        state.Text.resizeTextForBestFit = false;
        state.Text.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private static void Readout(Transform parent, string title, int value, string icon, float width)
    {
        var image = new GameObject(title, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(parent, false);
        image.rectTransform.sizeDelta = new Vector2(width, 25);
        PaintSurface(image, Surface.List, true);
        image.raycastTarget = false;
        AddIcon(image.rectTransform, NativeIcon(icon), 0, 11, 12);
        Caption(image.rectTransform, title, SecondaryText, TextAnchor.MiddleLeft, 22, 23, 8);
        Caption(image.rectTransform, value.ToString(), TitleColor, TextAnchor.MiddleRight, 65, 9, 11);
    }

    private static RawText Label(Transform parent, string text, float width, float height = 15, Color color = default,
        int fontSize = 9)
    {
        var label = RawText.Instantiate(parent, pName: "Label");
        label.Setup(text, color == default ? Color.white : color, TextAnchor.MiddleLeft);
        label.Text.fontSize = fontSize;
        label.Text.supportRichText = false;
        label.Text.resizeTextForBestFit = false;
        label.Text.raycastTarget = false;
        label.RectTransform.sizeDelta = new Vector2(width, height);
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return label;
    }

    private void Heading(string text) => Label(_body, text, EditorWidth, 18, TitleColor);

    private void Message(string text, bool error, bool richText = false, int indent = 0)
    {
        var row = indent > 0 ? Row(_body, EditorWidth, 18) : null;
        if (row != null) row.GetComponent<HorizontalLayoutGroup>().padding.left = indent;
        var label = Label(row ?? _body, text, EditorWidth - indent, 18, error ? ErrorColor : Color.white);
        label.Text.supportRichText = richText;
        label.Text.verticalOverflow = VerticalWrapMode.Overflow;
        float height = Mathf.Max(18, label.Text.preferredHeight + 4);
        label.GetComponent<LayoutElement>().preferredHeight = height;
        if (row != null)
        {
            row.GetComponent<LayoutElement>().preferredHeight = height;
            label.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }

    private static TextInput InputAt(Transform parent, string text, float width, Action<string> commit,
        InputField.CharacterValidation validation = InputField.CharacterValidation.None, float height = 19f,
        bool multiline = false)
    {
        TextInput input = TextInput.Instantiate(parent);
        if (multiline)
        {
            input.input.lineType = InputField.LineType.MultiLineNewline;
            input.input.textComponent.supportRichText = false;
        }
        input.Setup(text, commit.Invoke);
        input.input.characterValidation = validation;
        input.SetSize(new Vector2(width, height));
        if (multiline)
        {
            input.input.textComponent.alignment = TextAnchor.UpperLeft;
            RectTransform field = input.input.textComponent.rectTransform;
            field.anchorMin = Vector2.zero;
            field.anchorMax = Vector2.one;
            field.offsetMin = new Vector2(4f, 3f);
            field.offsetMax = new Vector2(-22f, -4f);
            if (input.transform.Find("Icon") is RectTransform icon)
            {
                icon.anchorMin = icon.anchorMax = icon.pivot = Vector2.one;
                icon.anchoredPosition = new Vector2(-3f, -3f);
                icon.sizeDelta = new Vector2(15f, 15f);
            }
        }
        var layout = input.gameObject.GetComponent<LayoutElement>() ?? input.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        return input;
    }

    private void OpenPicker(string title, IEnumerable<Option> options, Page returnTo = Page.Rules,
        PickerLayout layout = PickerLayout.List)
    {
        _pickerScroll = _page == returnTo ? _bodyScroll.verticalNormalizedPosition : 1f;
        _pickerTitle = title;
        _pickerLayout = layout;
        _options = options.ToArray();
        _pickerReturn = returnTo;
        Enter(Page.Picker);
    }

    private void BuildPicker()
    {
        Heading(_pickerTitle);
        RectTransform row = Row(_body, EditorWidth, 22);
        ButtonAt(row, L("command.cancel", "Cancel"), 84, () => Enter(_pickerReturn));
        Label(row, L("editor.search", "Search"), 66);
        TextInput search = InputAt(row, "", 215, _ => { });
        switch (_pickerLayout)
        {
            case PickerLayout.Traits: BuildTraitGrid(search); break;
            case PickerLayout.Icons: BuildIconGrid(search); break;
            default: BuildOptionList(search); break;
        }
    }

    private void BuildTraitGrid(TextInput search)
    {
        var sections = new List<(GameObject Row, (Option Option, GameObject Cell)[] Cells)>();
        var byGroup = _options.ToLookup(option => option.GroupID);
        var groups = AssetManager.trait_groups.list;
        foreach (var group in groups)
        {
            var entries = byGroup[group.id];
            if (!entries.Any()) continue;
            Color color = string.IsNullOrEmpty(group.color) ? SecondaryText : group.getColor();
            AddGroup(group.id, Localize(group.getLocaleID(), group.id), color, entries);
        }
        var registered = new HashSet<string>(groups.Select(group => group.id));
        var ungrouped = _options.Where(option => !registered.Contains(option.GroupID)).ToArray();
        if (ungrouped.Length > 0)
            AddGroup("ungrouped", L("trait_group.ungrouped", "Ungrouped"), SecondaryText, ungrouped);

        var empty = Label(_body, L("warning.no_choices", "No matching choices"), EditorWidth);

        void AddGroup(string id, string title, Color color, IEnumerable<Option> options)
        {
            const float size = 19f;
            var row = new GameObject("TraitGroup_" + id, typeof(RectTransform), typeof(HorizontalLayoutGroup))
                .GetComponent<HorizontalLayoutGroup>();
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(_body, false);
            rowRect.sizeDelta = new Vector2(EditorWidth, 0);
            row.spacing = 6f;
            row.padding = new RectOffset(0, 0, 2, 2);
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = false;
            row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Label(rowRect, title, 108f, size, color);

            var grid = CreateIconGrid(rowRect, "TraitGrid", 13, size);
            sections.Add((row.gameObject, options.Select(option => (option, IconCell(grid, option))).ToArray()));
        }

        void UpdateOptions(string text)
        {
            bool anyMatch = false;
            foreach (var section in sections)
            {
                bool sectionMatches = false;
                foreach (var (option, cell) in section.Cells)
                {
                    bool matches = option.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                    cell.SetActive(matches);
                    sectionMatches |= matches;
                }
                section.Row.SetActive(sectionMatches);
                anyMatch |= sectionMatches;
            }
            empty.gameObject.SetActive(!anyMatch);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
            _bodyScroll.StopMovement();
            _bodyScroll.verticalNormalizedPosition = 1f;
        }

        search.input.onValueChanged.AddListener(UpdateOptions);
        UpdateOptions("");
    }

    private static RectTransform CreateIconGrid(Transform parent, string name, int columns, float size)
    {
        const float spacing = 3f;
        var grid = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<GridLayoutGroup>();
        var rect = (RectTransform)grid.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(columns * size + (columns - 1) * spacing, 0);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.cellSize = new Vector2(size, size);
        grid.spacing = new Vector2(spacing, spacing);
        return rect;
    }

    private static GameObject IconCell(RectTransform parent, Option option)
    {
        var cell = new GameObject(option.Label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
        cell.transform.SetParent(parent, false);
        var image = cell.GetComponent<Image>();
        image.sprite = option.Icon ?? NativeIcon("iconInspect");
        image.preserveAspect = true;
        var button = cell.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(option.Apply.Invoke);
        SetTooltip(cell.GetComponent<TipButton>(), option.Label);
        if (option.Selected)
            AddIcon(image.rectTransform, NativeIcon("iconOn"), 1f, -3f, 8f, -5f, "Selected");
        return cell;
    }

    private void BuildIconGrid(TextInput search)
    {
        const int pageSize = 70;
        var grid = CreateIconGrid(_body, "HistoryIconGrid", 14, 24f);
        var empty = Label(_body, L("warning.no_choices", "No matching choices"), EditorWidth);
        var navigation = Row(_body, EditorWidth, 22);
        var options = _options;
        Option[] matches = Array.Empty<Option>();
        int page = 0;
        var previous = ButtonAt(navigation, L("log.icons.previous", "Previous"), 84, () => page--);
        var pageLabel = Label(navigation, "", 54, 19, SecondaryText);
        pageLabel.Text.alignment = TextAnchor.MiddleCenter;
        var next = ButtonAt(navigation, L("log.icons.next", "Next"), 84, () => page++);
        previous.onClick.AddListener(RenderPage);
        next.onClick.AddListener(RenderPage);

        void RenderPage()
        {
            int pageCount = Math.Max(1, (matches.Length + pageSize - 1) / pageSize);
            page = Mathf.Clamp(page, 0, pageCount - 1);
            Clear(grid);
            foreach (Option option in matches.Skip(page * pageSize).Take(pageSize))
                IconCell(grid, option);
            grid.gameObject.SetActive(matches.Length > 0);
            empty.gameObject.SetActive(matches.Length == 0);
            navigation.gameObject.SetActive(pageCount > 1);
            previous.interactable = page > 0;
            next.interactable = page + 1 < pageCount;
            pageLabel.Value = $"{page + 1} / {pageCount}";
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
            _bodyScroll.StopMovement();
            _bodyScroll.verticalNormalizedPosition = 1f;
        }

        void UpdateOptions(string text)
        {
            matches = options.Where(option => option.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            page = 0;
            RenderPage();
        }

        search.input.onValueChanged.AddListener(UpdateOptions);
        UpdateOptions("");
    }

    private void BuildOptionList(TextInput search)
    {
        var results = (RectTransform)new GameObject("Options", typeof(RectTransform), typeof(LayoutElement)).transform;
        results.SetParent(_body, false);
        results.sizeDelta = new Vector2(EditorWidth, 0);
        ConfigureColumn(results);

        void UpdateOptions(string text)
        {
            Clear(results);
            var matches = _options.Where(option => option.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(81).ToArray();
            foreach (Option option in matches.Take(80))
                ButtonAt(results, option.Label, EditorWidth - 3, option.Apply, option.Icon,
                    selected: option.Selected, height: 21, surface: Surface.List);
            if (matches.Length == 0) Label(results, L("warning.no_choices", "No matching choices"), EditorWidth);
            if (matches.Length > 80)
                Label(results, L("editor.refine_search", "More results available. Refine the search."), EditorWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(results);
            results.GetComponent<LayoutElement>().preferredHeight =
                results.GetComponent<VerticalLayoutGroup>().preferredHeight;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
            _bodyScroll.verticalNormalizedPosition = 1;
        }

        search.input.onValueChanged.AddListener(UpdateOptions);
        UpdateOptions("");
    }

    private static void Clear(Transform container)
    {
        foreach (Transform child in container.Cast<Transform>().ToArray())
        {
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    private static string L(string suffix, string fallback) => Localize($"{C.mod_prefix}.ui.world_script.{suffix}", fallback);

    private static string Localize(string key, string fallback) =>
        !string.IsNullOrEmpty(key) && LocalizedTextManager.stringExists(key) ? LocalizedTextManager.getText(key) : fallback;

    private static string DefinitionName(string category, string id, string fallback) =>
        string.IsNullOrEmpty(id) ? fallback : L($"{category}.{id}", fallback);
}
