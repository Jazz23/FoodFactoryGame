// Local elevator rides (decision 0020): standing on a building's elevator cell, FloorUp and FloorDown move the owned avatar to
// the same cell one storey up or down. Avatar position is presentation only (decision 0005), so a ride is a local move that
// the avatar's network transform replicates; no server rule depends on it.
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Buildings
{
    [DisallowMultipleComponent]
    public sealed class ElevatorRider : MonoBehaviour
    {
        [SerializeField] private BuildingPresenter buildings;
        [SerializeField] private InputActionReference floorUpAction;
        [SerializeField] private InputActionReference floorDownAction;

        // True while the local avatar stands on the elevator cell of the building it is in.
        public bool OnElevator
        {
            get
            {
                var building = buildings.LocalBuilding;
                return building != null && SiteGrid.IsShaft(building, buildings.LocalCell.X, buildings.LocalCell.Z);
            }
        }

        private void OnEnable()
        {
            floorUpAction.action.performed += OnFloorUp;
            floorDownAction.action.performed += OnFloorDown;
            floorUpAction.action.Enable();
            floorDownAction.action.Enable();
        }

        private void OnDisable()
        {
            floorUpAction.action.performed -= OnFloorUp;
            floorDownAction.action.performed -= OnFloorDown;
            floorUpAction.action.Disable();
            floorDownAction.action.Disable();
        }

        private void OnFloorUp(InputAction.CallbackContext _) => Ride(1);

        private void OnFloorDown(InputAction.CallbackContext _) => Ride(-1);

        // Moves one storey up (+1) or down (-1); does nothing off the elevator or past the top or ground floor.
        public bool Ride(int step)
        {
            var building = buildings.LocalBuilding;
            var avatar = buildings.LocalAvatar;
            var layout = buildings.Layout;
            var target = buildings.LocalLevel + step;
            if (!OnElevator || avatar == null || layout == null || target < 0 || target >= building.Floors) return false;
            avatar.Teleport(SiteGridSpace.FootprintCenter(layout, building.ElevatorX, building.ElevatorZ, 1, 1, target) + Vector3.up * 0.05f);
            return true;
        }
    }
}
