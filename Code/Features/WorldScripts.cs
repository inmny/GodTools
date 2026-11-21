using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using GodTools.Abstract;
using GodTools.Features.WorldScript;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GodTools.Features;

public class WorldScripts : IManager
{
    private const string WorldFileName = "world_scripts.json";
    private static readonly string LegacySavePath = Path.Combine(Application.persistentDataPath, "saved_scripts.json");
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        Converters = { new StringEnumConverter() }
    };
    private static IDictionary<string, JToken> _documentExtensionData = new Dictionary<string, JToken>();
    private static WorldScriptDocument _pendingDocument;
    private static string _pendingPath;
    private static string _preservedWorldScriptPath;
    private static volatile bool _loadingWorld;
    private static string _pendingSaveFolder;
    internal static string ActiveSaveFolder { get; private set; }
    private const string LegacyImportedKey = "legacyScriptsImported";
    public static bool CanImportLegacy => File.Exists(LegacySavePath) && !_documentExtensionData.ContainsKey(LegacyImportedKey);

    public static List<ScriptInstance> Scripts = new();
    public static List<ScriptScenario> Scenarios { get; private set; } = new();
    public static List<ScriptRegion> Regions { get; private set; } = new();
    public static ScriptRegion FindRegion(long id) => Regions.Find(region => region.ID == id);
    public static event Action ScriptsChanged;
    public static event Action<ScriptInstance> ScriptRuntimeChanged;

    public void Initialize()
    {
        Scripts = new List<ScriptInstance>();
        Scenarios = new List<ScriptScenario>();
        Regions = new List<ScriptRegion>();
        var harmony = Harmony.CreateAndPatchAll(typeof(WorldScripts));
        harmony.CreateClassProcessor(typeof(ActorDeath)).Patch();
        WorldScriptHistory.Initialize(harmony);
    }

    public static ScriptInstance NewScript(string name = "New Script")
    {
        return AddScript(new ScriptInstance { Name = name, Enabled = false, IsDraft = true });
    }

    public static ScriptInstance AddScript(ScriptInstance script)
    {
        if (Scripts.Contains(script)) script = Clone(script);
        Normalize(new List<ScriptInstance> { script });
        if (Scripts.Any(existing => existing.ID == script.ID)) script.ID = Guid.NewGuid().ToString("N");
        Scripts.Add(script);
        ScriptsChanged?.Invoke();
        return script;
    }

    internal static void AddPackage(List<ScriptInstance> drafts, ScriptScenario scenario = null)
    {
        Normalize(drafts);
        if (scenario != null) Scenarios.Add(scenario);
        Scripts.AddRange(drafts);
        ScriptsChanged?.Invoke();
    }

    public static ScriptInstance Duplicate(ScriptInstance source, string nameSuffix = " Copy")
    {
        ScriptInstance copy = Clone(source);
        copy.ID = Guid.NewGuid().ToString("N");
        copy.PackageScriptID = null;
        copy.Name += nameSuffix;
        copy.Enabled = false;
        copy.IsDraft = true;
        copy.Runtime = new ScriptRuntimeState();
        return AddScript(copy);
    }

    public static ScriptInstance Clone(ScriptInstance script) =>
        JsonConvert.DeserializeObject<ScriptInstance>(JsonConvert.SerializeObject(script, SerializerSettings), SerializerSettings);

    public static void Apply(ScriptInstance original, ScriptInstance draft, bool enabled)
    {
        int index = Scripts.IndexOf(original);
        if (index < 0) return;
        draft.Enabled = enabled;
        draft.IsDraft = false;
        draft.Runtime = original.Runtime;
        Scripts[index] = draft;
        ScriptsChanged?.Invoke();
    }

    public static void Delete(ScriptInstance script) { Scripts.Remove(script); ScriptsChanged?.Invoke(); }
    public static void NotifyChanged() => ScriptsChanged?.Invoke();
    public static void NotifyRuntimeChanged(ScriptInstance script) => ScriptRuntimeChanged?.Invoke(script);

    [HarmonyPostfix, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadData))]
    private static void LoadDataQueued(string pPath)
    {
        _loadingWorld = true;
        _pendingSaveFolder = string.IsNullOrEmpty(pPath) ? null : Path.GetFullPath(pPath);
        _pendingPath = null;
        _pendingDocument = string.IsNullOrEmpty(pPath)
            ? new WorldScriptDocument()
            : LoadDocument(Path.GetFullPath(Path.Combine(pPath, WorldFileName)), out _pendingPath);
    }

    [HarmonyTranspiler, HarmonyPatch(typeof(SaveManager), nameof(SaveManager.saveMapData))]
    private static IEnumerable<CodeInstruction> AfterSuccessfulMapSave(IEnumerable<CodeInstruction> instructions)
    {
        var move = AccessTools.Method(typeof(Toolbox), nameof(Toolbox.MoveSafely), new[] { typeof(string), typeof(string) });
        var replacement = AccessTools.Method(typeof(WorldScripts), nameof(MoveMapAndSaveScripts));
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(move))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            yield return instruction;
        }
    }

    private static bool MoveMapAndSaveScripts(string source, string destination)
    {
        bool saved = Toolbox.MoveSafely(source, destination);
        if (saved && !_loadingWorld) SaveScripts(Path.GetDirectoryName(destination));
        return saved;
    }

    private static void SaveScripts(string pFolder)
    {
        if (string.IsNullOrEmpty(pFolder)) return;
        try
        {
            string path = Path.GetFullPath(Path.Combine(pFolder, WorldFileName));
            if (string.Equals(path, _preservedWorldScriptPath, StringComparison.Ordinal)) return;
            var document = new WorldScriptDocument
            {
                Scripts = Scripts,
                Scenarios = Scenarios,
                Regions = Regions,
                QueuedEvents = WorldScriptEngine.SaveQueuedEvents(),
                ExtensionData = CloneExtensionData(_documentExtensionData)
            };
            WriteDocument(path, document);
        }
        catch (Exception exception)
        {
            Main.LogWarning($"Failed to save world scripts in '{pFolder}': {exception}");
            WorldTip.showNow("inmny.godtools.ui.world_script.editor.save_failed", true, "top");
        }
    }

    internal static WorldScriptDocument ReadSnapshot(string folder)
    {
        string path = Path.Combine(folder, WorldFileName);
        var snapshot = LoadDocument(path, out var invalid);
        if (!File.Exists(path) || invalid != null) throw new InvalidDataException("Missing or invalid WorldScript snapshot");
        return snapshot;
    }

    internal static void WriteSnapshot(string folder, WorldScriptDocument document) =>
        WriteDocument(Path.Combine(folder, WorldFileName), document);

    private static void WriteDocument(string path, WorldScriptDocument document)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(document, SerializerSettings));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void ClearWorldPostfix()
    {
        WorldScriptRecording.End();
        ActiveSaveFolder = null;
        Scripts = new List<ScriptInstance>();
        Scenarios = new List<ScriptScenario>();
        Regions = new List<ScriptRegion>();
        _documentExtensionData = new Dictionary<string, JToken>();
        _preservedWorldScriptPath = null;
        WorldScriptEngine.Reset();
        ScriptsChanged?.Invoke();
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapBox), nameof(MapBox.finishingUpLoading))]
    private static void WorldReadyPostfix()
    {
        var queued = _pendingDocument?.QueuedEvents;
        if (_pendingDocument != null)
        {
            InstallDocument(_pendingDocument);
            _preservedWorldScriptPath = _pendingPath;
            _pendingDocument = null;
            _pendingPath = null;
        }
        _loadingWorld = false;
        ActiveSaveFolder = _pendingSaveFolder;
        _pendingSaveFolder = null;
        bool resumeCheckpoint = WorldScriptCheckpoints.IsWorkingCopy(ActiveSaveFolder);
        if (resumeCheckpoint) Config.paused = true;
        WorldScriptEngine.OnWorldLoaded(resumeCheckpoint);
        WorldScriptEngine.RestoreQueuedEvents(queued);
        WorldScriptCheckpoints.RestoreView(ActiveSaveFolder);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(MapStats), nameof(MapStats.updateWorldTime))]
    private static void WorldTimeUpdatedPostfix() { if (!_loadingWorld) WorldScriptEngine.Tick(); }
    [HarmonyPostfix, HarmonyPatch(typeof(ActorManager), nameof(ActorManager.finalizeActor))]
    private static void ActorCreatedPostfix(Actor pActor) { if (!_loadingWorld) WorldScriptEngine.OnActorCreated(pActor); }
    [HarmonyPatch(typeof(Actor), nameof(Actor.die), typeof(bool), typeof(AttackType), typeof(bool), typeof(bool))]
    private static class ActorDeath
    {
        private static void Prefix(Actor __instance, AttackType pType, out WorldScriptEvent __state)
        {
            __state = !_loadingWorld && __instance.data != null && __instance.isAlive()
                ? new WorldScriptEvent(__instance, WorldScriptEngine.DeathKiller(__instance, pType)) : null;
        }

        private static void Postfix(Actor __instance, WorldScriptEvent __state)
        {
            if (!_loadingWorld && __state != null && !__instance.isAlive())
                WorldScriptEngine.OnActorDeath(__state);
        }
    }
    [HarmonyPostfix, HarmonyPatch(typeof(Actor), nameof(Actor.updateStats))]
    private static void ActorStatsUpdatedPostfix(Actor __instance) { if (!_loadingWorld) WorldScriptEngine.OnActorStatsUpdated(__instance); }

    [HarmonyPrefix, HarmonyPatch(typeof(City), nameof(City.setKingdom))]
    private static void VillageKingdomPrefix(City __instance, bool pFromLoad, out Kingdom __state) =>
        __state = _loadingWorld || pFromLoad ? null : __instance.kingdom;

    [HarmonyPostfix, HarmonyPatch(typeof(City), nameof(City.setKingdom))]
    private static void VillageKingdomPostfix(City __instance, Kingdom __state)
    {
        if (__state != null && __instance.data != null) WorldScriptEngine.OnVillageChangedKingdom(__instance, __state);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
    private static void SetKingPrefix(Kingdom __instance, bool pFromLoad, out (bool Track, long? ID, string Name) __state) =>
        __state = (!_loadingWorld && !pFromLoad, __instance.king?.data?.id, __instance.king?.data?.name);

    [HarmonyPrefix, HarmonyPatch(typeof(Kingdom), nameof(Kingdom.removeKing))]
    private static void RemoveKingPrefix(Kingdom __instance, out (bool Track, long? ID, string Name) __state) =>
        SetKingPrefix(__instance, false, out __state);

    [HarmonyPostfix, HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
    private static void KingChangedPostfix(Kingdom __instance, (bool Track, long? ID, string Name) __state)
    {
        if (__state.Track && __instance.data != null) WorldScriptEngine.OnKingChanged(__instance, __state.ID, __state.Name);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Kingdom), nameof(Kingdom.removeKing))]
    private static void KingRemovedPostfix(Kingdom __instance, (bool Track, long? ID, string Name) __state) =>
        KingChangedPostfix(__instance, __state);

    [HarmonyPrefix, HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    private static void EndWarPrefix(War pWar, WarWinner pWinner, out bool __state) =>
        __state = !_loadingWorld && pWinner != WarWinner.Merged && pWar.isAlive() && !pWar.hasEnded();

    [HarmonyPostfix, HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
    private static void EndWarPostfix(War pWar, bool __state)
    {
        if (__state && pWar.hasEnded()) WorldScriptEngine.OnWarEnded(pWar);
    }

    private static WorldScriptDocument LoadDocument(string path, out string preservedPath)
    {
        preservedPath = null;
        if (!File.Exists(path)) return new WorldScriptDocument();
        try
        {
            JToken source = JToken.Parse(File.ReadAllText(path));
            if (source is not JObject documentObject ||
                documentObject["schemaVersion"]?.Type != JTokenType.Integer ||
                documentObject.Value<int>("schemaVersion") != WorldScriptDocument.CurrentVersion)
            {
                preservedPath = path;
                Main.LogWarning($"Ignoring unsupported world scripts '{path}'");
                return new WorldScriptDocument();
            }

            WorldScriptDocument document = documentObject.ToObject<WorldScriptDocument>(JsonSerializer.Create(SerializerSettings)) ?? new WorldScriptDocument();
            Normalize(document.Scripts);
            return document;
        }
        catch (Exception exception)
        {
            preservedPath = path;
            Main.LogWarning($"Ignoring invalid world scripts '{path}': {exception.Message}");
            return new WorldScriptDocument();
        }
    }

    private static void InstallDocument(WorldScriptDocument document)
    {
        Scripts = document?.Scripts ?? new List<ScriptInstance>();
        Scenarios = document?.Scenarios?.Where(scenario => !string.IsNullOrWhiteSpace(scenario?.ID))
            .GroupBy(scenario => scenario.ID).Select(group => group.First()).ToList() ?? new List<ScriptScenario>();
        foreach (var scenario in Scenarios)
        {
            scenario.Parameters ??= new Dictionary<string, int>();
            WorldScriptScenes.Normalize(scenario);
        }
        Regions = document?.Regions?.Where(region => region?.ID > 0 && !string.IsNullOrWhiteSpace(region.Name))
            .GroupBy(region => region.ID).Select(group => group.First()).ToList() ?? new List<ScriptRegion>();
        _documentExtensionData = CloneExtensionData(document?.ExtensionData);
        Normalize(Scripts);
        WorldScriptEngine.Reset();
        ScriptsChanged?.Invoke();
    }

    internal static void Normalize(List<ScriptInstance> scripts)
    {
        if (scripts == null) return;
        scripts.RemoveAll(script => script == null);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScriptInstance script in scripts)
        {
            if (string.IsNullOrWhiteSpace(script.ID) || !ids.Add(script.ID))
                ids.Add(script.ID = Guid.NewGuid().ToString("N"));
            script.Name ??= "New Script";
            script.TriggerCondition ??= new TriggerCondition();
            script.TriggerCondition.Parameters ??= new JObject();
            TriggerDefinition trigger = WorldScriptRegistry.GetTrigger(script.TriggerCondition.TriggerID);
            if (trigger != null) WorldScriptRegistry.ApplyDefaults(script.TriggerCondition.Parameters, trigger.Parameters);
            script.TargetQuery = NormalizeTarget(script.TargetQuery);
            script.Conditions = NormalizeCondition(script.Conditions);
            script.Actions ??= new List<ScriptAction>();
            script.Runtime ??= new ScriptRuntimeState();
            script.Runtime.History ??= new List<ScriptExecutionRecord>();
            NormalizeActions(script.Actions);
            if (script.Flow is ScriptFlow flow)
            {
                flow.Gate = NormalizeCondition(flow.Gate);
                flow.Query = NormalizeQuery(flow.Query);
                flow.Steps ??= new List<ScriptStep>();
                flow.Steps.RemoveAll(step => step == null);
                foreach (ScriptStep step in flow.Steps)
                {
                    if (string.IsNullOrWhiteSpace(step.ID)) step.ID = Guid.NewGuid().ToString("N");
                    if (step.Query != null || step.Source == "scope") step.Query = NormalizeQuery(step.Query);
                    if (step.Relation is ScriptRelation relation)
                    {
                        relation.Filter = NormalizeCondition(relation.Filter);
                        if (relation.Next != null) relation.Next.Filter = NormalizeCondition(relation.Next.Filter);
                    }
                    step.Action ??= new ScriptAction();
                    NormalizeAction(step.Action);
                    step.Numbers ??= new Dictionary<string, ScriptNumberBinding>();
                    if (step.Branch is ScriptBranch branch)
                    {
                        branch.Condition = NormalizeCondition(branch.Condition);
                        if (branch.Otherwise != null) NormalizeAction(branch.Otherwise);
                        branch.OtherwiseNumbers ??= new Dictionary<string, ScriptNumberBinding>();
                    }
                }
            }
        }
    }

    private static ScriptQuery NormalizeQuery(ScriptQuery query)
    {
        query ??= new ScriptQuery();
        query.Scope = NormalizeTarget(query.Scope);
        if (query.DistanceOrigin != null) query.DistanceOrigin = NormalizeTarget(query.DistanceOrigin);
        query.Filter = NormalizeCondition(query.Filter);
        return query;
    }

    private static TargetSelector NormalizeTarget(TargetSelector selector)
    {
        selector ??= new TargetSelector();
        selector.Parameters ??= new JObject();
        TargetDefinition definition = WorldScriptRegistry.GetTarget(selector.TargetID);
        if (definition != null) WorldScriptRegistry.ApplyDefaults(selector.Parameters, definition.Parameters);
        return selector;
    }

    private static ConditionNode NormalizeCondition(ConditionNode node)
    {
        node ??= ConditionNode.All();
        node.Operator ??= "all";
        node.Parameters ??= new JObject();
        node.ExtensionData ??= new Dictionary<string, JToken>();
        node.Children ??= new List<ConditionNode>();
        for (int i = 0; i < node.Children.Count; i++)
            node.Children[i] = NormalizeCondition(node.Children[i]);
        if (node.Operator == "condition")
        {
            ConditionDefinition definition = WorldScriptRegistry.GetCondition(node.ConditionID);
            if (definition != null) WorldScriptRegistry.ApplyDefaults(node.Parameters, definition.Parameters);
        }
        return node;
    }

    private static void NormalizeActions(List<ScriptAction> actions)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            ScriptAction action = actions[i] ?? new ScriptAction();
            actions[i] = action;
            NormalizeAction(action);
        }
    }

    private static void NormalizeAction(ScriptAction action)
    {
        action.Operator ??= "action";
        action.Parameters ??= new JObject();
        action.ExtensionData ??= new Dictionary<string, JToken>();
        action.Children ??= new List<ScriptAction>();
        NormalizeActions(action.Children);
        if (action.Operator != "action") return;
        ActionDefinition definition = WorldScriptRegistry.GetAction(action.ActionID);
        if (definition != null) WorldScriptRegistry.ApplyDefaults(action.Parameters, definition.Parameters);
    }

    private static IDictionary<string, JToken> CloneExtensionData(IDictionary<string, JToken> source) =>
        source?.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone()) ?? new Dictionary<string, JToken>();

    public static int ImportLegacy()
    {
        if (!CanImportLegacy) return 0;
        var source = JArray.Parse(File.ReadAllText(LegacySavePath));
        var imported = new List<ScriptInstance>();
        foreach (JObject entry in source.OfType<JObject>())
        {
            var script = entry.ToObject<ScriptInstance>(JsonSerializer.Create(SerializerSettings));
            if (script == null) continue;
            script.ID = Guid.NewGuid().ToString("N");
            script.Enabled = false;
            script.Runtime = new ScriptRuntimeState();
            foreach (ScriptAction action in script.Actions ?? Enumerable.Empty<ScriptAction>())
                ImportLegacyValue(action?.Parameters, action?.ExtensionData,
                    WorldScriptRegistry.GetAction(action?.ActionID)?.Parameters);
            string legacyTrigger = entry["TriggerCondition"]?["Type"]?.ToString();
            switch (legacyTrigger)
            {
                case "0":
                case "Time":
                    script.TriggerCondition = new TriggerCondition
                    {
                        TriggerID = WorldScriptRegistry.TriggerWorldTimeReached,
                        Parameters = new JObject { ["time"] = entry["TriggerCondition"]["Value"]?.DeepClone() ?? new JValue(0) }
                    };
                    break;
                case "2":
                case "UpdateStats":
                    script.TriggerCondition = new TriggerCondition { TriggerID = WorldScriptRegistry.TriggerActorStatsUpdated };
                    script.Target = new TargetSelector { TargetID = WorldScriptRegistry.TargetActorEvent };
                    break;
            }
            if (entry["Filters"] is JArray filters)
            {
                foreach (var group in filters.ToObject<List<TargetFilter>>(JsonSerializer.Create(SerializerSettings))
                             .Where(filter => filter != null).GroupBy(filter => filter.Group))
                {
                    bool any = group.Key is FilterGroup.AnyTrue or FilterGroup.AnyFalse;
                    bool negate = group.Key is FilterGroup.MustFalse or FilterGroup.AnyFalse;
                    var node = new ConditionNode { Operator = any ? "any" : "all" };
                    foreach (TargetFilter filter in group)
                    {
                        ImportLegacyValue(filter.Parameters, filter.ExtensionData,
                            WorldScriptRegistry.GetCondition(filter.FilterID)?.Parameters);
                        ConditionNode leaf = filter;
                        if (negate) leaf = new ConditionNode { Operator = "not", Children = { leaf } };
                        node.Children.Add(leaf);
                    }
                    script.Conditions.Children.Add(node);
                }
            }
            imported.Add(script);
        }
        Normalize(imported);
        Scripts.AddRange(imported);
        _documentExtensionData[LegacyImportedKey] = true;
        ScriptsChanged?.Invoke();
        return imported.Count;
    }

    private static void ImportLegacyValue(JObject values, IDictionary<string, JToken> extension,
        ScriptParameterDefinition[] definitions)
    {
        if (values == null || extension == null || definitions == null ||
            !extension.TryGetValue("Value", out JToken legacy) || legacy?.Type == JTokenType.Null) return;
        ScriptParameterDefinition parameter = definitions.FirstOrDefault(item => item.Key == "value");
        if (parameter == null && definitions.Length == 1) parameter = definitions[0];
        if (parameter != null && values[parameter.Key] == null) values[parameter.Key] = legacy.DeepClone();
    }
}
