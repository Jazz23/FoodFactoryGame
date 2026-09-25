// Authored recipe content. The server registers it with GoodsWorld at start; recipes are never saved, and a started job
// keeps its own copy of output and duration, so editing this asset never changes a batch already in progress.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Recipe")]
    public sealed class RecipeAsset : ScriptableObject
    {
        [Serializable] public sealed class Ingredient
        {
            public string itemId;
            [Min(1)] public int quantity = 1;
        }

        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string stationKind;
        [SerializeField, Min(1)] private int durationSeconds = 1;
        [SerializeField] private List<Ingredient> inputs = new();
        [SerializeField] private string outputItemId;
        [SerializeField, Min(1)] private int outputQuantity = 1;
        [SerializeField, Min(1)] private int outputSpoilAfterSeconds = 1;
        // Sale recipe (decision 0013): above zero, the recipe is a menu item customers buy for this many cents (decision 0024)
        // instead of goods a station makes, and the output fields are ignored.
        [SerializeField, Min(0)] private int saleCents;
        // Menu attributes of a sale recipe (decision 0024): tier (1 basic and up) and cuisine, which customers weigh.
        [SerializeField, Min(1)] private int tier = 1;
        [SerializeField] private string cuisine;

        public string Id => id;
        public bool IsSale => saleCents > 0;
        public long SaleCents => saleCents;
        public int Tier => tier;
        public string Cuisine => cuisine ?? "";
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public string StationKind => stationKind;
        public int DurationSeconds => durationSeconds;
        public IReadOnlyList<Ingredient> Inputs => inputs;
        public string OutputItemId => outputItemId;
        public int OutputQuantity => outputQuantity;

        public RecipeDefinition ToDefinition() => new()
        {
            Id = id, StationKind = stationKind, DurationSeconds = durationSeconds,
            Inputs = inputs.Select(x => new RecipeInput { ItemId = x.itemId, Quantity = x.quantity }).ToList(),
            OutputItemId = IsSale ? "" : outputItemId, OutputQuantity = IsSale ? 0 : outputQuantity,
            OutputSpoilAfterSeconds = IsSale ? 0 : outputSpoilAfterSeconds, SaleCents = saleCents, Tier = tier, Cuisine = Cuisine
        };
    }
}
