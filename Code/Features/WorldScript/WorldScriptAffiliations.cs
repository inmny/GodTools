using System;
using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptAffiliations
{
    internal static string Kind(string definition)
    {
        if (definition == null) return null;
        string id = IsSetter(definition) ? definition.Substring(0, definition.Length - 4) : definition;
        return id switch
        {
            WorldScriptRegistry.TargetActorKingdom or WorldScriptRegistry.TargetCityKingdom or WorldScriptRegistry.TargetKingdomByID
                or "kingdom.start_war" => "kingdom",
            WorldScriptRegistry.TargetCityByID or "actor.city" => "city",
            WorldScriptRegistry.TargetActorCulture => "culture",
            WorldScriptRegistry.TargetActorClan => "clan",
            WorldScriptRegistry.TargetActorLanguage => "language",
            WorldScriptRegistry.TargetActorSubspecies => "subspecies",
            WorldScriptRegistry.TargetCityRegion or WorldScriptRegistry.TargetKingdomRegion or WorldScriptRegistry.TargetTileRegion
                or WorldScriptRegistry.TriggerRegionOwnership or WorldScriptRegistry.ConditionRegion => "region",
            _ => null
        };
    }

    internal static string Name(string kind, long id) =>
        kind == "region" ? WorldScripts.FindRegion(id)?.Name : Find(kind, id)?.name;

    internal static bool Exists(string kind, long id) =>
        kind == "region" ? WorldScripts.FindRegion(id) != null : Find(kind, id) != null;

    internal static IEnumerable<(long ID, string Name)> Named(string kind) =>
        kind == "region" ? WorldScripts.Regions.Select(region => (region.ID, region.Name)) :
        List(kind).Where(item => item?.getMetaData() != null).Select(item => (item.getID(), item.name));

    internal static bool IsSetter(string definition) => definition?.EndsWith(".set", StringComparison.Ordinal) == true;

    internal static bool CanClear(string definition) => IsSetter(definition) &&
        Kind(definition) is "culture" or "clan" or "language" or "subspecies";

    internal static IEnumerable<IMetaObject> List(string kind)
    {
        if (World.world == null) return Array.Empty<IMetaObject>();
        return kind switch
        {
            "kingdom" => World.world.kingdoms,
            "city" => World.world.cities,
            "culture" => World.world.cultures,
            "clan" => World.world.clans,
            "language" => World.world.languages,
            "subspecies" => World.world.subspecies,
            _ => Array.Empty<IMetaObject>()
        };
    }

    internal static IMetaObject Find(string kind, long id)
    {
        if (World.world == null) return null;
        return kind switch
        {
            "kingdom" => World.world.kingdoms.get(id),
            "city" => World.world.cities.get(id),
            "culture" => World.world.cultures.get(id),
            "clan" => World.world.clans.get(id),
            "language" => World.world.languages.get(id),
            "subspecies" => World.world.subspecies.get(id),
            _ => null
        };
    }
}
