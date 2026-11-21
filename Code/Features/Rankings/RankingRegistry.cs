using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NeoModLoader;
using NeoModLoader.api;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features.Rankings;

public enum RankingFieldKind { Number, Text, Choice, Tags, Integer, Ordinal, Category }

/// <summary>A stable option ID, localizable name and optional ordinal position.</summary>
public sealed class RankingOption(string id, string name, double order = 0)
{
    public string ID { get; } = id;
    public string Name { get; } = name;
    public double Order { get; } = order;
    public Func<string> FormatName { get; }
    public RankingDistributionBand Band { get; }

    public RankingOption(string id, string name, double order, Func<string> formatName, RankingDistributionBand band = null)
        : this(id, name, order)
    {
        FormatName = formatName;
        Band = band;
    }

    internal string LocalizedName() => FormatName?.Invoke() ?? BuiltInRankings.Label(Name, Name);
}

/// <summary>Combines several ordinal options in the distribution without changing their comparison.</summary>
public sealed class RankingDistributionBand(string id, string name, double order)
{
    public string ID { get; } = id;
    public string Name { get; } = name;
    public double Order { get; } = order;
}

public sealed class RankingValue
{
    [JsonProperty]
    public double? Number { get; private set; }
    [JsonProperty]
    public string Key { get; private set; }
    [JsonProperty]
    public string Label { get; private set; }
    [JsonProperty]
    public IReadOnlyDictionary<string, string> Tags { get; private set; }
    [JsonProperty]
    public string Details { get; private set; }
    [JsonProperty]
    public IReadOnlyDictionary<string, double> Parts { get; private set; }

    [JsonProperty]
    public double? ProgressRatio { get; private set; }
    [JsonProperty]
    public string CellIconPath { get; private set; }
    [JsonProperty]
    internal double? SortProgress { get; private set; }
    [JsonProperty]
    internal long? ExactInteger { get; private set; }

    public static RankingValue Numeric(double value, string label = null, string details = null) =>
        double.IsNaN(value) || double.IsInfinity(value) ? null : new RankingValue
        {
            Number = value, Key = value.ToString("R", CultureInfo.InvariantCulture),
            Label = label ?? FormatNumber(value), Details = details
        };

    public static RankingValue Integer(int value, string label = null) => Numeric(value, label);

    public static RankingValue Whole(long value, string label = null) => new()
    {
        Number = value, ExactInteger = value, Key = value.ToString(CultureInfo.InvariantCulture),
        Label = label ?? value.ToString(CultureInfo.CurrentCulture)
    };

    /// <summary>Displays a meter while sorting and filtering on the raw value.</summary>
    public static RankingValue Progress(double value, double maximum, string label = null)
    {
        if (!(maximum > 0) || double.IsInfinity(maximum)) return Numeric(value, label);
        var result = Numeric(value, label ?? $"{FormatNumber(value)} / {FormatNumber(maximum)}");
        if (result != null) result.ProgressRatio = Math.Clamp(value / maximum, 0, 1);
        return result;
    }

    public static RankingValue WithIcon(string key, string label, string iconPath)
    {
        var result = Choice(key, label);
        if (result != null) result.CellIconPath = iconPath;
        return result;
    }

    /// <summary>The optional progress orders entries in the same stage without changing range filters or chart groups.</summary>
    public static RankingValue OrderedChoice(string key, string label, double order, double? progress = null)
    {
        var result = Choice(key, label, order);
        if (result != null && progress is double amount && !double.IsNaN(amount) && !double.IsInfinity(amount))
            result.SortProgress = amount;
        return result;
    }

    internal RankingValue WithChoice(string label, double? order) => new()
    {
        Key = Key, Label = label, Number = order, Details = Details, Parts = Parts,
        ProgressRatio = ProgressRatio, CellIconPath = CellIconPath, SortProgress = order.HasValue ? SortProgress : null
    };

    public static RankingValue Text(string text) => text == null ? null : Choice(text, text);

