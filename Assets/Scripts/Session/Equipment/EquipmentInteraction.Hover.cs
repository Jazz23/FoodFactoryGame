// What the crosshair is on that has a screen: an employee, a placed machine or the dev storage within interactReach of
// the avatar on its floor, or a purchasable map building (PropertyMarker) within interactReach across the ground. That target is outlined
// (HoverOutline; map buildings are not) while no screen is open, and the Interact key (or left click with an empty cursor) opens
// its screen. The Inventory key always opens the player's own inventory; both default to E, so E on nothing opens the
// inventory and E on something opens that. Local presentation only: opening a screen never changes gameplay state.
using System.Linq;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        // Outline drawn round the hover target (the employees' prefab brings its own).
        [SerializeField] private Material hoverOutlineMaterial;
        // Light green see-through tint (FoodFactory/XRayTint) on every valid target while picking one for an employee task.
        [SerializeField] private Material pickTintMaterial;

        // An EmployeeWorker, EquipmentVisual or storage SiteLocationMarker; null when the crosshair is on nothing openable
        // within reach.
        private Component _hovered;

        public Component Hovered => _hovered;

        // The interact key's first binding as the player sees it ("E"), for the HUD prompt.
        public string InteractKey => KeyText(InteractAction ?? inventoryAction.action);

        // The inventory key's first binding as the player sees it, for the status line.
        public string InventoryKey => KeyText(inventoryAction.action);

        private static string KeyText(UnityEngine.InputSystem.InputAction action) =>
            UnityEngine.InputSystem.InputActionRebindingExtensions.GetBindingDisplayString(action, 0);

        // Resolves this frame's Interact and Inventory presses (gathered by their callbacks) once, so a key bound to both (E by
        // default) acts once: Interact wins over something openable, Inventory opens the inventory otherwise. On an open screen
        // either key closes it (Interact also picks while picking a target for an employee task).
        private void ResolveKeyPresses()
        {
            var interact = _interactPressed;
            var inventory = _inventoryPressed;
            _interactPressed = _inventoryPressed = false;
            if (!interact && !inventory) return;
            if (interact || Screen != InteractionScreen.None)
            {
                if (interact) Interact(inventory);
                else if (Screen is not InteractionScreen.PickPosition && !(Screen == InteractionScreen.Employee && ScriptTextFocused)) CloseScreen();
                return;
            }
            ToggleInventory();
        }

        private void UpdateHover()
        {
            var hovered = _camera != null && Screen == InteractionScreen.None ? HoverUnderCrosshair() : null;
            _hoveredEmployee = _held == null ? hovered as EmployeeWorker : null;
            if (hovered == _hovered) return;
            SetOutline(_hovered, false);
            _hovered = hovered;
            SetOutline(_hovered, true);
        }

        private void ClearHover()
        {
            SetOutline(_hovered, false);
            _hovered = null;
            _hoveredEmployee = null;
        }

        private void SetOutline(Component target, bool on)
        {
            // Map buildings are static-batched and far too large to outline; the hint names them instead.
            if (target == null || target is PropertyMarker) return;
            HoverOutline.For(target, hoverOutlineMaterial).SetHighlighted(on);
        }

        // Nearest non-avatar hit, triggers included only for employees (their click collider is a trigger so it never
        // blocks walking).
        private Component HoverUnderCrosshair()
        {
            var hit = Physics.RaycastAll(AimRay(), maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                .Where(x => x.collider.GetComponentInParent<PlayerAvatar>() == null
                            && (!x.collider.isTrigger || x.collider.GetComponentInParent<EmployeeWorker>() != null))
                .OrderBy(x => x.distance).FirstOrDefault().collider;
            if (hit == null) return null;
            // Map buildings are aimed at from the street: the same interactReach as everything else, but no floor check.
            var property = hit.GetComponentInParent<PropertyMarker>();
            if (property != null)
            {
                if (_avatar == null) return null;
                var position = _avatar.transform.position;
                var nearest = hit.bounds.ClosestPoint(position);
                return Vector2.Distance(new Vector2(nearest.x, nearest.z), new Vector2(position.x, position.z)) <= interactReach ? property : null;
            }
            if (!InReach(hit.bounds)) return null;
            var employee = hit.GetComponentInParent<EmployeeWorker>();
            if (employee != null) return employee;
            var visual = hit.GetComponentInParent<EquipmentVisual>();
            // Machines on another drawn site are visible but not usable until the player enters that site (decision 0031).
            if (visual != null) return OnCurrentSite(visual) ? visual : null;
            var marker = hit.GetComponentInParent<SiteLocationMarker>();
            return marker != null && marker.LocationId == DevWorld.StorageId ? marker : null;
        }

        // The avatar stands on the floor the bounds rest on, within interactReach of them across the floor.
        private bool InReach(Bounds bounds)
        {
            if (_avatar == null) return false;
            var position = _avatar.transform.position;
            if (Mathf.Abs(position.y - bounds.min.y) > SiteGridSpace.LevelHeight * 0.5f) return false;
            var nearest = bounds.ClosestPoint(new Vector3(position.x, bounds.center.y, position.z));
            return Vector2.Distance(new Vector2(nearest.x, nearest.z), new Vector2(position.x, position.z)) <= interactReach;
        }

        // Interact: closes an open screen (not while typing in the script screen's text box; while picking for it, it picks);
        // otherwise opens the hover target's screen, or the inventory when there is none and the Inventory key was pressed too.
        private void Interact(bool inventoryToo)
        {
            if (Screen != InteractionScreen.None)
            {
                // While picking for the employee screen, E picks like a click.
                if (Screen == InteractionScreen.PickPosition)
                {
                    FinishPick();
                    return;
                }
                if (Screen == InteractionScreen.Employee && ScriptTextFocused) return;
                CloseScreen();
                return;
            }
            switch (_hovered)
            {
                case EmployeeWorker employee when employee != null:
                    OpenEmployeeScreen(employee);
                    break;
                case EquipmentVisual visual when visual != null:
                    OpenMachine(visual.EquipmentId);
                    break;
                case SiteLocationMarker marker when marker != null:
                    OpenStorage();
                    break;
                case PropertyMarker property when property != null:
                    OpenProperty(property.LotId);
                    break;
                default:
                    if (inventoryToo) ToggleInventory();
                    break;
            }
        }

        private string HoverHint() => _hovered switch
        {
            EmployeeWorker => (_held == null ? "Left click or " : "") + $"{InteractKey}: give this employee a script. ",
            EquipmentVisual visual => $"{InteractKey}: open the {visual.Kind}. ",
            SiteLocationMarker => $"{InteractKey}: open the storage. ",
            PropertyMarker property => $"{InteractKey}: {property.BuildingId}: price and owner. ",
            _ => ""
        };
    }
}
