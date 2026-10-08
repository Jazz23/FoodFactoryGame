// Restaurant furnishing (decision 0034): tables, registers, decor, docks and machines are placed equipment with no contractor.
// Build mode buys and places in one order (BuyAndPlace: charge and placed pieces in one commit, all or nothing, each piece
// recording its price), and anything can be sold back at any time for exactly what it was charged (Sell). Selling a piece
// first moves the goods in its buffers, and the inputs of a running job, into the seller's inventory; the goods keep their IDs,
// nothing is deleted or duplicated, and a sale that cannot fit them all is refused. A register sells only while staffed by
// the player or an employee (Staff); staffing by a player ends when the player leaves the site or the server, and a player
// works at most one register.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GridPlacement
    {
        public int X;
        public int Z;
        public int Rotation;
    }

    // Buy one equipment offer per placement, placed at once (build mode).
    [Serializable] public sealed class FurnishOrder
    {
        public string SiteId;
        public string OfferId;
        public int Level;
        public List<GridPlacement> Placements = new();
    }

    public sealed partial class GoodsWorld
    {
        // Pieces one buy-and-place order may place (a floor finish dragged over a room); bounds the work of one request.
        public const int MaxFurnishPlacements = 200;

        // The dock street rule with the site's listed lot (decision 0034).
        private string DockProblem(GoodsSnapshot state, GoodsEquipment piece, int cellX, int cellZ, int rotation) =>
            RestaurantRules.DockProblem(state, piece, cellX, cellZ, rotation, _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == piece.SiteId));

        // The listed lot of a site, for client previews of the dock rule; null for sites without one.
        public PropertyOffer OfferOfSite(string siteId)
        {
            lock (_gate) return _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == siteId);
        }

        // Pure: the first problem of placing one piece of the template at each placement, in order, each seeing the ones before;
        // null when all fit. Shared by the server's command and the client's preview.
        public static string FurnishProblem(GoodsSnapshot state, string siteId, GoodsEquipment template, IReadOnlyList<GridPlacement> placements,
            int level, PropertyOffer offer)
        {
            if (template == null) return "invalid-offer";
            if (placements == null || placements.Count == 0 || placements.Count > MaxFurnishPlacements || placements.Any(x => x == null))
                return "invalid-placement";
            var probe = new GoodsSnapshot
            {
                WorldId = state.WorldId, SiteLayouts = state.SiteLayouts, Belts = state.Belts, Buildings = state.Buildings,
                Equipment = new List<GoodsEquipment>(state.Equipment)
            };
            for (var index = 0; index < placements.Count; index++)
            {
                var placement = placements[index];
                var piece = JsonUtility.FromJson<GoodsEquipment>(JsonUtility.ToJson(template));
                piece.Id = $"\u0000preview:{index}";
                piece.SiteId = siteId;
                piece.State = EquipmentState.Placed;
                piece.HolderId = "";
                piece.CellX = placement.X;
                piece.CellZ = placement.Z;
                piece.Rotation = placement.Rotation;
                piece.Level = level;
                var problem = SiteGrid.PlacementProblem(probe, piece, placement.X, placement.Z, placement.Rotation, level)
                    ?? RestaurantRules.DockProblem(probe, piece, placement.X, placement.Z, placement.Rotation, offer);
                if (problem != null) return problem;
                probe.Equipment.Add(piece);
            }
            return null;
        }

        // Volatile primitive for tests. Live request handlers must call BuyAndPlaceDurably.
        // Checks, in order: identity and replay; a grant on the site (forbidden); an equipment offer (invalid-offer); the site's
        // company (no-company) and grid (no-layout); every placement (FurnishProblem); the price of all pieces (insufficient-funds).
        // Pieces get short IDs <kind>-<n> (NewEquipmentId). Only the accepted order is recorded, so a retry replays and never pays twice.
        public GoodsOutcome BuyAndPlace(string playerId, string requestId, FurnishOrder order)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                var siteId = order?.SiteId;
                if (siteId == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId)) return Reject("forbidden");
                if (order.OfferId is null || !_equipmentOffers.TryGetValue(order.OfferId, out var offer)) return Reject("invalid-offer");
                var company = CompanyOfSiteLocked(siteId);
                if (company is null) return Reject("no-company");
                if (!_state.SiteLayouts.Any(x => x.SiteId == siteId)) return Reject("no-layout");
                var problem = FurnishProblem(_state, siteId, offer.Equipment, order.Placements, order.Level,
                    _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == siteId));
                if (problem is not null) return Reject(problem);
                var price = checked(offer.PriceCents * order.Placements.Count);
                if (_state.Companies.First(x => x.Id == company).Cash < price) return Reject("insufficient-funds");

                // All checks precede this single locked mutation.
                TryDebit(company, price);
                string first = null;
                for (var index = 0; index < order.Placements.Count; index++)
                {
                    var placement = order.Placements[index];
                    var piece = JsonUtility.FromJson<GoodsEquipment>(JsonUtility.ToJson(offer.Equipment));
                    piece.Id = NewEquipmentId(piece.Kind);
                    piece.SiteId = siteId;
                    piece.State = EquipmentState.Placed;
                    piece.HolderId = "";
                    piece.StaffId = "";
                    piece.Layer ??= "";
                    piece.CellX = placement.X;
                    piece.CellZ = placement.Z;
                    piece.Rotation = placement.Rotation;
                    piece.Level = order.Level;
                    piece.ChargedCents = offer.PriceCents;
                    _state.Equipment.Add(piece);
                    AddPlacedParts(piece);
                    first ??= piece.Id;
                }
                InvalidateDiners();
                return RecordCents(requestId, playerId, "bought", price, first);
            }
        }

        public GoodsOutcome BuyAndPlaceDurably(string playerId, string requestId, FurnishOrder order, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => BuyAndPlace(playerId, requestId, order));
        }

        // Volatile primitive for tests. Live request handlers must call SellDurably.
        // Checks, in order: identity and replay; the piece and a grant on its site (forbidden); a held piece must be the seller's
        // (not-held); a placed one must not serve or seat a customer (occupied), carry tabletop decor (blocked), be on a truck route
        // (in-route) or hold reserved goods (reserved); the seller's inventory on that site (no-inventory) must fit its goods and
        // a running job's inputs (capacity). Then, in one mutation, the goods move to the seller's inventory, the station, buffers
        // and the piece are removed and the site's company gets back exactly what the piece was charged.
        public GoodsOutcome Sell(string playerId, string requestId, string equipmentId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                var piece = _state.Equipment.FirstOrDefault(x => x.Id == equipmentId);
                if (piece is null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == piece.SiteId)) return Reject("forbidden");
                var company = CompanyOfSiteLocked(piece.SiteId);
                if (company is null && piece.ChargedCents > 0) return Reject("no-company");
                if (piece.State == EquipmentState.Held && piece.HolderId != playerId) return Reject("not-held");
                if (_state.Routes.Any(x => x.PickupDockId == piece.Id || x.DropoffDockId == piece.Id)) return Reject("in-route");
                var buffered = new List<GoodsLot>();
                var returned = new List<GoodsLot>();
                StationJob job = null;
                GoodsStation station = null;
                GoodsLocation inventory = null;
                if (piece.State == EquipmentState.Placed)
                {
                    if (_state.Customers.Any(x => x.CounterId == piece.Id || x.TableId == piece.Id)) return Reject("occupied");
                    if (HoldsTabletop(piece)) return Reject("blocked");
                    buffered = _state.Lots.Where(x => x.LocationId == piece.InputLocationId || x.LocationId == piece.OutputLocationId).ToList();
                    if (buffered.Any(x => _state.Reservations.Any(y => y.Active && y.LotId == x.Id))) return Reject("reserved");
                    station = _state.Stations.First(x => x.Id == piece.Id);
                    job = _state.Jobs.FirstOrDefault(x => x.StationId == station.Id);
                    if (buffered.Count > 0 || job is not null)
                    {
                        inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                        if (inventory is null || inventory.SiteId != piece.SiteId) return Reject("no-inventory");
                        returned = job == null ? new List<GoodsLot>()
                            : job.State == StationJobState.Blocked ? new List<GoodsLot> { OutputLot(job, station, inventory.Id, 0) }
                            : job.Inputs.Select(x => JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(x))).ToList();
                        if (!FitsAll(inventory.Id, buffered.Concat(returned))) return Reject("capacity");
                    }
                }

                // All checks precede this single locked mutation. Swept lots keep their IDs and exposure.
                foreach (var lot in buffered) lot.LocationId = inventory.Id;
                foreach (var lot in returned)
                {
                    lot.LocationId = inventory.Id;
                    _state.Lots.Add(lot);
                }
                if (job != null) _state.Jobs.Remove(job);
                if (station != null) _state.Stations.Remove(station);
                _state.Locations.RemoveAll(x => x.Id == piece.InputLocationId || x.Id == piece.OutputLocationId);
                _state.Equipment.Remove(piece);
                if (piece.ChargedCents > 0) TryCredit(company, piece.ChargedCents);
                InvalidateDiners();
                var result = RecordCents(requestId, playerId, "sold", -piece.ChargedCents, piece.Id);
                result.JobId = job?.Id;
                _state.Outcomes[_state.Outcomes.Count - 1].JobId = job?.Id;
                return result;
            }
        }

        public GoodsOutcome SellDurably(string playerId, string requestId, string equipmentId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => Sell(playerId, requestId, equipmentId));
        }

        // Volatile primitive for tests. Live request handlers must call StaffDurably.
        // Sets who works a register: staffId is the requester (who must be on the register's site), an employee of that site, or
        // empty to leave it unstaffed. Checks, in order: identity and replay; the register and a grant on its site (forbidden); a
        // counter (not-a-register) that is placed (not-placed); the staff (not-on-site, unknown-employee, forbidden); a change
        // (unchanged). The staff stops working any other register.
        public GoodsOutcome Staff(string playerId, string requestId, string registerId, string staffId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                staffId ??= "";
                var register = _state.Equipment.FirstOrDefault(x => x.Id == registerId);
                if (register is null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == register.SiteId)) return Reject("forbidden");
                if (register.Kind != CounterKind) return Reject("not-a-register");
                if (register.State != EquipmentState.Placed) return Reject("not-placed");
                if (staffId == playerId)
                {
                    if (_state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId))?.SiteId != register.SiteId) return Reject("not-on-site");
                }
                else if (staffId.StartsWith(EmployeePrefix, StringComparison.Ordinal))
                {
                    if (!_state.Employees.Any(x => x.Id == staffId && x.SiteId == register.SiteId)) return Reject("unknown-employee");
                }
                else if (staffId != "") return Reject("forbidden");
                if (register.StaffId == staffId) return Reject("unchanged");

                if (staffId != "") ReleaseStaffLocked(staffId);
                register.StaffId = staffId;
                InvalidateDiners();
                return Record(requestId, playerId, true, staffId == "" ? "unstaffed" : "staffed", null);
            }
        }

        public GoodsOutcome StaffDurably(string playerId, string requestId, string registerId, string staffId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => Staff(playerId, requestId, registerId, staffId));
        }

        // Server-only: a player who leaves the server stops working their register; null clears every register a player (not an
        // employee) works, as on a server start, when nobody is connected. Committed before returning; true when something changed.
        public bool ReleaseStaffDurably(string actorId, string savePath)
        {
            lock (_gate)
            {
                var stale = _state.Equipment.Where(x => !string.IsNullOrEmpty(x.StaffId)
                    && (actorId == null ? !x.StaffId.StartsWith(EmployeePrefix, StringComparison.Ordinal) : x.StaffId == actorId)).ToList();
                if (stale.Count == 0) return false;
                return Durably(savePath, () =>
                {
                    foreach (var register in _state.Equipment.Where(x => stale.Any(y => y.Id == x.Id))) register.StaffId = "";
                    InvalidateDiners();
                    _state.Revision++;
                    return true;
                }, () => false);
            }
        }

        // Call only under _gate: clears the actor's register, if any; true when one was cleared. The caller counts the revision.
        private bool ReleaseStaffLocked(string actorId)
        {
            var changed = false;
            foreach (var register in _state.Equipment.Where(x => x.StaffId == actorId && !string.IsNullOrEmpty(actorId)))
            {
                register.StaffId = "";
                changed = true;
            }
            return changed;
        }
    }
}
