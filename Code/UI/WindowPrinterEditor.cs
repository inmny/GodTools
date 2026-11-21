using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Features;
using NeoModLoader.api;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.UI;
using static GodTools.UI.WorldToolControls;
using Order = GodTools.Features.PrinterEditor.Order;
using Settings = GodTools.Features.PrinterEditor.Settings;
using Terrain = GodTools.Features.PrinterEditor.Terrain;

namespace GodTools.UI;

public class WindowPrinterEditor : AbstractWideWindow<WindowPrinterEditor>
{
    private const string Locale = "inmny.godtools.printer.";
    private const int Width = 454;
    private const int Height = 275;
    private const int PreviewSize = 158;
    private static readonly Color32 Paper = new(27, 31, 27, 255);
    private static readonly Color32 Pending = new(69, 77, 63, 255);
    private static readonly Color32 Ink = new(165, 187, 117, 255);

    private readonly Dictionary<PrintTemplate, SimpleButton> _shapes = new();
    private readonly Dictionary<(Terrain, string), SimpleButton> _tileButtons = new();
    private readonly Dictionary<Terrain, RectTransform> _tilePages = new();
    private readonly Dictionary<Terrain, SimpleButton> _tileTabs = new();
    private readonly Dictionary<Order, SimpleButton> _orders = new();
    private Settings _draft = Settings.Default;
    private PrintTemplate _shape;
    private Terrain _tilePage = Terrain.Base;

    private InputField _width;
    private InputField _height;
    private Text _patternName;
    private Text _material;
    private Text _metrics;
    private Text _status;
    private Text _progress;
    private SimpleButton _apply;
    private RectTransform _preview;
    private RawImage _image;
    private PrinterPreviewGrid _grid;
    private Slider _slider;
    private Texture2D _texture;
    private Color32[] _pixels;
    private int[] _firstStep;
    private int _stepCount;
    private bool _dirty;

    private static string L(string key) => LM.Get(Locale + key);