    public static RankingValue Choice(string key, string label, double? order = null) =>
        key == null ? null : new RankingValue
        {
            Key = key, Label = label ?? key,
            Number = order is double value && !double.IsNaN(value) && !double.IsInfinity(value) ? value : null
        };

    public static RankingValue TagSet(IReadOnlyDictionary<string, string> tags) =>
        tags == null ? null : new RankingValue
            { Tags = tags.ToDictionary(item => item.Key, item => item.Value), Label = string.Join("、", tags.Values) };

    internal static RankingValue Power(double score, IReadOnlyDictionary<string, double> parts,
        string label = null, string details = null)
    {
        var value = Numeric(score, label, details);
        if (value != null && parts != null)
            value.Parts = parts.Where(part => !double.IsNaN(part.Value) && !double.IsInfinity(part.Value))
                .ToDictionary(part => part.Key, part => part.Value);
        return value;
    }

    internal static string FormatNumber(double value) => Math.Abs(value) >= 1e7 ||
        Math.Abs(value) is > 0 and < 0.001 ? value.ToString("0.##e+0") : value.ToString("0.##");
}

public sealed class RankingSource<T>
{
    public string ID { get; set; }
    public Func<IEnumerable<T>> Read { get; set; }
    public Func<T, string> Key { get; set; }
    public Func<T, string> Name { get; set; }
    public Func<T, Sprite> Icon { get; set; }
    internal Func<T, Sprite> SavedPortrait { get; set; }
    public Func<string, Actor> ActorPortrait { get; set; }
    public Func<string, NanoObject> MetaPortrait { get; set; }
    public Action<string> Inspect { get; set; }
    public Func<string, bool> CanInspect { get; set; }
    public Func<string, WorldTile> Locate { get; set; }
    public Action<string> Place { get; set; }
    public ModDeclare Owner { get; set; }
}

public sealed class RankingField<T>
{
    public string ID { get; set; }
    public string Name { get; set; }
    public RankingFieldKind Kind { get; set; }
    public string IconPath { get; set; }
    public string Unit { get; set; }
    public string Description { get; set; }
    /// <summary>For Choice fields, use the numeric order for range selection and the distribution.</summary>
    public bool Ordered { get; set; }
    /// <summary>Stable choices, including options currently absent from the world.</summary>
    public IReadOnlyList<RankingOption> Options { get; set; }
    /// <summary>Restrict the operators offered by the field kind. Null uses the kind's defaults.</summary>
    public IReadOnlyList<RankingOperator> Operators { get; set; }
    public bool Sortable { get; set; } = true;
    /// <summary>Increase when a field's meaning or comparison changes.</summary>
    public int Version { get; set; } = 1;
    public MetaType? AffiliationType { get; set; }
    public Func<T, RankingValue> Read { get; set; }
    public ModDeclare Owner { get; set; }
}

public static class RankingGroupIds
{
    public const string CharactersAndEquipment = "godtools:characters-equipment";
    public const string Powers = "godtools:powers";
    public const string Communities = "godtools:communities";
    public const string Conflicts = "godtools:conflicts";
    public const string Archives = "godtools:archives";
    public const string Cultiway = "cultiway:rankings";
    public const string ModRankings = "godtools:mods";
}

public sealed class RankingGroup
{
    public string ID { get; set; }
    public string Name { get; set; }
    public int Order { get; set; } = 1000;
    public ModDeclare Owner { get; set; }
}

public sealed class RankingRowLayout
{
    public bool ShowPortrait { get; set; } = true;
    public bool ShowRank { get; set; } = true;
    public bool FillColumns { get; set; } = true;

    internal RankingRowLayout Copy() => new() { ShowPortrait = ShowPortrait, ShowRank = ShowRank, FillColumns = FillColumns };

    internal static bool Same(RankingRowLayout left, RankingRowLayout right) =>
        (left?.ShowPortrait ?? true) == (right?.ShowPortrait ?? true) &&
        (left?.ShowRank ?? true) == (right?.ShowRank ?? true) &&
        (left?.FillColumns ?? true) == (right?.FillColumns ?? true);
}

