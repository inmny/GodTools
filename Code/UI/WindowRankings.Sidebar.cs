using System;
using System.Collections.Generic;
using System.Linq;
using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings
{
    private enum Sidebar { None, Sort, Filters, Pins, Presets }
    private const float FilterActionsHeight = 88;

    private Sidebar _sidebar = Sidebar.Sort;
    private float _sortOffset;
    private float _conditionOffset;
    private float _pickerOffset;
    private float _pinOffset;
    private float _presetOffset;
    private float _sidebarHeight;

    private float SidebarOffset
    {
        get => _sidebar switch
        {
            Sidebar.Sort => _sortOffset,
            Sidebar.Pins => _pinOffset,
            Sidebar.Presets => _presetOffset,
            _ => _filterStep == FilterStep.Conditions ? _conditionOffset : _pickerOffset
        };
        set
        {
            if (_sidebar == Sidebar.Sort) _sortOffset = value;
            else if (_sidebar == Sidebar.Pins) _pinOffset = value;
            else if (_sidebar == Sidebar.Presets) _presetOffset = value;
            else if (_filterStep == FilterStep.Conditions) _conditionOffset = value;
            else _pickerOffset = value;
        }
    }

    private void ResetSidebarEditors()
    {
        _sortOffset = _conditionOffset = _pickerOffset = _pinOffset = _presetOffset = 0;
        _filterStep = FilterStep.Conditions;
        _filterNotice = null;
        _presetNotice = null;
        _sidebarScroll.StopMovement();
        _sidebarContent.anchoredPosition = Vector2.zero;
    }

    private void SetSidebar(Sidebar next)
    {
        if (_sidebar == next) return;
        if (ShowSidebar) SidebarOffset = _sidebarContent.anchoredPosition.y;
        _sidebar = next;
        Enter(Page.Table);
    }

    private RectTransform NextSidebarLine(float height = 23)
    {
        var row = StackLine("RankingSidebarRow", _sidebarContent, -_sidebarHeight, RightWidth, height);
        _sidebarHeight += height;
        return row;
    }

    private void DrawSidebarHeader()
    {
        Clear(_sidebarTabs);
        bool expanded = ShowSidebar;
        float compactHeight = CompactInset * 2 + CompactStep * 3 + CompactButtonSize;
        LayoutRail(_sidebarRail, expanded ? SidebarWidth + 2 : CollapsedWidth, expanded ? RailHeight : compactHeight,
            SidebarX, compact: !expanded);
        Place(_sidebarTabs, BackgroundTransform, expanded ? RightWidth : CollapsedWidth, expanded ? 22 : compactHeight,
            expanded ? SidebarX - 4 : SidebarX, expanded ? 108 : RailTop - compactHeight * 0.5f, centered: true);
        _sidebarTabs.GetComponent<HorizontalLayoutGroup>().enabled = expanded;

        Tab("sort", Sidebar.Sort, SpriteTextureLoader.getSprite("inmny/godtools/icons/iconRankingSort"), 0);
        Tab("filters", Sidebar.Filters, SpriteTextureLoader.getSprite("inmny/godtools/icons/iconRankingFilter"), 1);
        Tab("row_layout", Sidebar.Pins, Icon("iconCompareStatistics"), 2);
        Tab("presets", Sidebar.Presets, Icon("iconSaveLocal"), 3);
        if (expanded)
        {
            string collapse = L("hide_sidebar");
            Button(_sidebarTabs, collapse, 22, () => SetSidebar(Sidebar.None), Icon("iconArrowMetaRight"), hint: collapse)
                .name = "RankingSidebarToggle";
        }

        void Tab(string label, Sidebar section, Sprite icon, int row)
        {
            string title = L(label);
            if (!expanded)
            {
                CompactButton(_sidebarTabs, title, icon, () => SetSidebar(section), -CompactInset - row * CompactStep);
                return;
            }
            bool selected = _sidebar == section;
            var button = Button(_sidebarTabs, title, section == Sidebar.Pins ? 32 : 24,
                () => SetSidebar(section), flat: !selected, hint: title);
            button.GetComponentInChildren<Text>().resizeTextMaxSize = 9;
            if (selected) button.image.color = new Color(0.83f, 0.88f, 0.65f);
        }
    }

    private void DrawSidebar()
    {
        _sidebarScroll.StopMovement();
        Clear(_sidebarTools);
        Clear(_filterActions);
        Clear(_sidebarContent);
        bool filters = _sidebar == Sidebar.Filters;
        bool conditions = filters && _filterStep == FilterStep.Conditions;
        float height = _sidebar is Sidebar.Sort or Sidebar.Pins ? 221 : 198 - (conditions ? FilterActionsHeight : 0);
        _filterActions.gameObject.SetActive(conditions);
        Place((RectTransform)_sidebarScroll.transform, BackgroundTransform, RightWidth + ScrollbarGutter,
            height, SidebarX, -125 + height * 0.5f, centered: true);
        _sidebarHeight = 0;

        switch (_sidebar)
        {
            case Sidebar.Sort: DrawSort(); break;
            case Sidebar.Filters: DrawFilterSidebar(); break;
            case Sidebar.Pins: DrawPinnedColumns(); break;
            case Sidebar.Presets: DrawPresetSidebar(); break;
        }

        float viewHeight = _sidebarScroll.viewport.rect.height;
        SidebarOffset = Mathf.Clamp(SidebarOffset, 0, Mathf.Max(0, _sidebarHeight - viewHeight));
        _sidebarContent.sizeDelta = new Vector2(RightWidth, _sidebarHeight);
        _sidebarContent.anchoredPosition = new Vector2(0, SidebarOffset);
    }

    private void ResetSort()
    {
        _view.Sort.Clear();
        if (_board.DefaultSort != null && _source.Fields.GetValueOrDefault(_board.DefaultSort)?.Sortable == true)
            _view.Sort.Add(new RankingSort { Field = _board.DefaultSort, Ascending = _board.Ascending });
        _sortOffset = 0;
        _view.Scroll = 0;
        Refresh();
    }

    private void DrawSort()
    {
        var controls = NextSidebarLine();
        Label(controls, L("sort_order"), 61, 20, new Color32(220, 208, 168, 255)).fontSize = 9;
        Button(controls, L("reset_sort"), 73, ResetSort);
        if (_view.Sort.Count == 0) Label(NextSidebarLine(), L("default_sort"), RightWidth - 6);
        for (int i = 0; i < _view.Sort.Count; i++)
        {
            int index = i;
            var item = _view.Sort[index];
            var row = NextSidebarLine();
            row.name = "RankingSortKey." + item.Field;
            string name = _source.Fields.TryGetValue(item.Field, out var field) ? Name(field.Name) : item.Field;
            var title = Label(row, $"{index + 1}   {name}", 56,
                color: index == 0 ? new Color32(246, 219, 160, 255) : Color.white);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 7;
            title.resizeTextMaxSize = 9;
            string direction = item.Ascending ? "asc" : "desc";
            var order = Button(row, L(direction + "_short"), 26, () =>
            {
                item.Ascending = !item.Ascending;
                RefreshSort();
            }, hint: $"{name} · {L(direction)}");
            var caption = order.GetComponentInChildren<Text>();
            caption.rectTransform.offsetMax = new Vector2(-8, 0);
            SortCorner(order.transform, item.Ascending);
            Button(row, "↑", 15, () => MoveSort(index, -1), hint: L("move_sort_up")).interactable = index > 0;
            Button(row, "↓", 15, () => MoveSort(index, 1), hint: L("move_sort_down")).interactable = index < _view.Sort.Count - 1;
            Button(row, "×", 15, () => { _view.Sort.Remove(item); RefreshSort(); }, hint: L("remove_sort"));
        }

        DrawFieldGrid("sort_keys", _source.Fields.Values.Where(field => field.Sortable),
            field => AddSortKey(field.ID), field => !HasSortKey(field.ID));
    }

    private static Sprite SortDirectionIcon(bool ascending) => Icon(ascending ? "iconArrowDOWN" : "iconArrowUP");

    private void DrawPinnedColumns()
    {
        var layout = _view.RowLayout;
        var options = NextSidebarLine();
        Toggle("row_show_portrait", layout.ShowPortrait, () => layout.ShowPortrait = !layout.ShowPortrait);
        Toggle("row_show_rank", layout.ShowRank, () => layout.ShowRank = !layout.ShowRank);
        Toggle("row_fill_columns", layout.FillColumns, () => layout.FillColumns = !layout.FillColumns);
        SidebarSection("pinned_columns", 16);

        var pins = _view.PinnedColumns;
        var controls = NextSidebarLine();
        Label(controls, string.Format(L("pinned_count"), pins.Count, PinLimit), 84, 20,
            new Color32(220, 208, 168, 255)).fontSize = 9;
        Button(controls, L("clear_pins"), 48, () =>
        {
            pins.Clear();
            RefreshPins();
        }).interactable = pins.Count > 0;
        for (int i = 0; i < pins.Count; i++)
        {
            int index = i;
            string field = pins[i];
            var row = NextSidebarLine();
            row.name = "RankingPinnedColumn." + field;
            var title = Label(row, $"{index + 1}   {Name(_source.Fields[field].Name)}", 84);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 7;
            title.resizeTextMaxSize = 9;
            Button(row, "↑", 15, () => MovePin(index, -1), hint: L("move_pin_left")).interactable = index > 0;
            Button(row, "↓", 15, () => MovePin(index, 1), hint: L("move_pin_right")).interactable = index < pins.Count - 1;
            Button(row, "×", 15, () => { pins.Remove(field); RefreshPins(); }, hint: L("remove_pin"));
        }

        DrawFieldGrid("pin_fields", _source.Fields.Values.Where(field => IsDisplayColumn(field.ID)), field =>
        {
            if (pins.Count >= PinLimit || pins.Contains(field.ID)) return;
            pins.Add(field.ID);
            RefreshPins();
        }, field => pins.Count < PinLimit && !pins.Contains(field.ID));

        void Toggle(string key, bool selected, Action change)
        {
            var button = Button(options, L(key), 43, () => { change(); RefreshPins(); },
                flat: !selected, hint: L(key + "_hint"));
            if (selected) button.image.color = new Color(0.83f, 0.88f, 0.65f);
        }
    }

    private void MovePin(int current, int offset)
    {
        var pins = _view.PinnedColumns;
        int next = current + offset;
        if (next < 0 || next >= pins.Count) return;
        (pins[current], pins[next]) = (pins[next], pins[current]);
        RefreshPins();
    }

    private void RefreshPins()
    {
        _pinOffset = _sidebarContent.anchoredPosition.y;
        Refresh(recordPeaks: false);
    }

    private static void SortCorner(Transform parent, bool ascending)
    {
        var badge = IconImage(parent, 0, SortDirectionIcon(ascending));
        badge.name = "RankingSortCorner";
        var corner = new Vector2(1, 0);
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = badge.rectTransform.pivot = corner;
        badge.rectTransform.anchoredPosition = Vector2.zero;
        badge.rectTransform.sizeDelta = new Vector2(15, 15);
    }

    private bool HasSortKey(string field) => _view.Sort.Any(item => item.Field == field);

    private Sprite FieldIcon(string field) => FieldIcon(_source, field);

    private static Sprite FieldIcon(RankingRegistry.Source source, string field)
    {
        string path = field == null ? null : source?.Fields.GetValueOrDefault(field)?.IconPath;
        if (!string.IsNullOrEmpty(path) && SpriteTextureLoader.getSprite(path) is Sprite sprite && sprite != null)
            return sprite;
        return SpriteTextureLoader.getSprite(field switch
        {
            "kills" => "ui/icons/iconSkulls",
            "age" => "ui/icons/iconAge",
            "level" => "ui/icons/iconLevels",
            "health" => "ui/icons/iconHealth",
            "damage" => "ui/icons/iconDamage",
            "power" or "peak" or "score" => "ui/icons/iconStatistics",
            "births" => "ui/icons/iconBirths",
            "kingdom" => "ui/icons/iconKingdomList",
            "alliance" => "ui/icons/iconAllianceList",
            "attacker" or "defender" or "target_kingdom" or "kingdom_count" => "ui/icons/iconKingdomList",
            "culture" => "ui/icons/iconCultureList",
            "clan" => "ui/icons/iconClanList",
            "language" => "ui/icons/iconLanguageList",
            "subspecies" => "ui/icons/iconSubspeciesList",
            "city" or "target_city" or "settlements" or "cities" or "buildings" => "ui/icons/iconCityList",
            "religion" => "ui/icons/iconReligionList",
            "population" or "species" or "learners" or "members" or "followers" => "ui/icons/iconPopulation",
            "families" or "children" => "ui/icons/iconFamilyList",
            "profession" or "holder" or "ruler" or "founder" or "author" => "ui/icons/iconLeaders",
            "speakers" => "ui/icons/iconLanguageList",
            "books" => "ui/icons/iconBooks",
            "warriors" or "melee" or "ranged" => "ui/icons/iconWarriors",
            "casualties" => "ui/icons/iconSkulls",
            "territory" => "ui/icons/iconArmyZones",
            "renown" or "progress" => "ui/icons/iconStatistics",
            "traits" => "ui/icons/actor_traits/iconAttractive",
            "material" => "ui/icons/iconStone",
            "type" => "ui/icons/iconItemType",
            "durability" => "ui/icons/iconArmor",
            "mod_count" => "ui/icons/iconStatistics",
            "modifiers" => "ui/icons/iconEnchanted",
            "name" => "ui/icons/iconFamilyList",
            "year" => "ui/icons/iconClock",
            "spells" => "ui/icons/iconBooks",
            "cultiway:realm" => "cultiway/icons/iconCultivation",
            "cultiway:magic" => "cultiway/icons/iconMagic",
            "cultiway:knight" => "cultiway/icons/iconKnight",
            "cultiway:talent" => "cultiway/icons/iconElement",
            "cultiway:skill_count" => "cultiway/icons/iconWakan",
            "cultiway:book_grade" => "books/custom_book_covers/cultibook/01",
            "cultiway:apprentices" => "cultiway/icons/iconMasterApprentice",
            _ => "ui/icons/iconInspect"
        }) ?? Icon("iconInspect");
    }

    private static void PaintSidebarPanel(RectTransform frame)
    {
        var image = frame.gameObject.AddComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        image.type = Image.Type.Sliced;
        image.color = new Color32(27, 31, 26, 108);
        image.raycastTarget = false;
    }

    private void SidebarSection(string caption, float height = 17)
    {
        var row = NextSidebarLine(height);
        PaintSidebarPanel(row);
        Label(row, L(caption), RightWidth - 6, height - 1, new Color32(220, 208, 168, 255)).fontSize = 9;
    }

    private void DrawFieldGrid(string caption, IEnumerable<RankingRegistry.Field> fields,
        Action<RankingRegistry.Field> select, Func<RankingRegistry.Field, bool> canSelect = null)
    {
        SidebarSection(caption, 14);
        var groups = fields.Select(field => (Field: field, Mod: field.Origin()))
            .GroupBy(entry => entry.Mod.ID, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            string name = group.Key == Main.Mod?.UID ? L("group.vanilla") : group.First().Mod.Name;
            DrawFieldGroup(name, group.Select(entry => entry.Field), select, canSelect);
        }
    }

    private void DrawFieldGroup(string name, IEnumerable<RankingRegistry.Field> fields,
        Action<RankingRegistry.Field> select, Func<RankingRegistry.Field, bool> canSelect)
    {
        var heading = Label(NextSidebarLine(12), name, RightWidth - 6, 12, new Color32(188, 194, 161, 255));
        heading.name = "RankingFieldGroup";
        heading.resizeTextForBestFit = true;
        heading.resizeTextMinSize = 7;
        heading.resizeTextMaxSize = 8;
        const int columns = 7;
        const float cell = 18;
        RectTransform row = null;
        int index = 0;
        foreach (var field in fields)
        {
            if (index++ % columns == 0)
            {
                row = NextSidebarLine(cell);
                row.name = "RankingFieldIcons";
                row.GetComponent<HorizontalLayoutGroup>().spacing = 2;
            }
            string title = Name(field.Name);
            var button = BareIconButton(row, title, FieldIcon(field.ID), cell, 15, () => select(field),
                FieldTitle(field), FieldDescription(field));
            button.interactable = canSelect?.Invoke(field) ?? true;
        }
    }

    private static Button BareIconButton(Transform row, string title, Sprite sprite, float cell, float iconSize, Action click,
        string hint = null, string description = null)
    {
        var slot = Rect(title, row, cell, cell, 0, 0);
        var hitArea = slot.gameObject.AddComponent<Image>();
        hitArea.color = Color.clear;
        hitArea.canvasRenderer.cullTransparentMesh = false;
        var button = slot.gameObject.AddComponent<Button>();
        if (sprite != null)
        {
            var icon = IconImage(slot, cell * 0.5f, sprite);
            icon.rectTransform.sizeDelta = new Vector2(iconSize, iconSize);
            button.targetGraphic = icon;
        }
        button.onClick.AddListener(() => click());
        AttachButtonHint(slot.gameObject, hint ?? title, description);
        return button;
    }

    private void AddSortKey(string field)
    {
        if (HasSortKey(field)) return;
        _view.Sort.Add(new RankingSort { Field = field });
        RefreshSort();
    }

    private void MoveSort(int current, int offset)
    {
        int next = current + offset;
        if (next < 0 || next >= _view.Sort.Count) return;
        (_view.Sort[current], _view.Sort[next]) = (_view.Sort[next], _view.Sort[current]);
        RefreshSort();
    }

    private void RefreshSort()
    {
        if (ShowSidebar && _sidebar == Sidebar.Sort) _sortOffset = _sidebarContent.anchoredPosition.y;
        _view.Scroll = 0;
        Refresh();
    }

}
