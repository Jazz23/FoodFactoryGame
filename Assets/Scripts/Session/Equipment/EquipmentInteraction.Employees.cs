// Employee script screens (PROTOTYPE, see EmployeeWorker): left click or E on a hovered employee (EquipmentInteraction.Hover.cs)
// opens one.
// While that screen is open the gameplay action map is suspended (all but Esc and the pointer), so typing a script never
// moves the avatar, opens the inventory or presses hotbar keys. The screen itself is EmployeeScriptPanel.
// "Select world pos" hides the screen (InteractionScreen.PickPosition) with walking and looking back on: the ground cell or
// the machine/storage under the crosshair is shown red, and a click hands its Lua text ({x, z} or "<id>") back to the screen.
// The Tasks tab's "Set source", "Set destination" and "Select machine" (decision 0037) pick the same way, but only among
// valid targets, which all pulse red (the one under the crosshair solid red): everything with goods a player can open for a
// source or destination, a machine with a power switch for a machine. A click or E hands back its place text (unquoted).
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
        private static readonly int OutlineWidth = Shader.PropertyToID("_Width");
        // Outline widths (metres) while picking a target: the pulse swings between the first two; the one under the crosshair
        // holds the third.
        private const float PulseThin = 0.035f;
        private const float PulseThick = 0.08f;
        private const float PickedWidth = 0.09f;
        // Player actions left on while picking a world position, so the player can walk and look for the spot.
        private static readonly string[] PickMovementActions = { "Move", "Look", "Jump", "Sprint", "Zoom", "SwitchCamera" };

        // Seconds per pulse of the valid targets' outline while picking one.
        private const float PulseSeconds = 0.9f;

        private readonly List<InputAction> _suspendedActions = new();
        private readonly List<Component> _pulsing = new();
        private Material _pulseOutline;
        private float _nextTargetRefresh;
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
        // What the current pick is for; Position for "Select world pos".
        public PickTarget PickMode { get; private set; }
        // The targets that pulse while picking a source, destination or machine (empty otherwise).
        public IReadOnlyList<Component> PickTargets => _pulsing;

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
        public void BeginWorldPick(Action<string> done) => BeginPick(PickTarget.Position, done);

        // From the Tasks tab: hides the screen while the player picks one of the pulsing valid targets (done gets its place
        // text: "storage", "<id>", "<id>:out" or "<id>:in") or presses Esc (done is not called).
        public void BeginTargetPick(PickTarget target, Action<string> done) => BeginPick(target, done);

        private void BeginPick(PickTarget target, Action<string> done)
        {
            if (Screen != InteractionScreen.Employee || OpenEmployee == null) return;
            PickMode = target;
            _nextTargetRefresh = 0f;
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
            if (PickMode != PickTarget.Position)
            {
                UpdateTargetPick(site);
                return;
            }
            var layout = site.SiteLayouts.FirstOrDefault(x => x.SiteId == session.ClientSiteId);
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
            foreach (var target in _pulsing)
                if (target != null) HoverOutline.For(target, hoverOutlineMaterial).SetHighlighted(false);
            _pulsing.Clear();
        }

        // Pulses every valid target red and marks the one under the crosshair solid red; a click or E picks that one.
        private void UpdateTargetPick(GoodsSnapshot site)
        {
            if (Time.unscaledTime >= _nextTargetRefresh)
            {
                _nextTargetRefresh = Time.unscaledTime + 0.5f;
                var valid = ValidTargets(site);
                foreach (var stale in _pulsing.Where(x => x != null && !valid.Contains(x)))
                    HoverOutline.For(stale, hoverOutlineMaterial).SetHighlighted(false);
                _pulsing.Clear();
                _pulsing.AddRange(valid);
            }
            _pulsing.RemoveAll(x => x == null);
            var hit = UnderCrosshair();
            Component hovered = null;
            if (hit != null)
            {
                var visual = hit.GetComponentInParent<EquipmentVisual>();
                var marker = hit.GetComponentInParent<SiteLocationMarker>();
                hovered = visual != null && _pulsing.Contains(visual) ? visual : marker != null && _pulsing.Contains(marker) ? marker : null;
            }
            if (hoverOutlineMaterial != null)
            {
                // Unity-aware checks: an unassigned Material field is a fake null that ??= takes for an object.
                if (_pickOutline == null) _pickOutline = Outline("PickOutline", invalidColor);
                if (_pulseOutline == null) _pulseOutline = Outline("PulseOutline", invalidColor);
                _pickOutline.SetFloat(OutlineWidth, PickedWidth);
                var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / PulseSeconds);
                var dim = invalidColor * 0.55f;
                dim.a = 1f;
                _pulseOutline.SetColor(OutlineColor, Color.Lerp(dim, invalidColor, pulse));
                _pulseOutline.SetFloat(OutlineWidth, Mathf.Lerp(PulseThin, PulseThick, pulse));
                foreach (var target in _pulsing)
                    HoverOutline.For(target, hoverOutlineMaterial).SetHighlighted(true, target == hovered ? _pickOutline : _pulseOutline);
            }
            PickText = hovered == null ? null : TargetText(site, hovered);
        }

        private Material Outline(string name, Color color)
        {
            var material = new Material(hoverOutlineMaterial) { name = name };
            material.SetColor(OutlineColor, color);
            return material;
        }

        // Valid targets on the current site: for a source or destination, every placed piece a player can open for goods (not
        // tables or decor) and the storage; for a machine, every placed piece whose kind has a power switch.
        private List<Component> ValidTargets(GoodsSnapshot site)
        {
            var placed = site.Equipment.Where(x => x.State == EquipmentState.Placed && x.SiteId == session.ClientSiteId)
                .ToDictionary(x => x.Id);
            var targets = new List<Component>();
            foreach (var visual in FindObjectsByType<EquipmentVisual>())
            {
                if (visual == null || !placed.TryGetValue(visual.EquipmentId, out var equipment)) continue;
                var definition = session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == equipment.Kind);
                var valid = PickMode == PickTarget.Machine
                    ? definition != null && definition.ManualPower
                    : definition?.OpensScreen != false && !GoodsWorld.IsTable(equipment);
                if (valid) targets.Add(visual);
            }
            if (PickMode != PickTarget.Machine)
                targets.AddRange(FindObjectsByType<SiteLocationMarker>()
                    .Where(x => x != null && site.Locations.Any(y => y.Id == x.LocationId && y.SiteId == session.ClientSiteId)));
            return targets;
        }

        // The place text a picked target stands for. A source takes a recipe machine's (or a dock's incoming) output, and
        // anything else, such as the fridge, as a whole; a destination fills a machine's input.
        private string TargetText(GoodsSnapshot site, Component target)
        {
            if (target is SiteLocationMarker marker) return string.IsNullOrEmpty(marker.Alias) ? marker.LocationId : marker.Alias;
            var visual = (EquipmentVisual)target;
            var equipment = site.Equipment.FirstOrDefault(x => x.Id == visual.EquipmentId);
            if (equipment == null) return null;
            return PickMode switch
            {
                PickTarget.Destination => equipment.InputLocationId,
                PickTarget.Source when equipment.Kind == GoodsWorld.DockKind
                    || session.Recipes.Any(x => x != null && x.StationKind == equipment.Kind && !x.IsSale) => equipment.OutputLocationId,
                _ => equipment.Id
            };
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
            // E closes the script screen too, unless a text box has focus (Interact checks ScriptTextFocused), and picks the
            // target under the crosshair while picking.
            if (Screen is InteractionScreen.Employee or InteractionScreen.PickPosition) keep = keep.Append(inventoryAction.action).ToArray();
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