    protected override void Init()
    {
        var root = Rect(BackgroundTransform, "PrinterLayout", 0, 0, Width, Height);
        var scroll = ContentTransform.GetComponentInParent<ScrollRect>(true);
        if (GetComponent<ScrollWindow>().scrollingGradient is Image gradient) gradient.enabled = false;

        Panel(root, "PrinterSettings", 12, 18, 219, 211);
        Label(root, "PrinterPatternHeading", L("patterns"), 21, 22, 90, 16, 10, Gold);
        _patternName = Label(root, "PrinterPatternName", "", 108, 22, 112, 16, 9, Muted, TextAnchor.MiddleRight);
        int index = 0;
        var patterns = PrinterEditor.Templates.ToDictionary(pattern => pattern.name);
        foreach (var power in AssetManager.powers.list.Where(power => !string.IsNullOrEmpty(power.printers_print)))
        {
            if (!patterns.TryGetValue(power.printers_print, out var pattern) || _shapes.ContainsKey(pattern)) continue;
            var button = Button(root, "PrinterShape_" + pattern.name, null, 21 + index % 7 * 28, 41 + index / 7 * 24,
                25, 22, () => SelectShape(pattern), PatternIcon(pattern), PatternName(pattern));
            _shapes.Add(pattern, button);
            _shape ??= pattern;
            index++;
        }
        int shapeHeight = Math.Max(2, (index + 6) / 7) * 24;
        int setupY = 42 + shapeHeight;
        Label(root, "PrinterWidthLabel", L("width"), 22, setupY, 47, 20, 9, Muted);
        _width = Input(root, "PrinterWidth", "1", 69, setupY, 47, RequestPreview, InputField.CharacterValidation.Decimal);
        Label(root, "PrinterHeightLabel", L("height"), 123, setupY, 47, 20, 9, Muted);
        _height = Input(root, "PrinterHeight", "1", 173, setupY, 48, RequestPreview, InputField.CharacterValidation.Decimal);

        foreach (Order order in Enum.GetValues(typeof(Order)))
        {
            var button = Button(root, "PrinterOrder_" + order, LM.Get(order.ToString()), 21 + (int)order * 67,
                setupY + 25, 64, 19, () =>
                {
                    _draft = _draft with { Direction = order };
                    RequestPreview();
                });
            _orders.Add(order, button);
        }

        int tileY = setupY + 49;
        int scrollY = tileY + 37;
        float scrollHeight = Mathf.Max(23, 225 - scrollY);
        var tileContent = ScrollPane(scroll, root, 21, scrollY, 192, scrollHeight);
        tileContent.sizeDelta = new Vector2(192, scrollHeight);
        _material = Label(root, "PrinterTileLabel", "", 22, tileY, 193, 16, 9, Gold);
        _tileTabs[Terrain.Default] = Button(root, "PrinterNativeTerrain", L("default_tile"), 21, tileY + 17, 64, 17, () =>
        {
            _draft = _draft with { Layer = Terrain.Default, Tile = "" };
            RequestPreview();
        });
        _tileTabs[Terrain.Base] = Button(root, "PrinterGround", L("base"), 88, tileY + 17, 64, 17, () => ShowTiles(Terrain.Base));
        _tileTabs[Terrain.Top] = Button(root, "PrinterOverlay", L("top"), 155, tileY + 17, 64, 17, () => ShowTiles(Terrain.Top));

        AddTiles(tileContent, Terrain.Base, AssetManager.tiles.list);
        AddTiles(tileContent, Terrain.Top, AssetManager.top_tiles.list);
        void ShowTiles(Terrain layer)
        {
            _tilePage = layer;
            foreach (var pair in _tilePages) pair.Value.gameObject.SetActive(pair.Key == layer);
            tileContent.sizeDelta = _tilePages[layer].sizeDelta;
            scroll.StopMovement();
            tileContent.anchoredPosition = Vector2.zero;
            RequestPreview();
        }
        ShowTiles(_tilePage);

        Panel(root, "PrinterPreviewPanel", 240, 18, 201, 211);
        Label(root, "PrinterPreviewHeading", L("preview"), 251, 22, 174, 18, 10, Gold);
        _preview = Panel(root, "PrinterPreviewWell", 260, 41, 161, 161).rectTransform;
        _image = Rect(_preview, "PrinterFootprint", 0, 0, PreviewSize, PreviewSize).gameObject.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _grid = Rect(_image.transform, "PrinterGrid", 0, 0, 0, 0).gameObject.AddComponent<PrinterPreviewGrid>();
        _grid.rectTransform.anchorMin = Vector2.zero;
        _grid.rectTransform.anchorMax = Vector2.one;
        _grid.rectTransform.offsetMin = _grid.rectTransform.offsetMax = Vector2.zero;
        _grid.raycastTarget = false;
        _progress = Label(root, "PrinterProgress", "", 388, 204, 44, 18, 8, Muted, TextAnchor.MiddleRight);
        _slider = CreateProgress(root, 252, 205, 131);
        _slider.onValueChanged.AddListener(_ => DrawProgress());

        _metrics = Label(root, "PrinterMetrics", "", 16, 230, 223, 17, 9, Muted);
        _status = Label(root, "PrinterStatus", "", 16, 248, 250, 17, 9, Muted);
        Button(root, "PrinterReset", L("reset"), 272, 240, 77, 20, () => Revert(Settings.Default));
        _apply = Button(root, "PrinterApply", L("apply"), 355, 240, 83, 20, Apply);
        _apply.Background.color = Selected;
    }

    public override void OnNormalEnable()
    {
        _slider.SetValueWithoutNotify(1);
        Revert(PrinterEditor.Applied);
    }

    private static Slider CreateProgress(Transform parent, float x, float y, float width)
    {
        var rail = Panel(parent, "PrinterProgressSlider", x, y, width, 16);
        rail.raycastTarget = true;
        var slider = rail.gameObject.AddComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 1;
        var handleArea = Rect(rail.transform, "HandleArea", 0, 0, width, 16);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(6, 2);
        handleArea.offsetMax = new Vector2(-6, -2);
        var handle = Panel(handleArea, "Handle", 0, 0, 8, 12);
        handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        handle.rectTransform.anchorMin = handle.rectTransform.anchorMax = Vector2.one * 0.5f;
        handle.rectTransform.sizeDelta = new Vector2(8, 0);
        handle.rectTransform.anchoredPosition = Vector2.zero;
        handle.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSlicedRoundWhite");
        handle.pixelsPerUnitMultiplier = 2;
        handle.color = new Color32(231, 83, 64, 255);
        handle.raycastTarget = true;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        return slider;
    }