public sealed class RankingBoard
{
    public string ID { get; set; }
    public string SourceID { get; set; }
    public string Name { get; set; }
    public string IconPath { get; set; }
    public string GroupID { get; set; }
    public int Order { get; set; } = int.MaxValue;
    public string Category { get; set; }
    public string[] Columns { get; set; } = Array.Empty<string>();
    public RankingRowLayout RowLayout { get; set; } = new();
    public string DefaultSort { get; set; }
    public bool Ascending { get; set; }
    public Func<bool> Available { get; set; }
    public ModDeclare Owner { get; set; }
}

public static class RankingRegistry
{
    private const string IntegrationSlot = "GodTools.Rankings.Registry/v1";

    internal sealed class Source
    {
        internal Func<IEnumerable<object>> Read;
        internal Func<object, string> Key;
        internal Func<object, string> Name;
        internal Func<object, Sprite> Icon;
        internal Func<object, Sprite> SavedPortrait;
        internal Func<string, Actor> ActorPortrait;
        internal Func<string, NanoObject> MetaPortrait;
        internal Action<string> Inspect;
        internal Func<string, bool> CanInspect;
        internal Func<string, WorldTile> Locate;
        internal Action<string> Place;
        internal Type Type;
        internal readonly Dictionary<string, Field> Fields = new(StringComparer.Ordinal);
    }

    internal sealed class Field
    {
        internal string ID;
        internal string Name;
        internal RankingFieldKind Kind;
        internal string IconPath;
        internal string Unit;
        internal string Description;
        internal bool Ordered;
        internal bool Sortable;
        internal int Version;
        internal IReadOnlyDictionary<string, RankingOption> Options;
        internal IReadOnlyList<RankingOperator> Operators;
        internal bool Numeric => Kind is RankingFieldKind.Number or RankingFieldKind.Integer;
        internal bool IsChoice => Kind is RankingFieldKind.Choice or RankingFieldKind.Category or RankingFieldKind.Ordinal;
        internal bool HasValue(RankingValue value) => value != null && (Kind != RankingFieldKind.Tags || value.Tags?.Count > 0);

        internal RankingValue Normalize(RankingValue value)
        {
            if (!HasValue(value)) return null;
            if (Numeric) return value.Number is double n && (Kind != RankingFieldKind.Integer || n == Math.Truncate(n)) ? value : null;
            if (Kind == RankingFieldKind.Tags) return value;
            if (string.IsNullOrEmpty(value.Key)) return null;
            if (!IsChoice) return value;
            if (Options.TryGetValue(value.Key, out var option))
                return value.WithChoice(option.LocalizedName(), Ordered ? option.Order : null);
            if (Options.Count > 0 || Ordered && value.Number == null) return null;
            return Kind == RankingFieldKind.Category ? value.WithChoice(value.Label, null) : value;
        }

        internal string ValueLabel(RankingValue value)
        {
            if (value == null) return "—";
            if (value.Key != null && Options.TryGetValue(value.Key, out var option))
                return option.LocalizedName();
            return IsChoice ? BuiltInRankings.Label(value.Label, value.Label) : value.Label;
        }

        internal int Compare(RankingValue left, RankingValue right)
        {
            if (left.Number is double a && right.Number is double b)
            {
                int order = left.ExactInteger is long exactLeft && right.ExactInteger is long exactRight
                    ? exactLeft.CompareTo(exactRight) : a.CompareTo(b);
                if (order != 0 || !IsChoice) return order;
                order = StringComparer.Ordinal.Compare(left.Key, right.Key);
                return order != 0 ? order : Nullable.Compare(left.SortProgress, right.SortProgress);
            }
            if (left.Number.HasValue != right.Number.HasValue) return left.Number.HasValue ? -1 : 1;
            if (!IsChoice)
            {
                int order = StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key);
                if (order != 0) return order;
            }
            return StringComparer.Ordinal.Compare(left.Key, right.Key);
        }
        internal MetaType? AffiliationType;
        internal Func<object, RankingValue> Read;
        internal ModDeclare Owner;
        internal Assembly Registrar;

