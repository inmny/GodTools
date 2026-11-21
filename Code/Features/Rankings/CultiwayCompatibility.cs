using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GodTools.UI;
using GodTools.Utils;
using UnityEngine;

namespace GodTools.Features.Rankings;

// The mod types only exist when Cultiway is loaded. Game actors and assets use the regular game API.
internal sealed class CultiwayCompatibility
{
    internal const string ModId = "CULTIWAY";
    private const string Prefix = "inmny.godtools.ranking.cultiway.";
    private const string Books = "cultiway:cultibooks";
    private readonly Assembly assembly;
    private readonly Type extendType;
    private readonly MethodInfo readComponent;
    private readonly MethodInfo writeComponent;
    private readonly MethodInfo markStatsDirty;
    private readonly MethodInfo getPower;
    private readonly MethodInfo setPower;
    private readonly Func<Actor, object> extend;
    private readonly Dictionary<string, Type> components = new();
    private static CultiwayCompatibility _instance;

    internal static CultiwayCompatibility Find()
    {
        if (_instance != null) return _instance;
        var assembly = OptionalMods.AssemblyFor(ModId);
        if (assembly == null) return null;
        try { return _instance = new CultiwayCompatibility(assembly); }
        catch (Exception error) { Main.LogWarning($"Could not bind Cultiway editor: {error.Message}"); return null; }
    }

