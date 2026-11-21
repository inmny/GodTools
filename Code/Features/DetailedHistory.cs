using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Abstract;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLite;

namespace GodTools.Features;

public class DetailedHistory : IManager
{
    private static readonly List<Entry> Entries = new();
    internal static IReadOnlyList<Entry> Messages => Entries;
    internal static int WorldRevision { get; private set; }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class Entry
    {
        [JsonProperty("asset_id")] public string AssetId;
        [JsonProperty("timestamp")] public int Timestamp;
        [JsonProperty("special1")] public string Special1;
        [JsonProperty("special2")] public string Special2;
        [JsonProperty("special3")] public string Special3;
        [JsonProperty("color_special_1")] public string Color1;
        [JsonProperty("color_special_2")] public string Color2;
        [JsonProperty("color_special_3")] public string Color3;
        [JsonProperty("unit_id")] public long UnitId = -1;
        [JsonProperty("kingdom_id")] public long KingdomId = -1;
        [JsonProperty("x")] public int? X;
        [JsonProperty("y")] public int? Y;
        [JsonProperty("text")] public string SavedText;
        [JsonProperty("icon")] public string SavedIcon;

        internal WorldLogMessage ToMessage() => new()
        {
            asset_id = AssetId, timestamp = Timestamp,
            special1 = Special1, special2 = Special2, special3 = Special3,
            color_special_1 = Color1, color_special_2 = Color2, color_special_3 = Color3,
            unit_id = UnitId, kingdom_id = KingdomId, x = X, y = Y
        };

        internal static Entry Capture(WorldLogMessage message)
        {
            var entry = new Entry
            {
                AssetId = message.asset_id, Timestamp = message.timestamp,
                Special1 = message.special1, Special2 = message.special2, Special3 = message.special3,
                Color1 = message.color_special_1, Color2 = message.color_special_2, Color3 = message.color_special_3,
                UnitId = message.unit_id, KingdomId = message.kingdom_id, X = message.x, Y = message.y
            };
            entry.ReadDisplay(out entry.SavedText, out entry.SavedIcon);
            return entry;
        }

        internal void ReadDisplay(out string text, out string icon)
        {
            text = SavedText ?? string.Join(" · ", new[] { Special1, Special2, Special3 }
                .Where(value => !string.IsNullOrEmpty(value)));
            if (string.IsNullOrEmpty(text)) text = AssetId;
            icon = SavedIcon;
            if (!AssetManager.world_log_library.has(AssetId)) return;
            try
            {
                var message = ToMessage();
                var asset = message.getAsset();
                if (asset == null) return;
                icon = asset.path_icon ?? icon;
                string key = asset.random_ids > 0 ? asset.getLocaleID(Timestamp % asset.random_ids + 1) : asset.getLocaleID();
                string localized = LocalizedTextManager.stringExists(key) ? LocalizedTextManager.getText(key) : "";
                asset.text_replacer?.Invoke(message, ref localized);
                if (!string.IsNullOrWhiteSpace(localized)) text = localized;
            }
            catch (Exception)
            {
                // 缺失的模组资源或无法运行的文本替换器仍可显示保存的正文。
            }
        }
    }

    private sealed class Archive
    {
        public int Version = 1;
        public List<Entry> Events;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(WorldLogMessageExtensions), nameof(WorldLogMessageExtensions.add))]
    private static void Capture(WorldLogMessage pMessage)
    {
        if (!string.IsNullOrEmpty(pMessage?.asset_id)) Entries.Add(Entry.Capture(pMessage));
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveMapData))]
    private static void Save(string pFolder, SavedMap __result)
    {
        if (__result == null || string.IsNullOrEmpty(pFolder)) return;
        string path = Path.Combine(SaveManager.folderPath(pFolder), "worldlog.json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new Archive { Events = Entries }));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception error)
        {
            Main.LogWarning($"Could not save world history: {error.Message}");
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData))]
    private static void Load(string pPath)
    {
        if (!string.IsNullOrEmpty(pPath))
            SmoothLoader.add(() => ReadArchive(pPath), "Loading GodTools world history");
    }

    private static void ReadArchive(string folder)
    {
        string path = Path.Combine(SaveManager.folderPath(folder), "worldlog.json");
        try
        {
            if (File.Exists(path))
            {
                var json = JToken.Parse(File.ReadAllText(path));
                if (json.Type == JTokenType.Object)
                {
                    var archive = json.ToObject<Archive>();
                    if (archive is { Version: 1, Events: not null })
                    {
                        Restore(archive.Events);
                        return;
                    }
                }
                else if (json.Type == JTokenType.Array)
                {
                    var legacy = json.ToObject<List<Entry>>();
                    if (legacy.Any(IsValid))
                    {
                        Restore(legacy);
                        return;
                    }
                }
            }
        }
        catch (Exception error)
        {
            Main.LogWarning($"Could not read world history: {error.Message}");
        }

        try
        {
            // 旧版可能只写下了空对象。原版数据库仍保留最近的有效记录。
            string database = SaveManager.generateStatsPath(folder);
            if (!File.Exists(database)) return;
            using var connection = new SQLiteConnection(database, SQLiteOpenFlags.ReadOnly);
            var rows = connection.Query<WorldLogMessage>(
                "select * from WorldLogMessage order by timestamp asc, rowid asc");
            Restore(rows.Where(row => !string.IsNullOrEmpty(row.asset_id)).Select(Entry.Capture));
        }
        catch (Exception error)
        {
            Main.LogWarning($"Could not recover world history from the game database: {error.Message}");
        }
    }

    private static bool IsValid(Entry entry) => !string.IsNullOrWhiteSpace(entry?.AssetId);

    private static void Restore(IEnumerable<Entry> events)
    {
        Entries.InsertRange(0, events.Where(IsValid));
        WorldRevision++;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void Clear()
    {
        Entries.Clear();
        WorldRevision++;
    }

    public void Initialize() => Harmony.CreateAndPatchAll(typeof(DetailedHistory));
}
