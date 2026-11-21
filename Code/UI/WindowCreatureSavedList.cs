using System;
using System.Collections.Generic;
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

public class WindowCreatureSavedList : AbstractWideWindow<WindowCreatureSavedList>
{
    internal const float RowWidth = 320;
    internal const float RowHeight = 36;
    internal const float IdentityWidth = 199;
    internal const float LevelWidth = 35;
    private const string Locale = "inmny.godtools.saved_creatures.";
    private static readonly Color Muted = new Color32(192, 198, 174, 255);
    private static readonly Color Selected = new Color32(145, 162, 112, 255);
    private enum SortKey { Recent, Name, Level }

    private sealed class Entry(CreatureSavedList.SavedActorPacket packet, string name, string summary, string searchText, int order)
    {
        internal readonly CreatureSavedList.SavedActorPacket Packet = packet;
        internal readonly string Name = name;
        internal readonly string Summary = summary;
        internal readonly string SearchText = searchText;
        internal readonly int Order = order;
    }
    private Entry[] _catalog = Array.Empty<Entry>();
    private Entry[] _results = Array.Empty<Entry>();
    private readonly List<SavedActorDataCard> _cards = new();
    private int _first = -1;
    private float _offset;
    private bool _refreshing;
    private string _pendingDelete;
    private float _confirmUntil;
    private bool _onlyFavorites;
    private bool _ascending;
    private SortKey _sort;
    private readonly SimpleButton[] _sortButtons = new SimpleButton[3];

    private ScrollRect _scroll;
    private RectTransform _content;
    private InputField _search;
    private SimpleButton _clear;
    private SimpleButton _favorites;
    private SimpleButton _direction;
    private Text _directionLabel;
    private Text _count;
    private Text _empty;
    private PowerButton _powerButton;
    private PowerButton _villagePowerButton;

    protected override void Init()
    {
        _scroll = ContentTransform.GetComponentInParent<ScrollRect>(true);
        _scroll.name = "SavedCreatureScroll";
        _content = NativeListPane.Configure(_scroll, BackgroundTransform, RowWidth, 179, 0, -34.5f);
        if (GetComponent<ScrollWindow>().scrollingGradient is Image gradient) gradient.enabled = false;
        _scroll.onValueChanged.AddListener(_ =>
        {
            if (_refreshing || !isActiveAndEnabled) return;
            _offset = _content.anchoredPosition.y;
            DrawRows();
        });

        var toolbar = Band("SavedCreatureToolbar", 108, 22);
        var search = TextInput.Instantiate(toolbar);
        search.name = "SavedCreatureSearch";
        search.Setup("", _ => { });
        search.SetSize(new Vector2(143, 20));
        Position((RectTransform)search.transform, 0, 1, 143, 20);
        _search = search.input;
        _search.lineType = InputField.LineType.SingleLine;
        var placeholder = Label(search.transform, "SearchPlaceholder", L("search"), 3, 0, 125, 20, 9, Muted);
        var inputText = _search.textComponent.rectTransform;
        inputText.anchorMin = Vector2.zero;
        inputText.anchorMax = Vector2.one;
        inputText.offsetMin = new Vector2(3, 0);
        inputText.offsetMax = new Vector2(-14, 0);
        if (_search.placeholder != null) _search.placeholder.gameObject.SetActive(false);
        _search.placeholder = placeholder;
        _search.onValueChanged.AddListener(_ => Refresh(resetScroll: true));

        _clear = Command(toolbar, "ClearSearch", 146, 1, 20, Icon("iconClose"), L("clear"), () => _search.text = "");
        _favorites = Command(toolbar, "OnlyFavorites", 169, 1, 20, Icon("iconFavoriteStar"), L("only_favorites"), () =>
        {
            _onlyFavorites = !_onlyFavorites;
            Refresh(resetScroll: true);
        });
        AddSort(SortKey.Recent, "recent", 194, 41);
        AddSort(SortKey.Name, "name", 237, 31);
        AddSort(SortKey.Level, "level", 270, 28);
        _direction = Command(toolbar, "SortDirection", 301, 1, 19, null, L("descending"), () =>
        {
            _ascending = !_ascending;
            Refresh(resetScroll: true);
        });
        _direction.TipButton.setHoverAction(() => Tooltip.show(_direction.gameObject, "normal",
            new TooltipData { tip_name = L(_ascending ? "ascending" : "descending") }), false);
        _directionLabel = Label(_direction.transform, "DirectionLabel", "", 0, 0, 19, 20, 10, Muted,
            TextAnchor.MiddleCenter);

        void AddSort(SortKey key, string label, float x, float width)
        {
            var button = Command(toolbar, "Sort" + key, x, 1, width, null, null, () =>
            {
                _sort = key;
                _ascending = key == SortKey.Name;
                Refresh(resetScroll: true);
            });
            Label(button.transform, "SortLabel", L(label), 3, 0, width - 6, 20, 8, Color.white, TextAnchor.MiddleCenter);
            _sortButtons[(int)key] = button;
        }

        _count = Label(Band("SavedCreatureSummary", 85, 17), "SavedCreatureCount", "", 3, 0, RowWidth - 6, 17, 9, Muted);
        var headings = Band("SavedCreatureColumns", 65, 16);
        Label(headings, "IdentityHeading", L("creature"), 47, 0, 145, 16, 9, Muted);
        Label(headings, "LevelHeading", L("level"), IdentityWidth, 0, LevelWidth, 16, 9, Muted, TextAnchor.MiddleCenter);
        _empty = Label(Band("SavedCreatureEmpty", 15, 26), "SavedCreatureEmptyText", "", 0, 0, RowWidth, 26, 11, Muted,
            TextAnchor.MiddleCenter);

        _powerButton = PowerButtonCreator.CreateGodPowerButton(GodPowers.place_saved_actor.id,
            SpriteTextureLoader.getSprite("gt_windows/save_actor_list"), Main.prefabs);
        _villagePowerButton = PowerButtonCreator.CreateGodPowerButton(GodPowers.place_saved_actor_in_village.id,
            Icon("iconCitySelect"), Main.prefabs);
    }

