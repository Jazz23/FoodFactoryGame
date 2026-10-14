// Checks build mode's single catalog (owner, 2026-10-14) without entering Play mode: every structure style the server accepts
// and every authored equipment and belt offer gets exactly one card, keys are unique, the filters partition the cards (All shows
// them all), and the categories of the existing machines and pieces are the expected ones.
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class BuildCatalogTests
    {
        private static OfferAsset[] Offers() => AssetDatabase.FindAssets("t:OfferAsset", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<OfferAsset>).Where(x => x != null).ToArray();

        private static ItemDefinition[] Items() => AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemDefinition>).Where(x => x != null).ToArray();

        [Test]
        public void EveryStyleAndOfferHasOneCardAndTheFiltersPartitionThem()
        {
            var offers = Offers();
            var entries = BuildCatalog.Entries(offers, Items(), style => style, 100);
            Assert.That(entries.Select(x => x.Key).Distinct().Count(), Is.EqualTo(entries.Count), "Card keys are unique.");
            foreach (var (tool, styles) in new[]
                     {
                         (BuildTool.Wall, GoodsWorld.WallStyles), (BuildTool.Door, GoodsWorld.DoorStyles), (BuildTool.BackDoor, GoodsWorld.DoorStyles),
                         (BuildTool.Window, GoodsWorld.WindowStyles), (BuildTool.Finish, GoodsWorld.WallStyles)
                     })
            foreach (var style in styles)
                Assert.That(entries.Count(x => x.Tool == tool && x.Style == style && x.Filter == BuildCatalogFilter.Structure), Is.EqualTo(1), $"{tool} {style}");
            var equipment = offers.Where(x => x.Equipment != null && x.Truck == null).ToList();
            Assert.That(equipment, Is.Not.Empty);
            foreach (var offer in equipment)
                Assert.That(entries.Count(x => x.Tool == BuildTool.Item && x.OfferId == offer.Id), Is.EqualTo(1), offer.Id);
            Assert.That(entries.Any(x => x.OfferId != null && offers.Single(o => o.Id == x.OfferId).Truck != null), Is.False, "Trucks are not listed.");
            foreach (var offer in offers.Where(x => x.Equipment == null && x.Truck == null && x.ItemId == GoodsWorld.BeltItemId))
                Assert.That(entries.Single(x => x.OfferId == offer.Id).IsBelt, Is.True, offer.Id);
            Assert.That(entries.Count(x => BuildCatalog.Matches(x, BuildCatalogFilter.All)), Is.EqualTo(entries.Count), "All shows every card.");
            Assert.That(BuildCatalog.Filters.Where(x => x != BuildCatalogFilter.All).Sum(f => entries.Count(x => BuildCatalog.Matches(x, f))),
                Is.EqualTo(entries.Count), "Every card is in exactly one category.");
            Assert.That(BuildCatalog.Filters.First(), Is.EqualTo(BuildCatalogFilter.All));
        }

        [Test]
        public void ExistingPiecesFallInTheExpectedCategories()
        {
            var definitions = AssetDatabase.FindAssets("t:EquipmentDefinition", new[] { "Assets/Content" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<EquipmentDefinition>).Where(x => x != null).ToDictionary(x => x.Kind);
            foreach (var (kind, filter) in new[]
                     {
                         ("mixer", BuildCatalogFilter.Kitchen), ("oven", BuildCatalogFilter.Kitchen), ("fryer", BuildCatalogFilter.Kitchen),
                         ("fridge", BuildCatalogFilter.Storage), (GoodsWorld.CounterKind, BuildCatalogFilter.Sales),
                         (GoodsWorld.DockKind, BuildCatalogFilter.Logistics), (GoodsWorld.TableKind, BuildCatalogFilter.Furniture),
                         ("rt-chair-spindle", BuildCatalogFilter.Furniture), ("rt-wall-clock", BuildCatalogFilter.Decor),
                         ("rt-pendant", BuildCatalogFilter.Decor), ("rt-floor-oak", BuildCatalogFilter.Structure), ("rt-cornice", BuildCatalogFilter.Structure)
                     })
            {
                Assert.That(definitions.ContainsKey(kind), Is.True, $"{kind} exists");
                Assert.That(BuildCatalog.FilterOf(definitions[kind]), Is.EqualTo(filter), kind);
            }
        }
    }
}
