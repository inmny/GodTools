using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptRegistry
{
    public const string TargetCityRegion = "city.region";
    public const string TargetKingdomRegion = "kingdom.region";
    public const string TargetTileRegion = "tile.region";
    public const string TriggerRegionOwnership = "region.ownership";
    public const string ConditionRegion = "region.contains";

    private static ScriptRegion RegionParameter(JObject values) => WorldScripts.FindRegion(GetLong(values, "id", -1));

    private static void RegisterRegions()
    {
        var parameters = new[] { Text("id", "Map region", "") };
        RegisterTrigger(new TriggerDefinition { ID = TriggerRegionOwnership, Type = TriggerConditionType.RegionOwnership,
            Name = "Village changes kingdom in region", Parameters = parameters });
        AddTarget(TargetCityRegion, "Villages in region", ScriptTargetKind.City, parameters,
            (_, values) => RegionCities(RegionParameter(values)).Select(WorldScriptTarget.ForCity),
            (_, values) => RegionParameter(values)?.Center());
        AddTarget(TargetKingdomRegion, "Kingdoms with villages in region", ScriptTargetKind.Kingdom, parameters,
            (_, values) => RegionCities(RegionParameter(values)).Select(city => city.kingdom)
                .Where(kingdom => kingdom?.isCiv() == true && kingdom.isAlive()).Distinct().Select(WorldScriptTarget.ForKingdom),
            (_, values) => RegionParameter(values)?.Center());
        AddTarget(TargetTileRegion, "Tiles in region", ScriptTargetKind.Tile, parameters,
            (_, values) => RegionTiles(RegionParameter(values)), (_, values) => RegionParameter(values)?.Center());
        AddCondition(ConditionRegion, "Within map region", ScriptTargetKind.City | ScriptTargetKind.Kingdom |
            ScriptTargetKind.Actor | ScriptTargetKind.Tile, parameters, (_, target, values) =>
            {
                var region = RegionParameter(values);
                if (region == null) return false;
                return target.Meta switch
                {
                    City city => region.Contains(city.getTile()),
                    Kingdom kingdom => RegionCities(region).Any(city => city.kingdom == kingdom),
                    _ => region.Contains(target.Tile ?? target.Actor?.current_tile)
                };
            });
    }

    private static IEnumerable<City> RegionCities(ScriptRegion region) => region == null ? Enumerable.Empty<City>()
        : World.world.cities.Where(city => city?.isAlive() == true && region.Contains(city.getTile()));

    private static IEnumerable<WorldScriptTarget> RegionTiles(ScriptRegion region)
    {
        if (region == null) yield break;
        int endX = Math.Min(MapBox.width - 1, Math.Max(region.X1, region.X2));
        int endY = Math.Min(MapBox.height - 1, Math.Max(region.Y1, region.Y2));
        for (int y = Math.Max(0, Math.Min(region.Y1, region.Y2)); y <= endY; y++)
            for (int x = Math.Max(0, Math.Min(region.X1, region.X2)); x <= endX; x++)
                if (World.world.GetTile(x, y) is WorldTile tile) yield return WorldScriptTarget.ForTile(tile);
    }
}
