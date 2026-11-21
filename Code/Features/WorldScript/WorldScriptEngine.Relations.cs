using System;
using System.Collections.Generic;
using System.Linq;

namespace GodTools.Features.WorldScript;

public sealed class WorldScriptTargetGroup
{
    public readonly WorldScriptTarget Parent;
    public readonly List<WorldScriptTarget> Targets;
    public readonly string Name;
    public readonly List<WorldScriptTargetGroup> Villages;
    public readonly int MatchedCount;
    public readonly int CreatedCount;

    internal WorldScriptTargetGroup(WorldScriptTarget parent, List<WorldScriptTarget> targets, string name = null,
        List<WorldScriptTargetGroup> villages = null, int? matchedCount = null, int createdCount = 0)
    {
        Parent = parent;
        Targets = targets;
        Name = name ?? parent.Meta?.name ?? "";
        Villages = villages;
        MatchedCount = matchedCount ?? InputCount;
        CreatedCount = createdCount;
    }

    internal int InputCount => Villages?.Sum(village => village.Targets.Count) ?? Targets.Count;
    internal bool CanRun(bool report) =>
        Villages?.Any(village => village.CanRun(report)) ?? (report || Targets.Count > 0);
}

public static partial class WorldScriptEngine
{
    internal const string GroupResultSource = "group_result";
    internal const string CityResidents = "city.residents";
    internal const string CityCenter = "city.center";
    internal const string KingdomVillages = "kingdom.villages";
    internal const string KingdomKing = "kingdom.king";
    internal const string KingdomMembers = "kingdom.members";

    internal static readonly (string ID, ScriptTargetKind Parent, ScriptTargetKind Target)[] Relations =
    {
        (CityResidents, ScriptTargetKind.City, ScriptTargetKind.Actor),
        (CityCenter, ScriptTargetKind.City, ScriptTargetKind.Tile),
        (KingdomVillages, ScriptTargetKind.Kingdom, ScriptTargetKind.City),
        (KingdomKing, ScriptTargetKind.Kingdom, ScriptTargetKind.Actor),
        (KingdomMembers, ScriptTargetKind.Kingdom, ScriptTargetKind.Actor)
    };

    internal static ScriptTargetKind RelationTargetKind(ScriptTargetKind source, ScriptRelation relation) =>
        Relations.FirstOrDefault(item => item.Parent == source && item.ID == relation.ID).Target;

    internal static bool HasGroups(ScriptStep step) => step.Relation != null || step.Source == GroupResultSource;

    internal static bool CanReportGroups(ScriptFlow flow, int index) =>
        FindSourceStep(flow, index, implicitPrevious: true) is int source && HasGroups(flow.Steps[source]);

    private static List<WorldScriptTargetGroup> ResolveRelations(ScriptRelation relation,
        List<WorldScriptTarget> parents, WorldScriptExecutionContext context)
    {
        var groups = ResolveRelationLayer(relation, parents, context);
        if (relation.Next == null) return groups;

        // Resolve the villages together so a resident lookup scans the world only once.
        var villages = ResolveRelationLayer(relation.Next, groups.SelectMany(group => group.Targets).ToList(), context)
            .ToDictionary(group => group.Parent.MetaID);
        return groups.Select(group => new WorldScriptTargetGroup(group.Parent, group.Targets, group.Name,
            group.Targets.Where(village => villages.ContainsKey(village.MetaID))
                .Select(village => villages[village.MetaID]).ToList())).ToList();
    }

    private static List<WorldScriptTargetGroup> ResolveRelationLayer(ScriptRelation relation,
        List<WorldScriptTarget> parents, WorldScriptExecutionContext context)
    {
        if (parents.Count == 0) return new List<WorldScriptTargetGroup>();
        ILookup<IMetaObject, Actor> members = null;
        if (relation.ID is CityResidents or KingdomMembers)
        {
            members = World.world.units.getSimpleList().Where(actor => actor?.data != null && actor.isAlive())
                .ToLookup(actor => relation.ID == CityResidents ? (IMetaObject)actor.city : actor.kingdom);
        }
        var groups = new List<WorldScriptTargetGroup>(parents.Count);
        foreach (WorldScriptTarget parent in parents)
        {
            if (!IsUsable(parent, context)) continue;
            var targets = Distinct(RelationTargets(relation.ID, parent, members)
                .Where(target => IsUsable(target, context) && PassesPreviewFilter(relation.Filter, context, target, parent)));
            if (relation.Limit > 0) targets = targets.Take(relation.Limit);
            groups.Add(new WorldScriptTargetGroup(parent, targets.ToList()));
        }
        return groups;
    }

    private static IEnumerable<WorldScriptTarget> RelationTargets(string relation, WorldScriptTarget parent,
        ILookup<IMetaObject, Actor> members)
    {
        switch (relation)
        {
            case CityResidents:
            case KingdomMembers:
                return members[parent.Meta].Select(WorldScriptTarget.ForActor);
            case CityCenter:
                WorldTile tile = ((City)parent.Meta).getTile();
                return tile == null ? Array.Empty<WorldScriptTarget>() : new[] { WorldScriptTarget.ForTile(tile) };
            case KingdomVillages:
                return parent.Meta.getCities().Where(city => city?.data != null && city.kingdom == parent.Meta)
                    .Select(WorldScriptTarget.ForCity);
            case KingdomKing:
                Actor king = ((Kingdom)parent.Meta).king;
                return king?.data == null ? Array.Empty<WorldScriptTarget>() : new[] { WorldScriptTarget.ForActor(king) };
            default:
                return Array.Empty<WorldScriptTarget>();
        }
    }
}
