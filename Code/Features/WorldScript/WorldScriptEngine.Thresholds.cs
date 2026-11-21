using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    private sealed class ThresholdSweep
    {
        public ScriptInstance Script;
        public long[] CityIDs;
        public int Index;
        public string Settings;
    }

    private static ThresholdSweep _thresholdSweep;

    private static bool ValidThresholdSettings(JObject values)
    {
        return WorldScriptRegistry.GetInt(values, "low_threshold", 20) >= 0 &&
            WorldScriptRegistry.GetInt(values, "high_threshold", 40) > WorldScriptRegistry.GetInt(values, "low_threshold", 20) &&
            WorldScriptRegistry.GetNumber(values, "sample_interval", 5d) >= 0.01d &&
            (WorldScriptRegistry.GetString(values, "threshold_metric", "stock") == "population" ||
                AssetManager.resources.dict.ContainsKey(WorldScriptRegistry.GetString(values, "resource_id", "wood")));
    }

    private static void CollectThresholdEvents()
    {
        double now = World.world.getCurWorldTime();
        if (_thresholdSweep == null)
        {
            var script = WorldScripts.Scripts.Where(item => item?.Enabled == true &&
                item.TriggerCondition?.TriggerID == WorldScriptRegistry.TriggerCityThreshold &&
                (item.Runtime.NextThresholdSample <= now || item.Runtime.NextThresholdSample - now >
                    WorldScriptRegistry.GetNumber(item.TriggerCondition.Parameters, "sample_interval", 5d)))
                .OrderBy(item => item.Runtime.NextThresholdSample).FirstOrDefault();
            if (script == null) return;
            var values = script.TriggerCondition.Parameters;
            if (WorldScriptRegistry.ValidateParameters(values, WorldScriptRegistry.GetTrigger(WorldScriptRegistry.TriggerCityThreshold).Parameters).Count > 0 ||
                !ValidThresholdSettings(values))
            {
                script.Runtime.NextThresholdSample = now + 5d;
                return;
            }
            var state = script.Runtime;
            string settings = values.ToString(Newtonsoft.Json.Formatting.None);
            if (state.ThresholdSettings != settings || state.ThresholdLatches == null)
            {
                state.ThresholdSettings = settings;
                state.ThresholdLatches = new Dictionary<long, bool>();
            }
            state.NextThresholdSample = now + WorldScriptRegistry.GetNumber(values, "sample_interval", 5d);
            _thresholdSweep = new ThresholdSweep { Script = script, Settings = settings,
                CityIDs = World.world.cities.Where(city => city.isAlive()).Select(city => city.id).ToArray() };
        }

        var sweep = _thresholdSweep;
        var watching = sweep.Script;
        if (!watching.Enabled || !WorldScripts.Scripts.Contains(watching) || watching.Runtime.ThresholdLatches == null ||
            watching.TriggerCondition.Parameters.ToString(Newtonsoft.Json.Formatting.None) != sweep.Settings)
        {
            _thresholdSweep = null;
            return;
        }
        var parameters = watching.TriggerCondition.Parameters;
        int low = WorldScriptRegistry.GetInt(parameters, "low_threshold", 20);
        int high = WorldScriptRegistry.GetInt(parameters, "high_threshold", 40);
        string resource = WorldScriptRegistry.GetString(parameters, "resource_id", "wood");
        bool population = WorldScriptRegistry.GetString(parameters, "threshold_metric", "stock") == "population";
        bool listenLow = WorldScriptRegistry.GetString(parameters, "threshold_direction", "low") == "low";
        var latches = watching.Runtime.ThresholdLatches;
        const int checksPerFrame = 64;
        for (int checks = 0; checks < checksPerFrame && sweep.Index < sweep.CityIDs.Length; checks++)
        {
            long id = sweep.CityIDs[sweep.Index++];
            var city = World.world.cities.get(id);
            if (city?.isAlive() != true) continue;
            int value = population ? city.getPopulationPeople() : city.getResourcesAmount(resource);
            bool observed = latches.TryGetValue(id, out bool wasLow);
            bool isLow = wasLow ? value < high : value <= low;
            if (observed && wasLow != isLow && listenLow == isLow &&
                !QueueEvent(WorldScriptRegistry.TriggerCityThreshold,
                    new WorldScriptEvent(city, city.kingdom, null, null, null, null, city.getTile(), value), watching.ID))
                continue;
            latches[id] = isLow;
        }
        if (sweep.Index < sweep.CityIDs.Length) return;
        foreach (long id in latches.Keys.Where(id => World.world.cities.get(id)?.isAlive() != true).ToArray())
            latches.Remove(id);
        _thresholdSweep = null;
    }
}
