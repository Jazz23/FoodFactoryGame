// Checks the register window's sale value rule (PlayerHud.SaleValueCents): unspoiled goods at their menu price per whole
// serving, nothing for spoiled goods or goods the register does not sell. Pure rule; no scene or database.
using System.Collections.Generic;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class RegisterSaleValueTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private RecipeAsset Sale(string id, string itemId, int cents)
        {
            var recipe = RecipeAsset.CreateSale(id, id, itemId, cents, 1, "", 5);
            _created.Add(recipe);
            return recipe;
        }

        private static GoodsLot Lot(string itemId, int quantity, bool spoiled = false) =>
            new() { Id = itemId + quantity, ItemId = itemId, LocationId = "counter:in", Quantity = quantity, Spoiled = spoiled };

        [Test]
        public void FiveBreadAtFiveDollarsIsTwentyFiveDollars()
        {
            var recipes = new[] { Sale("sell-bread", "bread", 500) };
            var cents = PlayerHud.SaleValueCents(new[] { Lot("bread", 2), Lot("bread", 3) }, recipes);
            Assert.That(cents, Is.EqualTo(2500));
            Assert.That(PlayerHud.FormatCash(cents), Is.EqualTo("$25.00"));
        }

        [Test]
        public void SpoiledAndUnsoldGoodsCountNothing()
        {
            var recipes = new[] { Sale("sell-bread", "bread", 500), Sale("sell-soup", "soup", 750) };
            var lots = new[] { Lot("bread", 1), Lot("bread", 4, true), Lot("dough", 9), Lot("soup", 2) };
            Assert.That(PlayerHud.SaleValueCents(lots, recipes), Is.EqualTo(500 + 1500));
            Assert.That(PlayerHud.SaleValueCents(new GoodsLot[0], recipes), Is.EqualTo(0));
        }
    }
}
