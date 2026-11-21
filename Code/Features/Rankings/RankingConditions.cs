using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GodTools.Features.Rankings;

public enum RankingOperator { Equal, AtLeast, AtMost, Contains, Present, Absent }
public enum RankingSymbol { Value, And, Or, Not, Open, Close }

public sealed class RankingCondition
{
    public string Field;
    public RankingOperator Operator;
    public string Value;
    public string Label;
    // Legacy ordered presets store a numeric cutoff. New predicates store a stable choice ID.
    public bool ChoiceKey;
    public double? OrderHint;
}

public sealed class RankingToken
{
    public RankingSymbol Symbol;
    public RankingCondition Condition;
}

internal static class RankingConditions
{
    internal static bool TryCompile(IReadOnlyList<RankingToken> tokens, RankingRegistry.Source source,
        out Func<IReadOnlyDictionary<string, RankingValue>, bool> matches, out string invalidField,
        Func<string, string, double?> choiceOrder = null)
    {
        invalidField = null;
        matches = null;
        if (tokens == null || tokens.Count > 96 || tokens.Any(token => token == null)) return false;
        if (tokens.Count == 0) { matches = _ => true; return true; }
        var leaves = new Dictionary<RankingToken, Func<IReadOnlyDictionary<string, RankingValue>, bool>>();
        foreach (var token in tokens.Where(item => item.Symbol == RankingSymbol.Value))
        {
            var condition = token.Condition;
            if (condition == null || string.IsNullOrWhiteSpace(condition.Field)) return false;
            if (!source.Fields.TryGetValue(condition.Field, out var field))
            {
                invalidField = condition.Field;
                return false;
            }
            if (!field.Operators.Contains(condition.Operator)) return false;
            if (condition.Operator is not (RankingOperator.Present or RankingOperator.Absent) &&
                string.IsNullOrEmpty(condition.Value)) return false;
            double number = 0;
            long? whole = null;
            bool range = condition.Operator is RankingOperator.AtLeast or RankingOperator.AtMost;
            if (range && field.Ordered && condition.ChoiceKey)
            {
                if (field.Options.TryGetValue(condition.Value, out var option)) number = option.Order;
                else if (field.Options.Count == 0 &&
                    (choiceOrder?.Invoke(field.ID, condition.Value) ?? condition.OrderHint) is double order &&
                    !double.IsNaN(order) && !double.IsInfinity(order)) number = order;
                else
                {
                    invalidField = field.ID;
                    return false;
                }
            }
            else if (range || condition.Operator == RankingOperator.Equal && field.Numeric)
            {
                if (field.Kind == RankingFieldKind.Integer &&
                    long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long exact))
                {
                    whole = exact;
                    number = exact;
                }
                else if (!double.TryParse(condition.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                    double.IsNaN(number) || double.IsInfinity(number) ||
                    field.Kind == RankingFieldKind.Integer && number != Math.Truncate(number)) return false;
            }
            leaves[token] = values => Match(values.GetValueOrDefault(condition.Field), field, condition, number, whole);
        }
        int cursor = 0;
        matches = Group(RankingSymbol.Or);
        return matches != null && cursor == tokens.Count;

        Func<IReadOnlyDictionary<string, RankingValue>, bool> Group(RankingSymbol symbol)
        {
            bool any = symbol == RankingSymbol.Or;
            var first = any ? Group(RankingSymbol.And) : Operand();
            if (first == null || !Take(symbol)) return first;
            var children = new List<Func<IReadOnlyDictionary<string, RankingValue>, bool>> { first };
            do
            {
                var item = any ? Group(RankingSymbol.And) : Operand();
                if (item == null) return null;
                children.Add(item);
            } while (Take(symbol));
            return values => any ? children.Any(item => item(values)) : children.All(item => item(values));
        }

        Func<IReadOnlyDictionary<string, RankingValue>, bool> Operand()
        {
            bool negate = false;
            while (Take(RankingSymbol.Not)) negate = !negate;
            Func<IReadOnlyDictionary<string, RankingValue>, bool> operand;
            if (Take(RankingSymbol.Open))
            {
                operand = Group(RankingSymbol.Or);
                if (operand == null || !Take(RankingSymbol.Close)) return null;
            }
            else
            {
                if (!Take(RankingSymbol.Value)) return null;
                operand = leaves[tokens[cursor - 1]];
            }
            return negate ? values => !operand(values) : operand;
        }

        bool Take(RankingSymbol symbol)
        {
            if (cursor >= tokens.Count || tokens[cursor].Symbol != symbol) return false;
            cursor++;
            return true;
        }
    }

    private static bool Match(RankingValue value, RankingRegistry.Field field, RankingCondition test, double number, long? whole) =>
        test.Operator switch
        {
            RankingOperator.Present => field.HasValue(value),
            RankingOperator.Absent => !field.HasValue(value),
            RankingOperator.AtLeast => whole.HasValue && value?.ExactInteger is long lower
                ? lower >= whole.Value : value?.Number >= number,
            RankingOperator.AtMost => whole.HasValue && value?.ExactInteger is long upper
                ? upper <= whole.Value : value?.Number <= number,
            RankingOperator.Contains => value?.Label?.IndexOf(test.Value, StringComparison.OrdinalIgnoreCase) >= 0,
            RankingOperator.Equal when field.Numeric => whole.HasValue && value?.ExactInteger is long exact
                ? exact == whole.Value : value?.Number == number,
            RankingOperator.Equal when field.Kind == RankingFieldKind.Tags => value?.Tags?.ContainsKey(test.Value) == true,
            RankingOperator.Equal when field.IsChoice => string.Equals(value?.Key, test.Value, StringComparison.Ordinal),
            RankingOperator.Equal => string.Equals(value?.Key, test.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
