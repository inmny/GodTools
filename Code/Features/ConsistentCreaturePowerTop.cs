using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Abstract;
using GodTools.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using strings;
using UnityEngine;

namespace GodTools.Features;

public class ConsistentCreaturePowerTop : IManager
{
    public class CreaturePowerData
    {
        public long ID;
        public string Name;
        public double Power;
        public Sprite Sprite;
        public ActorData Data;
        public LanguageData LanguageData;
        public ClanData ClanData;
        public CultureData CultureData;
        public SubspeciesData SubspeciesData;
        public List<ItemData> ItemDatas;
    }

    public const int MaxCount = 100;
    private const string SnapshotFilePrefix = "consistent_creature_power_top";
    private const string SnapshotPointerFileName = SnapshotFilePrefix + ".current";
    private static readonly object _data_lock = new();
    private static List<long> _sortedCreaturesInTop = new();
    private static Dictionary<long, CreaturePowerData> _creaturePowerData = new();
    private static long _select_id;

    public static void SelectCreatureToPlace(long id)
    {
        lock (_data_lock)
        {
            _select_id = id;
        }
    }

    public static bool SpawnSelectedSavedActor(WorldTile tile, string power_id)
        => CreatureSavedList.Place(GetSavedCreature(_select_id), tile);

    internal static CreatureSavedList.SavedActorPacket GetSavedCreature(long id)
    {
        lock (_data_lock)
        {
            if (!_creaturePowerData.TryGetValue(id, out var entry)) return null;
            return new CreatureSavedList.SavedActorPacket
            {
                ActorData = entry.Data, SubspeciesData = entry.SubspeciesData, LanguageData = entry.LanguageData,
                CultureData = entry.CultureData, ClanData = entry.ClanData, ItemDatas = entry.ItemDatas
            };
        }
    }

    public static List<CreaturePowerData> GetSortedCreaturePowerData()
    {
        lock (_data_lock)
        {
            TrimOverflowLocked();
            return _sortedCreaturesInTop
                .Select(id => CopyForRead(_creaturePowerData[id]))
                .ToList();
        }
    }

    internal static bool HasRecords()
    {
        lock (_data_lock) return _creaturePowerData.Count > 0;
    }

    public static void Check(Actor actor)
    {
        lock (_data_lock)
        {
            CheckLocked(actor);
        }
    }

    private static void CheckLocked(Actor actor)
    {
        var power = CalculatePower(actor);
        try
        {
            actor.getName();
        }
        catch
        {
            Main.LogInfo($"Failed to get name for {actor.data.id}");
        }

        if (_creaturePowerData.TryGetValue(actor.data.id, out var data))
        {
            try
            {
                data.Name = actor.getName();
            }
            catch
            {
                data.Name = "无法获取";
            }

            data.Data.name = data.Name;

            if (power > data.Power)
            {
                data.Power = power;
                data.Sprite = actor._last_colored_sprite ?? actor.asset.getSpriteIcon();
                actor.prepareForSave();
                actor.clan?.save();
                actor.language?.save();
                actor.subspecies?.save();
                actor.culture?.save();
                if (actor.canUseItems())
                {
                    var items = new List<ItemData>();
                    foreach (var item in actor.equipment.getItems())
                    {
                        item.save();
                        items.Add(item.data.Copy());
                    }
                    data.ItemDatas = items;
                }
                data.Data = actor.data.Copy();
                data.ClanData = actor.clan?.data.Copy();
                data.CultureData = actor.culture?.data.Copy();
                data.LanguageData = actor.language?.data.Copy();
                data.SubspeciesData = actor.subspecies?.data.Copy();
                SortLocked();
            }

            return;
        }

        var lowest_power = _sortedCreaturesInTop.Count > 0 ? _creaturePowerData[_sortedCreaturesInTop[0]].Power : -1;
        if (power < lowest_power && _sortedCreaturesInTop.Count >= MaxCount)
        {
            return;
        }

        var actor_name = "无法获取";
        try
        {
            actor_name = actor.getName();
        }
        catch
        {
            // ignore
        }

        actor.prepareForSave();
        actor.clan?.save();
        actor.language?.save();
        actor.subspecies?.save();
        actor.culture?.save();
        var new_data = new CreaturePowerData()
        {
            ID = actor.data.id,
            Name = actor_name,
            Power = power,
            Sprite = actor._last_colored_sprite ?? actor.asset.getSpriteIcon(),
            Data = actor.data.Copy(),
            ClanData = actor.clan?.data.Copy(),
            CultureData = actor.culture?.data.Copy(),
            LanguageData = actor.language?.data.Copy(),
            SubspeciesData = actor.subspecies?.data.Copy()
        };
        if (actor.canUseItems())
        {
            var items = new List<ItemData>();
            foreach (var item in actor.equipment.getItems())
            {
                item.save();
                items.Add(item.data.Copy());
            }
            new_data.ItemDatas = items;
        }

        if (_sortedCreaturesInTop.Count >= MaxCount)
        {
            _creaturePowerData.Remove(_sortedCreaturesInTop[0]);
        }
        _creaturePowerData.Add(actor.data.id, new_data);
        SortLocked();
    }

