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
    private const float DistributionWidth = 164;
    private const float DistributionHeight = 238;
    private const float DistributionGap = 8;
    private const float DistributionInset = 11;
    private const int DistributionBins = 8;
    private const float DistributionContentWidth = DistributionWidth - 2 * DistributionInset;

    private RectTransform _distribution;
    private RectTransform _distributionDock;
    private RectTransform _distributionCanvas;
    private RectTransform _windowToolbar;
    private List<RankingRow> _distributionRows;
    private bool _distributionOpen = true;

    private void InitDistribution()
    {
        var window = (RectTransform)transform;
        // Fit the pair without taking over ScrollWindow's local movement animations.
        _distributionDock = Rect("RankingWindowDock", window.parent, 0, 0, 0, 0);
        _distributionDock.anchorMin = _distributionDock.anchorMax = _distributionDock.pivot = new Vector2(0.5f, 0.5f);
        _distributionDock.SetSiblingIndex(window.GetSiblingIndex());
        window.SetParent(_distributionDock, false);
        _distributionCanvas = (RectTransform)CanvasMain.instance.canvas_windows.transform;
        _windowToolbar = _distributionCanvas.Find("WindowToolbarContainer/WindowToolbar") as RectTransform;

        _distribution = Rect("RankingDistribution", BackgroundTransform, DistributionWidth, DistributionHeight, DistributionGap, 116);
        _distribution.anchorMin = _distribution.anchorMax = new Vector2(1, 0.5f);
        _distribution.anchoredPosition = new Vector2(DistributionGap, 116);
        var frame = BackgroundTransform.GetComponent<Image>();
        var image = _distribution.gameObject.AddComponent<Image>();
        image.sprite = frame.sprite;
        image.type = frame.type;
        image.pixelsPerUnitMultiplier = frame.pixelsPerUnitMultiplier;
        image.color = frame.color;
        _distribution.gameObject.SetActive(false);
    }

    private void ToggleDistribution()
    {
        _distributionOpen = !_distributionOpen;
        Clear(_tools);
        DrawToolbar();
        SyncDistribution();
    }

    private void SyncDistribution()
    {
        bool visible = _page == Page.Table && _distributionOpen;
        _distribution.gameObject.SetActive(visible);
        if (visible)
        {
            FitDistribution();
            if (ReferenceEquals(_distributionRows, _rows)) return;
            _distributionRows = _rows;
            DrawDistribution();
        }
        else
        {
            _distributionDock.localScale = Vector3.one;
            _distributionDock.anchoredPosition = Vector2.zero;
        }
    }

    private void LateUpdate()
    {
        if (_fallbackRevision != RankingRegistry.FallbackRevision && ScrollWindow.isCurrentWindow("WindowRankings"))
            OnNormalEnable();
        if (_distribution != null && _distribution.gameObject.activeSelf) FitDistribution();
    }

    private void FitDistribution()
    {
        float unit = Mathf.Abs(_distributionDock.parent.lossyScale.x);
        if (unit <= 0 || _distributionCanvas.rect.width <= 0) return;
        float width = _distributionCanvas.rect.width * Mathf.Abs(_distributionCanvas.lossyScale.x) / unit;
        float tools = _windowToolbar != null && _windowToolbar.gameObject.activeInHierarchy
            ? _windowToolbar.rect.width * Mathf.Abs(_windowToolbar.lossyScale.x) / unit : 0;
        float windowUnit = Mathf.Abs(transform.localScale.x * BackgroundTransform.localScale.x);
        float extra = (DistributionWidth + DistributionGap) * windowUnit;
        float total = ((RectTransform)BackgroundTransform).rect.width * windowUnit + extra;
        if (total <= 0) return;
        const float margin = 8;
        float fit = Mathf.Min(1, Mathf.Max(1, width - tools - 2 * margin) / total);
        var scale = Vector3.one * fit;
        var position = new Vector2((tools - extra * fit) * 0.5f, 0);
        if (_distributionDock.localScale != scale) _distributionDock.localScale = scale;
        if (_distributionDock.anchoredPosition != position) _distributionDock.anchoredPosition = position;
    }

    private void DrawDistribution()
    {
        Clear(_distribution);
        var header = StackLine("DistributionHeader", _distribution, -10, DistributionContentWidth, 20);
        header.anchoredPosition = new Vector2(DistributionInset, -10);
        var heading = Label(header, L("chart"), DistributionContentWidth - 28, 20, new Color32(246, 219, 160, 255));
        heading.fontSize = 12;
        Button(header, "×", 22, ToggleDistribution, hint: L("chart_hide"));
        ChartBand("DistributionDivider", 10, 33, DistributionWidth - 20, 1, new Color32(129, 135, 107, 115));

        string id = _view.Snapshot?.Primary ?? _view.Sort.FirstOrDefault()?.Field;
        var sample = _view.Snapshot;
        if (id == null || sample?.Data.Fields.TryGetValue(id, out var field) != true)
        {
            _distribution.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 83);
            ChartText(L("chart_pick_key"), 45, height: 27);
            return;
        }
        ChartText(FieldTitle(field), 38, fontSize: 11);
        var values = sample.Visible.Select(row => row.Values.GetValueOrDefault(id)).Where(value => value != null).ToArray();
        var bars = field.Numeric ? NumberBars(values)
            : field.Ordered ? OrderedBars(values, field) : CategoryBars(values, field);
        if (values.Length == 0)
        {
            _distribution.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 97);
            ChartText(_rows.Count == 0 ? L("empty") : L("chart_no_values"), 61, height: 25);
            return;
        }

        _distribution.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 86 + 19 * bars.Count);
        int count = bars.Sum(bar => bar.Count);
        string coverage = count == _rows.Count ? string.Format(L("chart_samples"), count)
            : string.Format(L("chart_coverage"), count, _rows.Count);
        ChartText(coverage, 55, fontSize: 8, color: new Color32(196, 199, 173, 255));
        int highest = bars.Max(bar => bar.Count);
        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            float y = 73 + 19 * i;
            var target = ChartBand("DistributionBucket", DistributionInset, y, DistributionContentWidth, 18, new Color(1, 1, 1, 0.008f));
            target.raycastTarget = true;
            var tip = target.gameObject.AddComponent<TipButton>();
            tip.showOnClick = false;
            string detail = $"{bar.Label} · {bar.Count} ({bar.Count / (double)count:P1})";
            tip.setHoverAction(() => Tooltip.show(tip.gameObject, "normal", new TooltipData { tip_name = detail }), false);
            ChartText(bar.Label, y, width: DistributionContentWidth - 32, fontSize: 9);
            ChartText(bar.Count.ToString(), y, x: DistributionWidth - DistributionInset - 31, width: 31,
                align: TextAnchor.MiddleRight, fontSize: 9);
            ChartBand("DistributionTrack", DistributionInset, y + 13, DistributionContentWidth, 4, new Color32(40, 44, 36, 255));
            if (bar.Count > 0)
                ChartBand("DistributionBar", DistributionInset, y + 13, Math.Max(1, DistributionContentWidth * bar.Count / highest),
                    4, new Color32(151, 169, 106, 255));
        }
    }

    private void ChartText(string text, float y, float x = DistributionInset, float width = DistributionContentWidth,
        float height = 14, int fontSize = 9, TextAnchor align = TextAnchor.MiddleLeft, Color color = default)
    {
        var label = Label(_distribution, text, width, height, color, align);
        Place(label.rectTransform, _distribution, width, height, x, -y);
        label.fontSize = fontSize;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 7;
        label.resizeTextMaxSize = fontSize;
    }

    private Image ChartBand(string name, float x, float y, float width, float height, Color color)
    {
        var image = Rect(name, _distribution, width, height, x, -y).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static List<(string Label, int Count)> NumberBars(IEnumerable<RankingValue> values)
    {
        var groups = values.Where(value => value.Number.HasValue).GroupBy(value => value.Number.Value)
            .ToDictionary(group => group.Key, group => group.Count());
        if (groups.Count <= DistributionBins)
            return groups.OrderBy(group => group.Key).Select(group => (ChartNumber(group.Key), group.Value)).ToList();

        var edges = NumberEdges(groups.Keys.Min(), groups.Keys.Max());
        var counts = new int[edges.Length - 1];
        foreach (var (number, count) in groups)
        {
            int index = Array.BinarySearch(edges, number);
            if (index < 0) index = ~index - 1;
            counts[Math.Min(counts.Length - 1, Math.Max(0, index))] += count;
        }

        var bars = new List<(string Label, int Count)>(counts.Length);
        for (int i = 0; i < counts.Length; i++)
            bars.Add(($"{ChartNumber(edges[i])} – {ChartNumber(edges[i + 1])}", counts[i]));
        return bars;
    }

    private static double[] NumberEdges(double min, double max)
    {
        double target = (max - min) / DistributionBins;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        foreach (double multiple in new[] { 1d, 2d, 2.5d, 5d, 10d, 20d })
        {
            double step = magnitude * multiple;
            if (step <= 0 || step < target || double.IsInfinity(step)) continue;
            double first = Math.Floor(min / step) * step;
            double count = Math.Ceiling((max - first) / step);
            if (!(count >= 1 && count <= DistributionBins && first <= min)) continue;
            var edges = Enumerable.Range(0, (int)count + 1).Select(i => first + i * step).ToArray();
            if (edges[^1] >= max && !double.IsInfinity(edges[^1]) && edges[1] > edges[0]) return edges;
        }

        // An extreme span can overflow even though both endpoints are finite.
        var fallback = new double[DistributionBins + 1];
        fallback[0] = min;
        fallback[DistributionBins] = max;
        for (int i = 1; i < DistributionBins; i++)
        {
            double weight = i / (double)DistributionBins;
            fallback[i] = min * (1 - weight) + max * weight;
        }
        return fallback;
    }

    private static List<(string Label, int Count)> OrderedBars(IEnumerable<RankingValue> values,
        RankingRegistry.Field field)
    {
        var counts = values.GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var groups = field.Options.Count == 0
            ? values.GroupBy(value => value.Key, StringComparer.Ordinal).Select(group => group.First())
                .Select(value => (Order: value.Number.Value, Key: value.Key, Label: field.ValueLabel(value), Count: counts[value.Key]))
            : field.Options.Values.GroupBy(option => (Banded: option.Band != null, ID: option.Band?.ID ?? option.ID))
                .Select(group =>
                {
                    var option = group.First();
                    return (Order: option.Band?.Order ?? option.Order, Key: group.Key.ID,
                        Label: option.Band != null ? Name(option.Band.Name) : option.LocalizedName(),
                        Count: group.Sum(item => counts.GetValueOrDefault(item.ID)));
                });
        var ordered = groups.OrderBy(group => group.Order).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
        int bins = Math.Min(ordered.Length, DistributionBins);
        var bars = new List<(string Label, int Count)>(bins);
        for (int i = 0; i < bins; i++)
        {
            int first = i * ordered.Length / bins;
            int end = (i + 1) * ordered.Length / bins;
            string label = end - first == 1 ? ordered[first].Label : $"{ordered[first].Label} – {ordered[end - 1].Label}";
            int count = 0;
            for (int j = first; j < end; j++) count += ordered[j].Count;
            bars.Add((label, count));
        }
        return bars;
    }

    private static List<(string Label, int Count)> CategoryBars(IEnumerable<RankingValue> values,
        RankingRegistry.Field field)
    {
        var groups = values.GroupBy(value => value.Key, StringComparer.Ordinal)
            .Select(group => (Key: group.Key, Label: field.ValueLabel(group.First()), Count: group.Count()))
            .OrderByDescending(group => group.Count).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (group.Label, group.Count)).ToArray();
        if (groups.Length <= DistributionBins) return groups.ToList();
        var bars = groups.Take(DistributionBins - 1).ToList();
        bars.Add((L("other"), groups.Skip(DistributionBins - 1).Sum(group => group.Count)));
        return bars;
    }

    private static string ChartNumber(double value) => value.ToString("G5", CultureInfo.CurrentCulture);
}
