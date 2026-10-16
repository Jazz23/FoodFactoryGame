// The keyboard and mouse bindings the Settings screen lets a player change, grouped as the screen shows them, plus interactive
// rebinding and conflict lookup on the project-wide action asset. Each row has a primary and an alternate slot: the action's
// first two Keyboard&Mouse bindings (a composite part's two bindings for movement); the asset authors an empty alternate where
// a key has none. Overrides live on the asset and are saved as JSON in ClientSettings; the authored bindings never change.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session
{
    public sealed class KeyBindingRow
    {
        public string Label;
        public InputAction Action;
        // Binding indices of the primary and alternate slot; -1 when the action has no such binding.
        public int Primary = -1;
        public int Alternate = -1;
        // Shown but not rebindable (the hotbar digits and scroll zoom), with this text instead of a key.
        public string FixedText;
    }

    public sealed class KeyBindingGroup
    {
        public string Name;
        public List<KeyBindingRow> Rows = new();
    }

    public static class KeyBindings
    {
        public const string MapName = "Player";
        private const string Group = "Keyboard&Mouse";

        // (group, label, action, composite part or null). Template leftovers (Attack, Look, Crouch, Previous, Next) are left out.
        private static readonly (string Group, string Label, string Action, string Part)[] Layout =
        {
            ("Movement", "Move forward", "Move", "up"),
            ("Movement", "Move back", "Move", "down"),
            ("Movement", "Move left", "Move", "left"),
            ("Movement", "Move right", "Move", "right"),
            ("Movement", "Jump", "Jump", null),
            ("Movement", "Sprint", "Sprint", null),
            ("Interaction", "Interact", "Interact", null),
            ("Interaction", "Inventory", "Inventory", null),
            ("Interaction", "Take item", "TakeItem", null),
            ("Interaction", "Place item", "PlaceItem", null),
            ("Interaction", "Quick transfer", "QuickTransfer", null),
            ("Interaction", "Clear cursor", "ClearCursor", null),
            ("Interaction", "Close screen", "CloseScreen", null),
            ("Building", "Build mode", "Build", null),
            ("Building", "Place", "Place", null),
            ("Building", "Remove", "Remove", null),
            ("Building", "Rotate", "Rotate", null),
            ("Building", "Pick block", "PickBlock", null),
            ("Building", "Confirm build", "BuildConfirm", null),
            ("Camera and floors", "Tilt camera", "CameraTilt", null),
            ("Camera and floors", "Zoom", "Zoom", "fixed:Scroll"),
            ("Camera and floors", "Floor up", "FloorUp", null),
            ("Camera and floors", "Floor down", "FloorDown", null),
            ("Camera and floors", "Flip lift", "FlipLift", null),
            ("Camera and floors", "Logistics", "Logistics", null),
            ("Camera and floors", "Phone", "Phone", null),
            ("Camera and floors", "Phone map", "Map", null),
            ("Hotbar", "Hotbar slots", "Hotbar", "fixed:1 – 9")
        };

        // The rows for an asset, in screen order; actions the asset lacks are skipped.
        public static List<KeyBindingGroup> Groups(InputActionAsset asset)
        {
            var groups = new List<KeyBindingGroup>();
            var map = asset?.FindActionMap(MapName);
            if (map == null) return groups;
            foreach (var (groupName, label, actionName, part) in Layout)
            {
                var action = map.FindAction(actionName);
                if (action == null) continue;
                var group = groups.FirstOrDefault(x => x.Name == groupName);
                if (group == null) groups.Add(group = new KeyBindingGroup { Name = groupName });
                var row = new KeyBindingRow { Label = label, Action = action };
                if (part != null && part.StartsWith("fixed:", StringComparison.Ordinal)) row.FixedText = part.Substring(6);
                else
                {
                    var slots = Slots(action, part);
                    row.Primary = slots.Count > 0 ? slots[0] : -1;
                    row.Alternate = slots.Count > 1 ? slots[1] : -1;
                }
                group.Rows.Add(row);
            }
            return groups;
        }

        // Keyboard&Mouse binding indices of an action (or of one composite part), in authored order.
        private static List<int> Slots(InputAction action, string part)
        {
            var slots = new List<int>();
            var bindings = action.bindings;
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding.isComposite || binding.isPartOfComposite != (part != null)) continue;
                if (part != null && !string.Equals(binding.name, part, StringComparison.OrdinalIgnoreCase)) continue;
                var keyboardOrMouse = binding.path.StartsWith("<Keyboard>", StringComparison.Ordinal)
                    || binding.path.StartsWith("<Mouse>", StringComparison.Ordinal)
                    || (string.IsNullOrEmpty(binding.path) && (binding.groups ?? "").Contains(Group));
                if (keyboardOrMouse) slots.Add(index);
            }
            return slots;
        }

        // What a slot shows: the key's short name, or an empty string when unbound.
        public static string Display(InputAction action, int index) =>
            index < 0 || string.IsNullOrEmpty(action.bindings[index].effectivePath) ? "" : action.GetBindingDisplayString(index);

        // Starts listening for the next key or mouse button for one slot. Esc cancels; Backspace unbinds. The action is disabled
        // while listening (the Input System requires it) and re-enabled afterwards if it was on. done(true) when it changed.
        public static InputActionRebindingExtensions.RebindingOperation Rebind(InputAction action, int index, Action<bool> done)
        {
            var wasEnabled = action.enabled;
            action.Disable();
            var before = action.bindings[index].effectivePath;
            var operation = action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Mouse>/press")
                .WithControlsExcluding("<Mouse>/clickCount")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f);
            void Finish(InputActionRebindingExtensions.RebindingOperation finished, bool completed)
            {
                if (completed && action.bindings[index].effectivePath == "<Keyboard>/backspace") action.ApplyBindingOverride(index, "");
                finished.Dispose();
                if (wasEnabled) action.Enable();
                done?.Invoke(completed && action.bindings[index].effectivePath != before);
            }
            operation.OnComplete(x => Finish(x, true)).OnCancel(x => Finish(x, false));
            return operation.Start();
        }

        // Labels of the other actions in the Player map bound to this slot's key (a warning, not a refusal: some keys are
        // shared on purpose, such as E for Interact and Inventory).
        public static List<string> ConflictsOf(InputActionAsset asset, InputAction action, int index)
        {
            var path = action.bindings[index].effectivePath;
            var conflicts = new List<string>();
            if (string.IsNullOrEmpty(path)) return conflicts;
            var map = asset.FindActionMap(MapName);
            foreach (var other in map.actions)
            {
                if (other == action || Layout.All(x => x.Action != other.name)) continue;
                if (other.bindings.Any(x => !x.isComposite && string.Equals(x.effectivePath, path, StringComparison.OrdinalIgnoreCase)))
                    conflicts.Add(LabelOf(other.name));
            }
            return conflicts.Distinct().ToList();
        }

        private static string LabelOf(string actionName) =>
            Layout.FirstOrDefault(x => x.Action == actionName && (x.Part == null || x.Part.StartsWith("fixed:", StringComparison.Ordinal))).Label
            ?? (actionName == "Move" ? "Move" : actionName);
    }
}
