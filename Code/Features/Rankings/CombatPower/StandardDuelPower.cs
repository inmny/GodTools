using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GodTools.Utils;
using strings;

namespace GodTools.Features.Rankings;

internal static class StandardDuelPower
{
    internal const string ProfileID = "godtools:standard-duel";
    internal const int Version = 2;
    internal const string PreviousFieldID = "power:" + ProfileID + "@1";
    internal const string BaseFieldID = "power";
    internal const string FieldID = "power:" + ProfileID + "@2";
    private const string Prefix = "inmny.godtools.ranking.duel.";

    private static readonly ModInput Cultiway =
        new(CultiwayCompatibility.ModId, assembly => new CultiwayDuelInput(assembly).Read);
    private static readonly ModInput CombatGods =
        new("TURBO_SAKUGA", assembly => new CombatGodsDuelInput(assembly).Read);

    internal static void Register() => RankingPowerProfiles.RegisterBuiltIn(new RankingPowerProfile
    {
        ID = ProfileID, Version = Version, Name = "inmny.godtools.ranking.power.score", Calculate = Assess,
        Description = Prefix + "description", IconPath = "ui/icons/iconWarriors", RecordPeaks = true
    });

    internal static RankingPowerScore Assess(Actor actor)
    {
        double health = actor.getMaxHealth();
        if (health <= 0) return null;

        // These are the finished game stats, including equipment, traits and mod stat bonuses.
        double critical = Math.Clamp(Positive(actor.stats[S.critical_chance]), 0, 1);
        double baseDamage = Positive(actor.stats[S.damage]);
        // AttackData stores the ordinary hit's base damage as a signed integer. A larger panel
        // value is outside the route modeled here, while independent skill effects can still be read.
        bool invalidOrdinaryHit = baseDamage > int.MaxValue || !IsFinite(actor.stats[S.damage]);
        double damage = (invalidOrdinaryHit ? 0 : Math.Floor(baseDamage)) * (Positive(actor.stats[S.damage_range]) + 1) / 2 *
            (1 + critical * Math.Max(0, Positive(actor.stats[S.critical_damage_multiplier]) - 1));
        var fighter = new DuelFighter
        {
            Health = health, Armor = IsFinite(actor.stats[S.armor]) ? actor.stats[S.armor] : 0,
            Damage = damage, AttacksPerSecond = actor.asset.skip_fight_logic ? 0 : Positive(actor.stats[S.attack_speed]),
            Range = Math.Max(1, Positive(actor.getAttackRange())), Speed = Positive(actor.stats[S.speed]) / 10
        };
        var details = new List<string> { Label("base") };
        if (invalidOrdinaryHit) details.Add(Label("attack_input"));
        var providers = RankingCombatProviders.Current;
        var owners = providers.Count == 0 ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skillOrigins = providers.Count == 0 ? null : new Dictionary<DuelStrike, RankingCombatProviders.Provider>();
        foreach (var provider in providers)
        {
            var contribution = new RankingCombatData();
            string author = BuiltInRankings.Label(provider.Name, provider.Name);
            try
            {
                if (!provider.Read(actor, contribution)) continue;
                if (contribution.Resources.Keys.Any(fighter.Resources.ContainsKey))
                    throw new InvalidOperationException("a shared resource already has a definition");
            }
            catch (Exception exception)
            {
                provider.Report(exception.Message);
                details.Add(author + " · " + Label("limited"));
                continue;
            }

            string owner = provider.ResolveOwner()?.UID;
            if (!string.IsNullOrEmpty(owner)) owners.Add(owner);
            foreach (var (id, pool) in contribution.Resources) fighter.Resources.Add(id, pool);
            foreach (var strike in contribution.Skills.Values)
            {
                fighter.Skills.Add(strike);
                skillOrigins.Add(strike, provider);
            }
            if (contribution.Resources.Count > 0 || contribution.Skills.Count > 0)
                details.Add(string.Format(Label("provider"), author, contribution.Skills.Count, contribution.Resources.Count));
            details.AddRange(contribution.Notes);
        }

        if (owners?.Contains(Cultiway.ID) != true) Apply(Cultiway);
        if (owners?.Contains(CombatGods.ID) != true) Apply(CombatGods);
        DuelRating[] matchups;
        while (true)
        {
            if ((fighter.Damage <= 0 || fighter.AttacksPerSecond <= 0) &&
                !fighter.Skills.Exists(strike => strike.Effect == RankingCombatEffect.Damage &&
                    (strike.Cost == 0 || fighter.Resources.GetValueOrDefault(strike.Resource).Capacity >= strike.Cost))) return null;
            try
            {
                matchups = DuelBenchmark.Opponents.Select(opponent => DuelBenchmark.Rate(fighter, opponent)).ToArray();
                break;
            }
            catch (DuelConditionException exception)
            {
                var provider = skillOrigins[exception.Strike];
                provider.Report(exception.Message);
                fighter.Skills.Remove(exception.Strike);
                details.Add(BuiltInRankings.Label(provider.Name, provider.Name) + " · " + Label("condition_error"));
            }
        }
        double logStrength = matchups.Select(result => result.LogStrength).OrderBy(value => value).ElementAt(1);
        double score = Index(logStrength);
        var parts = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < matchups.Length; i++)
            parts.Add(Prefix + DuelBenchmark.Opponents[i].Key, Index(matchups[i].LogStrength));
        if (fighter.Skills.Count > 0)
            parts.Add(Prefix + "casts", matchups.Max(value => value.Result.Casts));
        if (fighter.Skills.Exists(strike => strike.Trigger == RankingCombatTrigger.IncomingDamage))
            parts.Add(Prefix + "reactions", matchups.Max(value => value.Result.Reactions));
        if (fighter.ParryChance > 0)
            parts.Add(Prefix + "parries", matchups[0].Result.Parries);
        if (logStrength == DuelBenchmark.MinLogStrength) details.Add(Label("below"));
        if (logStrength == DuelBenchmark.MaxLogStrength) details.Add(Label("above"));
        string bound = logStrength == DuelBenchmark.MinLogStrength ? "≤" :
            logStrength == DuelBenchmark.MaxLogStrength ? "≥" : "";
        return new RankingPowerScore
        {
            Total = score, Parts = parts, Label = bound + score.ToString("0.0", CultureInfo.CurrentCulture),
            Details = string.Join("\n", details)
        };

