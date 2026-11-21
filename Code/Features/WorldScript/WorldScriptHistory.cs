using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GodTools.Features.WorldScript;

[HarmonyPatch(typeof(WorldLogMessageExtensions), nameof(WorldLogMessageExtensions.getAsset))]
internal static class WorldScriptHistory
{
    internal const string ActionID = "world.log";
    internal const string DefaultIcon = "ui/Icons/iconWorldLog";
    private const string AssetID = C.mod_prefix + ".world_script_log";
    private const string LocaleID = C.mod_prefix + ".ui.world_script.action.world.log";
    private static readonly Dictionary<string, WorldLogAsset> IconAssets = new(StringComparer.OrdinalIgnoreCase);

    internal static void Initialize(Harmony harmony)
    {
        if (!AssetManager.world_log_library.has(AssetID))
            AssetManager.world_log_library.add(CreateAsset(DefaultIcon));
        harmony.CreateClassProcessor(typeof(WorldScriptHistory)).Patch();
    }

    internal static bool HasIcon(string path) => !string.IsNullOrWhiteSpace(path) && SpriteTextureLoader.getSprite(path) != null;

    internal static void Add(string message, string icon, WorldTile tile)
    {
        new WorldLogMessage(AssetManager.world_log_library.get(AssetID), message, icon)
        {
            x = tile?.x, y = tile?.y
        }.add();
    }

    private static WorldLogAsset CreateAsset(string icon) => new()
    {
        id = AssetID,
        locale_id = LocaleID,
        path_icon = icon,
        text_replacer = FormatMessage
    };

    private static void FormatMessage(WorldLogMessage message, ref string text) => text = message.special1 ?? "";

    private static void Postfix(WorldLogMessage pMessage, ref WorldLogAsset __result)
    {
        if (pMessage.asset_id != AssetID || string.IsNullOrEmpty(pMessage.special2) ||
            string.Equals(pMessage.special2, DefaultIcon, StringComparison.OrdinalIgnoreCase)) return;
        string icon = pMessage.special2;
        if (IconAssets.TryGetValue(icon, out WorldLogAsset asset))
            __result = asset;
        else if (HasIcon(icon))
            __result = IconAssets[icon] = CreateAsset(icon);
    }
}
