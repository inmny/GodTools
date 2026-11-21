using System;
using System.Collections.Generic;

namespace GodTools.Features.Rankings;

internal sealed class DuelFighter
{
    internal double Health;
    internal double Damage;
    internal double AttacksPerSecond;
    internal double Armor;
    internal double Range;
    internal double Speed;
    internal double CultivationLevel;
    internal double ParryChance;
    internal bool ParryPerAttack;
    internal readonly Dictionary<string, DuelResource> Resources = new(StringComparer.Ordinal);
    internal readonly List<DuelStrike> Skills = new();
}

internal readonly struct DuelResource(double capacity, double starting, double recovery = 0)
{
    internal readonly double Capacity = capacity;
    internal readonly double Starting = starting;
    internal readonly double Recovery = recovery;
}

internal sealed class DuelStrike(double damage, double range, double recovery, string resource = null, double cost = 0,
    double cooldown = 0, int? uses = null, RankingCombatTrigger trigger = RankingCombatTrigger.Active,
    RankingCombatEffect effect = RankingCombatEffect.Damage, double chance = 1, Func<RankingCombatContext, bool> when = null)
{
    internal readonly double Amount = damage;
    internal readonly double Range = range;
    internal readonly double Recovery = recovery;
    internal readonly string Resource = resource;
    internal readonly double Cost = cost;
    internal readonly double Cooldown = cooldown;
    internal readonly int? Uses = uses;
    internal readonly RankingCombatTrigger Trigger = trigger;
    internal readonly RankingCombatEffect Effect = effect;
    internal readonly double Chance = chance;
    internal readonly Func<RankingCombatContext, bool> When = when;
}

internal sealed class DuelConditionException(DuelStrike strike, Exception cause) : Exception(cause.Message, cause)
{
    internal readonly DuelStrike Strike = strike;
}

internal readonly struct DuelResult(bool win, int casts, double parries, int reactions)
{
    internal readonly bool Win = win;
    internal readonly int Casts = casts;
    internal readonly double Parries = parries;
    internal readonly int Reactions = reactions;
}

internal readonly struct DuelRating(double strength, DuelResult result)
{
    internal readonly double LogStrength = strength;
    internal readonly DuelResult Result = result;
}

/// <summary>A read-only, deterministic estimate against fixed opponents. Time and resources are shared by attacks.</summary>
internal static class DuelBenchmark
{
    private const double Step = 0.25;
    private const int Steps = 128;
    internal const double MinLogStrength = -8;
    internal const double MaxLogStrength = 44;

    internal sealed class Opponent(RankingCombatOpponent style, string key, double health, double hit, double rate,
        double armor, double range, double speed, double retreat)
    {
        internal readonly RankingCombatOpponent Style = style;
        internal readonly string Key = key;
        internal readonly double Health = health;
        internal readonly double Hit = hit;
        internal readonly double Rate = rate;
        internal readonly double Armor = armor;
        internal readonly double Range = range;
        internal readonly double Speed = speed;
        internal readonly double Retreat = retreat;
    }

    internal static readonly Opponent[] Opponents =
    {
        new(RankingCombatOpponent.Melee, "melee", 100, 12, 1, 0, 1.8, 2.2, 0),
        new(RankingCombatOpponent.Ranged, "ranged", 85, 9, 1, 0, 13, 2.4, 9),
        new(RankingCombatOpponent.Armored, "armored", 155, 14, 0.7, 40, 1.8, 1.4, 0)
    };

