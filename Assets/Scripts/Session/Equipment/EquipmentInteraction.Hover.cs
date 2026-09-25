// What the crosshair is on that has a screen: an employee, a placed machine or the dev storage, within interactReach of
// the avatar on its floor. That target is outlined (HoverOutline) while no screen is open, and E (or left click with an empty cursor) opens its screen; E on nothing opens the
// inventory. Local presentation only: opening a screen never changes gameplay state.
using System.Linq;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Player;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        // Outline drawn round the hover target (the employees' prefab brings its own).
        [SerializeField] private Material hoverOutlineMaterial;

        // An EmployeeWorker, EquipmentVisual or storage SiteLocationMarker; null when the crosshair is on nothing openable
        // within reach.
        private Component _hovered;

        public Component Hovered => _hovered;

        private void UpdateHover()
        {
            var hovered = _camera != null && Screen == InteractionScreen.None && !_released ? HoverUnderCrosshair() : null;
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
            if (target == null) return;
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
            if (hit == null || !InReach(hit.bounds)) return null;
            var employee = hit.GetComponentInParent<EmployeeWorker>();
            if (employee != null) return employee;
            var visual = hit.GetComponentInParent<EquipmentVisual>();
            if (visual != null) return visual;
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

        // E: closes an open screen; otherwise opens the hover target's screen, or the inventory when there is none.
        private void Interact()
        {
            if (Screen != InteractionScreen.None)
            {
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
                default:
                    ToggleInventory();
                    break;
            }
        }

        private string HoverHint() => _hovered switch
        {
            EmployeeWorker => (_held == null ? "Left click or " : "") + "E: give this employee a script. ",
            EquipmentVisual visual => $"E: open the {visual.Kind}. ",
            SiteLocationMarker => "E: open the storage. ",
            _ => ""
        };
    }
}
