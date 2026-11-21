using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    private static ScriptTargetSample EventSubject(WorldScriptEvent snapshot)
    {
        if (snapshot?.CityID is long city)
            return new ScriptTargetSample { Kind = ScriptTargetKind.City, MetaID = city, Name = snapshot.CityName };
        if (snapshot?.ActorID >= 0)
            return new ScriptTargetSample { Kind = ScriptTargetKind.Actor, ActorID = snapshot.ActorID, Name = snapshot.ActorName };
        return snapshot?.KingdomID is long kingdom
            ? new ScriptTargetSample { Kind = ScriptTargetKind.Kingdom, MetaID = kingdom, Name = snapshot.KingdomName } : null;
    }

    internal static bool UsesCityEvent(string triggerID) =>
        triggerID is WorldScriptRegistry.TriggerCityKingdomChanged or WorldScriptRegistry.TriggerDelayed
            or WorldScriptRegistry.TriggerCityThreshold or WorldScriptRegistry.TriggerRegionOwnership;
    internal static bool UsesKingdomEvent(string triggerID) =>
        UsesCityEvent(triggerID) || triggerID is WorldScriptRegistry.TriggerKingdomKingChanged or WorldScriptRegistry.TriggerWarEnded;

    private static bool InTriggerRegion(ScriptInstance script, WorldScriptEvent snapshot) =>
        snapshot?.X is int x && snapshot.Y is int y &&
        long.TryParse(WorldScriptRegistry.GetString(script.TriggerCondition.Parameters, "id"), out long region) &&
        WorldScripts.FindRegion(region)?.Contains(x, y) == true;

    internal static void OnWarEnded(War war)
    {
        foreach (var kingdom in war.getAllAttackers().Concat(war.getAllDefenders())
                     .Where(kingdom => kingdom?.isAlive() == true).Distinct())
            QueueEvent(WorldScriptRegistry.TriggerWarEnded, new WorldScriptEvent(null, kingdom, null, null, null, null,
                kingdom.capital?.getTile(), warName: war.name));
    }

    internal static void OnVillageChangedKingdom(City city, Kingdom previous)
    {
        Kingdom current = city.kingdom;
        if (previous?.isCiv() != true || current?.isCiv() != true || previous == current) return;
        var snapshot = VillageEvent(city, previous);
        QueueEvent(WorldScriptRegistry.TriggerCityKingdomChanged, snapshot);
        if (WorldScripts.Regions.Count > 0) QueueEvent(WorldScriptRegistry.TriggerRegionOwnership, snapshot);
    }

    internal static WorldScriptEvent VillageEvent(City city, Kingdom previous = null) =>
        new(city, city.kingdom, previous?.data?.id, previous?.data?.name,
            city.kingdom?.data?.id, city.kingdom?.data?.name, city.getTile());

    internal static void OnKingChanged(Kingdom kingdom, long? previousID, string previousName)
    {
        Actor current = kingdom.king;
        if (previousID == current?.data?.id || !kingdom.isCiv()) return;
        QueueEvent(WorldScriptRegistry.TriggerKingdomKingChanged, RulerEvent(kingdom, previousID, previousName));
    }

    internal static WorldScriptEvent RulerEvent(Kingdom kingdom, long? previousID = null, string previousName = null) =>
        new(null, kingdom, previousID, previousName, kingdom.king?.data?.id, kingdom.king?.data?.name,
            kingdom.capital?.getTile() ?? kingdom.king?.current_tile);

    private static string EventCooldownKey(ScriptInstance script, WorldScriptEvent snapshot)
    {
        string triggerID = script.TriggerCondition.TriggerID;
        if (script.Cooldown <= 0 || !UsesKingdomEvent(triggerID) ||
            !WorldScriptRegistry.GetBool(script.TriggerCondition.Parameters, "per_object_cooldown")) return null;
        return UsesCityEvent(triggerID) ? $"city/{snapshot?.CityID}" : $"kingdom/{snapshot?.KingdomID}";
    }

    private static void RememberEventCooldown(ScriptInstance script, string key, double time)
    {
        var cooldowns = script.Runtime.EventCooldowns ??= new Dictionary<string, double>();
        cooldowns[key] = time;
        if (cooldowns.Count <= 256) return;
        foreach (var expired in cooldowns.Where(entry => entry.Value > time || time - entry.Value >= script.Cooldown).ToArray())
            cooldowns.Remove(expired.Key);
    }
}
