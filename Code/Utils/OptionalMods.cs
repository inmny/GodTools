using System;
using System.Linq;
using System.Reflection;
using NeoModLoader;
using NeoModLoader.api;

namespace GodTools.Utils;

internal static class OptionalMods
{
    internal static ModDeclare Find(string id) => WorldBoxMod.LoadedMods
        .FirstOrDefault(mod => string.Equals(mod.GetDeclaration().UID, id, StringComparison.OrdinalIgnoreCase))
        ?.GetDeclaration();

    internal static Assembly AssemblyFor(string id) => WorldBoxMod.LoadedMods
        .FirstOrDefault(mod => string.Equals(mod.GetDeclaration().UID, id, StringComparison.OrdinalIgnoreCase))
        ?.GetType().Assembly;

    internal static bool Present(string id) => Find(id) != null;
}
