using System.Globalization;
using System.Linq;
using NeoModLoader.General;

namespace GodTools.Features.Rankings;

internal sealed partial class BuiltInRankings
{
    private static void RegisterMetaBoards()
    {
        const string kingdoms = "godtools:kingdoms";
        RegisterMeta<Kingdom>(kingdoms, MetaType.Kingdom, "kingdom_board", RankingGroupIds.Powers, 100, KingdomTile,
            "population", "settlements", "territory");
        Number<Kingdom>(kingdoms, "population", "population", item => item.getPopulationPeople());
        Number<Kingdom>(kingdoms, "settlements", "settlements", item => item.countCities());
        Number<Kingdom>(kingdoms, "territory", "territory", item => item.countZones());
        Number<Kingdom>(kingdoms, "warriors", "warriors", item => item.countTotalWarriors());
        Field<Kingdom>(kingdoms, "ruler", "ruler", RankingFieldKind.Text, item => RankingValue.Text(item.king?.getName()));
        AffiliationField<Kingdom>(kingdoms, "alliance", "alliance", MetaType.Alliance, item => item.getAlliance());

        const string cities = "godtools:cities";
        RegisterMeta<City>(cities, MetaType.City, "city_board", RankingGroupIds.Powers, 200, item => item.getTile(),
            "population", "kingdom", "territory");
        Number<City>(cities, "population", "population", item => item.getPopulationPeople());
        Number<City>(cities, "territory", "territory", item => item.countZones());
        Number<City>(cities, "buildings", "buildings", item => item.countBuildings());
        AffiliationField<City>(cities, "kingdom", "kingdom", MetaType.Kingdom, item => item.kingdom);
        AffiliationField<City>(cities, "culture", "actor_culture", MetaType.Culture, item => item.culture);
        AffiliationField<City>(cities, "language", "actor_language", MetaType.Language, item => item.language);
        AffiliationField<City>(cities, "religion", "actor_religion", MetaType.Religion, item => item.religion);

        const string alliances = "godtools:alliances";
        RegisterMeta<Alliance>(alliances, MetaType.Alliance, "alliance_board", RankingGroupIds.Powers, 300,
            item => item.kingdoms_list.Select(KingdomTile).FirstOrDefault(tile => tile != null),
            "kingdom_count", "population", "warriors");
        Number<Alliance>(alliances, "kingdom_count", "member_kingdoms", item => item.countKingdoms());
        Number<Alliance>(alliances, "population", "population", item => item.countPopulation());
        Number<Alliance>(alliances, "warriors", "warriors", item => item.countWarriors());
        Number<Alliance>(alliances, "settlements", "settlements", item => item.countCities());
        Number<Alliance>(alliances, "territory", "territory", item => item.countZones());

        const string armies = "godtools:armies";
        RegisterMeta<Army>(armies, MetaType.Army, "army_board", RankingGroupIds.Powers, 400,
            item => ActorTile(item.getCaptain()) ?? MemberTile(item) ?? item.getCity()?.getTile(),
            "warriors", "kingdom", "city");
        Number<Army>(armies, "warriors", "warriors", item => item.countUnits());
        Number<Army>(armies, "melee", "melee", item => item.countMelee());
        Number<Army>(armies, "ranged", "ranged", item => item.countRange());
        AffiliationField<Army>(armies, "kingdom", "kingdom", MetaType.Kingdom, item => item.getKingdom());
        AffiliationField<Army>(armies, "city", "home_city", MetaType.City, item => item.getCity());

        RegisterMeta<Culture>(Cultures, MetaType.Culture, "culture_board", RankingGroupIds.Communities, 100, MemberTile,
            "population", "cities", "age");
        Number<Culture>(Cultures, "population", "population", item => item.countUnits());
        Number<Culture>(Cultures, "cities", "cities", item => item.countCities());
        Number<Culture>(Cultures, "kingdom_count", "covered_kingdoms", item => item.countKingdoms());
        Field<Culture>(Cultures, "traits", "culture_traits", RankingFieldKind.Tags,
            item => RankingValue.TagSet(item.getTraits().ToDictionary(trait => trait.id, trait => Label(trait.getLocaleID(), trait.id))));

        const string clans = "godtools:clans";
        RegisterMeta<Clan>(clans, MetaType.Clan, "clan_board", RankingGroupIds.Communities, 200, MemberTile, "members", "renown", "age");
        Number<Clan>(clans, "members", "members", item => item.countUnits());
        Number<Clan>(clans, "renown", "renown", item => item.getRenown());
        Number<Clan>(clans, "kingdom_count", "ruled_kingdoms", item => item.getKingdoms().Count());
        Field<Clan>(clans, "founder", "founder", RankingFieldKind.Text, item => RankingValue.Text(item.data.founder_actor_name));

        const string families = "godtools:families";
        RegisterMeta<Family>(families, MetaType.Family, "family_board", RankingGroupIds.Communities, 300, MemberTile, "members", "children", "age");
        Number<Family>(families, "members", "members", item => item.countUnits());
        Number<Family>(families, "children", "children", item => item.countChildren());

        const string languages = "godtools:languages";
        RegisterMeta<Language>(languages, MetaType.Language, "language_board", RankingGroupIds.Communities, 400, MemberTile,
            "speakers", "books", "cities");
        Number<Language>(languages, "speakers", "speakers", item => item.countUnits());
        Number<Language>(languages, "books", "written_books", item => item.countWrittenBooks());
        Number<Language>(languages, "cities", "cities", item => item.countCities());
        Number<Language>(languages, "kingdom_count", "covered_kingdoms", item => item.countKingdoms());

        const string subspecies = "godtools:subspecies";
        RegisterMeta<Subspecies>(subspecies, MetaType.Subspecies, "subspecies_board", RankingGroupIds.Communities, 500, MemberTile,
            "population", "families", "age");
        Number<Subspecies>(subspecies, "population", "population", item => item.countUnits());
        Number<Subspecies>(subspecies, "families", "family_count", item => item.countCurrentFamilies());
        Number<Subspecies>(subspecies, "cities", "main_cities", item => item.countMainCities());
        Number<Subspecies>(subspecies, "kingdom_count", "main_kingdoms", item => item.countMainKingdoms());
        Field<Subspecies>(subspecies, "species", "species", RankingFieldKind.Choice,
            item => item.getActorAsset() is { } asset ? RankingValue.Choice(asset.id, Label(asset.getLocaleID(), asset.id)) : null);

        const string religions = "godtools:religions";
        RegisterMeta<Religion>(religions, MetaType.Religion, "religion_board", RankingGroupIds.Communities, 600, MemberTile,
            "followers", "cities", "age");
        Number<Religion>(religions, "followers", "followers", item => item.countUnits());
        Number<Religion>(religions, "cities", "cities", item => item.countCities());
        Number<Religion>(religions, "kingdom_count", "covered_kingdoms", item => item.countKingdoms());

        const string wars = "godtools:wars";
        RegisterMeta<War>(wars, MetaType.War, "war_board", RankingGroupIds.Conflicts, 100,
            item => KingdomTile(item.getMainAttacker()) ?? KingdomTile(item.getMainDefender()), "casualties", "age", "kingdom_count");
        Number<War>(wars, "casualties", "casualties", item => (long)item.data.dead_attackers + item.data.dead_defenders);
        Number<War>(wars, "kingdom_count", "participants", item => item.countKingdoms());
        Number<War>(wars, "warriors", "warriors", item => item.countTotalArmy());
        AffiliationField<War>(wars, "attacker", "attacker", MetaType.Kingdom, item => item.getMainAttacker());
        AffiliationField<War>(wars, "defender", "defender", MetaType.Kingdom, item => item.getMainDefender());

        const string plots = "godtools:plots";
        RegisterMeta<Plot>(plots, MetaType.Plot, "plot_board", RankingGroupIds.Conflicts, 200,
            item => item.target_city?.getTile() ?? ActorTile(item.target_actor) ?? KingdomTile(item.target_kingdom) ?? ActorTile(item.getAuthor()),
            "progress", "age", "author");
        Field<Plot>(plots, "progress", "progress", RankingFieldKind.Number, item =>
        {
            int progress = item.getProgressPercentage();
            return RankingValue.Numeric(progress, $"{progress}%");
        });
        Field<Plot>(plots, "author", "author", RankingFieldKind.Text, item => RankingValue.Text(item.data.founder_name));
        AffiliationField<Plot>(plots, "target_kingdom", "target_kingdom", MetaType.Kingdom, item => item.target_kingdom);
        AffiliationField<Plot>(plots, "target_city", "target_city", MetaType.City, item => item.target_city);
    }

