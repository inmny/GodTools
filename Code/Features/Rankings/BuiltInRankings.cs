using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodTools.Abstract;
using GodTools.UI;
using NeoModLoader.General;
using strings;

namespace GodTools.Features.Rankings;

internal sealed partial class BuiltInRankings : IManager
{
    internal const string Actors = "godtools:actors";
    internal const string Items = "godtools:items";
    internal const string Cultures = "godtools:cultures";
    internal const string LegacyPeaks = "godtools:basic-peaks";
    private const string Prefix = "inmny.godtools.ranking.";

    public void Initialize()
    {
        RegisterActors();
        StandardDuelPower.Register();
        RegisterItems();
        RegisterMetaBoards();
        RegisterLegacyPeak();
        RankingRegistry.Publish();
    }

    private static void RegisterActors()
    {
        RankingRegistry.RegisterSource(new RankingSource<Actor>
        {
            ID = Actors, Read = InspectableActors, Key = actor => Key(actor.data.id), Name = actor => actor.getName(),
            Icon = actor => actor._last_colored_sprite ?? actor.asset.getSpriteIcon(),
            ActorPortrait = FindActor,
            Inspect = InspectActor, Locate = LocateActor
        });
        Field<Actor>(Actors, "name", "name", RankingFieldKind.Text, actor => RankingValue.Text(actor.getName()));
        Number<Actor>(Actors, "kills", "kills", actor => actor.data.kills);
        Number<Actor>(Actors, "age", "age", actor => actor.data.getAge());
        Number<Actor>(Actors, "level", "level", actor => actor.data.level);
        Number<Actor>(Actors, "damage", "damage", actor => actor.stats[S.damage]);
        Number<Actor>(Actors, "health", "health", actor => actor.stats[S.health]);
        RankingRegistry.RegisterField(Actors, new RankingField<Actor>
        {
            ID = "current_health", Name = Prefix + "current_health", Kind = RankingFieldKind.Number,
            IconPath = "ui/icons/iconHealth", Read = actor => RankingValue.Progress(actor.getHealth(), actor.getMaxHealth())
        });
        RankingRegistry.RegisterField(Actors, new RankingField<Actor>
        {
            ID = "sex", Name = Prefix + "sex", Kind = RankingFieldKind.Category, IconPath = "ui/icons/iconMale",
            Read = actor => actor.data.sex switch
            {
                ActorSex.Male => RankingValue.WithIcon("male", Prefix + "male", "ui/icons/iconMale"),
                ActorSex.Female => RankingValue.WithIcon("female", Prefix + "female", "ui/icons/iconFemale"),
                _ => null
            }
        });
        RankingRegistry.RegisterField(Actors, new RankingField<Actor>
        {
            ID = "favorite", Name = Prefix + "favorite", Kind = RankingFieldKind.Category,
            IconPath = "ui/icons/iconFavoriteStar",
            Read = actor => actor.isFavorite()
                ? RankingValue.WithIcon("yes", Prefix + "favorited", "ui/icons/iconFavoriteStar")
                : RankingValue.Choice("no", Prefix + "not_favorited")
        });
        Number<Actor>(Actors, "births", "births", actor => actor.data.births);
        AffiliationField<Actor>(Actors, "kingdom", "kingdom", MetaType.Kingdom,
            actor => actor.kingdom?.data != null && actor.kingdom.isCiv() ? actor.kingdom : null);
        AffiliationField<Actor>(Actors, "culture", "actor_culture", MetaType.Culture, actor => actor.culture);
        AffiliationField<Actor>(Actors, "clan", "actor_clan", MetaType.Clan, actor => actor.clan);
        AffiliationField<Actor>(Actors, "language", "actor_language", MetaType.Language, actor => actor.language);
        AffiliationField<Actor>(Actors, "subspecies", "actor_subspecies", MetaType.Subspecies, actor => actor.subspecies);
        AffiliationField<Actor>(Actors, "city", "actor_city", MetaType.City, actor => actor.city);
        AffiliationField<Actor>(Actors, "religion", "actor_religion", MetaType.Religion, actor => actor.religion);
        Field<Actor>(Actors, "profession", "profession", RankingFieldKind.Choice, actor =>
        {
            string profession = actor.data.profession.ToString();
            return RankingValue.Choice(profession, Label(Prefix + "profession." + profession.ToLowerInvariant(), profession));
        });
        Field<Actor>(Actors, "species", "species", RankingFieldKind.Choice, actor =>
            RankingValue.WithIcon(actor.asset.id, Label(actor.asset.getLocaleID(), actor.asset.id), actor.asset.getIconPath()));
        Field<Actor>(Actors, "traits", "traits", RankingFieldKind.Tags, actor =>
            RankingValue.TagSet(actor.traits.ToDictionary(trait => trait.id, trait => Label(trait.getLocaleID(), trait.id))));

        Board(Actors, Actors, "actor_board", "iconPopulation", RankingGroupIds.CharactersAndEquipment, 100,
            StandardDuelPower.FieldID, "kingdom", "kills");
    }

    private static RankingValue Affiliation(IMetaObject item) =>
        item?.getMetaData() == null ? null : RankingValue.Choice(Key(item.getID()), item.name);