        void Apply(ModInput input)
        {
            var read = input.TryGet();
            if (read == null)
            {
                if (input.IsPresent) details.Add(Label("limited"));
                return;
            }
            // Merge only after a successful read. An incompatible optional mod cannot empty the board.
            var contribution = new DuelFighter();
            var notes = new List<string>();
            try { read(actor, contribution, notes); }
            catch (Exception exception) { input.Report(exception); details.Add(Label("limited")); return; }
            fighter.CultivationLevel = Math.Max(fighter.CultivationLevel, contribution.CultivationLevel);
            fighter.ParryChance = Math.Max(fighter.ParryChance, contribution.ParryChance);
            fighter.ParryPerAttack |= contribution.ParryPerAttack;
            foreach (var resource in contribution.Resources) fighter.Resources.TryAdd(resource.Key, resource.Value);
            fighter.Skills.AddRange(contribution.Skills);
            details.AddRange(notes);
        }
    }

    private static double Index(double logStrength) => 1000 + 100 * logStrength;
    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static double Positive(double value) => IsFinite(value) && value > 0 ? value : 0;
    internal static string Label(string key) => BuiltInRankings.Label(Prefix + key, key);

    private sealed class ModInput(string id, Func<Assembly, Action<Actor, DuelFighter, List<string>>> bind)
    {
        private Assembly assembly;
        private Action<Actor, DuelFighter, List<string>> adapter;
        private bool logged;
        internal string ID => id;
        internal bool IsPresent => assembly != null;

        internal Action<Actor, DuelFighter, List<string>> TryGet()
        {
            if (assembly != null) return adapter;
            assembly = OptionalMods.AssemblyFor(id);
            if (assembly == null) return null;
            try { return adapter = bind(assembly); }
            catch (Exception exception) { Report(exception); return null; }
        }

        internal void Report(Exception exception)
        {
            if (logged) return;
            logged = true;
            Main.LogWarning($"Could not read {id} combat data: {exception}");
        }
    }
}