    internal static DuelRating Rate(DuelFighter fighter, Opponent opponent)
    {
        double low = 0, high = 0;
        DuelResult best;
        if (fighter.Skills.Exists(strike => strike.When != null))
        {
            // A predicate can make a stronger opponent easier. Rate the first sampled loss
            // along the ladder instead of using a later, disconnected winning band.
            low = MinLogStrength;
            best = Fight(fighter, opponent, low);
            if (!best.Win) return new DuelRating(low, best);
            for (high = low + 1; high <= MaxLogStrength; high++)
            {
                var result = Fight(fighter, opponent, high);
                if (!result.Win) break;
                low = high;
                best = result;
                if (low == MaxLogStrength) return new DuelRating(low, best);
            }
        }
        else if ((best = Fight(fighter, opponent, 0)).Win)
        {
            for (double step = 1; ; step *= 2)
            {
                high = Math.Min(MaxLogStrength, step);
                var result = Fight(fighter, opponent, high);
                if (!result.Win) break;
                low = high;
                best = result;
                if (low == MaxLogStrength) return new DuelRating(low, best);
            }
        }
        else
        {
            for (double step = 1; ; step *= 2)
            {
                low = Math.Max(MinLogStrength, -step);
                best = Fight(fighter, opponent, low);
                if (best.Win) break;
                if (low == MinLogStrength) return new DuelRating(low, best);
                high = low;
            }
        }
        while (high - low > 0.001)
        {
            double mid = (low + high) / 2;
            var result = Fight(fighter, opponent, mid);
            if (result.Win) { low = mid; best = result; }
            else high = mid;
        }
        return new DuelRating(low, best);
    }

