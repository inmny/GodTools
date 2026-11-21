using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.UI.Prefabs;
using GodTools.Utils;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;

namespace GodTools.UI.CreatureDataEditorGrids;

internal sealed class BackpackResourceEditor : CreatureDataEditorGrid
{
    private const string Locale = "inmny.godtools.ui.data_editor.backpack.";
    private const int Width = 176;
    private const int HeaderHeight = 68;
    private const int CatalogColumns = 6;
    private const int CellHeight = 25;
    private const int CellGap = 3;
    private static readonly Color Chosen = new(0.82f, 0.88f, 0.65f);
    private readonly List<Cell> _cells = new();
    private readonly Dictionary<string, int> _amounts = new(StringComparer.Ordinal);

    private Actor _actor;
    private long _actorID;
    private MapStats _map;
    private bool _carriedOnly;
    private string _selected;
    private float _nextRefresh;
    private ScrollRect _scroll;
    private RectTransform _header;
    private RectTransform _scrollbarMask;
    private Vector2 _viewportTop;
    private Vector2 _scrollbarSize;
    private Vector2 _scrollbarPosition;
    private RectTransform _catalog;
    private RectTransform _quantityRow;
    private InputField _search;
    private InputField _quantity;
    private Text _subject;
    private Text _resourceName;
    private Text _empty;
    private Image _resourceIcon;
    private Button _carried;
    private Button _all;
    private Button _decrease;
    private Button _increase;
    private Button _remove;

    private sealed class Cell
    {
        internal string ID;
        internal string Label;
        internal Button Button;
        internal Image Icon;
        internal Text Count;
    }

    private Actor Target => _actor?.data != null && _actor.data.id == _actorID && _actor.isAlive() &&
        ReferenceEquals(_map, World.world?.map_stats) ? _actor : null;

    public override void Setup()
    {
        GetComponent<ContentSizeFitter>().enabled = false;
        _scroll = GetComponentInParent<ScrollRect>(true);
        _scrollbarMask = (RectTransform)_scroll.verticalScrollbar.transform.parent;
        _viewportTop = _scroll.viewport.offsetMax;
        _scrollbarSize = _scrollbarMask.sizeDelta;
        _scrollbarPosition = _scrollbarMask.anchoredPosition;

        _header = new GameObject("BackpackHeader", typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        _header.SetParent(_scroll.transform, false);
        _header.anchorMin = _header.anchorMax = _header.pivot = new Vector2(0.5f, 1);
        _header.sizeDelta = new Vector2(Width, HeaderHeight);
        _header.anchoredPosition = new Vector2(-4, -4);
        var headerLayout = _header.GetComponent<VerticalLayoutGroup>();
        headerLayout.SetSimpleVertLayout(4);
        headerLayout.childControlHeight = true;

        var title = GetComponentInChildren<LocalizedText>(true);
        title.enabled = false;
        title.GetComponent<ContentSizeFitter>().enabled = false;
        _subject = title.GetComponent<Text>();
        _subject.transform.SetParent(_header, false);
        _subject.rectTransform.sizeDelta = new Vector2(Width, 16);
        var titleLayout = _subject.gameObject.AddComponent<LayoutElement>();
        titleLayout.minHeight = titleLayout.preferredHeight = 14;
        _subject.fontSize = 10;
        _subject.color = new Color(0.94f, 0.80f, 0.57f);

        var filters = Row("BackpackFilters", 20);
        _carried = ButtonControl(filters, "BackpackCarried", L("carried"), 40, () => SetMode(true));
        _all = ButtonControl(filters, "BackpackAll", L("all"), 40, () => SetMode(false));
        _search = Input(filters, "BackpackSearch", 90);
        var placeholder = RawText.Instantiate(_search.textComponent.transform.parent, pName: "Placeholder");
        placeholder.Setup(L("search"), new Color(1, 1, 1, 0.50f), TextAnchor.MiddleLeft);
        var rect = placeholder.RectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3, 0);
        rect.offsetMax = new Vector2(-3, 0);
        placeholder.Text.resizeTextForBestFit = false;
        placeholder.Text.fontSize = 8;
        placeholder.Text.raycastTarget = false;
        _search.placeholder = placeholder.Text;
        _search.onValueChanged.AddListener(_ =>
        {
            RenderCatalog();
            ScrollToTop();
        });

        _quantityRow = Row("BackpackQuantityRow", 22);
        _resourceIcon = Icon(_quantityRow, "BackpackResourceIcon", 16);
        _resourceName = Label(_quantityRow, "", 55, 18, 9);
        _decrease = ButtonControl(_quantityRow, "BackpackDecrease", "−", 16, () => Step(-1), L("decrease"));
        _quantity = Input(_quantityRow, "BackpackQuantity", 40);
        _quantity.textComponent.alignment = TextAnchor.MiddleCenter;
        _quantity.characterValidation = InputField.CharacterValidation.Integer;
        _quantity.characterLimit = 10;
        _quantity.onEndEdit.AddListener(CommitQuantity);
        _increase = ButtonControl(_quantityRow, "BackpackIncrease", "+", 16, () => Step(1), L("increase"));
        _remove = ButtonControl(_quantityRow, "BackpackRemove", "×", 16, () => SetQuantity(0), L("remove"));

        _catalog = new GameObject("BackpackResources", typeof(GridLayoutGroup)).GetComponent<RectTransform>();
        _catalog.SetParent(transform, false);
        var layout = _catalog.GetComponent<GridLayoutGroup>();
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = CatalogColumns;
        layout.cellSize = new Vector2(26, CellHeight);
        layout.spacing = new Vector2(CellGap, CellGap);
        layout.childAlignment = TextAnchor.UpperCenter;

        _empty = Label(transform, "", Width, 15, 9);
        _empty.alignment = TextAnchor.MiddleCenter;
        _empty.color = new Color(0.73f, 0.75f, 0.67f);
        _header.gameObject.SetActive(false);
    }

