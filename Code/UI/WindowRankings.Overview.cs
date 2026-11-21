using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features.Rankings;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI;

public partial class WindowRankings
{
    private const int ArchivePageSize = 16;
    private static readonly Color HeadingTint = new Color32(245, 219, 169, 255);
    private static readonly Color MutedTint = new Color32(192, 197, 170, 255);
    private readonly Dictionary<string, RankingSnapshot> _featured = new(StringComparer.Ordinal);
    private RankingArchive _archive;
    private RankingArchive _compare;
    private bool _chooseComparison;
    private int _archivePage;

    private void RememberFeatured(RankingSnapshot snapshot)
    {
        if (snapshot == null) return;
        foreach (var preset in RankingStore.Presets)
            if (preset.Favorite && RankingSnapshot.SameSettings(preset, snapshot.View, includeColumns: true))
                StoreFeatured(preset, snapshot);
    }

    private void StoreFeatured(RankingPreset preset, RankingSnapshot snapshot)
    {
        _featured[preset.ID] = snapshot;
        RankingStore.Previews[preset.ID] = RankingPreview.Capture(snapshot);
    }

    private void ForgetFeatured(RankingPreset preset)
    {
        _featured.Remove(preset.ID);
        RankingStore.Previews.Remove(preset.ID);
    }

    private void EnsureFeatured(bool reload = false, RankingPreset only = null)
    {
        if (_map == null) return;
        var views = RankingStore.Presets.Where(preset => preset.Favorite && (only == null || preset == only) &&
                (reload || (RankingStore.Previews.GetValueOrDefault(preset.ID)?.Covers(preset.PreviewCount) != true &&
                            FeaturedSnapshot(preset) == null)))
            .Select(preset => (Preset: preset, Board: RankingRegistry.GetBoard(preset.BoardID)))
            .Where(item => item.Board != null && (item.Board.Available?.Invoke() ?? true))
            .GroupBy(item => item.Board.SourceID);

        foreach (var group in views)
        {
            var source = RankingRegistry.GetSource(group.Key);
            if (source == null) continue;
            var fields = group.SelectMany(item => SnapshotFields(item.Board, source, item.Preset)).Distinct().ToArray();
            try
            {
                var previous = reload ? null : _states.Values.Select(state => state.Snapshot?.Data)
                    .Concat(_featured.Values.Select(snapshot => snapshot.Data))
                    .Where(data => data?.Covers(source, fields) == true)
                    .OrderByDescending(data => data.RefreshedAt).FirstOrDefault();
                var data = previous ?? RankingQuery.Capture(source, fields);
                foreach (var (preset, board) in group)
                {
                    try { StoreFeatured(preset, CreateSnapshot(board, RankingStore.Copy(preset), data)); }
                    catch (Exception exception) { Main.LogWarning($"Could not prepare featured rankings '{preset.Name}': {exception.Message}"); }
                }
            }
            catch (Exception exception) { Main.LogWarning($"Could not refresh featured source '{group.Key}': {exception.Message}"); }
        }
    }

    private RankingSnapshot FeaturedSnapshot(RankingPreset preset)
    {
        var snapshot = _featured.GetValueOrDefault(preset.ID);
        var board = RankingRegistry.GetBoard(preset.BoardID);
        var source = board == null ? null : RankingRegistry.GetSource(board.SourceID);
        return source != null && RankingSnapshot.SameSettings(preset, snapshot?.View, includeColumns: true) &&
            snapshot.Data.Covers(source, SnapshotFields(board, source, preset)) ? snapshot : null;
    }

    private void RefreshFeatured()
    {
        EnsureFeatured(reload: true);
        Enter(Page.Overview);
    }

