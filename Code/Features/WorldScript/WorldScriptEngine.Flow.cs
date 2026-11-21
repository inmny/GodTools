using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

public static partial class WorldScriptEngine
{
    internal static readonly string[] QueryOrders = { "registry", "random", "distance_asc", "health_asc", "power_desc" };
    internal static readonly string[] QueryGroups = { "none", "kingdom", "actor_asset" };
    private static readonly string[] ActorNumbers = { "target.max_health", "target.health", "target.missing_health" };
    internal static readonly string[] ResultSources = { "step_matched", "step_processed", "step_created" };
    internal static readonly string[] ResultMeasures = { "matched", "processed", "created" };
    internal static readonly string[] Comparisons = { ">=", ">", "=", "!=", "<", "<=" };

    private sealed class StepResult
    {
        public List<WorldScriptTarget> Matched = new();
        public List<WorldScriptTarget> Processed = new();
        public List<WorldScriptTarget> Created;
        public List<WorldScriptTargetGroup> Groups;
    }

    internal static IEnumerable<string> NumberSources(ScriptTargetKind kind, string triggerID, string scenarioID = null)
    {
        if (kind == ScriptTargetKind.Actor)
            foreach (string source in ActorNumbers) yield return source;
        if (UsesEventActor(triggerID)) yield return "event.max_health";
        if (Scenario(scenarioID)?.Parameters.Count > 0) yield return "scenario.parameter";
    }

    internal static bool CanUseScope(TargetDefinition definition, string triggerID) =>
        (!definition.RequiresActorEvent || UsesEventActor(triggerID)) &&
        (!definition.RequiresDeathEvent || triggerID == WorldScriptRegistry.TriggerActorDied) &&
        (!definition.RequiresCityEvent || UsesCityEvent(triggerID)) &&
        (!definition.RequiresKingdomEvent || UsesKingdomEvent(triggerID)) &&
        (!definition.RequiresPositionEvent || UsesEventActor(triggerID) || UsesKingdomEvent(triggerID));

    internal static ScriptTargetKind PreviousOutput(List<ScriptStep> steps, int index)
    {
        ScriptStep previous = steps.Take(index).LastOrDefault(step => step?.Action?.Enabled == true);
        return previous == null ? ScriptTargetKind.None : ActionOutput(previous);
    }

    private static ScriptTargetKind ActionOutput(ScriptStep step)
    {
        ScriptTargetKind first = WorldScriptRegistry.GetAction(step.Action.ActionID)?.OutputKind ?? ScriptTargetKind.None;
        if (!step.HasBranch || !step.Branch.HasOtherwise) return first;
        ScriptTargetKind alternate = WorldScriptRegistry.GetAction(step.Branch.Otherwise.ActionID)?.OutputKind ?? ScriptTargetKind.None;
        if (first == ScriptTargetKind.None) return alternate;
        return alternate == ScriptTargetKind.None || alternate == first ? first : ScriptTargetKind.None;
    }

    internal static ScriptTargetKind StepSourceKind(ScriptFlow flow, int index) => flow.Steps[index].Source switch
    {
        "query" => WorldScriptRegistry.GetTarget(flow.Query?.Scope?.TargetID)?.Kind ?? ScriptTargetKind.None,
        "scope" => WorldScriptRegistry.GetTarget(flow.Steps[index].Query?.Scope?.TargetID)?.Kind ?? ScriptTargetKind.None,
        "previous" => PreviousOutput(flow.Steps, index),
        GroupResultSource => ScriptTargetKind.World,
        "step_matched" or "step_processed" or "step_created" => FindSourceStep(flow, index) is int source
            ? ResultSourceKind(flow, source, flow.Steps[index].Source) : ScriptTargetKind.None,
        _ => ScriptTargetKind.None
    };

    internal static int? FindSourceStep(ScriptFlow flow, int index, bool implicitPrevious = false)
    {
        string id = flow.Steps[index].SourceStepID;
        for (int i = index - 1; i >= 0; i--)
        {
            var step = flow.Steps[i];
            if (step?.Action?.Enabled != true) continue;
            if (string.IsNullOrEmpty(id) ? implicitPrevious : step.ID == id) return i;
        }
        return null;
    }

