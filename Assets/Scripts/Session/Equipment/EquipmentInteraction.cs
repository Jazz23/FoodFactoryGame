// Local player's equipment input: Interact picks up the piece under the cursor; while holding one, a ghost footprint follows
// the cursor, Rotate turns it and Place requests placement. The ghost uses the server's grid rule only as a hint; the server
// re-checks every request, and nothing moves locally until the next replicated baseline arrives.
using System;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentInteraction : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private SessionRoot session;
        [SerializeField] private Renderer ghost;
        [SerializeField] private InputActionReference interactAction;
        [SerializeField] private InputActionReference placeAction;
        [SerializeField] private InputActionReference rotateAction;
        [SerializeField] private InputActionReference pointAction;
        [SerializeField] private Color validColor = new(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color invalidColor = new(0.9f, 0.2f, 0.15f, 1f);
        [SerializeField] private float maximumRayDistance = 100f;

        private ClientSiteSubscription _subscription;
        private MaterialPropertyBlock _block;
        private Camera _camera;
        private GoodsEquipment _held;
        private (int X, int Z) _target;
        private int _rotation;

        public string PendingRequestId { get; private set; }
        public string LastRejection { get; private set; }
        public string Status { get; private set; } = "";
        public GoodsEquipment Held => _held;
        public int Rotation => _rotation;

        private void OnEnable()
        {
            _block ??= new MaterialPropertyBlock();
            interactAction.action.performed += OnInteract;
            placeAction.action.performed += OnPlace;
            rotateAction.action.performed += OnRotate;
            foreach (var action in new[] { interactAction, placeAction, rotateAction, pointAction }) action.action.Enable();
            ghost.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            interactAction.action.performed -= OnInteract;
            placeAction.action.performed -= OnPlace;
            rotateAction.action.performed -= OnRotate;
            foreach (var action in new[] { interactAction, placeAction, rotateAction, pointAction }) action.action.Disable();
            Subscribe(null);
        }

        private void Update()
        {
            Subscribe(session.ClientSubscription);
            var site = session.ClientSite;
            var me = session.Authenticator.LocalPlayerId;
            _camera = LocalCamera();
            _held = site?.Equipment.FirstOrDefault(x => x.State == EquipmentState.Held && x.HolderId == me);
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var suffix = PendingRequestId != null ? " (waiting for server)" : LastRejection != null ? $" (rejected: {LastRejection})" : "";
            if (_held == null || layout == null || _camera == null || !TryFloorPoint(out var point))
            {
                ghost.gameObject.SetActive(false);
                Status = site == null ? "" : _held == null ? "E: pick up equipment under the cursor" + suffix : $"Holding {_held.Kind}" + suffix;
                return;
            }
            var (width, depth) = SiteGrid.Footprint(_held.Width, _held.Depth, _rotation);
            _target = SiteGridSpace.AnchorAt(layout, point, width, depth);
            var problem = SiteGrid.PlacementProblem(site, _held, _target.X, _target.Z, _rotation);
            ghost.gameObject.SetActive(true);
            ghost.transform.SetPositionAndRotation(SiteGridSpace.FootprintCenter(layout, _target.X, _target.Z, width, depth) + Vector3.up * 0.03f,
                Quaternion.identity);
            ghost.transform.localScale = new Vector3(width * SiteGrid.CellSize, 0.05f, depth * SiteGrid.CellSize);
            ghost.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, problem == null ? validColor : invalidColor);
            ghost.SetPropertyBlock(_block);
            Status = $"Holding {_held.Kind}: click to place, R to rotate" + (problem != null ? $" [{problem}]" : "") + suffix;
        }

        private void OnInteract(InputAction.CallbackContext _)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || _held != null || PendingRequestId != null || _camera == null)
            {
                Debug.Log($"[Equipment] Interact ignored: bridge={bridge != null}, holding={_held != null}, pending={PendingRequestId != null}, camera={_camera != null}.");
                return;
            }
            var pointer = pointAction.action.ReadValue<Vector2>();
            var ray = _camera.ScreenPointToRay(pointer);
            var visual = Physics.Raycast(ray, out var hit, maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.collider.GetComponentInParent<EquipmentVisual>() : null;
            if (visual == null)
            {
                LastRejection = "nothing-under-cursor";
                Debug.Log($"[Equipment] Interact at {pointer}: no equipment under the cursor.");
                return;
            }
            Debug.Log($"[Equipment] Requesting pickup of {visual.EquipmentId}.");
            PendingRequestId = NewRequestId();
            bridge.RequestPickUp(PendingRequestId, visual.EquipmentId);
        }

        private void OnPlace(InputAction.CallbackContext _)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || _held == null || PendingRequestId != null || !ghost.gameObject.activeSelf) return;
            PendingRequestId = NewRequestId();
            Debug.Log($"[Equipment] Requesting placement of {_held.Id} at ({_target.X}, {_target.Z}) rotation {_rotation}.");
            bridge.RequestPlace(PendingRequestId, _held.Id, _target.X, _target.Z, _rotation);
        }

        private void OnRotate(InputAction.CallbackContext _) => _rotation = (_rotation + 1) % 4;

        private void OnResult(GoodsOutcome outcome)
        {
            if (outcome.RequestId != PendingRequestId) return;
            PendingRequestId = null;
            LastRejection = outcome.Accepted ? null : outcome.Reason;
            Debug.Log($"[Equipment] {(outcome.Accepted ? "Accepted" : "Rejected")}: {outcome.Reason} (revision {outcome.Revision}).");
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            PendingRequestId = null;
        }

        private bool TryFloorPoint(out Vector3 point)
        {
            var ray = _camera.ScreenPointToRay(pointAction.action.ReadValue<Vector2>());
            point = default;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance) || distance > maximumRayDistance) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        // The owning client's avatar rig is the only enabled camera for this connection.
        private Camera LocalCamera()
        {
            var manager = session.NetworkManager;
            if (!manager.IsClientStarted) return null;
            var avatar = manager.ClientManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<PlayerAvatar>()).FirstOrDefault(x => x != null && x.IsOwner);
            return avatar == null ? null : avatar.CameraRig.GetComponentInChildren<Camera>();
        }

        private static string NewRequestId() => Guid.NewGuid().ToString("N");
    }
}
