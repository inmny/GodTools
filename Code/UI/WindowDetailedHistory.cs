using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GodTools.Features;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;
using static GodTools.UI.WorldToolControls;
using HistoryEntry = GodTools.Features.DetailedHistory.Entry;

namespace GodTools.UI;

public class WindowDetailedHistory : AbstractWideWindow<WindowDetailedHistory>
{
    private const float Width = 534;
    private const float Height = 275;
    private const float ListLeft = 111;
    private const float ListTop = 56;
    private const float ListHeight = 179;
    private const float WideList = 400;
    private const float NarrowList = 233;
    private const float RowHeight = 24;
    private const string Locale = "inmny.godtools.history.";
    private static readonly Regex RichTags = new(@"</?(?:b|i|color|size|material|quad|sprite|br)(?:\s[^>]*|=[^>]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private sealed class EventView
    {
        internal HistoryEntry Entry;
        internal int Year;
        internal string Date;
        internal string Body;
        internal string Plain;
        internal string IconPath;
    }

    private sealed class EventRow
    {
        internal SimpleButton Button;
        internal Image Icon;
        internal Text Date;
        internal Text Summary;
        internal EventView Event;
    }

    private readonly List<EventView> _index = new();
    private readonly List<EventView> _results = new();
    private readonly HashSet<string> _filters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SimpleButton> _filterButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EventRow> _rows = new();
    private GameLanguageAsset _language;
    private int _worldRevision = -1;
    private int _first = -1;
    private bool _dirty;
    private bool _exclude;
    private EventView _selected;
    private float ListWidth => _selected == null ? WideList : NarrowList;

    private ScrollRect _listScroll;
    private RectTransform _list;
    private RectTransform _filterGrid;
    private RectTransform _detail;
    private RectTransform _detailContent;
    private ScrollRect _detailScroll;
    private Text _detailDate;
    private Text _detailBody;
    private Text _detailPlace;
    private Text _earliest;
    private Text _matchCount;
    private Text _empty;
    private SimpleButton _location;
    private SimpleButton _actor;
    private SimpleButton _mode;
    private Text _modeText;
    private InputField _keyword;
    private InputField _from;
    private InputField _to;
    private InputField _jump;

    private static string L(string key) => LM.Get(Locale + key);
    private static Sprite NativeIcon(string id) => WindowCreatureSavedList.Icon(id);

    protected override void Init()
    {
        _listScroll = ContentTransform.GetComponentInParent<ScrollRect>(true);
        var root = Rect(BackgroundTransform, "HistoryLayout", 0, 0, Width, Height);
        _listScroll.name = "HistoryEvents";
        var categoryScroll = Instantiate(_listScroll, root, false);
        _detailScroll = Instantiate(_listScroll, root, false);
        _list = ScrollPane(_listScroll, root, ListLeft, ListTop, WideList, ListHeight);
        _listScroll.onValueChanged.AddListener(_ => DrawRows());

        Panel(root, "HistoryFilters", 12, 48, 89, 187).transform.SetAsFirstSibling();
        Label(root, "HistoryFilterTitle", L("event_types"), 20, 50, 74, 16, 9, Gold);
        _mode = Button(root, "HistoryFilterMode", null, 18, 69, 72, 17, () =>
        {
            _exclude = !_exclude;
            _dirty = true;
        });
        _modeText = Label(_mode.transform, "Caption", "", 0, 0, 72, 17, 8, alignment: TextAnchor.MiddleCenter);
        _filterGrid = ScrollPane(categoryScroll, root, 17, 90, 72, 139);

        _keyword = Input(root, "HistoryKeyword", L("search"), 15, 20, 180, RequestFilter);
        _from = Input(root, "HistoryYearFrom", L("from"), 208, 20, 70, RequestFilter, InputField.CharacterValidation.Integer);
        Label(root, "YearRangeSeparator", "–", 279, 20, 13, 20, alignment: TextAnchor.MiddleCenter);
        _to = Input(root, "HistoryYearTo", L("to"), 293, 20, 70, RequestFilter, InputField.CharacterValidation.Integer);
        Button(root, "HistoryClear", null, 367, 20, 21, 20, ClearFilters, NativeIcon("iconClose"), L("clear"));
        _matchCount = Label(root, "HistoryMatches", "", 397, 20, 121, 20, 9, Muted, TextAnchor.MiddleRight);

        _earliest = Label(root, "HistoryEarliest", "", 14, 241, 209, 20, 9, Muted);
        Button(root, "HistoryFirst", L("first"), 232, 241, 53, 20, () => ScrollTo(0));
        _jump = Input(root, "HistoryJump", L("jump"), 291, 241, 107, null, InputField.CharacterValidation.Integer);
        _jump.onEndEdit.AddListener(JumpToYear);
        Button(root, "HistoryLast", L("last"), 403, 241, 53, 20, () => ScrollTo(_results.Count - 1));
        _empty = Label(root, "HistoryEmpty", "", ListLeft, 121, WideList, 22, 10, Muted, TextAnchor.MiddleCenter);

        _detail = Panel(root, "HistoryEventDetail", 356, 48, 165, 187).rectTransform;
        _detailDate = Label(_detail, "HistoryDetailDate", "", 9, 6, 124, 20, 10, Gold);
        Button(_detail, "HistoryCloseDetails", null, 137, 6, 18, 18, () => Select(null), NativeIcon("iconClose"), L("close"));
        _detailContent = ScrollPane(_detailScroll, _detail, 9, 32, 138, 98);
        _detailBody = Label(_detailContent, "HistoryFullText", "", 0, 0, 136, 98, 10, alignment: TextAnchor.UpperLeft);
        _detailBody.supportRichText = true;
        _detailBody.verticalOverflow = VerticalWrapMode.Overflow;
        _detailPlace = Label(_detail, "HistoryCoordinates", "", 9, 136, 149, 16, 8, Muted);
        _location = Button(_detail, "HistoryLocate", L("locate"), 8, 159, 72, 20, JumpToEvent);
        _actor = Button(_detail, "HistoryOpenActor", L("actor"), 85, 159, 72, 20, OpenActor);
        _detail.gameObject.SetActive(false);

        if (GetComponent<ScrollWindow>().scrollingGradient is Image gradient) gradient.enabled = false;
    }

    private void RequestFilter() => _dirty = true;

    private void ClearFilters()
    {
        _keyword.SetTextWithoutNotify("");
        _from.SetTextWithoutNotify("");
        _to.SetTextWithoutNotify("");
        _filters.Clear();
        _dirty = true;
    }

    public override void OnNormalEnable()
    {
        _dirty = true;
        Refresh();
    }

    private void LateUpdate()
    {
        if (_list == null) return;
        Refresh();
        if (_selected != null) _actor.Button.interactable = LiveActor() != null;
    }

    private void Refresh()
    {
        bool resetScroll = _dirty;
        if (_worldRevision != DetailedHistory.WorldRevision || _language != LocalizedTextManager.current_language ||
            _index.Count > DetailedHistory.Messages.Count)
        {
            if (_worldRevision != DetailedHistory.WorldRevision)
            {
                ClearFilters();
                _exclude = false;
                _jump.SetTextWithoutNotify("");
                Select(null);
                resetScroll = true;
            }
            _worldRevision = DetailedHistory.WorldRevision;
            _language = LocalizedTextManager.current_language;
            _index.Clear();
            foreach (var button in _filterButtons.Values)
            {
                button.gameObject.SetActive(false);
                Destroy(button.gameObject);
            }
            _filterButtons.Clear();
            _dirty = true;
        }

        for (int i = _index.Count; i < DetailedHistory.Messages.Count; i++)
        {
            var entry = DetailedHistory.Messages[i];
            entry.ReadDisplay(out string body, out string path);
            var view = new EventView
            {
                Entry = entry, Year = Date.getYear(entry.Timestamp),
                Date = string.Format(L("date"), Date.getYear(entry.Timestamp), Date.getMonth(entry.Timestamp)),
                Body = body, Plain = Whitespace.Replace(RichTags.Replace(body, ""), " ").Trim(),
                IconPath = path ?? ""
            };
            _index.Add(view);
            AddFilter(view);
            _dirty = true;
        }

        if (!_dirty) return;
        _dirty = false;
        string[] words = _keyword.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        bool hasFrom = int.TryParse(_from.text, out int from);
        bool hasTo = int.TryParse(_to.text, out int to);
        _results.Clear();
        if (!hasFrom || !hasTo || from <= to)
            _results.AddRange(_index.Where(view =>
                (!hasFrom || view.Year >= from) && (!hasTo || view.Year <= to) &&
                (_filters.Count == 0 || _filters.Contains(view.IconPath) != _exclude) &&
                words.All(word => view.Plain.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(view => view.Entry.Timestamp));

        _earliest.text = _index.Count == 0 ? "" : string.Format(L("earliest"),
            _index.OrderBy(view => view.Entry.Timestamp).First().Date);
        _matchCount.text = string.Format(L("matches"), _results.Count);
        _empty.text = hasFrom && hasTo && from > to ? L("invalid_range") : L(_index.Count == 0 ? "empty" : "no_results");
        _empty.gameObject.SetActive(_results.Count == 0);
        _modeText.text = L(_exclude ? "exclude" : "include");
        foreach (var pair in _filterButtons)
            pair.Value.Background.color = _filters.Contains(pair.Key) ? (_exclude ? Gold : Selected) : Color.white;
        var previous = _selected?.Entry;
        var matching = previous == null ? null : _results.Find(view => ReferenceEquals(view.Entry, previous));
        if (!ReferenceEquals(matching, _selected)) Select(matching);
        SetListSize(resetScroll);
    }

    private void AddFilter(EventView view)
    {
        if (_filterButtons.ContainsKey(view.IconPath)) return;
        int index = _filterButtons.Count;
        var icon = EventIcon(view.IconPath);
        string hint = view.Entry.AssetId;
        if (AssetManager.world_log_library.getSimple(view.Entry.AssetId) is { } asset)
        {
            string key = asset.getLocaleID();
            if (LocalizedTextManager.stringExists(key)) hint = LocalizedTextManager.getText(key);
        }
        var button = Button(_filterGrid, "HistoryType", null, index % 3 * 24, index / 3 * 24, 22, 22, () =>
        {
            if (!_filters.Add(view.IconPath)) _filters.Remove(view.IconPath);
            _dirty = true;
        }, icon, hint);
        _filterButtons.Add(view.IconPath, button);
        _filterGrid.sizeDelta = new Vector2(72, (index / 3 + 1) * 24);
    }

    private static Sprite EventIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return NativeIcon("iconWorldLog");
        return (path.StartsWith("ui/", StringComparison.OrdinalIgnoreCase)
            ? SpriteTextureLoader.getSprite(path) : NativeIcon(path)) ?? NativeIcon("iconWorldLog");
    }

    private void SetListSize(bool resetScroll)
    {
        var root = (RectTransform)_listScroll.transform.parent;
        var layout = (RectTransform)_listScroll.transform;
        float oldOffset = _list.anchoredPosition.y;
        layout.sizeDelta = new Vector2(ListWidth + NativeListPane.ScrollbarGutter, ListHeight);
        layout.anchoredPosition = new Vector2(ListLeft + (layout.sizeDelta.x - root.rect.width) / 2, layout.anchoredPosition.y);
        _list.sizeDelta = new Vector2(ListWidth, _results.Count * RowHeight);
        Place(_empty.rectTransform, ListLeft, 121, ListWidth, 22);
        _first = -1;
        if (resetScroll) ScrollTo(0);
        else SetScrollOffset(oldOffset);
    }

    private void JumpToYear(string value)
    {
        if (!int.TryParse(value, out int year) || _results.Count == 0) return;
        int index = _results.FindIndex(view => view.Year >= year);
        ScrollTo(index < 0 ? _results.Count - 1 : index);
    }

    private void ScrollTo(int index) => SetScrollOffset(Mathf.Max(0, index) * RowHeight);

    private void SetScrollOffset(float offset)
    {
        _listScroll.StopMovement();
        _list.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, _list.sizeDelta.y - ListHeight)));
        DrawRows();
    }

    private void DrawRows()
    {
        int first = Mathf.Max(0, Mathf.FloorToInt(_list.anchoredPosition.y / RowHeight));
        if (first == _first) return;
        _first = first;
        int visible = Mathf.Max(0, Mathf.Min(_results.Count - first, Mathf.CeilToInt(ListHeight / RowHeight) + 1));
        while (_rows.Count < visible)
        {
            var row = new EventRow();
            row.Button = Button(_list, "HistoryEvent", null, 0, 0, ListWidth, RowHeight - 2, () => Select(row.Event));
            row.Icon = Rect(row.Button.transform, "EventIcon", 6, 4, 14, 14).gameObject.AddComponent<Image>();
            row.Icon.raycastTarget = false;
            row.Icon.preserveAspect = true;
            row.Date = Label(row.Button.transform, "EventDate", "", 25, 0, 65, RowHeight - 2, 8, Gold);
            row.Summary = Label(row.Button.transform, "EventSummary", "", 95, 0, ListWidth - 103, RowHeight - 2);
            row.Summary.horizontalOverflow = HorizontalWrapMode.Overflow;
            _rows.Add(row);
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Button.gameObject.SetActive(i < visible);
            if (i >= visible) continue;
            var view = _results[first + i];
            row.Event = view;
            Place((RectTransform)row.Button.transform, 0, (first + i) * RowHeight, ListWidth, RowHeight - 2);
            row.Button.Background.color = view == _selected ? Selected : Color.white;
            row.Icon.sprite = EventIcon(view.IconPath);
            row.Date.text = view.Date;
            row.Summary.text = FitSummary(row.Summary, view.Plain, ListWidth - 101);
        }
    }

    private static string FitSummary(Text label, string text, float width)
    {
        // 单行文本用换行边界截断，字符较宽的名称也不会遮住右侧。
        var settings = label.GetGenerationSettings(new Vector2(width, RowHeight - 3));
        settings.horizontalOverflow = HorizontalWrapMode.Wrap;
        settings.verticalOverflow = VerticalWrapMode.Truncate;
        settings.textAnchor = TextAnchor.UpperLeft;
        label.cachedTextGenerator.Populate(text, settings);
        int length = Math.Min(text.Length, label.cachedTextGenerator.lineCount > 1
            ? label.cachedTextGenerator.lines[1].startCharIdx : label.cachedTextGenerator.characterCountVisible);
        if (length >= text.Length) return text;
        length = Math.Max(0, length - 2);
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text.Substring(0, length).TrimEnd() + "…";
    }

    private void Select(EventView view)
    {
        _selected = view;
        _detail.gameObject.SetActive(view != null);
        if (view != null)
        {
            _detailDate.text = view.Date;
            _detailBody.text = view.Body;
            _detailBody.font = LocalizedTextManager.current_font;
            float height = Mathf.Max(98, _detailBody.preferredHeight + 4);
            _detailContent.sizeDelta = new Vector2(138, height);
            Place(_detailBody.rectTransform, 0, 0, 136, height);
            _detailScroll.StopMovement();
            _detailContent.anchoredPosition = Vector2.zero;
            var tile = EventTile();
            _detailPlace.text = tile == null ? L("no_position") : string.Format(L("coordinates"), tile.x, tile.y);
            _location.Button.interactable = tile != null;
            _actor.Button.interactable = LiveActor() != null;
        }
        SetListSize(false);
    }

    private WorldTile EventTile() => _selected?.Entry is { X: int x, Y: int y } && x >= 0 && y >= 0
        ? World.world?.GetTile(x, y) : null;

    private Actor LiveActor()
    {
        if (_selected?.Entry is not { UnitId: >= 0 } entry) return null;
        var actor = World.world?.units.get(entry.UnitId);
        return actor?.isAlive() == true ? actor : null;
    }

    private void JumpToEvent()
    {
        if (EventTile() is not { } tile) return;
        ScrollWindow.moveAllToLeftAndRemove();
        WorldLog.locationJump(new Vector3(tile.x, tile.y));
    }

    private void OpenActor()
    {
        if (LiveActor() is { } actor) ActionLibrary.openUnitWindow(actor);
    }
}