    private static void RegisterMeta<T>(string id, MetaType type, string label, string group, int order,
        System.Func<T, WorldTile> locate, params string[] columns) where T : NanoObject, IMetaObject
    {
        var asset = type.getAsset();
        string iconPath = "ui/icons/" + asset.icon_list;
        T Find(string key) => FindMeta(type, key) is T item && IsLiveMeta(item) ? item : null;
        RankingRegistry.RegisterSource(new RankingSource<T>
        {
            ID = id,
            Read = () => asset.get_list().OfType<T>().Where(IsLiveMeta),
            Key = item => Key(item.getID()),
            Name = item => item.name,
            Icon = _ => SpriteTextureLoader.getSprite(iconPath),
            MetaPortrait = Find,
            Inspect = key =>
            {
                if (Find(key) is not T item) return;
                asset.set_selected(item);
                ScrollWindow.showWindow(asset.window_name);
            },
            CanInspect = key => Find(key) != null,
            Locate = key => Find(key) is T item ? locate(item) : null
        });
        Field<T>(id, "name", "name", RankingFieldKind.Text, item => RankingValue.Text(item.name));
        Number<T>(id, "age", "age", item => Date.getYearsSince(item.getFoundedTimestamp()));
        Board(id, id, label, asset.icon_list, group, order, columns);
    }

    private static void AffiliationField<T>(string id, string field, string label, MetaType type,
        System.Func<T, IMetaObject> read) => RankingRegistry.RegisterField(id, new RankingField<T>
        {
            ID = field, Name = Prefix + label, Kind = RankingFieldKind.Choice, AffiliationType = type,
            Read = item => Affiliation(read(item))
        });

    internal static NanoObject FindMeta(MetaType type, string key) =>
        World.world != null && long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) &&
        type.getAsset()?.get?.Invoke(id) is { } item && item.isAlive() && item.getID() == id ? item : null;

    private static bool IsLiveMeta<T>(T item) where T : NanoObject, IMetaObject =>
        item != null && item.isAlive() && item.getMetaData() != null && !item.hasDied() &&
        (item is not Kingdom kingdom || kingdom.isCiv());

    private static WorldTile ActorTile(Actor actor) => actor?.isAlive() == true ? actor.current_tile : null;

    private static WorldTile MemberTile(IMetaObject item) =>
        item.getUnits().Select(ActorTile).FirstOrDefault(tile => tile != null);

    private static WorldTile KingdomTile(Kingdom item) =>
        item?.isAlive() == true ? item.capital?.getTile() ?? MemberTile(item) : null;
}
