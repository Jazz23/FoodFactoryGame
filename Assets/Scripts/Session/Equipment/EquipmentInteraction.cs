// Local player's Factorio-style controls. The gameplay cursor is locked to a centre crosshair so Look always orbits;
// opening a screen (E inventory, or clicking a machine) frees the pointer and suspends orbiting. Hotbar keys (or picking a
// machine out of the inventory grid) put a held machine kind on the cursor, which shows a see-through ghost of the machine
// on its footprint; left click places it (or opens the machine under the crosshair when the cursor is empty) and right click
// picks the machine under the crosshair back into the inventory. On a screen the cursor can instead carry a goods stack
// (CursorGoods), which is only a pointer to lots still in their container until it is dropped on another one. Capacity
// counts slots (decision 0009): moves are previewed against the destination's free slots with the items' max stacks.
// Every change is a request through the goods bridge: the ghost, cursor stack and capacity hints are only previews, the
// server re-checks each request, and nothing changes locally until the next replicated baseline.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    public enum InteractionScreen
    {
        None,
        Inventory,
        Machine
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

    [DisallowMultipleComponent]
    public sealed class EquipmentInteraction : MonoBehaviour
    {
        public const int HotbarSize = 9;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private SessionRoot session;
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
        [SerializeField] private Color validColor = new(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color invalidColor = new(0.9f, 0.2f, 0.15f, 1f);
        [SerializeField] private float maximumRayDistance = 100f;

        private readonly HashSet<string> _pending = new();
        private ClientSiteSubscription _subscription;
        private MaterialPropertyBlock _block;
        private Camera _camera;
        private OrbitCameraRig _rig;
        private GoodsEquipment _held;
        private (int X, int Z) _target;
        private int _rotation;
        private bool _released;
        private bool? _appliedLock;
        // Set when a screen opens and cleared once Place is seen released, so the click that opened a machine screen is
        // never also taken by the slot that appears under the pointer.
        private bool _awaitingRelease;
        private int _releasedFrame = -1;
        private GameObject _ghostModel;
        private string _ghostKind;
        private Renderer[] _ghostRenderers = Array.Empty<Renderer>();

        public InteractionScreen Screen { get; private set; }
        // Goods stack on the cursor while a screen is open; null when the cursor carries no goods.
        public CursorStack CursorGoods { get; private set; }
        public bool GhostVisible => _ghostModel != null && _ghostModel.activeSelf;
        // Pointer position in screen pixels (origin bottom left), for drawing the cursor stack.
        public Vector2 PointerPosition => pointAction.action.ReadValue<Vector2>();
        // Equipment ID of the open machine screen; null unless Screen is Machine.
        public string OpenMachineId { get; private set; }
        // Machine kind on the cursor; null when the cursor is empty.
        public string CursorKind { get; private set; }
        public int SelectedSlot { get; private set; } = -1;
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
        public string InventoryId => LocalPlayerId == null ? null : GoodsWorld.InventoryLocationId(LocalPlayerId);

        // Hotbar slots point to machine kinds, in the order of the session's equipment content.
        public IReadOnlyList<string> HotbarKinds => session.EquipmentDefinitions.Where(x => x != null).Take(HotbarSize).Select(x => x.Kind).ToList();

        private IEnumerable<InputActionReference> Actions => new[]
        {
            placeAction, removeAction, rotateAction, pointAction, inventoryAction, clearCursorAction, closeScreenAction, hotbarAction,
            quickTransferAction
        };

        private void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            placeAction.action.performed += OnPlace;
            removeAction.action.performed += OnRemove;
            rotateAction.action.performed += OnRotate;
            inventoryAction.action.performed += OnInventory;
            clearCursorAction.action.performed += OnClearCursor;
            closeScreenAction.action.performed += OnCloseScreen;
            hotbarAction.action.performed += OnHotbar;
            foreach (var action in Actions) action.action.Enable();
            ghost.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            placeAction.action.performed -= OnPlace;
            removeAction.action.performed -= OnRemove;
            rotateAction.action.performed -= OnRotate;
            inventoryAction.action.performed -= OnInventory;
            clearCursorAction.action.performed -= OnClearCursor;
            closeScreenAction.action.performed -= OnCloseScreen;
            hotbarAction.action.performed -= OnHotbar;
            foreach (var action in Actions) action.action.Disable();
            Subscribe(null);
            ApplyPointerLock(false);
            ShowGhostModel(null);
        }

        private void Update()
        {
            Subscribe(session.ClientSubscription);
            var site = session.ClientSite;
            var me = LocalPlayerId;
            _rig = LocalRig();
            _camera = _rig == null ? null : _rig.GetComponentInChildren<Camera>();
            if (_camera == null || site == null) CloseScreen();
            if (Screen == InteractionScreen.Machine
                && site?.Equipment.Any(x => x.Id == OpenMachineId && x.State == EquipmentState.Placed) != true)
                CloseScreen();
            if (_awaitingRelease && !placeAction.action.IsPressed())
            {
                _awaitingRelease = false;
                _releasedFrame = Time.frameCount;
            }

            // The cursor keeps its kind until none of that kind is held (after the last one is placed or before any arrives).
            _held = CursorKind == null ? null : site?.Equipment
                .Where(x => x.State == EquipmentState.Held && x.HolderId == me && x.Kind == CursorKind)
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (CursorKind != null && _held == null && site != null && !HasPendingRequests) ClearCursor();
            // A carried stack empties once its lots are gone from the container (moved, consumed or the machine picked up).
            if (CursorGoods != null && (Screen == InteractionScreen.None || site == null || !CursorLots(site).Any())) CursorGoods = null;

            ApplyPointerLock(_camera != null && Screen == InteractionScreen.None && !_released);
            if (_rig != null) _rig.OrbitEnabled = PointerLocked;

            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var suffix = HasPendingRequests ? " (waiting for server)" : !string.IsNullOrEmpty(LastRejection) ? $" (rejected: {LastRejection})" : "";
            if (_held == null || layout == null || _camera == null || Screen != InteractionScreen.None || !TryFloorPoint(out var point))
            {
                ghost.gameObject.SetActive(false);
                if (_ghostModel != null) _ghostModel.SetActive(false);
                Status = site == null ? "" : Screen switch
                {
                    InteractionScreen.Inventory => "Inventory: click a slot to pick up or put down, shift+click to move a stack across; E or Esc closes" + suffix,
                    InteractionScreen.Machine => "Machine: put ingredients in the input, take results from the output (shift+click moves a stack); E or Esc closes" + suffix,
                    _ => _released ? "Cursor released: click to resume" + suffix
                        : "E: inventory, 1-9: hotbar, left click: open machine, right click: pick up" + suffix
                };
                return;
            }
            var (width, depth) = SiteGrid.Footprint(_held.Width, _held.Depth, _rotation);
            _target = SiteGridSpace.AnchorAt(layout, point, width, depth);
            var problem = SiteGrid.PlacementProblem(site, _held, _target.X, _target.Z, _rotation);
            var center = SiteGridSpace.FootprintCenter(layout, _target.X, _target.Z, width, depth);
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
            Status = $"Cursor: {_held.Kind}: left click places, R rotates, Q clears" + (problem != null ? $" [{problem}]" : "") + suffix;
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
            var index = HotbarKinds.ToList().IndexOf(kind);
            if (index < 0 || HeldCount(kind) == 0) return;
            CursorGoods = null;
            SelectedSlot = index;
            CursorKind = kind;
            LastRejection = null;
        }

        public int HeldCount(string kind)
        {
            var site = session.ClientSite;
            var me = LocalPlayerId;
            return site == null ? 0 : site.Equipment.Count(x => x.State == EquipmentState.Held && x.HolderId == me && x.Kind == kind);
        }

        // Selecting a slot you hold at least one of puts that kind on the cursor; selecting the active slot again clears it.
        public void SelectSlot(int index)
        {
            var kinds = HotbarKinds;
            if (index < 0 || index >= kinds.Count || index == SelectedSlot && CursorKind != null)
            {
                ClearCursor();
                return;
            }
            if (HeldCount(kinds[index]) == 0)
            {
                LastRejection = $"no {kinds[index]} in inventory";
                return;
            }
            PickUpMachine(kinds[index]);
        }

        public void ClearCursor()
        {
            CursorKind = null;
            CursorGoods = null;
            SelectedSlot = -1;
            _held = null;
        }

        public void ToggleInventory()
        {
            if (Screen == InteractionScreen.None && _camera != null && session.ClientSite != null)
            {
                Screen = InteractionScreen.Inventory;
                _awaitingRelease = true;
            }
            else CloseScreen();
        }

        public void OpenMachine(string equipmentId)
        {
            if (_camera == null || session.ClientSite?.Equipment.Any(x => x.Id == equipmentId && x.State == EquipmentState.Placed) != true)
                return;
            Screen = InteractionScreen.Machine;
            OpenMachineId = equipmentId;
            _awaitingRelease = true;
        }

        // Closing a screen empties a carried goods stack (its lots never left their container); a machine stays on the cursor.
        public void CloseScreen()
        {
            Screen = InteractionScreen.None;
            OpenMachineId = null;
            CursorGoods = null;
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

        private void OnPlace(InputAction.CallbackContext _)
        {
            if (Screen != InteractionScreen.None || _camera == null) return;
            if (_released)
            {
                _released = false;
                return;
            }
            var bridge = _subscription?.Bridge;
            if (bridge == null) return;
            if (_held == null)
            {
                var visual = EquipmentUnderCrosshair();
                if (visual != null) OpenMachine(visual.EquipmentId);
                return;
            }
            if (!ghost.gameObject.activeSelf || HasPendingRequests) return;
            Debug.Log($"[Equipment] Requesting placement of {_held.Id} at ({_target.X}, {_target.Z}) rotation {_rotation}.");
            bridge.RequestPlace(Track(), _held.Id, _target.X, _target.Z, _rotation);
        }

        private void OnRemove(InputAction.CallbackContext _)
        {
            var bridge = _subscription?.Bridge;
            if (Screen != InteractionScreen.None || _released || bridge == null || _camera == null) return;
            var visual = EquipmentUnderCrosshair();
            if (visual == null)
            {
                LastRejection = "nothing-under-cursor";
                return;
            }
            Debug.Log($"[Equipment] Requesting pickup of {visual.EquipmentId}.");
            bridge.RequestPickUp(Track(), visual.EquipmentId);
        }

        private void OnRotate(InputAction.CallbackContext _) => _rotation = (_rotation + 1) % 4;

        private void OnInventory(InputAction.CallbackContext _) => ToggleInventory();

        private void OnClearCursor(InputAction.CallbackContext _) => ClearCursor();

        // Esc closes an open screen; otherwise it releases the pointer (so the window can be left) until the next click.
        private void OnCloseScreen(InputAction.CallbackContext _)
        {
            if (Screen != InteractionScreen.None) CloseScreen();
            else if (_camera != null) _released = !_released;
        }

        // Each Hotbar binding scales its key press to the slot number (1-9); a release reads 0.
        private void OnHotbar(InputAction.CallbackContext context)
        {
            var slot = Mathf.RoundToInt(context.ReadValue<float>());
            if (slot >= 1 && slot <= HotbarSize) SelectSlot(slot - 1);
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
        private EquipmentVisual EquipmentUnderCrosshair()
        {
            var hit = Physics.RaycastAll(AimRay(), maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(x => x.collider.GetComponentInParent<PlayerAvatar>() == null)
                .OrderBy(x => x.distance).FirstOrDefault();
            return hit.collider == null ? null : hit.collider.GetComponentInParent<EquipmentVisual>();
        }

        private bool TryFloorPoint(out Vector3 point)
        {
            var ray = AimRay();
            point = default;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance) || distance > maximumRayDistance) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        // The owning client's avatar rig is the only enabled camera for this connection.
        private OrbitCameraRig LocalRig()
        {
            var manager = session.NetworkManager;
            if (!manager.IsClientStarted) return null;
            var avatar = manager.ClientManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<PlayerAvatar>()).FirstOrDefault(x => x != null && x.IsOwner);
            return avatar == null ? null : avatar.CameraRig;
        }
    }
}
