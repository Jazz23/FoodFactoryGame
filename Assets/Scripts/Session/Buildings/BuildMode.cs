// Restaurant build mode (decision 0034, slice 2), local player only. Build (B) opens it as a screen of its own: the camera looks
// straight down on the current site, a grid covers the lot and a panel on the right offers tools (sell or remove, resize the
// shell, interior walls, doors, windows, wall finish) and a catalog of furnishings by category. The pointer's cell shows a ghost
// tinted by validity: the same pure rules the server applies (GoodsWorld.PlanShell, FurnishProblem, SiteGrid) plus the company's
// cash, with the price, refund and net amount, or the reason it would be refused. A click places a single piece, door or window;
// dragging draws a shell, a line of wall or a filled area of a piece, which waits for Confirm (BuildConfirm, Enter) or Cancel
// (CloseScreen, Esc). Remove (right mouse) sells the piece under the pointer, or removes the door, window or interior wall
// there, for its recorded refund. Every order is a request; nothing changes here until the next replicated baseline, and the
// server's reason is shown when it refuses. Controls come from the Player action map (Input System).
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Buildings
{
    public enum BuildTool
    {
        Sell,
        Resize,
        Wall,
        Door,
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
        private static readonly Color Backdrop = new(0.17f, 0.17f, 0.19f, 0.96f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.72f, 0.72f, 0.76f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color Chosen = new(0.98f, 0.66f, 0.2f, 1f);
        // Catalog sections in palette order; equipment without a category (machines, the supplier's pieces) is listed last.
        private static readonly string[] Sections = { "Restaurant", "Furniture", "Decor", "Lighting", "Surfaces", "Architecture", "Service" };
        private const string EquipmentSection = "Equipment";
        private const int MaxHighlights = 400;

        [SerializeField] private SessionRoot session;
        [SerializeField] private EquipmentInteraction interaction;
        [SerializeField] private BuildingPresenter buildings;
        [SerializeField] private UIDocument document;
        // Flat cell tiles (grid lines and highlights) and the see-through model ghost.
        [SerializeField] private Material cellMaterial;
        [SerializeField] private Material ghostModelMaterial;
        [SerializeField] private InputActionReference buildAction;
        [SerializeField] private InputActionReference confirmAction;
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
        private object _pendingOrder;
        private BuildPreview _pendingPreview;
        private bool _wasActive;
        private VisualElement _window;
        private Label _cash;
        private Label _preview;
        private Label _status;
        private VisualElement _tools;
        private VisualElement _styles;
        private VisualElement _catalog;
        private Button _confirm;
        private int _catalogVersion = -1;
        private string _shownTools;

        public bool Active => interaction != null && interaction.Screen == InteractionScreen.Build;
        public BuildTool Tool { get; private set; } = BuildTool.Item;
        public string Style { get; private set; } = "";
        public string OfferId { get; private set; }
        public int Rotation { get; private set; }
        public BuildPreview Preview { get; private set; } = new();
        public bool HasPending => _pendingOrder != null;
        public BuildPreview PendingPreview => _pendingPreview;
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
            confirmAction.action.performed += OnConfirm;
            buildAction.action.Enable();
            confirmAction.action.Enable();
            interaction.BuildEscape = CancelPending;
        }

        private void OnDisable()
        {
            buildAction.action.performed -= OnBuild;
            confirmAction.action.performed -= OnConfirm;
            buildAction.action.Disable();
            confirmAction.action.Disable();
            if (interaction != null && interaction.BuildEscape == CancelPending) interaction.BuildEscape = null;
            Subscribe(null);
            ShowWorld(false);
        }

        private void Start() => BuildPanel();

        private void OnBuild(InputAction.CallbackContext _) => Toggle();

        private void OnConfirm(InputAction.CallbackContext _)
        {
            if (Active) Confirm();
        }

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

        private static string DefaultStyle(BuildTool tool) => tool switch
        {
            BuildTool.Wall or BuildTool.Finish => GoodsWorld.WallStyles[0],
            BuildTool.Door => GoodsWorld.DoorStyles[0],
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
            if (_window != null) _window.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active)
            {
                ShowWorld(false);
                return;
            }
            var layout = Layout;
            EnsureGrid(layout);
            if (rotateAction.action.WasPressedThisFrame()) Rotate();
            _hover = ForcedHover ?? PointerCell(layout);
            var overPanel = PointerOverPanel();
            if (!overPanel && ForcedHover == null)
            {
                if (placeAction.action.WasPressedThisFrame() && _hover.HasValue && interaction.ScreenClicksArmed) PressAt(_hover.Value);
                if (placeAction.action.WasReleasedThisFrame() && _dragStart.HasValue) ReleaseAt(_hover ?? _dragStart.Value);
                if (removeAction.action.WasPressedThisFrame() && _hover.HasValue) RemoveAt(_hover.Value);
            }
            Preview = _pendingPreview ?? (_hover.HasValue ? Plan(_dragStart ?? _hover.Value, _hover.Value, out _) : new BuildPreview());
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
                if (rig != null) rig.SetBuildView(focus, distance);
                buildings.EditedSiteId = session.ClientSiteId;
                LastRejection = null;
                _catalogVersion = -1;
            }
            else
            {
                if (rig != null) rig.SetBuildView(null, 0f);
                buildings.EditedSiteId = null;
                CancelPending();
                _dragStart = null;
            }
        }

        // Click: single-cell tools act at once; drawing tools start a drag.
        public void PressAt((int X, int Z) cell)
        {
            if (_pendingOrder != null) CancelPending();
            switch (Tool)
            {
                case BuildTool.Resize:
                case BuildTool.Wall:
                    _dragStart = cell;
                    break;
                case BuildTool.Item:
                    if (OfferId == null) return;
                    _dragStart = cell;
                    break;
                case BuildTool.Sell:
                    RemoveAt(cell);
                    break;
                default:
                    Send(Plan(cell, cell, out var order), order);
                    break;
            }
        }

        // Ends a drag: a piece dragged on one cell places at once; anything else waits for Confirm.
        public void ReleaseAt((int X, int Z) cell)
        {
            if (!_dragStart.HasValue) return;
            var start = _dragStart.Value;
            _dragStart = null;
            var preview = Plan(start, cell, out var order);
            if (Tool == BuildTool.Item && start == cell)
            {
                Send(preview, order);
                return;
            }
            _pendingOrder = order;
            _pendingPreview = preview;
        }

        // Draws from one cell to another and leaves the order waiting for Confirm, as a mouse drag does.
        public void Drag((int X, int Z) from, (int X, int Z) to)
        {
            PressAt(from);
            if (_dragStart.HasValue) ReleaseAt(to);
        }

        public void Confirm()
        {
            if (_pendingOrder == null) return;
            var order = _pendingOrder;
            var preview = _pendingPreview;
            _pendingOrder = null;
            _pendingPreview = null;
            Send(preview, order);
        }

        // Esc: drops an unconfirmed order or drag; true when there was one (so build mode stays open).
        public bool CancelPending()
        {
            var had = _pendingOrder != null || _dragStart.HasValue;
            _pendingOrder = null;
            _pendingPreview = null;
            _dragStart = null;
            return had;
        }

        // Right click: sells the piece under the pointer (topmost layer first) or removes the door, window or interior wall there.
        public void RemoveAt((int X, int Z) cell)
        {
            var preview = RemovalPlan(cell, out var order);
            Send(preview, order);
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
            }
        }

        // What the active tool would order between two cells (the same cell for a click), and its preview.
        private BuildPreview Plan((int X, int Z) from, (int X, int Z) to, out object order)
        {
            order = null;
            var site = session.ClientSite;
            var preview = new BuildPreview();
            var restaurant = Restaurant;
            if (Tool == BuildTool.Sell) return RemovalPlan(to, out order);
            if (Tool == BuildTool.Item) return ItemPlan(from, to, out order);
            if (restaurant == null)
            {
                preview.Problem = "no-restaurant";
                return preview;
            }
            var shell = new ShellOrder { BuildingId = restaurant.Id, Style = Style };
            switch (Tool)
            {
                case BuildTool.Resize:
                    shell.Kind = ShellOrder.Resize;
                    shell.X = Math.Min(from.X, to.X);
                    shell.Z = Math.Min(from.Z, to.Z);
                    shell.Width = Math.Abs(to.X - from.X) + 1;
                    shell.Depth = Math.Abs(to.Z - from.Z) + 1;
                    preview.Label = $"Shell {shell.Width} x {shell.Depth}";
                    break;
                case BuildTool.Wall:
                {
                    shell.Kind = ShellOrder.Partition;
                    var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Z - from.Z);
                    var length = alongX ? Math.Abs(to.X - from.X) : Math.Abs(to.Z - from.Z);
                    for (var step = 0; step <= length; step++)
                        shell.Cells.Add(alongX
                            ? new GridCell { X = from.X + Math.Sign(to.X - from.X) * step, Z = from.Z }
                            : new GridCell { X = from.X, Z = from.Z + Math.Sign(to.Z - from.Z) * step });
                    preview.Label = $"{shell.Cells.Count} m of {Name(Style)} wall";
                    break;
                }
                case BuildTool.Door:
                    shell.Kind = ShellOrder.Door;
                    shell.X = to.X;
                    shell.Z = to.Z;
                    preview.Label = $"{Name(Style)} door";
                    break;
                case BuildTool.Window:
                    shell.Kind = ShellOrder.Window;
                    shell.X = to.X;
                    shell.Z = to.Z;
                    // On the perimeter the window follows its wall; on an interior wall R picks the direction.
                    shell.Axis = to.Z == restaurant.CellZ || to.Z == restaurant.CellZ + restaurant.Depth - 1 ? 0
                        : to.X == restaurant.CellX || to.X == restaurant.CellX + restaurant.Width - 1 ? 1 : Rotation % 2;
                    preview.Label = $"{Name(Style)} window";
                    break;
                case BuildTool.Finish:
                    shell.Kind = ShellOrder.WallFinish;
                    preview.Label = $"{Name(Style)} outer walls";
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
                case ShellOrder.WallFinish:
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
                furnish.Placements.Add(new GridPlacement { X = stepX > 0 ? x : x - width + 1, Z = stepZ > 0 ? z : z - depth + 1, Rotation = Rotation });
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

        // The sale of the piece under a cell (tabletop, wall, ceiling, object, then floor layer) or the removal of the door,
        // window or interior wall there.
        private BuildPreview RemovalPlan((int X, int Z) cell, out object order)
        {
            order = null;
            var preview = new BuildPreview { Removal = true };
            var site = session.ClientSite;
            var layers = new[] { SiteGrid.TabletopLayer, SiteGrid.WallLayer, SiteGrid.CeilingLayer, SiteGrid.ObjectLayer, SiteGrid.FloorLayer };
            var piece = site.Equipment.Where(x => x.SiteId == session.ClientSiteId && x.State == EquipmentState.Placed && x.Level == 0
                    && SiteGrid.Contains(x, cell.X, cell.Z, 1, 1))
                .OrderBy(x => Array.IndexOf(layers, x.Layer ?? "")).FirstOrDefault();
            if (piece != null)
            {
                var definition = session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == piece.Kind);
                preview.RefundCents = piece.ChargedCents;
                preview.Label = $"Sell {definition?.DisplayName ?? piece.Kind}";
                var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
                for (var x = piece.CellX; x < piece.CellX + width; x++)
                for (var z = piece.CellZ; z < piece.CellZ + depth; z++)
                    preview.Cells.Add((x, z));
                if (site.Customers.Any(x => x.CounterId == piece.Id || x.TableId == piece.Id)) preview.Problem = "occupied";
                order = piece.Id;
                return preview;
            }
            var restaurant = Restaurant;
            if (restaurant != null && (SiteGrid.WindowAt(restaurant, cell.X, cell.Z) != null || SiteGrid.IsDoor(restaurant, cell.X, cell.Z)
                    || SiteGrid.IsPartition(restaurant, cell.X, cell.Z)))
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

        private (int X, int Z)? PointerCell(SiteLayout layout)
        {
            var rig = buildings.LocalAvatar != null ? buildings.LocalAvatar.CameraRig : null;
            var camera = rig != null ? rig.GetComponentInChildren<Camera>() : null;
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
            var offer = shown && Tool == BuildTool.Item && _pendingOrder == null && _hover.HasValue
                ? session.Offers.FirstOrDefault(x => x != null && x.Id == OfferId && x.Equipment != null) : null;
            ShowGhost(offer?.Equipment);
            if (_ghost == null || offer == null) return;
            var (width, depth) = SiteGrid.Footprint(offer.Equipment.Width, offer.Equipment.Depth, Rotation);
            var anchor = _dragStart ?? _hover.Value;
            var rotation = SiteGridSpace.Rotation(Rotation);
            var center = SiteGridSpace.FootprintCenter(layout, anchor.X, anchor.Z, width, depth) + EquipmentModel.MountOffset(offer.Equipment, rotation);
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

        // ---- Panel (UI Toolkit, built in code) ----

        private void BuildPanel()
        {
            if (document == null) return;
            var root = document.rootVisualElement;
            root.Clear();
            _window = new VisualElement { name = "build" };
            // On the right, below the HUD's cash readout and clear of the session readout (top left) and the hotbar.
            _window.style.position = Position.Absolute;
            _window.style.right = 10;
            _window.style.top = 60;
            _window.style.bottom = 10;
            _window.style.width = 320;
            _window.style.backgroundColor = Backdrop;
            _window.style.paddingLeft = _window.style.paddingRight = _window.style.paddingTop = _window.style.paddingBottom = 8;
            var title = Caption("Build", 17, Heading);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _window.Add(title);
            _cash = Caption("", 12, Muted, 2);
            _cash.name = "build-cash";
            _window.Add(_cash);
            _tools = new VisualElement { name = "build-tools" };
            _tools.style.flexDirection = FlexDirection.Row;
            _tools.style.flexWrap = Wrap.Wrap;
            _tools.style.marginTop = 6;
            _window.Add(_tools);
            _styles = new VisualElement { name = "build-styles" };
            _styles.style.flexDirection = FlexDirection.Row;
            _styles.style.flexWrap = Wrap.Wrap;
            _window.Add(_styles);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "build-catalog-scroll" };
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 6;
            _catalog = new VisualElement { name = "build-catalog" };
            scroll.Add(_catalog);
            _window.Add(scroll);
            _preview = Caption(" ", 13, Color.white, 6);
            _preview.name = "build-preview";
            _window.Add(_preview);
            _status = Caption(" ", 12, Error, 2);
            _status.name = "build-status";
            _window.Add(_status);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
            _confirm = new Button(Confirm) { name = "build-confirm", text = "Confirm", focusable = false };
            buttons.Add(_confirm);
            buttons.Add(new Button(() => CancelPending()) { name = "build-cancel", text = "Cancel", focusable = false });
            buttons.Add(new Button(Toggle) { name = "build-leave", text = "Leave", focusable = false });
            _window.Add(buttons);
            // Rows keep their height in the fixed-height column; only the catalog scrolls.
            foreach (var child in _window.Children()) child.style.flexShrink = child is ScrollView ? 1 : 0;
            root.Add(_window);
            _window.style.display = DisplayStyle.None;
        }

        private void RefreshPanel()
        {
            if (_window == null) return;
            var site = session.ClientSite;
            var company = site.Companies.FirstOrDefault(x => x.SiteIds.Contains(session.ClientSiteId));
            _cash.text = $"Company cash {PlayerHud.FormatCash(company?.Cash ?? 0)}   Ambience {RestaurantRules.Ambience(site, session.ClientSiteId)}/{RestaurantRules.AmbienceCap}";
            var key = $"{Tool}|{Style}|{OfferId}";
            if (_shownTools != key)
            {
                _shownTools = key;
                FillTools();
            }
            if (_catalogVersion != session.Offers.Count)
            {
                _catalogVersion = session.Offers.Count;
                FillCatalog();
            }
            var preview = Preview;
            var money = preview.ChargeCents == 0 && preview.RefundCents == 0 ? "free"
                : $"charge {PlayerHud.FormatCash(preview.ChargeCents)}, refund {PlayerHud.FormatCash(preview.RefundCents)}, net {(preview.NetCents < 0 ? "+" : "-")}{PlayerHud.FormatCash(Math.Abs(preview.NetCents))}";
            _preview.text = string.IsNullOrEmpty(preview.Label) && preview.Problem == null ? " "
                : $"{preview.Label}: {money}" + (preview.Problem != null ? $"  [{preview.Problem}]" : HasPending ? "  (Confirm or Esc)" : "");
            _preview.style.color = preview.Problem != null ? Error : Color.white;
            _confirm.SetEnabled(HasPending && _pendingPreview?.Problem == null);
            _status.text = HasPendingRequests ? "Waiting for the server..." : string.IsNullOrEmpty(LastRejection) ? " " : $"Refused: {LastRejection}";
        }

        private void FillTools()
        {
            _tools.Clear();
            void ToolButton(BuildTool tool, string text)
            {
                var button = new Button(() => SelectTool(tool)) { name = $"build-tool-{tool.ToString().ToLowerInvariant()}", text = text, focusable = false };
                if (Tool == tool) button.style.color = Chosen;
                _tools.Add(button);
            }
            ToolButton(BuildTool.Sell, "Sell / remove");
            ToolButton(BuildTool.Resize, "Resize shell");
            ToolButton(BuildTool.Wall, "Wall");
            ToolButton(BuildTool.Door, "Door");
            ToolButton(BuildTool.Window, "Window");
            ToolButton(BuildTool.Finish, "Wall finish");
            _styles.Clear();
            var styles = Tool switch
            {
                BuildTool.Wall or BuildTool.Finish => GoodsWorld.WallStyles,
                BuildTool.Door => GoodsWorld.DoorStyles,
                BuildTool.Window => GoodsWorld.WindowStyles,
                _ => Array.Empty<string>()
            };
            foreach (var style in styles)
            {
                var chosen = style;
                var button = new Button(() => SelectTool(Tool, chosen)) { name = $"build-style-{style}", text = Name(style), focusable = false };
                if (Style == style) button.style.color = Chosen;
                _styles.Add(button);
            }
            foreach (var row in _catalog.Query<VisualElement>(className: "build-item").ToList())
                row.style.backgroundColor = (string)row.userData == OfferId ? new Color(0.35f, 0.3f, 0.2f, 1f) : Color.clear;
        }

        private void FillCatalog()
        {
            _catalog.Clear();
            var offers = session.Offers.Where(x => x != null && x.Equipment != null && x.Truck == null).ToList();
            foreach (var section in Sections.Append(EquipmentSection))
            {
                var inSection = offers.Where(x => (string.IsNullOrEmpty(x.Equipment.Category) ? EquipmentSection : x.Equipment.Category) == section).ToList();
                if (inSection.Count == 0) continue;
                var heading = Caption(section, 13, Heading, 8);
                heading.style.unityFontStyleAndWeight = FontStyle.Bold;
                _catalog.Add(heading);
                foreach (var offer in inSection)
                {
                    var row = new VisualElement { name = $"build-item-{offer.Id}", userData = offer.Id };
                    row.AddToClassList("build-item");
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 2;
                    var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                    icon.style.width = icon.style.height = 28;
                    if (offer.Equipment.Icon != null) icon.style.backgroundImage = new StyleBackground(offer.Equipment.Icon);
                    row.Add(icon);
                    var label = Caption($"{offer.Equipment.DisplayName}  {PlayerHud.FormatCash(offer.PriceCents)}", 12, Color.white);
                    label.style.flexGrow = 1;
                    label.style.marginLeft = 6;
                    row.Add(label);
                    var id = offer.Id;
                    row.Add(new Button(() => SelectOffer(id)) { name = $"build-pick-{id}", text = "Place", focusable = false });
                    _catalog.Add(row);
                }
            }
            _shownTools = null;
        }

        private static Label Caption(string text, int size, Color color, int marginTop = 0)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = marginTop;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
