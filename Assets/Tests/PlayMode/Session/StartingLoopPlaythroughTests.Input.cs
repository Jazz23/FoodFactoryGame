// Pass I of the P0 playthrough: every action goes through a virtual Keyboard and Mouse (InputTestFixture devices, so the real
// Player action map reads them) or, on UI Toolkit screens, a press of the virtual mouse over the element's centre with a UI
// pointer event at the same point when the panel does not take the device press. It may read state to aim, to find targets
// and to judge results, but never sends a bridge request or calls a gameplay method. Walking and aiming are closed loops with
// timeouts: Move is held toward each waypoint until the avatar is near it (side-stepping when stuck), and the crosshair is
// turned with Look deltas (outdoors) or the free pointer is moved onto the target (indoors, where the camera looks down).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Logistics;
using FoodFactoryGame.Session.WorldMap;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed partial class StartingLoopPlaythroughTests
    {
        private Keyboard _keyboard;
        private Mouse _mouse;
        private readonly HashSet<Key> _keys = new();
        private Vector2 _pointer;
        private bool _left;
        private bool _right;
        private int _uiByDevice;
        private int _uiByEvent;
        private int _uiUnpickable;

        private UnfocusedUiInput _unfocused;

        private void AddDevices()
        {
            // Clicks go through the devices whether or not the Editor has focus (the event route stays as a fallback).
            _unfocused = new UnfocusedUiInput();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _pointer = new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f);
            QueueMouse();
        }

        // Lets go of every key and button (between steps and on failure).
        private void ReleaseDevices(bool _ = true)
        {
            if (_keyboard == null || _mouse == null || !_keyboard.added || !_mouse.added) return;
            _keys.Clear();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            _left = _right = false;
            QueueMouse();
        }

        private void QueueKeys() => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_keys.ToArray()));

        private void QueueMouse(Vector2 delta = default)
        {
            var state = new MouseState { position = _pointer, delta = delta };
            if (_left) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            if (_right) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private static IEnumerator Frames(int count)
        {
            for (var index = 0; index < count; index++) yield return null;
        }

        private IEnumerator Press(Key key)
        {
            _keys.Add(key);
            QueueKeys();
            yield return Frames(3);
            _keys.Remove(key);
            QueueKeys();
            yield return Frames(3);
        }

        private IEnumerator PointerTo(Vector2 screen)
        {
            _pointer = screen;
            QueueMouse();
            yield return Frames(2);
        }

        private IEnumerator MouseClick(bool right = false)
        {
            if (right) _right = true;
            else _left = true;
            QueueMouse();
            yield return Frames(3);
            _left = _right = false;
            QueueMouse();
            yield return Frames(3);
        }

        private Camera ViewCamera => LocalAvatar().CameraRig.GetComponentInChildren<Camera>();

        private Vector2 ToScreen(Vector3 world)
        {
            var point = ViewCamera.WorldToScreenPoint(world);
            return new Vector2(point.x, point.y);
        }

        private static bool OnScreen(Vector2 point, float margin = 0.06f) =>
            point.x > UnityEngine.Screen.width * margin && point.x < UnityEngine.Screen.width * (1f - margin)
            && point.y > UnityEngine.Screen.height * margin && point.y < UnityEngine.Screen.height * (1f - margin);

        // ---------------------------------------------------------------- walking

        // Holds Move (and Sprint over long legs) toward a point until the avatar is within tolerance of it, across the floor.
        private IEnumerator WalkTo(Vector3 target, float tolerance, string what, Action probe = null, float? timeout = null)
        {
            var avatar = LocalAvatar();
            var startDistance = Flat(target - avatar.transform.position).magnitude;
            var limit = timeout ?? startDistance / 2.5f + 8f;
            var began = Time.realtimeSinceStartup;
            var checkedAt = began;
            var checkedPosition = avatar.transform.position;
            var sidestepUntil = 0f;
            var side = 1;
            while (true)
            {
                probe?.Invoke();
                var now = Time.realtimeSinceStartup;
                var delta = Flat(target - avatar.transform.position);
                if (delta.magnitude <= tolerance) break;
                if (now - began > limit)
                {
                    _keys.Clear();
                    QueueKeys();
                    throw Failure($"walking to {what}: still {delta.magnitude:F1} m away after {limit:F0} s at {avatar.transform.position} (target {target})");
                }
                if (now - checkedAt > 0.8f)
                {
                    if (Flat(avatar.transform.position - checkedPosition).magnitude < 0.25f)
                    {
                        sidestepUntil = now + 0.6f;
                        side = -side;
                    }
                    checkedAt = now;
                    checkedPosition = avatar.transform.position;
                }
                var local = Quaternion.Euler(0f, -avatar.CameraRig.Yaw, 0f) * delta.normalized;
                if (now < sidestepUntil) local = new Vector3(local.z * side, 0f, -local.x * side) + local * 0.2f;
                _keys.Clear();
                if (local.z > 0.38f) _keys.Add(Key.W);
                if (local.z < -0.38f) _keys.Add(Key.S);
                if (local.x > 0.38f) _keys.Add(Key.D);
                if (local.x < -0.38f) _keys.Add(Key.A);
                if (delta.magnitude > 8f) _keys.Add(Key.LeftShift);
                QueueKeys();
                yield return null;
            }
            _keys.Clear();
            QueueKeys();
            yield return Frames(3);
        }

        private static Vector3 Flat(Vector3 value) => new(value.x, 0f, value.z);

        private IEnumerator Route(string what, params Vector3[] points)
        {
            for (var index = 0; index < points.Length; index++)
                yield return WalkTo(points[index], index == points.Length - 1 ? 0.6f : 1.2f, $"{what} (waypoint {index + 1}/{points.Length})");
        }

        // Out of the current building by its front door, when the avatar is inside one.
        private IEnumerator LeaveBuilding()
        {
            if (!Indoors) yield break;
            var site = _root.ClientSiteId;
            var (door, outward) = FrontDoor(site);
            var point = CellPoint(site, door.X, door.Z);
            yield return Route("out of the front door", point - outward * 1.6f, point + outward * 2.5f);
        }

        private IEnumerator EnterBuilding(string siteId)
        {
            if (Indoors) yield break;
            var (door, outward) = FrontDoor(siteId);
            var point = CellPoint(siteId, door.X, door.Z);
            yield return Route("in by the front door", point + outward * 2.5f, point - outward * 1.6f);
        }

        // A free floor cell beside a placed piece (inside or outside the shell, as the piece is), nearest the avatar.
        private Vector3 StandBeside(GoodsEquipment piece)
        {
            var site = Server;
            var shell = Shell(site, piece.SiteId);
            var layout = LayoutOf(piece.SiteId);
            var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
            var inside = SiteGrid.IsInterior(shell, piece.CellX, piece.CellZ);
            var avatar = LocalAvatar().transform.position;
            var cells = new List<(int X, int Z)>();
            for (var x = piece.CellX - 1; x <= piece.CellX + width; x++)
            for (var z = piece.CellZ - 1; z <= piece.CellZ + depth; z++)
                if (SiteGrid.Overlaps(x, z, 1, 1, piece.CellX - 1, piece.CellZ - 1, width + 2, depth + 2) && !SiteGrid.Overlaps(x, z, 1, 1, piece.CellX, piece.CellZ, width, depth))
                    cells.Add((x, z));
            var free = cells.Where(c => c.X >= 0 && c.Z >= 0 && c.X < layout.Width && c.Z < layout.Depth && !SiteGrid.IsWall(shell, c.X, c.Z)
                    && SiteGrid.IsInterior(shell, c.X, c.Z) == inside && SiteGrid.CellProblem(site, piece.SiteId, c.X, c.Z, 1, 1, null) == null)
                .Select(c => SiteGridSpace.FootprintCenter(layout, c.X, c.Z, 1, 1)).OrderBy(p => Flat(p - avatar).sqrMagnitude).ToList();
            if (free.Count == 0) throw Failure($"no free cell beside {piece.Id}");
            return free[0];
        }

        private IEnumerator GoBeside(GoodsEquipment piece)
        {
            var inside = SiteGrid.IsInterior(Shell(Server, piece.SiteId), piece.CellX, piece.CellZ);
            var from = LocalAvatar().transform.position;
            var wasIndoors = Indoors;
            // A restaurant dock stands at a back door's doorstep (decision 0037): a player walks in by the front door and out by
            // that back door, as a straight walk from the front door can run into the shell.
            var shell = Shell(Server, piece.SiteId);
            var back = inside || piece.Kind != GoodsWorld.DockKind || shell == null ? null
                : SiteGrid.ServiceDoors(shell).Select(d => ((int X, int Z)?)d)
                    .OrderBy(d => Math.Abs(d.Value.X - piece.CellX) + Math.Abs(d.Value.Z - piece.CellZ)).FirstOrDefault();
            var step = back is { } door ? SiteGrid.Doorstep(shell, door.X, door.Z) : null;
            if (inside) yield return EnterBuilding(piece.SiteId);
            else if (back is { } b && step is { } s)
            {
                yield return EnterBuilding(piece.SiteId);
                var site = piece.SiteId;
                yield return Route("out of the back door", CellPoint(site, 2 * b.X - s.X, 2 * b.Z - s.Z), CellPoint(site, b.X, b.Z), CellPoint(site, s.X, s.Z));
            }
            else yield return LeaveBuilding();
            if (!inside) Note($"walk to {piece.Id} outside: indoors {wasIndoors} at {from}, after leaving {Indoors} at {LocalAvatar().transform.position}, target {StandBeside(piece)}");
            yield return WalkTo(StandBeside(piece), 0.5f, $"beside {piece.Id}");
        }

        // ---------------------------------------------------------------- aiming

        // Indoors the camera looks down and the pointer is free: the pointer goes onto the target. Outdoors the crosshair is
        // turned with Look deltas until the hover target is the one wanted.
        private IEnumerator AimAt(Func<Vector3> target, Func<bool> aimed, string what, float timeout = 8f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!aimed())
            {
                var camera = ViewCamera;
                if (Time.realtimeSinceStartup > end)
                {
                    var ray = Interaction.PointerLocked ? camera.ScreenPointToRay(new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f)) : camera.ScreenPointToRay(_pointer);
                    var hits = Physics.RaycastAll(ray, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance).Take(4)
                        .Select(x => $"{x.collider.name} at {x.distance:F1} m");
                    var off = Vector3.Angle(ray.direction, target() - ray.origin);
                    throw Failure($"aiming at {what}: hover is '{Describe(Interaction.Hovered)}' (pointer locked {Interaction.PointerLocked}); aim {off:F1} deg off the target; the ray hits {string.Join(", ", hits)}");
                }
                if (!Interaction.PointerLocked)
                {
                    _pointer = ToScreen(target());
                    QueueMouse();
                }
                else
                {
                    var direction = target() - camera.transform.position;
                    var yaw = Vector3.SignedAngle(Flat(camera.transform.forward), Flat(direction), Vector3.up);
                    var pitchNow = -Mathf.Asin(Mathf.Clamp(camera.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                    var pitchWanted = -Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                    // OrbitCameraRig: yaw += delta.x * 0.2, pitch -= delta.y * 0.2 degrees per unit.
                    var delta = new Vector2(Mathf.Clamp(yaw / 0.2f * 0.5f, -150f, 150f), Mathf.Clamp(-(pitchWanted - pitchNow) / 0.2f * 0.5f, -150f, 150f));
                    QueueMouse(delta);
                }
                yield return null;
            }
            QueueMouse();
            yield return null;
        }

        private static string Describe(Component component) => component switch
        {
            null => "nothing",
            EquipmentVisual visual => "machine " + visual.EquipmentId,
            PropertyMarker marker => "building " + marker.LotId,
            _ => component.GetType().Name
        };

        private Vector3 VisualCentre(string equipmentId)
        {
            var presenter = UnityEngine.Object.FindAnyObjectByType<EquipmentPresenter>();
            if (!presenter.Visuals.TryGetValue(equipmentId, out var visual)) throw Failure($"{equipmentId} is not drawn");
            var renderers = visual.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToList();
            if (renderers.Count == 0) return visual.transform.position + Vector3.up * 0.5f;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds.center;
        }

        private IEnumerator OpenMachineWithDevices(string equipmentId)
        {
            if (Interaction.Screen == InteractionScreen.Machine && Interaction.OpenMachineId == equipmentId) yield break;
            if (Interaction.Screen != InteractionScreen.None) yield return Press(Key.E);
            var piece = Server.Equipment.Single(x => x.Id == equipmentId);
            yield return GoBeside(piece);
            yield return AimAt(() => VisualCentre(equipmentId), () => Interaction.Hovered is EquipmentVisual v && v != null && v.EquipmentId == equipmentId,
                $"{piece.Kind} {equipmentId}");
            yield return Press(Key.E);
            yield return Until(() => Interaction.Screen == InteractionScreen.Machine && Interaction.OpenMachineId == equipmentId, $"the {piece.Kind} screen", 5f);
            yield return Frames(3);
        }

        // E with nothing openable under the aim opens the inventory (and its Supplier window).
        private IEnumerator OpenInventoryWithDevices()
        {
            if (Interaction.Screen == InteractionScreen.Inventory) yield break;
            if (Interaction.Screen != InteractionScreen.None) yield return Press(Key.E);
            if (!Interaction.PointerLocked) yield return PointerTo(ToScreen(LocalAvatar().transform.position));
            yield return Until(() => Interaction.Hovered == null, "nothing openable under the aim", 3f);
            yield return Press(Key.E);
            yield return Until(() => Interaction.Screen == InteractionScreen.Inventory, "the inventory screen", 5f);
            yield return Frames(3);
        }

        private IEnumerator CloseScreenWithDevices()
        {
            if (Interaction.Screen == InteractionScreen.None) yield break;
            yield return Press(Key.Escape);
            yield return Until(() => Interaction.Screen == InteractionScreen.None, "the screen to close", 3f);
        }

        // ---------------------------------------------------------------- UI Toolkit clicks

        private static Vector2 PanelToScreen(IPanel panel, Vector2 point)
        {
            var origin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            var unit = RuntimePanelUtils.ScreenToPanel(panel, Vector2.one) - origin;
            var topDown = new Vector2((point.x - origin.x) / unit.x, (point.y - origin.y) / unit.y);
            return new Vector2(topDown.x, UnityEngine.Screen.height - topDown.y);
        }

        // Scrolls a ScrollView with wheel events until the element is inside its viewport.
        private IEnumerator ScrollIntoView(VisualElement element, string what)
        {
            var scroll = element.GetFirstAncestorOfType<ScrollView>();
            if (scroll == null) yield break;
            for (var attempt = 0; attempt < 80; attempt++)
            {
                var view = scroll.contentViewport.worldBound;
                var bounds = element.worldBound;
                if (bounds.yMin >= view.yMin - 1f && bounds.yMax <= view.yMax + 1f) yield break;
                var down = bounds.yMax > view.yMax;
                var wheel = new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, down ? 3f : -3f), mousePosition = view.center };
                using (var evt = WheelEvent.GetPooled(wheel)) scroll.contentViewport.SendEvent(evt);
                yield return null;
            }
            throw Failure($"{what}: could not scroll it into view");
        }

        // Moves the pointer onto the element's centre and presses the virtual left button there; if the panel did not react
        // (effect false), sends UI pointer down/up events to the element under that point instead. Counts both routes.
        private IEnumerator ClickElement(VisualElement element, string what, Func<bool> effect, bool shift = false)
        {
            if (element == null) throw Failure($"{what}: not on screen");
            yield return Until(() => element.panel != null && element.resolvedStyle.display != DisplayStyle.None && element.worldBound.width > 0, $"{what} laid out", 5f);
            var hudSignature = HudSignature();
            yield return ScrollIntoView(element, what);
            if (!element.enabledInHierarchy) throw Failure($"{what}: disabled");
            var centre = element.worldBound.center;
            var picked = element.panel.Pick(centre);
            bool Hits(VisualElement x) => x != null && (x == element || element.Contains(x));
            if (!Hits(picked))
            {
                // A player clicks the part of the control that responds: try the rest of its rectangle.
                var bounds = element.worldBound;
                var spot = Enumerable.Range(0, 25).Select(i => new Vector2(bounds.xMin + bounds.width * (0.1f + 0.2f * (i % 5)), bounds.yMin + bounds.height * (0.1f + 0.2f * (i / 5))))
                    .FirstOrDefault(p => Hits(element.panel.Pick(p)));
                if (Hits(element.panel.Pick(spot)))
                {
                    Note($"UI: {what}: its centre is over '{picked?.name ?? "nothing"}' (bounds {bounds}); clicked at {spot} inside it instead");
                    centre = spot;
                    picked = element.panel.Pick(spot);
                }
            }
            var unpickable = !Hits(picked);
            if (unpickable)
            {
                // UI finding, not a harness stop: the panel's picking never reaches the control, so a pointer cannot click it.
                _uiUnpickable++;
                Note($"UI FINDING: {what}: no point of it takes a pointer pick (centre over '{picked?.name ?? "nothing"}' {picked?.worldBound}); bounds {element.worldBound}, "
                    + $"panel {element.panel.visualTree.worldBound} (screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}); ancestors "
                    + string.Join(" < ", Ancestors(element).Take(4).Select(x => $"'{x.name}' {x.worldBound}"))
                    + $"; PickAll at the centre: {PickAllNames(element.panel, element.worldBound.center)}; at its parent's centre: {PickAllNames(element.panel, element.parent.worldBound.center)}");
                picked = element;
            }
            if (shift)
            {
                _keys.Add(Key.LeftShift);
                QueueKeys();
            }
            // A reply can arrive within a frame on a local host, so any reply also counts as the click's effect.
            var replies = _results.Count;
            bool Reacted() => effect() || _results.Count != replies;
            yield return PointerTo(PanelToScreen(element.panel, centre));
            yield return MouseClick();
            for (var frame = 0; frame < 10 && !Reacted(); frame++) yield return null;
            if (Reacted())
            {
                _uiByDevice++;
            }
            else
            {
                foreach (var type in new[] { EventType.MouseDown, EventType.MouseUp })
                {
                    var system = new Event { type = type, button = 0, clickCount = 1, mousePosition = centre, modifiers = shift ? EventModifiers.Shift : EventModifiers.None };
                    using EventBase pointer = type == EventType.MouseDown ? PointerDownEvent.GetPooled(system) : PointerUpEvent.GetPooled(system);
                    picked.SendEvent(pointer);
                }
                for (var frame = 0; frame < 10 && !Reacted(); frame++) yield return null;
                _uiByEvent++;
                Note($"UI route: {what}: the virtual mouse press had no effect in 10 frames; a UI pointer event {(Reacted() ? "did" : "did not")}; clicks armed {Interaction.ScreenClicksArmed}, element attached {element.panel != null}, mouse left {_mouse.leftButton.isPressed}, device enabled {_mouse.enabled}, Game view focused {Application.isFocused}");
                if (!Reacted())
                {
                    var live = element.panel?.visualTree.Q(element.name);
                    Note($"UI DIAG: {what}: screen {Interaction.Screen}, clicks armed {Interaction.ScreenClicksArmed}, element attached {element.panel != null}, "
                        + $"same as the live '{element.name}' {ReferenceEquals(live, element)}, live bounds {live?.worldBound}, clicked bounds {element.worldBound}, "
                        + $"pick now '{element.panel?.Pick(centre)?.name}', cursor {Interaction.CursorGoods?.ItemId ?? "none"}, rejection {Interaction.LastRejection ?? "none"}; "
                        + $"HUD signature at the start: {hudSignature}; now: {HudSignature()}");
                    throw Failure($"{what}: neither the device press nor a UI pointer event had an effect");
                }
                if (unpickable) Expect(false, $"UI: {what} cannot be clicked by a pointer; only a UI event sent straight to it worked (workaround)");
            }
            if (shift)
            {
                _keys.Remove(Key.LeftShift);
                QueueKeys();
            }
        }

        // The HUD's last rebuild signature (diagnostics only: a change means the screen was rebuilt).
        private string HudSignature() =>
            (string)typeof(PlayerHud).GetField("_signature", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Hud);

        private static string PickAllNames(IPanel panel, Vector2 point)
        {
            var all = new List<VisualElement>();
            panel.PickAll(point, all);
            return string.Join(", ", all.Select(x => $"{x.GetType().Name}'{x.name}'"));
        }

        private static IEnumerable<VisualElement> Ancestors(VisualElement element)
        {
            for (var x = element; x != null; x = x.parent) yield return x;
        }

        private VisualElement HudElement(string name) => Hud.ScreenRoot.panel?.visualTree.Q(name) ?? Hud.ScreenRoot.Q(name);

        private Func<bool> Changed(Func<object> probe)
        {
            var before = probe();
            return () => !Equals(probe(), before);
        }

        private IEnumerator ClickSlot(string grid, int index, string what)
        {
            var cursor = Changed(() => (Interaction.CursorGoods?.ItemId, Interaction.CursorGoods?.LocationId, Interaction.HasPendingRequests));
            yield return ClickElement(HudElement($"hud-{grid}-slot-{index}"), what, () => cursor() || Interaction.HasPendingRequests);
            yield return Until(() => !Interaction.HasPendingRequests, $"the server's reply after {what}", 10f);
            // A reply can arrive before the baseline that shows it; read the grids again only from that baseline or a later one
            // (otherwise a stack the server already moved looks still there and the next click lands on a slot about to go).
            var revision = Server.Revision;
            yield return Until(() => Client != null && Client.Revision >= revision, $"the baseline after {what}", 10f);
            yield return Frames(3);
        }

        private int FirstEmpty(string grid)
        {
            for (var index = 0; index < 200; index++)
            {
                if (HudElement($"hud-{grid}-slot-{index}") == null) break;
                if (Hud.CountAt(grid, index) == 0) return index;
            }
            throw Failure($"no empty slot in the {grid} grid");
        }

        // Picks a stack up from one grid and drops it into another (as a player clicks two slots).
        private IEnumerator MoveStack(string fromGrid, string key, string toGrid, string what)
        {
            var from = Hud.SlotOf(fromGrid, key);
            if (from < 0) throw Failure($"{what}: no {key} in the {fromGrid} grid");
            yield return ClickSlot(fromGrid, from, $"{what}: pick up {key}");
            if (Interaction.CursorGoods == null) throw Failure($"{what}: nothing on the cursor after clicking the {key} slot");
            var to = toGrid == PlayerHud.InventoryGrid ? FirstEmpty(toGrid) : 0;
            yield return ClickSlot(toGrid, to, $"{what}: drop into {toGrid}");
            if (Interaction.LastRejection != null) Note($"{what}: rejection shown '{Interaction.LastRejection}'");
        }

        // ---------------------------------------------------------------- Pass I actions

        private IEnumerator BuyWithDevices(string[] offers)
        {
            yield return OpenInventoryWithDevices();
            foreach (var offer in offers)
            {
                var button = HudElement($"hud-offer-{offer}");
                yield return ClickElement(button, $"Buy {offer}", () => Interaction.HasPendingRequests);
                yield return Until(() => !Interaction.HasPendingRequests, $"the reply to Buy {offer}", 10f);
                Note($"Buy {offer}: {(Interaction.LastRejection == null ? "accepted" : "rejected " + Interaction.LastRejection)}");
            }
            yield return CloseScreenWithDevices();
            Note($"UI clicks so far: {_uiByDevice} taken from the virtual mouse, {_uiByEvent} needed a UI pointer event, {_uiUnpickable} controls not pickable");
        }

        private IEnumerator PlaceHeldWithDevices(string kind, (int X, int Z) anchor)
        {
            var slot = Interaction.HotbarIndex(HotbarEntry.Machine(kind));
            if (slot < 0) throw Failure($"no hotbar slot holds the {kind}");
            var siteId = _root.ClientSiteId;
            yield return EnterBuilding(siteId);
            yield return Press(Key.Digit1 + slot);
            yield return Until(() => Interaction.CursorKind == kind, $"the {kind} on the cursor (hotbar {slot + 1})", 3f);
            var held = Interaction.Held;
            var (width, depth) = SiteGrid.Footprint(held.Width, held.Depth, Interaction.Rotation);
            var centre = SiteGridSpace.FootprintCenter(LayoutOf(siteId), anchor.X, anchor.Z, width, depth);
            if (!OnScreen(ToScreen(centre))) yield return WalkTo(centre, 2.5f, $"near the {kind}'s spot");
            if (Interaction.PointerLocked) throw Failure("the pointer is locked indoors, so it cannot aim the ghost");
            yield return PointerTo(ToScreen(centre));
            yield return Frames(3);
            Note($"{kind} ghost at ({anchor.X},{anchor.Z}): visible {Interaction.GhostVisible}, status '{Interaction.Status}'");
            yield return MouseClick();
            yield return Until(() => !Interaction.HasPendingRequests && Server.Equipment.Any(x => x.Id == held.Id && x.State == EquipmentState.Placed)
                || Interaction.LastRejection != null, $"the {kind} placed", 10f);
            if (Interaction.LastRejection != null) throw Failure($"placement rejected: {Interaction.LastRejection}");
        }

        private IEnumerator LoadOvenWithDevices(GoodsEquipment oven)
        {
            yield return OpenMachineWithDevices(oven.Id);
            for (var attempt = 0; attempt < 4 && Hud.SlotOf(PlayerHud.InventoryGrid, "dough") >= 0; attempt++)
                yield return MoveStack(PlayerHud.InventoryGrid, "dough", PlayerHud.InputGrid, "dough into the oven");
            Note($"oven screen progress line '{(HudElement("hud-progress-label") as Label)?.text}'");
        }

        private IEnumerator TakeOvenOutputWithDevices(GoodsEquipment oven)
        {
            yield return OpenMachineWithDevices(oven.Id);
            var timers = Hud.ScreenRoot.Query<Label>("hud-spoil-timer").ToList().Select(x => x.text).Where(x => x != "").ToList();
            Note("spoil timers shown on the oven screen: " + (timers.Count == 0 ? "none" : string.Join(", ", timers)));
            for (var attempt = 0; attempt < 3 && Hud.SlotOf(PlayerHud.OutputGrid, "bread") >= 0; attempt++)
                yield return MoveStack(PlayerHud.OutputGrid, "bread", PlayerHud.InventoryGrid, "bread out of the oven");
            yield return CloseScreenWithDevices();
        }

        private IEnumerator StockAndStaffWithDevices(GoodsEquipment counter, int units)
        {
            yield return OpenMachineWithDevices(counter.Id);
            if (units > 0 && Hud.SlotOf(PlayerHud.InventoryGrid, "bread") >= 0)
                yield return MoveStack(PlayerHud.InventoryGrid, "bread", PlayerHud.InputGrid, "bread onto the register");
            if (Server.Equipment.Single(x => x.Id == counter.Id).StaffId != Me) yield return ClickStaffButtonWithDevices(counter, true);
            yield return CloseScreenWithDevices();
        }

        private IEnumerator ClickStaffButtonWithDevices(GoodsEquipment counter, bool work)
        {
            yield return OpenMachineWithDevices(counter.Id);
            var button = HudElement("hud-staff-button") as Button;
            Note($"staff button reads '{button?.text}'");
            yield return ClickElement(button, work ? "Work this register" : "Leave", () => Interaction.HasPendingRequests);
            yield return Until(() => !Interaction.HasPendingRequests, "the staffing reply", 10f);
            yield return Until(() => Server.Equipment.Single(x => x.Id == counter.Id).StaffId == (work ? Me : ""), "the staffing change", 5f);
        }

        private IEnumerator UseOvenAndWalkOutWithDevices(GoodsEquipment oven)
        {
            yield return OpenMachineWithDevices(oven.Id);
            yield return CloseScreenWithDevices();
            yield return LeaveBuilding();
            yield return Seconds(2f);
            yield return EnterBuilding(Start.SiteId);
        }

        private IEnumerator EmptyRegisterWithDevices(GoodsEquipment counter)
        {
            yield return OpenMachineWithDevices(counter.Id);
            for (var attempt = 0; attempt < 3 && Hud.SlotOf(PlayerHud.InputGrid, "bread") >= 0; attempt++)
                yield return MoveStack(PlayerHud.InputGrid, "bread", PlayerHud.InventoryGrid, "bread off the register");
        }

        // A dock's Outgoing (its input grid) from the inventory, everything of one item.
        private IEnumerator LoadDockWithDevices(string dockId, string item)
        {
            yield return OpenMachineWithDevices(dockId);
            for (var attempt = 0; attempt < 3 && Hud.SlotOf(PlayerHud.InventoryGrid, item) >= 0; attempt++)
                yield return MoveStack(PlayerHud.InventoryGrid, item, PlayerHud.InputGrid, $"{item} onto the dock");
            var note = HudElement("hud-dock-trucks") as Label;
            Note($"dock screen says '{note?.text}'");
            yield return CloseScreenWithDevices();
        }

        // ---------------------------------------------------------------- build mode

        private IEnumerator OpenBuildWithDevices()
        {
            if (Build.Active) yield break;
            if (Interaction.Screen != InteractionScreen.None) yield return CloseScreenWithDevices();
            yield return Press(Key.B);
            yield return Until(() => Build.Active, "build mode", 5f);
            yield return Seconds(1f);
        }

        private IEnumerator LeaveBuildWithDevices()
        {
            if (!Build.Active) yield break;
            yield return Press(Key.B);
            yield return Until(() => !Build.Active, "leaving build mode", 5f);
        }

        // Pans the build camera (Move) until the cell is on screen and clear of the panel, then puts the pointer on it.
        private IEnumerator PointAtCell(string siteId, (int X, int Z) cell)
        {
            var point = SiteGridSpace.FootprintCenter(LayoutOf(siteId), cell.X, cell.Z, 1, 1);
            var panel = Build.Window;
            var end = Time.realtimeSinceStartup + 6f;
            while (true)
            {
                var screen = ToScreen(point);
                var panelLeft = panel?.panel != null ? PanelToScreen(panel.panel, panel.worldBound.min).x : UnityEngine.Screen.width;
                var clear = OnScreen(screen) && screen.x < panelLeft - 24f;
                if (clear) break;
                if (Time.realtimeSinceStartup > end) throw Failure($"cell ({cell.X},{cell.Z}) stays off screen or under the build panel");
                _keys.Clear();
                if (screen.x >= panelLeft - 24f || screen.x > UnityEngine.Screen.width * 0.94f) _keys.Add(Key.D);
                else if (screen.x < UnityEngine.Screen.width * 0.06f) _keys.Add(Key.A);
                if (screen.y > UnityEngine.Screen.height * 0.94f) _keys.Add(Key.W);
                else if (screen.y < UnityEngine.Screen.height * 0.06f) _keys.Add(Key.S);
                // The pointer rests on the world while panning (over the panel the camera ignores Move).
                _pointer = new Vector2(UnityEngine.Screen.width * 0.3f, UnityEngine.Screen.height * 0.5f);
                QueueMouse();
                QueueKeys();
                yield return null;
            }
            _keys.Clear();
            QueueKeys();
            yield return PointerTo(ToScreen(point));
            yield return Frames(2);
        }

        private string ShowPreview() => $"{Build.Preview.Label}: charge {Cash(Build.Preview.ChargeCents)}, refund {Cash(Build.Preview.RefundCents)}" +
            (Build.Preview.Problem != null ? $" [{Build.Preview.Problem}]" : "");

        private IEnumerator SelectBuildOffer(string offerId)
        {
            yield return ClickElement(Build.Window.Q($"build-pick-{offerId}"), $"Place {offerId}", () => Build.OfferId == offerId);
        }

        private IEnumerator SelectBuildTool(BuildTool tool)
        {
            yield return ClickElement(Build.Window.Q($"build-tool-{tool.ToString().ToLowerInvariant()}"), $"{tool} tool", () => Build.Tool == tool);
        }

        private IEnumerator AwaitBuild(string what)
        {
            yield return Until(() => !Build.HasPendingRequests, $"the server's reply to {what}", 10f);
            Note($"{what}: {(Build.LastRejection == null ? "accepted" : "refused " + Build.LastRejection)}; cash now {Cash(Server.Companies.Single().Cash)}");
        }

        private IEnumerator ClickCell(string siteId, (int X, int Z) cell, string what, bool right = false)
        {
            yield return PointAtCell(siteId, cell);
            Note($"{what} preview at ({cell.X},{cell.Z}): {ShowPreview()}");
            var cash = Server.Companies.Single().Cash;
            yield return MouseClick(right);
            yield return Frames(2);
            if (!Build.HasPendingRequests && Server.Companies.Single().Cash == cash && Build.LastRejection == null)
                Note($"{what}: the click sent no order");
            yield return AwaitBuild(what);
        }

        private IEnumerator BuildWithDevices((int X, int Z) table, (int X, int Z) art, (int X, int Z) wall)
        {
            yield return OpenBuildWithDevices();
            var site = Start.SiteId;
            yield return SelectBuildOffer("supplier-rt-table-bistro");
            yield return ClickCell(site, table, "bistro table");
            yield return SelectBuildOffer("supplier-rt-wall-art");
            yield return ClickCell(site, art, "wall art");
            Note($"ambience with the art {RestaurantRules.Ambience(Server, site)}");
            // A two-cell interior wall: press, drag to the second cell, release, then Enter confirms.
            yield return SelectBuildTool(BuildTool.Wall);
            yield return PointAtCell(site, wall);
            _left = true;
            QueueMouse();
            yield return Frames(3);
            yield return PointerTo(ToScreen(SiteGridSpace.FootprintCenter(LayoutOf(site), wall.X, wall.Z + 1, 1, 1)));
            yield return Frames(3);
            _left = false;
            QueueMouse();
            yield return Frames(3);
            Note($"interior wall drawn: pending {Build.HasPending}, {(Build.PendingPreview != null ? $"{Build.PendingPreview.Label} charge {Cash(Build.PendingPreview.ChargeCents)} {Build.PendingPreview.Problem}" : "no preview")}");
            yield return Press(Key.Enter);
            yield return AwaitBuild("interior wall");
            yield return SelectBuildTool(BuildTool.Door);
            yield return ClickCell(site, wall, "door in the interior wall");
            yield return SelectBuildTool(BuildTool.Sell);
            yield return ClickCell(site, art, "sell the wall art", true);
            yield return LeaveBuildWithDevices();
        }

        private IEnumerator BuildItemWithDevices(string offerId, string siteId, (int X, int Z) cell, int rotation)
        {
            yield return OpenBuildWithDevices();
            yield return SelectBuildOffer(offerId);
            for (var turns = 0; turns < 4 && Build.Rotation != rotation; turns++) yield return Press(Key.R);
            if (Build.Rotation != rotation) throw Failure($"R did not turn the {offerId} to rotation {rotation} (at {Build.Rotation})");
            yield return ClickCell(siteId, cell, offerId);
            if (Build.LastRejection != null) throw Failure($"{offerId} refused: {Build.LastRejection}");
            yield return LeaveBuildWithDevices();
        }

        // ---------------------------------------------------------------- property, streets, logistics

        private IEnumerator BuyPropertyWithDevices(PropertyOffer diner)
        {
            yield return LeaveBuilding();
            var marker = UnityEngine.Object.FindObjectsByType<PropertyMarker>().FirstOrDefault(x => x.LotId == diner.LotId);
            if (marker == null) throw Failure($"no map marker for {diner.LotId}");
            var collider = marker.GetComponentInChildren<Collider>();
            var target = collider != null ? collider.bounds.center : marker.transform.position;
            var reach = Flat(target - LocalAvatar().transform.position).magnitude;
            Note($"diner marker {reach:F0} m from the avatar on the apron (reach 60 m)");
            // A player walks over to look at the building from its own street frontage, where nothing stands in between.
            yield return WalkTo(StreetPoint(Start), 1.5f, "the street in front of the start lot");
            yield return WalkStreets(StreetPoint(Start), StreetPoint(diner), 4f);
            yield return AimAt(() => target, () => Interaction.Hovered is PropertyMarker m && m != null && m.LotId == diner.LotId, $"building {diner.BuildingId}");
            yield return Press(Key.E);
            yield return Until(() => Interaction.Screen == InteractionScreen.Property && Interaction.OpenLotId == diner.LotId, "the buy panel", 5f);
            var panel = UnityEngine.Object.FindAnyObjectByType<PropertyPanel>();
            yield return Until(() => panel.CanBuy, "Buy enabled", 5f);
            Note($"buy panel: '{panel.StateText}', price shown '{(panel.Window.Q<Label>("property-price")?.text)}'");
            yield return ClickElement(panel.Window.Q("property-buy"), "Buy (property)", () => panel.HasPendingRequests);
            yield return Until(() => !panel.HasPendingRequests, "the purchase reply", 10f);
            if (panel.LastRejection != null) throw Failure("purchase refused: " + panel.LastRejection);
            yield return CloseScreenWithDevices();
        }

        private Vector3 StreetPoint(PropertyOffer offer) => SitePlacement.Active.OnGround(offer.AccessX + 0.5f, offer.AccessZ + 0.5f);

        // Walks from one street point to another along the road network's centre lines (the shortest node path).
        private IEnumerator WalkStreets(Vector3 from, Vector3 to, float stopShort = 0f)
        {
            var placement = SitePlacement.Active;
            var roads = placement.Roads;
            (int Segment, Vector2 Point) Nearest(Vector3 scene)
            {
                var map = placement.ToMap(scene);
                var best = (Segment: -1, Point: Vector2.zero, Distance: float.MaxValue);
                for (var index = 0; index < roads.Segments.Count; index++)
                {
                    var segment = roads.Segments[index];
                    var a = new Vector2(roads.Nodes[segment.From].X, roads.Nodes[segment.From].Z);
                    var b = new Vector2(roads.Nodes[segment.To].X, roads.Nodes[segment.To].Z);
                    var t = Mathf.Clamp01(Vector2.Dot(map - a, b - a) / Mathf.Max(0.001f, (b - a).sqrMagnitude));
                    var point = a + (b - a) * t;
                    var distance = (map - point).magnitude;
                    if (distance < best.Distance) best = (index, point, distance);
                }
                return (best.Segment, best.Point);
            }
            var start = Nearest(from);
            var end = Nearest(to);
            var path = new List<Vector2> { start.Point };
            if (start.Segment != end.Segment)
            {
                // Dijkstra over nodes from both ends of the start segment to either end of the goal segment.
                var distances = new Dictionary<int, float>();
                var previous = new Dictionary<int, int>();
                var open = new List<int>();
                foreach (var node in new[] { roads.Segments[start.Segment].From, roads.Segments[start.Segment].To })
                {
                    distances[node] = (new Vector2(roads.Nodes[node].X, roads.Nodes[node].Z) - start.Point).magnitude;
                    open.Add(node);
                }
                var goals = new HashSet<int> { roads.Segments[end.Segment].From, roads.Segments[end.Segment].To };
                while (open.Count > 0)
                {
                    var current = open.OrderBy(x => distances[x]).First();
                    open.Remove(current);
                    if (goals.Contains(current)) break;
                    foreach (var segmentIndex in roads.Nodes[current].Segments)
                    {
                        var segment = roads.Segments[segmentIndex];
                        var next = segment.From == current ? segment.To : segment.From;
                        var cost = distances[current] + segment.Length;
                        if (distances.TryGetValue(next, out var known) && known <= cost) continue;
                        distances[next] = cost;
                        previous[next] = current;
                        if (!open.Contains(next)) open.Add(next);
                    }
                }
                var goal = goals.Where(distances.ContainsKey).OrderBy(x => distances[x] + (new Vector2(roads.Nodes[x].X, roads.Nodes[x].Z) - end.Point).magnitude).Select(x => (int?)x).FirstOrDefault() ?? -1;
                if (goal < 0) throw Failure("no road path between the two lots");
                var nodes = new List<int>();
                for (var node = goal; ; node = previous[node])
                {
                    nodes.Add(node);
                    if (!previous.ContainsKey(node)) break;
                }
                nodes.Reverse();
                path.AddRange(nodes.Select(x => new Vector2(roads.Nodes[x].X, roads.Nodes[x].Z)));
            }
            path.Add(end.Point);
            var points = path.Select(p => placement.OnGround(p.x, p.y)).ToList();
            Note($"street walk: {points.Count} waypoints, {points.Zip(points.Skip(1), (a, b) => Vector3.Distance(a, b)).Sum():F0} m");
            for (var index = 0; index < points.Count; index++)
            {
                var last = index == points.Count - 1;
                yield return WalkTo(points[index], last ? Mathf.Max(1.5f, stopShort) : 2f, $"street waypoint {index + 1}/{points.Count}");
                if (stopShort > 0f && Flat(to - LocalAvatar().transform.position).magnitude <= stopShort) yield break;
            }
            yield return WalkTo(to, Mathf.Max(1.2f, stopShort), "the lot's street access");
        }

        // From wherever the avatar is to inside the target lot's building, along the streets.
        private IEnumerator WalkToSiteWithDevices(PropertyOffer target)
        {
            yield return LeaveBuilding();
            var here = SitePlacement.Active.LotOf(_root.ClientSiteId) is { } lot ? SitePlacement.Active.OfferOf(lot.Id) : Start;
            yield return WalkTo(StreetPoint(here), 1.5f, "the street in front of this lot");
            yield return WalkStreets(StreetPoint(here), StreetPoint(target));
            var (door, outward) = FrontDoor(target.SiteId);
            yield return WalkTo(CellPoint(target.SiteId, door.X, door.Z) + outward * 2.5f, 0.8f, "the target's front door");
            yield return EnterBuilding(target.SiteId);
        }

        private object Draft(LogisticsPanel panel, string routeKey)
        {
            var drafts = (System.Collections.IDictionary)typeof(LogisticsPanel).GetField("_drafts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            return drafts.Contains(routeKey) ? drafts[routeKey] : null;
        }

        private static string DraftField(object draft, string field) => (string)draft?.GetType().GetField(field).GetValue(draft);

        private string TruckDraft(LogisticsPanel panel, string truckId)
        {
            var drafts = (Dictionary<string, string>)typeof(LogisticsPanel).GetField("_truckDrafts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            return drafts.TryGetValue(truckId, out var chosen) ? chosen : null;
        }

        // Clicks a chooser's ">" until the draft reads what is wanted (the panel's state is read only to know when to stop).
        private IEnumerator Choose(LogisticsPanel panel, string rowName, Func<string> value, Func<bool> chosen, string what)
        {
            for (var attempt = 0; attempt < 8 && !chosen(); attempt++)
            {
                var next = panel.Window.Q(rowName)?.Query<Button>().ToList().LastOrDefault();
                var before = value();
                yield return ClickElement(next, $"{what} >", () => value() != before);
                yield return Frames(3);
            }
            if (!chosen()) throw Failure($"{what}: the chooser never reached the wanted value");
        }

        private IEnumerator TruckRouteWithDevices(string pickup, string dropoff)
        {
            yield return LeaveBuilding();
            yield return Press(Key.L);
            yield return Until(() => Interaction.Screen == InteractionScreen.Logistics, "the logistics screen", 5f);
            var panel = UnityEngine.Object.FindAnyObjectByType<LogisticsPanel>();
            yield return Frames(5);
            var trucks = Server.Trucks.Select(x => x.Id).ToList();
            yield return ClickElement(panel.Window.Q("logistics-buy-supplier-truck"), "Buy truck", () => panel.HasPendingRequests);
            yield return Until(() => !panel.HasPendingRequests, "the truck purchase", 10f);
            if (panel.LastRejection != null) throw Failure("truck refused: " + panel.LastRejection);
            yield return Until(() => Client.Trucks.Any(x => !trucks.Contains(x.Id)), "the truck in the baseline", 5f);
            var truck = Client.Trucks.First(x => !trucks.Contains(x.Id)).Id;
            yield return Choose(panel, $"logistics-{LogisticsPanel.PickupField}-{LogisticsPanel.NewRouteKey}", () => DraftField(Draft(panel, LogisticsPanel.NewRouteKey), "PickupDockId"), () => DraftField(Draft(panel, LogisticsPanel.NewRouteKey), "PickupDockId") == pickup, "Load at");
            yield return Choose(panel, $"logistics-{LogisticsPanel.DropoffField}-{LogisticsPanel.NewRouteKey}", () => DraftField(Draft(panel, LogisticsPanel.NewRouteKey), "DropoffDockId"), () => DraftField(Draft(panel, LogisticsPanel.NewRouteKey), "DropoffDockId") == dropoff, "Deliver to");
            var routes = Server.Routes.Select(x => x.Id).ToList();
            yield return ClickElement(panel.Window.Q("logistics-create-route"), "Create route", () => panel.HasPendingRequests);
            yield return Until(() => !panel.HasPendingRequests, "the route reply", 10f);
            if (panel.LastRejection != null) throw Failure("route refused: " + panel.LastRejection);
            yield return Until(() => Client.Routes.Any(x => !routes.Contains(x.Id)), "the route in the baseline", 5f);
            var route = Client.Routes.First(x => !routes.Contains(x.Id)).Id;
            yield return Frames(5);
            yield return Choose(panel, $"logistics-route-{truck}", () => TruckDraft(panel, truck), () => TruckDraft(panel, truck) == route, "Truck route");
            yield return ClickElement(panel.Window.Q($"logistics-assign-{truck}"), "Assign", () => panel.HasPendingRequests);
            yield return Until(() => !panel.HasPendingRequests, "the assignment reply", 10f);
            if (panel.LastRejection != null) throw Failure("assignment refused: " + panel.LastRejection);
            yield return Press(Key.L);
            Note($"UI clicks so far: {_uiByDevice} taken from the virtual mouse, {_uiByEvent} needed a UI pointer event, {_uiUnpickable} controls not pickable");
        }
    }
}
