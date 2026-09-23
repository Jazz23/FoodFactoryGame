// Slot capacity (decision 0009): a location's Capacity counts slots, and each (item, spoiled) stack in it takes
// ceil(quantity / max stack) slots. Shared by the server's entry checks and the client's previews so both count alike;
// max stack sizes are content, supplied by the caller.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public static class GoodsSlots
    {
        public static int Slots(long quantity, int maxStack)
        {
            maxStack = Math.Max(1, maxStack);
            return (int)Math.Min(int.MaxValue, (Math.Max(0, quantity) + maxStack - 1) / maxStack);
        }

        public static int SlotsUsed(IEnumerable<GoodsLot> lots, Func<string, int> maxStack) =>
            lots.GroupBy(x => (x.ItemId, x.Spoiled)).Sum(x => Slots(x.Sum(y => (long)y.Quantity), maxStack(x.Key.ItemId)));

        // Units of one stack that can still enter the location: its partly filled slot plus every free slot.
        public static long FreeUnits(GoodsLocation location, IEnumerable<GoodsLot> lotsInLocation, string itemId, bool spoiled,
            Func<string, int> maxStack)
        {
            if (location == null) return 0;
            var lots = lotsInLocation.Where(x => x.LocationId == location.Id).ToList();
            var own = lots.Where(x => x.ItemId == itemId && x.Spoiled == spoiled).Sum(x => (long)x.Quantity);
            var others = SlotsUsed(lots.Where(x => x.ItemId != itemId || x.Spoiled != spoiled), maxStack);
            return Math.Max(0, (long)(location.Capacity - others) * Math.Max(1, maxStack(itemId)) - own);
        }

        public static long FreeUnits(GoodsSnapshot site, string locationId, string itemId, bool spoiled, Func<string, int> maxStack) =>
            FreeUnits(site?.Locations.FirstOrDefault(x => x.Id == locationId), site?.Lots ?? new List<GoodsLot>(), itemId, spoiled, maxStack);
    }
}
