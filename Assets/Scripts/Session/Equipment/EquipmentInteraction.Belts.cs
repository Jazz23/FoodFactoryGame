// Local player's belt controls, Factorio-style. With belts on the cursor (a stack picked from the inventory, kept on the
// cursor after the screen closes), a ghost belt shows the cell under the crosshair in the cursor direction and the shape
// it would take. Pressing Place starts a drag: belts are laid along the cursor direction as the crosshair moves forward
// (never sideways or back), and every cell the crosshair skips is filled. Rotate while dragging with the crosshair off to
// the side of the line turns the belt at the head toward it, making a corner, and lays belts in the new direction up to
// and including the crosshair's cell (its projection on that line); the drag carries on from there. Rotate while dragging
// over any belt does nothing. Rotate may be held through the drag: the line then follows the crosshair round each turn.
// Rotate without a drag turns the cursor direction, or with no belts on the cursor the belt under the
// crosshair. Holding Remove takes up every belt the crosshair passes over. With any other goods on the cursor, aiming at a
// belt shows a ghost of the item where it would land and PlaceItem (Z) puts exactly one on the belt. TakeItem (F), whatever
// the cursor holds, takes the riding item nearest the crosshair off the aimed belt into the inventory.
// Each belt is its own request; pending ones are drawn as ghosts until the baseline shows them, and the server re-checks all.
// With lifts on the cursor (decision 0021), a ghost lift shows the crosshair's cell going up or down one floor; Place puts
// one there, R turns it, FlipLift (V) switches up and down. Belt drags never turn a lift; with an empty cursor R turns the
// aimed lift like a belt, and Remove takes it up.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Belts;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        // Longest a placed belt is drawn as a ghost after the server accepted it, waiting for the baseline that shows it.
        private const float PendingBeltSeconds = 3f;

        [SerializeField] private BeltPresenter belts;
        // Puts one unit of the cursor stack on the belt under the crosshair.
        [SerializeField] private InputActionReference placeItemAction;
        // Takes the riding item nearest the crosshair off the belt under it.
        [SerializeField] private InputActionReference takeItemAction;

        private sealed class PendingBelt
        {
            public string RequestId;
            public int Direction;
            public bool NewBelt;
            public float AcceptedAt = -1f;
        }

        private readonly Dictionary<(int X, int Z), PendingBelt> _pendingBelts = new();
        // Removal requests in flight (request ID -> belt ID), and belts already asked for during this Remove press (refused,
        // or accepted but not yet gone from the baseline), which are not asked for again until Remove is released.
        private readonly Dictionary<string, string> _removingBelts = new();
        private readonly HashSet<string> _refusedRemovals = new();
        private readonly Dictionary<BeltShape, List<(GameObject Root, Renderer[] Renderers)>> _beltGhosts = new();
        private bool _dragging;
        // Floor the pending belts (keyed by cell) were placed on; changing floors drops their ghosts and ends a drag.
        private int _beltLevel;
        private (int X, int Z) _dragHead;
        private (int X, int Z)? _aimCell;
        private GoodsBelt _aimBelt;
        private Vector3 _aimPoint;
        private SpriteRenderer _itemGhost;
        private string _itemGhostId;
        private Material _ghostTread;
        private readonly Dictionary<int, (GameObject Root, Renderer[] Renderers)> _liftGhosts = new();
        // Found in the Player map by name rather than serialized, so the authored scene needs no new reference.
        private InputAction _flipLiftAction;

        public bool BeltCursor => CursorGoods != null && CursorGoods.ItemId == GoodsWorld.BeltItemId && Screen == InteractionScreen.None;
        public bool LiftCursor => CursorGoods != null && CursorGoods.ItemId == GoodsWorld.LiftItemId && Screen == InteractionScreen.None;
        public bool ItemCursor => CursorGoods != null && CursorGoods.ItemId != GoodsWorld.BeltItemId && CursorGoods.ItemId != GoodsWorld.LiftItemId
            && Screen == InteractionScreen.None;
        // Way the next lift from the cursor goes: +1 up a floor, -1 down.
        public int LiftDirection { get; private set; } = 1;
        public bool LiftGhostVisible => _liftGhosts.Values.Any(x => x.Root.activeSelf);
        public bool Dragging => _dragging;
        public int PendingBeltCount => _pendingBelts.Count;
        public bool ItemGhostVisible => _itemGhost != null && _itemGhost.gameObject.activeSelf;
        public Vector3 ItemGhostPosition => _itemGhost == null ? default : _itemGhost.transform.position;
        public int BeltGhostCount => _beltGhosts.Values.Sum(x => x.Count(y => y.Root.activeSelf));
        // Cell and belt under the crosshair as of the last frame (tests and HUD).
        public (int X, int Z)? AimCell => _aimCell;
        public GoodsBelt AimBelt => _aimBelt;

        // Belt and item modes, run from Update while no screen is open. Returns false (with the belt ghosts hidden) when the
        // cursor carries no goods, so the machine controls take over.
        private bool UpdateBelts(GoodsSnapshot site, SiteLayout layout, string suffix)
        {
            if (_beltLevel != Level)
            {
                // Answers to requests still in flight find no ghost and are only reported.
                _beltLevel = Level;
                _pendingBelts.Clear();
                _dragging = false;
            }
            ExpirePendingBelts(site);
            var active = site != null && layout != null && _camera != null && Screen == InteractionScreen.None;
            _aimCell = null;
            _aimBelt = null;
            if (active) Aim(site, layout);
            if (!removeAction.action.IsPressed()) _refusedRemovals.Clear();
            else if (active && !_released) RemoveAimedBelt();
            if (!active || CursorGoods == null)
            {
                _dragging = false;
                HideBeltGhosts();
                HideLiftGhosts();
                ShowItemGhost(null, default);
                return false;
            }
            if (LiftCursor)
            {
                _dragging = false;
                ShowItemGhost(null, default);
                HideBeltGhosts();
                var problem = _aimCell == null ? null : LiftProblem(site, _aimCell.Value);
                ShowLiftGhost(layout, _aimCell, problem == null);
                var way = LiftDirection > 0 ? "up" : "down";
                var flip = _flipLiftAction?.GetBindingDisplayString() ?? "FlipLift";
                Status = $"Lifts ({LiftsCarried(site)}, {way}): click places a lift carrying items {way} a floor to the belt in front, "
                    + $"{flip} flips up/down, R turns, right click removes, Q clears" + (problem == null ? "" : $" [{problem}]") + suffix;
                return true;
            }
            HideLiftGhosts();
            if (BeltCursor)
            {
                ShowItemGhost(null, default);
                UpdateBeltBuild(site, layout);
                var held = BeltsCarried(site);
                Status = $"Belts ({held}): drag to lay a line, R turns (while dragging: a corner out to the crosshair), right click removes, F takes an item off, Q clears"
                    + (held == 0 ? " [no-belts]" : "") + suffix;
                return true;
            }
            _dragging = false;
            HideBeltGhosts();
            UpdateItemGhost(site, layout);
            var name = CursorGoods.ItemId;
            Status = _aimBelt == null
                ? $"Cursor: {name}: aim at a belt, Z puts one on it; Q clears" + suffix
                : $"Cursor: {name}: Z puts one on this belt, F takes one off" + (BeltRules.FreePosition(RidingPositions(site, _aimBelt)) < 0 ? " [belt-full]" : "") + suffix;
            return true;
        }

        // The crosshair names a belt by its collider (belt tops stand 0.8 m above the floor); otherwise the floor cell.
        private void Aim(GoodsSnapshot site, SiteLayout layout)
        {
            var ray = AimRay();
            var hit = Physics.RaycastAll(ray, maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                .Where(x => x.collider.GetComponentInParent<PlayerAvatar>() == null)
                .OrderBy(x => x.distance).FirstOrDefault();
            var visual = hit.collider == null ? null : hit.collider.GetComponentInParent<BeltVisual>();
            _aimPoint = hit.point;
            _aimBelt = visual == null ? null : site.Belts.FirstOrDefault(x => x.Id == visual.BeltId);
            if (_aimBelt != null) _aimCell = (_aimBelt.CellX, _aimBelt.CellZ);
            else if (TryFloorPoint(out var point))
            {
                _aimPoint = point;
                _aimCell = BeltPath.CellAt(layout, point);
                _aimBelt = BeltPresenter.Touching(site.Belts, Level).FirstOrDefault(x => x.CellX == _aimCell.Value.X && x.CellZ == _aimCell.Value.Z);
            }
        }

        private void UpdateBeltBuild(GoodsSnapshot site, SiteLayout layout)
        {
            if (_dragging && !placeAction.action.IsPressed()) _dragging = false;
            if (_dragging && _aimCell != null)
            {
                // Lay belts forward along the drag direction up to the crosshair's projection on that line.
                var (fx, fz) = BeltRules.Step(_rotation);
                LayForward(site, layout, (_aimCell.Value.X - _dragHead.X) * fx + (_aimCell.Value.Z - _dragHead.Z) * fz);
                // Holding Rotate keeps turning: each time the crosshair leaves the line, the line follows it.
                if (rotateAction.action.IsPressed()) TurnDragTowardAim(site, layout);
            }

            // Ghosts: every pending belt, plus the hovered cell while not dragging, each with the shape it would take.
            var planned = PlannedBelts(site);
            var shown = new List<(BeltShape Shape, (int X, int Z) Cell, int Direction, bool Valid)>();
            foreach (var pair in _pendingBelts)
                shown.Add((belts.ShapeAt(planned, pair.Key.X, pair.Key.Z, _beltLevel, pair.Value.Direction), pair.Key, pair.Value.Direction, true));
            if (!_dragging && _aimCell != null && !_pendingBelts.ContainsKey(_aimCell.Value))
            {
                var cell = _aimCell.Value;
                var hypothetical = planned.Where(x => x.CellX != cell.X || x.CellZ != cell.Z)
                    .Append(new GoodsBelt { CellX = cell.X, CellZ = cell.Z, Direction = _rotation, Level = _beltLevel }).ToList();
                shown.Add((belts.ShapeAt(hypothetical, cell.X, cell.Z, _beltLevel, _rotation), cell, _rotation, BeltProblem(site, cell) == null));
            }
            ShowBeltGhosts(layout, shown);
        }

        // Moves the drag head up to count cells along the cursor direction, placing a belt in each; stops at the layout edge.
        private void LayForward(GoodsSnapshot site, SiteLayout layout, int count)
        {
            var (fx, fz) = BeltRules.Step(_rotation);
            for (var step = 1; step <= count; step++)
            {
                var cell = (_dragHead.X + fx, _dragHead.Z + fz);
                if (!InLayout(layout, cell)) break;
                _dragHead = cell;
                TryPlaceBelt(site, cell, _rotation);
            }
        }

        // The flat belt at a cell of the local avatar's floor: the only kind a belt drag places over or turns.
        private GoodsBelt FlatBeltAt(GoodsSnapshot site, (int X, int Z) cell) =>
            BeltPresenter.OnLevel(site.Belts, Level).FirstOrDefault(x => x.Lift == 0 && x.CellX == cell.X && x.CellZ == cell.Z);

        // Existing belts and lifts standing on this floor with pending placements and turns applied, for ghost shapes.
        private List<GoodsBelt> PlannedBelts(GoodsSnapshot site)
        {
            var planned = BeltPresenter.Touching(site.Belts, _beltLevel).Where(x => x.Lift != 0 || !_pendingBelts.ContainsKey((x.CellX, x.CellZ)))
                .Select(x => new GoodsBelt { Id = x.Id, CellX = x.CellX, CellZ = x.CellZ, Direction = x.Direction, Level = x.Level, Lift = x.Lift })
                .ToList();
            planned.AddRange(_pendingBelts.Select(x => new GoodsBelt
            {
                CellX = x.Key.X, CellZ = x.Key.Z, Direction = x.Value.Direction, Level = _beltLevel
            }));
            return planned;
        }

        private static bool InLayout(SiteLayout layout, (int X, int Z) cell) =>
            cell.X >= 0 && cell.Z >= 0 && cell.X < layout.Width && cell.Z < layout.Depth;

        // Client preview of the server's belt rule: null when a belt may go (or an existing one turn) there.
        private string BeltProblem(GoodsSnapshot site, (int X, int Z) cell)
        {
            if (_pendingBelts.ContainsKey(cell)) return null;
            if (FlatBeltAt(site, cell) != null) return null;
            var problem = SiteGrid.CellProblem(site, DevWorld.SiteId, cell.X, cell.Z, 1, 1, null, Level);
            if (problem != null) return problem;
            return BeltsCarried(site) - _pendingBelts.Count(x => x.Value.NewBelt) > 0 ? null : "no-belts";
        }

        public int BeltsCarried(GoodsSnapshot site) => site == null || InventoryId == null ? 0
            : site.Lots.Where(x => x.LocationId == InventoryId && x.ItemId == GoodsWorld.BeltItemId).Sum(x => x.Quantity);

        // Sends one belt placement (or turn) unless the cell already shows that belt; the server re-checks everything.
        private void TryPlaceBelt(GoodsSnapshot site, (int X, int Z) cell, int direction)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null) return;
            var existing = FlatBeltAt(site, cell);
            if (_pendingBelts.TryGetValue(cell, out var pending) ? pending.Direction == direction : existing?.Direction == direction) return;
            var problem = BeltProblem(site, cell);
            if (problem != null)
            {
                LastRejection = problem;
                return;
            }
            var requestId = Track();
            _pendingBelts[cell] = new PendingBelt { RequestId = requestId, Direction = direction, NewBelt = existing == null && pending?.NewBelt != false };
            bridge.RequestPlaceBelt(requestId, DevWorld.SiteId, cell.X, cell.Z, direction, _beltLevel);
        }

        // Drops pending belts the baseline now shows, and accepted ones whose baseline never came.
        private void ExpirePendingBelts(GoodsSnapshot site)
        {
            if (_pendingBelts.Count == 0) return;
            foreach (var pair in _pendingBelts.ToList())
            {
                var shown = site?.Belts.Any(x => x.CellX == pair.Key.X && x.CellZ == pair.Key.Z && x.Level == _beltLevel
                    && x.Direction == pair.Value.Direction) == true;
                var stale = pair.Value.AcceptedAt >= 0f && Time.unscaledTime - pair.Value.AcceptedAt > PendingBeltSeconds;
                if ((shown && pair.Value.AcceptedAt >= 0f) || stale || site == null) _pendingBelts.Remove(pair.Key);
            }
            if (site == null) _removingBelts.Clear();
        }

        private void OnBeltResult(GoodsOutcome outcome)
        {
            if (_removingBelts.Remove(outcome.RequestId, out var removed)) _refusedRemovals.Add(removed);
            var pair = _pendingBelts.FirstOrDefault(x => x.Value.RequestId == outcome.RequestId);
            if (pair.Value == null) return;
            if (outcome.Accepted) pair.Value.AcceptedAt = Time.unscaledTime;
            else _pendingBelts.Remove(pair.Key);
        }

        private bool StartBeltDrag()
        {
            if (LiftCursor && _aimCell != null && session.ClientSite != null)
            {
                PlaceLift(session.ClientSite, _aimCell.Value);
                return true;
            }
            if (!BeltCursor || _aimCell == null || session.ClientSite == null) return false;
            _dragging = true;
            _dragHead = _aimCell.Value;
            TryPlaceBelt(session.ClientSite, _dragHead, _rotation);
            return true;
        }

        // Rotate: while dragging with the crosshair beside the line (not over a belt), turn the line's head toward it (a
        // corner) and lay belts that way through the crosshair's cell; with belts on the cursor, turn the cursor; with nothing
        // on the cursor, turn the belt under the crosshair.
        private bool RotateBelts()
        {
            var site = session.ClientSite;
            if (site == null || Screen != InteractionScreen.None) return false;
            if (BeltCursor && _dragging)
            {
                // Consumed either way, so a refused turn never spins the cursor mid-drag.
                if (belts.Layout != null) TurnDragTowardAim(site, belts.Layout);
                return true;
            }
            if (BeltCursor || LiftCursor)
            {
                _rotation = BeltRules.RightOf(_rotation);
                return true;
            }
            if (_held == null && _aimBelt != null && _aimBelt.Lift != 0)
            {
                // A lift turns in place, whichever end is aimed at; the server finds it by the floor it takes items on.
                _subscription?.Bridge?.RequestPlaceLift(Track(), DevWorld.SiteId, _aimBelt.CellX, _aimBelt.CellZ,
                    BeltRules.RightOf(_aimBelt.Direction), _aimBelt.Level, _aimBelt.Lift);
                return true;
            }
            if (_held == null && _aimBelt != null)
            {
                TryPlaceBelt(site, (_aimBelt.CellX, _aimBelt.CellZ), BeltRules.RightOf(_aimBelt.Direction));
                return true;
            }
            return false;
        }

        // With the crosshair beside the drag line and not over a belt, corners the head toward it and lays belts that way
        // through the crosshair's cell (its projection on the new line). Nothing happens over a belt or on the line.
        private void TurnDragTowardAim(GoodsSnapshot site, SiteLayout layout)
        {
            if (_aimCell == null || _aimBelt != null || _pendingBelts.ContainsKey(_aimCell.Value)) return;
            var (rx, rz) = BeltRules.Step(BeltRules.RightOf(_rotation));
            var side = (_aimCell.Value.X - _dragHead.X) * rx + (_aimCell.Value.Z - _dragHead.Z) * rz;
            if (side == 0) return;
            _rotation = side < 0 ? BeltRules.LeftOf(_rotation) : BeltRules.RightOf(_rotation);
            TryPlaceBelt(site, _dragHead, _rotation);
            LayForward(site, layout, Math.Abs(side));
        }

        private void RemoveAimedBelt()
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || _aimBelt == null || _removingBelts.ContainsValue(_aimBelt.Id) || _refusedRemovals.Contains(_aimBelt.Id)
                || _pendingBelts.ContainsKey((_aimBelt.CellX, _aimBelt.CellZ))) return;
            var requestId = Track();
            _removingBelts[requestId] = _aimBelt.Id;
            bridge.RequestRemoveBelt(requestId, _aimBelt.Id);
        }

        private void OnPlaceItem(InputAction.CallbackContext _) => PlaceItemOnBelt();

        private void OnTakeItem(InputAction.CallbackContext _) => TakeItemFromBelt();

        // Takes the riding item nearest the crosshair on the aimed belt (by its latest replicated position) into the inventory.
        public void TakeItemFromBelt()
        {
            var site = session.ClientSite;
            var bridge = _subscription?.Bridge;
            var layout = belts.Layout;
            if (site == null || bridge == null || layout == null || _released || Screen != InteractionScreen.None) return;
            if (_aimBelt == null)
            {
                LastRejection = "aim at a belt";
                return;
            }
            var shape = BeltRules.Shape(BeltRules.ByCell(site.Belts), _aimBelt);
            var nearest = site.Lots.Where(x => x.LocationId == _aimBelt.LocationId)
                .OrderBy(x => Vector3.ProjectOnPlane(BeltPath.WorldPoint(layout, _aimBelt, shape, x.BeltPosition) - _aimPoint, Vector3.up).sqrMagnitude)
                .FirstOrDefault();
            if (nearest == null)
            {
                LastRejection = "belt-empty";
                return;
            }
            bridge.RequestTakeFromBelt(Track(), nearest.Id);
        }

        // Puts exactly one unit of the cursor stack (its most exposed lot first) on the belt under the crosshair.
        public void PlaceItemOnBelt()
        {
            var site = session.ClientSite;
            var bridge = _subscription?.Bridge;
            if (!ItemCursor || site == null || bridge == null || _released) return;
            if (_aimBelt == null)
            {
                LastRejection = "aim at a belt";
                return;
            }
            var lot = CursorLots(site).OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (lot == null) return;
            if (BeltRules.FreePosition(RidingPositions(site, _aimBelt)) < 0)
            {
                LastRejection = "belt-full";
                return;
            }
            bridge.RequestPlaceOnBelt(Track(), lot.Id, _aimBelt.Id);
        }

        private static IEnumerable<int> RidingPositions(GoodsSnapshot site, GoodsBelt belt) =>
            site.Lots.Where(x => x.LocationId == belt.LocationId).Select(x => x.BeltPosition);

        // A see-through icon of the cursor item where the server would put it on the aimed belt (its free spot nearest the
        // middle), red when the belt has no room.
        private void UpdateItemGhost(GoodsSnapshot site, SiteLayout layout)
        {
            if (_aimBelt == null)
            {
                ShowItemGhost(null, default);
                return;
            }
            var position = BeltRules.FreePosition(RidingPositions(site, _aimBelt));
            var shape = BeltRules.Shape(BeltRules.ByCell(site.Belts), _aimBelt);
            var point = BeltPath.WorldPoint(layout, _aimBelt, shape, position < 0 ? BeltRules.Middle : position)
                        + Vector3.up * (belts.ItemSize * 0.5f);
            ShowItemGhost(CursorGoods.ItemId, point);
            var color = position < 0 ? invalidColor : Color.white;
            color.a = 0.55f;
            _itemGhost.color = color;
        }

        private void ShowItemGhost(string itemId, Vector3 position)
        {
            if (itemId == null)
            {
                if (_itemGhost != null) _itemGhost.gameObject.SetActive(false);
                return;
            }
            if (_itemGhost == null)
            {
                var ghostObject = new GameObject("Item ghost");
                ghostObject.transform.SetParent(transform, false);
                _itemGhost = ghostObject.AddComponent<SpriteRenderer>();
            }
            if (_itemGhostId != itemId)
            {
                _itemGhostId = itemId;
                var item = session.Items.FirstOrDefault(x => x != null && x.Id == itemId);
                BeltPresenter.ConfigureItemSprite(_itemGhost, item?.Icon, belts.ItemMaterial, belts.ItemSize);
            }
            _itemGhost.gameObject.SetActive(true);
            _itemGhost.transform.position = position;
            if (_camera != null) _itemGhost.transform.rotation = _camera.transform.rotation;
        }

        private void ShowBeltGhosts(SiteLayout layout, List<(BeltShape Shape, (int X, int Z) Cell, int Direction, bool Valid)> shown)
        {
            var used = new Dictionary<BeltShape, int>();
            foreach (var (shape, cell, direction, valid) in shown)
            {
                used.TryGetValue(shape, out var index);
                used[shape] = index + 1;
                var ghost = BeltGhost(shape, index);
                ghost.Root.SetActive(true);
                ghost.Root.transform.SetPositionAndRotation(BeltPath.CellCenter(layout, cell.X, cell.Z, _beltLevel), BeltPath.ModelRotation(shape, direction));
                var color = valid ? validColor : invalidColor;
                color.a = ghostAlpha;
                foreach (var renderer in ghost.Renderers)
                {
                    renderer.GetPropertyBlock(_block);
                    _block.SetColor(BaseColor, color);
                    renderer.SetPropertyBlock(_block);
                }
            }
            foreach (var pair in _beltGhosts)
            {
                used.TryGetValue(pair.Key, out var count);
                for (var index = count; index < pair.Value.Count; index++) pair.Value[index].Root.SetActive(false);
            }
        }

        private (GameObject Root, Renderer[] Renderers) BeltGhost(BeltShape shape, int index)
        {
            if (!_beltGhosts.TryGetValue(shape, out var pool)) _beltGhosts[shape] = pool = new List<(GameObject, Renderer[])>();
            while (pool.Count <= index)
            {
                var root = EquipmentModel.CreateGhost(belts.PrefabFor(shape), $"Belt ghost {shape}", transform, ghostModelMaterial, false,
                    belts.IsTread, GhostTread());
                pool.Add((root, root.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray()));
            }
            return pool[index];
        }

        // Client preview of the server's lift rule at a cell for the cursor's direction: null when a lift may go (or the lift
        // already there turn) there.
        private string LiftProblem(GoodsSnapshot site, (int X, int Z) cell)
        {
            if (site.Belts.Any(x => x.Lift == LiftDirection && x.Level == Level && x.CellX == cell.X && x.CellZ == cell.Z)) return null;
            var problem = SiteGrid.CellProblem(site, DevWorld.SiteId, cell.X, cell.Z, 1, 1, null, Level)
                ?? SiteGrid.CellProblem(site, DevWorld.SiteId, cell.X, cell.Z, 1, 1, null, Level + LiftDirection);
            if (problem != null) return problem;
            return LiftsCarried(site) > 0 ? null : "no-lifts";
        }

        public int LiftsCarried(GoodsSnapshot site) => site == null || InventoryId == null ? 0
            : site.Lots.Where(x => x.LocationId == InventoryId && x.ItemId == GoodsWorld.LiftItemId).Sum(x => x.Quantity);

        // Sends one lift placement at the crosshair's cell in the cursor direction; the server re-checks everything.
        public void PlaceLift(GoodsSnapshot site, (int X, int Z) cell)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null) return;
            var problem = LiftProblem(site, cell);
            if (problem != null)
            {
                LastRejection = problem;
                return;
            }
            bridge.RequestPlaceLift(Track(), DevWorld.SiteId, cell.X, cell.Z, _rotation, Level, LiftDirection);
        }

        // Switches the cursor's lifts between going up and going down a floor.
        public void FlipLift() => LiftDirection = -LiftDirection;

        private void OnFlipLift(InputAction.CallbackContext _)
        {
            if (LiftCursor) FlipLift();
        }

        private void EnableLiftInput()
        {
            _flipLiftAction ??= rotateAction.action.actionMap?.FindAction("FlipLift");
            if (_flipLiftAction == null) return;
            _flipLiftAction.performed += OnFlipLift;
            _flipLiftAction.Enable();
        }

        private void DisableLiftInput()
        {
            if (_flipLiftAction == null) return;
            _flipLiftAction.performed -= OnFlipLift;
            _flipLiftAction.Disable();
        }

        private void ShowLiftGhost(SiteLayout layout, (int X, int Z)? cell, bool valid)
        {
            HideLiftGhosts();
            if (cell == null) return;
            if (!_liftGhosts.TryGetValue(LiftDirection, out var ghost))
            {
                var root = EquipmentModel.CreateGhost(belts.LiftModel(LiftDirection), $"Lift ghost {LiftDirection}", transform, ghostModelMaterial, false,
                    belts.IsTread, GhostTread());
                _liftGhosts[LiftDirection] = ghost = (root, root.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray());
            }
            ghost.Root.SetActive(true);
            ghost.Root.transform.SetPositionAndRotation(BeltPath.CellCenter(layout, cell.Value.X, cell.Value.Z, Level), SiteGridSpace.Rotation(_rotation));
            var color = valid ? validColor : invalidColor;
            color.a = ghostAlpha;
            foreach (var renderer in ghost.Renderers)
            {
                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(_block);
            }
        }

        private void HideLiftGhosts()
        {
            foreach (var ghost in _liftGhosts.Values) ghost.Root.SetActive(false);
        }

        // The belt surface keeps its arrows in belt and lift ghosts, so the direction reads at a glance.
        private Material GhostTread()
        {
            if (_ghostTread == null)
            {
                _ghostTread = new Material(ghostModelMaterial) { name = "Belt ghost tread" };
                _ghostTread.SetTexture("_BaseMap", belts.Tread.GetTexture("_BaseMap"));
            }
            return _ghostTread;
        }

        private void HideBeltGhosts()
        {
            foreach (var pool in _beltGhosts.Values)
                foreach (var ghost in pool)
                    ghost.Root.SetActive(false);
        }

        private void DestroyBeltGhosts()
        {
            foreach (var pool in _beltGhosts.Values)
                foreach (var ghost in pool)
                    if (ghost.Root != null) Destroy(ghost.Root);
            _beltGhosts.Clear();
            foreach (var ghost in _liftGhosts.Values)
                if (ghost.Root != null) Destroy(ghost.Root);
            _liftGhosts.Clear();
            if (_itemGhost != null) Destroy(_itemGhost.gameObject);
            _itemGhost = null;
            _itemGhostId = null;
            if (_ghostTread != null) Destroy(_ghostTread);
            _ghostTread = null;
        }
    }
}
