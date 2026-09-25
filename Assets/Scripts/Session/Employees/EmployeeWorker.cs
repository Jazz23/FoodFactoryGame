// PROTOTYPE scriptable employee, spawned by the server from its saved record (GoodsEmployee in the world save; SessionRoot).
// A granted player sends a Lua program (EmployeeScript); only the server runs it, steering a
// NavMeshAgent and moving goods through GoodsNetworkBridge.WorkerTransfer, the same validated, durable transfer path as a
// player's. The employee is a goods actor with its own site grant and carried inventory (carried:<_employeeId>), so goods in
// its hands are ordinary lots: stopping or replacing a script never deletes or duplicates them. A transfer needs the
// employee within reach of the place's footprint on the server. NetworkTransform replicates the pose; every peer animates
// from observed movement and shows the carried box from the replicated Carrying flag. The pose and the assigned script (and
// whether it runs) are saved with the record; after a restart a running script starts again from its first line.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using MoonSharp.Interpreter;
using UnityEngine;
using UnityEngine.AI;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class EmployeeWorker : NetworkBehaviour
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkRateParameter = Animator.StringToHash("WalkRate");
        private static readonly int CarryingParameter = Animator.StringToHash("Carrying");

        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;
        // The box model parts, shown while the employee's hands hold goods.
        [SerializeField] private Renderer[] carriedBox = Array.Empty<Renderer>();
        // How close (metres, horizontally) to a place's footprint the employee must stand to use it.
        [SerializeField] private float reach = 1.3f;
        [SerializeField] private float walkTimeoutSeconds = 60f;
        // Ground speed at which the walk clip's feet do not slide; the clip is sped up or slowed to match actual speed.
        [SerializeField] private float walkClipSpeed = 1.5f;
        // How often the server records the pose in the world (saved by the next commit), and the change worth recording.
        [SerializeField] private float poseRecordSeconds = 1f;

        private readonly SyncVar<string> _id = new("");
        private readonly SyncVar<string> _name = new("Employee");
        private readonly SyncVar<bool> _carrying = new();
        private readonly SyncVar<string> _status = new("Idle");
        private readonly SyncVar<string> _source = new("");

        // Server-only, from the saved record (Configure).
        private GoodsEmployee _record;
        private string _employeeId;
        private string _siteId;

        private Vector3 _lastPosition;
        private float _speed;
        private SessionRoot _session;
        private float _nextCarryCheck;
        private float _nextPoseRecord;
        private Vector3 _recordedPosition;
        private float _recordedYaw;
        private EmployeeScript _script;
        // The script's last say()/print(), kept on the status line after it ends.
        private string _lastMessage;

        public string EmployeeId => _id.Value;
        public string DisplayName => _name.Value;
        public bool Carrying => _carrying.Value;
        public string Status => _status.Value;
        // The last program assigned to this employee (saved with it), so the script screen can show it again.
        public string Source => _source.Value;
        private string HandsId => GoodsWorld.InventoryLocationId(_employeeId);

        private void Awake()
        {
            // Only the server steers; enabled in OnStartServer.
            agent.enabled = false;
            _lastPosition = transform.position;
            _carrying.OnChange += (_, next, _) => ShowBox(next);
            ShowBox(false);
        }

        // Server-only, before spawning: the saved record this worker acts for.
        public void Configure(GoodsEmployee record)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _employeeId = record.Id;
            _siteId = record.SiteId;
        }

        public override void OnStartServer()
        {
            if (_record == null) throw new InvalidOperationException("An employee is spawned from a saved record (Configure) only.");
            _session = FindAnyObjectByType<SessionRoot>();
            _id.Value = _employeeId;
            _name.Value = string.IsNullOrWhiteSpace(_record.Name) ? _employeeId : _record.Name;
            _source.Value = _record.Script ?? "";
            agent.enabled = true;
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.stoppingDistance = 0.05f;
            _recordedPosition = transform.position;
            _recordedYaw = transform.eulerAngles.y;
            // Interpreter state is not saved, so a script that was running starts again from its first line.
            if (_record.ScriptRunning && !string.IsNullOrEmpty(_record.Script)) Begin(_record.Script, true);
        }

        public override void OnStopServer()
        {
            _script = null;
            agent.enabled = false;
        }

        public override void OnStartClient() => ShowBox(_carrying.Value);

        // Client request: run this program, replacing any running one. Goods in the employee's hands stay there.
        public void RequestRun(string source)
        {
            if (IsClientStarted) ServerRun(source ?? "");
        }

        public void RequestStop()
        {
            if (IsClientStarted) ServerStop();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRun(string source, NetworkConnection sender = null)
        {
            if (!Authorized(sender)) return;
            Halt();
            Begin(source, false);
        }

        // Compiles the program and saves it as this employee's running assignment before it starts; neither happens if the
        // program does not compile or the save fails.
        private void Begin(string source, bool restarted)
        {
            EmployeeScript script;
            try
            {
                script = new EmployeeScript(source, Operations(), Queries(), Say);
            }
            catch (Exception error) when (error is InterpreterException or ArgumentException)
            {
                _status.Value = "Error: " + (error is InterpreterException lua ? lua.DecoratedMessage ?? lua.Message : error.Message);
                if (restarted) Record(source, false);
                return;
            }
            var problem = Record(source, true);
            if (problem != null)
            {
                _status.Value = "Error: could not save the script (" + problem + ")";
                return;
            }
            _lastMessage = null;
            _script = script;
            _source.Value = source;
            _status.Value = restarted ? "Running (restarted after the server restarted)" : "Running";
            Debug.Log($"[Employee] {_employeeId} {(restarted ? "restarted its saved" : "started a")} script ({source.Length} characters).");
        }

        // Saves the assignment with the employee; null when saved, otherwise the reason.
        private string Record(string source, bool running)
        {
            var bridge = Bridge;
            return bridge == null ? "persistence-unavailable" : bridge.RecordWorkerScript(_employeeId, source, running);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerStop(NetworkConnection sender = null)
        {
            if (!Authorized(sender)) return;
            var wasRunning = _script != null;
            Halt();
            if (wasRunning) Record(_source.Value, false);
            _status.Value = "Stopped";
        }

        private bool Authorized(NetworkConnection sender)
        {
            var bridge = Bridge;
            if (bridge != null && bridge.CanCommand(sender, _siteId)) return true;
            Debug.LogWarning($"[Employee] Refused a command for {_employeeId}: the sender has no grant on {_siteId} or the server is not serving.");
            return false;
        }

        private void Halt()
        {
            _script = null;
            if (agent.enabled && agent.isOnNavMesh) agent.ResetPath();
        }

        private Goods.Network.GoodsNetworkBridge Bridge
        {
            get
            {
                if (_session == null) return null;
                var bridge = _session.ServerBridge;
                return bridge != null && bridge.IsServing ? bridge : null;
            }
        }

        private void Update()
        {
            if (IsServerStarted) ServerUpdate();
            Animate();
        }

        private void ServerUpdate()
        {
            var bridge = Bridge;
            if (bridge == null) return;
            // Goods can also leave the hands through a player's transfer, so the flag follows the world, not the script.
            if (Time.time >= _nextCarryCheck)
            {
                _nextCarryCheck = Time.time + 0.5f;
                RefreshCarrying(bridge.WorkerView(_employeeId, _siteId));
            }
            RecordPose(bridge);
            if (_script == null || _script.Step()) return;
            _status.Value = _script.State == EmployeeScriptState.Failed ? "Error: " + _script.Error
                : "Finished" + (_lastMessage != null ? $" (last said: {_lastMessage})" : "");
            _script = null;
            Record(_source.Value, false);
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        // The pose is saved with the world's next commit (at most one tick-commit interval later), not committed each move.
        private void RecordPose(Goods.Network.GoodsNetworkBridge bridge)
        {
            if (Time.time < _nextPoseRecord) return;
            _nextPoseRecord = Time.time + poseRecordSeconds;
            var yaw = transform.eulerAngles.y;
            if ((transform.position - _recordedPosition).sqrMagnitude < 0.0025f && Mathf.Abs(Mathf.DeltaAngle(yaw, _recordedYaw)) < 2f) return;
            _recordedPosition = transform.position;
            _recordedYaw = yaw;
            bridge.RecordWorkerPose(_employeeId, _recordedPosition, yaw);
        }

        private void RefreshCarrying(GoodsSnapshot view)
        {
            if (view == null) return;
            var carrying = view.Lots.Any(x => x.LocationId == HandsId);
            if (_carrying.Value != carrying) _carrying.Value = carrying;
        }

        private void Say(string text)
        {
            _lastMessage = text;
            _status.Value = "Running: " + text;
            Debug.Log($"[Employee] {_employeeId}: {text}");
        }

        // ---- Lua API ----

        private Dictionary<string, EmployeeScript.Operation> Operations() => new()
        {
            ["move_to"] = MoveTo,
            ["take"] = Take,
            ["put"] = Put,
            ["wait"] = Wait
        };

        private Dictionary<string, Func<Script, CallbackArguments, DynValue>> Queries() => new()
        {
            ["count"] = Count,
            ["find"] = Find,
            ["carrying"] = (_, _) => DynValue.NewNumber(View()?.Lots.Where(x => x.LocationId == HandsId).Sum(x => x.Quantity) ?? 0),
            ["say"] = (_, args) =>
            {
                Say(string.Join(" ", args.GetArray().Select(x => x.ToPrintString())));
                return DynValue.Nil;
            }
        };

        private GoodsSnapshot View() => Bridge?.WorkerView(_employeeId, _siteId);

        // move_to(place): walks next to a place. Returns true, or false and a reason.
        private IEnumerator MoveTo(CallbackArguments args, EmployeeScript.Result result)
        {
            var place = Resolve(OptionalString(args, 0), View());
            if (place.Problem != null)
            {
                result.Set(DynValue.False, DynValue.NewString(place.Problem));
                yield break;
            }
            _status.Value = $"Running: walking to {place.Name}";
            var walk = Walk(place, result);
            while (walk.MoveNext()) yield return null;
            if (result.Value.IsNil()) result.Set(DynValue.True);
        }

        // take(place, item, amount): walks to a place and picks up to amount units of an item (any item if nil; as many as fit if
        // amount is nil). Unspoiled goods only. Returns the number taken, and a reason when it is 0.
        private IEnumerator Take(CallbackArguments args, EmployeeScript.Result result)
        {
            var target = OptionalString(args, 0) ?? "storage";
            var item = OptionalString(args, 1);
            var amount = OptionalAmount(args, 2);
            var place = Resolve(target, View());
            if (place.Problem != null)
            {
                result.Set(DynValue.NewNumber(0), DynValue.NewString(place.Problem));
                yield break;
            }
            _status.Value = $"Running: fetching {(item ?? "goods")} from {place.Name}";
            var walk = Walk(place, result);
            while (walk.MoveNext()) yield return null;
            if (!result.Value.IsNil()) yield break;
            var (moved, reason) = MoveGoods(place.TakeFrom, HandsId, item, amount, false);
            result.Set(DynValue.NewNumber(moved), moved > 0 ? DynValue.Nil : DynValue.NewString(reason));
        }

        // put(place, item, amount): walks to a place and puts carried goods in (all items if nil, all units if amount is nil).
        // Returns the number put, and a reason when it is 0.
        private IEnumerator Put(CallbackArguments args, EmployeeScript.Result result)
        {
            var target = OptionalString(args, 0) ?? "storage";
            var item = OptionalString(args, 1);
            var amount = OptionalAmount(args, 2);
            var place = Resolve(target, View());
            if (place.Problem != null)
            {
                result.Set(DynValue.NewNumber(0), DynValue.NewString(place.Problem));
                yield break;
            }
            _status.Value = $"Running: delivering {(item ?? "goods")} to {place.Name}";
            var walk = Walk(place, result);
            while (walk.MoveNext()) yield return null;
            if (!result.Value.IsNil()) yield break;
            var (moved, reason) = MoveGoods(new[] { HandsId }, place.PutInto, item, amount, true);
            result.Set(DynValue.NewNumber(moved), moved > 0 ? DynValue.Nil : DynValue.NewString(reason));
        }

        // wait(seconds), at most an hour.
        private IEnumerator Wait(CallbackArguments args, EmployeeScript.Result result)
        {
            var seconds = Mathf.Clamp((float)(args[0].CastToNumber() ?? 0), 0f, 3600f);
            var end = Time.time + seconds;
            while (Time.time < end) yield return null;
            result.Set(DynValue.True);
        }

        // count(place, item): units of an item (all items if nil) at a place; "hands" counts what the employee carries.
        private DynValue Count(Script _, CallbackArguments args)
        {
            var view = View();
            var target = OptionalString(args, 0) ?? "hands";
            var item = OptionalString(args, 1);
            var place = Resolve(target, view);
            if (view == null || place.Problem != null) return DynValue.NewNumber(0);
            var locations = new HashSet<string>(place.TakeFrom.Append(place.PutInto));
            return DynValue.NewNumber(view.Lots.Where(x => locations.Contains(x.LocationId) && (item == null || x.ItemId == item))
                .Sum(x => x.Quantity));
        }

        // find(kind): IDs of placed ground-floor machines of a kind, nearest first.
        private DynValue Find(Script lua, CallbackArguments args)
        {
            var kind = OptionalString(args, 0);
            var view = View();
            var layout = view?.SiteLayouts.FirstOrDefault(x => x.SiteId == _siteId);
            var table = new Table(lua);
            if (layout == null) return DynValue.NewTable(table);
            foreach (var equipment in view.Equipment
                         .Where(x => x.State == EquipmentState.Placed && x.Level == 0 && (kind == null || x.Kind == kind))
                         .OrderBy(x => (SiteGridSpace.Center(layout, x) - transform.position).sqrMagnitude))
                table.Append(DynValue.NewString(equipment.Id));
            return DynValue.NewTable(table);
        }

        private static string OptionalString(CallbackArguments args, int index)
        {
            var value = args[index];
            return value.IsNil() ? null : value.CastToString();
        }

        private static int OptionalAmount(CallbackArguments args, int index)
        {
            var value = args[index];
            if (value.IsNil()) return int.MaxValue;
            var number = value.CastToNumber() ?? throw new ScriptRuntimeException("amount must be a number");
            return (int)Math.Max(0, Math.Min(int.MaxValue, Math.Floor(number)));
        }

        // ---- Places ----

        private sealed class Place
        {
            public string Name;
            public string Problem;
            public Rect Area;
            // Locations take() empties, in order, and the one put() fills.
            public string[] TakeFrom = Array.Empty<string>();
            public string PutInto;
        }

        // A place is "hands", a marked location's alias or ID ("storage"), a machine ID, a machine kind (the nearest one), or
        // a machine buffer ID ("<machine>:in" / "<machine>:out"). Machines give their output first, then input, and take
        // deliveries into their input. Only ground-floor machines are reachable (the NavMesh covers the ground only).
        private Place Resolve(string target, GoodsSnapshot view)
        {
            if (string.IsNullOrWhiteSpace(target)) return new Place { Problem = "no place given" };
            if (view == null) return new Place { Name = target, Problem = "the world is not available" };
            if (target == "hands" || target == HandsId)
                return new Place { Name = "hands", TakeFrom = new[] { HandsId }, PutInto = HandsId, Area = new Rect(transform.position.x, transform.position.z, 0f, 0f) };
            var marker = FindObjectsByType<SiteLocationMarker>()
                .FirstOrDefault(x => x.Alias == target || x.LocationId == target);
            if (marker != null)
                return view.Locations.Any(x => x.Id == marker.LocationId)
                    ? new Place { Name = marker.Alias, Area = marker.Area, TakeFrom = new[] { marker.LocationId }, PutInto = marker.LocationId }
                    : new Place { Name = target, Problem = $"'{target}' is not on this site" };
            var layout = view.SiteLayouts.FirstOrDefault(x => x.SiteId == _siteId);
            var buffer = target.EndsWith(":in", StringComparison.Ordinal) || target.EndsWith(":out", StringComparison.Ordinal);
            var machineId = buffer ? target.Substring(0, target.LastIndexOf(':')) : target;
            var placed = view.Equipment.Where(x => x.State == EquipmentState.Placed).ToList();
            var machine = placed.FirstOrDefault(x => x.Id == machineId)
                ?? placed.Where(x => x.Kind == target && x.Level == 0)
                    .OrderBy(x => layout == null ? 0f : (SiteGridSpace.Center(layout, x) - transform.position).sqrMagnitude)
                    .FirstOrDefault();
            if (machine == null || layout == null)
                return new Place { Name = target, Problem = $"no place called '{target}' (try \"storage\", a machine kind such as \"oven\", or an ID from find())" };
            if (machine.Level != 0) return new Place { Name = machine.Id, Problem = $"{machine.Id} is on an upper floor" };
            var (width, depth) = SiteGrid.Footprint(machine.Width, machine.Depth, machine.Rotation);
            var center = SiteGridSpace.Center(layout, machine);
            var size = new Vector2(width * SiteGrid.CellSize, depth * SiteGrid.CellSize);
            var input = machine.Id + ":in";
            var output = machine.Id + ":out";
            return new Place
            {
                Name = machine.Id,
                Area = new Rect(center.x - size.x * 0.5f, center.z - size.y * 0.5f, size.x, size.y),
                TakeFrom = buffer ? new[] { target } : new[] { output, input },
                PutInto = buffer ? target : input
            };
        }

        // ---- Movement ----

        private float DistanceTo(Rect area)
        {
            var position = new Vector2(transform.position.x, transform.position.z);
            var closest = new Vector2(Mathf.Clamp(position.x, area.xMin, area.xMax), Mathf.Clamp(position.y, area.yMin, area.yMax));
            return Vector2.Distance(position, closest);
        }

        // Walks until within reach of the place, re-pathing as obstacles change. Leaves result nil on arrival, or sets
        // (false/0, reason) if the place cannot be reached.
        private IEnumerator Walk(Place place, EmployeeScript.Result result)
        {
            var deadline = Time.time + walkTimeoutSeconds;
            var nextPath = 0f;
            while (DistanceTo(place.Area) > reach)
            {
                if (!agent.isOnNavMesh || Time.time > deadline)
                {
                    result.Set(DynValue.False, DynValue.NewString($"could not reach {place.Name}"));
                    agent.ResetPath();
                    yield break;
                }
                if (Time.time >= nextPath)
                {
                    nextPath = Time.time + 0.5f;
                    if (!TryApproachPoint(place.Area, out var point) || !agent.SetDestination(point))
                    {
                        result.Set(DynValue.False, DynValue.NewString($"no path to {place.Name}"));
                        yield break;
                    }
                }
                // Arrived at the end of a (partial) path but still out of reach: the place is walled off.
                if (!agent.pathPending && agent.hasPath && agent.remainingDistance <= 0.05f && agent.pathStatus != NavMeshPathStatus.PathComplete)
                {
                    result.Set(DynValue.False, DynValue.NewString($"could not reach {place.Name}"));
                    agent.ResetPath();
                    yield break;
                }
                yield return null;
            }
            agent.ResetPath();
            // Face the place before using it.
            var look = new Vector3(place.Area.center.x, transform.position.y, place.Area.center.y) - transform.position;
            if (look.sqrMagnitude < 0.0001f) yield break;
            var facing = Quaternion.LookRotation(look);
            for (var turned = 0f; turned < 0.3f && Quaternion.Angle(transform.rotation, facing) > 2f; turned += Time.deltaTime)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, 540f * Time.deltaTime);
                yield return null;
            }
        }

        // A walkable point just outside the footprint, on the side facing the employee.
        private bool TryApproachPoint(Rect area, out Vector3 point)
        {
            var margin = agent.radius + 0.3f;
            var outer = new Rect(area.xMin - margin, area.yMin - margin, area.width + margin * 2f, area.height + margin * 2f);
            var position = transform.position;
            var x = Mathf.Clamp(position.x, outer.xMin, outer.xMax);
            var z = Mathf.Clamp(position.z, outer.yMin, outer.yMax);
            if (area.Contains(new Vector2(x, z)))
            {
                // Inside the footprint: step out through the nearest edge.
                var toLeft = x - outer.xMin;
                var toRight = outer.xMax - x;
                var toBottom = z - outer.yMin;
                var toTop = outer.yMax - z;
                var least = Mathf.Min(Mathf.Min(toLeft, toRight), Mathf.Min(toBottom, toTop));
                if (least == toLeft) x = outer.xMin;
                else if (least == toRight) x = outer.xMax;
                else if (least == toBottom) z = outer.yMin;
                else z = outer.yMax;
            }
            if (NavMesh.SamplePosition(new Vector3(x, position.y, z), out var hit, reach, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
            var center = new Vector3(area.center.x, position.y, area.center.y);
            if (NavMesh.SamplePosition(center, out hit, Mathf.Max(area.width, area.height) * 0.5f + reach, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
            point = default;
            return false;
        }

        // ---- Goods ----

        // Moves up to amount units (of one item, or any) from the sources into the destination, one server transfer per lot,
        // as many as the destination's slots take. Returns the units moved and, if none, the reason.
        private (int Moved, string Reason) MoveGoods(IReadOnlyList<string> sources, string destination, string item, int amount,
            bool includeSpoiled)
        {
            var bridge = Bridge;
            if (bridge == null) return (0, "the world is not available");
            if (DistanceToLocation(destination, sources) is var distance && distance > reach + 0.25f)
                return (0, "too far away");
            var moved = 0;
            var reason = item == null ? "nothing there" : $"no {item} there";
            foreach (var source in sources)
            {
                while (moved < amount)
                {
                    var view = bridge.WorkerView(_employeeId, _siteId);
                    var lot = view?.Lots
                        .Where(x => x.LocationId == source && (item == null || x.ItemId == item) && (includeSpoiled || !x.Spoiled))
                        .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal)
                        .FirstOrDefault(x => GoodsSlots.FreeUnits(view, destination, x.ItemId, x.Spoiled, _session.MaxStack) > 0);
                    if (lot == null)
                    {
                        if (view?.Lots.Any(x => x.LocationId == source && (item == null || x.ItemId == item)) == true)
                            reason = source == HandsId ? "no room there" : "hands are full";
                        break;
                    }
                    var free = GoodsSlots.FreeUnits(view, destination, lot.ItemId, lot.Spoiled, _session.MaxStack);
                    var take = (int)Math.Min(Math.Min(free, lot.Quantity), amount - moved);
                    var outcome = bridge.WorkerTransfer(_employeeId, new TransferIntent
                    {
                        RequestId = Guid.NewGuid().ToString("N"), LotId = lot.Id, DestinationId = destination, Quantity = take
                    });
                    if (!outcome.Accepted)
                    {
                        reason = outcome.Reason;
                        break;
                    }
                    moved += take;
                }
            }
            RefreshCarrying(bridge.WorkerView(_employeeId, _siteId));
            if (moved > 0) Debug.Log($"[Employee] {_employeeId} moved {moved} {(item ?? "goods")} into {destination}.");
            return (moved, reason);
        }

        // Server reach rule: the employee must stand by the non-hands end of the move. Hands-only moves are always in reach.
        private float DistanceToLocation(string destination, IReadOnlyList<string> sources)
        {
            var other = destination != HandsId ? destination : sources.FirstOrDefault(x => x != HandsId);
            if (other == null) return 0f;
            var place = Resolve(other, View());
            return place.Problem != null ? float.MaxValue : DistanceTo(place.Area);
        }

        // ---- Presentation ----

        private void ShowBox(bool visible)
        {
            foreach (var part in carriedBox)
                if (part != null) part.enabled = visible;
        }

        // Observed planar speed, so remote copies moved by NetworkTransform animate exactly like the server's.
        private void Animate()
        {
            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;
            var measured = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-10f * Time.deltaTime));
            animator.SetFloat(SpeedParameter, _speed);
            animator.SetFloat(WalkRateParameter, Mathf.Max(0.5f, _speed / walkClipSpeed));
            animator.SetBool(CarryingParameter, _carrying.Value);
        }
    }
}
