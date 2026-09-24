// Verifies refrigeration (decision 0018): a placed fridge's refrigerated input pauses spoilage and stays refrigerated through
// pickup and placement, and a job finishing into a refrigerated output does not age its overshoot, in one step or many.
using System;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class RefrigerationTests
    {
        private GoodsWorld _world;

        [SetUp]
        public void SetUp() => _world = CreateWorld();

        // TEST-ONLY values: a 10x10 restaurant with a 1x1 fridge (4 refrigerated input slots), a 1x1 oven whose output is
        // refrigerated, 5 dough (spoils after 100 s) in the chef's inventory, and a bake of 1 dough to 1 bread in 5 s, bread
        // spoiling after 10 s. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsEquipment { Id = "fridge-1", Kind = "fridge", SiteId = "restaurant", Width = 1, Depth = 1, InputCapacity = 4, OutputCapacity = 1, InputRefrigerated = true });
            world.Bootstrap(new GoodsEquipment { Id = "oven-cold", Kind = "oven", SiteId = "restaurant", CellX = 2, Width = 1, Depth = 1, InputCapacity = 2, OutputCapacity = 2, OutputRefrigerated = true });
            world.Bootstrap(new GoodsLot { Id = "dough", ItemId = "dough", OwnerId = "restaurant", LocationId = "carried:chef", Quantity = 5, SpoilAfterSeconds = 100 });
            world.Grant("chef", "restaurant");
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "bake", StationKind = "oven", DurationSeconds = 5, Inputs = { new RecipeInput { ItemId = "dough", Quantity = 1 } },
                OutputItemId = "bread", OutputQuantity = 1, OutputSpoilAfterSeconds = 10
            });
            return world;
        }

        private GoodsOutcome Move(string request, string lot, int quantity, string destination) =>
            _world.Transfer("chef", new TransferIntent { RequestId = request, LotId = lot, DestinationId = destination, Quantity = quantity });

        [Test]
        public void PlacedFridgePausesSpoilageAndStaysRefrigeratedWhenMoved()
        {
            Assert.That(_world.Snapshot().Locations.Single(x => x.Id == "fridge-1:in").Refrigerated, Is.True);
            _world.Advance(30);
            var chilled = Move("chill", "dough", 2, "fridge-1:in");
            Assert.That(chilled.Accepted, Is.True);
            _world.Advance(1000);
            var state = _world.Snapshot();
            var warm = state.Lots.Single(x => x.Id == "dough");
            var cold = state.Lots.Single(x => x.Id == chilled.MovedLotId);
            Assert.That((warm.ExposureSeconds, warm.Spoiled), Is.EqualTo((100L, true)));
            Assert.That((cold.ExposureSeconds, cold.Spoiled), Is.EqualTo((30L, false)), "Refrigerated time adds nothing.");

            // Pickup sweeps the chilled goods into the inventory with their history; placing again restores a refrigerated input.
            Assert.That(_world.PickUp("chef", "lift", "fridge-1").Accepted, Is.True);
            Assert.That(_world.Place("chef", "drop", "fridge-1", 5, 5, 1).Accepted, Is.True);
            state = _world.Snapshot();
            Assert.That(state.Locations.Single(x => x.Id == "fridge-1:in").Refrigerated, Is.True);
            var carried = state.Lots.Single(x => x.Id == chilled.MovedLotId);
            Assert.That((carried.LocationId, carried.ExposureSeconds), Is.EqualTo(("carried:chef", 30L)));
            _world.Advance(70);
            Assert.That(_world.Snapshot().Lots.Single(x => x.Id == chilled.MovedLotId).Spoiled, Is.True, "Out of the cold, spoilage resumes.");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(_world.Snapshot()));
            var tampered = _world.Snapshot();
            tampered.Locations.Single(x => x.Id == "fridge-1:in").Refrigerated = false;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(tampered), "A placed fridge's input must be refrigerated.");
        }

        [Test]
        public void JobOutputIntoARefrigeratedBufferDoesNotAgeInOneStepOrMany()
        {
            Assert.That(Move("load", "dough", 1, "oven-cold:in").Accepted, Is.True);
            Assert.That(_world.StartJob("chef", "bake", "oven-cold", "bake").Accepted, Is.True);
            var stepwise = GoodsWorld.Restore(_world.Snapshot());
            _world.Advance(25);
            for (var second = 0; second < 25; second++) stepwise.Advance(1);
            var bread = _world.Snapshot().Lots.Single(x => x.ItemId == "bread");
            Assert.That((bread.ExposureSeconds, bread.Spoiled), Is.EqualTo((0L, false)), "20 s past completion in the cold; at ambient it would have spoiled after 10.");
            var many = stepwise.Snapshot().Lots.Single(x => x.ItemId == "bread");
            Assert.That((many.ExposureSeconds, many.Spoiled), Is.EqualTo((bread.ExposureSeconds, bread.Spoiled)));
        }
    }
}