    private void AddTiles(RectTransform content, Terrain layer, IEnumerable<TileTypeBase> tiles)
    {
        var page = Rect(content, "PrinterTiles_" + layer, 0, 0, 192, 0);
        _tilePages[layer] = page;
        int count = 0;
        foreach (var tile in tiles)
        {
            if (tile.sprites?.getVariation(0)?.sprite is not { } sprite) continue;
            var button = Button(page, "PrinterTile_" + tile.id, null, count % 8 * 24, count / 8 * 23, 22, 21, () =>
            {
                _draft = _draft with { Layer = layer, Tile = tile.id };
                RequestPreview();
            }, sprite, TileName(tile.id));
            _tileButtons[(layer, tile.id)] = button;
            count++;
        }
        page.sizeDelta = new Vector2(192, ((count + 7) / 8) * 23);
    }

    private static string TileName(string id) => LM.Has(id) ? LM.Get(id) : id;
    private static string PatternName(PrintTemplate pattern) =>
        LM.Has(Locale + "shape." + pattern.name) ? L("shape." + pattern.name) : pattern.name;

    private static Sprite PatternIcon(PrintTemplate pattern)
    {
        string suffix = pattern.name switch
        {
            "aliendrawing" => "AlienDrawing",
            "island1" => "Island",
            "starfort" => "StarFort",
            _ => char.ToUpperInvariant(pattern.name[0]) + pattern.name.Substring(1)
        };
        return WindowCreatureSavedList.Icon("iconPrinter" + suffix) ??
               SpriteTextureLoader.getSprite("inmny/godtools/icons/iconPrinterEditor");
    }

    private void SelectShape(PrintTemplate shape)
    {
        _shape = shape;
        _slider.SetValueWithoutNotify(1);
        RequestPreview();
    }

    private void Revert(Settings settings)
    {
        _draft = settings;
        _width.SetTextWithoutNotify(settings.Width.ToString("G", CultureInfo.CurrentCulture));
        _height.SetTextWithoutNotify(settings.Height.ToString("G", CultureInfo.CurrentCulture));
        RequestPreview();
    }

    private void RequestPreview() => _dirty = true;

    private void LateUpdate()
    {
        if (!_dirty || _apply == null) return;
        _dirty = false;
        RefreshPreview();
    }

    private bool ReadDraft()
    {
        if (!PrinterEditor.TryScale(_width.text, out float width) || !PrinterEditor.TryScale(_height.text, out float height))
            return false;
        _draft = _draft with { Width = width, Height = height };
        return true;
    }

    private void RefreshPreview()
    {
        foreach (var pair in _shapes) pair.Value.Background.color = pair.Key == _shape ? Selected : Color.white;
        foreach (var pair in _orders) pair.Value.Background.color = pair.Key == _draft.Direction ? Selected : Color.white;
        foreach (var pair in _tileTabs)
            pair.Value.Background.color = pair.Key == _tilePage || pair.Key == Terrain.Default && _draft.Layer == Terrain.Default
                ? Selected : Color.white;
        foreach (var pair in _tileButtons)
            pair.Value.Background.color = pair.Key == (_draft.Layer, _draft.Tile) ? Selected : Color.white;
        _patternName.text = _shape == null ? "" : PatternName(_shape);
        _material.text = string.Format(L("tile"), _draft.Layer == Terrain.Default ? L("default_tile") : TileName(_draft.Tile));
        _apply.Button.interactable = false;

        bool validInput = ReadDraft();
        if (!validInput || _shape == null || !PrinterEditor.CanApply(_draft))
        {
            _status.text = L(!validInput ? "invalid_scale" : _shape == null ? "no_patterns" : "too_big");
            _status.color = Gold;
            _image.enabled = _grid.enabled = _slider.interactable = false;
            _metrics.text = _progress.text = "";
            _firstStep = null;
            return;
        }

        var plan = PrinterEditor.BuildPlan(_shape, _draft);
        _status.color = Muted;
        _status.text = L(_draft == PrinterEditor.Applied ? "applied" : "draft");
        _apply.Button.interactable = _draft != PrinterEditor.Applied;
        SetPreviewPlan(plan);
    }

