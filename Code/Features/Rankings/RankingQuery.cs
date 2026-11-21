using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features.Rankings;

public sealed class RankingSort
{
    public string Field;
    public bool Ascending;
}

[JsonObject(MemberSerialization.OptIn)]
internal sealed class RankingRow
{
    [JsonProperty] internal string Key;
    [JsonProperty] internal string Name;
    internal Sprite Icon;
    internal Sprite SavedPortrait;
    internal RankingPeak Peak;
    [JsonProperty] internal int Rank;
    [JsonProperty] internal Dictionary<string, RankingValue> Values;

    internal RankingRow CopyForRanking() => new()
    {
        Key = Key, Name = Name, Icon = Icon, SavedPortrait = SavedPortrait, Peak = Peak, Values = Values
    };
}

internal sealed class RankingDataset
{
    internal RankingRegistry.Source Source;
    internal Dictionary<string, RankingRegistry.Field> Fields;
    internal List<RankingRow> Rows;
    internal DateTime RefreshedAt;
    internal double WorldTime;

    internal bool Covers(RankingRegistry.Source source, IEnumerable<string> keys) =>
        Source == source && keys.All(key => Fields.TryGetValue(key, out var field) && source.Fields.GetValueOrDefault(key) == field);

    internal double? OrderOf(string field, string key)
    {
        foreach (var row in Rows)
        {
            var value = row.Values.GetValueOrDefault(field);
            if (value?.Key == key) return value.Number;
        }
        return null;
    }
}

internal sealed class RankingSnapshot
{
    internal RankingPreset View;
    internal RankingDataset Data;
    internal List<RankingRow> Visible;
    internal int ScopeCount;
    internal int RankedCount;
    internal string Primary => View.Sort.FirstOrDefault()?.Field;

    internal static bool SameSettings(RankingPreset left, RankingPreset right, bool includeColumns = false) =>
        left != null && right != null && left.BoardID == right.BoardID &&
        left.Sort.Select(key => (key.Field, key.Ascending)).SequenceEqual(right.Sort.Select(key => (key.Field, key.Ascending))) &&
        Predicates(left.Conditions).SequenceEqual(Predicates(right.Conditions)) &&
        Predicates(left.DisplayConditions).SequenceEqual(Predicates(right.DisplayConditions)) &&
        (!includeColumns || left.PinnedColumns.SequenceEqual(right.PinnedColumns) && RankingRowLayout.Same(left.RowLayout, right.RowLayout));

    private static IEnumerable<(RankingSymbol, string, RankingOperator?, string, bool, double?)> Predicates(
        IEnumerable<RankingToken> tokens) =>
        tokens.Select(token => (token.Symbol, token.Condition?.Field, token.Condition?.Operator, token.Condition?.Value,
            token.Condition?.ChoiceKey == true, token.Condition?.OrderHint));
}

internal static class RankingQuery
{
    internal static IEnumerable<string> RuleFields(RankingPreset view) =>
        view.Sort.Select(key => key.Field).Concat(ConditionFields(view.Conditions)).Concat(ConditionFields(view.DisplayConditions)).Where(id => id != null).Distinct();

    private static IEnumerable<string> ConditionFields(IEnumerable<RankingToken> conditions) =>
        conditions.Where(token => token?.Symbol == RankingSymbol.Value).Select(token => token.Condition?.Field);

    internal static RankingDataset Capture(RankingRegistry.Source source, IEnumerable<string> keys)
    {
        var fields = keys.Where(field => field != null).Distinct().Select(id => source.Fields.GetValueOrDefault(id))
            .Where(field => field != null).ToDictionary(field => field.ID, StringComparer.Ordinal);
        var rows = ReadRows(source, fields.Values);
        return new RankingDataset
        {
            Source = source, Fields = fields, RefreshedAt = DateTime.UtcNow, WorldTime = World.world.getCurWorldTime(),
            Rows = rows
        };
    }

    internal static List<RankingRow> Read(RankingRegistry.Source source, IEnumerable<string> keys) =>
        ReadRows(source, keys.Where(key => key != null).Distinct().Select(key => source.Fields.GetValueOrDefault(key))
            .Where(field => field != null).ToArray());

    private static List<RankingRow> ReadRows(RankingRegistry.Source source, IReadOnlyCollection<RankingRegistry.Field> fields)
    {
        var rows = new List<RankingRow>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source.Read())
        {
            if (item == null) continue;
            string key = source.Key(item);
            if (string.IsNullOrWhiteSpace(key) || !keys.Add(key)) continue;
            var values = new Dictionary<string, RankingValue>(fields.Count, StringComparer.Ordinal);
            foreach (var field in fields)
                values[field.ID] = field.Normalize(field.Read(item));
            rows.Add(new RankingRow
            {
                Key = key, Name = source.Name(item) ?? key, Icon = source.Icon(item),
                SavedPortrait = source.SavedPortrait?.Invoke(item), Peak = item as RankingPeak, Values = values
            });
        }
        return rows;
    }

    internal static bool TryEvaluate(RankingDataset data, RankingPreset view, out RankingSnapshot snapshot, out string invalid)
    {
        snapshot = null;
        if (!RankingConditions.TryCompile(view.Conditions, data.Source, out var scope, out invalid, data.OrderOf) ||
            !RankingConditions.TryCompile(view.DisplayConditions, data.Source, out var display, out invalid, data.OrderOf)) return false;
        var scopeRows = data.Rows.Where(row => scope(row.Values)).Select(row => row.CopyForRanking()).ToList();
        Sort(scopeRows, view.Sort, data.Source);
        snapshot = new RankingSnapshot
        {
            View = view, Data = data, ScopeCount = scopeRows.Count, RankedCount = scopeRows.Count(row => row.Rank > 0),
            Visible = scopeRows.Where(row => display(row.Values)).ToList()
        };
        return true;
    }

    private static void Sort(List<RankingRow> rows, IReadOnlyList<RankingSort> keys, RankingRegistry.Source source)
    {
        var rules = keys.Select(key => (Field: source.Fields[key.Field], key.Ascending)).ToArray();
        string primary = rules.Length > 0 ? rules[0].Field.ID : null;
        rows.Sort((left, right) =>
        {
            int order = Compare(left, right);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Key, right.Key);
        });
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (primary == null || row.Values.GetValueOrDefault(primary) == null) break;
            row.Rank = i == 0 || Compare(rows[i - 1], row) != 0 ? i + 1 : rows[i - 1].Rank;
        }

        int Compare(RankingRow left, RankingRow right)
        {
            foreach (var rule in rules)
            {
                var a = left.Values.GetValueOrDefault(rule.Field.ID);
                var b = right.Values.GetValueOrDefault(rule.Field.ID);
                if (a == null || b == null)
                {
                    if (a != b) return a == null ? 1 : -1;
                    continue;
                }
                int order = rule.Field.Compare(a, b);
                if (order != 0) return rule.Ascending ? Math.Sign(order) : -Math.Sign(order);
            }
            return rules.Length == 0 ? StringComparer.CurrentCulture.Compare(left.Name, right.Name) : 0;
        }
    }
}
