using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings
{
    private enum FilterStep { Conditions, Value }
    private const string PresetTooltip = "inmny.godtools.ranking.preset_tooltip";
    private const float PresetIconCell = 17;
    private const float PresetIconGap = 3;
    private const int PresetIconInset = 5;
    private const float BannerCellSize = 32;
    private const float ChoiceIconCellSize = 18;
    private const int ChoiceIconColumns = 7;

    private sealed class PresetTooltipData : TooltipData
    {
        internal RankingSort[] Sort;
        internal Sprite[] Icons;
    }

    private FilterStep _filterStep;
    private string _filterNotice;
    private bool _filterError;
    private RankingRegistry.Field _field;
    private RankingOperator _operator;
    private string _fieldInput;
    private string _presetNotice;
    private Dictionary<string, string> _choices;
    private Dictionary<string, double> _choiceOrders;

    private void DrawFilterSidebar()
    {
        if (_view.Draft == null)
        {
            _view.Draft = new List<RankingToken>(_view.EditDisplay ? _view.DisplayConditions : _view.Conditions);
            _view.DraftCursor = _view.Draft.Count;
        }
        if (_filterStep == FilterStep.Value)
        {
            DrawValue();
            return;
        }
        DrawFilterLine();
        Button(_sidebarTools, L("preview"), 66, PreviewFilter, Icon("iconInspect"));
        Button(_sidebarTools, L("apply_filter"), 66, ApplyFilters, Icon("iconOn"));
        DrawFilterPalette();
    }

    private void DrawFilterLine()
    {
        Clear(_filterActions);
        var stages = StackLine("RankingFilterStages", _filterActions, 0, RightWidth, 21);
        AddStage(false, "filter_scope", "filter_scope_hint");
        AddStage(true, "filter_display", "filter_display_hint");
        var frame = Rect("RankingConditionLine", _filterActions, RightWidth, 63, 0, -22);
        PaintSidebarPanel(frame);
        var heading = StackLine("RankingFilterCursorActions", frame, 0, RightWidth, 22);
        Label(heading, L("filter_line"), 61, 20, new Color32(229, 216, 174, 255)).fontSize = 9;
        Button(heading, "←", 15, () => MoveCursor(-1), Icon("iconArrowMetaLeft"), flat: true, hint: L("filter_previous"))
            .interactable = _view.DraftCursor > 0;
        Button(heading, "→", 15, () => MoveCursor(1), Icon("iconArrowMetaRight"), flat: true, hint: L("filter_next"))
            .interactable = _view.DraftCursor < _view.Draft.Count;
        Button(heading, L("remove_token"), 15, RemoveCondition, AssetManager.onomastics_library.get("backspace")?.getSprite(),
            flat: true, hint: L("filter_remove_before")).interactable = _view.DraftCursor > 0;
        Button(heading, L("clear"), 15, () =>
        {
            _view.Draft.Clear();
            _view.DraftCursor = 0;
            RedrawFilterLine();
        }, Icon("iconOff"), flat: true, hint: L("clear")).interactable = _view.Draft.Count > 0;

        const float lineWidth = RightWidth - 8;
        var viewport = Rect("RankingConditionViewport", frame, lineWidth, 24, 4, -22);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hitArea = viewport.gameObject.AddComponent<Image>();
        hitArea.color = new Color32(34, 37, 30, 155);
        var content = Rect("RankingConditionTokens", viewport, lineWidth, 24, 0, 0);
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.inertia = false;
        scroll.scrollSensitivity = 24;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        float x = 6;
        float caretX = x - 3;
        for (int i = 0; i < _view.Draft.Count; i++)
        {
            int slot = i + 1;
            var token = _view.Draft[i];
            string compact = ShortTokenLabel(token);
            bool field = token?.Symbol == RankingSymbol.Value;
            float width = field ? Mathf.Clamp(19 + compact.Length * 8, 36, 76) : 20;
            var icon = field && token.Condition != null ? FieldIcon(token.Condition.Field) : null;
            var button = Button(content, compact, width, () => SetFilterCursor(slot), icon,
                flat: !field, hint: TokenLabel(token));
            button.name = "RankingFilterToken." + i;
            button.gameObject.AddComponent<ScrollableButton>();
            Place((RectTransform)button.transform, content, width, 19, x, -2);
            if (field) button.image.color = new Color32(163, 166, 133, 195);
            else button.GetComponentInChildren<Text>().color = new Color32(243, 212, 141, 255);
            x += width + 5;
            if (slot == _view.DraftCursor) caretX = x - 3;
        }
        float contentWidth = Mathf.Max(lineWidth, x + 2);
        content.sizeDelta = new Vector2(contentWidth, 24);
        var caret = Rect("RankingFilterCaret", content, 1.5f, 21, caretX, -1).gameObject.AddComponent<Image>();
        caret.color = new Color32(255, 211, 84, 255);
        caret.raycastTarget = false;
        content.anchoredPosition = new Vector2(-Mathf.Clamp(caretX - lineWidth * 0.5f, 0, contentWidth - lineWidth), 0);

        string selected = _view.Draft.Count == 0 ? L("no_filter") : _view.DraftCursor == 0
            ? L("filter_line_start") : TokenLabel(_view.Draft[_view.DraftCursor - 1]);
        var info = _filterNotice == null ? new Color32(215, 213, 190, 255) : _filterError
            ? new Color32(246, 169, 132, 255) : new Color32(200, 222, 161, 255);
        var detail = Label(frame, _filterNotice ?? selected, lineWidth, 17, info);
        detail.name = "RankingFilterReadout";
        detail.resizeTextForBestFit = true;
        detail.resizeTextMinSize = 7;
        detail.resizeTextMaxSize = 9;
        Place(detail.rectTransform, frame, lineWidth, 17, 4, -46);

        void AddStage(bool display, string title, string hint)
        {
            bool selected = _view.EditDisplay == display;
            var button = Button(stages, L(title), 66, () =>
            {
                if (selected) return;
                _view.EditDisplay = display;
                _filterNotice = null;
                _conditionOffset = 0;
                DrawSidebar();
            }, flat: !selected);
            AttachButtonHint(button.gameObject, L(title), L(hint));
        }
    }

    private string ShortTokenLabel(RankingToken token)
    {
        if (token == null) return "?";
        if (token.Symbol != RankingSymbol.Value) return Symbol(token.Symbol);
        string id = token.Condition?.Field;
        return id != null && _source.Fields.TryGetValue(id, out var field) ? Name(field.Name) : id ?? "?";
    }

    private void DrawFilterPalette()
    {
        SidebarSection("filter_logic");
        var logic = NextSidebarLine();
        logic.name = "RankingFilterOperators";
        foreach (var symbol in new[] { RankingSymbol.And, RankingSymbol.Or, RankingSymbol.Not, RankingSymbol.Open, RankingSymbol.Close })
        {
            var button = Button(logic, Symbol(symbol), 24, () => AddToken(new RankingToken { Symbol = symbol }), flat: true);
            button.GetComponentInChildren<Text>().color = new Color32(243, 212, 141, 255);
        }

        DrawFieldGrid("filter_fields", _source.Fields.Values.Where(field => field.Operators.Count > 0), ChooseField);
    }

    private void MoveCursor(int change) => SetFilterCursor(_view.DraftCursor + change);

    private void SetFilterCursor(int cursor)
    {
        _view.DraftCursor = Mathf.Clamp(cursor, 0, _view.Draft.Count);
        RedrawFilterLine();
    }

    private void RemoveCondition()
    {
        if (_view.DraftCursor == 0) return;
        _view.Draft.RemoveAt(--_view.DraftCursor);
        RedrawFilterLine();
    }

    private void RedrawFilterLine()
    {
        _filterNotice = null;
        DrawFilterLine();
    }

    private void AddToken(RankingToken token)
    {
        int cursor = _view.DraftCursor;
        bool insertAnd = cursor > 0 && token.Symbol is (RankingSymbol.Value or RankingSymbol.Open or RankingSymbol.Not) &&
            _view.Draft[cursor - 1].Symbol is (RankingSymbol.Value or RankingSymbol.Close);
        if (_view.Draft.Count + (insertAnd ? 2 : 1) > 96)
        {
            ShowFilterNotice(L("long_filter"), error: true);
            return;
        }
        if (insertAnd) _view.Draft.Insert(cursor++, new RankingToken { Symbol = RankingSymbol.And });
        _view.Draft.Insert(cursor++, token);
        _view.DraftCursor = cursor;
        if (_filterStep == FilterStep.Value) ShowFilterStep(FilterStep.Conditions);
        else RedrawFilterLine();
    }

    private void ShowFilterStep(FilterStep step)
    {
        SidebarOffset = _sidebarContent.anchoredPosition.y;
        _filterStep = step;
        _filterNotice = null;
        if (step == FilterStep.Value) _pickerOffset = 0;
        DrawSidebar();
    }

    private void ChooseField(RankingRegistry.Field field)
    {
        _field = field;
        var preferred = field.Numeric ? RankingOperator.AtLeast :
            field.Kind == RankingFieldKind.Text ? RankingOperator.Contains : RankingOperator.Equal;
        _operator = field.Operators.Contains(preferred) ? preferred : field.Operators[0];
        _fieldInput = "";
        _choices = new Dictionary<string, string>(StringComparer.Ordinal);
        _choiceOrders = field.Ordered ? new Dictionary<string, double>(StringComparer.Ordinal) : null;
        try
        {
            foreach (var option in field.Options.Values)
            {
                _choices.Add(option.ID, option.LocalizedName());
                _choiceOrders?.Add(option.ID, option.Order);
            }
            if (field.Options.Count == 0 && (field.IsChoice || field.Kind == RankingFieldKind.Tags))
            {
                var data = _view.Snapshot?.Data;
                var rows = data?.Covers(_source, new[] { field.ID }) == true
                    ? data.Rows : RankingQuery.Read(_source, new[] { field.ID });
                foreach (var item in rows)
                {
                    var value = item.Values.GetValueOrDefault(field.ID);
                    if (value?.Tags != null)
                        foreach (var entry in value.Tags) _choices[entry.Key] = entry.Value;
                    else if (value?.Key != null)
                    {
                        _choices[value.Key] = field.ValueLabel(value);
                        if (_choiceOrders != null && value.Number is double order) _choiceOrders[value.Key] = order;
                    }
                }
            }
            ShowFilterStep(FilterStep.Value);
        }
        catch (Exception exception)
        {
            ShowFilterNotice(exception.Message, error: true);
        }
    }

    private void DrawValue()
    {
        Button(_sidebarTools, L("filter_back"), 64, () => ShowFilterStep(FilterStep.Conditions), Icon("iconArrowMetaLeft"));
        var heading = Label(_sidebarTools, FieldTitle(_field), 69, 20);
        heading.fontSize = 9;
        heading.resizeTextForBestFit = true;
        heading.resizeTextMinSize = 7;
        heading.resizeTextMaxSize = 9;
        if (_filterNotice != null)
            Label(NextSidebarLine(), _filterNotice, RightWidth - 6, color: new Color32(247, 145, 127, 255));
        SidebarSection("filter_comparison");
        RectTransform row = null;
        int slot = 0;
        foreach (var operation in _field.Operators)
        {
            if (slot++ % 3 == 0) row = NextSidebarLine();
            var button = Button(row, Operator(operation), 43, () =>
            {
                _operator = operation;
                ShowFilterStep(FilterStep.Value);
            }, flat: _operator != operation);
            if (_operator == operation) button.image.color = new Color(0.83f, 0.88f, 0.65f);
        }
        var enter = NextSidebarLine();
        if (_operator is RankingOperator.Present or RankingOperator.Absent)
        {
            Button(enter, L("add_condition"), RightWidth - 6, () => UseCondition(null, null));
            return;
        }

        if (_field.IsChoice || _field.Kind == RankingFieldKind.Tags)
        {
            DrawChoices(enter);
            return;
        }

        var valueInput = Input(enter, _fieldInput, 73, value => _fieldInput = value);
        if (valueInput.placeholder is Text valuePlaceholder)
            valuePlaceholder.text = string.IsNullOrWhiteSpace(_field.Unit) ? L("value") : Name(_field.Unit);
        Button(enter, L("add_condition"), 60, () =>
        {
            string value = _fieldInput.Trim();
            if (_field.Kind == RankingFieldKind.Integer &&
                long.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out long exact))
                value = exact.ToString(CultureInfo.InvariantCulture);
            else if (_field.Numeric)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) &&
                    !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                    double.IsNaN(number) || double.IsInfinity(number) ||
                    _field.Kind == RankingFieldKind.Integer && Math.Truncate(number) != number)
                {
                    ShowFilterNotice(L(_field.Kind == RankingFieldKind.Integer ? "invalid_integer" : "invalid_number"), error: true);
                    return;
                }
                value = number.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value.Length > 0) UseCondition(value, null);
        });
    }

    private void DrawChoices(RectTransform searchRow)
    {
        bool actors = _board.SourceID == BuiltInRankings.Actors;
        bool flags = _field.AffiliationType.HasValue;
        bool species = _field.ID == "species" && (actors || _source.Type == typeof(Subspecies));
        bool actorTraits = actors && _field.ID == "traits";
        bool cultureTraits = _board.SourceID == BuiltInRankings.Cultures && _field.ID == "traits";
        float top = _sidebarHeight;
        var options = Rect("RankingFilterChoices", _sidebarContent, RightWidth, 0, 0, -top);
        const float searchLabelWidth = 29;
        Label(searchRow, L("refresh_choices"), searchLabelWidth, 20, new Color32(220, 208, 168, 255)).resizeTextMaxSize = 9;
        var input = Input(searchRow, _fieldInput, RightWidth - searchLabelWidth - 6, value =>
        {
            _fieldInput = value;
            Render();
            _sidebarScroll.StopMovement();
            SidebarOffset = 0;
            _sidebarContent.anchoredPosition = Vector2.zero;
        });
        input.name = "RankingFilterSearch";
        if (input.placeholder is Text placeholder) placeholder.text = L("find_value");
        Render();

        void Render()
        {
            Clear(options);
            float height = 0;
            bool any = false;
            string query = _fieldInput.Trim();
            if (actorTraits) DrawTraits(AssetManager.traits.list, AssetManager.trait_groups.list);
            else if (cultureTraits) DrawTraits(AssetManager.culture_traits.list, AssetManager.culture_trait_groups.list);
            else
            {
                var choices = _choices.Where(item => Matches(item.Value))
                    .OrderBy(item => _choiceOrders?.GetValueOrDefault(item.Key))
                    .ThenBy(item => item.Value, StringComparer.CurrentCulture).Take(81).ToArray();
                int columns = flags ? 4 : species ? ChoiceIconColumns : 2;
                RectTransform row = null;
                int index = 0;
                foreach (var item in choices.Take(80))
                {
                    if (index++ % columns == 0)
                        row = species ? IconLine("RankingSpeciesIcons")
                            : Line(flags ? "RankingAffiliationFlags" : "RankingChoiceRow", flags ? BannerCellSize + 2 : 23);
                    if (flags) DrawAffiliationFlag(row, item.Key, item.Value);
                    else if (species)
                    {
                        var icon = AssetManager.actor_library.get(item.Key)?.getSpriteIcon() ?? Icon("iconQuestionMark") ?? FieldIcon("species");
                        BareIconButton(row, item.Value, icon, ChoiceIconCellSize, 16, () => UseChoice(item.Key, item.Value));
                    }
                    else Button(row, item.Value, 66, () => UseChoice(item.Key, item.Value), hint: item.Value);
                }
                any = choices.Length > 0;
                if (choices.Length > 80) Label(Line("RankingChoiceHint", 18), L("refine_choices"), RightWidth - 6, 17);
            }
            if (!any) Label(Line("RankingChoiceEmpty", 23), L("empty"), RightWidth - 6);
            options.sizeDelta = new Vector2(RightWidth, height);
            _sidebarHeight = top + height;
            _sidebarContent.sizeDelta = new Vector2(RightWidth, _sidebarHeight);

            RectTransform Line(string name, float lineHeight)
            {
                var row = StackLine(name, options, -height, RightWidth, lineHeight);
                height += lineHeight;
                return row;
            }

            RectTransform IconLine(string name)
            {
                var line = Line(name, ChoiceIconCellSize);
                line.GetComponent<HorizontalLayoutGroup>().spacing = 2;
                return line;
            }

            bool Matches(string value) => value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;

            void DrawTraits<TTrait>(IEnumerable<TTrait> assets, IEnumerable<BaseTraitGroupAsset> groups)
                where TTrait : BaseTrait<TTrait>
            {
                var known = assets.Where(asset => asset != null).ToDictionary(asset => asset.id, StringComparer.Ordinal);
                var entries = known.Values.Select(asset => (ID: asset.id, Label: BuiltInRankings.Label(asset.getLocaleID(), asset.id),
                        Asset: asset))
                    .Concat(_choices.Where(item => !known.ContainsKey(item.Key))
                        .Select(item => (ID: item.Key, Label: item.Value, Asset: (TTrait)null)))
                    .Where(entry => Matches(entry.Label) || Matches(entry.ID))
                    .OrderByDescending(entry => entry.Asset?.priority ?? 0)
                    .ThenBy(entry => entry.Label, StringComparer.CurrentCulture)
                    .ToLookup(entry => entry.Asset?.group_id);
                var groupIDs = new HashSet<string>(groups.Select(group => group.id));
                foreach (var group in groups)
                {
                    var color = string.IsNullOrEmpty(group.color) ? (Color)new Color32(188, 194, 161, 255) : group.getColor();
                    Group(BuiltInRankings.Label(group.getLocaleID(), group.id), group.id, color, entries[group.id]);
                }
                Group(L("ungrouped_traits"), "ungrouped", new Color32(188, 194, 161, 255),
                    entries.Where(group => !groupIDs.Contains(group.Key)).SelectMany(group => group));

                void Group(string title, string id, Color color, IEnumerable<(string ID, string Label, TTrait Asset)> traits)
                {
                    RectTransform row = null;
                    int index = 0;
                    foreach (var trait in traits)
                    {
                        if (index == 0)
                        {
                            Label(Line("RankingTraitGroup." + id, 14), title, RightWidth - 6, 13, color)
                                .resizeTextMaxSize = 9;
                            any = true;
                        }
                        if (index++ % ChoiceIconColumns == 0) row = IconLine("RankingTraitIcons");
                        BareIconButton(row, trait.Label, trait.Asset?.getSprite() ?? FieldIcon("traits"), ChoiceIconCellSize, 16,
                            () => UseCondition(trait.ID, trait.Label));
                    }
                }
            }
        }
    }

    private void DrawAffiliationFlag(Transform row, string key, string title)
    {
        var item = BuiltInRankings.FindMeta(_field.AffiliationType.Value, key);
        var asset = BannerAsset(item);
        void SelectFlag() => UseChoice(key, title);
        if (asset?.get_banner == null)
        {
            BareIconButton(row, title, FieldIcon(_field.ID), BannerCellSize, 23, SelectFlag);
            return;
        }

        var button = BareIconButton(row, title, null, BannerCellSize, 0, SelectFlag);
        CreateBanner(asset, item, button.transform, BannerCellSize - 2, BannerCellSize - 2);
    }

    private void UseChoice(string key, string title)
    {
        if (_field.Ordered && _operator is RankingOperator.AtLeast or RankingOperator.AtMost)
        {
            if (_choiceOrders.TryGetValue(key, out double order))
                UseCondition(key, title, choiceKey: true, orderHint: _field.Options.Count == 0 ? order : null);
            return;
        }
        UseCondition(key, title);
    }

    private void UseCondition(string value, string label, bool choiceKey = false, double? orderHint = null) => AddToken(new RankingToken
    {
        Symbol = RankingSymbol.Value,
        Condition = new RankingCondition
        {
            Field = _field.ID, Operator = _operator, Value = value, Label = label, ChoiceKey = choiceKey, OrderHint = orderHint
        }
    });

    private string FilterError(string invalid) =>
        invalid == StandardDuelPower.BaseFieldID && _board?.SourceID == BuiltInRankings.Actors
            ? L("power.legacy_filter")
            : invalid == null ? L("invalid") : string.Format(L("unavailable"), invalid);

    private void ShowFilterNotice(string message, bool error = false)
    {
        _filterNotice = message;
        _filterError = error;
        if (_filterStep == FilterStep.Conditions) DrawFilterLine();
        else DrawSidebar();
    }

    private RankingPreset DraftView()
    {
        var view = CurrentView();
        if (_view.EditDisplay) view.DisplayConditions = _view.Draft;
        else view.Conditions = _view.Draft;
        return RankingStore.Copy(view);
    }

    private void PreviewFilter()
    {
        try
        {
            var snapshot = CreateSnapshot(_board, DraftView(), _view.Snapshot?.Data);
            ShowFilterNotice(string.Format(L("filter_preview_count"), snapshot?.Visible.Count ?? 0));
        }
        catch (Exception exception) { ShowFilterNotice(exception.Message, error: true); }
    }

    private void ApplyFilters()
    {
        var next = DraftView();
        RankingSnapshot snapshot;
        try { snapshot = CreateSnapshot(_board, next, _view.Snapshot?.Data, recordPeaks: true); }
        catch (Exception exception) { ShowFilterNotice(exception.Message, error: true); return; }
        _view.Conditions = new List<RankingToken>(next.Conditions);
        _view.DisplayConditions = new List<RankingToken>(next.DisplayConditions);
        _view.Draft = null;
        _view.Scroll = 0;
        _filterNotice = null;
        _notice = null;
        RememberFeatured(snapshot);
        ShowSnapshot(snapshot);
    }

    private void EditFilterDraft(List<RankingToken> conditions, string error = null, bool display = false)
    {
        _view.EditDisplay = display;
        _view.Draft = conditions;
        _view.DraftCursor = conditions.Count;
        _filterStep = FilterStep.Conditions;
        _conditionOffset = 0;
        _filterNotice = error;
        _filterError = error != null;
        _sidebar = Sidebar.Filters;
        Go(Page.Table);
    }

    private void DrawPresetSidebar()
    {
        var nameInput = Input(_sidebarTools, _view.PresetName, 81, value => _view.PresetName = value.Trim());
        nameInput.name = "RankingPresetName";
        SetInputPlaceholder(nameInput, L("preset_name"), TextAnchor.MiddleLeft);
        var save = Button(_sidebarTools, L("save_preset"), 53, () => { SavePreset(); DrawSidebar(); });
        AddPresetTooltip(save, () => L("preset_current"), () => _view.Sort, () => _view.Conditions,
            () => _view.DisplayConditions, () => _view.PinnedColumns);
        nameInput.onValueChanged.AddListener(_ => UpdateSaveButton());
        UpdateSaveButton();

        if (_presetNotice != null)
            Label(NextSidebarLine(), _presetNotice, RightWidth - 6, color: new Color32(247, 145, 127, 255));
        SidebarSection("preset_saved", 16);

        var presets = RankingStore.Presets.Where(preset => preset.BoardID == _board.ID).ToArray();
        if (presets.Length == 0) Label(NextSidebarLine(), L("no_presets"), RightWidth - 6);
        DrawSavedPresets(presets);

        int FindPreset() => RankingStore.Presets.FindIndex(item => item.BoardID == _board.ID && item.Name == _view.PresetName);

        void UpdateSaveButton()
        {
            string label = L(FindPreset() >= 0 ? "update_preset" : "save_preset");
            save.name = label;
            save.GetComponentInChildren<Text>().text = label;
            save.interactable = _view.PresetName.Length > 0;
        }

        void SavePreset()
        {
            if (_view.PresetName.Length == 0) return;
            int index = FindPreset();
            var saved = RankingStore.Copy(CurrentView());
            var previous = index >= 0 ? RankingStore.Presets[index] : null;
            saved.ID = previous?.ID ?? Guid.NewGuid().ToString("N");
            saved.Favorite = previous?.Favorite == true;
            saved.PreviewCount = previous?.PreviewCount ?? RankingPreview.DefaultLeaderCount;
            if (index < 0) RankingStore.Presets.Add(saved);
            else
            {
                if (!RankingSnapshot.SameSettings(previous, saved, includeColumns: true)) ForgetFeatured(previous);
                RankingStore.Presets[index] = saved;
            }
            RememberFeatured(_view.Snapshot);
            _presetNotice = null;
        }
    }

    private void DrawSavedPresets(IEnumerable<RankingPreset> presets)
    {
        const float iconSize = 16;
        const float nameWidth = RightWidth - 3 * iconSize - 15;
        Action cancelActive = null;
        foreach (var preset in presets)
        {
            var row = NextSidebarLine(22);
            row.name = "RankingPreset";
            ShowPreset();

            Button ActionIcon(string id, string hint, Sprite icon, Action action)
            {
                var button = BareIconButton(row, L(hint), icon, iconSize, 12, action);
                button.name = id;
                return button;
            }

            void ShowPreset()
            {
                var button = Button(row, preset.Name, nameWidth, () => UsePreset(preset));
                AlignCaption(button, 6);
                var caption = button.GetComponentInChildren<Text>();
                caption.rectTransform.offsetMin = new Vector2(6, 3);
                caption.rectTransform.offsetMax = new Vector2(-3, -3);
                AddPresetTooltip(button, () => preset.Name, () => preset.Sort, () => preset.Conditions,
                    () => preset.DisplayConditions, () => preset.PinnedColumns);
                var favorite = ActionIcon("RankingPresetFavorite", preset.Favorite ? "unfeature_view" : "feature_view",
                    Icon("iconFavoriteStar"), ToggleFavorite);
                if (!preset.Favorite) favorite.image.color = new Color(0.64f, 0.64f, 0.64f, 0.55f);
                var edit = ActionIcon("RankingPresetRename", "rename_preset", Icon("iconEdit"), BeginRename);
                edit.image.rectTransform.sizeDelta = new Vector2(24, 24);
                ActionIcon("RankingPresetDelete", "delete_preset",
                    SpriteTextureLoader.getSprite("inmny/godtools/icons/iconRankingDelete"), BeginDelete);
            }

            void ToggleFavorite()
            {
                cancelActive?.Invoke();
                preset.Favorite = !preset.Favorite;
                if (preset.Favorite) RememberFeatured(_view.Snapshot);
                else ForgetFeatured(preset);
                Tooltip.hideTooltipNow();
                Clear(row);
                ShowPreset();
            }

            void OpenEditor()
            {
                cancelActive?.Invoke();
                Tooltip.hideTooltipNow();
                Clear(row);
                cancelActive = Cancel;
            }

            void Cancel()
            {
                cancelActive = null;
                Clear(row);
                ShowPreset();
            }

            void BeginRename()
            {
                OpenEditor();
                var input = Input(row, preset.Name, nameWidth, _ => { });
                input.name = "RankingPresetRenameInput";
                SetInputPlaceholder(input, L("preset_name"), TextAnchor.MiddleLeft);
                var confirm = ActionIcon("RankingPresetConfirmRename", "confirm_preset_rename", Icon("iconOn"), Rename);
                ActionIcon("RankingPresetCancel", "cancel_preset_action", Icon("iconOff"), Cancel);
                input.onValueChanged.AddListener(_ => UpdateConfirmation());
                input.onEndEdit.AddListener(_ =>
                {
                    if (!input.isActiveAndEnabled) return;
                    if (input.wasCanceled) Cancel();
                    else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
                        Rename();
                });
                input.ActivateInputField();

                void UpdateConfirmation()
                {
                    string name = input.text.Trim();
                    bool duplicate = HasName(name);
                    confirm.interactable = name.Length > 0 && !duplicate;
                    var tip = confirm.GetComponent<TipButton>();
                    tip.setHoverAction(() => Tooltip.show(confirm.gameObject, "normal",
                        new TooltipData { tip_name = L(duplicate ? "preset_duplicate" : "confirm_preset_rename") }), false);
                }

                bool HasName(string name) => RankingStore.Presets.Any(other =>
                    other != preset && other.BoardID == preset.BoardID && other.Name == name);

                void Rename()
                {
                    string name = input.text.Trim();
                    if (name.Length == 0 || HasName(name)) return;
                    if (_view.PresetName == preset.Name) _view.PresetName = name;
                    preset.Name = name;
                    _presetNotice = null;
                    Tooltip.hideTooltipNow();
                    DrawSidebar();
                }
            }

            void BeginDelete()
            {
                OpenEditor();
                Label(row, L("confirm_preset_delete"), nameWidth, 18, new Color32(247, 145, 127, 255)).resizeTextMaxSize = 9;
                ActionIcon("RankingPresetConfirmDelete", "delete_preset", Icon("iconOn"), () =>
                {
                    RankingStore.Presets.Remove(preset);
                    ForgetFeatured(preset);
                    if (_view.PresetName == preset.Name) _view.PresetName = "";
                    _presetNotice = null;
                    Tooltip.hideTooltipNow();
                    DrawSidebar();
                });
                ActionIcon("RankingPresetCancel", "cancel_preset_action", Icon("iconOff"), Cancel);
            }
        }
    }

    private static void RegisterPresetTooltip()
    {
        AssetManager.tooltips.add(new TooltipAsset
        {
            id = PresetTooltip,
            callback = (tooltip, _, data) =>
            {
                var preset = (PresetTooltipData)data;
                const float padding = 18;
                ConfigureDescription(tooltip.description, 10);
                ConfigureDescription(tooltip.description_2, 9);
                var color = tooltip.background.color;
                color.a = 0.97f;
                tooltip.background.color = color;
                tooltip.name.supportRichText = false;
                tooltip.name.resizeTextMaxSize = 11;
                tooltip.name.resizeTextMinSize = 8;
                tooltip.name.fontSize = 11;

                float titleWidth = MeasureText(tooltip.name, data.tip_name);
                float descriptionWidth = Mathf.Max(MeasureText(tooltip.description, data.tip_description),
                    MeasureText(tooltip.description_2, data.tip_description_2));
                float iconWidth = preset.Sort.Length == 0 ? 0 :
                    2 * PresetIconInset + preset.Sort.Length * (PresetIconCell + PresetIconGap) - PresetIconGap;
                float scale = CanvasMain.instance.canvas_tooltip.scaleFactor * data.tooltip_scale;
                float maxWidth = Mathf.Min(188, Mathf.Max(1f, Screen.width / scale - 20));
                float width = Mathf.Clamp(Mathf.Ceil(Mathf.Max(titleWidth, descriptionWidth, iconWidth) + padding), Mathf.Min(92, maxWidth), maxWidth);
                float bodyWidth = Mathf.Max(1f, width - padding);
                SetWidth(tooltip.transform, width);
                SetWidth(tooltip.topGraphics.transform, width);
                SetWidth(tooltip.name.transform, bodyWidth);
                SetWidth(tooltip.description.transform.parent, bodyWidth);
                SetWidth(tooltip.description.transform, bodyWidth);
                SetWidth(tooltip.description_2.transform.parent, bodyWidth);
                SetWidth(tooltip.description_2.transform, bodyWidth);
                tooltip.name.text = data.tip_name;
                tooltip.setDescription(data.tip_description);
                tooltip.setBottomDescription(data.tip_description_2);
                DrawPresetSortIcons(tooltip, preset, bodyWidth);

                static void ConfigureDescription(Text description, int fontSize)
                {
                    description.supportRichText = false;
                    description.alignment = TextAnchor.MiddleLeft;
                    description.resizeTextForBestFit = false;
                    description.fontSize = fontSize;
                    description.fontStyle = FontStyle.Normal;
                    description.lineSpacing = 1.15f;
                    description.color = new Color32(244, 238, 216, 255);
                }

                static float MeasureText(Text label, string value)
                {
                    var settings = label.GetGenerationSettings(Vector2.zero);
                    settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                    settings.resizeTextForBestFit = false;
                    return label.cachedTextGeneratorForLayout.GetPreferredWidth(value, settings) / label.pixelsPerUnit;
                }
            }
        });
    }

    private static void DrawPresetSortIcons(Tooltip tooltip, PresetTooltipData data, float width)
    {
        const string rowName = "RankingPresetSortIcons";
        var row = tooltip.transform.Find(rowName)?.GetComponent<TooltipIconsRow>();
        if (data.Sort.Length == 0)
        {
            if (row != null) row.gameObject.SetActive(false);
            return;
        }

        if (row == null)
        {
            var prefab = Resources.Load<Tooltip>("tooltips/tooltip_actor").GetComponentInChildren<TooltipActorTraitsRow>(true);
            var native = Instantiate(prefab, tooltip.transform, false);
            native.name = rowName;
            native.transform.SetSiblingIndex(tooltip.description.transform.parent.GetSiblingIndex() + 1);
            native.GetComponent<Image>().raycastTarget = false;
            row = native.gameObject.AddComponent<TooltipIconsRow>();
            row.items_parent = native.items_parent;
            row.item = native.item;
            row.item.raycastTarget = false;
            row.item.preserveAspect = true;
            var content = (RectTransform)row.items_parent;
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layoutGrid = content.GetComponent<GridLayoutGroup>();
            layoutGrid.childAlignment = TextAnchor.MiddleLeft;
            layoutGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layoutGrid.cellSize = new Vector2(PresetIconCell, PresetIconCell);
            layoutGrid.spacing = new Vector2(PresetIconGap, PresetIconGap);
            layoutGrid.padding = new RectOffset(PresetIconInset, PresetIconInset, 2, 2);
            Destroy(native);
        }

        int columns = Mathf.Max(1, Mathf.FloorToInt((width - PresetIconInset * 2 + PresetIconGap) / (PresetIconCell + PresetIconGap)));
        row.items_parent.GetComponent<GridLayoutGroup>().constraintCount = columns;
        foreach (var icon in data.Icons) row.addIcon(icon);
        row.init(tooltip, data);
        float height = Mathf.Ceil(data.Sort.Length / (float)columns) * (PresetIconCell + PresetIconGap) + 1;
        var layout = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;
        ((RectTransform)row.transform).sizeDelta = new Vector2(width, height);

        var items = row.items_pool.getListTotal();
        for (int index = 0; index < data.Sort.Length; index++)
        {
            var icon = items[index];
            icon.name = "RankingPresetSortIcon";
            var corner = icon.transform.Find("RankingSortCorner")?.GetComponent<Image>();
            if (corner == null) SortCorner(icon.transform, data.Sort[index].Ascending);
            else corner.sprite = SortDirectionIcon(data.Sort[index].Ascending);
        }
    }

    private void AddPresetTooltip(Button button, Func<string> title,
        Func<IReadOnlyList<RankingSort>> sorting, Func<IReadOnlyList<RankingToken>> filtering,
        Func<IReadOnlyList<RankingToken>> displayed, Func<IReadOnlyList<string>> pinnedColumns) =>
        AddPresetTooltip(button, _source, title, sorting, filtering, displayed, pinnedColumns);

    private static void AddPresetTooltip(Button button, RankingRegistry.Source source, Func<string> title,
        Func<IReadOnlyList<RankingSort>> sorting, Func<IReadOnlyList<RankingToken>> filtering,
        Func<IReadOnlyList<RankingToken>> displayed, Func<IReadOnlyList<string>> pinnedColumns, bool showOnPress = false)
    {
        var tip = button.GetComponent<TipButton>() ?? button.gameObject.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(Show, false);
        if (showOnPress) button.onClick.AddListener(Show);

        void Show()
        {
            var keys = sorting().Where(item => item?.Field != null).ToArray();
            string columns = string.Join(" · ", (pinnedColumns() ?? Array.Empty<string>())
                .Where(id => id != null && id != "name" && source?.Fields.ContainsKey(id) == true)
                .Select(id => Name(source.Fields[id].Name)));
            string conditions = $"{L("preset_conditions")}\n\n{DescribePresetFilters(filtering(), source)}";
            if (displayed().Count > 0)
                conditions += $"\n\n{L("filter_display")}\n{DescribePresetFilters(displayed(), source)}";
            Tooltip.show(button.gameObject, PresetTooltip, new PresetTooltipData
            {
                tip_name = title(),
                tip_description = keys.Length == 0 ? $"{L("sort_keys")}\n{L("default_sort")}" : L("sort_keys"),
                tip_description_2 = columns.Length == 0 ? conditions : $"{L("pinned_columns")}\n{columns}\n\n{conditions}",
                Sort = keys, Icons = keys.Select(key => FieldIcon(source, key.Field)).ToArray()
            });
        }
    }

    private static string DescribePresetFilters(IReadOnlyList<RankingToken> conditions, RankingRegistry.Source source)
    {
        if (conditions.Count == 0) return L("preset_all_entries");
        var output = new StringBuilder();
        int depth = 0;
        bool negate = false;
        foreach (var token in conditions)
        {
            switch (token?.Symbol)
            {
                case RankingSymbol.And:
                case RankingSymbol.Or:
                    StartLine();
                    output.Append(L(token.Symbol == RankingSymbol.And ? "preset_and" : "preset_or")).Append(' ');
                    break;
                case RankingSymbol.Open:
                    if (negate) output.Append(L("preset_not")).Append(' ');
                    negate = false;
                    output.Append(Symbol(token.Symbol));
                    depth++;
                    StartLine();
                    break;
                case RankingSymbol.Close:
                    depth = Math.Max(0, depth - 1);
                    StartLine();
                    output.Append(Symbol(token.Symbol));
                    break;
                case RankingSymbol.Not:
                    negate = !negate;
                    break;
                default:
                    output.Append(TokenLabel(token, source, quoteValue: true, negate: negate));
                    negate = false;
                    break;
            }
        }
        return output.ToString();

        void StartLine() => output.Append('\n').Append('\u3000', Math.Min(depth, 4));
    }

    private void UsePreset(RankingPreset saved, RankingSnapshot cached = null)
    {
        var preset = RankingStore.Copy(saved);
        if (!RankingConditions.TryCompile(preset.Conditions, _source, out _, out string invalid))
        {
            EditFilterDraft(preset.Conditions, FilterError(invalid));
            return;
        }
        if (!RankingConditions.TryCompile(preset.DisplayConditions, _source, out _, out invalid))
        {
            EditFilterDraft(preset.DisplayConditions, FilterError(invalid), display: true);
            return;
        }
        var unknown = preset.Sort.FirstOrDefault(item => item != null &&
            (item.Field == null || _source.Fields.GetValueOrDefault(item.Field)?.Sortable != true));
        if (unknown != null)
        {
            _presetNotice = string.Format(L("unavailable"), unknown.Field ?? "—");
            _presetOffset = 0;
            _sidebar = Sidebar.Presets;
            Enter(Page.Table);
            return;
        }
        _view.Sort = preset.Sort.Where(item => item != null).ToList();
        _view.PinnedColumns = (preset.PinnedColumns ?? new List<string>())
            .Where(IsDisplayColumn).Distinct().Take(PinLimit).ToList();
        _view.RowLayout = (preset.RowLayout ?? _board.RowLayout).Copy();
        _view.Conditions = preset.Conditions;
        _view.DisplayConditions = preset.DisplayConditions;
        _view.EditDisplay = false;
        _view.ScopeDraft.Tokens = _view.DisplayDraft.Tokens = null;
        _view.PresetName = preset.Name;
        _filterStep = FilterStep.Conditions;
        _filterNotice = null;
        _presetNotice = null;
        _sortOffset = _conditionOffset = _pinOffset = 0;
        _view.Scroll = 0;
        _notice = null;
        if (cached == null) Refresh();
        else ShowSnapshot(cached);
    }

    private static string Symbol(RankingSymbol symbol) => L("symbol." + symbol.ToString().ToLowerInvariant());
    private static string Operator(RankingOperator value) => L("operator." + value.ToString().ToLowerInvariant());
    private string TokenLabel(RankingToken token) => TokenLabel(token, _source);

    private static string TokenLabel(RankingToken token, RankingRegistry.Source source, bool quoteValue = false, bool negate = false)
    {
        if (token?.Symbol != RankingSymbol.Value || token.Condition == null)
            return token == null ? "?" : Symbol(token.Symbol);
        var condition = token.Condition;
        var field = condition.Field == null ? null : source?.Fields.GetValueOrDefault(condition.Field);
        string label = field != null ? Name(field.Name) : condition.Field ?? "?";
        if (condition.Field == StandardDuelPower.BaseFieldID && ReferenceEquals(source, RankingRegistry.GetSource(BuiltInRankings.Actors)))
            label = L("power.legacy");
        string value = condition.Label ?? condition.Value;
        if ((condition.ChoiceKey || condition.Operator == RankingOperator.Equal) && field?.IsChoice == true && condition.Value != null)
            value = field.Options.TryGetValue(condition.Value, out var option)
                ? option.LocalizedName() : BuiltInRankings.Label(condition.Value, value);
        if (quoteValue && !string.IsNullOrEmpty(value) && field?.Numeric != true)
            value = string.Format(L("preset_quote"), value);
        if (quoteValue && condition.Operator == RankingOperator.Contains)
            return string.Format(L(negate ? "preset_not_contains" : "preset_contains"), label, value);
        string text = $"{label} {Operator(condition.Operator)} {value}".TrimEnd();
        return negate ? $"{L("preset_not")} {text}" : text;
    }
}