    private static void RegisterItems()
    {
        RankingRegistry.RegisterSource(new RankingSource<Item>
        {
            ID = Items, Read = () => World.world.items.Where(item => item?.data != null && item.getAsset() != null && !item.shouldbe_removed),
            Key = item => Key(item.id), Name = item => item.getName(), Icon = item => item.getSprite(),
            Inspect = key =>
            {
                if (FindItem(key) is not Item item) return;
                SelectedMetas.selected_item = item;
                ScrollWindow.showWindow("item");
            },
            Locate = key => FindItem(key) is Item item ? item.getActor()?.current_tile ?? item.getCity()?.getTile() : null
        });
        Field<Item>(Items, "name", "name", RankingFieldKind.Text, item => RankingValue.Text(item.getName()));
        Number<Item>(Items, "kills", "kills", item => item.data.kills);
        Number<Item>(Items, "durability", "durability", item => item.data.durability);
        Number<Item>(Items, "mod_count", "mod_count", item => item.data.modifiers.Count);
        Field<Item>(Items, "type", "item_type", RankingFieldKind.Choice, item =>
        {
            string type = item.getAsset().equipment_type.ToString();
            return RankingValue.Choice(type, Label(Prefix + "item_type." + type.ToLowerInvariant(), type));
        });
        Field<Item>(Items, "material", "material", RankingFieldKind.Choice, item =>
            RankingValue.Choice(item.data.material, Label(item.data.material, item.data.material)));
        Field<Item>(Items, "modifiers", "modifiers", RankingFieldKind.Tags, item =>
            RankingValue.TagSet(item.data.modifiers.Distinct().ToDictionary(id => id,
                id => Label(AssetManager.items_modifiers.get(id)?.getLocaleID(), id))));
        Field<Item>(Items, "holder", "holder", RankingFieldKind.Text, item =>
            RankingValue.Text(item.getActor()?.getName() ?? item.getCity()?.name));
        Board(Items, Items, "item_board", "items/icon_sword_adamantine", RankingGroupIds.CharactersAndEquipment, 200,
            "kills", "holder", "material");
    }

    private static void RegisterLegacyPeak()
    {
        RankingRegistry.RegisterSource(new RankingSource<ConsistentCreaturePowerTop.CreaturePowerData>
        {
            ID = LegacyPeaks, Read = ConsistentCreaturePowerTop.GetSortedCreaturePowerData,
            Key = entry => Key(entry.ID), Name = entry => entry.Name, Icon = entry => entry.Sprite,
            SavedCreature = key => long.TryParse(key, out long id) ? ConsistentCreaturePowerTop.GetSavedCreature(id) : null,
            Inspect = InspectActor, CanInspect = CanInspectActor, Locate = LocateActor,
            Place = key => { if (long.TryParse(key, out long id)) WindowRankings.PlaceArchivedActor(id); }
        });
        Field<ConsistentCreaturePowerTop.CreaturePowerData>(LegacyPeaks, "name", "name", RankingFieldKind.Text,
            sample => RankingValue.Text(sample.Name));
        Number<ConsistentCreaturePowerTop.CreaturePowerData>(LegacyPeaks, "peak", "power.score", sample => sample.Power);
        RankingRegistry.RegisterBoard(new RankingBoard
        {
            ID = LegacyPeaks, SourceID = LegacyPeaks, Name = Prefix + "basic_peaks", IconPath = "ui/icons/iconWorldLog",
            GroupID = RankingGroupIds.Archives, Order = 200, Columns = new[] { "peak" }, DefaultSort = "peak",
            Available = ConsistentCreaturePowerTop.HasRecords
        });
    }

    private static void Board(string id, string source, string label, string icon, string group, int order, params string[] columns) =>
        RankingRegistry.RegisterBoard(new RankingBoard
        {
            ID = id, SourceID = source, Name = Prefix + label, IconPath = "ui/icons/" + icon,
            GroupID = group, Order = order,
            Columns = columns, DefaultSort = columns.FirstOrDefault()
        });

    internal static void Field<T>(string source, string id, string label, RankingFieldKind kind, Func<T, RankingValue> read) =>
        RankingRegistry.RegisterField(source, new RankingField<T> { ID = id, Name = Prefix + label, Kind = kind, Read = read });

    internal static void Number<T>(string source, string id, string label, Func<T, double> read) =>
        Field<T>(source, id, label, RankingFieldKind.Number, item => RankingValue.Numeric(read(item)));

    internal static string Label(string key, string fallback) => !string.IsNullOrEmpty(key) && LM.Has(key) ? LM.Get(key) : fallback;
    internal static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);
    internal static IEnumerable<Actor> InspectableActors() => World.world.units.getSimpleList()
        .Where(actor => actor?.data != null && actor.isAlive() && actor.asset?.can_be_inspected == true);

    internal static void InspectActor(string key)
    {
        if (FindActor(key) is Actor actor) ActionLibrary.openUnitWindow(actor);
    }
    internal static WorldTile LocateActor(string key) => FindActor(key)?.current_tile;
    internal static bool CanInspectActor(string key) => FindActor(key) != null;
    internal static Actor FindActor(string key) => long.TryParse(key, out long id) && World.world?.units.get(id) is Actor actor &&
        actor.data?.id == id && actor.isAlive() ? actor : null;
    private static Item FindItem(string key) => long.TryParse(key, out long id) ? World.world?.items.get(id) : null;
}
