using System;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.api;

namespace GodTools.Features.Rankings;

public enum RankingCombatOpponent { Melee, Ranged, Armored }
public enum RankingCombatTrigger { Active, IncomingDamage }
public enum RankingCombatEffect { Damage, Heal, PreventDamage }

/// <summary>One read-only frame of the combat estimate, valid during the condition callback.</summary>
public readonly struct RankingCombatContext
{
    private readonly IReadOnlyDictionary<string, double> resources;

    internal RankingCombatContext(RankingCombatOpponent opponent, double seconds, double healthFraction,
        double opponentHealthFraction, double distance, double incomingDamage, IReadOnlyDictionary<string, double> resources)
    {
        Opponent = opponent;
        Seconds = seconds;
        HealthFraction = healthFraction;
        OpponentHealthFraction = opponentHealthFraction;
        Distance = distance;
        IncomingDamage = incomingDamage;
        this.resources = resources;
    }

    public RankingCombatOpponent Opponent { get; }
    public double Seconds { get; }
    public double HealthFraction { get; }
    public double OpponentHealthFraction { get; }
    public double Distance { get; }
    public double IncomingDamage { get; }
    public double Resource(string id) => id == null ? 0 : resources?.GetValueOrDefault(id) ?? 0;
}

public sealed class RankingCombatProvider
{
    public string ID { get; set; }
    public string Name { get; set; }
    public ModDeclare Owner { get; set; }
    /// <summary>Read the actor once per assessment. Return true to supply data for this actor.</summary>
    public Func<Actor, RankingCombatData, bool> Read { get; set; }
}

public sealed class RankingCombatSkill
{
    public string ID { get; set; }
    public RankingCombatTrigger Trigger { get; set; }
    public RankingCombatEffect Effect { get; set; }
    /// <summary>Damage or HP restored, or a 0–1 fraction for PreventDamage.</summary>
    public double Amount { get; set; }
    public double Range { get; set; } = 1.8;
    public double RecoverySeconds { get; set; } = 0.5;
    public double CooldownSeconds { get; set; }
    public string ResourceID { get; set; }
    public double Cost { get; set; }
    public int? Uses { get; set; }
    public double Chance { get; set; } = 1;
    /// <summary>Pure predicate evaluated against the simulated frame. Do not read or modify live combat.</summary>
    public Func<RankingCombatContext, bool> When { get; set; }
}

/// <summary>A provider's contribution to one actor. Definitions are copied on submission.</summary>
public sealed class RankingCombatData
{
    internal RankingCombatData() { }

    internal readonly Dictionary<string, DuelResource> Resources = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, DuelStrike> Skills = new(StringComparer.Ordinal);
    internal readonly List<string> Notes = new();

    /// <summary>Describe one shared pool. The starting value defaults to capacity.</summary>
    public bool AddResource(string id, double capacity, double recoveryPerSecond = 0, double? starting = null)
    {
        double initial = starting ?? capacity;
        return RankingRegistry.IsID(id) && NonNegative(capacity) && NonNegative(recoveryPerSecond) &&
            NonNegative(initial) && Resources.TryAdd(id, new DuelResource(capacity, Math.Min(initial, capacity), recoveryPerSecond));
    }

    public bool AddSkill(RankingCombatSkill skill)
    {
        if (skill == null || !RankingRegistry.IsID(skill.ID) ||
            skill.Trigger is not (RankingCombatTrigger.Active or RankingCombatTrigger.IncomingDamage) ||
            skill.Effect is not (RankingCombatEffect.Damage or RankingCombatEffect.Heal or RankingCombatEffect.PreventDamage) ||
            !NonNegative(skill.Amount) || skill.Amount == 0 || !NonNegative(skill.Range) ||
            !NonNegative(skill.RecoverySeconds) || !NonNegative(skill.CooldownSeconds) || !NonNegative(skill.Cost) ||
            (skill.Cost > 0 && !RankingRegistry.IsID(skill.ResourceID)) || skill.Uses <= 0 ||
            !NonNegative(skill.Chance) || skill.Chance == 0 || skill.Chance > 1 ||
            (skill.Effect == RankingCombatEffect.PreventDamage &&
             (skill.Trigger != RankingCombatTrigger.IncomingDamage || skill.Amount > 1))) return false;
        // Bound public magnitudes so even extreme mod inputs remain safe in the benchmark arithmetic.
        return Skills.TryAdd(skill.ID, new DuelStrike(Math.Min(skill.Amount, 1e100), skill.Range,
            skill.RecoverySeconds, skill.ResourceID, skill.Cost, skill.CooldownSeconds, skill.Uses,
            skill.Trigger, skill.Effect, skill.Chance, skill.When));
    }

    public void AddNote(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) Notes.Add(text);
    }

    private static bool NonNegative(double value) => StandardDuelPower.IsFinite(value) && value >= 0;
}

internal static class RankingCombatProviders
{
    private static readonly Dictionary<string, Provider> Definitions = new(StringComparer.Ordinal);
    internal static IReadOnlyList<Provider> Current { get; private set; } = Array.Empty<Provider>();

    internal sealed class Provider(string id, string name, ModDeclare owner, System.Reflection.Assembly registrar,
        Func<Actor, RankingCombatData, bool> read)
    {
        internal readonly string ID = id;
        internal readonly string Name = name;
        internal ModDeclare Owner = owner;
        internal ModDeclare ResolveOwner() => Owner ??= RankingRegistry.FindOwner(null, registrar);
        internal readonly Func<Actor, RankingCombatData, bool> Read = read;
        private bool logged;

        internal void Report(string message)
        {
            if (logged) return;
            logged = true;
            Main.LogWarning($"Could not fully read {ID} combat data: {message}");
        }
    }

    internal static bool Register(RankingCombatProvider definition, System.Reflection.Assembly registrar)
    {
        if (definition == null || !RankingRegistry.IsID(definition.ID) || definition.Read == null) return false;
        string name = string.IsNullOrWhiteSpace(definition.Name) ? definition.ID : definition.Name;
        if (!Definitions.TryAdd(definition.ID, new Provider(definition.ID, name, definition.Owner, registrar,
                definition.Read))) return false;
        Current = Definitions.Values.OrderBy(provider => provider.ID, StringComparer.Ordinal).ToArray();
        return true;
    }
}