    internal static DuelResult Fight(DuelFighter fighter, Opponent opponent, double logStrength)
    {
        double scale = Math.Pow(10, logStrength);
        double enemyMax = opponent.Health * scale;
        double ownHealth = fighter.Health, enemyHealth = enemyMax;
        double distance = 8d, retreat = opponent.Retreat;
        double busy = 0, parries = 0;
        int casts = 0, reactions = 0;
        Dictionary<string, double> resources = null;
        if (fighter.Resources.Count > 0)
        {
            resources = new Dictionary<string, double>(fighter.Resources.Count, StringComparer.Ordinal);
            foreach (var entry in fighter.Resources) resources.Add(entry.Key, entry.Value.Starting);
        }
        int skillCount = fighter.Skills.Count;
        var ready = skillCount == 0 ? null : new double[skillCount];
        var uses = skillCount == 0 ? null : new int[skillCount];
        var amount = skillCount == 0 ? null : new double[skillCount];
        for (int i = 0; i < skillCount; i++)
        {
            var strike = fighter.Skills[i];
            uses[i] = strike.Uses ?? int.MaxValue;
            amount[i] = (strike.Effect == RankingCombatEffect.Damage
                ? ReceivedDamage(strike.Amount, opponent.Armor, 0) : strike.Amount) * strike.Chance;
        }
        double incomingPerHit = ReceivedDamage(opponent.Hit * scale, fighter.Armor, fighter.CultivationLevel);
        double outgoingPerHit = ReceivedDamage(fighter.Damage, opponent.Armor, 0);
        for (int i = 0; i < Steps; i++)
        {
            double now = i * Step;
            if (i > 0 && resources != null)
                foreach (var (key, pool) in fighter.Resources)
                    if (pool.Recovery > 0) resources[key] = Math.Min(pool.Capacity, resources[key] + pool.Recovery * Step);
            double toward = distance > fighter.Range && now >= busy ? fighter.Speed * Step : 0;
            double away = Math.Min(retreat, toward > 0 && distance <= opponent.Range
                ? opponent.Speed * Step : 0);
            retreat -= away;
            double enemyApproach = distance > opponent.Range ? opponent.Speed * Step : 0;
            distance = Math.Max(1, distance - toward + away - enemyApproach);

            double incoming = distance > opponent.Range ? 0 : incomingPerHit * opponent.Rate * Step;
            if (incoming > 0 && opponent.Range <= 2 && now >= busy && fighter.ParryChance > 0)
            {
                double chance = fighter.ParryChance;
                // Approximate the expected rate after the one-second recovery at levels I–II.
                if (!fighter.ParryPerAttack) chance /= 1 + chance * Math.Max(0, opponent.Rate - 1);
                parries += opponent.Rate * Step * chance;
                incoming *= 1 - chance;
            }

            double counterDamage = 0;
            double exposure = incoming;
            if (exposure > 0)
                for (int j = 0; j < skillCount; j++)
                {
                    var strike = fighter.Skills[j];
                    if (strike.Trigger != RankingCombatTrigger.IncomingDamage ||
                        (strike.Effect == RankingCombatEffect.Heal && ownHealth >= fighter.Health) ||
                        (strike.Effect == RankingCombatEffect.PreventDamage && incoming <= 0) ||
                        !CanUse(j, now, exposure)) continue;
                    switch (strike.Effect)
                    {
                        case RankingCombatEffect.Damage: counterDamage += amount[j]; break;
                        case RankingCombatEffect.Heal: ownHealth = Math.Min(fighter.Health, ownHealth + amount[j]); break;
                        case RankingCombatEffect.PreventDamage: incoming *= 1 - amount[j]; break;
                    }
                    Spend(j, now);
                    reactions++;
                }

            double outgoing = now < busy || distance > fighter.Range
                ? 0 : outgoingPerHit * fighter.AttacksPerSecond * Step;
            if (now >= busy)
            {
                double benefit = 0;
                int choice = -1;
                for (int j = 0; j < skillCount; j++)
                {
                    var strike = fighter.Skills[j];
                    if (strike.Trigger != RankingCombatTrigger.Active) continue;
                    double effect = strike.Effect == RankingCombatEffect.Heal
                        ? Math.Min(fighter.Health - ownHealth, amount[j]) : amount[j];
                    if (effect <= 0) continue;
                    double recovery = Math.Max(Step, strike.Recovery);
                    double gain = effect - (distance <= fighter.Range
                        ? outgoingPerHit * fighter.AttacksPerSecond * recovery : 0);
                    if (gain <= benefit || !CanUse(j, now, exposure)) continue;
                    benefit = gain;
                    choice = j;
                }
                if (choice >= 0)
                {
                    var strike = fighter.Skills[choice];
                    Spend(choice, now);
                    busy = now + Math.Max(Step, strike.Recovery);
                    outgoing = 0;
                    if (strike.Effect == RankingCombatEffect.Heal)
                        ownHealth = Math.Min(fighter.Health, ownHealth + amount[choice]);
                    else outgoing = amount[choice];
                    casts++;
                }
            }

            outgoing += counterDamage;
            if (outgoing >= enemyHealth || incoming >= ownHealth)
            {
                double killAt = outgoing > 0 ? enemyHealth / outgoing : double.PositiveInfinity;
                double fallAt = incoming > 0 ? ownHealth / incoming : double.PositiveInfinity;
                return new DuelResult(killAt <= 1 && killAt < fallAt, casts, parries, reactions);
            }
            ownHealth -= incoming;
            enemyHealth -= outgoing;
        }
        return new DuelResult(false, casts, parries, reactions);

        bool CanUse(int index, double now, double incoming)
        {
            var strike = fighter.Skills[index];
            if ((strike.Effect != RankingCombatEffect.Heal && distance > strike.Range) || now < ready[index] ||
                uses[index] == 0 || (strike.Cost > 0 && (resources == null ||
                resources.GetValueOrDefault(strike.Resource) < strike.Cost * (1 - 1e-12)))) return false;
            if (strike.When == null) return true;
            try
            {
                return strike.When(new RankingCombatContext(opponent.Style, now, ownHealth / fighter.Health,
                    enemyHealth / enemyMax, distance, incoming, resources));
            }
            catch (Exception exception) { throw new DuelConditionException(strike, exception); }
        }

        void Spend(int index, double now)
        {
            var strike = fighter.Skills[index];
            ready[index] = now + strike.Cooldown;
            uses[index]--;
            if (strike.Cost > 0) resources[strike.Resource] = Math.Max(0, resources[strike.Resource] - strike.Cost);
        }
    }

    private static double ReceivedDamage(double hit, double armor, double level)
    {
        if (hit <= 0) return 0;
        double passRatio = Math.Max(0, 1 - armor / 100);
        if (level <= 0) return Math.Max(1, hit * passRatio);
        // Cultiway compresses low-layer damage and can reject the hit. Compute the logarithm
        // as a ratio so a very large level gap does not overflow pow(100, level).
        double suppressed = Math.Log(Math.Max(1, hit)) / (level * Math.Log(100));
        return (suppressed >= 1 ? suppressed * passRatio : suppressed) * Math.Pow(0.5, level);
    }
}