    private void Apply()
    {
        if (ReadDraft()) PrinterEditor.Apply(_draft);
        RefreshPreview();
    }

    private void SetPreviewPlan(PrintTemplate plan)
    {
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var step in plan.steps)
        {
            minX = Math.Min(minX, step.x);
            maxX = Math.Max(maxX, step.x);
            minY = Math.Min(minY, step.y);
            maxY = Math.Max(maxY, step.y);
        }
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        _firstStep = new int[width * height];
        _stepCount = plan.steps.Length;
        int occupied = 0;
        for (int index = 0; index < _stepCount; index++)
        {
            var step = plan.steps[index];
            int cell = step.x - minX + (step.y - minY) * width;
            if (_firstStep[cell] != 0) continue;
            _firstStep[cell] = index + 1;
            occupied++;
        }
        _metrics.text = occupied == 0 ? L("empty_shape") : string.Format(L("metrics"), width, height, occupied);
        if (_texture == null || _texture.width != width || _texture.height != height)
        {
            if (_texture != null) Destroy(_texture);
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _image.texture = _texture;
            _pixels = new Color32[width * height];
        }
        float scale = PreviewSize / (float)Math.Max(width, height);
        float screenWidth = width * scale;
        float screenHeight = height * scale;
        Place(_image.rectTransform, (161 - screenWidth) / 2, (161 - screenHeight) / 2, screenWidth, screenHeight);
        _grid.Configure(width, height, -minX, -minY);
        _grid.enabled = _image.enabled = true;
        _slider.interactable = _stepCount > 0;
        DrawProgress();
    }

    private void DrawProgress()
    {
        if (_firstStep == null || _texture == null) return;
        int visibleSteps = Mathf.RoundToInt(_slider.value * _stepCount);
        Color32 shade = PrinterEditor.GetTile(_draft.Layer, _draft.Tile)?.color ?? Ink;
        if (shade.a == 0) shade = Ink;
        for (int i = 0; i < _firstStep.Length; i++)
            _pixels[i] = _firstStep[i] == 0 ? Paper : _firstStep[i] <= visibleSteps ? shade : Pending;
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        _progress.text = Mathf.RoundToInt(_slider.value * 100) + "%";
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }
}

internal sealed class PrinterPreviewGrid : MaskableGraphic
{
    private int _columns, _rows, _originX, _originY;

    internal void Configure(int columns, int rows, int originX, int originY)
    {
        _columns = columns;
        _rows = rows;
        _originX = originX;
        _originY = originY;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (_columns == 0 || _rows == 0) return;
        Rect area = rectTransform.rect;
        float xStep = area.width / _columns;
        float yStep = area.height / _rows;
        var gridColor = new Color32(204, 220, 191, 32);
        if (Mathf.Min(xStep, yStep) >= 2)
        {
            for (int x = 1; x < _columns; x++) Line(area.xMin + x * xStep, area.yMin, 0.35f, area.height, gridColor);
            for (int y = 1; y < _rows; y++) Line(area.xMin, area.yMin + y * yStep, area.width, 0.35f, gridColor);
        }
        float centerX = area.xMin + (_originX + 0.5f) * xStep;
        float centerY = area.yMin + (_originY + 0.5f) * yStep;
        var outline = new Color32(37, 34, 25, 255);
        Line(centerX - 3.5f, centerY - 1, 7, 2, outline);
        Line(centerX - 1, centerY - 3.5f, 2, 7, outline);
        var marker = new Color32(255, 221, 145, 255);
        Line(centerX - 3, centerY - 0.5f, 6, 1, marker);
        Line(centerX - 0.5f, centerY - 3, 1, 6, marker);

        void Line(float x, float y, float width, float height, Color32 color)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, Vector2.zero);
            mesh.AddVert(new Vector3(x, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y + height), color, Vector2.zero);
            mesh.AddVert(new Vector3(x + width, y), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
