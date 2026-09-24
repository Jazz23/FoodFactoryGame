// Employees under the crosshair (PROTOTYPE, see EmployeeWorker): hovering outlines one, left click opens its script screen.
// While that screen is open the gameplay action map is suspended (all but Esc and the pointer), so typing a script never
// moves the avatar, opens the inventory or presses hotbar keys. The screen itself is EmployeeScriptPanel.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        private readonly List<InputAction> _suspendedActions = new();
        private EmployeeWorker _hoveredEmployee;

        // Employee whose script screen is open; null unless Screen is Employee.
        public EmployeeWorker OpenEmployee { get; private set; }
        public EmployeeWorker HoveredEmployee => _hoveredEmployee;

        private void UpdateEmployeeHover()
        {
            var hovered = _camera != null && Screen == InteractionScreen.None && _held == null && !_released
                ? EmployeeUnderCrosshair() : null;
            if (hovered == _hoveredEmployee) return;
            SetOutline(_hoveredEmployee, false);
            _hoveredEmployee = hovered;
            SetOutline(_hoveredEmployee, true);
        }

        private static void SetOutline(EmployeeWorker employee, bool on)
        {
            if (employee == null) return;
            var outline = employee.GetComponent<EmployeeOutline>();
            if (outline != null) outline.SetHighlighted(on);
        }

        // Nearest non-avatar hit, triggers included (the employee's click collider is a trigger so it never blocks walking).
        private EmployeeWorker EmployeeUnderCrosshair()
        {
            var hit = Physics.RaycastAll(AimRay(), maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                .Where(x => x.collider.GetComponentInParent<PlayerAvatar>() == null
                            && (!x.collider.isTrigger || x.collider.GetComponentInParent<EmployeeWorker>() != null))
                .OrderBy(x => x.distance).FirstOrDefault();
            return hit.collider == null ? null : hit.collider.GetComponentInParent<EmployeeWorker>();
        }

        public void OpenEmployeeScreen(EmployeeWorker employee)
        {
            if (_camera == null || employee == null) return;
            SetOutline(_hoveredEmployee, false);
            _hoveredEmployee = null;
            Screen = InteractionScreen.Employee;
            OpenEmployee = employee;
            _awaitingRelease = true;
            SuspendGameplayInput(true);
        }

        private void CloseEmployeeScreen()
        {
            OpenEmployee = null;
            SuspendGameplayInput(false);
        }

        private void SuspendGameplayInput(bool suspend)
        {
            if (!suspend)
            {
                foreach (var action in _suspendedActions) action.Enable();
                _suspendedActions.Clear();
                return;
            }
            if (_suspendedActions.Count > 0) return;
            var keep = new[] { closeScreenAction.action, pointAction.action };
            foreach (var action in placeAction.action.actionMap.actions.Where(x => x.enabled && !keep.Contains(x)))
            {
                action.Disable();
                _suspendedActions.Add(action);
            }
        }
    }
}
