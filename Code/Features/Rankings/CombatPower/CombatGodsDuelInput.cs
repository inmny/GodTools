using System;
using System.Collections.Generic;
using System.Reflection;

namespace GodTools.Features.Rankings;

/// <summary>Uses the installed Combat Gods 1.x technique metadata. Samples Still Water's ordinary-hit rule.</summary>
internal sealed class CombatGodsDuelInput
{
    private readonly MethodInfo anyTrait, get, level, tierOf, info, persona, tryGetFighter;

    internal CombatGodsDuelInput(Assembly assembly)
    {
        Type Find(string type) => assembly.GetType("Sakuga." + type, true);
        Type techniques = Find("Techniques");
        anyTrait = techniques.GetMethod("AnyTrait");
        get = techniques.GetMethod("Get");
        level = techniques.GetMethod("Level", new[] { typeof(Actor), Find("Technique") });
        tierOf = Find("Tiers").GetMethod("Of");
        info = Find("Tiers").GetMethod("Info");
        persona = Find("Personas").GetMethod("Of");
        tryGetFighter = Find("Fighters").GetMethod("TryGet", new[] { typeof(Actor), Find("Fighter").MakeByRefType() });
    }

    internal void Read(Actor actor, DuelFighter fighter, List<string> details)
    {
        var args = new object[] { actor };
        if (!(bool)anyTrait.Invoke(null, args)) return;
        details.Add(StandardDuelPower.Label("gods_uncovered"));
        object technique = get.Invoke(null, new object[] { "still_water" });
        if (technique == null) return;
        int learned = (int)level.Invoke(null, new[] { actor, technique });
        if (learned <= 0 || !(bool)tryGetFighter.Invoke(null, new object[] { actor, null })) return;

        // The benchmark opponent is a front-facing ordinary fighter, without a Sakuga tier.
        object tier = tierOf.Invoke(null, args);
        object tierInfo = info.Invoke(null, new[] { tier });
        object disposition = persona.Invoke(null, args);
        double readSkill = Convert.ToDouble(CultiwayCompatibility.Member(disposition, "ReadSkill"));
        double react = Convert.ToDouble(CultiwayCompatibility.Member(tierInfo, "React") ?? 0.3f);
        fighter.ParryChance = Math.Clamp((react + 0.1 * Convert.ToInt32(tier)) * readSkill, 0.03, 0.95) * 0.5;
        fighter.ParryPerAttack = learned >= 3;
        details.Add(StandardDuelPower.Label("gods_parry"));
    }
}