        internal (string ID, string Name) Origin()
        {
            Owner ??= Registrar == typeof(RankingRegistry).Assembly ? Main.Mod :
                WorldBoxMod.LoadedMods.FirstOrDefault(mod => mod.GetType().Assembly == Registrar)?.GetDeclaration();
            return Owner != null ? (Owner.UID, Owner.Name) : (Registrar.FullName, Registrar.GetName().Name);
        }
    }

    private static readonly Dictionary<string, Source> Sources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RankingBoard> Boards = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RankingGroup> Groups = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Providers = new(StringComparer.OrdinalIgnoreCase);
    private sealed class FallbackEntry(object collection, string id, Action remove)
    {
        internal readonly object Collection = collection;
        internal readonly string ID = id;
        internal readonly Action Remove = remove;
    }
    private static readonly Dictionary<string, List<FallbackEntry>> Fallbacks = new(StringComparer.OrdinalIgnoreCase);
    private static List<FallbackEntry> _fallbackChanges;
    internal static int FallbackRevision { get; private set; }

    internal static bool HasProvider(string modId) => Providers.Contains(modId);

    internal static void InstallFallback(ModDeclare mod, Action register)
    {
        if (mod == null || HasProvider(mod.UID) || Fallbacks.ContainsKey(mod.UID)) return;
        var changes = new List<FallbackEntry>();
        _fallbackChanges = changes;
        try
        {
            register();
            Fallbacks.Add(mod.UID, changes);
        }
        catch (Exception ex)
        {
            RemoveFallback(changes);
            Main.LogWarning($"Could not load built-in rankings for {mod.Name}: {ex}");
        }
        finally
        {
            _fallbackChanges = null;
        }
    }

    private static void RemoveFallback(List<FallbackEntry> changes)
    {
        for (int i = changes.Count - 1; i >= 0; i--) changes[i].Remove();
    }

    private static bool Add<T>(Dictionary<string, T> definitions, string id, T definition, ModDeclare owner, Assembly caller)
    {
        if (_fallbackChanges != null)
        {
            if (!definitions.TryAdd(id, definition)) return false;
            _fallbackChanges.Add(new FallbackEntry(definitions, id, () => definitions.Remove(id)));
            return true;
        }
        owner = FindOwner(owner, caller);
        if (owner != null && owner.UID != Main.Mod?.UID)
        {
            if (Fallbacks.TryGetValue(owner.UID, out var changes))
            {
                if (definitions.ContainsKey(id) && !changes.Any(entry => entry.Collection == definitions && entry.ID == id))
                    return false;
                RemoveFallback(changes);
                Fallbacks.Remove(owner.UID);
                FallbackRevision++;
            }
            if (!definitions.TryAdd(id, definition)) return false;
            Providers.Add(owner.UID);
            return true;
        }
        return definitions.TryAdd(id, definition);
    }

    internal static ModDeclare FindOwner(ModDeclare owner, Assembly caller) => owner ??
        (caller == typeof(RankingRegistry).Assembly ? null :
            WorldBoxMod.LoadedMods.FirstOrDefault(mod => mod.GetType().Assembly == caller)?.GetDeclaration());

    static RankingRegistry()
    {
        const string prefix = "inmny.godtools.ranking.group.";
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.CharactersAndEquipment, Name = prefix + "world", Order = 100 });
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.Powers, Name = prefix + "powers", Order = 200 });
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.Communities, Name = prefix + "communities", Order = 300 });
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.Conflicts, Name = prefix + "conflicts", Order = 400 });
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.Archives, Name = prefix + "archive", Order = 500 });
        RegisterGroup(new RankingGroup { ID = RankingGroupIds.ModRankings, Name = prefix + "mods", Order = int.MaxValue });
    }

    internal static void Publish()
    {
        var domain = AppDomain.CurrentDomain;
        var waiting = domain.GetData(IntegrationSlot) as Action<Type>;
        domain.SetData(IntegrationSlot, typeof(RankingRegistry));
        if (waiting == null) return;
        foreach (Action<Type> connect in waiting.GetInvocationList())
        {
            try { connect(typeof(RankingRegistry)); }
            catch (Exception exception) { Main.LogWarning($"Could not connect a ranking provider: {exception}"); }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterCombatProvider(RankingCombatProvider definition) =>
        RankingCombatProviders.Register(definition, Assembly.GetCallingAssembly());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterGroup(RankingGroup definition)
    {
        if (definition == null || !IsID(definition.ID) || string.IsNullOrWhiteSpace(definition.Name)) return false;
        return Add(Groups, definition.ID, new RankingGroup
        {
            ID = definition.ID, Name = definition.Name, Order = definition.Order
        }, definition.Owner, Assembly.GetCallingAssembly());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterSource<T>(RankingSource<T> definition)
    {
        if (definition == null || !IsID(definition.ID) || definition.Read == null ||
            definition.Key == null || definition.Name == null) return false;
        var read = definition.Read;
        var key = definition.Key;
        var name = definition.Name;
        var icon = definition.Icon;
        var portrait = definition.SavedPortrait;
        return Add(Sources, definition.ID, new Source
        {
            Type = typeof(T), Read = () => read().Select(item => (object)item),
            Key = item => key((T)item), Name = item => name((T)item),
            Icon = item => icon?.Invoke((T)item),
            SavedPortrait = portrait == null ? null : item => portrait((T)item),
            ActorPortrait = definition.ActorPortrait,
            MetaPortrait = definition.MetaPortrait,
            Inspect = definition.Inspect, CanInspect = definition.CanInspect, Locate = definition.Locate, Place = definition.Place
        }, definition.Owner, Assembly.GetCallingAssembly());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterField<T>(string sourceID, RankingField<T> definition) =>
        RegisterField(sourceID, definition, Assembly.GetCallingAssembly());

    internal static bool RegisterField<T>(string sourceID, RankingField<T> definition, Assembly registrar)
    {
        if (sourceID == null || !Sources.TryGetValue(sourceID, out var source) || source.Type != typeof(T) ||
            definition == null || !IsID(definition.ID) || definition.Read == null ||
            string.IsNullOrWhiteSpace(definition.Name) || definition.Version < 1 ||
            !Enum.IsDefined(typeof(RankingFieldKind), definition.Kind)) return false;
        var owner = FindOwner(definition.Owner, registrar);
        if (_fallbackChanges == null && owner != null && Fallbacks.TryGetValue(owner.UID, out var changes) &&
            changes.Any(entry => entry.Collection == Sources && entry.ID == sourceID)) return false;
        var read = definition.Read;
        bool ordered = definition.Kind == RankingFieldKind.Ordinal || definition.Kind == RankingFieldKind.Choice && definition.Ordered;
        bool isChoice = definition.Kind is RankingFieldKind.Ordinal or RankingFieldKind.Choice or RankingFieldKind.Category;
        bool numeric = definition.Kind is RankingFieldKind.Number or RankingFieldKind.Integer;
        var options = new Dictionary<string, RankingOption>(StringComparer.Ordinal);
        var bands = new Dictionary<string, RankingDistributionBand>(StringComparer.Ordinal);
        if (definition.Options != null)
            foreach (var option in definition.Options)
            {
                if (!isChoice || option == null || !IsID(option.ID) || !IsID(option.Name) ||
                    double.IsNaN(option.Order) || double.IsInfinity(option.Order) || !options.TryAdd(option.ID, option))
                    return false;
                if (option.Band is not { } band) continue;
                if (!ordered || !IsID(band.ID) || !IsID(band.Name) || double.IsNaN(band.Order) || double.IsInfinity(band.Order))
                    return false;
                if (!bands.TryAdd(band.ID, band) && (bands[band.ID].Name != band.Name || bands[band.ID].Order != band.Order))
                    return false;
            }
        var operators = new List<RankingOperator> { RankingOperator.Equal, RankingOperator.Present, RankingOperator.Absent };
        if (numeric || ordered) operators.InsertRange(0, new[] { RankingOperator.AtLeast, RankingOperator.AtMost });
        if (definition.Kind == RankingFieldKind.Text) operators.Insert(0, RankingOperator.Contains);
        if (definition.Operators != null)
        {
            if (definition.Operators.Any(item => !operators.Contains(item))) return false;
            operators.RemoveAll(item => !definition.Operators.Contains(item));
        }
        return Add(source.Fields, definition.ID, new Field
        {
            ID = definition.ID, Name = definition.Name, Kind = definition.Kind, Read = item => read((T)item),
            AffiliationType = isChoice ? definition.AffiliationType : null,
            IconPath = definition.IconPath, Unit = numeric ? definition.Unit : null, Description = definition.Description,
            Ordered = ordered, Options = options, Operators = operators,
            Sortable = definition.Sortable && definition.Kind != RankingFieldKind.Tags, Version = definition.Version,
            Owner = owner, Registrar = registrar
        }, owner, registrar);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterBoard(RankingBoard definition)
    {
        if (definition == null || !IsID(definition.ID) || !IsID(definition.SourceID) ||
            string.IsNullOrWhiteSpace(definition.Name)) return false;
        return Add(Boards, definition.ID, new RankingBoard
        {
            ID = definition.ID, SourceID = definition.SourceID, Name = definition.Name, IconPath = definition.IconPath,
            GroupID = definition.GroupID, Order = definition.Order, Category = definition.Category,
            Columns = definition.Columns?.Where(IsID).Distinct().ToArray() ?? Array.Empty<string>(),
            RowLayout = definition.RowLayout?.Copy() ?? new RankingRowLayout(),
            DefaultSort = definition.DefaultSort, Ascending = definition.Ascending, Available = definition.Available
        }, definition.Owner, Assembly.GetCallingAssembly());
    }

    internal static Source GetSource(string id) => id == null ? null : Sources.GetValueOrDefault(id);
    internal static RankingBoard GetBoard(string id) => id == null ? null : Boards.GetValueOrDefault(id);
    internal static IEnumerable<(string Name, IEnumerable<RankingBoard> Boards)> AvailableBoardGroups() => Boards.Values
        .Where(board => Sources.ContainsKey(board.SourceID) && (board.Available?.Invoke() ?? true))
        .GroupBy(GroupFor)
        .OrderBy(group => group.Key.Order)
        .Select(group => (group.Key.Name, (IEnumerable<RankingBoard>)group.OrderBy(board => board.Order)));

    private static (string ID, string Name, int Order) GroupFor(RankingBoard board)
    {
        if (IsID(board.GroupID) && Groups.TryGetValue(board.GroupID, out var group))
            return (group.ID, group.Name, group.Order);

        if (IsID(board.Category))
        {
            string label = BuiltInRankings.Label(board.Category, board.Category);
            var legacy = board.Category is "世界榜单" or "World rankings"
                ? Groups[RankingGroupIds.CharactersAndEquipment]
                : Groups.Values.FirstOrDefault(item => item.Name == board.Category)
                    ?? Groups.Values.FirstOrDefault(item => BuiltInRankings.Label(item.Name, item.Name) == label);
            return legacy == null ? (null, label, 1000) : (legacy.ID, legacy.Name, legacy.Order);
        }

        if (!IsID(board.GroupID) && board.ID.StartsWith("cultiway:", StringComparison.Ordinal))
            return Groups.TryGetValue(RankingGroupIds.Cultiway, out var cultiway)
                ? (cultiway.ID, cultiway.Name, cultiway.Order)
                : (RankingGroupIds.Cultiway, "Cultiway", 600);

        string id = IsID(board.GroupID) ? RankingGroupIds.ModRankings : board.SourceID switch
            {
                BuiltInRankings.Actors or BuiltInRankings.Items => RankingGroupIds.CharactersAndEquipment,
                BuiltInRankings.LegacyPeaks => RankingGroupIds.Archives,
                _ => RankingGroupIds.ModRankings
            };
        var fallback = Groups[id];
        return (fallback.ID, fallback.Name, fallback.Order);
    }

    internal static bool IsID(string id) => !string.IsNullOrWhiteSpace(id);
}
