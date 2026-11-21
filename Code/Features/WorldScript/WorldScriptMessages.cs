using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GodTools.Features.WorldScript;

internal static class WorldScriptMessages
{
    internal const string TipAction = "world.tip";
    internal const string EnabledKey = "use_variables";
    internal const string EventActor = "event_actor";
    internal const string EventKingdom = "event_kingdom";
    internal const string MatchCount = "match_count";
    internal const string TargetCount = "target_count";
    internal const string GroupName = "group_name";
    internal const string EventCity = "event_city";
    internal const string EventOld = "event_old";
    internal const string EventNew = "event_new";
    internal const string EventValue = "event_value";
    internal const string EventWar = "event_war";
    private static readonly Regex Variables = new(@"\{(event_actor|event_kingdom|match_count|target_count|group_name|event_city|event_old|event_new|event_value|event_war)\}");

    internal static bool IsMessageAction(string id) => id is TipAction or WorldScriptHistory.ActionID;

    internal static bool UsesVariables(JObject parameters) =>
        bool.TryParse(WorldScriptRegistry.GetString(parameters, EnabledKey), out bool enabled) && enabled;

    internal static string Render(JObject parameters, WorldScriptEvent snapshot, int matches, int? targets, string groupName = null)
    {
        string message = WorldScriptRegistry.GetString(parameters, "message");
        if (!UsesVariables(parameters)) return message;
        return Variables.Replace(message, value => value.Groups[1].Value switch
        {
            EventActor when snapshot != null => NameText(snapshot.ActorName, snapshot.ActorID),
            EventKingdom when snapshot?.KingdomID is long id => NameText(snapshot.KingdomName, id),
            EventCity when snapshot?.CityID is long id => NameText(snapshot.CityName, id),
            EventOld when snapshot?.PreviousID is long id => NameText(snapshot.PreviousName, id),
            EventNew when snapshot?.NextID is long id => NameText(snapshot.NextName, id),
            EventWar when snapshot?.WarName != null => PlainName(snapshot.WarName),
            EventValue when snapshot?.Value is int count => count.ToString(CultureInfo.InvariantCulture),
            EventActor or EventKingdom or EventCity or EventOld or EventNew or EventWar or EventValue => "—",
            MatchCount => matches.ToString(CultureInfo.InvariantCulture),
            TargetCount when targets.HasValue => targets.Value.ToString(CultureInfo.InvariantCulture),
            GroupName when groupName != null => PlainName(groupName),
            _ => value.Value
        });
    }

    private static string NameText(string name, long id) => string.IsNullOrWhiteSpace(name)
        ? "#" + id.ToString(CultureInfo.InvariantCulture) : PlainName(name);

    private static string PlainName(string name) => name.Replace('<', '＜').Replace('>', '＞');
}
