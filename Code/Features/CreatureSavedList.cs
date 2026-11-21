using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Abstract;
using GodTools.Libraries;
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
        var packet = Capture(actor);
        packet.ID = id;
        SavedActors[id] = packet;
        Persist();
    }

    internal static SavedActorPacket Capture(Actor actor)
    {
        actor.prepareForSave();
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

        var data = actor.data.Copy();
        data.saved_items = item_datas is { Count: > 0 } ? item_datas.Select(item => item.id).ToList() : null;
        return new SavedActorPacket
        {
            ActorData = data,
            AgeAtCapture = actor.getAge(),
            ClanData = actor.clan?.data is { } clan ? new ClanData
            {
                name = clan.name, color_id = clan.color_id,
                banner_background_id = clan.banner_background_id, banner_icon_id = clan.banner_icon_id
            } : null,
            CultureData = actor.culture?.data is { } culture ? new CultureData
            {
                name = culture.name, color_id = culture.color_id,
                banner_decor_id = culture.banner_decor_id, banner_element_id = culture.banner_element_id
            } : null,
            LanguageData = actor.language?.data is { } language ? new LanguageData
            {
                name = language.name, color_id = language.color_id,
                banner_background_id = language.banner_background_id, banner_icon_id = language.banner_icon_id
            } : null,
            SubspeciesData = actor.subspecies?.data.Copy(),
            ItemDatas = item_datas,
            ModData = CreatureSaveExtensions.Capture(actor)
        };
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
        AppDomain.CurrentDomain.SetData("GodTools.CreatureSaveExtensions/v1", typeof(CreatureSaveExtensions));
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
            if (packet?.ActorData == null) continue;
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

    private static Func<SavedActorPacket> _selected;
    private static MapStats _selectedWorld;

    public class SavedActorPacket
    {
        public string ID;
        public ActorData ActorData;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public int? AgeAtCapture;
        public ClanData ClanData;
        public CultureData CultureData;
        public LanguageData LanguageData;
        public SubspeciesData SubspeciesData;
        public List<ItemData> ItemDatas;
        [JsonProperty] internal Dictionary<string, CreatureSaveExtensions.Payload> ModData;
    }

    public static void SelectActorToPlace(SavedActorPacket packet)
    {
        _selected = () => SavedActors.GetValueOrDefault(packet.ID);
        _selectedWorld = World.world?.map_stats;
    }

    internal static void SelectSnapshotToPlace(SavedActorPacket packet)
    {
        _selected = () => packet;
        _selectedWorld = World.world?.map_stats;
    }

    public static bool SpawnSelectedSavedActor(WorldTile tile, string power_id) =>
        _selectedWorld != null && _selectedWorld == World.world?.map_stats &&
        Place(_selected?.Invoke(), tile, power_id == GodPowers.place_saved_actor_in_village.id);

    internal static bool CanPlace(SavedActorPacket packet) => packet?.ActorData?.asset_id is { Length: > 0 } id &&
        AssetManager.actor_library.get(id) != null;

    internal static bool CanPlaceInVillage(SavedActorPacket packet) => CanPlace(packet) &&
        AssetManager.actor_library.get(packet.ActorData.asset_id).canBecomeSapient();

    internal static bool Place(SavedActorPacket saved_actor, WorldTile tile, bool inVillage = false)
    {
        var world = World.world;
        if (world == null || tile == null || world.GetTile(tile.x, tile.y) != tile || !CanPlace(saved_actor)) return false;
        City village = null;
        if (inVillage)
        {
            if (!CanPlaceInVillage(saved_actor)) return false;
            village = tile.zone?.city;
            if (tile.Type.liquid || village?.isAlive() != true || village.kingdom?.isAlive() != true ||
                village.isNeutral() || world.cities.get(village.id) != village)
            {
                WorldTip.showNowTop("inmny.godtools.creature_placement.village_invalid", true);
                return false;
            }
        }
        var data = saved_actor.ActorData.Copy();
        var item_datas = saved_actor.ItemDatas?.Select(item => item.Copy()).ToList();
        data.subspecies = SavedCreatureSubspecies.Resolve(world, data.asset_id, saved_actor.SubspeciesData);

        if (data.saved_items != null)
        {
            var item_ids = new Dictionary<long, long>();
            foreach (var item_data in item_datas ?? Enumerable.Empty<ItemData>())
            {
                var old_item_id = item_data.id;
                if (!data.saved_items.Contains(old_item_id) || item_ids.ContainsKey(old_item_id) ||
                    AssetManager.items.get(item_data.asset_id) == null) continue;
                item_data.modifiers.RemoveAll(id => !AssetManager.items_modifiers.has(id));
                item_data.id = World.world.map_stats.getNextId("item");
                item_data.created_time = World.world.map_stats.world_time;
                World.world.items.loadObject(item_data);
                item_ids[old_item_id] = item_data.id;
            }
            data.saved_items = data.saved_items.Where(item_ids.ContainsKey).Select(id => item_ids[id]).ToList();
        }

        data.id = world.map_stats.getNextId("unit");
        if (saved_actor.AgeAtCapture is int age)
        {
            data.created_time = world.getCurWorldTime();
            data.age_overgrowth = age;
        }
        data.custom_data_string?.dict?.Remove(SavedActorIdKey);

        var resources = data.inventory?.getResources();
        if (resources != null)
            foreach (var resource in resources.Values.Where(resource => !AssetManager.resources.has(resource.id)).ToArray())
                data.inventory = data.inventory.remove(resource.id, resource.amount);

        // 原版会按这些数字在当前地图查找对象，放置副本前清除来源地图的关系和职务。
        data.cityID = data.civ_kingdom_id = data.homeBuildingID = data.transportID = -1;
        data.culture = data.clan = data.language = data.religion = data.name_culture_id = -1;
        data.family = data.army = data.plot = data.ancestor_family = -1;
        data.lover = data.best_friend_id = data.parent_id_1 = data.parent_id_2 = -1;
        data.profession = UnitProfession.Nothing;
        data.x = tile.pos.x;
        data.y = tile.pos.y;
        var actor = world.units.loadObject(data);
        if (actor == null) return false;
        if (village != null) actor.joinCity(village);
        CreatureSaveExtensions.Restore(actor, saved_actor.ModData);
        actor.setStatsDirty();
        return true;
    }
}
