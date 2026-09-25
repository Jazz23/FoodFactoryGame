// Local player's Factorio-style controls. The gameplay cursor is locked to a centre crosshair so Look always orbits (the
// top-down camera view instead leaves the pointer free and aims where it points);
// opening a screen (E, or clicking a machine) frees the pointer and suspends orbiting. E opens the screen of whatever
// openable thing the crosshair is on (EquipmentInteraction.Hover.cs), or the inventory when it is on nothing openable. Hotbar slots point to a
// machine kind or an item, assigned on a screen (a hotbar key over a stack, or dropping the cursor on a hotbar slot); this
// client's arrangement only, never saved or sent. Hotbar keys (or picking a machine out of the inventory grid) put a held
// machine kind or the inventory's stack of an item on the cursor; a machine shows a see-through ghost of the machine
// on its footprint; left click places it (or opens the highlighted machine or storage when the cursor is empty) and right click
// picks the machine under the crosshair back into the inventory. On a screen the cursor can instead carry a goods stack
// (CursorGoods), which is only a pointer to lots still in their container until it is dropped on another one. Capacity
// counts slots (decision 0009): moves are previewed against the destination's free slots with the items' max stacks.
// Every change is a request through the goods bridge: the ghost, cursor stack and capacity hints are only previews, the
// server re-checks each request, and nothing changes locally until the next replicated baseline.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    public enum InteractionScreen
    {
        None,
        Inventory,
        Machine,
        // An employee's script screen (EquipmentInteraction.Employees.cs, EmployeeScriptPanel).
        Employee,
        // The employee script screen hidden while the player picks a cell or machine in the world to insert into the script
        // (EquipmentInteraction.Employees.cs); the avatar can walk and look, and a click or Esc returns to the script.
        PickPosition,
        // Trucks, routes and the company's remote sites (decision 0022, LogisticsPanel).
        Logistics
    }

    // One slot's goods stack (item and spoiled state, up to the item's max stack) in one container, carried on the cursor.
    // The lots stay where they are; Quantity is how many units a drop may move, and Slot names the HUD slot it came from.
    public sealed class CursorStack
    {
        public string LocationId;
        public string ItemId;
        public bool Spoiled;
        public int Quantity;
        public string Slot;
    }

    // One hotbar slot's target: a held machine kind or an item (the inventory's unspoiled stack of it).
    public sealed class HotbarEntry
    {
        public string MachineKind;
        public string ItemId;

        public static HotbarEntry Machine(string kind) => new() { MachineKind = kind };
        public static HotbarEntry Goods(string itemId) => new() { ItemId = itemId };
        public bool Matches(HotbarEntry other) => other != null && other.MachineKind == MachineKind && other.ItemId == ItemId;
    }

    [DisallowMultipleComponent]
    public sealed partial class EquipmentInteraction : MonoBehaviour
    {
        public const int HotbarSize = 9;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private SessionRoot session;
        // Knows the local avatar's building and level: placement and aiming use the floor the avatar stands on (decision 0020).
        [SerializeField] private BuildingPresenter buildings;
        // Flat footprint under the ghost, coloured by whether the placement preview passes.
        [SerializeField] private Renderer ghost;
        // Transparent material for the machine ghost; tinted per renderer with the valid/invalid colour at ghostAlpha.
        [SerializeField] private Material ghostModelMaterial;
        [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.45f;
        [SerializeField] private InputActionReference placeAction;
        [SerializeField] private InputActionReference removeAction;
        [SerializeField] private InputActionReference rotateAction;
        [SerializeField] private InputActionReference pointAction;
        [SerializeField] private InputActionReference inventoryAction;
        [SerializeField] private InputActionReference clearCursorAction;
        [SerializeField] private InputActionReference closeScreenAction;
        [SerializeField] private InputActionReference hotbarAction;
        // Held while clicking a slot to send its stack to the other open container instead of picking it up.
        [SerializeField] private InputActionReference quickTransferAction;
        // Opens and closes the logistics screen (trucks and remote sites, decision 0022).
        [SerializeField] private InputActionReference logisticsAction;
        [SerializeField] private Color validColor = new(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color invalidColor = new(0.9f, 0.2f, 0.15f, 1f);
        [SerializeField] private float maximumRayDistance = 100f;
        // Farthest the avatar may stand from a machine, employee or the storage (metres across the floor) to open it.
        [SerializeField] private float interactReach = 2.5f;

        private readonly HashSet<string> _pending = new();
        private readonly HotbarEntry[] _hotbar = new HotbarEntry[HotbarSize];
        private bool _hotbarSeeded;
        private ClientSiteSubscription _subscription;
        private MaterialPropertyBlock _block;
        private Camera _camera;
        private PlayerAvatar _avatar;
        private OrbitCameraRig _rig;
        private GoodsEquipment _held;
        private (int X, int Z) _target;
        private int _targetLevel;
        private int _rotation;
        private bool _released;
        private bool? _appliedLock;
        // Set when a screen opens and cleared once Place is seen released, so the click that opened a machine screen is
        // never also taken by the slot that appears under the pointer.
        private bool _awaitingRelease;
        private int _releasedFrame = -1;
        private GameObject _ghostModel;
        private string _ghostKind;
        private bool _openMachineSells;
        private bool _openMachineStores;
        private bool _openMachineTable;
        private bool _openMachineDock;
        private Renderer[] _ghostRenderers = Array.Empty<Renderer>();

        public InteractionScreen Screen { get; private set; }
        // True while the inventory screen also shows the dev storage; false on every other screen.
        public bool StorageOpen { get; private set; }
        // Goods stack on the cursor while a screen is open; null when the cursor carries no goods.
        public CursorStack CursorGoods { get; private set; }
        public bool GhostVisible => _ghostModel != null && _ghostModel.activeSelf;
        // Pointer position in screen pixels (origin bottom left), for drawing the cursor stack.
        public Vector2 PointerPosition => pointAction.action.ReadValue<Vector2>();
        // Equipment ID of the open machine screen; null unless Screen is Machine.
        public string OpenMachineId { get; private set; }
        // Machine kind on the cursor; null when the cursor is empty.
        public string CursorKind { get; private set; }
        // Hotbar slot whose machine or item is on the cursor; -1 when none is.
        public int SelectedSlot => HotbarIndex(CursorEntry);
        public bool PointerLocked => _appliedLock == true;
        public bool HasPendingRequests => _pending.Count > 0;
        public bool QuickTransferHeld => quickTransferAction.action.IsPressed();
        // True once the press that opened the screen was released on an earlier frame. The HUD samples it when a press
        // starts on the screen, so the opening press stays ignored however late the UI receives it.
        public bool ScreenClicksArmed => !_awaitingRelease && Time.frameCount > _releasedFrame;
        public string LastRejection { get; private set; }
        public string Status { get; private set; } = "";
        public GoodsEquipment Held => _held;
        public int Rotation => _rotation;
        public SessionRoot Session => session;
        public string LocalPlayerId => session.Authenticator.LocalPlayerId;
        // Floor the local avatar stands on; aims, previews and placement requests use it.
        public int Level => buildings.LocalLevel;
        public BuildingPresenter Buildings => buildings;
        public string InventoryId => LocalPlayerId == null ? null : GoodsWorld.InventoryLocationId(LocalPlayerId);

        // Hotbar slots (null when empty). Machine kinds fill them in the order of the session's equipment content once that is
        // loaded, and the player reassigns them from there.
        public IReadOnlyList<HotbarEntry> Hotbar
        {
            get
            {
                if (!_hotbarSeeded && session.EquipmentDefinitions.Any(x => x != null))
                {
                    _hotbarSeeded = true;
                    var kinds = session.EquipmentDefinitions.Where(x => x != null).Take(HotbarSize).Select(x => x.Kind).ToList();
                    for (var index = 0; index < kinds.Count; index++) _hotbar[index] = HotbarEntry.Machine(kinds[index]);
                }
                return _hotbar;
            }
        }

        // Set by the HUD: the machine or item of the stack under the pointer on an open screen (null when none), so a hotbar
        // key there assigns it to that slot instead of selecting the slot.
        public Func<HotbarEntry> HoveredEntry { get; set; }

        // The cursor's machine kind, or an unspoiled goods stack carried from the inventory.
        private HotbarEntry CursorEntry => CursorKind != null ? HotbarEntry.Machine(CursorKind)
            : CursorGoods != null && !CursorGoods.Spoiled && CursorGoods.LocationId == InventoryId ? HotbarEntry.Goods(CursorGoods.ItemId)
            : null;

        private IEnumerable<InputActionReference> Actions => new[]
        {
            placeAction, removeAction, rotateAction, pointAction, inventoryAction, clearCursorAction, closeScreenAction, hotbarAction,
            quickTransferAction, placeItemAction, takeItemAction, logisticsAction
        };

        private void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            placeAction.action.performed += OnPlace;
            removeAction.action.performed += OnRemove;
            rotateAction.action.performed += OnRotate;
            inventoryAction.action.performed += OnInventory;
            logisticsAction.action.performed += OnLogistics;
            clearCursorAction.action.performed += OnClearCursor;
            closeScreenAction.action.performed += OnCloseScreen;
            hotbarAction.action.performed += OnHotbar;
            placeItemAction.action.performed += OnPlaceItem;
            takeItemAction.action.performed += OnTakeItem;
            foreach (var action in Actions) action.action.Enable();
            EnableLiftInput();
            ghost.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            placeAction.action.performed -= OnPlace;
            removeAction.action.performed -= OnRemove;
            rotateAction.action.performed -= OnRotate;
            inventoryAction.action.performed -= OnInventory;
            logisticsAction.action.performed -= OnLogistics;
            clearCursorAction.action.performed -= OnClearCursor;
            closeScreenAction.action.performed -= OnCloseScreen;
            hotbarAction.action.performed -= OnHotbar;
            placeItemAction.action.performed -= OnPlaceItem;
            takeItemAction.action.performed -= OnTakeItem;
            DisableLiftInput();
            CloseEmployeeScreen();
            ClearHover();
            foreach (var action in Actions) action.action.Disable();
            Subscribe(null);
            ApplyPointerLock(false);
            ShowGhostModel(null);
            DestroyBeltGhosts();
            _pendingBelts.Clear();
            _removingBelts.Clear();
            _dragging = false;
        }

        private void Update()
        {
            Subscribe(session.ClientSubscription);
            var site = session.ClientSite;
            var me = LocalPlayerId;
            _avatar = LocalAvatar();
            _rig = _avatar == null ? null : _avatar.CameraRig;
            _camera = _rig == null ? null : _rig.GetComponentInChildren<Camera>();
            if (_camera == null || site == null) CloseScreen();
            if (Screen == InteractionScreen.Machine
                && site?.Equipment.Any(x => x.Id == OpenMachineId && x.State == EquipmentState.Placed) != true)
                CloseScreen();
            // Read the bound controls: the employee screen disables Place, and a disabled action never reports pressed.
            if (_awaitingRelease && !placeAction.action.controls.Any(x => x.IsPressed()))
            {
                _awaitingRelease = false;
                _releasedFrame = Time.frameCount;
            }

            // The cursor keeps its kind until none of that kind is held (after the last one is placed or before any arrives).
            _held = CursorKind == null ? null : site?.Equipment
                .Where(x => x.State == EquipmentState.Held && x.HolderId == me && x.Kind == CursorKind)
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (CursorKind != null && _held == null && site != null && !HasPendingRequests) ClearCursor();
            // A carried stack empties once its lots are gone from the container (moved, consumed, put on belts or the machine
            // picked up). Outside a screen only an inventory stack stays on the cursor (CloseScreen).
            if (CursorGoods != null && (site == null || !CursorLots(site).Any())) CursorGoods = null;

            // The top-down view does not orbit, so it aims with a free pointer instead of the centre crosshair.
            ApplyPointerLock(_camera != null && Screen is InteractionScreen.None or InteractionScreen.PickPosition && !_released && !_rig.TopDown);
            if (_rig != null) _rig.OrbitEnabled = PointerLocked;
            if (Screen is InteractionScreen.Employee or InteractionScreen.PickPosition && OpenEmployee == null) CloseScreen();
            UpdateHover();
            UpdatePick(site);

            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var suffix = HasPendingRequests ? " (waiting for server)" : !string.IsNullOrEmpty(LastRejection) ? $" (rejected: {LastRejection})" : "";
            if (UpdateBelts(site, layout, suffix))
            {
                ghost.gameObject.SetActive(false);
                if (_ghostModel != null) _ghostModel.SetActive(false);
                return;
            }
            if (_held == null || layout == null || _camera == null || Screen != InteractionScreen.None || !TryFloorPoint(out var point))
            {
                ghost.gameObject.SetActive(false);
                if (_ghostModel != null) _ghostModel.SetActive(false);
                Status = site == null ? "" : Screen switch
                {
                    InteractionScreen.Inventory => "Inventory: click a slot to pick up or put down, shift+click to move a stack across, 1-9 over a stack or dropping it on the hotbar assigns it there, Buy spends company cash at the supplier; E or Esc closes" + suffix,
                    // A sale station (decision 0013) has no results to take: it sells its input for the company.
                    InteractionScreen.Machine when _openMachineSells =>
                        "Counter: put edible goods in the input; customers queue here and buy them for the company (shift+click moves a stack); E or Esc closes" + suffix,
                    InteractionScreen.Machine when _openMachineTable =>
                        "Table: customers who dine in buy only once a seat is free, then sit here to eat; right click picks it up when nobody sits here; E or Esc closes" + suffix,
                    InteractionScreen.Machine when _openMachineDock =>
                        "Dock: put goods in Outgoing for trucks to load; take deliveries from Incoming (shift+click moves a stack); L: trucks and routes; E or Esc closes" + suffix,
                    InteractionScreen.Machine when _openMachineStores =>
                        "Storage: click or shift+click to move goods in and out; hover a stack to see when it spoils; E or Esc closes" + suffix,
                    InteractionScreen.Logistics => "Logistics: set each truck's route and cargo, and move stock at remote sites; L or Esc closes" + suffix,
                    InteractionScreen.Employee => "Employee: paste a Lua script and press Run; Stop halts it; Esc closes" + suffix,
                    InteractionScreen.PickPosition => "Select world pos: look at a cell or a machine (red) and click to insert it into the script; Esc returns"
                        + (PickText != null ? $" [{PickText}]" : ""),
                    InteractionScreen.Machine => "Machine: put ingredients in the input, take results from the output (shift+click moves a stack); E or Esc closes" + suffix,
                    _ => _released ? "Cursor released: click to resume" + suffix
                        : ElevatorHint() + HoverHint() + "E: inventory (pick belts or goods to carry them out), L: trucks, 1-9: hotbar, left click: open machine, right click: pick up, R: turn belt, F: take an item off a belt" + suffix
                };
                return;
            }
            var (width, depth) = SiteGrid.Footprint(_held.Width, _held.Depth, _rotation);
            _target = SiteGridSpace.AnchorAt(layout, point, width, depth);
            _targetLevel = Level;
            var problem = SiteGrid.PlacementProblem(site, _held, _target.X, _target.Z, _rotation, _targetLevel);
            var center = SiteGridSpace.FootprintCenter(layout, _target.X, _target.Z, width, depth, _targetLevel);
            var color = problem == null ? validColor : invalidColor;
            ghost.gameObject.SetActive(true);
            ghost.transform.SetPositionAndRotation(center + Vector3.up * 0.03f, Quaternion.identity);
            ghost.transform.localScale = new Vector3(width * SiteGrid.CellSize, 0.05f, depth * SiteGrid.CellSize);
            ghost.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, color);
            ghost.SetPropertyBlock(_block);
            ShowGhostModel(_held.Kind);
            if (_ghostModel != null)
            {
                _ghostModel.SetActive(true);
                _ghostModel.transform.SetPositionAndRotation(center, SiteGridSpace.Rotation(_rotation));
                color.a = ghostAlpha;
                foreach (var renderer in _ghostRenderers)
                {
                    renderer.GetPropertyBlock(_block);
                    _block.SetColor(BaseColor, color);
                    renderer.SetPropertyBlock(_block);
                }
            }
            Status = $"Cursor: {_held.Kind}: left click places, R rotates, X or Esc clears" + (problem != null ? $" [{problem}]" : "") + suffix;
        }

        // Keeps one ghost model for the cursor's machine kind; a kind without a visual shows only the footprint.
        private void ShowGhostModel(string kind)
        {
            if (kind == _ghostKind) return;
            if (_ghostModel != null) Destroy(_ghostModel);
            _ghostModel = null;
            _ghostRenderers = Array.Empty<Renderer>();
            _ghostKind = kind;
            var definition = kind == null ? null : session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind);
            if (definition == null || definition.VisualPrefab == null) return;
            _ghostModel = EquipmentModel.CreateGhost(definition, transform, ghostModelMaterial);
            _ghostRenderers = _ghostModel.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
        }

        public IEnumerable<GoodsLot> CursorLots(GoodsSnapshot site) => CursorGoods == null || site == null
            ? Enumerable.Empty<GoodsLot>()
            : site.Lots.Where(x => x.LocationId == CursorGoods.LocationId && x.ItemId == CursorGoods.ItemId && x.Spoiled == CursorGoods.Spoiled);

        // Puts one slot's goods stack on the cursor (replacing whatever it carried). Nothing moves until DropGoods.
        public void PickUpGoods(string locationId, string itemId, bool spoiled, int quantity, string slot = null)
        {
            ClearCursor();
            CursorGoods = new CursorStack { LocationId = locationId, ItemId = itemId, Spoiled = spoiled, Quantity = quantity, Slot = slot };
            if (quantity < 1 || !CursorLots(session.ClientSite).Any()) CursorGoods = null;
        }

        // Drops the cursor stack on another container: ordinary server-checked transfers of up to its quantity, as many as fit.
        public void DropGoods(string destinationId)
        {
            var stack = CursorGoods;
            CursorGoods = null;
            if (stack == null) return;
            TransferStack(stack.LocationId, stack.ItemId, stack.Spoiled, stack.Quantity, destinationId);
        }

        // Quick transfer (shift click) and drops: moves up to quantity units of one stack from a container to another,
        // limited to what the destination's free slots can take, so a partial fit moves just enough to fill it.
        public void TransferStack(string sourceId, string itemId, bool spoiled, int quantity, string destinationId)
        {
            if (sourceId == destinationId || session.ClientSite == null) return;
            MoveGoods(session.ClientSite.Lots.Where(x => x.LocationId == sourceId && x.ItemId == itemId && x.Spoiled == spoiled).ToList(),
                destinationId, quantity);
        }

        // Puts a held machine kind on the cursor, as the hotbar does; used when a machine is picked out of the inventory grid.
        public void PickUpMachine(string kind)
        {
            if (HeldCount(kind) == 0) return;
            CursorGoods = null;
            CursorKind = kind;
            LastRejection = null;
        }

        public int HeldCount(string kind)
        {
            var site = session.ClientSite;
            var me = LocalPlayerId;
            return site == null ? 0 : site.Equipment.Count(x => x.State == EquipmentState.Held && x.HolderId == me && x.Kind == kind);
        }

        // Units a hotbar entry stands for: held machines of the kind, or the inventory's unspoiled units of the item.
        public int HotbarCount(HotbarEntry entry)
        {
            if (entry == null) return 0;
            if (entry.MachineKind != null) return HeldCount(entry.MachineKind);
            var site = session.ClientSite;
            return site == null || InventoryId == null ? 0
                : site.Lots.Where(x => x.LocationId == InventoryId && x.ItemId == entry.ItemId && !x.Spoiled).Sum(x => x.Quantity);
        }

        // Selecting a slot whose machine or item you have puts it on the cursor (an item as one slot's worth of the inventory's
        // stack); selecting the active slot again, or an empty slot, clears it.
        public void SelectSlot(int index)
        {
            var entry = index >= 0 && index < HotbarSize ? Hotbar[index] : null;
            if (entry == null || index == SelectedSlot)
            {
                ClearCursor();
                return;
            }
            var count = HotbarCount(entry);
            if (count == 0)
            {
                LastRejection = $"no {entry.MachineKind ?? entry.ItemId} in inventory";
                return;
            }
            if (entry.MachineKind != null) PickUpMachine(entry.MachineKind);
            else PickUpGoods(InventoryId, entry.ItemId, false, Math.Min(session.MaxStack(entry.ItemId), count), entry.ItemId + "#0");
        }

        // Points a hotbar slot at a machine kind or item, taking it off any other slot it was on.
        public void AssignHotbar(int index, HotbarEntry entry)
        {
            if (index < 0 || index >= HotbarSize || entry == null) return;
            var slots = Hotbar;
            for (var other = 0; other < HotbarSize; other++)
                if (entry.Matches(slots[other])) _hotbar[other] = null;
            _hotbar[index] = entry;
        }

        public int HotbarIndex(HotbarEntry entry)
        {
            if (entry == null) return -1;
            var slots = Hotbar;
            for (var index = 0; index < HotbarSize; index++)
                if (entry.Matches(slots[index])) return index;
            return -1;
        }

        public void ClearCursor()
        {
            CursorKind = null;
            CursorGoods = null;
            _held = null;
        }

        // Opens the inventory on its own, or closes any open screen.
        public void ToggleInventory()
        {
            if (Screen == InteractionScreen.None) OpenInventory(false);
            else CloseScreen();
        }

        // Opens the inventory beside the dev storage wherever the avatar stands (the screen E opens on the storage).
        public void OpenStorage() => OpenInventory(true);

        private void OpenInventory(bool storage)
        {
            if (Screen != InteractionScreen.None || _camera == null || session.ClientSite == null) return;
            Screen = InteractionScreen.Inventory;
            StorageOpen = storage;
            _awaitingRelease = true;
        }

        // The logistics screen (decision 0022) opens from the world like the inventory and closes with L or Esc.
        public void ToggleLogistics()
        {
            if (Screen == InteractionScreen.None && _camera != null && session.ClientSite != null)
            {
                Screen = InteractionScreen.Logistics;
                _awaitingRelease = true;
            }
            else if (Screen == InteractionScreen.Logistics) CloseScreen();
        }

        public void OpenMachine(string equipmentId)
        {
            if (_camera == null || session.ClientSite?.Equipment.Any(x => x.Id == equipmentId && x.State == EquipmentState.Placed) != true)
                return;
            Screen = InteractionScreen.Machine;
            OpenMachineId = equipmentId;
            // Decided once per opening rather than every frame: a sale station (decision 0013) gets the counter hint.
            var kind = session.ClientSite.Equipment.First(x => x.Id == equipmentId).Kind;
            _openMachineSells = session.Recipes.Any(x => x != null && x.IsSale && x.StationKind == kind);
            // A machine with no recipes is storage (the fridge, decision 0018) and gets the storage hint.
            _openMachineStores = !session.Recipes.Any(x => x != null && x.StationKind == kind);
            _openMachineDock = kind == GoodsWorld.DockKind;
            _openMachineTable = kind == GoodsWorld.TableKind;
            _awaitingRelease = true;
        }

        // Closing a screen keeps a stack carried from the inventory on the cursor (for belts and placing goods on belts) and
        // empties one carried from any other container; its lots never left their container. A machine stays on the cursor.
        public void CloseScreen()
        {
            Screen = InteractionScreen.None;
            StorageOpen = false;
            OpenMachineId = null;
            CloseEmployeeScreen();
            if (CursorGoods != null && CursorGoods.LocationId != InventoryId) CursorGoods = null;
        }

        // Moves units of one stack (lots of one item and spoiled state), most exposed first, until quantity or the room the
        // destination's slots have for that stack (per the latest baseline) runs out; a lot is split when only part is taken.
        // Each lot is its own transfer request; the server re-checks capacity, so a stale preview is only a rejection.
        public void MoveGoods(IReadOnlyList<GoodsLot> lots, string destinationId, int quantity)
        {
            var site = session.ClientSite;
            var bridge = _subscription?.Bridge;
            var destination = site?.Locations.FirstOrDefault(x => x.Id == destinationId);
            if (bridge == null || destination == null || lots.Count == 0) return;
            var free = Math.Min(quantity, GoodsSlots.FreeUnits(site, destinationId, lots[0].ItemId, lots[0].Spoiled, session.MaxStack));
            if (free <= 0)
            {
                LastRejection = "capacity";
                return;
            }
            foreach (var lot in lots.OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (free <= 0) break;
                var take = (int)Math.Min(free, lot.Quantity);
                free -= take;
                var requestId = Track();
                Debug.Log($"[Equipment] Requesting transfer of {take} {lot.ItemId} from {lot.LocationId} to {destinationId}.");
                bridge.RequestTransfer(requestId, lot.Id, destinationId, take);
            }
        }

        // Buys one pack of a supplier offer (decision 0014); the server checks funds and inventory room and replies with the
        // outcome. Nothing changes locally until the next baseline.
        public void Buy(string offerId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || string.IsNullOrEmpty(offerId)) return;
            LastRejection = null;
            // The subscribed site: the one whose balance and inventory this client shows.
            bridge.RequestPurchase(Track(), _subscription.SiteId, offerId);
        }

        private void OnPlace(InputAction.CallbackContext _)
        {
            if (Screen == InteractionScreen.PickPosition)
            {
                FinishPick();
                return;
            }
            if (Screen != InteractionScreen.None || _camera == null) return;
            if (_released)
            {
                _released = false;
                return;
            }
            var bridge = _subscription?.Bridge;
            if (_held == null && _hoveredEmployee != null)
            {
                OpenEmployeeScreen(_hoveredEmployee);
                return;
            }
            if (bridge == null || StartBeltDrag()) return;
            if (_held == null)
            {
                // Only the highlighted hover target (a machine or the storage within reach) opens.
                if (_hovered is EquipmentVisual visual && visual != null) OpenMachine(visual.EquipmentId);
                else if (_hovered is Employees.SiteLocationMarker marker && marker != null) OpenStorage();
                return;
            }
            if (!ghost.gameObject.activeSelf || HasPendingRequests) return;
            Debug.Log($"[Equipment] Requesting placement of {_held.Id} at ({_target.X}, {_target.Z}) level {_targetLevel} rotation {_rotation}.");
            bridge.RequestPlace(Track(), _held.Id, _target.X, _target.Z, _rotation, _targetLevel);
        }

        // Orders one more floor for the factory the local avatar stands in (decision 0020); the first one puts the elevator on
        // the avatar's cell. The company pays; the server checks the building, price and cell and replies with the outcome.
        public void AddFloor()
        {
            var bridge = _subscription?.Bridge;
            var building = buildings.LocalBuilding;
            if (bridge == null || building == null) return;
            LastRejection = null;
            var (x, z) = buildings.LocalCell;
            Debug.Log($"[Buildings] Requesting a floor for {building.Id} (elevator cell ({x}, {z})).");
            bridge.RequestAddFloor(Track(), building.Id, x, z);
        }

        private string ElevatorHint()
        {
            var building = buildings.LocalBuilding;
            if (building == null || !SiteGrid.IsShaft(building, buildings.LocalCell.X, buildings.LocalCell.Z)) return "";
            return $"Elevator (floor {Level + 1} of {building.Floors}): PgUp up, PgDn down. ";
        }

        private void OnRemove(InputAction.CallbackContext _)
        {
            var bridge = _subscription?.Bridge;
            if (Screen != InteractionScreen.None || _released || bridge == null || _camera == null) return;
            var visual = EquipmentUnderCrosshair();
            // Belts under the crosshair are taken up while Remove is held (UpdateBelts).
            if (visual == null && _aimBelt != null) return;
            if (visual == null)
            {
                LastRejection = "nothing-under-cursor";
                return;
            }
            Debug.Log($"[Equipment] Requesting pickup of {visual.EquipmentId}.");
            bridge.RequestPickUp(Track(), visual.EquipmentId);
        }

        private void OnRotate(InputAction.CallbackContext _)
        {
            if (!RotateBelts()) _rotation = (_rotation + 1) % 4;
        }

        private void OnInventory(InputAction.CallbackContext _) => Interact();

        private void OnLogistics(InputAction.CallbackContext _) => ToggleLogistics();

        private void OnClearCursor(InputAction.CallbackContext _) => ClearCursor();

        // Esc closes an open screen, else empties a non-empty cursor; otherwise it releases the pointer (so the window can be
        // left) until the next click.
        private void OnCloseScreen(InputAction.CallbackContext _)
        {
            if (Screen == InteractionScreen.PickPosition) CancelPick();
            else if (Screen != InteractionScreen.None) CloseScreen();
            else if (CursorKind != null || CursorGoods != null) ClearCursor();
            else if (_camera != null) _released = !_released;
        }

        // Each Hotbar binding scales its key press to the slot number (1-9); a release reads 0.
        private void OnHotbar(InputAction.CallbackContext context) => PressHotbar(Mathf.RoundToInt(context.ReadValue<float>()));

        // A hotbar key (1-9): over a stack on an open screen it assigns that stack's machine or item to the slot; otherwise it
        // selects the slot.
        public void PressHotbar(int number)
        {
            if (number < 1 || number > HotbarSize) return;
            var hovered = Screen != InteractionScreen.None ? HoveredEntry?.Invoke() : null;
            if (hovered != null) AssignHotbar(number - 1, hovered);
            else SelectSlot(number - 1);
        }

        private string Track()
        {
            var requestId = Guid.NewGuid().ToString("N");
            _pending.Add(requestId);
            return requestId;
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!_pending.Remove(outcome.RequestId)) return;
            OnBeltResult(outcome);
            if (outcome.Accepted && _pending.Count == 0) LastRejection = null;
            if (!outcome.Accepted) LastRejection = string.IsNullOrEmpty(outcome.Reason) ? "rejected" : outcome.Reason;
            Debug.Log($"[Equipment] {(outcome.Accepted ? "Accepted" : "Rejected")}: {outcome.Reason} (revision {outcome.Revision}).");
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            _pending.Clear();
            _pendingBelts.Clear();
            _removingBelts.Clear();
        }

        private void ApplyPointerLock(bool locked)
        {
            if (_appliedLock == locked) return;
            _appliedLock = locked;
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        // A locked pointer aims through the crosshair at the screen centre; a released one uses the pointer position.
        private Ray AimRay() => _camera.ScreenPointToRay(PointerLocked
            ? new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f)
            : pointAction.action.ReadValue<Vector2>());

        // The nearest hit that is not a player avatar (the local CharacterController sits right beside the aim ray).
        private Collider UnderCrosshair() => Physics.RaycastAll(AimRay(), maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            .Where(x => x.collider.GetComponentInParent<PlayerAvatar>() == null)
            .OrderBy(x => x.distance).FirstOrDefault().collider;

        private EquipmentVisual EquipmentUnderCrosshair()
        {
            var hit = UnderCrosshair();
            return hit == null ? null : hit.GetComponentInParent<EquipmentVisual>();
        }

        private bool TryFloorPoint(out Vector3 point)
        {
            var ray = AimRay();
            point = default;
            var floor = new Plane(Vector3.up, Vector3.up * (Level * SiteGridSpace.LevelHeight));
            if (!floor.Raycast(ray, out var distance) || distance > maximumRayDistance) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        // The owning client's avatar; its rig is the only enabled camera for this connection.
        private PlayerAvatar LocalAvatar()
        {
            var manager = session.NetworkManager;
            if (!manager.IsClientStarted) return null;
            return manager.ClientManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<PlayerAvatar>()).FirstOrDefault(x => x != null && x.IsOwner);
        }
    }
}
