// Presentation geometry in front of a competitor's building (decision 0033). Competitor lots have no shell, NavMesh or site, but
// like every lot their building spans the lot except toward the street, with its door on the street side (decision 0028), so
// customer figures walk a fixed line: along the street band, across the apron to the door, and queue along the facade. Placed by
// SitePlacement like any lot. Presentation only.
using System.Collections.Generic;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Customers
{
    public sealed class CompetitorFrontage
    {
        // PROTOTYPE (decision 0033): queue places drawn outside the door; customers further back get no figure.
        public const int QueueSpots = 8;
        private const float Spacing = 0.8f;

        public string LotId { get; private set; }
        // The lot's grid centre at its building's ground floor, for distance from the camera.
        public Vector3 Anchor { get; private set; }
        // On the apron just outside the door, where figures go in and come out.
        public Vector3 Door { get; private set; }
        // On the lot's street edge, in line with the door.
        public Vector3 Apron { get; private set; }
        // On the street band, in line with the door.
        public Vector3 Kerb { get; private set; }
        public Vector3 Outward { get; private set; }
        // Along the facade, toward the side of the door with more room.
        public Vector3 Along { get; private set; }
        // Points on the street band (SiteStreet) where figures appear and leave.
        public IReadOnlyList<Vector3> Street { get; private set; }
        private int _perRow;

        // Null when the lot is unknown or its building has no door.
        public static CompetitorFrontage For(SitePlacement placement, string lotId)
        {
            var offer = placement?.OfferOf(lotId);
            if (offer == null || offer.Doors.Count == 0) return null;
            var door = offer.Doors[0];
            var outward = door.Z == offer.BuildingZ + offer.BuildingDepth - 1 ? new Vector2Int(0, 1)
                : door.Z == offer.BuildingZ ? new Vector2Int(0, -1)
                : door.X == offer.BuildingX ? new Vector2Int(-1, 0) : new Vector2Int(1, 0);
            var anchor = placement.SiteOrigin(offer.SiteId);
            var cell = anchor + new Vector3(door.X + 0.5f - offer.Width * 0.5f, 0f, door.Z + 0.5f - offer.Depth * 0.5f) * SiteGrid.CellSize;
            var out3 = new Vector3(outward.x, 0f, outward.y);
            // Metres from the door cell's centre to the lot's street edge, and the room on each side of the door along the facade.
            var edge = (outward.x > 0 ? offer.Width - (door.X + 0.5f) : outward.x < 0 ? door.X + 0.5f
                : outward.y > 0 ? offer.Depth - (door.Z + 0.5f) : door.Z + 0.5f) * SiteGrid.CellSize;
            var (low, high) = outward.x == 0 ? (door.X + 0.5f, offer.Width - (door.X + 0.5f)) : (door.Z + 0.5f, offer.Depth - (door.Z + 0.5f));
            var along = (outward.x == 0 ? Vector3.right : Vector3.forward) * (high >= low ? 1f : -1f);
            return new CompetitorFrontage
            {
                LotId = lotId, Anchor = anchor, Outward = out3, Along = along,
                Door = cell + out3 * (0.5f * SiteGrid.CellSize + 0.3f),
                Apron = cell + out3 * Mathf.Max(0.5f * SiteGrid.CellSize + 0.3f, edge - 0.3f),
                Kerb = cell + out3 * (edge + SiteStreet.Band * 0.5f),
                Street = SiteStreet.Points(anchor, offer.Width, offer.Depth, outward),
                _perRow = Mathf.Max(1, Mathf.FloorToInt((Mathf.Max(low, high) * SiteGrid.CellSize - 0.4f) / Spacing))
            };
        }

        // The rank-th place in the queue (0 is next to the door): along the facade, then back along a row further out, so the line
        // bends at its far end instead of restarting beside the door.
        public Vector3 QueueSpot(int rank)
        {
            var row = rank / _perRow;
            var column = row % 2 == 0 ? rank % _perRow : _perRow - 1 - rank % _perRow;
            return Door + Outward * (Spacing * (1 + row) - 0.3f) + Along * (Spacing * (column + 1));
        }

        // The walk from a point on the street to a target on the apron: along the street to the door's line, then across.
        public Vector3[] Arrive(Vector3 target) => new[] { Kerb, Apron, target };

        // The walk from the door (or the queue) out to a street point.
        public Vector3[] Depart(Vector3 street) => new[] { Apron, Kerb, street };
    }
}
