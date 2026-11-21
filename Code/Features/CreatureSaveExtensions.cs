using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GodTools.Features;

/// <summary>Lets a mod include its own actor data when GodTools saves and places a creature.</summary>
public static class CreatureSaveExtensions
{
    private sealed class Provider(int version, Func<Actor, JToken> capture, Action<Actor, int, JToken> restore)
    {
        internal readonly int Version = version;
        internal readonly Func<Actor, JToken> Capture = capture;
        internal readonly Action<Actor, int, JToken> Restore = restore;
        internal Func<int, JToken, string> Describe;
    }

    internal sealed class Payload
    {
        public int Version;
        public JToken Data;
    }

    private static readonly Dictionary<string, Provider> Providers = new(StringComparer.Ordinal);

    /// <summary>Registers a stable namespaced ID. Restore receives the version saved by Capture.</summary>
    /// <remarks>Register on the game thread. Return null from Capture when the actor has no data for this mod.</remarks>
    public static bool Register(string id, int version, Func<Actor, JToken> capture, Action<Actor, int, JToken> restore)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOf(':') <= 0 ||
            id.EndsWith(":", StringComparison.Ordinal) || id.Any(char.IsWhiteSpace) ||
            version < 1 || capture == null || restore == null) return false;
        return Providers.TryAdd(id, new Provider(version, capture, restore));
    }

    /// <summary>为已注册的保存扩展提供简短的本地化说明。回调读取保存时的版本和数据副本。</summary>
    public static bool RegisterSummary(string id, Func<int, JToken, string> describe)
    {
        if (id == null || describe == null || !Providers.TryGetValue(id, out var provider) || provider.Describe != null)
            return false;
        provider.Describe = describe;
        return true;
    }

    internal static bool IsRegistered(string id) => Providers.ContainsKey(id);

    internal static string Describe(string id, Payload payload)
    {
        if (!Providers.TryGetValue(id, out var provider) || provider.Describe == null) return null;
        try { return provider.Describe(payload.Version, payload.Data.DeepClone()); }
        catch (Exception error)
        {
            Main.LogWarning($"Could not describe saved creature data for '{id}': {error.Message}");
            return null;
        }
    }

    internal static Dictionary<string, Payload> Capture(Actor actor)
    {
        Dictionary<string, Payload> result = null;
        foreach (var (id, provider) in Providers.ToArray())
        {
            try
            {
                var data = provider.Capture(actor);
                if (data == null || data.Type == JTokenType.Null) continue;
                (result ??= new Dictionary<string, Payload>(StringComparer.Ordinal)).Add(id,
                    new Payload { Version = provider.Version, Data = data.DeepClone() });
            }
            catch (Exception error)
            {
                Main.LogWarning($"Could not save creature data for '{id}': {error}");
            }
        }
        return result;
    }

    internal static void Restore(Actor actor, Dictionary<string, Payload> payloads)
    {
        if (payloads == null) return;
        foreach (var (id, payload) in payloads)
        {
            if (payload?.Data == null || !Providers.TryGetValue(id, out var provider)) continue;
            try { provider.Restore(actor, payload.Version, payload.Data.DeepClone()); }
            catch (Exception error)
            {
                Main.LogWarning($"Could not restore creature data for '{id}': {error}");
            }
        }
    }
}