    private static void SortLocked()
    {
        _sortedCreaturesInTop.Clear();
        _sortedCreaturesInTop.AddRange(_creaturePowerData.Keys);
        _sortedCreaturesInTop.Sort((a, b) => _creaturePowerData[a].Power.CompareTo(_creaturePowerData[b].Power));
        TrimOverflowLocked();
    }

    private static void TrimOverflowLocked()
    {
        while (_sortedCreaturesInTop.Count > MaxCount)
        {
            var id = _sortedCreaturesInTop[0];
            _sortedCreaturesInTop.RemoveAt(0);
            _creaturePowerData.Remove(id);
        }
    }

    private static CreaturePowerData CopyForRead(CreaturePowerData source)
    {
        return new CreaturePowerData
        {
            ID = source.ID,
            Name = source.Name,
            Power = source.Power,
            Sprite = source.Sprite,
            Data = source.Data,
            LanguageData = source.LanguageData,
            ClanData = source.ClanData,
            CultureData = source.CultureData,
            SubspeciesData = source.SubspeciesData,
            ItemDatas = source.ItemDatas
        };
    }

    public static double CalculatePower(Actor actor) => CalculatePower(actor, out _, out _);

    internal static double CalculatePower(Actor actor, out float dps, out float live_time)
    {
        live_time = actor.stats[S.health] / Mathf.Max(1f, 100 - actor.stats[S.armor]);
        var attack_speed = actor.asset.skip_fight_logic
            ? 1f
            : (1f / Mathf.Clamp(actor.getAttackCooldown(), 0.016f, 100));
        var attack_fluctuation = (actor.stats[S.damage_range] + 1) * 0.5f;
        var critical_bonus = Mathf.Max(0, actor.stats[S.critical_chance]) *
                             Mathf.Max(0, actor.stats[S.critical_damage_multiplier] - 1);
        dps = attack_speed * Mathf.Max(1f, actor.stats[S.damage]) *
                  attack_fluctuation * (1 + critical_bonus);

        return ((double)live_time) * ((double)dps) * 0.182699;
    }

