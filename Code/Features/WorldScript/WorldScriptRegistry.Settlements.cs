using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptRegistry
{
    private static readonly ScriptParameterDefinition Resource = Text("resource_id", "Resource", "wood");

    private static void RegisterSettlementActions()
    {
        AddCondition("city.resources", "Village resources", ScriptTargetKind.City, new[]
        {
            Resource, Choice("operator", "Operator", "<", WorldScriptEngine.Comparisons), Integer("value", "Amount", 10)
        }, (_, target, values) => Compare(((City)target.Meta).getResourcesAmount(GetString(values, "resource_id", "wood")), values));

        AddAction("settlement.rename", "Rename village or kingdom", ScriptTargetKind.City | ScriptTargetKind.Kingdom, false,
            new[] { Text("new_name", "New name", "") }, (context, target, values) =>
            {
                string name = GetString(values, "new_name").Trim();
                if (name == target.Meta.name) context.SkipAction("no_change");
                else if (target.Meta is City city) city.setName(name);
                else ((Kingdom)target.Meta).setName(name);
            });
        RegisterAction(new ActionDefinition
        {
            ID = "city.resources.change", Name = "Change village resources", AllowedTargets = ScriptTargetKind.City,
            Parameters = new[] { Resource, Integer("value", "Amount", 10),
                Choice("stock_write", "Inventory write policy", "delta", "delta", "at_least", "at_most") },
            EstimateChange = (target, values) => ResourceChange((City)target.Meta, values, apply: false),
            Execute = (context, target, values) =>
            {
                var city = (City)target.Meta;
                string resource = GetString(values, "resource_id", "wood");
                string policy = GetString(values, "stock_write", "delta");
                int before = city.getResourcesAmount(resource);
                int moved = ResourceChange(city, values, apply: true);
                context.WriteRecord = new ScriptWriteRecord
                {
                    Target = ScriptTargetSample.From(target), Key = resource, Policy = policy,
                    Before = before, After = city.getResourcesAmount(resource)
                };
                if (moved == 0) context.SkipAction(RequestedStockChange(values, before) == 0 ? "inventory_satisfied" : "storage_unavailable");
                else context.RecordChange(moved);
            }
        });
        AddAction("actor.city.set", "Move to village", ScriptTargetKind.Actor, true,
            new[] { Text("id", "Village ID", "") }, (context, target, values) =>
            {
                var city = World.world.cities.get(GetLong(values, "id", -1));
                if (city?.isAlive() != true || target.Actor.city == city) context.SkipAction("no_change");
                else target.Actor.joinCity(city);
            });
        AddAction("tile.building.place", "Place building", ScriptTargetKind.Tile, true,
            new[] { Text("building_id", "Building", "") }, (context, target, values) =>
            {
                if (World.world.buildings.addBuilding(GetString(values, "building_id"), target.Tile, pCheckForBuild: true) == null)
                    context.SkipAction("invalid_building_site");
                else context.RecordChange(1);
            });
        AddAction("kingdom.start_war", "Declare war", ScriptTargetKind.Kingdom, true,
            new[] { Text("id", "Target kingdom", "") }, (context, target, values) =>
            {
                var other = World.world.kingdoms.get(GetLong(values, "id", -1));
                if (other?.isAlive() != true ||
                    World.world.diplomacy.startWar((Kingdom)target.Meta, other, WarTypeLibrary.normal) == null)
                    context.SkipAction("no_change");
            });
    }

    private static int ResourceChange(City city, JObject values, bool apply)
    {
        var resource = AssetManager.resources.get(GetString(values, "resource_id", "wood"));
        if (resource == null) return 0;
        int requested = RequestedStockChange(values, city.getResourcesAmount(resource.id));
        if (requested <= 0)
        {
            int before = city.getResourcesAmount(resource.id);
            int removal = (int)Math.Min(before, -(long)requested);
            if (!apply || removal == 0) return -removal;
            city.takeResource(resource.id, removal);
            return city.getResourcesAmount(resource.id) - before;
        }

        int delivered = 0;
        foreach (Building storage in city.stockpiles.Where(storage => storage?.resources != null && storage.isUsable()))
        {
            int before = storage.getResourcesAmount(resource.id);
            int capacity = Math.Min(resource.maximum, resource.storage_max);
            int amount = Math.Min(requested - delivered, Math.Max(0, capacity - before));
            if (amount == 0) continue;
            if (apply)
            {
                storage.addResources(resource.id, amount);
                amount = storage.getResourcesAmount(resource.id) - before;
            }
            delivered += amount;
            if (delivered >= requested) break;
        }
        if (apply && delivered != 0) city._storage_version++;
        return delivered;
    }

    private static int RequestedStockChange(JObject values, int current)
    {
        int amount = GetInt(values, "value", 10);
        return GetString(values, "stock_write", "delta") switch
        {
            "at_least" => Math.Max(0, Math.Max(0, amount) - current),
            "at_most" => Math.Min(0, Math.Max(0, amount) - current),
            _ => amount
        };
    }
}
