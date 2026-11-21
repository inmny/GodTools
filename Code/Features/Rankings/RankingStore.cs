using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GodTools.Abstract;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GodTools.Features.Rankings;

public sealed class RankingPreset
{
    public string ID;
    public bool Favorite;
    public int PreviewCount = RankingPreview.DefaultLeaderCount;
    public string Name;
    public string BoardID;
    public List<RankingToken> Conditions = new();
    public List<RankingToken> DisplayConditions = new();
    public List<RankingSort> Sort = new();
    public List<string> PinnedColumns = new();
    public RankingRowLayout RowLayout;
}

internal sealed class RankingPeak
{
    public string ActorID;
    public string ActorName;
    public double Score;
    public double Time;
    public Dictionary<string, double> Parts;
    public string Details;
    public CreatureSavedList.SavedActorPacket Creature;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public RankingPeakPortrait Portrait;
}

internal sealed class RankingPeakBoard
{
    public string ProfileID;
    public string Name;
    public int Version;
    public List<RankingPeak> Entries = new();
}

internal sealed class RankingStore : IManager
{
    private sealed class Document
    {
        public int Version = 1;
        public List<RankingPreset> Presets = new();
        public List<RankingPeakBoard> Peaks = new();
        public List<RankingArchive> Archives = new();
        public Dictionary<string, RankingPreview> Previews = new(StringComparer.Ordinal);
    }

    private const string FileName = "godtools_rankings.json";
    private static readonly JsonSerializerSettings Settings = new() { Converters = { new StringEnumConverter() } };
    private static Document _world = new();
    private static Document _pending;
    internal static List<RankingPreset> Presets => _world.Presets;
    internal static IEnumerable<RankingPeakBoard> Peaks => _world.Peaks;
    internal static List<RankingArchive> Archives => _world.Archives;
    internal static Dictionary<string, RankingPreview> Previews => _world.Previews;
    internal static object WorldIdentity => _world;

    public void Initialize() => Harmony.CreateAndPatchAll(typeof(RankingStore));

    internal static RankingPreset Copy(RankingPreset preset) =>
        JsonConvert.DeserializeObject<RankingPreset>(JsonConvert.SerializeObject(preset, Settings), Settings);

    internal static RankingPeakBoard FindPeaks(string id, int version) =>
        _world.Peaks.Find(item => item.ProfileID == id && item.Version == version);

