// Authored content for a goods item: its display name, inventory icon and max stack (units per slot). Goods state refers to
// items only by ID. The server registers every definition's max stack with GoodsWorld at start; an item without a
// definition shows its ID and a placeholder and stacks to 1.
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField, Min(1)] private int maxStack = 1;

        public string Id => id;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        public Sprite Icon => icon;
        public int MaxStack => Mathf.Max(1, maxStack);
    }
}
