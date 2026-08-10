using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Abstract;
using GodTools.Utils;
using Newtonsoft.Json;
using UnityEngine;

namespace GodTools.Features;

public class CreatureSavedList : IManager
{
    private static readonly string _save_path = Path.Combine(Application.persistentDataPath, "saved_actors.json");
    private const string SavedActorIdKey = C.mod_prefix + ".saved_actor_id";

    public static Dictionary<string, SavedActorPacket> SavedActors;

    private static string GetSavedActorId(ActorData data)
    {
        data.get(SavedActorIdKey, out string id);
        return id;
    }

    private static void SetSavedActorId(ActorData data, string id)
    {
        if (data.custom_data_string?.dict == null)
        {
            data.custom_data_string = new CustomDataContainer<string>();
        }
        data.set(SavedActorIdKey, id);
    }

    private static string GetOrCreateSavedActorId(ActorData data)
    {
        var id = GetSavedActorId(data);
        if (Guid.TryParseExact(id, "N", out _)) return id;
        id = Guid.NewGuid().ToString("N");
        SetSavedActorId(data, id);
        return id;
    }

    private static void Persist()
    {
        File.WriteAllText(_save_path, JsonConvert.SerializeObject(SavedActors));
    }

    public static bool ActorSaved(Actor actor)
    {
        var id = GetSavedActorId(actor.data);
        return !string.IsNullOrEmpty(id) && SavedActors.ContainsKey(id);
    }

    public static void SaveActor(Actor actor)
    {
        var id = GetOrCreateSavedActorId(actor.data);
        actor.prepareForSave();
        actor.clan?.save();
        actor.culture?.save();
        actor.language?.save();
        actor.subspecies?.save();
        List<ItemData> item_datas = null;
        if (actor.canUseItems())
        {
            item_datas = new List<ItemData>();
            foreach (var item in actor.equipment.getItems())
            {
                item.save();
                item_datas.Add(item.data.Copy());
            }
        }

        SavedActors[id] = new SavedActorPacket
        {
            ID = id,
            ActorData = actor.data.Copy(),
            ClanData = actor.clan?.data.Copy(),
            CultureData = actor.culture?.data.Copy(),
            LanguageData = actor.language?.data.Copy(),
            SubspeciesData = actor.subspecies?.data.Copy(),
            ItemDatas = item_datas
        };
        Persist();
    }

    public static void UnsaveActor(Actor actor)
    {
        SavedActors.Remove(GetSavedActorId(actor.data));
        Persist();
    }

    public static void UnsaveActor(SavedActorPacket packet)
    {
        SavedActors.Remove(packet.ID);
        Persist();
    }

    public void Initialize()
    {
        try
        {
            SavedActors = JsonConvert.DeserializeObject<Dictionary<string, SavedActorPacket>>(File.ReadAllText(_save_path));
        }
        catch (Exception)
        {
            SavedActors = new Dictionary<string, SavedActorPacket>();
        }
        SavedActors ??= new Dictionary<string, SavedActorPacket>();

        var migrated_actors = new Dictionary<string, SavedActorPacket>();
        var migrated = false;
        foreach (var pair in SavedActors)
        {
            var packet = pair.Value;
            var id = packet.ID;
            if (!Guid.TryParseExact(id, "N", out _) || migrated_actors.ContainsKey(id))
            {
                id = Guid.NewGuid().ToString("N");
                migrated = true;
            }
            if (pair.Key != id || packet.ID != id || GetSavedActorId(packet.ActorData) != id)
            {
                migrated = true;
            }
            packet.ID = id;
            SetSavedActorId(packet.ActorData, id);
            migrated_actors.Add(id, packet);
        }
        SavedActors = migrated_actors;
        if (migrated) Persist();
    }

    private static string _selected_id;

    public class SavedActorPacket
    {
        public string ID;
        public ActorData ActorData;
        public ClanData ClanData;
        public CultureData CultureData;
        public LanguageData LanguageData;
        public SubspeciesData SubspeciesData;
        public List<ItemData> ItemDatas;
    }

    public static void SelectActorToPlace(SavedActorPacket packet)
    {
        _selected_id = packet.ID;
    }
    public static bool SpawnSelectedSavedActor(WorldTile tile, string power_id)
    {
        if (!SavedActors.TryGetValue(_selected_id, out var saved_actor)) return false;
        var subspecies_data = saved_actor.SubspeciesData.Copy();
        var language_data = saved_actor.LanguageData.Copy();
        var culture_data = saved_actor.CultureData.Copy();
        var clan_data = saved_actor.ClanData.Copy();
        var data = saved_actor.ActorData.Copy();
        var item_datas = saved_actor.ItemDatas?.Select(item => item.Copy()).ToList();

        if (subspecies_data != null)
        {
            subspecies_data.id = World.world.map_stats.getNextId("subspecies");
            subspecies_data.created_time = World.world.map_stats.world_time;
            data.subspecies = subspecies_data.id;
            World.world.subspecies.loadObject(subspecies_data);
        }

        if (language_data != null)
        {
            language_data.id = World.world.map_stats.getNextId("language");
            language_data.created_time = World.world.map_stats.world_time;
            data.language = language_data.id;
            World.world.languages.loadObject(language_data);
        }

        if (culture_data != null)
        {
            culture_data.id = World.world.map_stats.getNextId("culture");
            culture_data.created_time = World.world.map_stats.world_time;
            data.culture = culture_data.id;
            World.world.cultures.loadObject(culture_data);
        }

        if (clan_data != null)
        {
            clan_data.id = World.world.map_stats.getNextId("clan");
            clan_data.created_time = World.world.map_stats.world_time;
            data.clan = clan_data.id;
            World.world.clans.loadObject(clan_data);
        }

        if (item_datas != null)
        {
            var item_ids = new Dictionary<long, long>();
            foreach (var item_data in item_datas)
            {
                var old_item_id = item_data.id;
                item_data.id = World.world.map_stats.getNextId("item");
                item_data.created_time = World.world.map_stats.world_time;
                World.world.items.loadObject(item_data);
                item_ids[old_item_id] = item_data.id;
            }
            for (int i = 0; i < data.saved_items.Count; i++)
            {
                if (item_ids.TryGetValue(data.saved_items[i], out var new_item_id))
                {
                    data.saved_items[i] = new_item_id;
                }
            }
        }

        data.id = World.world.map_stats.getNextId("unit");


        if (World.world.cities.get(data.cityID) == null) data.cityID = -1;

        data.homeBuildingID = -1;
        data.transportID = -1;
        data.x = tile.pos.x;
        data.y = tile.pos.y;
        var actor = World.world.units.loadObject(data);
        actor.setStatsDirty();
        return true;
    }
}
