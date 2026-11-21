using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features;
using GodTools.Features.Rankings;
using GodTools.Libraries;
using GodTools.UI.Prefabs;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings : AbstractWideWindow<WindowRankings>
{
    private static PowerButton _archivePlacement;

    internal static void PlaceArchivedActor(long id)
    {
        ConsistentCreaturePowerTop.SelectCreatureToPlace(id);
        ScrollWindow.hideAllEvent(pWithAnimation: false);
        if (_archivePlacement == null)
            _archivePlacement = PowerButtonCreator.CreateGodPowerButton(GodPowers.place_saved_actor_in_top.id,
                SpriteTextureLoader.getSprite("gt_windows/save_actor_list"), Main.prefabs);
        if (World.world.selected_power != GodPowers.place_saved_actor_in_top)
            _archivePlacement.clickActivePower();
    }

    private const float Width = 411;
    private const float LeftWidth = 108;
    private const float RightWidth = 142;
    private const float CollapsedWidth = 32;
    private const float CompactButtonSize = 24;
    private const float CompactStep = 26;
    private const float CompactInset = 3;
    private const float RailTop = 120;
    private const float RailHeight = 248;
    private const float PaneLeft = -284.5f;
    private const float PaneRight = 278.5f;
    private const float PaneGap = 3.5f;
    private const float ScrollbarGutter = NativeListPane.ScrollbarGutter;
    private const float ControlHeight = 25;
    private const float OverviewLineHeight = 22;
    private const float RowHeight = 39;
    private const float RowGap = 3;
    private const float PortraitNameInset = 44;
    private const float ActorPortraitScale = 0.65f;
    private const float MinIdentityWidth = 112;
    private const float MinMetricWidth = 39;
    private const float MaxMetricWidth = 64;
    private const float MinTableWidth = PaneRight - PaneLeft - LeftWidth - RightWidth - 2 * PaneGap - 3 * ScrollbarGutter;
    private const float MaxTableWidth = PaneRight - PaneLeft - 2 * (CollapsedWidth + PaneGap + ScrollbarGutter);
    private enum Page { Table, Overview, Details, History, Record }

    private sealed class State
    {
        internal List<RankingToken> Conditions = new();
        internal List<RankingToken> DisplayConditions = new();
        internal bool EditDisplay;
        internal readonly FilterDraft ScopeDraft = new();
        internal readonly FilterDraft DisplayDraft = new();
        private FilterDraft ActiveDraft => EditDisplay ? DisplayDraft : ScopeDraft;
        internal List<RankingToken> Draft { get => ActiveDraft.Tokens; set => ActiveDraft.Tokens = value; }
        internal int DraftCursor { get => ActiveDraft.Cursor; set => ActiveDraft.Cursor = value; }
        internal RankingSnapshot Snapshot;
        internal List<RankingSort> Sort = new();
        internal List<string> PinnedColumns = new();
        internal RankingRowLayout RowLayout;
        internal string PresetName = "";
        internal float Scroll;
    }

    private sealed class FilterDraft
    {
        internal List<RankingToken> Tokens;
        internal int Cursor;
    }

    private sealed class Line
    {
        internal RectTransform Root;
        internal Text Rank;
        internal Text Name;
        internal Image Icon;
        internal Image IconFrame;
        internal RectTransform Portrait;
        internal UiUnitAvatarElement Avatar;
        internal Image AvatarFrame;
        internal IBanner Banner;
        internal Button Inspect;
        internal Button Locate;
        internal Button Place;
        internal Button PlaceInVillage;
        internal CreatureSavedList.SavedActorPacket Creature;
        internal readonly List<Metric> Metrics = new();
        internal RankingRow Entry;
    }

    private sealed class Metric
    {
        internal Button Details;
        internal Text Value;
        internal RankingValue Reading;
        internal string Title;
        internal Image Icon;
        internal RectTransform Meter;
        internal Image Fill;
    }

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly List<Line> _pool = new();
    private RankingBoard _board;
    private RankingRegistry.Source _source;
    private State _view;
    private MapStats _map;
    private object _worldIdentity;
    private Page _page;
    private RectTransform _tools;
    private RectTransform _caption;
    private RectTransform _header;
    private RectTransform _content;
    private RectTransform _panel;
    private RectTransform _list;
    private ScrollRect _scroll;
    private ScrollRect _navigation;
    private RectTransform _navigationContent;
    private RectTransform _navigationRail;
    private RectTransform _navigationHeader;
    private ScrollRect _sidebarScroll;
    private RectTransform _sidebarContent;
    private RectTransform _sidebarRail;
    private RectTransform _sidebarTabs;
    private RectTransform _sidebarTools;
    private RectTransform _filterActions;
    private UiUnitAvatarElement _avatarPrefab;
    private Sprite[] _podiumFrames;
    private List<RankingRow> _rows = new();
    private string[] _columnCandidates = Array.Empty<string>();
    private string[] _columns = Array.Empty<string>();
    private float _identityWidth;
    private float _metricWidth;
    private string _notice;
    private string _detailTitle;
    private string _detailDescription;
    private bool _drawing;
    private bool _navigationCollapsed;
    private int _lineIndex;
    private int _firstRow = -1;

    private bool ShowSidebar => _page == Page.Table && _sidebar != Sidebar.None;
    private float NavigationWidth => _navigationCollapsed ? CollapsedWidth : LeftWidth;
    private float NavigationX => PaneLeft + (NavigationWidth + ScrollbarGutter) * 0.5f;
    private float SidebarWidth => ShowSidebar ? RightWidth + ScrollbarGutter : CollapsedWidth;
    private float SidebarX => PaneRight - SidebarWidth * 0.5f;
    private float BoardLeft => PaneLeft + NavigationWidth + ScrollbarGutter + PaneGap;
    private float BoardRight => _page == Page.Table ? PaneRight - SidebarWidth - PaneGap : PaneRight;
    private float BoardX => (BoardLeft + BoardRight) * 0.5f;
    private float TableWidth => BoardRight - BoardLeft - ScrollbarGutter;
    private static float ActionWidth(float tableWidth) => Mathf.Round(Mathf.Lerp(17, 22, Mathf.InverseLerp(279, Width, tableWidth)));
    private static float ColumnSpace(float tableWidth, RankingRegistry.Source source)
    {
        int actions = (source.Locate == null ? 0 : 1) + (source.Place == null ? 0 : 1) + (source.SavedCreature == null ? 0 : 1);
        return tableWidth - 2 * RowGap - actions * (ActionWidth(tableWidth) + RowGap);
    }
    private static int ColumnsThatFit(float tableWidth, RankingRegistry.Source source) =>
        Mathf.Max(0, Mathf.FloorToInt((ColumnSpace(tableWidth, source) - MinIdentityWidth) / (MinMetricWidth + RowGap)));
    private int PinLimit => ColumnsThatFit(MinTableWidth, _source);

    protected override void Init()
    {
        RegisterPresetTooltip();
        RegisterMetricTooltip();
        RegisterPeakTooltip();
        _scroll = ContentTransform.GetComponentInParent<ScrollRect>(true);
        _navigation = Instantiate(_scroll, BackgroundTransform);
        _navigation.name = "RankingNavigation";
        _navigationContent = NativeListPane.Configure(_navigation, BackgroundTransform, NavigationWidth, 222, NavigationX, -14);
        _navigationRail = CreateRail(_navigation, "RankingNavigationBackground", NavigationWidth, NavigationX);
        _navigationHeader = Band("RankingNavigationHeader", 108, NavigationWidth, NavigationX - 4);
        _sidebarScroll = Instantiate(_scroll, BackgroundTransform);
        _sidebarScroll.name = "RankingRightSidebar";
        _sidebarContent = NativeListPane.Configure(_sidebarScroll, BackgroundTransform, RightWidth, 198, SidebarX, -26);
        _sidebarRail = CreateRail(_sidebarScroll, "RankingRightBackground", RightWidth, SidebarX);
        _sidebarTabs = Band("RankingRightTabs", 108, RightWidth, SidebarX - 4);
        _sidebarTools = Band("RankingRightTools", 85, RightWidth, SidebarX - 4);
        _filterActions = Rect("RankingFilterActions", BackgroundTransform, RightWidth, FilterActionsHeight, 0, 0);
        Place(_filterActions, BackgroundTransform, RightWidth, FilterActionsHeight,
            SidebarX - 4, 73 - FilterActionsHeight * 0.5f, centered: true);
        _sidebarScroll.onValueChanged.AddListener(_ =>
        {
            if (ShowSidebar) SidebarOffset = _sidebarContent.anchoredPosition.y;
        });
        _content = NativeListPane.Configure(_scroll, BackgroundTransform, Width, 188, BoardX, -32);
        if (GetComponent<ScrollWindow>().scrollingGradient is Image gradient)
            gradient.enabled = false;
        var actorList = Resources.Load<ListWindow>("windows/list_favorite_units");
        _avatarPrefab = actorList?._list_element_prefab?.GetComponent<PrefabUnitElement>()?.avatarElement;
        const string portraitPath = "inmny/godtools/ui/rankings/portrait_";
        _podiumFrames = new[]
        {
            SpriteTextureLoader.getSprite(portraitPath + "gold"),
            SpriteTextureLoader.getSprite(portraitPath + "silver"),
            SpriteTextureLoader.getSprite(portraitPath + "bronze")
        };
        _tools = Band("RankingToolbar", 109, Width, BoardX - 4);
        _caption = Band("RankingSummary", 88, Width, BoardX - 4);
        _header = Band("RankingColumns", 72, Width, BoardX - 4);
        _list = Rect("RankingEntries", _content, Width, 0, 0, 0);
        _scroll.onValueChanged.AddListener(_ =>
        {
            if (_drawing || _page != Page.Table || _view == null) return;
            _view.Scroll = _content.anchoredPosition.y;
            ShowVisible();
        });
        InitDistribution();
    }

    private RectTransform CreateRail(ScrollRect pane, string name, float width, float x)
    {
        var rail = Rect(name, BackgroundTransform, width + 11, RailHeight, 0, 0);
        rail.SetSiblingIndex(pane.transform.GetSiblingIndex());
        rail.gameObject.AddComponent<Image>();
        LayoutRail(rail, width + 11, RailHeight, x);
        return rail;
    }

    private void LayoutRail(RectTransform rail, float width, float height, float x, bool compact = false)
    {
        Place(rail, BackgroundTransform, width, height, x, RailTop - height * 0.5f, centered: true);
        var image = rail.GetComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite(compact
            ? "ui/special/windowInnerSlicedRoundWhite" : "ui/special/windowInnerSliced");
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = compact ? 1.6f : 1;
        image.color = compact ? (Color)new Color32(39, 45, 36, 226) : new Color(0.77f, 0.80f, 0.72f, 0.76f);
        image.raycastTarget = false;
    }

    private int _fallbackRevision;

    public override void OnNormalEnable()
    {
        CultiwayCompatibility.InstallRankings();
        OptionalActorRankingFields.Install();
        bool providersChanged = _fallbackRevision != RankingRegistry.FallbackRevision;
        _fallbackRevision = RankingRegistry.FallbackRevision;
        var current = World.world?.map_stats;
        if (!ReferenceEquals(_map, current) || _worldIdentity != RankingStore.WorldIdentity)
        {
            _map = current;
            _worldIdentity = RankingStore.WorldIdentity;
            _states.Clear();
            _featured.Clear();
            _archive = null;
            _compare = null;
            _view = null;
            _board = null;
            _page = Page.Overview;
            ResetSidebarEditors();
        }
        if (providersChanged) _featured.Clear();
        if (providersChanged && _board != null)
        {
            var replacement = RankingRegistry.GetBoard(_board.ID);
            if (replacement != null && replacement != _board)
            {
                Select(replacement);
                return;
            }
            _board = replacement;
        }
        if (_page is Page.Overview or Page.History or Page.Record)
        {
            Go(_page);
            return;
        }
        Go(_board == null ? Page.Overview : Page.Table);
    }

    private void Select(RankingBoard board)
    {
        BindBoard(board);
        Refresh(reload: true);
    }

    private void BindBoard(RankingBoard board)
    {
        if (_board != board) ResetSidebarEditors();
        _board = board;
        _source = RankingRegistry.GetSource(board.SourceID);
        if (!_states.TryGetValue(board.ID, out _view))
        {
            _view = new State { RowLayout = board.RowLayout.Copy() };
            if (!_view.RowLayout.FillColumns)
                _view.PinnedColumns = board.Columns.Where(IsDisplayColumn).Take(PinLimit).ToList();
            if (board.DefaultSort != null && _source.Fields.GetValueOrDefault(board.DefaultSort)?.Sortable == true)
                _view.Sort.Add(new RankingSort { Field = board.DefaultSort, Ascending = board.Ascending });
            _states.Add(board.ID, _view);
        }
        _rows = _view.Snapshot?.Visible ?? new List<RankingRow>();
        _columnCandidates = ColumnCandidates(board, _source, CurrentView());
    }

    private bool IsDisplayColumn(string id) => id != null && id != "name" && _source.Fields.ContainsKey(id);

    private static string[] ColumnCandidates(RankingBoard board, RankingRegistry.Source source, RankingPreset view) =>
        view.Sort.Select(item => item.Field)
            .Concat(view.PinnedColumns)
            .Concat((view.RowLayout?.FillColumns ?? board.RowLayout.FillColumns) ? board.Columns : Array.Empty<string>())
            .Where(id => id != null && id != "name" && source.Fields.ContainsKey(id))
            .Distinct().Take(ColumnsThatFit(MaxTableWidth, source)).ToArray();

    private void FitColumns(float width)
    {
        _columns = _columnCandidates.Take(ColumnsThatFit(width, _source)).ToArray();
        float space = ColumnSpace(width, _source);
        int metrics = _columns.Length;
        _metricWidth = metrics == 0 ? 0 : Mathf.Min(MaxMetricWidth, Mathf.Floor((space - MinIdentityWidth) / metrics) - RowGap);
        _identityWidth = space - metrics * (_metricWidth + RowGap);
    }

    private RankingPreset CurrentView() => new()
    {
        BoardID = _board.ID, Name = string.IsNullOrWhiteSpace(_view.PresetName) ? Name(_board.Name) : _view.PresetName,
        Conditions = _view.Conditions, DisplayConditions = _view.DisplayConditions,
        Sort = _view.Sort, PinnedColumns = _view.PinnedColumns, RowLayout = _view.RowLayout
    };

    private string[] SnapshotFields(RankingBoard board, RankingRegistry.Source source, RankingPreset view) =>
        ColumnCandidates(board, source, view).Concat(RankingQuery.RuleFields(view)).Distinct().ToArray();

    private RankingSnapshot CreateSnapshot(RankingBoard board, RankingPreset view, RankingDataset previous = null, bool recordPeaks = false)
    {
        if (_map == null) return null;
        var source = RankingRegistry.GetSource(board.SourceID);
        if (source == null) throw new InvalidOperationException(string.Format(L("unavailable"), board.SourceID));
        var invalidSort = view.Sort.FirstOrDefault(key => key.Field == null || source.Fields.GetValueOrDefault(key.Field)?.Sortable != true);
        if (invalidSort != null) throw new InvalidOperationException(FilterError(invalidSort.Field ?? "—"));
        var fields = SnapshotFields(board, source, view);
        var data = previous?.Covers(source, fields) == true ? previous : RankingQuery.Capture(source, fields);
        if (!RankingQuery.TryEvaluate(data, view, out var snapshot, out string invalid))
            throw new InvalidOperationException(FilterError(invalid));
        if (recordPeaks && board.SourceID == BuiltInRankings.Actors)
            RankingPowerProfiles.Record(fields, data);
        return snapshot;
    }

    private void ShowSnapshot(RankingSnapshot snapshot)
    {
        _view.Snapshot = snapshot;
        _columnCandidates = ColumnCandidates(_board, _source, CurrentView());
        _rows = snapshot?.Visible ?? new List<RankingRow>();
        Enter(Page.Table);
    }

    private void Refresh(bool recordPeaks = true, bool reload = false)
    {
        _notice = null;
        RankingSnapshot snapshot = null;
        try
        {
            _view.PinnedColumns.RemoveAll(id => !IsDisplayColumn(id));
            snapshot = CreateSnapshot(_board, RankingStore.Copy(CurrentView()), reload ? null : _view.Snapshot?.Data, recordPeaks);
            RememberFeatured(snapshot);
        }
        catch (Exception exception)
        {
            _notice = exception.Message;
            Main.LogWarning($"Could not refresh rankings '{_board.ID}': {exception}");
        }
        ShowSnapshot(snapshot);
    }

    private void Enter(Page page)
    {
        HideMetricTooltip();
        HidePeakTooltip();
        _page = page;
        _drawing = true;
        _scroll.StopMovement();
        bool overview = page == Page.Overview;
        float width = TableWidth;
        float x = BoardX;
        bool table = page == Page.Table;
        float listHeight = overview ? 220 : table ? 201 : 188;
        float y = overview ? -16 : table ? -25.5f : -32;
        Place((RectTransform)_scroll.transform, BackgroundTransform, width + ScrollbarGutter, listHeight, x, y, centered: true);
        Place(_tools, BackgroundTransform, width, 22, x - ScrollbarGutter * 0.5f, 109, centered: true);
        Place(_caption, BackgroundTransform, width, 22, x - 4, 88, centered: true);
        Place(_header, BackgroundTransform, width, 18, x - ScrollbarGutter * 0.5f, 85, centered: true);
        _caption.gameObject.SetActive(page == Page.Details);
        _sidebarScroll.gameObject.SetActive(ShowSidebar);
        _sidebarRail.gameObject.SetActive(page == Page.Table);
        _sidebarTabs.gameObject.SetActive(page == Page.Table);
        _sidebarTools.gameObject.SetActive(ShowSidebar && _sidebar is Sidebar.Filters or Sidebar.Presets);
        if (!ShowSidebar) _filterActions.gameObject.SetActive(false);
        Clear(_tools);
        Clear(_caption);
        Clear(_header);
        if (_panel != null) { _panel.gameObject.SetActive(false); Destroy(_panel.gameObject); }
        _panel = Rect("RankingPanel", _content, width, 0, 0, 0);
        _lineIndex = 0;
        _list.gameObject.SetActive(page == Page.Table);
        _firstRow = -1;
        DrawNavigation();
        DrawToolbar();
        if (_caption.gameObject.activeSelf)
            Label(_caption, _notice ?? PageName(page), width, 15, new Color(0.86f, 0.85f, 0.76f)).fontSize = 9;
        switch (page)
        {
            case Page.Table:
                FitColumns(width);
                DrawTable();
                DrawSidebarHeader();
                if (ShowSidebar) DrawSidebar();
                break;
            case Page.Overview: DrawOverview(); break;
            case Page.Details: DrawDetails(); break;
            case Page.History: DrawHistory(); break;
            case Page.Record: DrawArchive(); break;
        }
        float contentHeight = page == Page.Table ? Math.Max(_rows.Count, 1) * RowHeight : _lineIndex * LineHeight();
        _content.sizeDelta = new Vector2(width, contentHeight);
        _list.sizeDelta = _content.sizeDelta;
        _content.anchoredPosition = new Vector2(0, Mathf.Clamp(page == Page.Table ? _view?.Scroll ?? 0f : 0,
            0, Mathf.Max(0f, contentHeight - _scroll.viewport.rect.height)));
        _drawing = false;
        if (page == Page.Table) ShowVisible();
        SyncDistribution();
    }

    private void DrawToolbar()
    {
        bool table = _page == Page.Table;
        _tools.GetComponent<HorizontalLayoutGroup>().enabled = !table;
        if (_page == Page.Overview)
        {
            Label(_tools, L("overview"), TableWidth - 32).fontSize = 13;
            Button(_tools, L("refresh_featured"), 22, RefreshFeatured, Icon("iconStatistics"), hint: L("refresh_featured"));
            return;
        }
        if (_page is Page.History or Page.Record)
        {
            DrawArchiveToolbar();
            return;
        }
        if (_board == null) return;
        var headings = table ? StackLine("RankingToolbarHeading", _tools, 0, TableWidth, 22) : _tools;
        var heading = Label(headings, Name(_board.Name), 165, 20, new Color(0.97f, 0.86f, 0.65f));
        heading.fontSize = 12;
        heading.resizeTextForBestFit = true;
        heading.resizeTextMinSize = 9;
        heading.resizeTextMaxSize = 12;
        if (!table)
        {
            Button(_tools, L("back"), 109, () => Go(Page.Table), Icon("iconArrowLeft"));
            return;
        }
        const float actionWidth = 22;
        const float jumpWidth = 46;
        const float endWidth = 22;
        const float toolbarGap = 3;
        float actionsWidth = actionWidth * 3 + toolbarGap * 3;
        float navigationWidth = jumpWidth + endWidth * 2 + toolbarGap * 3;
        var count = Label(headings, string.Format(L("result_count"), _rows.Count), 0, 20, new Color32(196, 199, 173, 255));
        count.name = "RankingResultCount";
        count.resizeTextForBestFit = false;
        count.fontSize = 8;
        float countWidth = Mathf.Ceil(count.preferredWidth) + 2;
        float actionsX = TableWidth - actionsWidth;
        float navigationMaxX = actionsX - toolbarGap - navigationWidth;
        float preferredTitleWidth = Mathf.Ceil(heading.preferredWidth) + 2;
        float titleWidth = Mathf.Max(1, Mathf.Min(preferredTitleWidth, navigationMaxX - toolbarGap - countWidth - 6));
        heading.resizeTextMinSize = 7;
        if (titleWidth < preferredTitleWidth)
        {
            heading.raycastTarget = true;
            AttachButtonHint(heading.gameObject, heading.text);
        }
        SetWidth(heading.transform, titleWidth);
        SetWidth(count.transform, countWidth);
        float navigationX = Mathf.Clamp((TableWidth - navigationWidth) * 0.5f, titleWidth + countWidth + 6 + toolbarGap, navigationMaxX);
        var navigation = StackLine("RankingToolbarNavigation", _tools, 0, navigationWidth, 22);
        navigation.anchoredPosition = new Vector2(navigationX, 0);
        DrawScrollControls(navigation, jumpWidth, endWidth);

        var actions = StackLine("RankingToolbarActions", _tools, 0, actionsWidth, 22);
        actions.anchoredPosition = new Vector2(actionsX, 0);
        var chart = Button(actions, L("chart"), actionWidth, ToggleDistribution, Icon("iconStatistics"), hint: L("chart"));
        if (_distributionOpen) chart.image.color = new Color(0.83f, 0.88f, 0.65f);
        Button(actions, L("refresh"), actionWidth, () => Refresh(reload: true),
            SpriteTextureLoader.getSprite("inmny/godtools/icons/iconRankingRefresh"), hint: L("refresh"));
        var record = Button(actions, L("capture_archive"), actionWidth, CaptureArchive, Icon("iconWorldLog"));
        AttachButtonHint(record.gameObject, L("capture_archive"), L("capture_archive_hint"));
        record.interactable = _view.Snapshot?.Primary != null;
    }

    private float TableScrollLimit => Mathf.Max(0f, _rows.Count * RowHeight - _scroll.viewport.rect.height);

    private void DrawScrollControls(RectTransform group, float jumpWidth, float endWidth)
    {
        AddEndButton("to_top", "iconRankingTop", () => ScrollTo(0));
        var input = Input(group, "", jumpWidth, _ => { });
        input.name = "RankingRankJumpInput";
        input.characterValidation = InputField.CharacterValidation.Integer;
        input.characterLimit = 10;
        input.interactable = _rows.Count > 0;
        input.textComponent.alignment = TextAnchor.MiddleCenter;
        AttachButtonHint(input.gameObject, L("jump_hint"));
        SetInputPlaceholder(input, L("jump_rank"), TextAnchor.MiddleCenter);
        input.onEndEdit.AddListener(value =>
        {
            if (_drawing || !input.isActiveAndEnabled || input.wasCanceled ||
                !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int rank) ||
                rank < 1 || rank > (_view.Snapshot?.RankedCount ?? 0)) return;
            int rowIndex = _rows.FindIndex(item => item.Rank >= rank);
            ScrollTo(rowIndex < 0 ? TableScrollLimit : rowIndex * RowHeight);
            input.SetTextWithoutNotify("");
        });
        AddEndButton("to_bottom", "iconRankingBottom", () => ScrollTo(TableScrollLimit));

        void AddEndButton(string label, string icon, Action action)
        {
            string title = L(label);
            Button(group, title, endWidth, action, SpriteTextureLoader.getSprite("inmny/godtools/icons/" + icon), hint: title)
                .interactable = TableScrollLimit > 0;
        }

        void ScrollTo(float offset)
        {
            _scroll.StopMovement();
            _view.Scroll = Mathf.Clamp(offset, 0f, TableScrollLimit);
            _content.anchoredPosition = new Vector2(0f, _view.Scroll);
            ShowVisible();
        }
    }

    private void DrawNavigation()
    {
        bool compact = _navigationCollapsed;
        float width = NavigationWidth;
        float navX = NavigationX;
        float offset = _navigationContent.anchoredPosition.y;
        Clear(_navigationHeader);
        Clear(_navigationContent);
        var headerLayout = _navigationHeader.GetComponent<HorizontalLayoutGroup>();
        headerLayout.enabled = !compact;
        float headerHeight = CompactStep + CompactInset;
        Place(_navigationHeader, BackgroundTransform, width, compact ? headerHeight : 22,
            compact ? PaneLeft + width * 0.5f : navX - 4, compact ? RailTop - headerHeight * 0.5f : 108, centered: true);
        string overview = L("overview");
        var overviewIcon = Icon("iconWorldInfo");
        if (!compact)
            NavigationButton(_navigationHeader, overview, width - 29, overviewIcon, () => Go(Page.Overview), _page == Page.Overview);
        string toggle = L(compact ? "expand_navigation" : "collapse_navigation");
        var arrow = Icon(compact ? "iconArrowMetaRight" : "iconArrowMetaLeft");
        void ToggleNavigation()
        {
            _navigationCollapsed = !_navigationCollapsed;
            Enter(_page);
        }
        var toggleButton = compact
            ? CompactButton(_navigationHeader, toggle, arrow, ToggleNavigation, -CompactInset)
            : Button(_navigationHeader, toggle, 22, ToggleNavigation, arrow, hint: toggle);
        toggleButton.name = "RankingNavigationToggle";
        float y = 0;
        if (compact) AddEntry(overview, overviewIcon, () => Go(Page.Overview), _page == Page.Overview);
        foreach (var group in RankingRegistry.AvailableBoardGroups())
        {
            if (compact) AddDivider();
            else
            {
                var title = StackLine("RankingGroup", _navigationContent, -y, width, 18);
                Label(title, Name(group.Name), width - 6, 17, new Color(0.80f, 0.79f, 0.64f)).fontSize = 9;
                y += 18;
            }
            foreach (var board in group.Boards)
                AddEntry(Name(board.Name), BoardIcon(board), () => Select(board), (_page is Page.Table or Page.Details) && _board == board);
        }
        if (compact) AddDivider();
        AddEntry(L("rank_history"), Icon("iconWorldLog"), () => Go(Page.History), _page is Page.History or Page.Record);
        float viewportHeight = compact ? Mathf.Min(y + CompactInset, RailHeight - headerHeight) : 222;
        Place((RectTransform)_navigation.transform, BackgroundTransform, width + ScrollbarGutter, viewportHeight, navX,
            compact ? RailTop - headerHeight - viewportHeight * 0.5f : -14, centered: true);
        if (compact)
        {
            float railWidth = width + (y > viewportHeight ? ScrollbarGutter : 0);
            LayoutRail(_navigationRail, railWidth, headerHeight + viewportHeight, PaneLeft + railWidth * 0.5f, compact: true);
        }
        else LayoutRail(_navigationRail, width + 11, RailHeight, navX);
        _navigationContent.sizeDelta = new Vector2(width, y);
        _navigationContent.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, y - viewportHeight)));

        void AddDivider()
        {
            var divider = Rect("RankingGroupDivider", _navigationContent, 16, 1, (width - 16) * 0.5f, -y - 2)
                .gameObject.AddComponent<Image>();
            divider.color = new Color32(134, 144, 113, 150);
            divider.raycastTarget = false;
            y += 6;
        }

        void AddEntry(string name, Sprite icon, Action click, bool selected)
        {
            if (compact)
            {
                var button = CompactButton(_navigationContent, name, icon ?? Icon("iconStatistics"), click, -y, selected);
                button.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
                y += CompactStep;
                return;
            }
            var line = StackLine("RankingNavigationRow", _navigationContent, -y, width, 22);
            NavigationButton(line, name, width - 5, icon, click, selected);
            y += 22;
        }

        void NavigationButton(Transform parent, string name, float buttonWidth, Sprite icon, Action click, bool selected)
        {
            var button = Button(parent, name, buttonWidth, click, icon ?? Icon("iconStatistics"), flat: !selected, hint: name);
            AlignCaption(button, 18);
            if (parent != _navigationHeader) button.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
            if (selected) button.image.color = new Color(0.83f, 0.88f, 0.65f);
        }
    }

    private static Button CompactButton(RectTransform parent, string title, Sprite icon, Action action, float y, bool selected = false)
    {
        var slot = Rect(title, parent, CompactButtonSize, CompactButtonSize, (CollapsedWidth - CompactButtonSize) * 0.5f, y);
        var background = slot.gameObject.AddComponent<Image>();
        background.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 1.6f;
        background.canvasRenderer.cullTransparentMesh = false;
        var button = slot.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.colors = new ColorBlock
        {
            normalColor = selected ? (Color)new Color32(95, 105, 74, 242) : Color.clear,
            highlightedColor = new Color32(106, 118, 84, 222),
            pressedColor = new Color32(74, 85, 61, 255),
            selectedColor = new Color32(106, 118, 84, 222),
            disabledColor = Color.clear,
            colorMultiplier = 1,
            fadeDuration = 0.1f
        };
        button.onClick.AddListener(() => action());
        IconImage(slot, CompactButtonSize * 0.5f, icon).rectTransform.sizeDelta = new Vector2(17, 17);
        AttachButtonHint(slot.gameObject, title);
        return button;
    }

    private static Sprite BoardIcon(RankingBoard board) =>
        string.IsNullOrEmpty(board.IconPath) ? null : SpriteTextureLoader.getSprite(board.IconPath);

    private static void AlignCaption(Button button, float inset)
    {
        var text = button.GetComponentInChildren<RawText>().Text;
        text.alignment = TextAnchor.MiddleLeft;
        text.rectTransform.offsetMin = new Vector2(inset, 0);
    }

    private void DrawTable()
    {
        if (_source == null) return;
        Column(L("name"), _identityWidth, TextAnchor.MiddleLeft);
        foreach (string column in _columns)
        {
            var field = _source.Fields[column];
            Column(FieldTitle(field), _metricWidth, TextAnchor.MiddleCenter, _view.PinnedColumns.Contains(column), FieldDescription(field));
        }
        if (_rows.Count == 0) Label(NextLine(), _notice ?? L("empty"), TableWidth);

        void Column(string title, float width, TextAnchor alignment, bool pinned = false, string description = null)
        {
            var color = pinned ? new Color32(236, 218, 167, 255) : new Color32(200, 203, 181, 255);
            var label = Label(_header, title, width, 16, color, alignment);
            label.fontSize = 9;
            label.resizeTextMinSize = 7;
            label.resizeTextMaxSize = 9;
            if (description != null)
            {
                label.raycastTarget = true;
                AttachButtonHint(label.gameObject, title, description);
            }
        }
    }

    private void ShowVisible()
    {
        int first = Mathf.Clamp(Mathf.FloorToInt(_content.anchoredPosition.y / RowHeight), 0, Math.Max(0, _rows.Count - 1));
        if (first == _firstRow) return;
        HideMetricTooltip();
        HidePeakTooltip();
        _firstRow = first;
        int length = Mathf.Min(Mathf.CeilToInt(_scroll.viewport.rect.height / RowHeight) + 2, _rows.Count - first);
        while (_pool.Count < length) _pool.Add(NewLine());
        for (int i = 0; i < _pool.Count; i++)
        {
            var line = _pool[i];
            line.Root.gameObject.SetActive(i < length);
            if (i >= length) continue;
            var entry = line.Entry = _rows[first + i];
            line.Root.anchoredPosition = new Vector2(0, -(first + i) * RowHeight);
            FitLine(line);
            line.Rank.text = RankLabel(entry.Rank);
            line.Name.text = entry.Name;
            var actor = _source.ActorPortrait?.Invoke(entry.Key);
            var meta = _source.MetaPortrait?.Invoke(entry.Key);
            line.Name.color = actor?.kingdom?.getColor()?.getColorText() ?? Color.white;
            line.Inspect.gameObject.name = entry.Name;
            line.Inspect.interactable = _source.Inspect != null && (_source.CanInspect?.Invoke(entry.Key) ?? true);
            line.Rank.color = entry.Rank is >= 1 and <= 3 ? new Color(0.94f, 0.79f, 0.50f) : new Color(0.75f, 0.76f, 0.69f);
            if (_view.RowLayout.ShowPortrait) BindPortrait(line, entry, actor, meta);
            for (int j = 0; j < _columns.Length; j++)
            {
                var field = _source.Fields[_columns[j]];
                var value = entry.Values.GetValueOrDefault(field.ID);
                var metric = line.Metrics[j];
                BindMetric(metric, value, field.ValueLabel(value));
                metric.Title = $"{entry.Name} · {FieldTitle(field)}";
            }
            line.Locate.gameObject.SetActive(_source.Locate != null);
            line.Locate.interactable = _source.Locate?.Invoke(entry.Key) != null;
            line.Creature = entry.Peak != null ? entry.Peak.Creature : _source.SavedCreature?.Invoke(entry.Key);
            line.Place.gameObject.SetActive(_source.Place != null);
            line.Place.interactable = _source.SavedCreature == null || CreatureSavedList.CanPlace(line.Creature);
            line.PlaceInVillage.gameObject.SetActive(_source.SavedCreature != null);
            line.PlaceInVillage.interactable = CreatureSavedList.CanPlaceInVillage(line.Creature);
        }
    }

    private void FitLine(Line line)
    {
        SetWidth(line.Root, TableWidth);
        SetWidth(line.Inspect.transform, _identityWidth);
        line.Portrait.gameObject.SetActive(_view.RowLayout.ShowPortrait);
        line.Rank.gameObject.SetActive(_view.RowLayout.ShowRank);
        float inset = _view.RowLayout.ShowPortrait ? PortraitNameInset : 6f;
        line.Name.rectTransform.offsetMin = new Vector2(inset, _view.RowLayout.ShowRank ? 13 : 0);
        line.Rank.rectTransform.offsetMin = new Vector2(inset, 2);
        float actionWidth = ActionWidth(TableWidth);
        SetWidth(line.Locate.transform, actionWidth);
        SetWidth(line.Place.transform, actionWidth);
        SetWidth(line.PlaceInVillage.transform, actionWidth);
        while (line.Metrics.Count < _columns.Length)
        {
            var button = Button(line.Root, "", _metricWidth, null, flat: true);
            button.name = "RankingMetric";
            button.transform.SetSiblingIndex(line.Locate.transform.GetSiblingIndex());
            var metric = new Metric { Details = button, Value = button.GetComponentInChildren<RawText>().Text };
            AddMetricTooltip(button, () => metric.Title, () => metric.Reading);
            line.Metrics.Add(metric);
        }
        for (int i = 0; i < line.Metrics.Count; i++)
        {
            var button = line.Metrics[i].Details;
            button.gameObject.SetActive(i < _columns.Length);
            if (i < _columns.Length) SetWidth(button.transform, _metricWidth);
        }
    }

    private static void BindMetric(Metric metric, RankingValue value, string label)
    {
        var parent = metric.Details.transform;
        Sprite sprite = string.IsNullOrEmpty(value?.CellIconPath) ? null : SpriteTextureLoader.getSprite(value.CellIconPath);
        if (sprite != null && metric.Icon == null) metric.Icon = IconImage(parent, 8, sprite);
        if (metric.Icon != null)
        {
            metric.Icon.sprite = sprite;
            metric.Icon.enabled = sprite != null;
        }

        if (value?.ProgressRatio is double gauge)
        {
            if (metric.Meter == null)
            {
                metric.Meter = Rect("RankingMetricMeter", parent, 0, 3, 0, 0);
                metric.Meter.SetAsFirstSibling();
                metric.Meter.anchorMin = Vector2.zero;
                metric.Meter.anchorMax = Vector2.right;
                metric.Meter.offsetMin = new Vector2(4, 2);
                metric.Meter.offsetMax = new Vector2(-4, 5);
                var track = metric.Meter.gameObject.AddComponent<Image>();
                track.color = new Color32(25, 30, 23, 255);
                track.raycastTarget = false;
                var fill = Rect("RankingMetricMeterFill", metric.Meter, 0, 0, 0, 0);
                fill.anchorMin = Vector2.zero;
                fill.anchorMax = Vector2.one;
                fill.offsetMin = fill.offsetMax = Vector2.zero;
                metric.Fill = fill.gameObject.AddComponent<Image>();
                metric.Fill.raycastTarget = false;
            }
            metric.Meter.gameObject.SetActive(true);
            metric.Fill.rectTransform.anchorMax = new Vector2((float)gauge, 1);
            metric.Fill.color = Color.Lerp(new Color32(203, 86, 68, 255), new Color32(148, 186, 106, 255), (float)gauge);
        }
        else if (metric.Meter != null) metric.Meter.gameObject.SetActive(false);

        metric.Value.rectTransform.offsetMin = new Vector2(sprite != null ? 16 : 2, value?.ProgressRatio != null ? 6 : 0);
        metric.Value.text = label;
        metric.Reading = value;
        metric.Details.image.raycastTarget = HasMetricDetails(value);
    }

    private static void SetWidth(Transform rect, float width) =>
        ((RectTransform)rect).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

    private Line NewLine()
    {
        var line = new Line { Root = StackLine("RankingRow", _list, 0, Width, RowHeight - 2) };
        var background = line.Root.gameObject.AddComponent<Image>();
        background.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        background.type = Image.Type.Sliced;
        background.color = new Color(0.17f, 0.20f, 0.17f, 0.67f);
        background.raycastTarget = false;
        line.Inspect = Button(line.Root, "", MinIdentityWidth, () => _source.Inspect?.Invoke(line.Entry.Key), flat: true);
        ((RectTransform)line.Inspect.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 35);
        line.Name = line.Inspect.GetComponentInChildren<RawText>().Text;
        line.Name.alignment = TextAnchor.MiddleLeft;
        line.Name.rectTransform.offsetMin = new Vector2(PortraitNameInset, 13);
        line.Rank = Label(line.Inspect.transform, "", 0, 12);
        line.Rank.name = "RankingEntryRank";
        line.Rank.resizeTextForBestFit = false;
        line.Rank.fontSize = 8;
        line.Rank.rectTransform.anchorMin = Vector2.zero;
        line.Rank.rectTransform.anchorMax = Vector2.right;
        line.Rank.rectTransform.offsetMin = new Vector2(PortraitNameInset, 2);
        line.Rank.rectTransform.offsetMax = new Vector2(-3, 15);
        line.Portrait = Rect("RankingPortrait", line.Inspect.transform, 37, 33, 0, 0);
        line.Portrait.anchorMin = line.Portrait.anchorMax = new Vector2(0, 0.5f);
        line.Portrait.pivot = new Vector2(0.5f, 0.5f);
        line.Portrait.anchoredPosition = new Vector2(21, 0);
        line.IconFrame = line.Portrait.gameObject.AddComponent<Image>();
        line.IconFrame.sprite = SpriteTextureLoader.getSprite("ui/special/windowAvatarElement");
        line.IconFrame.raycastTarget = false;
        line.Icon = IconImage(line.Portrait, 18.5f, null);
        var tip = line.Inspect.gameObject.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(() =>
        {
            if (line.Entry.Peak != null)
                ShowPeakTooltip(tip.gameObject, line.Entry);
            else if (line.Creature != null)
                ShowSavedCreatureTooltip(tip.gameObject, line.Creature, line.Entry.Icon, line.Entry.Name);
            else if (_source.ActorPortrait?.Invoke(line.Entry.Key) is Actor actor && actor.isAlive())
                actor.showTooltip(tip.gameObject);
            else if (line.Portrait.gameObject.activeSelf && line.Banner?.gameObject.activeSelf == true && _source.MetaPortrait?.Invoke(line.Entry.Key) != null)
                line.Banner.showTooltip();
            else
                Tooltip.show(tip.gameObject, "normal", new TooltipData { tip_name = line.Entry.Name });
        }, false);
        line.Inspect.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
        line.Locate = Button(line.Root, "⌖", 22, () =>
        {
            if (_source.Locate?.Invoke(line.Entry.Key) is WorldTile tile)
                World.world.locatePosition(new Vector3(tile.x, tile.y, 0));
        }, Icon("iconArrowAttackTarget"), hint: L("locate"));
        line.Place = Button(line.Root, "+", 22, () => _source.Place?.Invoke(line.Entry.Key),
            Icon("iconShovelPlus"));
        var placeTip = line.Place.gameObject.AddComponent<TipButton>();
        placeTip.showOnClick = false;
        placeTip.setHoverAction(() => Tooltip.show(placeTip.gameObject, "normal",
            new TooltipData
            {
                tip_name = !line.Place.interactable ? CreatureStatus(line.Creature) :
                    line.Creature != null ? LM.Get("inmny.godtools.place_saved_actor") : L("place")
            }), false);
        line.PlaceInVillage = Button(line.Root, "RankingPlaceInVillage", 22,
            () => WindowCreatureSavedList.PlaceSnapshot(line.Creature, inVillage: true), Icon("iconCitySelect"),
            hint: LM.Get("inmny.godtools.place_saved_actor_in_village"));
        return line;
    }

    private void BindPortrait(Line line, RankingRow entry, Actor actor, NanoObject meta)
    {
        bool saved = entry.SavedPortrait != null;
        bool showActor = !saved && actor?.isAlive() == true && _avatarPrefab != null;
        if (showActor && line.Avatar == null)
        {
            line.Avatar = Instantiate(_avatarPrefab, line.Portrait, false);
            line.AvatarFrame = line.Avatar.GetComponent<Image>();
            line.Avatar.unit_type_bg.enabled = false;
            var rect = (RectTransform)line.Avatar.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localScale = new Vector3(ActorPortraitScale, ActorPortraitScale, 1);
            var group = rect.GetComponent<CanvasGroup>() ?? rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            if (rect.TryGetComponent<TipButton>(out var avatarTip)) avatarTip.enabled = false;
        }
        line.Avatar?.show(showActor ? actor : null);
        if (showActor)
        {
            line.AvatarFrame.overrideSprite = entry.Rank is >= 1 and <= 3 ? _podiumFrames[entry.Rank - 1] : null;
            FrameActor(line.Avatar.avatarLoader);
        }
        bool showBanner = BindBanner(line, showActor ? null : meta);
        line.IconFrame.enabled = !showActor && !showBanner;
        line.IconFrame.overrideSprite = saved && entry.Rank is >= 1 and <= 3 ? _podiumFrames[entry.Rank - 1] : null;
        line.Icon.sprite = entry.SavedPortrait ?? entry.Icon;
        line.Icon.rectTransform.sizeDelta = saved ? new Vector2(23, 21) : new Vector2(30, 29);
        line.Icon.enabled = !showActor && !showBanner && line.Icon.sprite != null;
    }

    private static bool BindBanner(Line line, NanoObject item)
    {
        var asset = BannerAsset(item);
        if (asset == null)
        {
            line.Banner?.gameObject.SetActive(false);
            return false;
        }
        if (line.Banner?.meta_asset != asset)
        {
            if (line.Banner != null)
            {
                line.Banner.gameObject.SetActive(false);
                Destroy(line.Banner.gameObject);
            }
            line.Banner = CreateBanner(asset, item, line.Portrait, 34, 32);
        }
        else
        {
            line.Banner.gameObject.SetActive(true);
            line.Banner.load(item);
        }
        return true;
    }

    private static MetaCustomizationAsset BannerAsset(NanoObject item) => item switch
    {
        null or City { kingdom: null } => null,
        Army army when army.getKingdom() == null => null,
        War war when war.getAsset() == null => null,
        Plot plot when plot.getAsset() == null => null,
        _ => AssetManager.meta_customization_library.get(item.getType()) is { get_banner: not null } asset ? asset : null
    };

    private static IBanner CreateBanner(MetaCustomizationAsset asset, NanoObject item, Transform parent, float width, float height)
    {
        var banner = asset.get_banner(asset, item, parent);
        var rect = (RectTransform)banner.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one * Mathf.Min(0.58f, width / Mathf.Max(1, rect.rect.width), height / Mathf.Max(1, rect.rect.height));
        var group = banner.gameObject.GetComponent<CanvasGroup>() ?? banner.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        foreach (var button in banner.gameObject.GetComponentsInChildren<Button>(true))
            button.enabled = false;
        foreach (var tip in banner.gameObject.GetComponentsInChildren<TipButton>(true))
            tip.enabled = false;
        foreach (var label in banner.gameObject.GetComponentsInChildren<Text>(true))
            label.enabled = false;
        return banner;
    }

    private static void FrameActor(UnitAvatarLoader avatar)
    {
        var rect = (RectTransform)avatar.transform;
        var frame = (RectTransform)rect.parent;
        rect.anchoredPosition = Vector2.zero;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(frame, avatar._actor_image.rectTransform);
        if (avatar._item_image.enabled && avatar._item_image.sprite != null)
            bounds.Encapsulate(RectTransformUtility.CalculateRelativeRectTransformBounds(frame, avatar._item_image.rectTransform));
        float scale = Mathf.Min(1f, (frame.rect.width - 3) / Mathf.Max(1f, bounds.size.x),
            (frame.rect.height - 3) / Mathf.Max(1f, bounds.size.y));
        rect.localScale *= scale;
        rect.anchoredPosition = -(Vector2)bounds.center * scale;
    }

    private void DrawDetails()
    {
        Label(NextLine(), _detailTitle, Width, 20);
        if (!string.IsNullOrEmpty(_detailDescription)) Label(NextLine(), _detailDescription, Width);
    }

    public static void ShowEntryDescription(string title, string description) =>
        Instance.ShowDescription(title, description);

    private void ShowDescription(string title, string description)
    {
        _detailTitle = title;
        _detailDescription = description;
        Go(Page.Details);
    }

    private void Go(Page page)
    {
        _notice = null;
        if (page == Page.Overview) EnsureFeatured();
        Enter(page);
    }
    private string PageName(Page page) => L(page.ToString().ToLowerInvariant());
    private static string FieldTitle(RankingRegistry.Field field) => string.IsNullOrWhiteSpace(field.Unit)
        ? Name(field.Name) : $"{Name(field.Name)} ({Name(field.Unit)})";

    private static string FieldDescription(RankingRegistry.Field field) =>
        string.IsNullOrWhiteSpace(field.Description) ? null : Name(field.Description);

    private static string Name(string key) => BuiltInRankings.Label(key, key);
    private static string L(string key) => LM.Get("inmny.godtools.ranking." + key);
    private static Sprite Icon(string id) => SpriteTextureLoader.getSprite("ui/icons/" + id);
    private float LineHeight() => _page == Page.Overview ? OverviewLineHeight : ControlHeight;
    private RectTransform NextLine() => StackLine("RankingControls", _panel, -_lineIndex++ * LineHeight(), height: LineHeight());

    private RectTransform Band(string name, float y, float width, float x)
    {
        var band = StackLine(name, BackgroundTransform, y, width);
        Place(band, BackgroundTransform, width, 22, x, y, centered: true);
        return band;
    }

    private static RectTransform StackLine(string name, Transform parent, float y, float width = Width, float height = ControlHeight)
    {
        var rect = Rect(name, parent, width, height, 0, y);
        var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = RowGap;
        group.padding = new RectOffset((int)RowGap, 0, 0, 0);
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = group.childControlHeight = group.childForceExpandWidth = group.childForceExpandHeight = false;
        return rect;
    }

    private static RectTransform Rect(string name, Transform parent, float width, float height, float x, float y)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Place(rect, parent, width, height, x, y);
        return rect;
    }

    private static void Place(RectTransform rect, Transform parent, float width, float height, float x, float y, bool centered = false)
    {
        if (rect.parent != parent) rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    private static Text Label(Transform parent, string value, float width, float height = 20, Color color = default,
        TextAnchor align = TextAnchor.MiddleLeft)
    {
        var label = RawText.Instantiate(parent);
        label.Setup(value, color == default ? Color.white : color, align);
        label.SetSize(new Vector2(width, height));
        label.Text.fontSize = 10;
        label.Text.supportRichText = false;
        label.Text.raycastTarget = false;
        label.Text.verticalOverflow = VerticalWrapMode.Truncate;
        label.Text.horizontalOverflow = HorizontalWrapMode.Wrap;
        return label.Text;
    }

    private static Button Button(Transform parent, string title, float width, Action action, Sprite icon = null,
        bool flat = false, string hint = null)
    {
        var rect = Rect(title, parent, width, 20, 0, 0);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite("ui/special/special_buttonGray_insides");
        image.type = Image.Type.Sliced;
        image.color = flat ? new Color(1, 1, 1, 0.055f) : Color.white;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        if (action != null) button.onClick.AddListener(() => action());
        var text = Label(rect, width < 27 && icon != null ? "" : title, width, align: TextAnchor.MiddleCenter);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(icon != null && width >= 27 ? 16 : 2, 0);
        text.rectTransform.offsetMax = new Vector2(-2, 0);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 7;
        text.resizeTextMaxSize = 10;
        if (icon != null) IconImage(rect, width < 27 ? width * 0.5f : 9, icon);
        if (hint != null) AttachButtonHint(rect.gameObject, hint);
        return button;
    }

    private static void AttachButtonHint(GameObject target, string hint, string description = null)
    {
        var tip = target.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(() => Tooltip.show(tip.gameObject, "normal",
            new TooltipData { tip_name = hint, tip_description = description }), false);
    }

    private static Image IconImage(Transform parent, float x, Sprite sprite)
    {
        var rect = Rect("Icon", parent, 13, 13, 0, 0);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.sprite = sprite;
        return image;
    }

    private static InputField Input(Transform parent, string text, float width, Action<string> change)
    {
        var input = TextInput.Instantiate(parent);
        input.Setup(text, _ => { });
        input.SetSize(new Vector2(width, 20));
        input.input.onValueChanged.AddListener(value => change(value));
        return input.input;
    }

    private static void SetInputPlaceholder(InputField input, string title, TextAnchor alignment)
    {
        var placeholder = Label(input.transform, title, 0, 19, new Color32(188, 194, 161, 255), alignment);
        placeholder.rectTransform.anchorMin = Vector2.zero;
        placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = new Vector2(2, 0);
        placeholder.rectTransform.offsetMax = new Vector2(-14, 0);
        placeholder.resizeTextMaxSize = 8;
        input.placeholder = placeholder;
    }

    private static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }
}
