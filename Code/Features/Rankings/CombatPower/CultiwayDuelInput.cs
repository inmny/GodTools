using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GodTools.Features.Rankings;

/// <summary>Reads Cultiway's active abilities and resource quotes without spending world resources.</summary>
internal sealed class CultiwayDuelInput
{
    private readonly Type handlesType;
    private readonly MethodInfo getExtend, getLevel, collect, channels, range, resolveResource, quote, demand, growth;
    private readonly MethodInfo skillComponent, talismanComponent, scrollComponent;
    private readonly MethodInfo requirements;
    private readonly object casterFunding, itemFunding;
    private readonly double learnedStrength;

    internal CultiwayDuelInput(Assembly assembly)
    {
        Type Find(string name) => assembly.GetType("Cultiway." + name, true);
        Type service = Find("Core.SkillLibV3.ActiveAbilities.ActiveAbilityService");
        Type handle = Find("Core.SkillLibV3.ActiveAbilities.ActiveAbilityHandle");
        Type entity = handle.GetField("Source").FieldType;
        handlesType = typeof(List<>).MakeGenericType(handle);
        getExtend = Find("Utils.Extension.ActorTools").GetMethod("TryGetExtend");
        getLevel = Find("Core.ActorExtend").GetMethod("GetPowerLevel");
        collect = service.GetMethod("Collect");
        channels = service.GetMethod("GetChannels");
        range = service.GetMethod("ResolveRange");
        resolveResource = Find("Core.SkillLibV3.SkillCastResourceResolver").GetMethod("Resolve");
        quote = Find("Core.SkillLibV3.SkillCastPayment").GetMethod("Quote");
        demand = Find("Core.SkillLibV3.SkillCastCost").GetMethod("CalculateStepDemand");
        growth = Find("Core.SkillLibV3.Utils.SkillEffectRadius").GetMethod("ResolveCasterGrowth");
        requirements = Find("Core.SkillLibV3.SkillCastRequirements").GetMethod("Check");
        var fundingType = Find("Core.SkillLibV3.SkillCastFundingSource");
        casterFunding = Enum.Parse(fundingType, "CasterResources");
        itemFunding = Enum.Parse(fundingType, "Prepaid");
        var tryRead = entity.GetMethod("TryGetComponent");
        skillComponent = tryRead.MakeGenericMethod(Find("Core.SkillLibV3.Components.SkillContainer"));
        talismanComponent = tryRead.MakeGenericMethod(Find("Content.Components.Talisman"));
        scrollComponent = tryRead.MakeGenericMethod(Find("Content.Components.MagicScroll"));
        learnedStrength = Convert.ToDouble(Find("Core.SkillLibV3.Components.SkillContext").GetField("DefaultStrength").GetRawConstantValue());
    }

    internal void Read(Actor actor, DuelFighter fighter, List<string> details)
    {
        var arguments = new object[] { actor, null };
        if (!(bool)getExtend.Invoke(null, arguments)) return;
        object unit = arguments[1];
        double level = StandardDuelPower.Positive(Convert.ToDouble(getLevel.Invoke(unit, null)));
        fighter.CultivationLevel = level;
        if (level > 0) details.Add(StandardDuelPower.Label("cultiway_level"));

        var handles = (IList)Activator.CreateInstance(handlesType);
        collect.Invoke(null, new[] { unit, handles });
        var seen = new HashSet<object>();
        int uncovered = 0;
        foreach (var handle in handles)
        {
            if ((Convert.ToInt32(channels.Invoke(null, new[] { unit, handle })) & 1) == 0 || !seen.Add(handle)) continue;
            if (!TryAdd(actor, unit, handle, fighter)) uncovered++;
        }
        if (fighter.Skills.Count > 0)
            details.Add(string.Format(StandardDuelPower.Label("cultiway_abilities"), fighter.Skills.Count));
        if (uncovered > 0)
            details.Add(string.Format(StandardDuelPower.Label("cultiway_uncovered"), uncovered));
    }

    private bool TryAdd(Actor actor, object unit, object handle, DuelFighter fighter)
    {
        object source = Member(handle, "Source");
        string provider = (string)Member(handle, "ProviderId");
        object payload = provider switch
        {
            "content.talisman" => Component(talismanComponent, source),
            "content.magic_scroll" => Component(scrollComponent, source),
            _ => null
        };
        bool consumable = payload != null;
        if (!consumable && provider != "core.learned_skill") return false;
        object skill = consumable ? Member(payload, "SkillContainer") : source;
        object asset = Member(Component(skillComponent, skill), "Asset");
        var impact = Member(asset, "ImpactProfile");
        if (Member(asset, "DealsBaseDamage") is not true ||
            Member(Member(asset, "UseProfile"), "TargetRelation")?.ToString() != "Hostile" ||
            Member(impact, "Kind")?.ToString() is not ("Projectile" or "Piercing" or "Explosion" or "HeavySkyfall")) return false;
        if (!(bool)requirements.Invoke(null, new[] { unit, skill, consumable ? itemFunding : casterFunding })) return false;

        double strength = consumable ? Number(payload, "Strength") : learnedStrength;
        if (provider == "content.talisman")
            strength *= Math.Pow(2, Math.Max(0, Number(payload, "PowerLevel") - fighter.CultivationLevel));
        double damage = StandardDuelPower.Positive(strength * Number(impact, "DamageMultiplier") *
            Number(Member(asset, "ImpactTuning"), "DamageMultiplier"));
        double reach = StandardDuelPower.Positive(Convert.ToDouble(range.Invoke(null, new[] { unit, handle, null })));
        if (damage <= 0 || reach <= 0) return false;

        string pool;
        double cost, available;
        if (consumable)
        {
            pool = $"cultiway:item:{Member(source, "Pid")}";
            cost = available = 1;
        }
        else
        {
            var binding = resolveResource.Invoke(null, new[] { unit, skill });
            if (Member(binding, "Resources") is not IEnumerable resources) return false;
            var resource = resources.Cast<object>().Take(2).ToArray();
            if (resource.Length != 1) return false;
            var read = (Delegate)(Member(resource[0], "MaximumAmount") ?? Member(resource[0], "ReadAmount"));
            if (read == null) return false;
            available = StandardDuelPower.Positive(Convert.ToDouble(read.DynamicInvoke(unit)));
            pool = "cultiway:" + (string)Member(resource[0], "BalanceId");
            double stepDemand = Convert.ToDouble(demand.Invoke(null, new[] { skill }));
            double areaGrowth = Convert.ToDouble(growth.Invoke(null, new[] { unit, skill }));
            cost = Convert.ToDouble(quote.Invoke(null, new[] { unit, resource[0], (float)(stepDemand * Math.Sqrt(areaGrowth)) }));
            if (double.IsNaN(cost) || double.IsInfinity(cost) || cost < 0) return false;
        }
        fighter.Resources[pool] = new DuelResource(available, available);
        double recovery = StandardDuelPower.Positive(actor.getAttackCooldown());
        fighter.Skills.Add(new DuelStrike(damage, reach, Math.Max(0.5, recovery), pool, cost));
        return true;
    }

    private static object Component(MethodInfo read, object entity)
    {
        if (entity == null || Member(entity, "IsNull") is true) return null;
        var arguments = new object[1];
        return (bool)read.Invoke(entity, arguments) ? arguments[0] : null;
    }

    private static object Member(object item, string name) => CultiwayCompatibility.Member(item, name);
    private static double Number(object item, string name) => Convert.ToDouble(Member(item, name));
}
