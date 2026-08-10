using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GodTools.Abstract;
using HarmonyLib;
using NeoModLoader.api.attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace GodTools.Features;

public class DetailedHistory : IManager
{
    public static List<WorldLogMessage> Messages = new();
    private static readonly JsonSerializerSettings _serializer_settings = new()
    {
        ContractResolver = new FieldsOnlyContractResolver(),
        Formatting = Formatting.None
    };
    
    [HarmonyPostfix, HarmonyPatch(typeof(WorldLogMessageExtensions), nameof(WorldLogMessageExtensions.add))]
    private static void WorldLogMessageExtensions_add_postfix(WorldLogMessage pMessage)
    {
        Messages.Add(pMessage);
    }
    public class FieldsOnlyContractResolver : DefaultContractResolver
    {
        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            // 仅包含字段
            return objectType.GetFields(BindingFlags.Public | BindingFlags.Instance).Cast<MemberInfo>().ToList();
        }
    }
    [Hotfixable]
    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveMapData))]
    private static void SaveManager_saveMapData_postfix(string pFolder)
    {
        var data = new List<WorldLogMessage>(Messages.Count);
        foreach (var msg in Messages)
        {
            data.Add(msg);
        }
        var folder = SaveManager.folderPath(pFolder);
        var file = Path.Combine(folder, "worldlog.json");
        
        Main.LogInfo($"Saving world log to {file}");
        File.WriteAllText(file, JsonConvert.SerializeObject(data, _serializer_settings));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld), [typeof(string), typeof(bool)])]
    private static void SaveManager_loadWorld_postfix(string pPath)
    {
        var file = Path.Combine(SaveManager.folderPath(pPath), "worldlog.json");
        if (!File.Exists(file))
        {
            Messages.Clear();
            return;
        }

        try
        {
            var loaded = JsonConvert.DeserializeObject<List<WorldLogMessage>>(
                             File.ReadAllText(file), _serializer_settings) ?? new List<WorldLogMessage>();
            Messages.Clear();
            Messages.AddRange(loaded);
        }
        catch (Exception e)
        {
            Messages.Clear();
            Main.LogWarning($"Ignoring invalid world history '{file}': {e.Message}");
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void MapBox_clearWorld_postfix()
    {
        Messages.Clear();
    }

    public void Initialize()
    {
        Harmony.CreateAndPatchAll(typeof(DetailedHistory));
    }
}
