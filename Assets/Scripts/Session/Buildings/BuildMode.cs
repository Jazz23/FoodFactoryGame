// Restaurant build mode (decision 0034, slice 2), local player only. Build (B) opens it as a screen of its own: the camera looks
// down on the current site, a grid covers the lot and a panel on the right offers the Sell tool and one catalog (BuildCatalog,
// owner 2026-10-14) of every wall, door, back door (decision 0037), window and wall finish style beside the furnishings and
// machines, filtered by category chips (All by default), ending with Belts (owner, 2026-10-13): a belt card
// leaves build mode with belts on the cursor to lay a line (EquipmentInteraction.LayBelts), buying a pack when none are
// held. Walls are free (decision 0036): outer and inner walls are drawn and removed alike, anywhere in the lot. The pointer's cell shows a ghost tinted by validity: the same pure rules the server applies
// (GoodsWorld.PlanShell, FurnishProblem, SiteGrid) plus the company's cash, with the price, refund and net amount, or the reason it
// would be refused. A click places a single piece, door or window; dragging draws a line of wall, restyles a line of walls or
// fills an area with a piece, ordered as soon as the button is released (owner feedback 0038: no Confirm); Esc during a drag drops
// it. Holding Remove (right mouse) still for HoldSeconds, while a wheel at the pointer fills, sells the piece the pointer is on (the
// piece's own colliders, so wall and ceiling decor is aimed at directly) or, with no piece there, removes the door, window or wall
// the pointer is on (any part of it, not just its base), or sells a piece standing on the floor cell the pointer is on, for its
// recorded refund; a press on nothing removable shows no wheel; moving the pointer while holding tilts the camera
// instead (OrbitCameraRig), Move pans it (faster with Sprint) and Zoom zooms it. Once a hold has removed something, keeping Remove
// held sweeps (owner, 2026-10-14): each further thing of the same family (placed pieces, or doors, windows and walls) the pointer
// moves onto is removed at once, each only once, while the camera's tilt is held still; nothing the preview refuses is sent and
// the server still validates every removal and its refund. Release ends the sweep. The panel never takes keyboard focus, so
// Navigate (the UI map shares WASD and the arrows with Move) never scrolls the catalog while the camera moves.
// Cancel and ClearCursor (X) clear the selected
// tool or item. Wall decor turns to face away from its wall and a backed piece (a sink) turns its back to a wall when one is
// behind either way round. Every order is a request; nothing changes here until the next replicated baseline, and the server's
// reason is shown when it refuses. Controls come from the Player action map (Input System). Leaving build mode remembers its
// camera (focus, distance, tilt and turn) and the next opening on the same site returns to it, unless the avatar has since
// moved more than RememberedViewReach from that focus, which frames the lot again. Pick (PickBlock, middle mouse; owner,
// 2026-10-15) copies what the pointer is on into the cursor, as Minecraft's pick block does: a placed piece selects its offer
// (and its rotation), a wall, door, back door or window selects that tool in its style. Picking only selects; nothing is ordered.
// Build mode is the only way to remove things (owner, 2026-10-15): Remove works on every site the player builds on (restaurant
// and factory) and every floor shown. A placed piece with a recorded charge is sold for that refund; one without (a machine
// placed from the inventory) is picked up into the inventory instead, so nothing is deleted for nothing; a belt or conveyor lift
// is taken up into the inventory. These are the same server-validated requests (Sell, PickUp, RemoveBelt) the world cursor sent.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Belts;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Buildings
{
    public enum BuildTool
    {
        // Nothing selected: clicks do nothing; a right click still sells or removes.
        None,
        Sell,
        Wall,
        Door,
        // A back door (decision 0037): a door on an outer wall for staff and goods only.
        BackDoor,
        Window,
        Finish,
        Item
    }

    // What the cell or drag under the pointer would do; Problem is null when the server should accept it.
    public sealed class BuildPreview
    {
        public string Problem;
        public long ChargeCents;
        public long RefundCents;
        public string Label = "";
        public readonly List<(int X, int Z)> Cells = new();
        public bool Removal;
        public long NetCents => ChargeCents - RefundCents;
    }

    [DisallowMultipleComponent]
    public sealed class BuildMode : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private const int MaxHighlights = 400;
        // A right press that moves the pointer further than this (pixels) tilts the camera instead of removing.
        private const float ClickSlop = 8f;
        // How long Remove is held still to sell or remove what the pointer is on.
        public const float HoldSeconds = 0.75f;
        private const float WheelSize = 34f;
        // Farthest (metres across the ground) the avatar may be from the remembered build focus for build mode to reopen there.
        public const float RememberedViewReach = 20f;
        // The Player map action that picks what the pointer is on; looked up in the same map as Place, so binding overrides apply.
        public const string PickActionName = "PickBlock";

        [SerializeField] private SessionRoot session;
        [SerializeField] private EquipmentInteraction interaction;
        [SerializeField] private BuildingPresenter buildings;
        [SerializeField] private UIDocument document;
        // Flat cell tiles (grid lines and highlights) and the see-through model ghost.
        [SerializeField] private Material cellMaterial;
        [SerializeField] private Material ghostModelMaterial;
        [SerializeField] private InputActionReference buildAction;
        [SerializeField] private InputActionReference placeAction;
        [SerializeField] private InputActionReference removeAction;
        [SerializeField] private InputActionReference rotateAction;
        [SerializeField] private InputActionReference pointAction;
        [SerializeField] private Color validColor = new(0.2f, 0.85f, 0.3f, 0.55f);
        [SerializeField] private Color invalidColor = new(0.9f, 0.2f, 0.15f, 0.55f);
        [SerializeField] private Color removeColor = new(1f, 0.6f, 0.15f, 0.55f);
        [SerializeField] private Color gridColor = new(1f, 1f, 1f, 0.4f);

        private readonly Dictionary<string, string> _pendingRequests = new();
        private readonly List<Renderer> _highlights = new();
        private ClientSiteSubscription _subscription;
        private MaterialPropertyBlock _block;
        private GameObject _grid;
        private string _gridFor;
        private GameObject _ghost;
        private string _ghostKind;
        private Renderer[] _ghostRenderers = Array.Empty<Renderer>();
        private (int X, int Z)? _dragStart;
        private (int X, int Z)? _hover;
        private Vector2? _removePress;
        private float _removeStart;
        // The placed piece on the current site the pointer is on (its colliders), or null.
        private string _pointed;
        // The belt or lift the pointer is on (its collider, before anything solid), or null.
        private string _pointedBelt;
        // The cell a removal aims at: the cell of the solid surface under the pointer (a wall's side or top, a door, the floor),
        // so a wall is removed by pointing anywhere on it; the floor cell under the pointer when nothing solid is hit.
        private (int X, int Z)? _removeCell;
        private VisualElement _holdWheel;
        private bool _wasActive;
        private VisualElement _window;
        private Label _cash;
        private Label _ambience;
        private Label _preview;
        private Label _status;
        private VisualElement _tools;
        private VisualElement _filters;
        private VisualElement _catalog;
        private List<BuildCatalogEntry> _entries = new();
        // What the catalog was last filled from (offer count, price percentage, filter); null forces a refill.
        private string _catalogKey;
        private string _shownTools;
        // The drag-to-remove sweep after a completed hold; null when none.
        private RemovalSweep _sweep;
        // PickBlock from the Place action's map; null when the asset lacks it (picking is then unavailable).
        private InputAction _pickAction;

        // What one sweep has removed: the family it started on, the things sent (each once) and, for structure, their cells (a
        // removed window's or door's wall is not taken next). Last is the thing under the pointer on the previous frame.
        private sealed class RemovalSweep
        {
            public bool Structure;
            public readonly HashSet<string> Sent = new();
            public readonly HashSet<(int X, int Z)> Cells = new();
            public string Last;
            public Vector2 Pointer;
        }
        // Removal orders other than a sale (an equipment ID string) or a ShellOrder.
        private sealed class PickUpOrder
        {
            public readonly string EquipmentId;
            public PickUpOrder(string equipmentId) => EquipmentId = equipmentId;
        }

        private sealed class BeltRemoval
        {
            public readonly string BeltId;
            public BeltRemoval(string beltId) => BeltId = beltId;
        }

        // The build camera when build mode was last left (this client's presentation only, never saved or sent).
        private (string SiteId, Vector3 Focus, float Distance, float Yaw, float Pitch)? _rememberedView;

        public bool Active => interaction != null && interaction.Screen == InteractionScreen.Build;
        public BuildTool Tool { get; private set; } = BuildTool.None;
        public string Style { get; private set; } = "";
        public string OfferId { get; private set; }
        public int Rotation { get; private set; }
        public BuildPreview Preview { get; private set; } = new();
        // The catalog filter chip shown chosen; All lists every card.
        public BuildCatalogFilter Filter { get; private set; } = BuildCatalogFilter.All;
        // The catalog cards for the current offers, in display order (All of them, whatever the filter).
        public IReadOnlyList<BuildCatalogEntry> CatalogEntries => _entries;
        // True while Remove stays held after a hold removed something, so moving onto more things removes them too.
        public bool Sweeping => _sweep != null;
        // 0 to 1 while Remove is held still over the lot; the sale or removal is ordered at 1.
        public float RemoveProgress => _removePress.HasValue ? Mathf.Clamp01((Time.unscaledTime - _removeStart) / HoldSeconds) : 0f;
        public string PointedPieceId => _pointed;
        public (int X, int Z)? PointedCell => _removeCell;
        public string LastRejection { get; private set; }
        public bool HasPendingRequests => _pendingRequests.Count > 0;
        // Tests and tools aim at a cell directly; null uses the pointer.
        public (int X, int Z)? ForcedHover { get; set; }
        public VisualElement Window => _window;
        public GameObject Ghost => _ghost;
        public int HighlightCount => _highlights.Count(x => x.gameObject.activeSelf);

        private SiteLayout Layout => session.ClientSite?.SiteLayouts.FirstOrDefault(x => x.SiteId == session.ClientSiteId);
        private GoodsBuilding Restaurant => session.ClientSite?.Buildings.FirstOrDefault(x => x.SiteId == session.ClientSiteId && x.Kind == GoodsWorld.RestaurantKind);

        private void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            buildAction.action.performed += OnBuild;
            buildAction.action.Enable();
            _pickAction = placeAction.action.actionMap?.FindAction(PickActionName);
            _pickAction?.Enable();
            interaction.BuildEscape = CancelPending;
            interaction.BuildClear = ClearSelection;
        }

        private void OnDisable()
        {
            buildAction.action.performed -= OnBuild;
            buildAction.action.Disable();
            _pickAction?.Disable();
            if (interaction != null && interaction.BuildEscape == CancelPending) interaction.BuildEscape = null;
            if (interaction != null && interaction.BuildClear == ClearSelection) interaction.BuildClear = null;
            Subscribe(null);
            ShowWorld(false);
        }

        private void Start() => BuildPanel();

        private void OnBuild(InputAction.CallbackContext _) => Toggle();

        // Opens build mode on the current site, or leaves it.
        public void Toggle()
        {
            if (Active) interaction.CloseScreen();
            else interaction.OpenBuild();
        }

        public void SelectTool(BuildTool tool, string style = null)
        {
            Tool = tool;
            Style = style ?? DefaultStyle(tool);
            if (tool != BuildTool.Item) OfferId = null;
            CancelPending();
            _shownTools = null;
        }

        public void SelectOffer(string offerId)
        {
            Tool = BuildTool.Item;
            OfferId = offerId;
            Rotation = 0;
            CancelPending();
            _shownTools = null;
        }

        public void Rotate() => Rotation = (Rotation + 1) % 4;

        // Shows only the catalog cards of one category (All shows every card).
        public void SelectFilter(BuildCatalogFilter filter)
        {
            Filter = filter;
            _catalogKey = null;
        }

        // Cancel and ClearCursor (X): drops a drag in progress and the chosen tool or item, so the pointer carries nothing.
        public void ClearSelection()
        {
            Tool = BuildTool.None;
            OfferId = null;
            Style = "";
            CancelPending();
            _shownTools = null;
        }

        public static string DefaultStyle(BuildTool tool) => tool switch
        {
            BuildTool.Wall or BuildTool.Finish => GoodsWorld.WallStyles[0],
            BuildTool.Door => GoodsWorld.DoorStyles[0],
            BuildTool.BackDoor => GoodsWorld.BackDoorStyle,
            BuildTool.Window => GoodsWorld.WindowStyles[0],
            _ => ""
        };

        private void Update()
        {
            Subscribe(session.ClientSubscription);
            var active = Active && Layout != null;
            if (active != _wasActive)
            {
                _wasActive = active;
                OnActiveChanged(active);
            }
            if (_window != null) _window.Show(active);
            if (!active)
            {
                _removePress = null;
                _sweep = null;
                ShowWheel();
                ShowWorld(false);
                return;
            }
            var layout = Layout;
            EnsureGrid(layout);
            ReleasePanelFocus();
            if (rotateAction.action.WasPressedThisFrame()) Rotate();
            _hover = ForcedHover ?? PointerCell(layout);
            var overPanel = PointerOverPanel();
            var pointer = pointAction.action.ReadValue<Vector2>();
            (int X, int Z)? surface = null;
            _pointedBelt = null;
            _pointed = !overPanel && ForcedHover == null ? PointedPiece(pointer, layout, out surface, out _pointedBelt) : null;
            _removeCell = surface ?? _hover;
            // What a hold would sell or remove now; with nothing there, a right press starts no hold and shows no wheel.
            var removal = RemovalPlan(_removeCell, _pointed, out var removalOrder, _pointedBelt);
            if (!overPanel && ForcedHover == null)
            {
                if (placeAction.action.WasPressedThisFrame() && _hover.HasValue && interaction.ScreenClicksArmed)
                    PressAt(Tool == BuildTool.Sell ? _removeCell ?? _hover.Value : _hover.Value);
                if (placeAction.action.WasReleasedThisFrame() && _dragStart.HasValue) ReleaseAt(_hover ?? _dragStart.Value);
                if (_pickAction != null && _pickAction.WasPressedThisFrame()) PickAt(_removeCell, _pointed);
                if (removeAction.action.WasPressedThisFrame() && removalOrder != null)
                {
                    _removePress = pointer;
                    _removeStart = Time.unscaledTime;
                }
            }
            // Remove acts once held still for HoldSeconds; releasing early does nothing, and moving tilts the camera instead.
            if (_removePress.HasValue && (!removeAction.action.IsPressed() || overPanel || removalOrder == null
                    || (pointer - _removePress.Value).magnitude > ClickSlop))
                _removePress = null;
            if (_sweep != null && !removeAction.action.IsPressed()) _sweep = null;
            if (_removePress.HasValue && RemoveProgress >= 1f)
            {
                _removePress = null;
                // The hold's removal, which starts a sweep of the same family while Remove stays held.
                if (removalOrder != null && removal.Problem == null)
                {
                    var key = RemovalKey(removalOrder, out var structure, out var cells);
                    _sweep = new RemovalSweep { Structure = structure, Last = key, Pointer = pointer };
                    _sweep.Sent.Add(key);
                    _sweep.Cells.UnionWith(cells);
                }
                Send(removal, removalOrder);
            }
            else if (_sweep != null && !overPanel && ForcedHover == null) SweepOver(removal, removalOrder, pointer);
            var rig = buildings.LocalAvatar != null ? buildings.LocalAvatar.CameraRig : null;
            if (rig != null)
            {
                rig.BuildControls = !overPanel;
                rig.BuildTiltEnabled = _sweep == null;
            }
            Preview = _removePress.HasValue ? removal
                : _sweep != null ? removalOrder != null ? removal : new BuildPreview()
                : _hover.HasValue ? Plan(_dragStart ?? _hover.Value, _hover.Value, out _) : new BuildPreview();
            ShowWheel();
            ShowWorld(true);
            RefreshPanel();
        }

        private void OnActiveChanged(bool active)
        {
            var rig = buildings.LocalAvatar != null ? buildings.LocalAvatar.CameraRig : null;
            if (active)
            {
                var layout = Layout;
                var focus = SiteGridSpace.FootprintCenter(layout, 0, 0, layout.Width, layout.Depth);
                // Frames the whole lot for a 60 degree camera beside the panel.
                var distance = Mathf.Max(layout.Width, layout.Depth) * 1.1f + 4f;
                if (rig != null)
                {
                    if (_rememberedView is { } view && view.SiteId == session.ClientSiteId && Near(view.Focus))
                        rig.SetBuildView(view.Focus, view.Distance, view.Yaw, view.Pitch);
                    else rig.SetBuildView(focus, distance);
                }
                buildings.EditedSiteId = session.ClientSiteId;
                LastRejection = null;
                _catalogKey = null;
            }
            else
            {
                _sweep = null;
                if (rig != null && rig.BuildFocus.HasValue)
                    _rememberedView = (session.ClientSiteId, rig.BuildFocus.Value, rig.BuildDistance, rig.BuildYaw, rig.BuildPitch);
                if (rig != null)
                {
                    rig.SetBuildView(null, 0f);
                    rig.BuildTiltEnabled = true;
                }
                buildings.EditedSiteId = null;
                CancelPending();
                _dragStart = null;
            }
        }

        // Whether the avatar stands within RememberedViewReach of a build focus, across the ground.
        private bool Near(Vector3 focus)
        {
            var avatar = buildings.LocalAvatar;
            if (avatar == null) return false;
            var offset = avatar.transform.position - focus;
            offset.y = 0f;
            return offset.magnitude <= RememberedViewReach;
        }

        // Pick block: selects what is at the pointer as the cursor (a placed piece's offer and rotation, else the wall, door, back
        // door or window on the cell in its style). False, with the selection unchanged, when nothing there can be bought again.
        public bool PickAt((int X, int Z)? cell, string pieceId = null)
        {
            var site = session.ClientSite;
            if (site == null) return false;
            var piece = pieceId != null
                ? site.Equipment.FirstOrDefault(x => x.Id == pieceId && x.SiteId == session.ClientSiteId && x.State == EquipmentState.Placed)
                : null;
            if (piece == null && cell is { } at)
            {
                var layers = new[] { SiteGrid.TabletopLayer, SiteGrid.ObjectLayer };
                piece = site.Equipment.Where(x => x.SiteId == session.ClientSiteId && x.State == EquipmentState.Placed && x.Level == buildings.LocalLevel
                        && Array.IndexOf(layers, x.Layer ?? "") >= 0 && SiteGrid.Contains(x, at.X, at.Z, 1, 1))
                    .OrderBy(x => Array.IndexOf(layers, x.Layer ?? "")).FirstOrDefault();
            }
            if (piece != null)
            {
                var offer = session.Offers.FirstOrDefault(x => x != null && x.Truck == null && x.Equipment != null && x.Equipment.Kind == piece.Kind);
                if (offer == null) return false;
                SelectOffer(offer.Id);
                Rotation = ((piece.Rotation % 4) + 4) % 4;
                return true;
            }
            var restaurant = Restaurant;
            if (restaurant == null || cell is not { } c) return PickFloor(cell);
            var window = SiteGrid.WindowAt(restaurant, c.X, c.Z);
            if (window != null)
            {
                SelectTool(BuildTool.Window, StyleOr(window.Style, GoodsWorld.WindowStyles));
                return true;
            }
            if (SiteGrid.IsDoor(restaurant, c.X, c.Z))
            {
                var record = restaurant.Structures?.FirstOrDefault(x => x != null && x.Kind == GoodsWorld.DoorStructure && x.X == c.X && x.Z == c.Z);
                var back = SiteGrid.IsServiceDoor(restaurant, c.X, c.Z);
                SelectTool(back ? BuildTool.BackDoor : BuildTool.Door, back ? GoodsWorld.BackDoorStyle : StyleOr(record?.Style, GoodsWorld.DoorStyles));
                return true;
            }
            if (IsWallCell(restaurant, c.X, c.Z))
            {
                var partition = restaurant.Structures?.FirstOrDefault(x => x != null && x.Kind == GoodsWorld.PartitionStructure && x.X == c.X && x.Z == c.Z);
                SelectTool(BuildTool.Wall, StyleOr(partition?.Style ?? restaurant.WallStyle, GoodsWorld.WallStyles));
                return true;
            }
            return PickFloor(cell);
        }

        // A floor-layer piece (a floor tile or rug) under the cell, picked when nothing stands above it.
        private bool PickFloor((int X, int Z)? cell)
        {
            if (cell is not { } at) return false;
            var piece = session.ClientSite.Equipment.FirstOrDefault(x => x.SiteId == session.ClientSiteId && x.State == EquipmentState.Placed
                && x.Level == buildings.LocalLevel && x.Layer == SiteGrid.FloorLayer && SiteGrid.Contains(x, at.X, at.Z, 1, 1));
            var offer = piece == null ? null
                : session.Offers.FirstOrDefault(x => x != null && x.Truck == null && x.Equipment != null && x.Equipment.Kind == piece.Kind);
            if (offer == null) return false;
            SelectOffer(offer.Id);
            Rotation = ((piece.Rotation % 4) + 4) % 4;
            return true;
        }

        // A recorded style when the server lists it, else the list's first (the style an unstyled wall, door or window is drawn in).
        private static string StyleOr(string style, IReadOnlyList<string> styles) =>
            !string.IsNullOrEmpty(style) && styles.Contains(style) ? style : styles[0];

        // Click: single-cell tools act at once; drawing tools start a drag.
        public void PressAt((int X, int Z) cell)
        {
            CancelPending();
            switch (Tool)
            {
                case BuildTool.None:
                    return;
                case BuildTool.Wall:
                case BuildTool.Finish:
                    _dragStart = cell;
                    break;
                case BuildTool.Item:
                    if (OfferId == null) return;
                    _dragStart = cell;
                    break;
                case BuildTool.Sell:
                    RemoveAt(cell, _pointed, beltId: _pointedBelt);
                    break;
                default:
                    Send(Plan(cell, cell, out var order), order);
                    break;
            }
        }

        // Ends a drag (or a click) and orders what was drawn at once.
        public void ReleaseAt((int X, int Z) cell)
        {
            if (!_dragStart.HasValue) return;
            var start = _dragStart.Value;
            _dragStart = null;
            var preview = Plan(start, cell, out var order);
            Send(preview, order);
        }

        // Draws from one cell to another and orders it, as a mouse drag does.
        public void Drag((int X, int Z) from, (int X, int Z) to)
        {
            PressAt(from);
            if (_dragStart.HasValue) ReleaseAt(to);
        }

        // What the active tool would order for a drag between two cells, without ordering it.
        public BuildPreview PreviewDrag((int X, int Z) from, (int X, int Z) to) => Plan(from, to, out _);

        // Esc: drops a drag in progress; true when there was one (so build mode stays open).
        public bool CancelPending()
        {
            var had = _dragStart.HasValue;
            _dragStart = null;
            return had;
        }

        // Sells a placed piece (pieceId, the one the pointer is on) or, without one, removes the door, window or wall on the cell or
        // sells a piece standing on it; wall and ceiling decor is sold only by pointing at it. useCell false ignores the cell.
        public void RemoveAt((int X, int Z) cell, string pieceId = null, bool useCell = true, string beltId = null)
        {
            var preview = RemovalPlan(useCell ? cell : null, pieceId, out var order, beltId);
            Send(preview, order);
        }

        // One sweep frame: the thing under the pointer is removed when the pointer has just moved onto it, it is of the sweep's
        // family, this sweep has not sent it (or, for structure, touched its cells) and the preview finds no problem. A thing that
        // only appears under a still pointer (what stood under a piece just sold) waits for the pointer to move onto it again.
        private void SweepOver(BuildPreview removal, object order, Vector2 pointer)
        {
            var moved = (pointer - _sweep.Pointer).sqrMagnitude > 0.01f;
            _sweep.Pointer = pointer;
            var key = RemovalKey(order, out var structure, out var cells);
            if (key == _sweep.Last) return;
            _sweep.Last = key;
            if (key == null || !moved || structure != _sweep.Structure || removal.Problem != null || _sweep.Sent.Contains(key)
                || (structure && cells.Any(_sweep.Cells.Contains)))
                return;
            _sweep.Sent.Add(key);
            _sweep.Cells.UnionWith(cells);
            Send(removal, order);
        }

        // A stable name for what a removal order takes (a piece by ID; a window by its anchor, a door or wall by its cell), whether
        // it is structure, and the cells that structure covers; null for no order.
        private string RemovalKey(object order, out bool structure, out List<(int X, int Z)> cells)
        {
            cells = new List<(int X, int Z)>();
            structure = order is ShellOrder;
            if (order is string pieceId) return $"piece:{pieceId}";
            if (order is PickUpOrder pickUp) return $"piece:{pickUp.EquipmentId}";
            if (order is BeltRemoval belt) return $"belt:{belt.BeltId}";
            if (order is not ShellOrder { Cells: { Count: > 0 } } shell) return null;
            var (x, z) = (shell.Cells[0].X, shell.Cells[0].Z);
            var restaurant = Restaurant;
            var window = restaurant != null ? SiteGrid.WindowAt(restaurant, x, z) : null;
            if (window != null)
            {
                cells.Add((window.X, window.Z));
                cells.Add(window.Axis == 0 ? (window.X + 1, window.Z) : (window.X, window.Z + 1));
                return $"window:{window.X},{window.Z}";
            }
            cells.Add((x, z));
            return restaurant != null && SiteGrid.IsDoor(restaurant, x, z) ? $"door:{x},{z}" : $"wall:{x},{z}";
        }

        // The panel never keeps keyboard focus: Navigate (WASD and the arrows, shared with Move) would otherwise step a focused
        // scroller or card list while the camera pans.
        private void ReleasePanelFocus()
        {
            if (_window?.focusController?.focusedElement is VisualElement focused && (focused == _window || _window.Contains(focused)))
                focused.Blur();
        }

        private void Send(BuildPreview preview, object order)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || order == null) return;
            if (preview?.Problem != null)
            {
                LastRejection = preview.Problem;
                return;
            }
            LastRejection = null;
            var requestId = Guid.NewGuid().ToString("N");
            _pendingRequests[requestId] = preview?.Label ?? "";
            switch (order)
            {
                case ShellOrder shell:
                    Debug.Log($"[Build] Requesting {shell.Kind} on {shell.BuildingId} ({preview?.NetCents} cents).");
                    bridge.RequestShellOrder(requestId, shell);
                    break;
                case FurnishOrder furnish:
                    Debug.Log($"[Build] Requesting {furnish.Placements.Count} x {furnish.OfferId} ({preview?.NetCents} cents).");
                    bridge.RequestBuyAndPlace(requestId, furnish);
                    break;
                case string equipmentId:
                    Debug.Log($"[Build] Requesting the sale of {equipmentId}.");
                    bridge.RequestSell(requestId, equipmentId);
                    break;
                case PickUpOrder pickUp:
                    Debug.Log($"[Build] Requesting pickup of {pickUp.EquipmentId}.");
                    bridge.RequestPickUp(requestId, pickUp.EquipmentId);
                    break;
                case BeltRemoval belt:
                    Debug.Log($"[Build] Requesting removal of belt {belt.BeltId}.");
                    bridge.RequestRemoveBelt(requestId, belt.BeltId);
                    break;
            }
        }

        // What the active tool would order between two cells (the same cell for a click), and its preview.
        private BuildPreview Plan((int X, int Z) from, (int X, int Z) to, out object order)
        {
            order = null;
            var site = session.ClientSite;
            var preview = new BuildPreview();
            var restaurant = Restaurant;
            if (Tool == BuildTool.None) return preview;
            if (Tool == BuildTool.Sell)
                return RemovalPlan(ForcedHover == null ? _removeCell ?? to : to, _pointed, out order, ForcedHover == null ? _pointedBelt : null);
            if (Tool == BuildTool.Item) return ItemPlan(from, to, out order);
            if (restaurant == null)
            {
                preview.Problem = "no-restaurant";
                return preview;
            }
            var shell = new ShellOrder { BuildingId = restaurant.Id, Style = Style };
            switch (Tool)
            {
                case BuildTool.Wall:
                {
                    shell.Kind = ShellOrder.Partition;
                    // Cells that already have a wall are skipped, so a line may cross or continue existing walls.
                    shell.Cells.AddRange(Line(from, to).Where(c => !IsWallCell(restaurant, c.X, c.Z)).Select(c => new GridCell { X = c.X, Z = c.Z }));
                    if (shell.Cells.Count == 0) shell.Cells.Add(new GridCell { X = to.X, Z = to.Z });
                    preview.Label = $"{shell.Cells.Count} m of {Name(Style)} wall";
                    break;
                }
                case BuildTool.Door:
                case BuildTool.BackDoor:
                    shell.Kind = Tool == BuildTool.BackDoor ? ShellOrder.BackDoor : ShellOrder.Door;
                    shell.X = to.X;
                    shell.Z = to.Z;
                    preview.Label = Tool == BuildTool.BackDoor ? $"{Name(Style)} back door" : $"{Name(Style)} door";
                    break;
                case BuildTool.Window:
                    shell.Kind = ShellOrder.Window;
                    shell.X = to.X;
                    shell.Z = to.Z;
                    // The window follows its wall: along X when the next cell east is a wall, along Z when the next cell north is; R
                    // picks between them where both are.
                    var eastWall = IsWallCell(restaurant, to.X + 1, to.Z);
                    var northWall = IsWallCell(restaurant, to.X, to.Z + 1);
                    shell.Axis = eastWall && northWall ? Rotation % 2 : eastWall ? 0 : northWall ? 1 : Rotation % 2;
                    preview.Label = $"{Name(Style)} window";
                    break;
                case BuildTool.Finish:
                    shell.Kind = ShellOrder.WallFinish;
                    shell.Cells.AddRange(Line(from, to).Where(c => IsWallCell(restaurant, c.X, c.Z)).Select(c => new GridCell { X = c.X, Z = c.Z }));
                    if (shell.Cells.Count == 0) shell.Cells.Add(new GridCell { X = to.X, Z = to.Z });
                    preview.Label = $"{Name(Style)} finish on {shell.Cells.Count} m of wall";
                    break;
            }
            var plan = GoodsWorld.PlanShell(site, shell, PricePercent());
            preview.Problem = plan.Problem ?? FundsProblem(plan.NetCents);
            preview.ChargeCents = plan.ChargeCents;
            preview.RefundCents = plan.RefundCents;
            preview.Cells.AddRange(StructureCells(shell, plan));
            order = shell;
            return preview;
        }

        // A straight line of cells from one cell towards another, along whichever axis the second lies further along.
        private static IEnumerable<(int X, int Z)> Line((int X, int Z) from, (int X, int Z) to)
        {
            var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Z - from.Z);
            var length = alongX ? Math.Abs(to.X - from.X) : Math.Abs(to.Z - from.Z);
            for (var step = 0; step <= length; step++)
                yield return alongX ? (from.X + Math.Sign(to.X - from.X) * step, from.Z) : (from.X, from.Z + Math.Sign(to.Z - from.Z) * step);
        }

        // A wall of the restaurant stands on the cell (a door or window included): a wall record, or a rectangular shell's perimeter.
        private static bool IsWallCell(GoodsBuilding restaurant, int x, int z) => SiteGrid.IsPartition(restaurant, x, z) || SiteGrid.OnPerimeter(restaurant, x, z);

        // The rotation a piece is placed with at a cell: wall decor faces away from its wall (into a room if it can, else to any open
        // cell), a backed piece is turned round when that puts more wall behind it, and a dock when that puts more wall in front
        // (its door frame). Anything else keeps the chosen rotation.
        public int FacingRotation(EquipmentDefinition definition, int cellX, int cellZ)
        {
            var site = session.ClientSite;
            var siteId = session.ClientSiteId;
            if (site == null || definition == null) return Rotation;
            if (definition.Mount == EquipmentMount.Wall)
            {
                var best = Rotation;
                var bestScore = -1;
                for (var turn = 0; turn < 4; turn++)
                {
                    var rotation = (Rotation + turn) % 4;
                    var (fx, fz) = SiteGrid.Facing(rotation);
                    var (x, z) = (cellX + fx, cellZ + fz);
                    var score = SiteGrid.WallAt(site, siteId, x, z) ? 0
                        : site.Buildings.Any(b => b.SiteId == siteId && SiteGrid.IsInterior(b, x, z)) ? 2 : 1;
                    if (score <= bestScore) continue;
                    best = rotation;
                    bestScore = score;
                }
                return best;
            }
            if (definition.Kind == GoodsWorld.DockKind)
            {
                // A dock's upright door frame is its front: it turns round when that puts more wall in front of it.
                int WallsAhead(int rotation)
                {
                    var (width, depth) = SiteGrid.Footprint(definition.Width, definition.Depth, rotation);
                    return SiteGrid.BehindCells(cellX, cellZ, width, depth, (rotation + 2) % 4).Count(c => SiteGrid.WallAt(site, siteId, c.X, c.Z));
                }
                var turned = (Rotation + 2) % 4;
                return WallsAhead(turned) > WallsAhead(Rotation) ? turned : Rotation;
            }
            if (definition.Mount != EquipmentMount.Backed) return Rotation;
            int WallsBehind(int rotation)
            {
                var (width, depth) = SiteGrid.Footprint(definition.Width, definition.Depth, rotation);
                return SiteGrid.BehindCells(cellX, cellZ, width, depth, rotation).Count(c => SiteGrid.WallAt(site, siteId, c.X, c.Z));
            }
            var flipped = (Rotation + 2) % 4;
            return WallsBehind(flipped) > WallsBehind(Rotation) ? flipped : Rotation;
        }

        // Cells the shell order touches, for the tint: the new perimeter for a resize, otherwise the named cells.
        private static IEnumerable<(int X, int Z)> StructureCells(ShellOrder order, ShellPlan plan)
        {
            switch (order.Kind)
            {
                case ShellOrder.Resize:
                    for (var x = order.X; x < order.X + Math.Max(1, order.Width); x++)
                    for (var z = order.Z; z < order.Z + Math.Max(1, order.Depth); z++)
                        if (x == order.X || z == order.Z || x == order.X + order.Width - 1 || z == order.Z + order.Depth - 1) yield return (x, z);
                    break;
                case ShellOrder.Window:
                    yield return (order.X, order.Z);
                    yield return order.Axis == 0 ? (order.X + 1, order.Z) : (order.X, order.Z + 1);
                    break;
                case ShellOrder.WallFinish when order.Cells.Count == 0:
                    if (plan.Building == null) yield break;
                    foreach (var cell in Perimeter(plan.Building)) yield return cell;
                    break;
                default:
                    if (order.Cells.Count > 0) foreach (var cell in order.Cells) yield return (cell.X, cell.Z);
                    else yield return (order.X, order.Z);
                    break;
            }
        }

        private static IEnumerable<(int X, int Z)> Perimeter(GoodsBuilding building)
        {
            for (var x = building.CellX; x < building.CellX + building.Width; x++)
            for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
                if (SiteGrid.OnPerimeter(building, x, z)) yield return (x, z);
        }

        // Buying and placing the chosen piece over the dragged area (whole footprints from the first cell), or at one cell.
        private BuildPreview ItemPlan((int X, int Z) from, (int X, int Z) to, out object order)
        {
            order = null;
            var preview = new BuildPreview();
            var offer = session.Offers.FirstOrDefault(x => x != null && x.Id == OfferId && x.Equipment != null);
            if (offer == null)
            {
                preview.Problem = "choose-an-item";
                return preview;
            }
            var (width, depth) = SiteGrid.Footprint(offer.Equipment.Width, offer.Equipment.Depth, Rotation);
            var furnish = new FurnishOrder { SiteId = session.ClientSiteId, OfferId = offer.Id };
            var stepX = to.X >= from.X ? width : -width;
            var stepZ = to.Z >= from.Z ? depth : -depth;
            for (var x = from.X; stepX > 0 ? x <= to.X : x >= to.X; x += stepX)
            for (var z = from.Z; stepZ > 0 ? z <= to.Z : z >= to.Z; z += stepZ)
            {
                if (furnish.Placements.Count >= GoodsWorld.MaxFurnishPlacements) break;
                var anchorX = stepX > 0 ? x : x - width + 1;
                var anchorZ = stepZ > 0 ? z : z - depth + 1;
                furnish.Placements.Add(new GridPlacement { X = anchorX, Z = anchorZ, Rotation = FacingRotation(offer.Equipment, anchorX, anchorZ) });
            }
            var site = session.ClientSite;
            var problem = GoodsWorld.FurnishProblem(site, session.ClientSiteId, offer.Equipment.CreateTemplate(), furnish.Placements, 0, SiteOffer());
            preview.ChargeCents = offer.PriceCents * furnish.Placements.Count;
            preview.Problem = problem ?? FundsProblem(preview.ChargeCents);
            preview.Label = $"{furnish.Placements.Count} x {offer.Equipment.DisplayName}";
            foreach (var placement in furnish.Placements)
                for (var x = placement.X; x < placement.X + width; x++)
                for (var z = placement.Z; z < placement.Z + depth; z++)
                    preview.Cells.Add((x, z));
            order = furnish;
            return preview;
        }

        // The removal of the given piece (any floor), else of the given belt or lift, else of a piece standing on the cell on the
        // floor shown (tabletop, object, then floor layer; wall and ceiling decor hangs away from its cell, so it is only removed by
        // pointing at it), else of a belt or lift there, else of the door, window or wall there. A piece with a recorded charge is
        // sold for that refund; one without is picked up into the inventory, so removing never deletes it for nothing.
        private BuildPreview RemovalPlan((int X, int Z)? target, string pieceId, out object order, string beltId = null)
        {
            order = null;
            var preview = new BuildPreview { Removal = true };
            var site = session.ClientSite;
            var level = buildings.LocalLevel;
            var layers = new[] { SiteGrid.TabletopLayer, SiteGrid.ObjectLayer, SiteGrid.FloorLayer };
            var placed = site.Equipment.Where(x => x.SiteId == session.ClientSiteId && x.State == EquipmentState.Placed);
            var piece = pieceId != null ? placed.FirstOrDefault(x => x.Id == pieceId)
                : beltId == null && target is { } at ? placed.Where(x => x.Level == level && Array.IndexOf(layers, x.Layer ?? "") >= 0
                        && SiteGrid.Contains(x, at.X, at.Z, 1, 1))
                    .OrderBy(x => Array.IndexOf(layers, x.Layer ?? "")).FirstOrDefault()
                : null;
            if (piece != null)
            {
                var definition = session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == piece.Kind);
                var sold = piece.ChargedCents > 0;
                preview.RefundCents = piece.ChargedCents;
                preview.Label = $"{(sold ? "Sell" : "Pick up")} {definition?.DisplayName ?? piece.Kind}";
                var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
                for (var x = piece.CellX; x < piece.CellX + width; x++)
                for (var z = piece.CellZ; z < piece.CellZ + depth; z++)
                    preview.Cells.Add((x, z));
                if (site.Customers.Any(x => x.CounterId == piece.Id || x.TableId == piece.Id)) preview.Problem = "occupied";
                order = sold ? piece.Id : new PickUpOrder(piece.Id);
                return preview;
            }
            var belts = site.Belts.Where(x => x.SiteId == session.ClientSiteId);
            var belt = beltId != null ? belts.FirstOrDefault(x => x.Id == beltId)
                : target is { } on ? BeltPresenter.Touching(belts, level).FirstOrDefault(x => x.CellX == on.X && x.CellZ == on.Z)
                : null;
            if (belt != null)
            {
                preview.Label = belt.Lift != 0 ? "Take up conveyor lift" : "Take up belt";
                preview.Cells.Add((belt.CellX, belt.CellZ));
                order = new BeltRemoval(belt.Id);
                return preview;
            }
            var restaurant = Restaurant;
            if (target is not { } cell)
            {
                preview.Problem = "nothing-here";
                return preview;
            }
            if (restaurant != null && (SiteGrid.WindowAt(restaurant, cell.X, cell.Z) != null || SiteGrid.IsDoor(restaurant, cell.X, cell.Z)
                    || IsWallCell(restaurant, cell.X, cell.Z)))
            {
                var shell = new ShellOrder { Kind = ShellOrder.Remove, BuildingId = restaurant.Id, Cells = { new GridCell { X = cell.X, Z = cell.Z } } };
                var plan = GoodsWorld.PlanShell(site, shell, PricePercent());
                preview.Problem = plan.Problem;
                preview.RefundCents = plan.RefundCents;
                preview.Label = SiteGrid.WindowAt(restaurant, cell.X, cell.Z) != null ? "Remove window"
                    : SiteGrid.IsDoor(restaurant, cell.X, cell.Z) ? "Remove door" : "Remove wall";
                preview.Cells.Add(cell);
                order = shell;
                return preview;
            }
            preview.Problem = "nothing-here";
            return preview;
        }

        private string FundsProblem(long net)
        {
            var cash = session.ClientSite?.Companies.FirstOrDefault(x => x.SiteIds.Contains(session.ClientSiteId))?.Cash;
            if (cash == null) return "no-company";
            return net > cash.Value ? "insufficient-funds" : null;
        }

        // The current site's listed lot (its street side and district price), from the replicated layout; null on dev sites.
        private PropertyOffer SiteOffer()
        {
            var placement = SitePlacement.Active;
            var lot = placement?.LotOf(session.ClientSiteId);
            return lot == null ? null : placement.OfferOf(lot.Id);
        }

        private int PricePercent() => SiteOffer()?.PricePercent ?? 100;

        private string Name(string style) => buildings.RestaurantStyles != null ? buildings.RestaurantStyles.NameOf(style) : style;

        private Camera BuildCamera()
        {
            var rig = buildings.LocalAvatar != null ? buildings.LocalAvatar.CameraRig : null;
            return rig != null ? rig.GetComponentInChildren<Camera>() : null;
        }

        // The placed piece of the current site whose collider (trigger or not) the pointer is on: the nearest piece hit before
        // anything solid, or inside the first solid thing hit (wall decor hangs within its wall cell's collider box). Anything
        // further, behind a wall or the floor, is never picked. Pieces drawn hidden in build mode (ceiling panels) and avatars are
        // looked through. surface is the lot cell of the first solid thing hit (just inside it), or null.
        private string PointedPiece(Vector2 pointer, SiteLayout layout, out (int X, int Z)? surface, out string belt)
        {
            surface = null;
            belt = null;
            var camera = BuildCamera();
            if (camera == null) return null;
            Collider blocker = null;
            var blockedAt = float.PositiveInfinity;
            var ray = camera.ScreenPointToRay(pointer);
            var hits = Physics.RaycastAll(ray, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            foreach (var hit in hits.OrderBy(x => x.distance))
            {
                if (hit.collider.GetComponentInParent<PlayerAvatar>() != null) continue;
                // A belt or lift in front of anything solid is what the pointer is on (belt is set; no piece is).
                var beltVisual = hit.collider.GetComponentInParent<BeltVisual>();
                if (beltVisual != null)
                {
                    if (blocker == null && beltVisual.GetComponentsInChildren<Renderer>().Any(x => x.enabled)
                        && session.ClientSite?.Belts.Any(x => x.Id == beltVisual.BeltId && x.SiteId == session.ClientSiteId) == true)
                    {
                        belt = beltVisual.BeltId;
                        return null;
                    }
                    continue;
                }
                var visual = hit.collider.GetComponentInParent<EquipmentVisual>();
                if (visual == null)
                {
                    if (blocker == null && !hit.collider.isTrigger)
                    {
                        blocker = hit.collider;
                        blockedAt = hit.distance;
                        var (x, z) = SiteGridSpace.AnchorAt(layout, hit.point + ray.direction * 0.05f, 1, 1);
                        if (x >= 0 && z >= 0 && x < layout.Width && z < layout.Depth) surface = (x, z);
                    }
                    continue;
                }
                // Inside the wall that was hit counts only for decor on the face turned to the camera (it faces its root's forward).
                if (hit.distance > blockedAt && (!blocker.bounds.Contains(hit.point) || Vector3.Dot(ray.direction, visual.transform.forward) >= 0f))
                    return null;
                if (!visual.GetComponentsInChildren<Renderer>().Any(x => x.enabled)) continue;
                if (session.ClientSite?.Equipment.Any(x => x.Id == visual.EquipmentId && x.SiteId == session.ClientSiteId
                        && x.State == EquipmentState.Placed) == true)
                    return visual.EquipmentId;
            }
            return null;
        }

        private (int X, int Z)? PointerCell(SiteLayout layout)
        {
            var camera = BuildCamera();
            if (camera == null) return null;
            var ray = camera.ScreenPointToRay(pointAction.action.ReadValue<Vector2>());
            var floor = new Plane(Vector3.up, Vector3.up * SiteGridSpace.FloorHeight(layout, 0));
            if (!floor.Raycast(ray, out var distance)) return null;
            var (x, z) = SiteGridSpace.AnchorAt(layout, ray.GetPoint(distance), 1, 1);
            return x >= 0 && z >= 0 && x < layout.Width && z < layout.Depth ? (x, z) : null;
        }

        private bool PointerOverPanel()
        {
            if (_window?.panel == null) return false;
            var pointer = pointAction.action.ReadValue<Vector2>();
            var position = RuntimePanelUtils.ScreenToPanel(_window.panel, new Vector2(pointer.x, UnityEngine.Screen.height - pointer.y));
            return _window.worldBound.Contains(position);
        }

        // Grid, cell tint and model ghost.
        private void ShowWorld(bool shown)
        {
            if (_grid != null && _grid.activeSelf != shown) _grid.SetActive(shown);
            var layout = shown ? Layout : null;
            var preview = Preview;
            var cells = shown && layout != null ? preview.Cells : new List<(int X, int Z)>();
            var color = preview.Problem != null ? invalidColor : preview.Removal ? removeColor : validColor;
            while (_highlights.Count < Math.Min(cells.Count, MaxHighlights)) _highlights.Add(Tile($"Build highlight {_highlights.Count}"));
            for (var index = 0; index < _highlights.Count; index++)
            {
                var on = index < cells.Count;
                if (_highlights[index].gameObject.activeSelf != on) _highlights[index].gameObject.SetActive(on);
                if (!on) continue;
                _highlights[index].transform.position = SiteGridSpace.FootprintCenter(layout, cells[index].X, cells[index].Z, 1, 1) + Vector3.up * 0.04f;
                Tint(_highlights[index], color);
            }
            var offer = shown && Tool == BuildTool.Item && !_removePress.HasValue && _sweep == null && _hover.HasValue
                ? session.Offers.FirstOrDefault(x => x != null && x.Id == OfferId && x.Equipment != null) : null;
            ShowGhost(offer?.Equipment);
            if (_ghost == null || offer == null) return;
            var anchor = _dragStart ?? _hover.Value;
            var turns = FacingRotation(offer.Equipment, anchor.X, anchor.Z);
            var (width, depth) = SiteGrid.Footprint(offer.Equipment.Width, offer.Equipment.Depth, turns);
            var rotation = SiteGridSpace.Rotation(turns);
            var againstWall = EquipmentModel.BackedAgainstWall(offer.Equipment, session.ClientSite, session.ClientSiteId, anchor.X, anchor.Z, turns);
            var center = SiteGridSpace.FootprintCenter(layout, anchor.X, anchor.Z, width, depth) + EquipmentModel.MountOffset(offer.Equipment, rotation, againstWall);
            if (offer.Equipment.Mount == EquipmentMount.Tabletop) center += Vector3.up * 0.76f;
            _ghost.transform.SetPositionAndRotation(center, rotation);
            var ghostColor = color;
            ghostColor.a = 0.45f;
            foreach (var renderer in _ghostRenderers) Tint(renderer, ghostColor);
        }

        private void ShowGhost(EquipmentDefinition definition)
        {
            var kind = definition?.Kind;
            if (kind == _ghostKind) return;
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = Array.Empty<Renderer>();
            _ghostKind = kind;
            if (definition == null || definition.VisualPrefab == null) return;
            _ghost = EquipmentModel.CreateGhost(definition, transform, ghostModelMaterial);
            _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
        }

        // One mesh of thin lines on every cell edge of the current site, rebuilt when the site or its size changes.
        private void EnsureGrid(SiteLayout layout)
        {
            var key = $"{layout.SiteId}:{layout.Width}x{layout.Depth}:{SiteGridSpace.Origin(layout)}";
            if (_grid != null && _gridFor == key) return;
            if (_grid != null) Destroy(_grid);
            _gridFor = key;
            _grid = new GameObject("Build grid");
            _grid.transform.SetParent(transform, false);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var corner = SiteGridSpace.FootprintCenter(layout, 0, 0, 1, 1) - new Vector3(SiteGrid.CellSize, 0f, SiteGrid.CellSize) * 0.5f + Vector3.up * 0.03f;
            const float half = 0.03f;
            void Line(Vector3 a, Vector3 b)
            {
                var across = Vector3.Cross(Vector3.up, (b - a).normalized) * half;
                var start = vertices.Count;
                vertices.AddRange(new[] { a - across, a + across, b + across, b - across });
                // Clockwise seen from above, so the lines face the top-down camera.
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            for (var x = 0; x <= layout.Width; x++)
                Line(corner + new Vector3(x, 0f, 0f), corner + new Vector3(x, 0f, layout.Depth));
            for (var z = 0; z <= layout.Depth; z++)
                Line(corner + new Vector3(0f, 0f, z), corner + new Vector3(layout.Width, 0f, z));
            var mesh = new Mesh { name = "Build grid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            _grid.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = _grid.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = cellMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Tint(renderer, gridColor);
        }

        private Renderer Tile(string name)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = name;
            Destroy(tile.GetComponent<Collider>());
            tile.transform.SetParent(transform, false);
            tile.transform.localScale = new Vector3(SiteGrid.CellSize * 0.92f, 0.02f, SiteGrid.CellSize * 0.92f);
            var renderer = tile.GetComponent<Renderer>();
            renderer.sharedMaterial = cellMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        private void Tint(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(_block);
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!_pendingRequests.Remove(outcome.RequestId, out var label)) return;
            LastRejection = outcome.Accepted ? null : string.IsNullOrEmpty(outcome.Reason) ? "rejected" : outcome.Reason;
            Debug.Log($"[Build] {(outcome.Accepted ? "Accepted" : "Rejected")}: {label} {outcome.Reason} ({outcome.Cents} cents, revision {outcome.Revision}).");
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            _pendingRequests.Clear();
        }

        // ---- Panel (Build UXML and USS under Assets/UI/Resources/UI, decision 0042) ----

        private void BuildPanel()
        {
            if (document == null) return;
            var root = document.rootVisualElement;
            root.Clear();
            UiTemplates.Fill(root, "Build/Build");
            _window = root.Part("build");
            _window.Show(false);
            root.Part<Button>("build-close").clicked += Toggle;
            _cash = root.Part<Label>("build-cash");
            _ambience = root.Part<Label>("build-ambience");
            _tools = root.Part("build-tools");
            _filters = root.Part("build-filters");
            _catalog = root.Part("build-catalog");
            _preview = root.Part<Label>("build-preview");
            _status = root.Part<Label>("build-status");
            root.Part<Button>("build-cancel").clicked += ClearSelection;
            root.Part<Button>("build-leave").clicked += Toggle;
            foreach (var tool in Tools)
            {
                var chosen = tool;
                _tools.Part<Button>(ToolName(tool)).clicked += () => SelectTool(chosen);
            }
            // The catalog scrolls by the pointer only: no part of it takes keyboard focus, and Navigate (WASD and the arrows, shared
            // with Move) is stopped before it reaches the panel, so panning the camera never scrolls the list.
            var scroll = root.Part<ScrollView>("build-catalog-scroll");
            foreach (var part in new VisualElement[] { scroll, scroll.contentContainer, scroll.verticalScroller, scroll.verticalScroller.slider,
                         scroll.horizontalScroller, scroll.horizontalScroller.slider })
                part.focusable = false;
            _window.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                _window.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            // The hold-to-remove wheel: a ring that fills clockwise from the top as Remove is held.
            _holdWheel = root.Part("build-hold-wheel");
            _holdWheel.generateVisualContent += DrawWheel;
        }

        // The tool chips in Build.uxml, named build-tool-<tool>; structure is chosen from catalog cards.
        private static readonly BuildTool[] Tools = { BuildTool.Sell };

        private static string ToolName(BuildTool tool) => $"build-tool-{tool.ToString().ToLowerInvariant()}";

        private void DrawWheel(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var center = new Vector2(WheelSize * 0.5f, WheelSize * 0.5f);
            var radius = WheelSize * 0.5f - 4f;
            painter.lineWidth = 6f;
            painter.strokeColor = new Color(0f, 0f, 0f, 0.55f);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
            var progress = RemoveProgress;
            if (progress <= 0f) return;
            painter.lineWidth = 4f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color(removeColor.r, removeColor.g, removeColor.b, 1f);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * progress));
            painter.Stroke();
        }

        // Centres the wheel on the pointer while Remove is held, and hides it otherwise.
        private void ShowWheel()
        {
            if (_holdWheel?.panel == null) return;
            var holding = _removePress.HasValue;
            _holdWheel.Show(holding);
            if (!holding) return;
            var pointer = pointAction.action.ReadValue<Vector2>();
            var position = RuntimePanelUtils.ScreenToPanel(_holdWheel.panel, new Vector2(pointer.x, UnityEngine.Screen.height - pointer.y));
            _holdWheel.style.left = position.x - WheelSize * 0.5f;
            _holdWheel.style.top = position.y - WheelSize * 0.5f;
            _holdWheel.MarkDirtyRepaint();
        }

        private void RefreshPanel()
        {
            if (_window == null) return;
            var site = session.ClientSite;
            var company = site.Companies.FirstOrDefault(x => x.SiteIds.Contains(session.ClientSiteId));
            _cash.SetText(PlayerHud.FormatCash(company?.Cash ?? 0));
            _ambience.SetText($"{RestaurantRules.Ambience(site, session.ClientSiteId)} / {RestaurantRules.AmbienceCap}");
            var key = $"{Tool}|{Style}|{OfferId}";
            if (_shownTools != key)
            {
                _shownTools = key;
                FillTools();
            }
            var catalogKey = $"{session.Offers.Count}|{PricePercent()}|{Filter}";
            if (_catalogKey != catalogKey)
            {
                _catalogKey = catalogKey;
                FillCatalog();
            }
            var preview = Preview;
            var money = preview.ChargeCents == 0 && preview.RefundCents == 0 ? "free"
                : $"charge {PlayerHud.FormatCash(preview.ChargeCents)}, refund {PlayerHud.FormatCash(preview.RefundCents)}, net {(preview.NetCents < 0 ? "+" : "-")}{PlayerHud.FormatCash(Math.Abs(preview.NetCents))}";
            var idle = string.IsNullOrEmpty(preview.Label) && preview.Problem == null;
            _preview.SetText(idle ? Hint() : $"{preview.Label}: {money}" + (preview.Problem != null ? $"  [{preview.Problem}]" : ""));
            _preview.EnableInClassList("build__preview--problem", preview.Problem != null);
            _preview.EnableInClassList("build__preview--idle", idle);
            _status.SetText(HasPendingRequests ? "Waiting for the server..." : string.IsNullOrEmpty(LastRejection) ? "" : $"Refused: {LastRejection}");
            _status.Show(_status.text.Length > 0);
        }

        // What to do next, while nothing is previewed.
        private string Hint() => Tool == BuildTool.None
            ? "Pick an item from the catalog, or middle click something to pick it. Hold right click on something to sell or remove it, then keep holding and sweep to remove more."
            : "Click to place, drag to draw. R rotates; X clears. Middle click picks what the pointer is on.";

        // Lights the chosen tool and marks the chosen catalog card (a structure style or an offer).
        private void FillTools()
        {
            foreach (var tool in Tools) _tools.Part(ToolName(tool)).EnableInClassList("ff-chip--chosen", Tool == tool);
            foreach (var row in _catalog.Query<VisualElement>(className: "build-item").ToList())
                row.EnableInClassList("build-item--chosen", row.userData is BuildCatalogEntry entry && IsChosen(entry));
        }

        private bool IsChosen(BuildCatalogEntry entry) => entry.Tool == BuildTool.Item
            ? Tool == BuildTool.Item && OfferId == entry.OfferId
            : entry.Tool != BuildTool.None && Tool == entry.Tool && Style == entry.Style;

        // The filter chips (All, then each category with cards) and the cards of the chosen filter under their headings.
        private void FillCatalog()
        {
            _entries = BuildCatalog.Entries(session.Offers, session.Items, Name, PricePercent(),
                (tool, style) => StructureIcons.Get(buildings.RestaurantStyles, tool, style));
            if (Filter != BuildCatalogFilter.All && _entries.All(x => x.Filter != Filter)) Filter = BuildCatalogFilter.All;
            _filters.Clear();
            foreach (var filter in BuildCatalog.Filters)
            {
                if (filter != BuildCatalogFilter.All && _entries.All(x => x.Filter != filter)) continue;
                var chosen = filter;
                var chip = new Button(() => SelectFilter(chosen)) { name = BuildCatalog.FilterElementName(filter), text = BuildCatalog.FilterName(filter), focusable = false };
                chip.AddToClassList("ff-button");
                chip.AddToClassList("ff-chip");
                chip.EnableInClassList("ff-chip--chosen", Filter == filter);
                _filters.Add(chip);
            }
            _catalog.Clear();
            string section = null;
            foreach (var entry in _entries.Where(x => BuildCatalog.Matches(x, Filter)))
            {
                if (entry.Section != section)
                {
                    section = entry.Section;
                    var heading = new Label(section) { pickingMode = PickingMode.Ignore };
                    heading.AddToClassList("build-section");
                    heading.EnableInClassList("build-section--first", _catalog.childCount == 0);
                    _catalog.Add(heading);
                }
                _catalog.Add(Card(entry));
            }
            _shownTools = null;
        }

        // One catalog card; the whole card picks it. Belts are goods laid in the world rather than pieces placed here, so their
        // card hands over to the belt cursor.
        private VisualElement Card(BuildCatalogEntry entry)
        {
            var row = UiTemplates.Clone("Build/BuildCatalogItem");
            row.name = BuildCatalog.CardName(entry.Key);
            row.userData = entry;
            var icon = row.Part("icon");
            icon.SetIcon(entry.Icon, "");
            icon.EnableInClassList("ff-icon--empty", entry.Icon == null);
            row.Part<Label>("title").text = entry.Title;
            row.Part<Label>("price").text = entry.PriceCents == 0 ? "Free" : PlayerHud.FormatCash(entry.PriceCents) + entry.PriceSuffix;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (entry.IsBelt) interaction.LayBelts(entry.OfferId);
                else if (entry.Tool == BuildTool.Item) SelectOffer(entry.OfferId);
                else SelectTool(entry.Tool, entry.Style);
            });
            return row;
        }
    }
}