    private CultiwayCompatibility(Assembly assembly)
    {
        this.assembly = assembly;
        extendType = Type("Core.ActorExtend");
        extend = (Func<Actor, object>)Type("Utils.Extension.ActorTools").GetMethod("GetExtend")
            .CreateDelegate(typeof(Func<Actor, object>));
        readComponent = extendType.GetMethod("TryGetComponent");
        writeComponent = extendType.GetMethod("AddComponent", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        markStatsDirty = extendType.GetMethod("MarkCultiwayStatsDirty");
        getPower = extendType.GetMethod("GetPowerLevel");
        setPower = extendType.GetMethod("SetPowerLevel");
    }

    internal float Power(Actor actor) => (float)getPower.Invoke(extend(actor), null);
    internal void SetPower(Actor actor, float value) => setPower.Invoke(extend(actor), new object[] { value });

    internal object Component(Actor actor, string name) => Component(extend(actor), ComponentType(name));

    private object Component(object unit, Type type)
    {
        var value = new object[1];
        return (bool)readComponent.MakeGenericMethod(type).Invoke(unit, value) ? value[0] : null;
    }

    internal void SetComponent(Actor actor, object value)
    {
        var unit = extend(actor);
        // Cultiway's AddComponent updates an existing Friflo component of the same type.
        writeComponent.MakeGenericMethod(value.GetType()).Invoke(unit, new[] { value });
        markStatsDirty.Invoke(unit, new object[] { true });
    }

    internal object CreateRoot(float[] values) => Activator.CreateInstance(ComponentType("ElementRoot"), new object[] { values });
    internal static float RootElement(object root, int index) => (float)root.GetType().GetProperty("Item")
        .GetValue(root, new object[] { index });

    internal static object Member(object item, string name) => item?.GetType().GetField(name)?.GetValue(item)
        ?? item?.GetType().GetProperty(name)?.GetValue(item);

    private Type Type(string name) => assembly.GetType("Cultiway." + name, true);

    private Type ComponentType(string name)
    {
        if (!components.TryGetValue(name, out var type))
            components[name] = type = assembly.GetType("Cultiway.Core.Components." + name) ?? Type("Content.Components." + name);
        return type;
    }

    internal static void InstallRankings()
    {
        var mod = OptionalMods.Find(ModId);
        RankingRegistry.InstallFallback(mod, () => Find()?.Register(mod));
    }

    private void Register(NeoModLoader.api.ModDeclare mod)
    {
        var realm = Type("Content.Cultisyses");
        var levelForSort = extendType.GetMethod("GetCultisysLevelForSort");
        foreach (var (id, system, empty) in new[] { ("realm", "Xian", "mortal"), ("magic", "Magic", "no_magic"), ("knight", "Knight", "no_knight") })
        {
            var component = ComponentType(system);
            var order = levelForSort.MakeGenericMethod(component);
            var asset = realm.GetProperty(system).GetValue(null);
            var levelName = asset.GetType().GetMethod("GetLevelName");
            Field(id, RankingFieldKind.Ordinal, actor =>
            {
                var unit = extend(actor);
                var data = Component(unit, component);
                if (data == null) return RankingValue.Choice(empty, Label(empty), -1);
                var level = Member(data, "CurrLevel");
                return RankingValue.Choice(level.ToString(), (string)levelName.Invoke(asset, new[] { level }),
                    (float)order.Invoke(unit, null));
            });
        }
        var root = ComponentType("ElementRoot");
        var strength = root.GetMethod("GetStrength");
        Field("talent", RankingFieldKind.Number, actor =>
            Component(extend(actor), root) is { } value ? RankingValue.Numeric((float)strength.Invoke(value, null) * 100d) : null);
        var skills = extendType.GetField("all_skills");
        var skillCount = skills.FieldType.GetProperty("Count");
        Field("skill_count", RankingFieldKind.Number, actor => RankingValue.Numeric((int)skillCount.GetValue(skills.GetValue(extend(actor)))));
        var mainBook = Type("Content.Extensions.ActorExtendTools").GetMethod("GetMainCultibook");
        Field("book_grade", RankingFieldKind.Ordinal, actor => mainBook.Invoke(null, new[] { extend(actor) }) is { } book
            ? Grade(Member(book, "Level")) : null);
        var apprentice = ComponentType("MasterApprenticeState");
        Field("apprentices", RankingFieldKind.Number, actor =>
            RankingValue.Numeric((int)(Member(Component(extend(actor), apprentice), "ApprenticeCount") ?? 0)));
        RegisterBooks(mod);

        void Field(string id, RankingFieldKind kind, Func<Actor, RankingValue> read)
        {
            RankingRegistry.RegisterField(BuiltInRankings.Actors, new RankingField<Actor>
            { ID = "cultiway:" + id, Name = Prefix + id, Kind = kind, Read = read, Owner = mod });
        }
    }

    private sealed class BookRow
    {
        internal Asset Book;
        internal int Learners;
        internal Actor Reader;
        internal float Mastery;
    }

    private void RegisterBooks(NeoModLoader.api.ModDeclare mod)
    {
        var bookType = Type("Content.Libraries.CultibookAsset");
        var library = Type("Content.Libraries.Manager").GetProperty("CultibookLibrary").GetValue(null);
        var list = (IEnumerable)library.GetType().GetField("list").GetValue(library);
        var readMastery = extendType.GetMethod("GetAllMaster").MakeGenericMethod(bookType);
        var getMastery = extendType.GetMethods().Single(method => method.Name == "GetMaster" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(bookType);
        var tooltip = Type("Content.UI.Prefab.CultibookTooltip").GetMethod("Show",
            new[] { typeof(GameObject), bookType, extendType, typeof(float) });
        Dictionary<string, BookRow> rows = new();
        const string icon = "books/custom_book_covers/cultibook/01";
        RankingRegistry.RegisterGroup(new RankingGroup { ID = RankingGroupIds.Cultiway, Name = "Cultiway", Order = 600 });
        RankingRegistry.RegisterSource(new RankingSource<BookRow>
        {
            ID = Books, Read = ReadBooks, Key = row => row.Book.id, Name = row => (string)Member(row.Book, "Name"),
            Icon = _ => SpriteTextureLoader.getSprite(icon),
            Inspect = key =>
            {
                if (!rows.TryGetValue(key, out var row)) return;
                var actor = row.Reader;
                if (actor?.isAlive() == true)
                {
                    var reader = extend(actor);
                    float mastery = (float)getMastery.Invoke(reader, new[] { row.Book });
                    if (mastery > 0)
                    {
                        tooltip.Invoke(null, new[] { WindowRankings.Instance.gameObject, (object)row.Book, reader, mastery });
                        return;
                    }
                }
                WindowRankings.ShowEntryDescription((string)Member(row.Book, "Name"), (string)Member(row.Book, "Description"));
            }
        });
        Field("name", "name", RankingFieldKind.Text, row => RankingValue.Text((string)Member(row.Book, "Name")));
        Field("level", "grade", RankingFieldKind.Ordinal, row => Grade(Member(row.Book, "Level")));
        Field("learners", "learners", RankingFieldKind.Number, row => RankingValue.Numeric(row.Learners));
        Field("spells", "spells", RankingFieldKind.Number, row =>
            RankingValue.Numeric(((ICollection)Member(row.Book, "SkillPool")).Count +
                                 ((ICollection)Member(row.Book, "ConstructionPool")).Count));
        RankingRegistry.RegisterBoard(new RankingBoard
        {
            ID = Books, SourceID = Books, Name = Prefix + "books", GroupID = RankingGroupIds.Cultiway,
            Order = 100, IconPath = icon, Columns = new[] { "learners", "level", "spells" }, DefaultSort = "learners"
        });

        IEnumerable<BookRow> ReadBooks()
        {
            rows = list.OfType<Asset>().ToDictionary(book => book.id, book => new BookRow { Book = book });
            foreach (var actor in BuiltInRankings.InspectableActors())
            foreach (ITuple known in (IEnumerable)readMastery.Invoke(extend(actor), null))
            {
                float mastery = (float)known[1];
                if (mastery <= 0 || known[0] is not Asset book || !rows.TryGetValue(book.id, out var row)) continue;
                row.Learners++;
                if (row.Mastery >= mastery) continue;
                row.Mastery = mastery;
                row.Reader = actor;
            }
            return rows.Values;
        }

        void Field(string id, string label, RankingFieldKind kind, Func<BookRow, RankingValue> read) =>
            RankingRegistry.RegisterField(Books, new RankingField<BookRow>
            { ID = id, Name = Prefix + label, Kind = kind, Read = read, Owner = mod });
    }

    private static RankingValue Grade(object level)
    {
        var type = level.GetType();
        int order = (int)type.GetMethod("op_Implicit").Invoke(null, new[] { level });
        return RankingValue.Choice(order.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (string)type.GetMethod("GetName").Invoke(level, null), order);
    }

    private static string Label(string id) => BuiltInRankings.Label(Prefix + id, id);
}
