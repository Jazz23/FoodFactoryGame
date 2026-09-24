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

        public string Id => id;
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
            OutputItemId = outputItemId, OutputQuantity = outputQuantity, OutputSpoilAfterSeconds = outputSpoilAfterSeconds
        };
    }
}