    public override void EnabledWith(Actor actor)
    {
        _actor = actor;
        _actorID = actor?.data?.id ?? 0;
        _map = World.world?.map_stats;
        ShowHeader(true);
        _subject.text = Target?.getName() ?? L("unavailable");
        _search.SetTextWithoutNotify("");
        _selected = actor?.inventory.hasResources() == true
            ? actor.inventory.getResources().FirstOrDefault(entry => entry.Value.amount > 0).Key : null;
        _carriedOnly = _selected != null;
        Refresh();
    }

    private void OnDisable()
    {
        _actor = null;
        if (_header != null) ShowHeader(false);
    }

    private void ShowHeader(bool visible)
    {
        _header.gameObject.SetActive(visible);
        var shift = new Vector2(0, visible ? HeaderHeight : 0);
        _scroll.viewport.offsetMax = _viewportTop - shift;
        _scrollbarMask.sizeDelta = _scrollbarSize - shift;
        _scrollbarMask.anchoredPosition = _scrollbarPosition - shift / 2;
        ScrollToTop();
    }

    private void ScrollToTop()
    {
        _scroll.StopMovement();
        var position = _scroll.content.anchoredPosition;
        _scroll.content.anchoredPosition = new Vector2(position.x, 0);
    }

    private void Update()
    {
        if (_actor == null || Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        if (Target == null)
        {
            _subject.text = L("unavailable");
            _actor = null;
            Refresh();
            return;
        }

        var bag = _actor.inventory?.getResources();
        if ((bag?.Count ?? 0) != _amounts.Count ||
            bag != null && bag.Any(entry => !_amounts.TryGetValue(entry.Key, out var amount) || amount != entry.Value.amount))
            Refresh();
    }

    private void Refresh()
    {
        _amounts.Clear();
        var bag = Target?.inventory?.getResources();
        if (bag != null)
            foreach (var (id, value) in bag) _amounts[id] = value.amount;
        RenderQuantity();
        RenderCatalog();
    }

    private void SetMode(bool carriedOnly)
    {
        _carriedOnly = carriedOnly;
        Refresh();
        ScrollToTop();
    }

    private void RenderQuantity()
    {
        var actor = Target;
        _quantityRow.gameObject.SetActive(actor != null && _selected != null);
        if (!_quantityRow.gameObject.activeSelf) return;
        var asset = AssetManager.resources.getSimple(_selected);
        int amount = Math.Max(0, actor.inventory.getResource(_selected));
        _resourceIcon.sprite = ResourceIcon(asset);
        _resourceName.text = ResourceName(_selected);
        if (!_quantity.isFocused) _quantity.SetTextWithoutNotify(amount.ToString(CultureInfo.InvariantCulture));
        _quantity.interactable = asset != null;
        _decrease.interactable = amount > 0;
        _increase.interactable = asset != null && amount < int.MaxValue;
        _remove.interactable = amount > 0;
    }

    private void RenderCatalog()
    {
        bool available = Target != null;
        _carried.interactable = _all.interactable = _search.interactable = available;
        _carried.image.color = _carriedOnly ? Chosen : Color.white;
        _all.image.color = _carriedOnly ? Color.white : Chosen;

        var resources = available
            ? (_carriedOnly ? _amounts.Where(entry => entry.Value > 0).Select(entry => entry.Key)
                : AssetManager.resources.list.Where(asset => !asset.isTemplateAsset()).Select(asset => asset.id)
                    .Concat(_amounts.Where(entry => entry.Value > 0).Select(entry => entry.Key)).Distinct())
            : Enumerable.Empty<string>();
        string search = _search.text.Trim();
        var entries = resources.Select(id => (ID: id, Label: ResourceName(id)))
            .Where(item => search.Length == 0 || item.Label.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                item.ID.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();

        for (int i = 0; i < entries.Length; i++)
        {
            var (id, label) = entries[i];
            if (i == _cells.Count) _cells.Add(CreateCell());
            var cell = _cells[i];
            cell.ID = id;
            cell.Label = label;
            cell.Button.name = "BackpackResource." + id;
            cell.Button.gameObject.SetActive(true);
            cell.Button.image.color = id == _selected ? Chosen : new Color(1, 1, 1, 0.22f);
            cell.Icon.sprite = ResourceIcon(AssetManager.resources.getSimple(id));
            int count = _amounts.GetValueOrDefault(id);
            cell.Count.text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
        }
        for (int i = entries.Length; i < _cells.Count; i++) _cells[i].Button.gameObject.SetActive(false);
        int rows = (entries.Length + CatalogColumns - 1) / CatalogColumns;
        int gridHeight = Math.Max(0, rows * (CellHeight + CellGap) - CellGap);
        _catalog.sizeDelta = new Vector2(Width, gridHeight);
        _catalog.gameObject.SetActive(rows > 0);
        _empty.gameObject.SetActive(available && rows == 0);
        _empty.text = search.Length > 0 ? L("no_results") : L("empty");
        var body = (RectTransform)transform;
        body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _empty.gameObject.activeSelf ? _empty.rectTransform.rect.height : gridHeight);
        LayoutRebuilder.MarkLayoutForRebuild(body);
    }

    private Cell CreateCell()
    {
        var cell = new Cell();
        cell.Button = ButtonControl(_catalog, "BackpackResource", "", 26, () =>
        {
            _selected = cell.ID;
            Refresh();
        });
        cell.Icon = Icon(cell.Button.transform, "Icon", 14);
        cell.Icon.rectTransform.anchoredPosition = new Vector2(0, 4);
        cell.Count = Label(cell.Button.transform, "", 24, 9, 8);
        cell.Count.alignment = TextAnchor.MiddleCenter;
        cell.Count.rectTransform.anchoredPosition = new Vector2(0, -7.5f);
        cell.Count.resizeTextForBestFit = true;
        cell.Count.resizeTextMinSize = 5;
        cell.Count.resizeTextMaxSize = 8;
        cell.Button.gameObject.AddComponent<ScrollableButton>()._scroll_wheel_only = true;
        Hint(cell.Button, () => new TooltipData
        {
            tip_name = cell.Label,
            tip_description = string.Format(L("count"), Target?.inventory.getResource(cell.ID) ?? 0)
        });
        return cell;
    }

    private void CommitQuantity(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
            SetQuantity(quantity);
        else
            RenderQuantity();
    }

    private void Step(int delta)
    {
        if (Target is not Actor actor || _selected == null) return;
        long quantity = (long)actor.inventory.getResource(_selected) + delta;
        SetQuantity((int)Math.Clamp(quantity, 0, int.MaxValue));
    }

    private void SetQuantity(int quantity)
    {
        if (Target is not Actor actor || _selected == null) return;
        int current = Math.Max(0, actor.inventory.getResource(_selected));
        if (quantity > current && AssetManager.resources.has(_selected))
            actor.addToInventory(_selected, quantity - current);
        else if (quantity < current)
            actor.takeFromInventory(_selected, current - quantity);
        Refresh();
    }

    private static string L(string key) => LM.Get(Locale + key);
    private static string ResourceName(string id) => LM.Has(id) ? LM.Get(id) : id;
    private static Sprite ResourceIcon(ResourceAsset asset) =>
        asset?.getSpriteIcon() ?? SpriteTextureLoader.getSprite("ui/icons/iconCityInventory");

    private RectTransform Row(string name, int height)
    {
        var row = new GameObject(name, typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
        row.SetParent(_header, false);
        row.sizeDelta = new Vector2(Width, height);
        var element = row.gameObject.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 3;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return row;
    }

    private static InputField Input(Transform parent, string name, int width)
    {
        var input = TextInput.Instantiate(parent, pName: name);
        input.Setup("", _ => { });
        input.SetSize(new Vector2(width, 18));
        input.text.fontSize = 9;
        return input.input;
    }

    private static Text Label(Transform parent, string title, float width, float height, int font)
    {
        var label = RawText.Instantiate(parent);
        label.SetSize(new Vector2(width, height));
        label.Setup(title, Color.white, TextAnchor.MiddleLeft);
        label.Text.fontSize = font;
        label.Text.resizeTextForBestFit = false;
        label.Text.supportRichText = false;
        label.Text.verticalOverflow = VerticalWrapMode.Truncate;
        label.Text.raycastTarget = false;
        return label.Text;
    }

    private static Image Icon(Transform parent, string name, int size)
    {
        var image = new GameObject(name, typeof(Image)).GetComponent<Image>();
        image.transform.SetParent(parent, false);
        image.rectTransform.sizeDelta = new Vector2(size, size);
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    private static Button ButtonControl(Transform parent, string name, string label, int width, Action action, string hint = null)
    {
        var button = new GameObject(name, typeof(Image), typeof(Button)).GetComponent<Button>();
        var rect = (RectTransform)button.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, 18);
        button.image.sprite = GeneralTools.get_button();
        button.image.type = Image.Type.Sliced;
        if (label.Length > 0)
        {
            var text = Label(rect, label, width - 2, 18, 9);
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(1, 0);
            text.rectTransform.offsetMax = new Vector2(-1, 0);
        }
        button.onClick.AddListener(() => action());
        if (hint != null) Hint(button, () => new TooltipData { tip_name = hint });
        return button;
    }

    private static void Hint(Button button, Func<TooltipData> read)
    {
        var tip = button.gameObject.AddComponent<TipButton>();
        tip.showOnClick = false;
        tip.setHoverAction(() => Tooltip.show(button.gameObject, "normal", read()), false);
    }
}