    private void DrawOverview()
    {
        var favorites = RankingStore.Presets.Where(preset => preset.Favorite).ToArray();
        if (favorites.Length == 0) SummaryText(NextLine(), L("feature_empty"), TableWidth - 6, MutedTint);
        else
        {
            const float gap = 6;
            float width = (TableWidth - 13) / 2;
            float left = 0, right = 0;
            foreach (var preset in favorites)
            {
                if (left <= right) left += DrawFeaturedCard(preset, 4, -left, width) + gap;
                else right += DrawFeaturedCard(preset, width + 9, -right, width) + gap;
            }
            _lineIndex = Mathf.CeilToInt(Mathf.Max(left, right) / LineHeight());
        }

        var history = NextLine();
        SummaryText(history, L("recent_archives"), TableWidth - 106, HeadingTint, 11);
        Button(history, L("rank_history"), 92, () => Go(Page.History), Icon("iconWorldLog"));
        DrawArchiveList(RankingStore.Archives.Take(4), L("archive_empty"), OpenArchive);
    }

    private float DrawFeaturedCard(RankingPreset preset, float x, float y, float width)
    {
        const float headingHeight = 24;
        const float rowHeight = 17;
        const float footerHeight = 19;
        int limit = preset.PreviewCount;
        var board = RankingRegistry.GetBoard(preset.BoardID);
        var source = board == null ? null : RankingRegistry.GetSource(board.SourceID);
        bool available = source != null && (board.Available?.Invoke() ?? true);
        var snapshot = available ? FeaturedSnapshot(preset) : null;
        var preview = RankingStore.Previews.GetValueOrDefault(preset.ID);
        int leaders = Math.Min(limit, preview?.Leaders.Count ?? 0);
        int rowsShown = Math.Max(1, leaders);
        float height = headingHeight + rowsShown * rowHeight + (preview == null ? 3 : footerHeight);

        var card = Rect(preset.Name, _panel, width, height, x, y);
        PaintSidebarPanel(card);
        var image = card.GetComponent<Image>();
        image.raycastTarget = true;
        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
        button.interactable = available;
        if (available)
            button.onClick.AddListener(() =>
            {
                BindBoard(board);
                UsePreset(preset, snapshot);
            });

        var heading = CardLine("RankingFeaturedHeader", 2, 20);
        CardText(heading, preset.Name, width - 92, HeadingTint, 11);
        var up = Button(heading, "↑", 16, () => ChangeFeaturedCount(preset, limit + 1), hint: L("feature_more"));
        up.name = "RankingFeaturedMore";
        up.interactable = limit < RankingPreview.MaxLeaderCount;
        var count = Input(heading, limit.ToString(CultureInfo.InvariantCulture), 24, _ => { });
        count.name = "RankingFeaturedCount";
        count.characterValidation = InputField.CharacterValidation.Integer;
        count.characterLimit = 4;
        count.textComponent.alignment = TextAnchor.MiddleCenter;
        var textArea = count.textComponent.rectTransform;
        textArea.anchorMin = Vector2.zero;
        textArea.anchorMax = Vector2.one;
        textArea.offsetMin = new Vector2(2, 1);
        textArea.offsetMax = new Vector2(-2, -1);
        count.transform.parent.Find("Icon")?.gameObject.SetActive(false);
        count.onEndEdit.AddListener(text =>
        {
            if (_drawing || !count.isActiveAndEnabled) return;
            if (count.wasCanceled || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                value = limit;
            count.SetTextWithoutNotify(RankingPreview.ClampCount(value).ToString(CultureInfo.InvariantCulture));
            ChangeFeaturedCount(preset, value);
        });
        var down = Button(heading, "↓", 16, () => ChangeFeaturedCount(preset, limit - 1), hint: L("feature_fewer"));
        down.name = "RankingFeaturedFewer";
        down.interactable = limit > 1;
        var detail = BareIconButton(heading, L("feature_details"), Icon("iconMainInfo"), 17, 13, () => { });
        detail.name = "RankingFeaturedDetails";
        AddPresetTooltip(detail, source, () => preset.Name, () => preset.Sort, () => preset.Conditions,
            () => preset.DisplayConditions, () => preset.PinnedColumns, showOnPress: true);

        string primary = preset.Sort.FirstOrDefault()?.Field;
        var field = primary != null ? source?.Fields.GetValueOrDefault(primary) : null;
        for (int i = 0; i < leaders; i++)
        {
            var row = preview.Leaders[i];
            var line = CardLine("RankingFeaturedLeader", headingHeight + i * rowHeight, rowHeight);
            var value = primary == null ? null : row.Values.GetValueOrDefault(primary);
            CardText(line, $"{RankLabel(row.Rank)}  {row.Name}", width - 68, HeadingTint);
            var score = CardText(line, field?.ValueLabel(value) ?? value?.Label ?? "—", 60, MutedTint);
            score.alignment = TextAnchor.MiddleRight;
        }
        if (leaders == 0)
            CardText(CardLine("RankingFeaturedNotice", headingHeight, rowHeight),
                L(!available ? "feature_unavailable" : preview == null ? "feature_waiting" : "feature_no_ranked"),
                width - 8, MutedTint);
        if (preview != null && (available || leaders > 0))
            CardText(CardLine("RankingFeaturedTimestamp", headingHeight + rowsShown * rowHeight, footerHeight),
                available ? string.Format(L("feature_timestamp"), SampleTime(preview.RefreshedAt)) : L("feature_unavailable"),
                width - 8, MutedTint, 8);
        return height;

        RectTransform CardLine(string name, float offset, float lineHeight) =>
            StackLine(name, card, -offset, width, lineHeight);
    }

    private void ChangeFeaturedCount(RankingPreset preset, int requested)
    {
        int count = RankingPreview.ClampCount(requested);
        if (count == preset.PreviewCount) return;
        preset.PreviewCount = count;
        if (RankingStore.Previews.GetValueOrDefault(preset.ID)?.Covers(count) != true)
        {
            var snapshot = FeaturedSnapshot(preset);
            if (snapshot != null) StoreFeatured(preset, snapshot);
            else EnsureFeatured(only: preset);
        }

        float offset = _content.anchoredPosition.y;
        Tooltip.hideTooltipNow();
        _scroll.StopMovement();
        Enter(Page.Overview);
        _content.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0,
            Mathf.Max(0, _content.rect.height - _scroll.viewport.rect.height)));
    }

    private static Text CardText(Transform parent, string text, float width, Color color, int font = 9)
    {
        var label = SummaryText(parent, text, width, color, font);
        label.resizeTextForBestFit = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var settings = label.GetGenerationSettings(Vector2.zero);
        var generator = label.cachedTextGeneratorForLayout;
        if (Fits(text)) return label;
        int low = 0, high = text.Length;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (Fits(text.Substring(0, mid) + "…")) low = mid;
            else high = mid;
        }
        if (low > 0 && char.IsLowSurrogate(text[low])) low--;
        label.text = text.Substring(0, low) + "…";
        return label;

        bool Fits(string candidate) => generator.GetPreferredWidth(candidate, settings) / label.pixelsPerUnit <= width;
    }

    private static Text SummaryText(Transform parent, string text, float width, Color color = default, int font = 9)
    {
        var label = Label(parent, text, Mathf.Max(1, width), 20, color);
        label.fontSize = font;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 7;
        label.resizeTextMaxSize = font;
        return label;
    }

    private static string RankLabel(int rank) => rank > 0 ? $"#{rank}" : L("unranked");
    private static string SampleTime(DateTime at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    private static string RecordTime(RankingArchive archive) => SampleTime(archive.SavedAt ?? archive.RefreshedAt);
    private static string ArchiveName(RankingArchive archive) =>
        archive.Name ?? archive.View.Name ?? Name(archive.BoardName ?? archive.View.BoardID);

    private void CaptureArchive()
    {
        var snapshot = _view.Snapshot;
        if (snapshot?.Primary == null) return;
        var archive = RankingArchive.Capture(snapshot, _board.Name, snapshot.Data.Fields[snapshot.Primary].Name,
            SnapshotFields(_board, _source, snapshot.View));
        archive.View.Name = CurrentView().Name;
        RankingStore.Archives.Insert(0, archive);
        OpenArchive(archive);
    }

    private void OpenArchive(RankingArchive archive)
    {
        _archive = archive;
        _compare = null;
        _chooseComparison = false;
        _archivePage = 0;
        Go(Page.Record);
    }

    private void DrawArchiveToolbar()
    {
        if (_page == Page.History)
        {
            SummaryText(_tools, L("rank_history"), TableWidth - 94, HeadingTint, 12);
            Button(_tools, L("overview"), 83, () => Go(Page.Overview), Icon("iconWorldInfo"));
            return;
        }
        SummaryText(_tools, _archive == null ? L("rank_history") : ArchiveName(_archive), TableWidth - 106, HeadingTint, 12);
        Button(_tools, L("back_history"), 95, () => Go(Page.History), Icon("iconArrowLeft"));
    }

    private void DrawHistory() => DrawArchiveList(RankingStore.Archives, L("archive_empty"), OpenArchive, allowRename: true);

    private void DrawArchiveList(IEnumerable<RankingArchive> archives, string empty, Action<RankingArchive> select,
        bool allowRename = false)
    {
        const float timeWidth = 106;
        const float countWidth = 63;
        const float actionWidth = 22;
        float nameWidth = TableWidth - timeWidth - countWidth - 12 - (allowRename ? actionWidth + RowGap : 0);
        bool found = false;
        Action closeEditor = null;
        foreach (var archive in archives)
        {
            found = true;
            var line = NextLine();
            string board = Name(archive.BoardName ?? archive.View.BoardID);
            string time = RecordTime(archive);
            string count = string.Format(L("archive_places"), archive.Rows.Count(row => row.Rank > 0));
            ShowRow();

            void ShowRow()
            {
                string name = ArchiveName(archive);
                string caption = name == board ? name : $"{board} · {name}";
                var button = Button(line, caption, nameWidth, () => select(archive), Icon("iconWorldLog"), hint: caption);
                AlignCaption(button, 20);
                button.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
                Metadata();
                if (allowRename) ActionButton("RankingArchiveRename", "archive_rename", "iconEdit", BeginRename);
            }

            void Metadata()
            {
                SummaryText(line, time, timeWidth, MutedTint);
                SummaryText(line, count, countWidth, MutedTint);
            }

            Button ActionButton(string id, string hint, string icon, Action action)
            {
                var button = Button(line, L(hint), actionWidth, action, Icon(icon), hint: L(hint));
                button.name = id;
                return button;
            }

            void BeginRename()
            {
                closeEditor?.Invoke();
                Tooltip.hideTooltipNow();
                Clear(line);
                closeEditor = Finish;
                var input = Input(line, ArchiveName(archive), nameWidth - 2 * (actionWidth + RowGap), _ => { });
                input.name = "RankingArchiveRenameInput";
                var confirm = ActionButton("RankingArchiveConfirmRename", "archive_confirm_rename", "iconOn", Rename);
                ActionButton("RankingArchiveCancelRename", "archive_cancel_rename", "iconOff", Finish);
                Metadata();
                confirm.interactable = !string.IsNullOrWhiteSpace(input.text);
                input.onValueChanged.AddListener(value => confirm.interactable = !string.IsNullOrWhiteSpace(value));
                input.onEndEdit.AddListener(_ =>
                {
                    if (!input.isActiveAndEnabled) return;
                    if (input.wasCanceled) Finish();
                    else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
                        Rename();
                });
                input.ActivateInputField();

                void Rename()
                {
                    string name = input.text.Trim();
                    if (name.Length == 0) return;
                    archive.Name = name;
                    Finish();
                }

                void Finish()
                {
                    closeEditor = null;
                    Tooltip.hideTooltipNow();
                    Clear(line);
                    ShowRow();
                }
            }
        }
        if (!found) SummaryText(NextLine(), empty, TableWidth - 6, MutedTint);
    }

    private void DrawArchive()
    {
        if (_archive == null) return;
        var primary = _archive.View.Sort.FirstOrDefault()?.Field;
        string metricName = _archive.View.BoardID == BuiltInRankings.Actors && primary == StandardDuelPower.BaseFieldID
            ? L("power.legacy") : Name(_archive.PrimaryName);
        var controls = NextLine();
        string subtitle = $"{metricName}   ·   {RecordTime(_archive)}";
        SummaryText(controls, subtitle, TableWidth - 123, HeadingTint, 10);
        if (_chooseComparison || _compare != null)
            Button(controls, L("archive_close_compare"), 111, () =>
            {
                _chooseComparison = false;
                _compare = null;
                _archivePage = 0;
                Enter(Page.Record);
            });
        else
            Button(controls, L("archive_compare"), 111, () =>
            {
                _chooseComparison = true;
                Enter(Page.Record);
            });

        if (_chooseComparison)
        {
            DrawArchiveList(RankingStore.Archives.Where(other => _archive.CanCompare(other)), L("archive_no_compatible"), other =>
            {
                _compare = other;
                _chooseComparison = false;
                _archivePage = 0;
                Enter(Page.Record);
            });
            return;
        }

        var scope = NextLine();
        SummaryText(scope, string.Format(L("feature_counts"), _archive.RankedCount, _archive.ScopeCount, _archive.Rows.Count),
            TableWidth - 7, MutedTint);
        if (_compare != null)
            SummaryText(NextLine(), string.Format(L("archive_compared_time"), $"{ArchiveName(_compare)} · {RecordTime(_compare)}"),
                TableWidth - 7, MutedTint);
        var before = _compare?.Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
        var current = _archive.Rows.Select(row => (Now: row, Before: before?.GetValueOrDefault(row.Key))).ToList();
        if (before != null)
        {
            var keys = new HashSet<string>(_archive.Rows.Select(row => row.Key), StringComparer.Ordinal);
            current.AddRange(_compare.Rows.Where(row => !keys.Contains(row.Key)).Select(row => ((RankingRow)null, row)));
        }
        int pages = Math.Max(1, (current.Count + ArchivePageSize - 1) / ArchivePageSize);
        if (pages > 1)
        {
            var pager = NextLine();
            Button(pager, "‹", 25, () => Move(-1)).interactable = _archivePage > 0;
            SummaryText(pager, $"{_archivePage + 1} / {pages}", 57, MutedTint);
            Button(pager, "›", 25, () => Move(1)).interactable = _archivePage + 1 < pages;
        }
        if (current.Count == 0) SummaryText(NextLine(), L("empty"), TableWidth - 7, MutedTint);
        foreach (var (now, previous) in current.Skip(_archivePage * ArchivePageSize).Take(ArchivePageSize))
        {
            var row = now ?? previous;
            var line = NextLine();
            string currentRank = now == null ? "—" : RankLabel(now.Rank);
            string rank = _compare == null ? currentRank : $"{(previous == null ? "—" : RankLabel(previous.Rank))} → {currentRank}";
            SummaryText(line, rank, _compare == null ? 55 : 112, HeadingTint);
            float remaining = TableWidth - (_compare == null ? 60 : 117);
            SummaryText(line, row.Name, remaining * 0.49f);
            var currentValue = now?.Values.GetValueOrDefault(primary);
            var previousValue = previous?.Values.GetValueOrDefault(primary);
            string label = ArchiveValueLabel(primary, currentValue);
            if (_compare != null) label = $"{ArchiveValueLabel(primary, previousValue)} → {label}";
            var valueButton = Button(line, label, remaining * 0.47f, null, flat: true);
            var score = currentValue ?? previousValue;
            valueButton.image.raycastTarget = HasMetricDetails(score);
            if (HasMetricDetails(score))
                AddMetricTooltip(valueButton, () => $"{row.Name} · {metricName}", () => score);
        }

        void Move(int by)
        {
            _archivePage = Mathf.Clamp(_archivePage + by, 0, pages - 1);
            Enter(Page.Record);
        }
    }

    private string ArchiveValueLabel(string fieldID, RankingValue value)
    {
        if (value == null) return "—";
        var board = RankingRegistry.GetBoard(_archive.View.BoardID);
        var field = board == null ? null : RankingRegistry.GetSource(board.SourceID)?.Fields.GetValueOrDefault(fieldID);
        return field?.ValueLabel(value) ?? value.Label;
    }
}
