// Employee script screens (PROTOTYPE, see EmployeeWorker): left click or E on a hovered employee (EquipmentInteraction.Hover.cs)
// opens one.
// While that screen is open the gameplay action map is suspended (all but Esc and the pointer), so typing a script never
// moves the avatar, opens the inventory or presses hotbar keys. The screen itself is EmployeeScriptPanel.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Session.Employees;
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

        public void OpenEmployeeScreen(EmployeeWorker employee)
        {
            if (_camera == null || employee == null) return;
            ClearHover();
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