    public override void OnNormalEnable()
    {
        ReadCatalog();
        Refresh(resetScroll: false);
    }

    public override void OnNormalDisable()
    {
        _pendingDelete = null;
        WindowRankings.HidePeakTooltip();
        base.OnNormalDisable();
    }

    private void ReadCatalog()
    {
        _catalog = CreatureSavedList.SavedActors.Values.Where(packet => packet?.ActorData != null).Select((packet, index) =>
        {
            var unit = packet.ActorData;
            var name = string.IsNullOrWhiteSpace(unit.name) ? L("unnamed") : unit.name;
            var species = BuiltInRankings.Label(AssetManager.actor_library.get(unit.asset_id)?.getLocaleID(), unit.asset_id);
            var ties = new[] { packet.CultureData?.name, packet.ClanData?.name, packet.LanguageData?.name, packet.SubspeciesData?.name };
            var summary = string.Join(" · ", new[] { species, ties.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());
            var search = string.Join("\n", new[] { name, species, unit.asset_id }.Concat(ties));
            return new Entry(packet, name, summary, search, index);
        }).ToArray();
    }

    private void Refresh(bool resetScroll)
    {
        _pendingDelete = null;
        string[] words = _search.text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        var matching = _catalog.Where(entry => (!_onlyFavorites || entry.Packet.Favorite) &&
            words.All(word => entry.SearchText.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));
        _results = (_sort switch
        {
            SortKey.Name => Order(matching, entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            SortKey.Level => Order(matching, entry => entry.Packet.ActorData.level),
            _ => Order(matching, entry => entry.Packet.SavedAtUtc)
        }).ToArray();

        IOrderedEnumerable<Entry> Order<T>(IEnumerable<Entry> entries, Func<Entry, T> key, IComparer<T> comparer = null) =>
            _ascending ? entries.OrderBy(key, comparer).ThenBy(entry => entry.Order) :
            entries.OrderByDescending(key, comparer).ThenByDescending(entry => entry.Order);

        _clear.Button.interactable = _search.text.Length > 0;
        _favorites.Background.color = _onlyFavorites ? Selected : Color.white;
        for (int index = 0; index < _sortButtons.Length; index++)
            _sortButtons[index].Background.color = index == (int)_sort ? Selected : Color.white;
        _directionLabel.text = _ascending ? "↑" : "↓";
        _count.text = string.Format(L(_results.Length == _catalog.Length && !_onlyFavorites ? "count" :
            _onlyFavorites ? "favorite_count" : "match_count"), _results.Length, _catalog.Length);
        _empty.text = L(_catalog.Length == 0 ? "empty" : "no_results");
        _empty.gameObject.SetActive(_results.Length == 0);
        _refreshing = true;
        _scroll.StopMovement();
        _content.sizeDelta = new Vector2(RowWidth, _results.Length * RowHeight);
        _offset = resetScroll ? 0 : Mathf.Clamp(_offset, 0, Mathf.Max(0, _content.sizeDelta.y - _scroll.viewport.rect.height));
        _content.anchoredPosition = new Vector2(0, _offset);
        _refreshing = false;
        _first = -1;
        DrawRows();
    }

    private void DrawRows()
    {
        int first = Mathf.Clamp(Mathf.FloorToInt(_content.anchoredPosition.y / RowHeight), 0, Mathf.Max(0, _results.Length - 1));
        if (first == _first) return;
        _first = first;
        WindowRankings.HidePeakTooltip();
        int visible = Mathf.Min(Mathf.CeilToInt(_scroll.viewport.rect.height / RowHeight) + 1, _results.Length - first);
        while (_cards.Count < visible) _cards.Add(Instantiate(SavedActorDataCard.Prefab, _content, false));
        for (int index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            card.gameObject.SetActive(index < visible);
            if (index >= visible) continue;
            var entry = _results[first + index];
            var packet = entry.Packet;
            Position((RectTransform)card.transform, 0, (first + index) * RowHeight, RowWidth, RowHeight - 2);
            card.Setup(packet, entry.Name, entry.Summary, _pendingDelete == packet.ID,
                () => PlaceCatalogEntry(packet, false), () => PlaceCatalogEntry(packet, true),
                () => ChangeCatalog(() => CreatureSavedList.ToggleFavorite(packet)), () => RequestDelete(packet));
        }
    }

    private void Update()
    {
        if (_pendingDelete == null || Time.unscaledTime < _confirmUntil) return;
        _pendingDelete = null;
        _first = -1;
        DrawRows();
    }

    private void RequestDelete(CreatureSavedList.SavedActorPacket packet)
    {
        if (_pendingDelete == packet.ID && Time.unscaledTime < _confirmUntil)
        {
            ChangeCatalog(() => CreatureSavedList.UnsaveActor(packet));
            return;
        }
        _pendingDelete = packet.ID;
        _confirmUntil = Time.unscaledTime + 4;
        _first = -1;
        DrawRows();
        WorldTip.showNowTop(Locale + "confirm_delete", true);
    }

    private void ChangeCatalog(Action change)
    {
        try { change(); }
        catch (Exception error)
        {
            Main.LogWarning($"Could not update saved creatures: {error.Message}");
            WorldTip.showNowTop(Locale + "save_error", true);
        }
        ReadCatalog();
        Refresh(resetScroll: false);
    }

    private void PlaceCatalogEntry(CreatureSavedList.SavedActorPacket packet, bool inVillage)
    {
        if (!CanArmPlacement(packet, inVillage)) return;
        CreatureSavedList.SelectActorToPlace(packet);
        ArmPlacement(inVillage);
    }

    internal static void PlaceSnapshot(CreatureSavedList.SavedActorPacket packet, bool inVillage = false)
    {
        if (!CanArmPlacement(packet, inVillage)) return;
        CreatureSavedList.SelectSnapshotToPlace(packet);
        Instance.ArmPlacement(inVillage);
    }

    private static bool CanArmPlacement(CreatureSavedList.SavedActorPacket packet, bool inVillage) =>
        inVillage ? CreatureSavedList.CanPlaceInVillage(packet) : CreatureSavedList.CanPlace(packet);

    private void ArmPlacement(bool inVillage)
    {
        ScrollWindow.hideAllEvent();
        if (inVillage)
        {
            if (World.world.selected_power != GodPowers.place_saved_actor_in_village) _villagePowerButton.clickActivePower();
            WorldTip.showNowTop("inmny.godtools.place_saved_actor_in_village description", true);
        }
        else if (World.world.selected_power != GodPowers.place_saved_actor) _powerButton.clickActivePower();
    }

    private RectTransform Band(string name, float y, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(BackgroundTransform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-NativeListPane.ScrollbarGutter * 0.5f, y);
        rect.sizeDelta = new Vector2(RowWidth, height);
        return rect;
    }

    internal static void Position(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static Text Label(Transform parent, string name, string value, float x, float y, float width, float height,
        int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var label = RawText.Instantiate(parent);
        label.name = name;
        label.Setup(value, color, alignment);
        Position((RectTransform)label.transform, x, y, width, height);
        label.Text.fontSize = size;
        label.Text.resizeTextForBestFit = false;
        label.Text.raycastTarget = false;
        label.Text.supportRichText = false;
        return label.Text;
    }

    private static SimpleButton Command(Transform parent, string name, float x, float y, float width, Sprite icon,
        string hint, Action action)
    {
        var button = SimpleButton.Instantiate(parent);
        button.name = name;
        button.Setup(() => action(), icon, pSize: new Vector2(width, 20),
            pTipType: string.IsNullOrEmpty(hint) ? null : "tip", pTipData: new TooltipData { tip_name = hint });
        button.Icon.enabled = icon != null;
        button.Background.sprite = ControlSurface();
        Position((RectTransform)button.transform, x, y, width, 20);
        return button;
    }

    internal static Sprite ControlSurface() => SpriteTextureLoader.getSprite("ui/special/special_buttonGray_insides");
    internal static Sprite Icon(string id) => SpriteTextureLoader.getSprite("ui/icons/" + id);
    internal static string L(string key) => LM.Get(Locale + key);
}