    internal static void Record(RankingPowerProfile profile, RankingDataset sample, string fieldID)
    {
        var samples = sample.Rows.Where(row => row.Values.GetValueOrDefault(fieldID)?.Number != null).ToArray();
        if (samples.Length == 0) return;
        var board = FindPeaks(profile.ID, profile.Version);
        if (board == null)
        {
            board = new RankingPeakBoard { ProfileID = profile.ID, Version = profile.Version, Name = profile.Name };
            _world.Peaks.Add(board);
        }
        var previousEntries = board.Entries.ToDictionary(entry => entry.ActorID, StringComparer.Ordinal);
        var entries = new Dictionary<string, RankingPeak>(previousEntries, StringComparer.Ordinal);
        foreach (var row in samples)
        {
            var value = row.Values[fieldID];
            double score = value.Number.Value;
            if (entries.GetValueOrDefault(row.Key) is RankingPeak previous && previous.Score >= score) continue;
            entries[row.Key] = new RankingPeak
            {
                ActorID = row.Key, ActorName = row.Name, Score = score, Time = sample.WorldTime,
                Parts = value.Parts?.ToDictionary(part => part.Key, part => part.Value), Details = value.Details
            };
        }
        board.Entries = entries.Values.OrderByDescending(item => item.Score).ThenBy(item => item.ActorID, StringComparer.Ordinal)
            .Take(ConsistentCreaturePowerTop.MaxCount).ToList();
        if (profile.ID == StandardDuelPower.ProfileID && profile.Version == StandardDuelPower.Version)
        {
            foreach (var entry in board.Entries)
            {
                if (ReferenceEquals(entry, previousEntries.GetValueOrDefault(entry.ActorID)) ||
                    BuiltInRankings.FindActor(entry.ActorID) is not Actor actor) continue;
                try { entry.Creature = CreatureSavedList.Capture(actor); }
                catch (Exception error) { Main.LogWarning($"Could not save creature at its combat peak '{entry.ActorID}': {error}"); }
                try { entry.Portrait = RankingPeakPortrait.Capture(actor); }
                catch (Exception error) { Main.LogWarning($"Could not capture peak portrait '{entry.ActorID}': {error.Message}"); }
            }
            foreach (var replaced in previousEntries.Values.Except(board.Entries)) replaced.Portrait?.Release();
        }
        RankingPowerProfiles.RegisterPeakBoard(board);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData))]
    private static void Read(string pPath)
    {
        _pending = new Document();
        if (string.IsNullOrEmpty(pPath)) return;
        string path = Path.Combine(pPath, FileName);
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path), Settings);
            if (data?.Version != 1) throw new InvalidDataException("Unknown rankings save version");
            data.Presets ??= new();
            data.Presets.RemoveAll(preset => string.IsNullOrWhiteSpace(preset?.BoardID) ||
                string.IsNullOrWhiteSpace(preset.Name) || preset.Conditions == null || preset.Sort == null);
            var changedPreviews = ConsolidatePresets(data.Presets);
            data.Archives = (data.Archives ?? new()).Where(archive => archive?.ID != null && archive.View?.BoardID != null
                && archive.Rows != null && archive.Schema != null).ToList();
            data.Previews ??= new(StringComparer.Ordinal);
            var favorites = new HashSet<string>(data.Presets.Where(preset => preset.Favorite).Select(preset => preset.ID),
                StringComparer.Ordinal);
            foreach (string id in data.Previews.Keys.ToArray())
            {
                var preview = data.Previews[id];
                if (!favorites.Contains(id) || changedPreviews.Contains(id) || preview?.Leaders == null ||
                    preview.Leaders.Any(row => row?.Values == null))
                    data.Previews.Remove(id);
            }
            data.Peaks ??= new();
            data.Peaks = data.Peaks.Where(board => !string.IsNullOrWhiteSpace(board?.ProfileID) && board.Version > 0)
                .GroupBy(board => (board.ProfileID, board.Version)).Select(group => group.First()).ToList();
            foreach (var board in data.Peaks)
                board.Entries = (board.Entries ?? new()).Where(entry => !string.IsNullOrWhiteSpace(entry?.ActorID) &&
                    !double.IsNaN(entry.Score) && !double.IsInfinity(entry.Score))
                    .GroupBy(entry => entry.ActorID).Select(group => group.OrderByDescending(entry => entry.Score).First())
                    .OrderByDescending(entry => entry.Score).ThenBy(entry => entry.ActorID, StringComparer.Ordinal)
                    .Take(ConsistentCreaturePowerTop.MaxCount).ToList();
            _pending = data;
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not read rankings from '{path}': {exception}");
        }
    }

    private static HashSet<string> ConsolidatePresets(IEnumerable<RankingPreset> presets)
    {
        var changedPreviews = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<(string Board, string Name)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in presets)
        {
            preset.DisplayConditions ??= new();
            preset.PreviewCount = RankingPreview.ClampCount(preset.PreviewCount);
            preset.PinnedColumns ??= new();
            preset.Sort.RemoveAll(key => key == null);
            if (string.IsNullOrEmpty(preset.ID) || !ids.Add(preset.ID))
            {
                preset.ID = Guid.NewGuid().ToString("N");
                ids.Add(preset.ID);
            }
            preset.BoardID = preset.BoardID switch
            {
                "godtools:actor-power" or "godtools:actor-kills" or "godtools:actor-age" or "godtools:actor-damage" or "cultiway:realm"
                    => BuiltInRankings.Actors,
                "godtools:item-kills" => BuiltInRankings.Items,
                "godtools:culture-population" => BuiltInRankings.Cultures,
                var id when id.StartsWith("godtools:power:", StringComparison.Ordinal) => BuiltInRankings.Actors,
                _ => preset.BoardID
            };
            if (preset.BoardID == BuiltInRankings.Actors)
            {
                foreach (var sort in preset.Sort)
                {
                    if (sort.Field != StandardDuelPower.BaseFieldID && sort.Field != StandardDuelPower.PreviousFieldID) continue;
                    sort.Field = StandardDuelPower.FieldID;
                    changedPreviews.Add(preset.ID);
                }
                var sortFields = new HashSet<string>(StringComparer.Ordinal);
                preset.Sort.RemoveAll(sort => !sortFields.Add(sort.Field));
                foreach (var token in preset.Conditions.Concat(preset.DisplayConditions))
                {
                    var condition = token?.Condition;
                    if (condition?.Field is StandardDuelPower.BaseFieldID or StandardDuelPower.PreviousFieldID)
                        changedPreviews.Add(preset.ID);
                    // Preserve base-score thresholds for editing because the scores use a different scale.
                    if (condition?.Field == StandardDuelPower.PreviousFieldID ||
                        condition?.Field == StandardDuelPower.BaseFieldID &&
                        condition.Operator is RankingOperator.Present or RankingOperator.Absent)
                        condition.Field = StandardDuelPower.FieldID;
                    if (condition?.Field == "cultiway:book_grade" && condition.Operator == RankingOperator.Equal && condition.Value == "none")
                    {
                        condition.Operator = RankingOperator.Absent;
                        condition.Value = condition.Label = null;
                    }
                }
                preset.PinnedColumns = preset.PinnedColumns.Select(field =>
                        field is StandardDuelPower.BaseFieldID or StandardDuelPower.PreviousFieldID ? StandardDuelPower.FieldID : field)
                    .Distinct(StringComparer.Ordinal).ToList();
            }
            string original = preset.Name;
            for (int i = 2; !names.Add((preset.BoardID, preset.Name)); i++)
                preset.Name = $"{original} ({i})";
        }
        return changedPreviews;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void Clear()
    {
        foreach (var board in _world.Peaks)
        foreach (var entry in board.Entries)
            entry.Portrait?.Release();
        _world = new Document();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.finishingUpLoading))]
    private static void Ready()
    {
        if (_pending == null) return;
        _world = _pending;
        _pending = null;
        foreach (var board in _world.Peaks) RankingPowerProfiles.RegisterPeakBoard(board);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveMapData))]
    private static void Save(string pFolder)
    {
        string path = Path.Combine(SaveManager.folderPath(pFolder), FileName);
        if (_world.Presets.Count == 0 && _world.Peaks.Count == 0 && _world.Archives.Count == 0 && !File.Exists(path)) return;
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonConvert.SerializeObject(_world, Formatting.Indented, Settings));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Could not save rankings to '{path}': {exception}");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException exception) { Main.LogWarning($"Could not remove temporary rankings file '{temp}': {exception.Message}"); }
            catch (UnauthorizedAccessException exception) { Main.LogWarning($"Could not remove temporary rankings file '{temp}': {exception.Message}"); }
        }
    }
}
