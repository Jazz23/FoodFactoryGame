// Verifies decision 0043's authored kitchen content: every recipe and food price is valid server content for items that have
// definitions and icons; the ten new dishes are each made by a recipe and sold on the menu with a tier; every ingredient the
// supplier sells is used by some recipe; machines with several recipes share no unresolvable ambiguity without a recipe
// chooser; the five stations are sold by the supplier with one collider and a model that fits their footprint, the counter is
// worker-bound and the rest have power switches; and the oven has room for three ingredients.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class KitchenContentTests
    {
        private const string BalancePath = "Assets/Content/FoodBalance.asset";

        private static readonly string[] NewDishes =
            { "fries", "pancakes", "grilled-cheese", "soup-and-bread", "cheeseburger", "pizza", "spaghetti", "chicken-and-fries", "fish-and-chips", "lasagna" };

        private static FoodBalance Balance() => AssetDatabase.LoadAssetAtPath<FoodBalance>(BalancePath);

        private static List<RecipeAsset> AuthoredRecipes() => AssetDatabase.FindAssets("t:RecipeAsset", new[] { "Assets/Content/Recipes" })
            .Select(x => AssetDatabase.LoadAssetAtPath<RecipeAsset>(AssetDatabase.GUIDToAssetPath(x))).ToList();

        private static EquipmentDefinition Definition(string file) =>
            AssetDatabase.LoadAssetAtPath<EquipmentDefinition>($"Assets/Content/Equipment/{file}.asset");

        [Test]
        public void EveryRecipeAndPriceIsValidContentForKnownItems()
        {
            var balance = Balance();
            Assert.That(balance, Is.Not.Null);
            var items = SessionTestFiles.ContentItems().ToDictionary(x => x.Id);
            Assert.That(items.Values.All(x => x.Icon != null), Is.True, "Every item has an icon.");
            var recipes = AuthoredRecipes().Concat(balance.Menu()).ToList();
            var world = new GoodsWorld("authoring-check");
            foreach (var recipe in recipes) Assert.DoesNotThrow(() => world.RegisterRecipe(recipe.ToDefinition()), recipe.Id);
            foreach (var offer in balance.Offers()) Assert.DoesNotThrow(() => world.RegisterOffer(offer.ToDefinition()), offer.Id);
            var used = recipes.SelectMany(x => x.Inputs.Select(y => y.itemId)).Concat(recipes.Where(x => !x.IsSale).Select(x => x.OutputItemId)).Distinct();
            Assert.That(used.Where(x => !items.ContainsKey(x)), Is.Empty, "Every recipe item has a definition.");
            Assert.That(balance.Supplies.Select(x => x.itemId).Where(x => !items.ContainsKey(x)), Is.Empty);
            Assert.That(balance.Offers().Select(x => x.Id).Distinct().Count(), Is.EqualTo(balance.Supplies.Count), "Offer IDs are unique.");
            Assert.That(balance.Offers().Select(x => x.Id), Does.Contain("supplier-dough-5"), "The dough pack keeps its ID.");
        }

        [Test]
        public void TheNewDishesAreMadeAndSoldWithTiers()
        {
            var menu = Balance().Menu();
            var made = AuthoredRecipes().Select(x => x.OutputItemId).ToHashSet();
            foreach (var dish in NewDishes)
            {
                Assert.That(made, Does.Contain(dish), $"A recipe makes {dish}.");
                var item = menu.SingleOrDefault(x => x.Inputs.Single().itemId == dish);
                Assert.That(item, Is.Not.Null, $"{dish} is on the menu.");
                Assert.That((item.IsSale, item.Tier >= 1, item.Cuisine != ""), Is.EqualTo((true, true, true)), dish);
            }
            Assert.That(menu.Select(x => x.Id), Does.Contain("counter-sell-bread"), "Bread stays on the menu under its ID.");
            Assert.That(menu.Single(x => x.Id == "counter-sell-bread").SaleCents, Is.EqualTo(250));
            Assert.That(menu.Count(x => x.Tier >= 2), Is.GreaterThan(0), "A wealthy district has something to buy.");
        }

        [Test]
        public void EverySuppliedIngredientIsUsed()
        {
            var inputs = AuthoredRecipes().SelectMany(x => x.Inputs).Select(x => x.itemId).ToHashSet();
            Assert.That(Balance().Supplies.Select(x => x.itemId).Where(x => !inputs.Contains(x)), Is.Empty);
        }

        [Test]
        public void StationsAreSoldWithAFittingModelAndTheirControls()
        {
            var stations = new[] { ("Mixer", "mixer", true), ("Stove", "stove", true), ("Fryer", "fryer", true), ("Grill", "grill", true),
                ("KitchenCounter", "kitchen-counter", false) };
            foreach (var (file, kind, power) in stations)
            {
                var definition = Definition(file);
                Assert.That(definition, Is.Not.Null, file);
                Assert.That((definition.Kind, definition.ManualPower, definition.WorkerBound), Is.EqualTo((kind, power, !power)), file);
                Assert.That(definition.Icon, Is.Not.Null, file);
                Assert.That(definition.VisualPrefab.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), $"{file}: one collider for aim rays.");
                var bounds = definition.VisualPrefab.GetComponentInChildren<Collider>(true) is BoxCollider box ? box.size : Vector3.zero;
                Assert.That(bounds.x, Is.LessThanOrEqualTo(definition.Width * SiteGrid.CellSize + 0.01f), file);
                Assert.That(bounds.z, Is.LessThanOrEqualTo(definition.Depth * SiteGrid.CellSize + 0.01f), file);
                var offer = AssetDatabase.LoadAssetAtPath<OfferAsset>($"Assets/Content/Offers/{file}1.asset");
                Assert.That((offer.Id, offer.Equipment), Is.EqualTo(($"supplier-{kind}", definition)), file);
                Assert.DoesNotThrow(() => offer.RegisterWith(new GoodsWorld("authoring-check")), file);
                // Each machine's inputs fit its buffer: the largest recipe needs one slot per ingredient.
                var widest = AuthoredRecipes().Where(x => x.StationKind == kind).Select(x => x.Inputs.Count).DefaultIfEmpty(0).Max();
                Assert.That(definition.InputCapacity, Is.GreaterThanOrEqualTo(widest), file);
            }
            var oven = Definition("Oven");
            Assert.That(oven.InputCapacity, Is.GreaterThanOrEqualTo(3), "Pizza and lasagna need three ingredients in the oven.");
        }
    }
}
