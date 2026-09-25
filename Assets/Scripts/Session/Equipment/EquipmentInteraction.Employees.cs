// Employee script screens (PROTOTYPE, see EmployeeWorker): left click or E on a hovered employee (EquipmentInteraction.Hover.cs)
// opens one.
// While that screen is open the gameplay action map is suspended (all but Esc and the pointer), so typing a script never
// moves the avatar, opens the inventory or presses hotbar keys. The screen itself is EmployeeScriptPanel.
// "Select world pos" hides the screen (InteractionScreen.PickPosition) with walking and looking back on: the ground cell or
// the machine/storage under the crosshair is shown red, and a click hands its Lua text ({x, z} or "<id>") back to the screen.
// Local presentation only; the text is resolved again by the server when the script runs.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Equipment
{
    public sealed partial class EquipmentInteraction
    {
        private static readonly int OutlineColor = Shader.PropertyToID("_Color");
        // Player actions left on while picking a world position, so the player can walk and look for the spot.
        private static readonly string[] PickMovementActions = { "Move", "Look", "Jump", "Sprint", "Zoom", "SwitchCamera" };

        private readonly List<InputAction> _suspendedActions = new();
        private EmployeeWorker _hoveredEmployee;
        private Action<string> _pickDone;
        private Component _pickObject;
        private Renderer _pickMarker;
        private Material _pickOutline;

        // Employee whose script screen is open; null unless Screen is Employee or PickPosition.
        public EmployeeWorker OpenEmployee { get; private set; }
        public EmployeeWorker HoveredEmployee => _hoveredEmployee;
        // Set each frame by the script screen while its text box has focus, so an E typed there is text, not a close.
        public bool ScriptTextFocused { get; set; }
        // Lua text a click would insert while picking ({x, z} for a cell, "<id>" for a machine or storage); null on nothing.
        public string PickText { get; private set; }

        public void OpenEmployeeScreen(EmployeeWorker employee)
        {
            if (_camera == null || employee == null) return;
            ClearHover();
            Screen = InteractionScreen.Employee;
            OpenEmployee = employee;
            _awaitingRelease = true;
            SuspendGameplayInput(true);
        }

        // From the script screen: hides it until the player clicks a world position (done gets its Lua text) or presses Esc
        // (done is not called). Either way the script screen comes back.
        public void BeginWorldPick(Action<string> done)
        {
            if (Screen != InteractionScreen.Employee || OpenEmployee == null) return;
            Screen = InteractionScreen.PickPosition;
            _pickDone = done;
            _awaitingRelease = true;
            SuspendGameplayInput(false);
            SuspendGameplayInput(true, PickMovementActions);
        }

        private void FinishPick()
        {
            if (_awaitingRelease || PickText == null) return;
            var done = _pickDone;
            var text = PickText;
            EndPick();
            done?.Invoke(text);
        }

        private void CancelPick() => EndPick();

        // Back to the script screen with typing-safe input.
        private void EndPick()
        {
            ClearPick();
            _pickDone = null;
            if (Screen != InteractionScreen.PickPosition) return;
            Screen = InteractionScreen.Employee;
            _awaitingRelease = true;
            SuspendGameplayInput(false);
            SuspendGameplayInput(true);
        }

        private void CloseEmployeeScreen()
        {
            ClearPick();
            _pickDone = null;
            OpenEmployee = null;
            SuspendGameplayInput(false);
        }

        // Marks what a click would pick: a machine or the storage under the crosshair (red outline), otherwise the ground cell
        // under it on the avatar's floor (red tile).
        private void UpdatePick(GoodsSnapshot site)
        {
            if (Screen != InteractionScreen.PickPosition || _camera == null || site == null)
            {
                ClearPick();
                return;
            }
            var layout = site.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var hit = UnderCrosshair();
            Component target = hit == null ? null : hit.GetComponentInParent<EquipmentVisual>();
            string text = null;
            if (target is EquipmentVisual visual) text = $"\"{visual.EquipmentId}\"";
            else if (hit != null && hit.GetComponentInParent<SiteLocationMarker>() is { } marker && marker != null)
            {
                target = marker;
                text = $"\"{(string.IsNullOrEmpty(marker.Alias) ? marker.LocationId : marker.Alias)}\"";
            }
            SetPickObject(target);
            ShowPickMarker(false);
            if (text == null && layout != null && TryFloorPoint(out var point))
            {
                var (x, z) = SiteGridSpace.AnchorAt(layout, point, 1, 1);
                if (x >= 0 && z >= 0 && x < layout.Width && z < layout.Depth)
                {
                    text = $"{{{x}, {z}}}";
                    ShowPickMarker(true);
                    _pickMarker.transform.SetPositionAndRotation(
                        SiteGridSpace.FootprintCenter(layout, x, z, 1, 1, Level) + Vector3.up * 0.04f, Quaternion.identity);
                    _pickMarker.transform.localScale = new Vector3(SiteGrid.CellSize, 0.05f, SiteGrid.CellSize);
                }
            }
            PickText = text;
        }

        private void ClearPick()
        {
            SetPickObject(null);
            ShowPickMarker(false);
            PickText = null;
        }

        private void SetPickObject(Component target)
        {
            if (target == _pickObject) return;
            if (_pickObject != null) HoverOutline.For(_pickObject, hoverOutlineMaterial).SetHighlighted(false);
            _pickObject = target;
            if (target == null || hoverOutlineMaterial == null) return;
            if (_pickOutline == null)
            {
                _pickOutline = new Material(hoverOutlineMaterial) { name = "PickOutline" };
                _pickOutline.SetColor(OutlineColor, invalidColor);
            }
            HoverOutline.For(target, hoverOutlineMaterial).SetHighlighted(true, _pickOutline);
        }

        // A red copy of the placement footprint, kept separately because the ghost is hidden while a screen is open.
        private void ShowPickMarker(bool visible)
        {
            if (!visible)
            {
                if (_pickMarker != null) _pickMarker.gameObject.SetActive(false);
                return;
            }
            if (_pickMarker == null)
            {
                _pickMarker = Instantiate(ghost, transform);
                _pickMarker.name = "PickMarker";
                foreach (var collider in _pickMarker.GetComponentsInChildren<Collider>()) Destroy(collider);
                _block ??= new MaterialPropertyBlock();
                _pickMarker.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, invalidColor);
                _pickMarker.SetPropertyBlock(_block);
            }
            _pickMarker.gameObject.SetActive(true);
        }

        // Suspends the gameplay action map except Esc and the pointer (and the named actions, by action name in that map).
        private void SuspendGameplayInput(bool suspend, params string[] alsoKeep)
        {
            if (!suspend)
            {
                foreach (var action in _suspendedActions) action.Enable();
                _suspendedActions.Clear();
                return;
            }
            if (_suspendedActions.Count > 0) return;
            var keep = new[] { closeScreenAction.action, pointAction.action };
            if (alsoKeep.Length > 0) keep = keep.Append(placeAction.action).ToArray();
            // E closes the script screen too, unless the text box has focus (Interact checks ScriptTextFocused).
            if (Screen == InteractionScreen.Employee) keep = keep.Append(inventoryAction.action).ToArray();
            foreach (var action in placeAction.action.actionMap.actions
                         .Where(x => x.enabled && !keep.Contains(x) && !alsoKeep.Contains(x.name)))
            {
                action.Disable();
                _suspendedActions.Add(action);
            }
        }

        // Hands one machine of a kind the local player holds to the open employee, for its script's place().
        public void GiveMachine(string kind)
        {
            var bridge = _subscription?.Bridge;
            var employee = OpenEmployee;
            var me = LocalPlayerId;
            var machine = session.ClientSite?.Equipment
                .Where(x => x.State == EquipmentState.Held && x.HolderId == me && x.Kind == kind)
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (bridge == null || employee == null || machine == null) return;
            LastRejection = null;
            Debug.Log($"[Employees] Requesting that {machine.Id} be given to {employee.EmployeeId}.");
            bridge.RequestGive(Track(), machine.Id, employee.EmployeeId);
        }
    }
}