    internal static ScriptTargetKind ResultSourceKind(ScriptFlow flow, int source, string result) =>
        result == "step_created" ? ActionOutput(flow.Steps[source])
            : flow.Steps[source].Source == GroupResultSource ? ScriptTargetKind.None : StepInputKind(flow, source);

    internal static ScriptTargetKind StepInputKind(ScriptFlow flow, int index)
    {
        ScriptTargetKind source = StepSourceKind(flow, index);
        var relation = flow.Steps[index].Relation;
        if (relation == null) return source;
        ScriptTargetKind related = RelationTargetKind(source, relation);
        return relation.Next == null ? related : RelationTargetKind(related, relation.Next);
    }

    private static TargetDefinition ValidateScope(TargetSelector selector, ScriptInstance script, List<string> errors, string path)
    {
        TargetDefinition definition = WorldScriptRegistry.GetTarget(selector?.TargetID);
        if (definition == null)
        {
            errors.Add($"{path}: Unknown target '{selector?.TargetID}'");
            return null;
        }
        if (!CanUseScope(definition, script.TriggerCondition?.TriggerID))
            errors.Add($"{path}: This scope requires a matching actor event");
        AddParameterErrors(errors, path, selector.Parameters, definition.Parameters, definition.ID);
        return definition;
    }

