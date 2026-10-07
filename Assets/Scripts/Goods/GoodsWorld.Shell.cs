// Restaurant shell editing (decision 0034, slice 1): the owner resizes a restaurant's rectangular shell within its site (the
// bought lot) and adds or removes interior walls, doors and windows, or changes the perimeter's wall finish. One pure planner,
// PlanShell, turns an order into the building it would leave, the cents it charges for new structure and the cents it refunds
// for removed structure; the client's build-mode preview and the server's command share it, so the preview shows exactly what
// the server checks. The server re-plans against its own state, then debits or credits the net amount and swaps the building
// in one mutation inside the durable boundary: an order is paid or refunded exactly once, and a rejected or failed order
// changes nothing. Every new piece records what it was charged (the order fee rides on its first new piece), so a later removal
// refunds exactly that. An order may not leave a wall over equipment, belts, tables or decor, nor leave decor without the wall or
// ceiling it needs; selling a piece is its own order. Shell edits apply to restaurants only and are instant once paid.
// Free walls (decision 0036): every order but a resize first turns a rectangular restaurant into free walls (ToFreeWalls), so
// the owner then places and removes outer walls exactly like interior ones, anywhere in the lot; the footprint follows the walls.
// Back doors (decision 0037): a back door is a door record with the service role on an outer wall. A restaurant that has one
// keeps at least one (no-back-door), and no order may take away the back door or open doorstep a dock stands beside
// (dock-attached).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // What an owner asks the shell to become. Kind picks the fields used.
    [Serializable] public sealed class ShellOrder
    {
        public const string Resize = "resize";
        public const string Partition = "partition";
        public const string Door = "door";
        public const string Window = "window";
        public const string Remove = "remove";
        public const string WallFinish = "finish";
        // A back door (decision 0037): a door on an outer wall, for staff and goods only.
        public const string BackDoor = "backdoor";
        // Resize applies only to a restaurant without free walls (decision 0036); build mode no longer offers it.

        public string Kind;
        public string BuildingId;
        // Resize: the new footprint (walls included). Door and window: the cell; window: its axis (0 along X, 1 along Z).
        public int X;
        public int Z;
        public int Width;
        public int Depth;
        public int Axis;
        // Partition, door, window and wall finish: the style from GoodsWorld's lists.
        public string Style = "";
        // Partition and remove: the cells. Wall finish: the walls to restyle (none restyles every wall).
        public List<GridCell> Cells = new();
    }

    // The planner's answer: the problem (null when the order is valid), the resulting building and what it costs.
    public sealed class ShellPlan
    {
        public string Problem;
        public GoodsBuilding Building;
        public long ChargeCents;
        public long RefundCents;
        public List<GoodsStructure> Added = new();
        public List<GoodsStructure> Removed = new();

        public long NetCents => ChargeCents - RefundCents;
    }

    public sealed partial class GoodsWorld
    {
        public const string FloorStructure = "floor";
        public const string WallStructure = "wall";
        public const string PartitionStructure = "partition";
        public const string DoorStructure = "door";
        public const string WindowStructure = "window";

        // Styles are presentation choices (the restaurant art kit); the server only checks that they are known.
        public static readonly IReadOnlyList<string> WallStyles = new[] { "plaster", "brick", "wainscot", "tile" };
        public static readonly IReadOnlyList<string> DoorStyles = new[] { "panel", "glazed", "kitchen" };
        public static readonly IReadOnlyList<string> WindowStyles = new[] { "picture", "mullioned", "hatch" };

        // PROTOTYPE prices (decision 0034 PROPOSAL "Price": per wall cell and per added floor cell plus a fixed fee per order,
        // scaled by the district's price percent; not decided). Whole cents before the district multiplier.
        public const long ShellFloorCellCents = 2_000;
        public const long ShellWallCellCents = 3_000;
        public const long ShellDoorCents = 15_000;
        public const long ShellWindowCents = 25_000;
        public const long ShellOrderFeeCents = 10_000;
        // Cells one partition or remove order may name; bounds the work of one request.
        public const int MaxShellCells = 400;

        // A base price at a site's district price percent (100 = list price), rounded down to whole cents, at least 1 cent.
        public static long ScaledCents(long baseCents, int pricePercent) => Math.Max(1, checked(baseCents * Math.Max(1, pricePercent) / 100));

        // The price percent of a site: its lot's district (PropertyOffer.PricePercent), or 100 for a site without a listed lot.
        private int PricePercentLocked(string siteId) => _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == siteId)?.PricePercent ?? 100;

        public int PricePercentOf(string siteId)
        {
            lock (_gate) return PricePercentLocked(siteId);
        }

        // Pure: what the order would do to the current state. Everything but the company's cash and the requester's rights is
        // checked here, in the same order as the server applies it.
        public static ShellPlan PlanShell(GoodsSnapshot state, ShellOrder order, int pricePercent = 100)
        {
            var plan = new ShellPlan();
            ShellPlan Fail(string problem)
            {
                plan.Problem = problem;
                plan.Building = null;
                return plan;
            }
            var current = order == null ? null : state.Buildings.FirstOrDefault(x => x.Id == order.BuildingId);
            if (current == null) return Fail("forbidden");
            if (current.Kind != RestaurantKind) return Fail("not-a-restaurant");
            var next = JsonUtility.FromJson<GoodsBuilding>(JsonUtility.ToJson(current));
            next.Structures ??= new List<GoodsStructure>();
            next.WallStyle ??= "";
            plan.Building = next;
            GoodsStructure Add(string kind, int x, int z, long baseCents, string style = "", int axis = 0)
            {
                var piece = new GoodsStructure { Kind = kind, X = x, Z = z, Axis = axis, Style = style, ChargedCents = ScaledCents(baseCents, pricePercent) };
                next.Structures.Add(piece);
                plan.Added.Add(piece);
                return piece;
            }
            void Drop(GoodsStructure piece)
            {
                if (next.Structures.Remove(piece)) plan.Removed.Add(piece);
            }
            var cells = (order.Cells ?? new List<GridCell>()).Where(x => x != null).Select(x => (x.X, x.Z)).ToList();
            var lot = state.SiteLayouts.FirstOrDefault(x => x.SiteId == current.SiteId);
            if (order.Kind != ShellOrder.Resize && !next.FreeWalls)
            {
                ToFreeWalls(next, plan);
                // Every check below reads the converted shell.
                current = JsonUtility.FromJson<GoodsBuilding>(JsonUtility.ToJson(next));
            }
            switch (order.Kind)
            {
                case ShellOrder.Resize:
                {
                    if (current.FreeWalls) return Fail("invalid-order");
                    var layout = lot;
                    if (order.Width < MinimumBuildingSize || order.Depth < MinimumBuildingSize) return Fail("too-small");
                    if (layout == null || order.X < 0 || order.Z < 0 || order.X + order.Width > layout.Width || order.Z + order.Depth > layout.Depth)
                        return Fail("out-of-bounds");
                    if (order.X == current.CellX && order.Z == current.CellZ && order.Width == current.Width && order.Depth == current.Depth)
                        return Fail("unchanged");
                    next.CellX = order.X;
                    next.CellZ = order.Z;
                    next.Width = order.Width;
                    next.Depth = order.Depth;
                    // Interior walls left outside the new interior go, with their doors and windows.
                    foreach (var partition in next.Structures.Where(x => x.Kind == PartitionStructure && !SiteGrid.IsInterior(next, x.X, x.Z)).ToList())
                    {
                        foreach (var fitting in next.Structures.Where(x => (x.Kind == DoorStructure && x.X == partition.X && x.Z == partition.Z)
                                     || (x.Kind == WindowStructure && SiteGrid.WindowCovers(x, partition.X, partition.Z))).ToList())
                            Drop(fitting);
                        Drop(partition);
                    }
                    // Doors and windows on a side move with it; one that lands on a corner or off the side goes (refunded).
                    var doors = new List<GridCell>();
                    foreach (var door in current.Doors)
                    {
                        var record = next.Structures.FirstOrDefault(x => x.Kind == DoorStructure && x.X == door.X && x.Z == door.Z);
                        var (x, z) = Project(current, next, door.X, door.Z);
                        if (SiteGrid.IsDoorCell(next, x, z))
                        {
                            doors.Add(new GridCell { X = x, Z = z });
                            if (record != null)
                            {
                                record.X = x;
                                record.Z = z;
                            }
                        }
                        else if (record != null) Drop(record);
                    }
                    next.Doors = doors;
                    foreach (var window in next.Structures.Where(x => x.Kind == WindowStructure && SiteGrid.OnPerimeter(current, x.X, x.Z)).ToList())
                    {
                        var (x, z) = Project(current, next, window.X, window.Z);
                        var moved = new GoodsStructure { Kind = WindowStructure, X = x, Z = z, Axis = window.Axis };
                        if (WindowFits(next, moved))
                        {
                            window.X = x;
                            window.Z = z;
                        }
                        else Drop(window);
                    }
                    // Paid footprint cells and perimeter walls that are no longer there are refunded; new ones are charged.
                    foreach (var floor in next.Structures.Where(x => x.Kind == FloorStructure && !Inside(next, x.X, x.Z)).ToList()) Drop(floor);
                    foreach (var wall in next.Structures.Where(x => x.Kind == WallStructure && !SiteGrid.OnPerimeter(next, x.X, x.Z)).ToList()) Drop(wall);
                    for (var x = next.CellX; x < next.CellX + next.Width; x++)
                    for (var z = next.CellZ; z < next.CellZ + next.Depth; z++)
                    {
                        if (!Inside(current, x, z)) Add(FloorStructure, x, z, ShellFloorCellCents);
                        if (SiteGrid.OnPerimeter(next, x, z) && !SiteGrid.OnPerimeter(current, x, z)) Add(WallStructure, x, z, ShellWallCellCents);
                    }
                    break;
                }
                case ShellOrder.Partition:
                    if (!WallStyles.Contains(order.Style ?? "")) return Fail("invalid-style");
                    if (cells.Count == 0 || cells.Count > MaxShellCells || cells.Distinct().Count() != cells.Count) return Fail("invalid-cell");
                    foreach (var (x, z) in cells)
                    {
                        // Free walls go anywhere in the lot that has no wall yet.
                        if (lot == null || x < 0 || z < 0 || x >= lot.Width || z >= lot.Depth || SiteGrid.IsPartition(current, x, z)) return Fail("invalid-cell");
                        Add(PartitionStructure, x, z, ShellWallCellCents, order.Style);
                    }
                    break;
                case ShellOrder.Door:
                case ShellOrder.BackDoor:
                {
                    if (!DoorStyles.Contains(order.Style ?? "")) return Fail("invalid-style");
                    if (SiteGrid.WindowAt(current, order.X, order.Z) != null || SiteGrid.IsDoor(current, order.X, order.Z)) return Fail("invalid-cell");
                    // A back door opens from an outer wall onto open ground of the lot (decision 0037).
                    var back = order.Kind == ShellOrder.BackDoor;
                    if (back && (SiteGrid.Doorstep(current, order.X, order.Z) is not { } step
                            || lot == null || step.X < 0 || step.Z < 0 || step.X >= lot.Width || step.Z >= lot.Depth))
                        return Fail("invalid-cell");
                    if (SiteGrid.IsDoorCell(current, order.X, order.Z)) next.Doors.Add(new GridCell { X = order.X, Z = order.Z });
                    else if (!SiteGrid.IsPartition(current, order.X, order.Z) || IsCorner(current, order.X, order.Z)) return Fail("invalid-cell");
                    var door = Add(DoorStructure, order.X, order.Z, ShellDoorCents, order.Style);
                    if (back) door.Role = ServiceDoorRole;
                    break;
                }
                case ShellOrder.Window:
                {
                    if (!WindowStyles.Contains(order.Style ?? "")) return Fail("invalid-style");
                    var window = new GoodsStructure { Kind = WindowStructure, X = order.X, Z = order.Z, Axis = order.Axis };
                    if ((order.Axis != 0 && order.Axis != 1) || !WindowFits(current, window)
                        || SiteGrid.WindowAt(current, order.X, order.Z) != null
                        || SiteGrid.WindowAt(current, order.Axis == 0 ? order.X + 1 : order.X, order.Axis == 0 ? order.Z : order.Z + 1) != null)
                        return Fail("invalid-cell");
                    Add(WindowStructure, order.X, order.Z, ShellWindowCents, order.Style, order.Axis);
                    break;
                }
                case ShellOrder.Remove:
                    if (cells.Count == 0 || cells.Count > MaxShellCells) return Fail("invalid-cell");
                    foreach (var (x, z) in cells.Distinct())
                    {
                        // One layer at a time: a window or door first, the interior wall under it on a later order. A window
                        // named by both of its cells goes once.
                        if (plan.Removed.Any(s => s.Kind == WindowStructure && SiteGrid.WindowCovers(s, x, z))) continue;
                        var window = SiteGrid.WindowAt(next, x, z);
                        var doorRecord = next.Structures.FirstOrDefault(s => s.Kind == DoorStructure && s.X == x && s.Z == z);
                        var perimeterDoor = next.Doors.FirstOrDefault(d => d.X == x && d.Z == z);
                        var partition = next.Structures.FirstOrDefault(s => s.Kind == PartitionStructure && s.X == x && s.Z == z);
                        if (window != null) Drop(window);
                        else if (perimeterDoor != null || doorRecord != null)
                        {
                            if (perimeterDoor != null) next.Doors.Remove(perimeterDoor);
                            if (doorRecord != null) Drop(doorRecord);
                            else plan.Removed.Add(new GoodsStructure { Kind = DoorStructure, X = x, Z = z });
                        }
                        else if (partition != null) Drop(partition);
                        else return Fail("invalid-cell");
                    }
                    break;
                case ShellOrder.WallFinish:
                {
                    if (!WallStyles.Contains(order.Style ?? "")) return Fail("invalid-style");
                    if (cells.Count > MaxShellCells) return Fail("invalid-cell");
                    // PROTOTYPE: changing a finish is free (a cosmetic style of walls already paid for). Named cells must be walls.
                    var walls = cells.Count == 0 ? next.Structures.Where(x => x.Kind == PartitionStructure).ToList()
                        : cells.Distinct().Select(c => next.Structures.FirstOrDefault(s => s.Kind == PartitionStructure && s.X == c.X && s.Z == c.Z)).ToList();
                    if (walls.Any(x => x == null)) return Fail("invalid-cell");
                    if (walls.All(x => x.Style == order.Style) && (cells.Count > 0 || next.WallStyle == order.Style)) return Fail("unchanged");
                    foreach (var wall in walls) wall.Style = order.Style;
                    if (cells.Count == 0) next.WallStyle = order.Style;
                    break;
                }
                default:
                    return Fail("invalid-order");
            }
            if (next.FreeWalls)
            {
                // The footprint follows the walls.
                var walls = next.Structures.Where(x => x.Kind == PartitionStructure).ToList();
                if (walls.Count == 0) return Fail("no-walls");
                next.CellX = walls.Min(x => x.X);
                next.CellZ = walls.Min(x => x.Z);
                next.Width = walls.Max(x => x.X) - next.CellX + 1;
                next.Depth = walls.Max(x => x.Z) - next.CellZ + 1;
            }
            if (next.Doors.Count == 0 && !next.Structures.Any(x => x.Kind == DoorStructure)) return Fail("no-door");
            // Decision 0037: a restaurant that has a back door keeps one (move one by placing the new door first), and a dock that
            // stood beside a back door still does: its door stays and its doorstep stays open.
            if (SiteGrid.ServiceDoors(current).Any() && !SiteGrid.ServiceDoors(next).Any()) return Fail("no-back-door");
            // Customers enter only by customer doors (owner decision 2026-10-06): a restaurant that has one keeps one.
            if (SiteGrid.CustomerDoors(current).Any() && !SiteGrid.CustomerDoors(next).Any()) return Fail("no-customer-door");
            // A back door's doorstep stays clear of object-layer pieces and belts (decision 0037; belts since 2026-10-06), so a new
            // back door, or a doorstep a wall change moves, may not land on one.
            var oldSteps = SiteGrid.ServiceDoors(current).Select(d => SiteGrid.Doorstep(current, d.X, d.Z)).ToList();
            if (SiteGrid.ServiceDoors(next).Select(d => SiteGrid.Doorstep(next, d.X, d.Z))
                    .Any(s => s is { } step && !oldSteps.Contains(step) && RestaurantRules.Covered(state, current.SiteId, step.X, step.Z)))
                return Fail("doorstep");
            var withNext = new GoodsSnapshot
            {
                WorldId = state.WorldId, SiteLayouts = state.SiteLayouts, Equipment = state.Equipment,
                Buildings = state.Buildings.Select(x => x.Id == current.Id ? next : x).ToList()
            };
            var withCurrent = new GoodsSnapshot
            {
                WorldId = state.WorldId, SiteLayouts = state.SiteLayouts, Equipment = state.Equipment,
                Buildings = state.Buildings.Select(x => x.Id == current.Id ? current : x).ToList()
            };
            if (state.Equipment.Any(x => x.SiteId == current.SiteId && x.Kind == DockKind && x.State == EquipmentState.Placed
                    && RestaurantRules.BesideBackDoor(withCurrent, current, x) && !RestaurantRules.BesideBackDoor(withNext, next, x)))
                return Fail("dock-attached");
            // The fee rides on the first new piece, so removing everything an order built refunds the fee too.
            if (plan.Added.Count > 0) plan.Added[0].ChargedCents = checked(plan.Added[0].ChargedCents + ScaledCents(ShellOrderFeeCents, pricePercent));
            plan.ChargeCents = plan.Added.Sum(x => x.ChargedCents);
            plan.RefundCents = plan.Removed.Sum(x => x.ChargedCents);
            // The result must be a valid shell whose walls cover no object-layer equipment or belt and overlap no other shell, and
            // every decor piece on the site must still have the wall, ceiling or floor it needs.
            var probe = new GoodsSnapshot
            {
                WorldId = state.WorldId, SiteLayouts = state.SiteLayouts, Equipment = state.Equipment, Belts = state.Belts,
                Buildings = state.Buildings.Select(x => x.Id == current.Id ? next : x).ToList()
            };
            var problem = BuildingProblem(probe, next);
            if (problem != null) return Fail(problem == "invalid-building" ? "out-of-bounds" : problem);
            if (state.Equipment.Any(x => x.SiteId == current.SiteId && x.State == EquipmentState.Placed && !string.IsNullOrEmpty(x.Layer)
                    && SiteGrid.PlacementProblem(probe, x, x.CellX, x.CellZ, x.Rotation, x.Level) != null))
                return Fail("blocked");
            return plan;
        }

        // Turns a rectangular restaurant into free walls (decision 0036): each perimeter cell becomes a wall record in the outer
        // finish, keeping what a paid perimeter wall cost; each perimeter door becomes a door record on its wall (an unpaid one
        // records nothing paid). Floor records mean nothing once the footprint follows the walls, so they are refunded by the
        // order that converts. Windows already stand on those walls.
        private static void ToFreeWalls(GoodsBuilding building, ShellPlan plan)
        {
            var style = string.IsNullOrEmpty(building.WallStyle) ? WallStyles[0] : building.WallStyle;
            var perimeter = new List<GoodsStructure>();
            for (var x = building.CellX; x < building.CellX + building.Width; x++)
            for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
            {
                if (!SiteGrid.OnPerimeter(building, x, z)) continue;
                var paid = building.Structures.FirstOrDefault(s => s.Kind == WallStructure && s.X == x && s.Z == z);
                perimeter.Add(new GoodsStructure { Kind = PartitionStructure, X = x, Z = z, Style = style, ChargedCents = paid?.ChargedCents ?? 0 });
            }
            foreach (var door in building.Doors)
                if (!building.Structures.Any(s => s.Kind == DoorStructure && s.X == door.X && s.Z == door.Z))
                    building.Structures.Add(new GoodsStructure { Kind = DoorStructure, X = door.X, Z = door.Z, Style = DoorStyles[0] });
            building.Structures.RemoveAll(s => s.Kind == WallStructure);
            foreach (var floor in building.Structures.Where(s => s.Kind == FloorStructure).ToList())
            {
                building.Structures.Remove(floor);
                plan.Removed.Add(floor);
            }
            building.Structures.AddRange(perimeter);
            building.Doors = new List<GridCell>();
            building.WallStyle = style;
            building.FreeWalls = true;
        }

        // A wall cell joined to walls along both axes (a corner or junction), where a doorway would cut a wall run off.
        private static bool IsCorner(GoodsBuilding building, int x, int z) =>
            (SiteGrid.IsPartition(building, x + 1, z) || SiteGrid.IsPartition(building, x - 1, z))
            && (SiteGrid.IsPartition(building, x, z + 1) || SiteGrid.IsPartition(building, x, z - 1));

        // A cell inside a footprint (walls included).
        private static bool Inside(GoodsBuilding building, int x, int z) =>
            SiteGrid.Overlaps(x, z, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth);

        // Where a perimeter cell of the old footprint lands when its side moves to the new footprint's side.
        private static (int X, int Z) Project(GoodsBuilding from, GoodsBuilding to, int x, int z)
        {
            if (z == from.CellZ) return (x, to.CellZ);
            if (z == from.CellZ + from.Depth - 1) return (x, to.CellZ + to.Depth - 1);
            if (x == from.CellX) return (to.CellX, z);
            return (to.CellX + to.Width - 1, z);
        }

        // Volatile primitive for tests. Live request handlers must call OrderShellDurably.
        // Checks, in order: identity and replay; the building and the requester's grant on its site (forbidden); a company that
        // owns the site (no-company); the plan (its problem); the company's cash for the net charge (insufficient-funds). Like a
        // purchase, only the accepted order is recorded, so a retried request replays it and never pays twice.
        public GoodsOutcome OrderShell(string playerId, string requestId, ShellOrder order)
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
                var building = order == null ? null : _state.Buildings.FirstOrDefault(x => x.Id == order.BuildingId);
                if (building is null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == building.SiteId)) return Reject("forbidden");
                var company = CompanyOfSiteLocked(building.SiteId);
                if (company is null) return Reject("no-company");
                var plan = PlanShell(_state, order, PricePercentLocked(building.SiteId));
                if (plan.Problem is not null) return Reject(plan.Problem);
                var net = plan.NetCents;
                if (net > 0 && _state.Companies.First(x => x.Id == company).Cash < net) return Reject("insufficient-funds");

                // All checks precede this single locked mutation: the new building and the net payment together.
                _state.Buildings[_state.Buildings.IndexOf(building)] = plan.Building;
                if (net > 0) TryDebit(company, net, new CashNote(LedgerShell, building.SiteId, requestId));
                else if (net < 0) TryCredit(company, -net, new CashNote(LedgerShell, building.SiteId, requestId));
                InvalidateDiners();
                return RecordCents(requestId, playerId, "shell-changed", net, null);
            }
        }

        public GoodsOutcome OrderShellDurably(string playerId, string requestId, ShellOrder order, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => OrderShell(playerId, requestId, order));
        }

        // An accepted outcome that also records the cents it moved and the equipment it created (if any).
        private GoodsOutcome RecordCents(string requestId, string playerId, string reason, long cents, string equipmentId)
        {
            var result = Record(requestId, playerId, true, reason, null);
            var stored = _state.Outcomes[_state.Outcomes.Count - 1];
            stored.Cents = result.Cents = cents;
            stored.EquipmentId = result.EquipmentId = equipmentId;
            return result;
        }
    }
}
