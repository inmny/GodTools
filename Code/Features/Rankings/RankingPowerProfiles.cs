using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GodTools.UI;
using NeoModLoader.api;
using NeoModLoader.General;

namespace GodTools.Features.Rankings;

public sealed class RankingPowerScore
{
    public double Total { get; set; }
    public IReadOnlyDictionary<string, double> Parts { get; set; }
    public string Label { get; set; }
    public string Details { get; set; }
}

public sealed class RankingPowerProfile
{
    public string ID { get; set; }
    public int Version { get; set; } = 1;
    public string Name { get; set; }
    public string IconPath { get; set; }
    public string Unit { get; set; }
    public string Description { get; set; }
    public Func<Actor, RankingPowerScore> Calculate { get; set; }
    public bool RecordPeaks { get; set; }
    public ModDeclare Owner { get; set; }
}

public static class RankingPowerProfiles
{
    private static readonly Dictionary<string, RankingPowerProfile> Profiles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<Actor, double?>> Factors = new(StringComparer.Ordinal);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterFactor(string id, string name, Func<Actor, double?> read) =>
        RegisterFactor(id, name, read, null, Assembly.GetCallingAssembly());

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool RegisterFactor(string id, string name, Func<Actor, double?> read, ModDeclare owner) =>
        RegisterFactor(id, name, read, owner, Assembly.GetCallingAssembly());

    private static bool RegisterFactor(string id, string name, Func<Actor, double?> read, ModDeclare owner, Assembly registrar)
    {
        if (!RankingRegistry.IsID(id) || string.IsNullOrWhiteSpace(name) || read == null || Factors.ContainsKey(id)) return false;
        if (!RankingRegistry.RegisterField(BuiltInRankings.Actors, new RankingField<Actor>
            {
                ID = $"factor:{id}", Name = name, Kind = RankingFieldKind.Number, Owner = owner,
                Read = actor => ReadFactor(id, actor) is double value ? RankingValue.Numeric(value) : null
            }, registrar)) return false;
        Factors.Add(id, read);
        return true;
    }

    public static double? ReadFactor(string id, Actor actor)
    {
        if (id == null || !Factors.TryGetValue(id, out var read) || read(actor) is not double value ||
            double.IsNaN(value) || double.IsInfinity(value)) return null;
        return value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Register(RankingPowerProfile profile) => RegisterProfile(profile, Assembly.GetCallingAssembly(), false);

    internal static bool RegisterBuiltIn(RankingPowerProfile profile) => RegisterProfile(profile, typeof(RankingPowerProfiles).Assembly, true);

    private static bool RegisterProfile(RankingPowerProfile profile, Assembly registrar, bool builtIn)
    {
        if (profile == null || !RankingRegistry.IsID(profile.ID) || profile.Version < 1 ||
            string.IsNullOrWhiteSpace(profile.Name) || profile.Calculate == null) return false;
        string field = $"power:{profile.ID}@{profile.Version}";
        if (Profiles.ContainsKey(field)) return false;
        var definition = new RankingPowerProfile
        {
            ID = profile.ID, Version = profile.Version, Name = profile.Name,
            Calculate = profile.Calculate, RecordPeaks = profile.RecordPeaks
        };
        if (!RankingRegistry.RegisterField(BuiltInRankings.Actors, new RankingField<Actor>
            {
                ID = field, Name = builtIn ? definition.Name : Versioned(definition.Name, definition.Version), Kind = RankingFieldKind.Number,
                Owner = profile.Owner, IconPath = profile.IconPath, Unit = profile.Unit, Description = profile.Description,
                Read = actor =>
                {
                    var score = definition.Calculate(actor);
                    return score == null ? null : RankingValue.Power(score.Total, score.Parts, score.Label, score.Details);
                }
            }, registrar)) return false;
        Profiles.Add(field, definition);
        return true;
    }

    internal static void Record(IEnumerable<string> fields, RankingDataset sample)
    {
        foreach (string field in fields)
            if (Profiles.TryGetValue(field, out var profile) && profile.RecordPeaks)
                RankingStore.Record(profile, sample, field);
    }

    internal static string Versioned(string name, int version) => $"{(LM.Has(name) ? LM.Get(name) : name)} · v{version}";

    internal static void RegisterPeakBoard(RankingPeakBoard archive)
    {
        string profile = archive.ProfileID;
        int version = archive.Version;
        string id = $"godtools:peak:{profile}@{version}";
        bool builtIn = profile == StandardDuelPower.ProfileID && version == StandardDuelPower.Version;
        CreatureSavedList.SavedActorPacket Creature(string key) =>
            RankingStore.FindPeaks(profile, version)?.Entries.Find(sample => sample.ActorID == key)?.Creature;
        if (!RankingRegistry.RegisterSource(new RankingSource<RankingPeak>
            {
                ID = id, Read = () => RankingStore.FindPeaks(profile, version)?.Entries ?? Enumerable.Empty<RankingPeak>(),
                Key = sample => sample.ActorID, Name = sample => sample.ActorName,
                Icon = sample => sample.Creature?.ActorData?.asset_id is string asset ? AssetManager.actor_library.get(asset)?.getSpriteIcon() : null,
                SavedPortrait = builtIn ? sample => sample.Portrait?.GetSprite() ??
                    (BuiltInRankings.FindActor(sample.ActorID) == null ? RankingPeakPortrait.FromSavedCreature(sample) : null) : null,
                ActorPortrait = builtIn ? BuiltInRankings.FindActor : null,
                SavedCreature = builtIn ? Creature : null,
                Inspect = BuiltInRankings.InspectActor, CanInspect = BuiltInRankings.CanInspectActor,
                Locate = BuiltInRankings.LocateActor,
                Place = builtIn ? key => WindowCreatureSavedList.PlaceSnapshot(Creature(key)) : null
            })) return;
        BuiltInRankings.Field<RankingPeak>(id, "name", "name", RankingFieldKind.Text,
            sample => RankingValue.Text(sample.ActorName));
        BuiltInRankings.Field<RankingPeak>(id, "score", "power.score", RankingFieldKind.Number,
            sample => RankingValue.Power(sample.Score, sample.Parts, details: sample.Details));
        BuiltInRankings.Field<RankingPeak>(id, "year", "year", RankingFieldKind.Number,
            sample => RankingValue.Numeric(Date.getYear(sample.Time)));
        RankingRegistry.RegisterBoard(new RankingBoard
        {
            ID = id, SourceID = id, Name = builtIn ? "inmny.godtools.ranking.current_peaks" :
                $"{Versioned(archive.Name ?? profile, version)} · {LM.Get("inmny.godtools.ranking.peak_samples")}",
            GroupID = RankingGroupIds.Archives,
            Order = builtIn ? 100 : 300,
            Columns = new[] { "score", "year" }, DefaultSort = "score", IconPath = "ui/icons/iconWorldLog",
            Available = () => RankingStore.FindPeaks(profile, version) != null
        });
    }
}
