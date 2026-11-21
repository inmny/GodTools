using System;
using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.Rankings;

internal sealed class RankingArchive
{
    public string ID;
    public RankingPreset View;
    public string Name;
    public string BoardName;
    public string PrimaryName;
    public DateTime RefreshedAt;
    public DateTime? SavedAt;
    public double WorldTime;
    public int ScopeCount;
    public int RankedCount;
    public int RulesVersion;
    public string[] Schema;
    public List<RankingRow> Rows;

    internal static RankingArchive Capture(RankingSnapshot snapshot, string boardName, string primaryName,
        IEnumerable<string> viewFields)
    {
        var fields = new HashSet<string>(viewFields, StringComparer.Ordinal);
        return new RankingArchive
        {
            ID = Guid.NewGuid().ToString("N"), View = RankingStore.Copy(snapshot.View), BoardName = boardName, PrimaryName = primaryName,
            RefreshedAt = snapshot.Data.RefreshedAt, SavedAt = DateTime.UtcNow, WorldTime = snapshot.Data.WorldTime,
            ScopeCount = snapshot.ScopeCount, RankedCount = snapshot.RankedCount, RulesVersion = 1,
            Schema = RankingQuery.RuleFields(snapshot.View).Select(id =>
            {
                var field = snapshot.Data.Fields[id];
                return $"{id}|{field.Kind}|{field.Ordered}|{field.Version}";
            }).ToArray(),
            Rows = snapshot.Visible.Select(row => new RankingRow
            {
                Key = row.Key, Name = row.Name, Rank = row.Rank,
                Values = row.Values.Where(pair => fields.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            }).ToList()
        };
    }

    internal bool CanCompare(RankingArchive other) =>
        other != null && ID != other.ID && RulesVersion == other.RulesVersion && Schema.SequenceEqual(other.Schema) &&
        RankingSnapshot.SameSettings(View, other.View);
}

/// <summary>The small result kept for a featured preset when its live snapshot is no longer in memory.</summary>
internal sealed class RankingPreview
{
    internal const int DefaultLeaderCount = 3;
    internal const int MaxLeaderCount = 50;

    public DateTime RefreshedAt;
    public List<RankingRow> Leaders;
    public bool Complete;

    internal static int ClampCount(int count) => Math.Clamp(count, 1, MaxLeaderCount);

    internal bool Covers(int count) => Complete || Leaders.Count >= count || Leaders.Count < DefaultLeaderCount;

    internal static RankingPreview Capture(RankingSnapshot sample)
    {
        var ranked = sample.Visible.Where(row => row.Rank > 0).Take(MaxLeaderCount + 1).ToArray();
        return new RankingPreview
        {
            RefreshedAt = sample.Data.RefreshedAt,
            Complete = ranked.Length <= MaxLeaderCount,
            Leaders = ranked.Take(MaxLeaderCount).Select(row =>
            {
                var value = row.Values[sample.Primary];
                return new RankingRow
                {
                    Name = row.Name, Key = row.Key, Rank = row.Rank,
                    Values = new Dictionary<string, RankingValue>(StringComparer.Ordinal)
                    {
                        [sample.Primary] = RankingValue.Choice(value.Key, value.Label, value.Number)
                    }
                };
            }).ToList()
        };
    }
}
