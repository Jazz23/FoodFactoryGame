// Every food price in one editable asset (decision 0043): what the supplier charges for each raw ingredient pack, and what
// customers pay for each dish. The session turns each supply row into a supplier offer and each dish row into a menu item (a
// sale recipe at the register) when it starts, so editing this asset changes prices from the next server start. Rows keep
// stable IDs ("supplier-<item>-<pack>", "counter-sell-<item>") derived from the item, so saves and outcomes never depend on the
// asset's row order. How long goods last and how long machines take stays on the recipe and offer assets.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Food Balance")]
    public sealed class FoodBalance : ScriptableObject
    {
        [Serializable] public sealed class Supply
        {
            public string itemId;
            [Min(1)] public int packQuantity = 5;
            // Whole cents for one pack.
            [Min(1)] public int packPriceCents = 1;
            // Seconds a delivered unit stays edible outside refrigeration.
            [Min(1)] public long spoilAfterSeconds = 3600;
        }

        [Serializable] public sealed class Dish
        {
            public string itemId;
            public string displayName;
            // Whole cents a customer pays for one.
            [Min(1)] public int saleCents = 100;
            // 1 budget, 2 mid, 3 upscale: wealthy districts buy only tier 2 and up (GoodsWorld.MinTier).
            [Min(1)] public int tier = 1;
            public string cuisine;
            // Seconds a register takes to serve one.
            [Min(1)] public int serveSeconds = 5;
        }

        [SerializeField] private List<Supply> supplies = new();
        [SerializeField] private List<Dish> dishes = new();

        private OfferAsset[] _offers;
        private RecipeAsset[] _menu;

        public IReadOnlyList<Supply> Supplies => supplies;
        public IReadOnlyList<Dish> Dishes => dishes;

        public static string OfferId(Supply supply) => $"supplier-{supply.itemId}-{supply.packQuantity}";
        public static string MenuId(Dish dish) => $"counter-sell-{dish.itemId}";

        // One supplier offer per supply row, made once per load of this asset (runtime objects, never saved).
        public IReadOnlyList<OfferAsset> Offers() => _offers ??= supplies.Where(x => x != null && !string.IsNullOrWhiteSpace(x.itemId))
            .Select(x => OfferAsset.CreateSupply(OfferId(x), x.itemId, x.packQuantity, x.packPriceCents, x.spoilAfterSeconds)).ToArray();

        // One menu item (a sale recipe at the register) per dish row, made once per load of this asset.
        public IReadOnlyList<RecipeAsset> Menu() => _menu ??= dishes.Where(x => x != null && !string.IsNullOrWhiteSpace(x.itemId))
            .Select(x => RecipeAsset.CreateSale(MenuId(x), "Sell " + (string.IsNullOrWhiteSpace(x.displayName) ? x.itemId : x.displayName),
                x.itemId, x.saleCents, x.tier, x.cuisine ?? "", x.serveSeconds)).ToArray();

        // Edits in the Inspector rebuild the generated content on next use.
        private void OnValidate()
        {
            _offers = null;
            _menu = null;
        }
    }
}