    private static void ValidateFlow(ScriptInstance script, List<string> errors)
    {
        ScriptFlow flow = script.Flow;
        string triggerID = script.TriggerCondition?.TriggerID;
        ValidateFlowConditions(flow.Gate, ScriptTargetKind.World, triggerID, errors, "gate");
        ValidateQuery(flow.Query, script, errors, "target");
        if (script.EmptyTargetPolicy is not ("skip" or "error")) errors.Add("Unknown empty target policy");
        if (flow.Steps?.Any(step => step?.Action?.Enabled == true) != true)
        {
            errors.Add("Add at least one enabled action");
            return;
        }
        var stepIDs = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < flow.Steps.Count; i++)
        {
            ScriptStep step = flow.Steps[i];
            if (step?.Action?.Enabled != true) continue;
            string path = $"step {i + 1}";
            int firstError = errors.Count;
            if (string.IsNullOrEmpty(step.ID) || !stepIDs.Add(step.ID))
                errors.Add($"{path}: Step IDs must be unique");
            ScriptTargetKind input = StepInputKind(flow, i);
            if (step.Source == "scope") ValidateQuery(step.Query, script, errors, path);
            if (step.Source == GroupResultSource && (!CanReportGroups(flow, i) || step.Mode != "once" ||
                step.Relation != null || step.HasBranch))
                errors.Add($"{path}: Choose a grouped step before reporting its results");
            if (step.ResultCondition is ScriptCountRule rule &&
                (!(step.Source == GroupResultSource || ResultSources.Contains(step.Source)) ||
                    !ResultMeasures.Contains(rule.Measure) || !Comparisons.Contains(rule.Operator) || rule.Value < 0))
                errors.Add($"{path}: Choose a valid step result condition");
            if (step.Relation is ScriptRelation relation)
            {
                ScriptTargetKind related = RelationTargetKind(StepSourceKind(flow, i), relation);
                ValidateRelation(relation, related, triggerID, errors, path + ".related");
                if (relation.Next != null)
                {
                    if (related != ScriptTargetKind.City || relation.Next.Next != null)
                        errors.Add($"{path}: Only a village relation can be followed by one more relation");
                    ValidateRelation(relation.Next, input, triggerID, errors, path + ".related.next");
                }
            }
            if (input == ScriptTargetKind.None) errors.Add($"{path}: Choose a target source with available output");
            if (step.Mode is not ("each" or "once")) errors.Add($"{path}: Unknown execution mode");
            ScriptTargetKind actionKind = step.Mode == "once" ? ScriptTargetKind.World : input;
            ValidateStepAction(step.Action, step.Numbers, script, actionKind, errors, path);
            if (step.HasBranch)
            {
                if (!HasPredicate(step.Branch.Condition)) errors.Add($"{path}: Add a condition to the branch");
                ValidateFlowConditions(step.Branch.Condition, input, triggerID, errors, path + ".if");
                if (step.Branch.HasOtherwise)
                    ValidateStepAction(step.Branch.Otherwise, step.Branch.OtherwiseNumbers, script, actionKind, errors, path + ".else");
            }
            for (int error = firstError; error < errors.Count; error++)
                if (!errors[error].StartsWith(path + ":", StringComparison.Ordinal) &&
                    !errors[error].StartsWith(path + ".", StringComparison.Ordinal))
                    errors[error] = $"{path}: {errors[error]}";
        }
    }

    private static void ValidateRelation(ScriptRelation relation, ScriptTargetKind kind, string triggerID,
        List<string> errors, string path)
    {
        if (kind == ScriptTargetKind.None) errors.Add($"{path}: Choose a relation for the source object");
        if (relation.Limit < 0) errors.Add($"{path}: The group limit must be nonnegative");
        ValidateFlowConditions(relation.Filter, kind, triggerID, errors, path);
    }

    private static bool HasPredicate(ConditionNode node) =>
        node?.Operator == "condition" || node?.Children?.Any(HasPredicate) == true;

    private static void ValidateStepAction(ScriptAction step, Dictionary<string, ScriptNumberBinding> bindings,
        ScriptInstance script, ScriptTargetKind kind, List<string> errors, string path)
    {
        if (step.Operator != "action")
        {
            errors.Add($"{path}: Select one action per branch");
            return;
        }
        ValidateActions(new[] { step }, script, kind, errors, path);
        var action = WorldScriptRegistry.GetAction(step.ActionID);
        if (bindings == null || action == null) return;
        foreach (var entry in bindings)
        {
            ScriptNumberBinding binding = entry.Value;
            if (binding == null || !NumberSources(kind, script.TriggerCondition?.TriggerID, script.ScenarioID).Contains(binding.Source) ||
                binding.Source == "scenario.parameter" &&
                    (string.IsNullOrEmpty(binding.Parameter) || !Scenario(script.ScenarioID).Parameters.ContainsKey(binding.Parameter)) ||
                double.IsNaN(binding.Percent) || double.IsInfinity(binding.Percent) ||
                !action.Parameters.Any(parameter => parameter.Key == entry.Key &&
                    parameter.Type is ScriptParameterType.Integer or ScriptParameterType.Number))
                errors.Add($"{path}: Invalid number source for '{entry.Key}'");
        }
    }

    private static void ValidateQuery(ScriptQuery query, ScriptInstance script, List<string> errors, string path)
    {
        if (query == null)
        {
            errors.Add("Choose a target scope");
            return;
        }
        string triggerID = script.TriggerCondition?.TriggerID;
        TargetDefinition scope = ValidateScope(query.Scope, script, errors, path);
        ScriptTargetKind kind = scope?.Kind ?? ScriptTargetKind.None;
        ValidateFlowConditions(query.Filter, kind, triggerID, errors, path);
        if (scope?.ID == WorldScriptRegistry.TargetActorEvent && triggerID == WorldScriptRegistry.TriggerActorDied)
            errors.Add("The event actor is no longer a living target");
        if (!QueryOrders.Contains(query.Order) || kind != ScriptTargetKind.Actor && query.Order is "health_asc" or "power_desc")
            errors.Add("Order is incompatible with the query target type");
        if (!QueryGroups.Contains(query.Group) || kind != ScriptTargetKind.Actor && query.Group != "none")
            errors.Add("Grouping requires actor targets");
        if (query.Order == "distance_asc")
        {
            TargetDefinition origin = query.DistanceOrigin == null ? scope
                : ValidateScope(query.DistanceOrigin, script, errors, "distance origin");
            if (origin?.GetOrigin == null || kind == ScriptTargetKind.World) errors.Add("Choose a distance origin");
        }
        if (query.Count < 0) errors.Add("SampleCount cannot be negative");
    }

    private static void ValidateFlowConditions(ConditionNode node, ScriptTargetKind kind, string triggerID,
        List<string> errors, string path)
    {
        ValidateCondition(node, kind, errors, path);
        if (UsesEventActor(triggerID) || node == null) return;
        var nodes = new Stack<ConditionNode>();
        nodes.Push(node);
        while (nodes.Count > 0)
        {
            ConditionNode current = nodes.Pop();
            if (current?.Operator == "condition" && WorldScriptRegistry.GetCondition(current.ConditionID)?.RequiresActorEvent == true)
                errors.Add($"{path}: Condition '{current.ConditionID}' requires an actor event");
            if (current?.Children == null) continue;
            foreach (ConditionNode child in current.Children) nodes.Push(child);
        }
    }

    private static WorldScriptPreviewResult ResolveFlow(ScriptInstance script, WorldScriptExecutionContext context,
        Random random, WorldScriptPreviewResult result)
    {
        ScriptFlow flow = script.Flow;
        ScriptQuery query = flow.Query;
        try
        {
            result.GatePassed = PassesPreviewFilter(flow.Gate, context, WorldScriptTarget.ForWorld());
            if (!result.GatePassed) return result;
            var matches = MatchQuery(query, context);
            result.MatchCount = matches.Count;
            result.Targets.AddRange(ApplyTargetCap(OrderQuery(matches, query, context, random), context));
            if (result.Targets.Count == 0 && script.EmptyTargetPolicy == "error")
            {
                result.Errors.Add("Target query returned no targets");
                return result;
            }

            bool earlierAction = false;
            var previews = new Dictionary<int, WorldScriptStepPreview>();
            for (int i = 0; i < flow.Steps.Count; i++)
            {
                ScriptStep step = flow.Steps[i];
                if (step?.Action?.Enabled != true) continue;
                context.CurrentStep = i + 1;
                var preview = new WorldScriptStepPreview
                {
                    StepIndex = i, Deferred = step.Source == "previous" || ResultSources.Contains(step.Source)
                };
                if (step.Source == "query") preview.Targets.AddRange(result.Targets);
                if (step.Source == "scope")
                    preview.Targets.AddRange(ApplyTargetCap(OrderQuery(MatchQuery(step.Query, context), step.Query, context, random), context));
                if (step.Source == GroupResultSource)
                {
                    if (FindSourceStep(flow, i, implicitPrevious: true) is int source)
                        preview.Groups = previews[source].Groups;
                    preview.ReportsGroups = true;
                    preview.Deferred = preview.Groups == null;
                }
                else if (step.Relation != null && !preview.Deferred)
                    preview.Groups = ResolveRelations(step.Relation, preview.Targets, context);
                if (step.HasBranch && step.Relation == null)
                {
                    preview.RunsOtherwise = step.Branch.HasOtherwise;
                    preview.BranchKnown = !preview.Deferred && !earlierAction;
                    if (preview.BranchKnown)
                        foreach (WorldScriptTarget target in preview.Targets)
                        {
                            bool passed = EvaluateNode(step.Branch.Condition, context, target, out var rejecting);
                            (passed ? preview.ThenTargets : preview.OtherwiseTargets).Add(target);
                            if (!passed && !preview.RunsOtherwise) context.NotePreviewDecision(target, null, "branch", rejecting);
                        }
                }
                result.Steps.Add(preview);
                earlierAction |= preview.CanRun;
                previews.Add(i, preview);
            }
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
        }
        return result;
    }

    private static List<WorldScriptTarget> MatchQuery(ScriptQuery query, WorldScriptExecutionContext context) =>
        ResolveScope(query.Scope, context).Where(target => PassesPreviewFilter(query.Filter, context, target)).ToList();

    private static IEnumerable<WorldScriptTarget> ApplyTargetCap(IEnumerable<WorldScriptTarget> targets,
        WorldScriptExecutionContext context)
    {
        int limit = context.Script.MaxTargets;
        int index = 0;
        foreach (var target in targets)
        {
            if (index++ < limit) yield return target;
            else
            {
                context.NoteLimit("targets");
                context.NotePreviewDecision(target, null, "limit");
                yield break;
            }
        }
    }

    private static IEnumerable<WorldScriptTarget> ResolveScope(TargetSelector scope, WorldScriptExecutionContext context)
    {
        var definition = WorldScriptRegistry.GetTarget(scope.TargetID);
        var targets = definition.Resolve(context, scope.Parameters) ?? Enumerable.Empty<WorldScriptTarget>();
        return Distinct(targets.Where(target => target?.Kind == definition.Kind && IsUsable(target, context)));
    }

    private static bool IsUsable(WorldScriptTarget target, WorldScriptExecutionContext context) => target.Kind switch
    {
        ScriptTargetKind.Actor => target.Actor?.data?.id == target.ActorID && target.Actor.isAlive() &&
            (context.TriggerID != WorldScriptRegistry.TriggerActorDied || context.Event?.ActorID != target.ActorID),
        ScriptTargetKind.City or ScriptTargetKind.Kingdom => target.Meta?.getMetaData() != null &&
            target.Meta.getID() == target.MetaID && target.Meta.isAlive(),
        ScriptTargetKind.Tile => target.Tile != null,
        _ => target.Kind == ScriptTargetKind.World
    };

    private static IEnumerable<WorldScriptTarget> OrderQuery(List<WorldScriptTarget> matches, ScriptQuery query,
        WorldScriptExecutionContext context, Random random)
    {
        TargetSelector origin = query.DistanceOrigin ?? query.Scope;
        WorldTile center = query.Order == "distance_asc"
            ? WorldScriptRegistry.GetTarget(origin.TargetID).GetOrigin(context, origin.Parameters) : null;
        IEnumerable<WorldScriptTarget> ordered = query.Order switch
        {
            "random" => matches.OrderBy(_ => random.Next()),
            "distance_asc" when center != null => matches.OrderBy(target =>
                TargetTile(target) is WorldTile tile
                    ? WorldScriptRegistry.DistanceSquared(tile, center) : long.MaxValue),
            "health_asc" => matches.OrderBy(target => target.Actor.getHealthRatio()),
            "power_desc" => matches.OrderByDescending(target => ConsistentCreaturePowerTop.CalculatePower(target.Actor)),
            _ => matches
        };
        if (query.Group == "none") return query.Count > 0 ? ordered.Take(query.Count) : ordered;

        var groups = ordered.GroupBy(target => query.Group == "kingdom"
                ? target.Actor.kingdom?.data?.id.ToString() ?? "" : target.Actor.asset?.id ?? "")
            .Select(group => (query.Count > 0 ? group.Take(query.Count) : group).ToList()).ToList();
        return RoundRobin(groups);
    }

    private static WorldTile TargetTile(WorldScriptTarget target) => target.Meta switch
    {
        City city => city.getTile(),
        Kingdom kingdom => kingdom.capital?.getTile(),
        _ => target.Tile ?? target.Actor?.current_tile
    };

    private static IEnumerable<WorldScriptTarget> RoundRobin(List<List<WorldScriptTarget>> groups)
    {
        if (groups.Count == 0) yield break;
        var active = new Queue<List<WorldScriptTarget>>(groups);
        for (int index = 0; active.Count > 0; index++)
        {
            int count = active.Count;
            for (int group = 0; group < count; group++)
            {
                var targets = active.Dequeue();
                yield return targets[index];
                if (index + 1 < targets.Count) active.Enqueue(targets);
            }
        }
    }

    private static void ExecuteFlow(ScriptInstance script, WorldScriptExecutionContext context, WorldScriptPreviewResult preview)
    {
        var processed = new HashSet<(ScriptTargetKind Kind, long ID, WorldTile Tile)>();
        List<WorldScriptTarget> output = new();
        var results = new Dictionary<int, StepResult>();
        foreach (WorldScriptStepPreview stepPreview in preview.Steps)
        {
            ScriptStep step = script.Flow.Steps[stepPreview.StepIndex];
            context.CurrentStep = stepPreview.StepIndex + 1;
            bool report = step.Source == GroupResultSource;
            bool referencesStep = report || ResultSources.Contains(step.Source);
            StepResult referenced = null;
            if (referencesStep && FindSourceStep(script.Flow, stepPreview.StepIndex, implicitPrevious: report) is int source)
                results.TryGetValue(source, out referenced);
            List<WorldScriptTarget> inputs = step.Source switch
            {
                "previous" => output,
                "step_matched" => referenced?.Matched,
                "step_processed" => referenced?.Processed,
                "step_created" => referenced?.Created,
                _ => stepPreview.Targets
            };
            inputs ??= new List<WorldScriptTarget>();
            var result = new StepResult();
            ScriptStepExecution trace = BeginStep(context, stepPreview.StepIndex + 1, step.Action.ActionID);
            if (trace != null)
            {
                trace.Name = ScriptStepExecution.Limit(step.Name, 80);
                trace.StepID = step.ID;
            }
            output = new List<WorldScriptTarget>();
            context.StepOutput = output;
            int previousActions = context.CompletedActionCount;
            try
            {
                if (report)
                {
                    result.Groups = RunGroups(step, referenced?.Groups, trace, report: true, result);
                }
                else if (!PassesCountRule(step.ResultCondition, referenced?.Matched.Count ?? 0,
                             referenced?.Processed.Count ?? 0, referenced?.Created.Count ?? 0, trace))
                {
                    context.HadUnmetBranch = true;
                    if (trace != null) trace.Outcome = "condition_unmet";
                    if (step.Relation != null) result.Groups = new List<WorldScriptTargetGroup>();
                }
                else if (step.Relation != null)
                {
                    result.Groups = RunGroups(step, ResolveRelations(step.Relation, inputs, context), trace, report: false, result);
                }
                else
                {
                    result.Matched = inputs.Where(target => IsUsable(target, context)).ToList();
                    RunInputs(step, result.Matched.ToList(), trace, result.Processed);
                    if (trace != null) trace.ProcessedCount = result.Processed.Count;
                }
                output = Distinct(output).ToList();
                result.Created = output;
                result.Matched = Distinct(result.Matched).ToList();
                result.Processed = Distinct(result.Processed).ToList();
                results.Add(stepPreview.StepIndex, result);
            }
            catch (Exception exception)
            {
                FailStep(trace, exception);
                if (trace != null && context.CompletedActionCount > previousActions) trace.Outcome = "partial";
                throw;
            }
            finally
            {
                context.StepOutput = null;
                context.BatchTargets = Array.Empty<WorldScriptTarget>();
                context.GroupName = null;
                context.GroupTarget = null;
            }
        }

        List<WorldScriptTargetGroup> RunGroups(ScriptStep step, List<WorldScriptTargetGroup> groups,
            ScriptStepExecution trace, bool report, StepResult result)
        {
            const int maxGroupDetails = 40;
            int remainingVillageRows = maxGroupDetails;
            int processedCount = 0;
            bool unmet = false;
            return RunGroupLayer(groups, trace, ref processedCount, ref unmet, villageLayer: false);

            List<WorldScriptTargetGroup> RunGroupLayer(List<WorldScriptTargetGroup> entries,
                ScriptStepExecution summary, ref int processedInLayer, ref bool unmetInLayer, bool villageLayer)
            {
                var results = new List<WorldScriptTargetGroup>();
                if (summary != null)
                {
                    summary.Groups = new List<ScriptStepExecution>();
                    summary.InputCount = entries?.Sum(group => group.InputCount) ?? 0;
                }
                int actionsBeforeLayer = context.CompletedActionCount;
                foreach (WorldScriptTargetGroup group in entries ?? Enumerable.Empty<WorldScriptTargetGroup>())
                {
                    ScriptStepExecution detail = null;
                    if (summary?.Groups.Count < maxGroupDetails && (!villageLayer || remainingVillageRows > 0))
                    {
                        detail = new ScriptStepExecution
                        {
                            Step = summary.Step, StepID = step.ID, Name = summary.Name, ActionID = step.Action.ActionID,
                            Parent = ScriptTargetSample.From(group.Parent)
                        };
                        detail.Parent.Name = ScriptStepExecution.Limit(group.Name, 100);
                        summary.Groups.Add(detail);
                        if (villageLayer) remainingVillageRows--;
                    }
                    else if (summary != null) summary.OmittedGroups++;

                    int groupProcessed = 0;
                    bool groupUnmet = false;
                    int actionsBeforeGroup = context.CompletedActionCount;
                    List<WorldScriptTarget> applied = null;
                    try
                    {
                        if (group.Villages != null)
                        {
                            var villages = RunGroupLayer(group.Villages, detail, ref groupProcessed, ref groupUnmet, villageLayer: true);
                            results.Add(new WorldScriptTargetGroup(group.Parent, group.Targets, group.Name, villages));
                            continue;
                        }

                        context.GroupName = group.Name;
                        context.GroupTarget = group.Parent;
                        context.BatchTargets = Array.Empty<WorldScriptTarget>();
                        if (report)
                        {
                            if (!PassesCountRule(step.ResultCondition, group.MatchedCount, group.Targets.Count, group.CreatedCount, detail))
                            {
                                groupUnmet = true;
                                context.HadUnmetBranch = true;
                                if (detail != null) detail.Outcome = "condition_unmet";
                                continue;
                            }
                            groupProcessed = group.Targets.Count;
                            if (detail != null) detail.InputCount = groupProcessed;
                            context.BatchTargets = group.Targets;
                            context.MessageTargetCount = groupProcessed;
                            bool ran = RunAction(WorldScriptRegistry.GetAction(step.Action.ActionID), step.Action, step.Numbers,
                                WorldScriptTarget.ForWorld(), detail, null, groupProcessed);
                            if (detail != null) detail.Outcome = ran ? "completed" : "no_change";
                            if (ran) results.Add(group);
                        }
                        else
                        {
                            applied = new List<WorldScriptTarget>();
                            var matched = group.Targets.Where(target => IsUsable(target, context)).ToList();
                            result.Matched.AddRange(matched);
                            int firstOutput = output.Count;
                            groupUnmet = RunInputs(step, matched.ToList(), detail, applied);
                            result.Processed.AddRange(applied);
                            results.Add(new WorldScriptTargetGroup(group.Parent, applied, group.Name,
                                matchedCount: matched.Count, createdCount: Distinct(output.Skip(firstOutput)).Count()));
                        }
                    }
                    catch (Exception exception)
                    {
                        FailStep(detail, exception);
                        if (detail != null && context.CompletedActionCount > actionsBeforeGroup) detail.Outcome = "partial";
                        throw;
                    }
                    finally
                    {
                        groupProcessed += applied?.Count ?? 0;
                        if (detail != null) detail.ProcessedCount = groupProcessed;
                        processedInLayer += groupProcessed;
                        if (summary != null) summary.ProcessedCount = processedInLayer;
                        unmetInLayer |= groupUnmet;
                    }
                }
                if (summary != null) summary.Outcome = context.CompletedActionCount > actionsBeforeLayer ? "completed"
                    : summary.Groups.Any(group => group.Outcome == "no_change") ? "no_change"
                    : unmetInLayer ? "condition_unmet" : "no_targets";
                return results;
            }
        }

        bool RunInputs(ScriptStep step, List<WorldScriptTarget> inputs, ScriptStepExecution trace,
            List<WorldScriptTarget> applied = null)
        {
            SampleInputs(trace, inputs);
            context.MessageTargetCount = inputs.Count;
            if (inputs.Count == 0)
            {
                if (trace != null) trace.Outcome = step.Source == "previous" && step.Relation == null ? "no_output" : "no_targets";
                return false;
            }

            int previousActions = context.CompletedActionCount;
            int previousAttempts = context.ActionCount;
            var definition = WorldScriptRegistry.GetAction(step.Action.ActionID);
            ScriptBranch branch = step.HasBranch ? step.Branch : null;
            var alternate = branch?.HasOtherwise == true ? WorldScriptRegistry.GetAction(branch.Otherwise.ActionID) : null;
            var passed = branch == null ? null
                : new HashSet<WorldScriptTarget>(inputs.Where(target => EvaluateNode(branch.Condition, context, target)));
            if (trace != null && passed != null)
            {
                trace.ThenCount = passed.Count;
                trace.OtherwiseCount = inputs.Count - passed.Count;
                for (int index = 0; index < trace.Targets.Count; index++)
                    trace.Targets[index].Branch = passed.Contains(inputs[index]) ? "then" : "otherwise";
            }
            if (step.Mode == "once")
            {
                if (passed == null)
                    RunBatch(definition, step.Action, step.Numbers, inputs, trace, null, applied);
                else
                {
                    RunBatch(definition, step.Action, step.Numbers, inputs.Where(passed.Contains).ToList(), trace, "then", applied);
                    if (alternate != null)
                        RunBatch(alternate, branch.Otherwise, branch.OtherwiseNumbers,
                            inputs.Where(target => !passed.Contains(target)).ToList(), trace, "otherwise", applied);
                }
            }
            else
            {
                foreach (WorldScriptTarget input in inputs)
                {
                    if (!IsUsable(input, context)) continue;
                    bool appliedAction;
                    if (passed == null || passed.Contains(input))
                        appliedAction = RunAction(definition, step.Action, step.Numbers, input, trace, passed == null ? null : "then", 1);
                    else if (alternate != null)
                        appliedAction = RunAction(alternate, branch.Otherwise, branch.OtherwiseNumbers, input, trace, "otherwise", 1);
                    else continue;
                    if (appliedAction) Count(input, applied);
                }
            }
            bool unmet = passed?.Count == 0 && alternate == null;
            context.HadUnmetBranch |= unmet;
            if (trace != null)
                trace.Outcome = context.CompletedActionCount > previousActions ? "completed"
                    : context.ActionCount > previousAttempts ? "no_change" : unmet ? "condition_unmet" : "no_targets";
            return unmet;
        }

        void RunBatch(ActionDefinition definition, ScriptAction action, Dictionary<string, ScriptNumberBinding> bindings,
            List<WorldScriptTarget> inputs, ScriptStepExecution trace, string branch, List<WorldScriptTarget> applied)
        {
            inputs.RemoveAll(target => !IsUsable(target, context));
            if (inputs.Count == 0) return;
            context.BatchTargets = inputs;
            context.MessageTargetCount = inputs.Count;
            if (RunAction(definition, action, bindings, WorldScriptTarget.ForWorld(), trace, branch, inputs.Count))
                foreach (WorldScriptTarget input in inputs) Count(input, applied);
        }

        bool RunAction(ActionDefinition definition, ScriptAction action, Dictionary<string, ScriptNumberBinding> bindings,
            WorldScriptTarget input, ScriptStepExecution trace, string branch, int count)
        {
            int first = output.Count;
            bool ran = ExecuteTracedAction(definition, context, input, BindNumbers(action, bindings, definition, context, input),
                trace, branch, count);
            for (int index = output.Count - 1; index >= first; index--)
                if (output[index].Kind != definition.OutputKind) output.RemoveAt(index);
            return ran;
        }

        void Count(WorldScriptTarget target, List<WorldScriptTarget> applied)
        {
            applied?.Add(target);
            if (processed.Add((target.Kind, target.EntityID, target.Tile))) script.Runtime.LastTargetCount++;
        }

        static void SampleInputs(ScriptStepExecution trace, List<WorldScriptTarget> inputs)
        {
            if (trace == null) return;
            trace.InputCount = inputs.Count;
            foreach (WorldScriptTarget input in inputs.Take(8)) trace.Sample(input);
        }
    }

    private static bool PassesCountRule(ScriptCountRule rule, int matched, int processed, int created, ScriptStepExecution trace)
    {
        if (rule == null) return true;
        int count = rule.Measure switch { "matched" => matched, "created" => created, _ => processed };
        if (trace != null) trace.ResultValue = count;
        return WorldScriptRegistry.Compare(count, rule.Operator, rule.Value);
    }

    internal static int? EstimateChange(ScriptInstance script, ScriptStep step, WorldScriptTarget target, WorldScriptEvent snapshot)
    {
        var definition = WorldScriptRegistry.GetAction(step.Action.ActionID);
        if (definition?.EstimateChange == null) return null;
        var context = CreateContext(script, script.TriggerCondition?.TriggerID, snapshot, MaxActionsPerEvent);
        try
        {
            return definition.EstimateChange(target, BindNumbers(step.Action, step.Numbers, definition, context, target));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static JObject BindNumbers(ScriptAction action, Dictionary<string, ScriptNumberBinding> bindings,
        ActionDefinition definition, WorldScriptExecutionContext context, WorldScriptTarget target)
    {
        if (bindings?.Count is not > 0) return action.Parameters;
        var values = (JObject)(action.Parameters?.DeepClone() ?? new JObject());
        foreach (var entry in bindings)
        {
            ScriptNumberBinding binding = entry.Value;
            double quantity = binding.Source switch
            {
                "target.health" => target.Actor.getHealth(),
                "target.missing_health" => (double)target.Actor.getMaxHealth() - target.Actor.getHealth(),
                "target.max_health" => target.Actor.getMaxHealth(),
                "event.max_health" => context.Event.MaxHealth,
                "scenario.parameter" => Scenario(context.Script.ScenarioID).Parameters[binding.Parameter],
                _ => throw new InvalidOperationException($"Unknown number source '{binding.Source}'")
            };
            double resolved = quantity * (binding.Percent / 100d);
            var parameter = definition.Parameters.First(candidate => candidate.Key == entry.Key);
            if (double.IsNaN(resolved) || double.IsInfinity(resolved))
                throw new InvalidOperationException($"Resolved value for '{entry.Key}' is too large");
            if (parameter.Type == ScriptParameterType.Integer)
                values[entry.Key] = checked((int)Math.Round(resolved, MidpointRounding.AwayFromZero));
            else
                values[entry.Key] = resolved;
        }
        return values;
    }
}
