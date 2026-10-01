// Verifies the crowd view (decision 0033): only lot-linked competitors within the radius, in catalog order; travelling
// customers only near arrival unless they walked out elsewhere; no simulation-only fields; asking for it never changes what the
// world does; and its signature ignores travel time but not arrivals or state changes. No saves are written.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class CrowdTests
    {
        // TEST-ONLY values, not gameplay content: one district and four competitors on a line at z 200. "a" and "b" (140 m east)
        // are within 150 m of "a"; "far" is 300 m away; "dev" has no lot and stands on "a".
        private static GoodsWorld CreateWorld(int customersPerHour = 0)
        {
            var world = new GoodsWorld("crowd-test");
            world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "District", MapX = 100, MapZ = 260, CustomersPerHour = customersPerHour, WealthPercent = 50,
                AppearanceVariants = 3, LikedCuisines = { "bakery" }, DineInPercent = 50, RangeMetres = 500
            });
            world.Bootstrap(Competitor("a", "lot-a", 100));
            world.Bootstrap(Competitor("b", "lot-b", 240));
            world.Bootstrap(Competitor("far", "lot-far", 400));
            world.Bootstrap(Competitor("dev", "", 100));
            return world;
        }

        private static GoodsCompetitor Competitor(string id, string lotId, int mapX) => new()
        {
            Id = id, Name = id, LotId = lotId, MapX = mapX, MapZ = 200, Cuisine = "bakery", Tier = 1, PriceCents = 300, Servers = 2,
            ServiceSeconds = 20, Seats = 8
        };

        private static GoodsCustomer Customer(string id, string restaurant, CustomerState state, long remaining = 0, string left = "",
            long ticket = 0)
        {
            var paying = state is CustomerState.Ordering or CustomerState.Eating;
            return new GoodsCustomer
            {
                Id = id, DistrictId = "district", Appearance = 1, DineIn = state == CustomerState.Eating, PatienceSeconds = 200,
                State = state, RestaurantId = restaurant, RemainingSeconds = remaining, LeftRestaurantId = left, Ticket = ticket,
                PaidCents = paying ? 300 : 0, TableId = state == CustomerState.Eating ? restaurant : ""
            };
        }

        [Test]
        public void OnlyNearbyLinkedCompetitorsAndTheirArrivingCustomersAreIncluded()
        {
            var world = CreateWorld();
            Assert.That(world.CrowdNear(100, 200).Restaurants, Is.Empty, "Competitors without customers are left out.");
            world.Bootstrap(Customer("queued", "a", CustomerState.Queued, ticket: 4));
            world.Bootstrap(Customer("arriving", "a", CustomerState.Travelling, remaining: 10));
            world.Bootstrap(Customer("on-the-way", "a", CustomerState.Travelling, remaining: 30));
            world.Bootstrap(Customer("walked-out", "a", CustomerState.Travelling, remaining: 60, left: "b"));
            world.Bootstrap(Customer("eating", "b", CustomerState.Eating, remaining: 100));
            world.Bootstrap(Customer("at-dev", "dev", CustomerState.Queued));
            world.Bootstrap(Customer("at-far", "far", CustomerState.Queued));

            var crowd = world.CrowdNear(100, 200);

            Assert.That(crowd.Restaurants.Select(x => (x.CompetitorId, x.LotId, x.Servers, x.Seats)),
                Is.EqualTo(new[] { ("a", "lot-a", 2, 8), ("b", "lot-b", 2, 8) }));
            var a = crowd.Restaurants[0].Customers;
            Assert.That(a.Select(x => (x.Id, x.State, x.RemainingSeconds, x.WalkedOut, x.Ticket)), Is.EqualTo(new[]
            {
                ("queued", CustomerState.Queued, 0L, false, 4L),
                ("arriving", CustomerState.Travelling, 10L, false, 0L),
                ("walked-out", CustomerState.Travelling, 60L, true, 0L)
            }));
            var eating = crowd.Restaurants[1].Customers.Single();
            Assert.That((eating.Id, eating.State, eating.DineIn, eating.RemainingSeconds, eating.DistrictId, eating.Appearance),
                Is.EqualTo(("eating", CustomerState.Eating, true, 0L, "district", 1)));

            var json = JsonUtility.ToJson(crowd);
            foreach (var hidden in new[] { "Patience", "Paid", "Price", "Reputation", "TableId", "LeftRestaurant", "Recipe" })
                Assert.That(json, Does.Not.Contain(hidden), "The crowd carries only what drawing needs.");

            Assert.That(world.CrowdNear(100, 200, 139).Restaurants.Select(x => x.CompetitorId), Is.EqualTo(new[] { "a" }));
            Assert.That(world.CrowdNear(700, 200).Restaurants, Is.Empty);
            Assert.That(world.CrowdNear(float.NaN, 200).Restaurants, Is.Empty);
        }

        [Test]
        public void AskingForCrowdsNeverChangesTheWorld()
        {
            var watched = CreateWorld(customersPerHour: 720);
            var unwatched = CreateWorld(customersPerHour: 720);
            var drawn = 0;
            for (var second = 0; second < 900; second++)
            {
                drawn += watched.CrowdNear(100, 200).Restaurants.Sum(x => x.Customers.Count);
                watched.Advance(1);
                unwatched.Advance(1);
            }
            Assert.That(drawn, Is.GreaterThan(0), "Customers reached the competitors during the run.");
            Assert.That(JsonUtility.ToJson(watched.Snapshot()), Is.EqualTo(JsonUtility.ToJson(unwatched.Snapshot())));
        }

        [Test]
        public void TheSignatureIgnoresTravelTimeButNotArrivalsOrStates()
        {
            var crowd = new GoodsCrowdView
            {
                Restaurants =
                {
                    new GoodsCrowdRestaurant
                    {
                        CompetitorId = "a",
                        Customers = { new GoodsCrowdCustomer { Id = "c", State = CustomerState.Travelling, RemainingSeconds = 9 } }
                    }
                }
            };
            var before = GoodsWorld.CrowdSignature(crowd);
            crowd.Restaurants[0].Customers[0].RemainingSeconds = 8;
            Assert.That(GoodsWorld.CrowdSignature(crowd), Is.EqualTo(before));
            crowd.Restaurants[0].Customers[0].State = CustomerState.Queued;
            Assert.That(GoodsWorld.CrowdSignature(crowd), Is.Not.EqualTo(before));
            var queued = GoodsWorld.CrowdSignature(crowd);
            crowd.Restaurants[0].Customers.Add(new GoodsCrowdCustomer { Id = "d" });
            Assert.That(GoodsWorld.CrowdSignature(crowd), Is.Not.EqualTo(queued));
            Assert.That(GoodsWorld.CrowdSignature(new GoodsCrowdView()), Is.EqualTo(""));
            Assert.That(GoodsWorld.CrowdSignature(null), Is.EqualTo(""));
        }
    }
}
