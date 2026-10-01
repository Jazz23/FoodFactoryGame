// The crowd view (decision 0033): a read-only copy of the customers at lot-linked competitors near a map point, so clients can
// draw them. Competitors are not sites and never appear in a site baseline. It is derived from live state like View, carries
// only what drawing needs (never patience, prices or reputation), and is never stored, validated or read by the simulation.
using System;
using System.Collections.Generic;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsCrowdView
    {
        public long Revision;
        // Competitors within range that have at least one customer to draw, in catalog order.
        public List<GoodsCrowdRestaurant> Restaurants = new();
    }

    [Serializable] public sealed class GoodsCrowdRestaurant
    {
        public string CompetitorId;
        public string LotId;
        public int Servers;
        public int Seats;
        // In snapshot order.
        public List<GoodsCrowdCustomer> Customers = new();
    }

    [Serializable] public sealed class GoodsCrowdCustomer
    {
        public string Id;
        public string DistrictId;
        public int Appearance;
        public bool DineIn;
        public CustomerState State;
        public long Ticket;
        // Travelling only; 0 otherwise.
        public long RemainingSeconds;
        // True for a customer who walked out of another restaurant: their figure may already be on screen there.
        public bool WalkedOut;
    }

    public sealed partial class GoodsWorld
    {
        // PROTOTYPE (decision 0033): how far (Manhattan metres, like every range) from a player competitors' customers are sent.
        public const int CrowdRadiusMetres = 150;
        // Travelling customers are sent only this close to arriving, the window in which figures first appear (CustomerPresenter),
        // unless they walked out of another restaurant.
        public const long CrowdTravelSeconds = 12;

        public GoodsCrowdView CrowdNear(float mapX, float mapZ, int radiusMetres = CrowdRadiusMetres)
        {
            lock (_gate)
            {
                var view = new GoodsCrowdView { Revision = _state.Revision };
                if (float.IsNaN(mapX) || float.IsNaN(mapZ)) return view;
                var near = new Dictionary<string, GoodsCrowdRestaurant>(StringComparer.Ordinal);
                foreach (var competitor in _state.Competitors)
                {
                    if (competitor.LotId == "" || Math.Abs(competitor.MapX - mapX) + Math.Abs(competitor.MapZ - mapZ) > radiusMetres) continue;
                    var restaurant = new GoodsCrowdRestaurant
                    {
                        CompetitorId = competitor.Id, LotId = competitor.LotId, Servers = competitor.Servers, Seats = competitor.Seats
                    };
                    near.Add(competitor.Id, restaurant);
                    view.Restaurants.Add(restaurant);
                }
                if (near.Count == 0) return view;
                foreach (var customer in _state.Customers)
                {
                    if (!near.TryGetValue(customer.RestaurantId, out var restaurant)) continue;
                    var travelling = customer.State == CustomerState.Travelling;
                    var walkedOut = customer.LeftRestaurantId != "";
                    if (travelling && customer.RemainingSeconds > CrowdTravelSeconds && !walkedOut) continue;
                    restaurant.Customers.Add(new GoodsCrowdCustomer
                    {
                        Id = customer.Id, DistrictId = customer.DistrictId, Appearance = customer.Appearance, DineIn = customer.DineIn,
                        State = customer.State, Ticket = customer.Ticket, RemainingSeconds = travelling ? customer.RemainingSeconds : 0,
                        WalkedOut = walkedOut
                    });
                }
                view.Restaurants.RemoveAll(x => x.Customers.Count == 0);
                return view;
            }
        }

        // What a client would draw differently: the competitors listed and each customer's ID, state and ticket. Travel time is left
        // out, so a crowd is only resent when someone arrives, changes state or leaves.
        public static string CrowdSignature(GoodsCrowdView crowd)
        {
            if (crowd == null) return "";
            var text = new System.Text.StringBuilder();
            foreach (var restaurant in crowd.Restaurants)
            {
                text.Append(restaurant.CompetitorId).Append('[');
                foreach (var customer in restaurant.Customers)
                    text.Append(customer.Id).Append(':').Append((int)customer.State).Append(':').Append(customer.Ticket).Append(';');
                text.Append(']');
            }
            return text.ToString();
        }
    }
}