    public void Initialize()
    {
        Harmony.CreateAndPatchAll(typeof(ConsistentCreaturePowerTop));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void OnCleanUp()
    {
        lock (_data_lock)
        {
            _creaturePowerData.Clear();
            _sortedCreaturesInTop.Clear();
            _select_id = 0;
        }
        ReleaseLoadedVisuals();
    }

    private struct RawRectInt(int posX, int posY, int fitWidth, int fitHeight)
    {
        public int x = posX;
        public int y = posY;
        public int width = fitWidth;
        public int height = fitHeight;
    }

    private struct CreaturePowerDataForSave
    {
        public long ID;
        public string Name;
        public double Power;
        public RawRectInt SpriteRect;
        public ActorData Data;
        public LanguageData LanguageData;
        public ClanData ClanData;
        public CultureData CultureData;
        public SubspeciesData SubspeciesData;
        public List<ItemData> ItemDatas;
    }

    private static (MapStats Map, string Folder) _pendingLoad;
    private static readonly List<Sprite> _loaded_sprites = new();
    private static Texture2D _loaded_texture;

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData))]
    private static void QueueLoad(SavedMap pData, string pPath)
    {
        _pendingLoad = string.IsNullOrEmpty(pPath) ? default : (pData.mapStats, SaveManager.folderPath(pPath));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.finishingUpLoading))]
    private static void LoadToWorld(MapBox __instance)
    {
        var (map, folder) = _pendingLoad;
        _pendingLoad = default;
        if (map == null || !ReferenceEquals(map, __instance.map_stats)) return;

        Texture2D texture = null;
        try
        {
            if (!TryGetSnapshotPaths(folder, out var listPath, out var texturePath)) return;
            var entries = JsonConvert.DeserializeObject<List<CreaturePowerDataForSave>>(
                File.ReadAllText(listPath), GeneralTools.private_members_visit_settings)
                ?? throw new InvalidDataException("生物战力榜存档内容为空");
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(texturePath), true))
                throw new InvalidDataException("生物战力榜贴图无法读取");

            lock (_data_lock)
            {
                _loaded_texture = texture;
                texture = null;
                foreach (var entry in entries)
                {
                    if (entry.Data == null || _creaturePowerData.ContainsKey(entry.ID)) continue;
                    var rect = entry.SpriteRect;
                    if (rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0 ||
                        rect.x + rect.width > _loaded_texture.width || rect.y + rect.height > _loaded_texture.height)
                        throw new InvalidDataException("生物战力榜贴图区域越界");
                    var sprite = Sprite.Create(_loaded_texture, new Rect(rect.x, rect.y, rect.width, rect.height),
                        new Vector2(0.5f, 0), 1);
                    _loaded_sprites.Add(sprite);
                    _creaturePowerData.Add(entry.ID, new CreaturePowerData
                    {
                        ID = entry.ID, Name = entry.Name, Power = entry.Power, Sprite = sprite,
                        Data = entry.Data, ClanData = entry.ClanData, CultureData = entry.CultureData,
                        LanguageData = entry.LanguageData, SubspeciesData = entry.SubspeciesData, ItemDatas = entry.ItemDatas
                    });
                }
                SortLocked();
            }
        }
        catch (Exception error)
        {
            OnCleanUp();
            Main.LogWarning($"Ignoring invalid creature power-top snapshot: {error.Message}");
        }
        finally
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveMapData))]
    private static void Save(string pFolder)
    {
        pFolder = SaveManager.folderPath(pFolder);
        var (save_data, sprites) = CaptureSaveSnapshot();
        byte[] texture_bytes;
        Texture2D texture = null;
        try
        {
            var combined = CombineSprites(sprites);
            texture = combined.Item1;
            var rects = combined.Item2;
            for (int i = 0; i < save_data.Count; i++)
            {
                var entry = save_data[i];
                entry.SpriteRect = rects[i];
                save_data[i] = entry;
            }

            texture_bytes = texture.EncodeToPNG();
        }
        finally
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        var generation = Guid.NewGuid().ToString("N");
        var list_save_path = Path.Combine(pFolder, $"{SnapshotFilePrefix}.{generation}.json");
        var texture_save_path = Path.Combine(pFolder, $"{SnapshotFilePrefix}.{generation}.png");
        var list_temp_path = list_save_path + ".tmp";
        var texture_temp_path = texture_save_path + ".tmp";
        var pointer_path = Path.Combine(pFolder, SnapshotPointerFileName);
        var pointer_temp_path = pointer_path + "." + generation + ".tmp";
        var committed = false;
        try
        {
            File.WriteAllText(list_temp_path,
                JsonConvert.SerializeObject(save_data, Formatting.None, GeneralTools.private_members_visit_settings));
            File.WriteAllBytes(texture_temp_path, texture_bytes);
            File.Move(list_temp_path, list_save_path);
            File.Move(texture_temp_path, texture_save_path);
            File.WriteAllText(pointer_temp_path, generation);
            if (File.Exists(pointer_path))
            {
                File.Replace(pointer_temp_path, pointer_path, null);
            }
            else
            {
                File.Move(pointer_temp_path, pointer_path);
            }
            committed = true;
        }
        finally
        {
            TryDeleteFile(list_temp_path);
            TryDeleteFile(texture_temp_path);
            TryDeleteFile(pointer_temp_path);
            if (!committed)
            {
                TryDeleteFile(list_save_path);
                TryDeleteFile(texture_save_path);
            }
        }

        CleanupOldSnapshotGenerations(pFolder, generation);
    }

    private static bool TryGetSnapshotPaths(string folder, out string listPath, out string texturePath)
    {
        var pointer_path = Path.Combine(folder, SnapshotPointerFileName);
        if (File.Exists(pointer_path))
        {
            var generation = File.ReadAllText(pointer_path).Trim();
            if (!Guid.TryParseExact(generation, "N", out _))
            {
                throw new InvalidDataException("生物战力榜存档指针无效");
            }

            listPath = Path.Combine(folder, $"{SnapshotFilePrefix}.{generation}.json");
            texturePath = Path.Combine(folder, $"{SnapshotFilePrefix}.{generation}.png");
            if (!File.Exists(listPath) || !File.Exists(texturePath))
            {
                throw new InvalidDataException("生物战力榜存档世代不完整");
            }
            return true;
        }

        listPath = Path.Combine(folder, SnapshotFilePrefix + ".json");
        texturePath = Path.Combine(folder, SnapshotFilePrefix + ".png");
        var has_list = File.Exists(listPath);
        var has_texture = File.Exists(texturePath);
        if (has_list != has_texture)
        {
            throw new InvalidDataException("旧版生物战力榜存档不完整");
        }
        return has_list;
    }

    private static void CleanupOldSnapshotGenerations(string folder, string currentGeneration)
    {
        foreach (var extension in new[] { "json", "png" })
        {
            foreach (var path in Directory.GetFiles(folder, $"{SnapshotFilePrefix}.*.{extension}"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var generation = name.Substring(SnapshotFilePrefix.Length + 1);
                if (generation == currentGeneration || !Guid.TryParseExact(generation, "N", out _)) continue;
                TryDeleteFile(path);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Main.LogWarning($"Failed to remove stale creature power-top file '{path}': {e.Message}");
        }
    }

    private static (List<CreaturePowerDataForSave> SaveData, List<Sprite> Sprites) CaptureSaveSnapshot()
    {
        lock (_data_lock)
        {
            TrimOverflowLocked();
            var save_data = new List<CreaturePowerDataForSave>(_sortedCreaturesInTop.Count);
            var sprites = new List<Sprite>(_sortedCreaturesInTop.Count);
            foreach (var id in _sortedCreaturesInTop)
            {
                var data = _creaturePowerData[id];
                save_data.Add(new CreaturePowerDataForSave
                {
                    ID = data.ID,
                    Name = data.Name,
                    Power = data.Power,
                    Data = data.Data?.Copy(),
                    LanguageData = data.LanguageData?.Copy(),
                    ClanData = data.ClanData?.Copy(),
                    CultureData = data.CultureData?.Copy(),
                    SubspeciesData = data.SubspeciesData?.Copy(),
                    ItemDatas = data.ItemDatas?.Select(item => item.Copy()).ToList()
                });
                sprites.Add(data.Sprite);
            }

            return (save_data, sprites);
        }
    }

    private static (Texture2D, List<RawRectInt>) CombineSprites(List<Sprite> sprites)
    {
        if (sprites.Count == 0)
        {
            var empty_texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            empty_texture.SetPixel(0, 0, Color.clear);
            empty_texture.Apply(false, false);
            return (empty_texture, new List<RawRectInt>());
        }

        var sizes = new List<Vector2Int>(sprites.Count);
        for (int i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite == null || sprite.texture == null)
            {
                throw new InvalidOperationException($"战力榜第 {i + 1} 个条目没有可保存的贴图");
            }

            var sprite_rect = sprite.textureRect;
            var size = new Vector2Int((int)sprite_rect.width, (int)sprite_rect.height);
            if (size.x <= 0 || size.y <= 0)
            {
                throw new InvalidOperationException($"战力榜第 {i + 1} 个条目的贴图区域无效");
            }
            sizes.Add(size);
        }

        (var positions, var tex_size) = CalcLayout(sprites, sizes);
        var combined_texture = new Texture2D(
            tex_size.x,
            tex_size.y,
            TextureFormat.RGBA32,
            false);
        try
        {
            combined_texture.SetPixels32(new Color32[tex_size.x * tex_size.y]);
            var rects = new List<RawRectInt>(sprites.Count);
            var source_groups = new Dictionary<Texture2D, List<int>>();
            for (int i = 0; i < sprites.Count; i++)
            {
                var pos = positions[i];
                var size = sizes[i];
                if (pos.x < 0 || pos.y < 0 ||
                    pos.x + size.x > tex_size.x ||
                    pos.y + size.y > tex_size.y)
                {
                    throw new InvalidOperationException($"战力榜第 {i + 1} 个条目的贴图布局越界");
                }

                var source_texture = sprites[i].texture;
                if (!source_groups.TryGetValue(source_texture, out var indices))
                {
                    indices = new List<int>();
                    source_groups.Add(source_texture, indices);
                }
                indices.Add(i);
                rects.Add(new RawRectInt(pos.x, pos.y, size.x, size.y));
            }

            var previous_active = RenderTexture.active;
            try
            {
                foreach (var pair in source_groups)
                {
                    var source_texture = pair.Key;
                    RenderTexture temporary = null;
                    try
                    {
                        temporary = RenderTexture.GetTemporary(
                            source_texture.width,
                            source_texture.height,
                            0,
                            RenderTextureFormat.Default,
                            source_texture.isDataSRGB
                                ? RenderTextureReadWrite.sRGB
                                : RenderTextureReadWrite.Linear);
                        Graphics.Blit(source_texture, temporary);
                        RenderTexture.active = temporary;
                        foreach (var index in pair.Value)
                        {
                            var pos = positions[index];
                            combined_texture.ReadPixels(sprites[index].textureRect, pos.x, pos.y, false);
                        }
                    }
                    finally
                    {
                        RenderTexture.active = previous_active;
                        if (temporary != null)
                        {
                            RenderTexture.ReleaseTemporary(temporary);
                        }
                    }
                }
            }
            finally
            {
                RenderTexture.active = previous_active;
            }

            combined_texture.Apply(false, false);
            return (combined_texture, rects);
        }
        catch
        {
            UnityEngine.Object.Destroy(combined_texture);
            throw;
        }
    }

    private static void ReleaseLoadedVisuals()
    {
        Sprite[] sprites;
        Texture2D texture;
        lock (_data_lock)
        {
            sprites = _loaded_sprites.ToArray();
            _loaded_sprites.Clear();
            texture = _loaded_texture;
            _loaded_texture = null;
        }

        foreach (var sprite in sprites)
        {
            if (sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
            }
        }
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    private static int CalculateRequiredHeight(List<Vector2Int> sizes, int width)
    {
        int current_x = 0;
        int current_y = 0;
        int row_max_height = 0;

        foreach (var size in sizes)
        {
            if (current_x + size.x > width)
            {
                current_x = 0;
                current_y += row_max_height;
                row_max_height = 0;
            }

            current_x += size.x;
            row_max_height = Mathf.Max(row_max_height, size.y);
        }

        current_y += row_max_height;

        return current_y;
    }

    private const int SuggestedWidth = 1024;
    private const int SuggestedHeight = 1024;

    private static (List<Vector2Int>, Vector2Int) CalcLayout(List<Sprite> sprites, List<Vector2Int> sizes)
    {
        int total_width = 0;
        int total_height = 0;
        int max_sprite_width = 0;
        int max_sprite_height = 0;

        foreach (var size in sizes)
        {
            total_width += size.x;
            total_height += size.y;
            max_sprite_width = Mathf.Max(max_sprite_width, size.x);
            max_sprite_height = Mathf.Max(max_sprite_height, size.y);
        }

        int current_width = Mathf.Clamp(max_sprite_width, max_sprite_width, SuggestedWidth);
        if (total_width < SuggestedWidth)
        {
            current_width = total_width;
        }

        int current_height = CalculateRequiredHeight(sizes, current_width);

        while (current_height > SuggestedHeight && current_width < SuggestedWidth)
        {
            current_width = Mathf.Min((int)(current_width * 1.5f), SuggestedWidth);
            current_height = CalculateRequiredHeight(sizes, current_width);
        }

        List<Vector2Int> positions =
            CalculateSimpleLayout(sprites, sizes, new Vector2Int(current_width, current_height));

        return (positions, new Vector2Int(current_width, current_height));
    }

    private static List<Vector2Int> CalculateSimpleLayout(List<Sprite> sprites, List<Vector2Int> sizes,
        Vector2Int textureSize)
    {
        List<Vector2Int> positions = new List<Vector2Int>();
        int current_x = 0;
        int current_y = 0;
        int row_max_height = 0;

        for (int i = 0; i < sprites.Count; i++)
        {
            var size = sizes[i];

            if (current_x + size.x > textureSize.x)
            {
                current_x = 0;
                current_y += row_max_height;
                row_max_height = 0;
            }

            positions.Add(new Vector2Int(current_x, current_y));

            current_x += size.x;
            row_max_height = Mathf.Max(row_max_height, size.y);
        }

        return positions;
    }
}
