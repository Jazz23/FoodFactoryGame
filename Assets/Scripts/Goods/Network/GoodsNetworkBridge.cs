// Transports intent, authorized site baselines and nearby competitor crowds; FishNet connections, not payloads, determine identity.
using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Object;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Goods.Network
{
    public sealed class GoodsNetworkBridge : NetworkBehaviour
    {
        private GoodsWorld _world;
        private Func<NetworkConnection, string> _resolvePlayer;
        // Server-only: where a connection's avatar stands on the map (metres), supplied by the session; null when unknown.
        private Func<NetworkConnection, (float X, float Z)?> _mapPositionOf;
        // A connection may watch several granted sites (its own and, for remote management, the company's other sites).
        private readonly Dictionary<NetworkConnection, HashSet<string>> _subscriptions = new();
        private readonly Dictionary<string, long> _clientRevisions = new();
        // A tick exception may restore a lower revision. Epochs let clients accept that rollback and reject late old views.
        private long _serverEpoch;
        private long _clientEpoch = -1;
        private const float StatsIntervalSeconds = 60f;
        // Decision 0016: clock ticks run in memory and are saved this often (player commands still save at once), so a
        // crash loses at most this much simulated time.
        private const long CommitIntervalSeconds = 10;
        private float _clockRemainder;
        private long _uncommittedSeconds;
        private float _statsRemainder;
        private string _savePath;
        private bool _persistenceFailed;

        public event Action<GoodsOutcome> ResultReceived;
        // Client: this player's wage warning setting (decision 0039), in game hours of wages; sent on request and after a change.
        public event Action<int> WageWarningReceived;
        // Server-only workforce hooks from the session (ConfigureWorkforce): where a connection's avatar stands in the scene, so a
        // hire appears beside the player, and what to spawn or despawn once a hire or firing is committed. Without them a
        // scene cannot show employees, so hiring is refused.
        private Func<NetworkConnection, (Vector3 Position, float Yaw)?> _scenePoseOf;
        private Action<GoodsEmployee> _hired;
        private Action<string> _fired;
        // Server-only player settings store (the SQLite player registry, decision 0011), from the session.
        private Func<string, int> _wageWarningOf;
        private Func<string, int, bool> _saveWageWarning;
        public const int MaxWageWarningHours = 48;
        public event Action<GoodsSnapshot> SiteReceived;
        // The customers at competitors near this client's avatar (decision 0033); presentation only.
        public event Action<GoodsCrowdView> CrowdReceived;
        // Server: the crowd signature each connection last received, so an unchanged crowd is not resent.
        private readonly Dictionary<NetworkConnection, string> _crowdSent = new();

        // Called by the server's session/bootstrap owner after authenticating connection identities.
        public void InitializeServer(GoodsWorld world, Func<NetworkConnection, string> resolvePlayer, string savePath,
            Func<NetworkConnection, (float X, float Z)?> mapPositionOf = null)
        {
            if (!IsServerStarted || world == null || resolvePlayer == null || string.IsNullOrWhiteSpace(savePath))
                throw new InvalidOperationException("A running server world, identity resolver and save path are required.");
            // The composition owner must first create or explicitly recover this save; never overwrite it implicitly.
            if (world.HasUncommittedChanges || !File.Exists(savePath)
                || JsonUtility.ToJson(GoodsSnapshotStore.Load(savePath).Snapshot()) != JsonUtility.ToJson(world.Snapshot()))
                throw new InvalidOperationException("Server world must match its committed snapshot before accepting requests.");
            _world = world;
            _resolvePlayer = resolvePlayer;
            _mapPositionOf = mapPositionOf;
            _savePath = savePath;
            // The served save commits repeatedly, so it keeps one WAL connection open until the server stops.
            GoodsSnapshotStore.Hold(savePath);
            // Decision 0012 measurements describe this served world, not earlier saves in the same process.
            GoodsSnapshotStore.Stats.Reset();
        }

        // Server-only: lets this scene hire and fire employees (see the fields above).
        public void ConfigureWorkforce(Func<NetworkConnection, (Vector3 Position, float Yaw)?> scenePoseOf, Action<GoodsEmployee> hired,
            Action<string> fired)
        {
            _scenePoseOf = scenePoseOf;
            _hired = hired;
            _fired = fired;
        }

        // Server-only: where each player's wage warning setting is kept.
        public void ConfigurePlayerSettings(Func<string, int> wageWarningOf, Func<string, int, bool> saveWageWarning)
        {
            _wageWarningOf = wageWarningOf;
            _saveWageWarning = saveWageWarning;
        }

        public override void OnStopServer()
        {
            _scenePoseOf = null;
            _hired = null;
            _fired = null;
            _wageWarningOf = null;
            _saveWageWarning = null;
            _subscriptions.Clear();
            _crowdSent.Clear();
            CloseSave();
            _world = null;
            _resolvePlayer = null;
            _mapPositionOf = null;
            _savePath = null;
            _clockRemainder = 0;
            _uncommittedSeconds = 0;
            _statsRemainder = 0;
            _persistenceFailed = false;
            _serverEpoch = 0;
        }

        public override void OnStopClient()
        {
            _clientRevisions.Clear();
            _clientEpoch = -1;
        }

        // Backstop for a bridge destroyed without OnStopServer, so pending ticks are saved and the held save's files can
        // be closed and deleted.
        private void OnDestroy() => CloseSave();

        // A clean stop saves ticks not yet committed; only a crash loses them.
        private void CloseSave()
        {
            if (_world != null && _savePath != null && !_world.TryCommitDurably(_savePath))
                Debug.LogWarning("[Goods] Could not save the last clock ticks while stopping; the save keeps its previous revision.");
            GoodsSnapshotStore.Release(_savePath);
        }

        private void Update()
        {
            if (!IsServerStarted || _world == null) return;
            // Decision 0012 measurement: one summary line a minute of what world commits cost.
            _statsRemainder += Time.unscaledDeltaTime;
            if (_statsRemainder >= StatsIntervalSeconds)
            {
                _statsRemainder = 0;
                Debug.Log(GoodsSnapshotStore.Stats.Summary());
            }
            _clockRemainder += Time.unscaledDeltaTime;
            if (_clockRemainder < 1f) return;
            // While a commit is failing the clock waits, so memory never runs more than one interval ahead of the save.
            if (_persistenceFailed && !TryCommit()) return;
            var seconds = (long)_clockRemainder;
            try { _world.AdvanceUncommitted(seconds); }
            catch (Exception error)
            {
                // The world restored its last save. Send a new baseline epoch even though its revision moved backward.
                _clockRemainder = 0;
                _uncommittedSeconds = 0;
                _serverEpoch++;
                Debug.LogError($"[Goods] Clock step failed; restored the last committed world: {error}");
                Broadcast();
                _crowdSent.Clear();
                SendCrowds();
                return;
            }
            _clockRemainder -= seconds;
            _uncommittedSeconds += seconds;
            if (_uncommittedSeconds >= CommitIntervalSeconds) TryCommit();
            Broadcast();
            SendCrowds();
        }

        // Once per clock step (at most once a second): each subscribed connection gets the customers at competitors within
        // GoodsWorld.CrowdRadiusMetres of the server's copy of its avatar (decision 0033), or an empty crowd without a position.
        // Only a changed crowd is sent. This decides what a client is told, never what the simulation does.
        private void SendCrowds()
        {
            foreach (var connection in new List<NetworkConnection>(_subscriptions.Keys))
            {
                if (connection == null || !connection.IsActive) continue;
                var position = _mapPositionOf?.Invoke(connection);
                var crowd = position == null ? new GoodsCrowdView() : _world.CrowdNear(position.Value.X, position.Value.Z);
                var signature = GoodsWorld.CrowdSignature(crowd);
                if (_crowdSent.TryGetValue(connection, out var sent) && sent == signature) continue;
                _crowdSent[connection] = signature;
                TargetCrowd(connection, JsonUtility.ToJson(crowd), _serverEpoch);
            }
            foreach (var gone in new List<NetworkConnection>(_crowdSent.Keys))
                if (!_subscriptions.ContainsKey(gone)) _crowdSent.Remove(gone);
        }

        // Also a no-op when a player command has already saved the pending ticks.
        private bool TryCommit()
        {
            _persistenceFailed = !_world.TryCommitDurably(_savePath);
            if (!_persistenceFailed) _uncommittedSeconds = 0;
            return !_persistenceFailed;
        }

        public void RequestTransfer(string requestId, string lotId, string destinationId, int quantity, string reservationId = "")
        {
            if (IsClientStarted) ServerTransfer(requestId, lotId, destinationId, quantity, reservationId);
        }

        public void RequestSite(string siteId)
        {
            if (IsClientStarted) ServerSubscribe(siteId);
        }

        public void RequestReservation(string requestId, string lotId, int quantity)
        {
            if (IsClientStarted) ServerReserve(requestId, lotId, quantity);
        }

        public void RequestCancellation(string requestId, string reservationId)
        {
            if (IsClientStarted) ServerCancel(requestId, reservationId);
        }

        public void RequestPickUp(string requestId, string equipmentId)
        {
            if (IsClientStarted) ServerPickUp(requestId, equipmentId);
        }

        // Level is the floor to place on: 0 is the ground, higher levels are upper floors of a building (decision 0020).
        public void RequestPlace(string requestId, string equipmentId, int cellX, int cellZ, int rotation, int level = 0)
        {
            if (IsClientStarted) ServerPlace(requestId, equipmentId, cellX, cellZ, rotation, level);
        }

        // Hands a machine the player holds to an employee on its site, so the employee's script can place it (GiveDurably).
        public void RequestGive(string requestId, string equipmentId, string employeeId)
        {
            if (IsClientStarted) ServerGive(requestId, equipmentId, employeeId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerGive(string requestId, string equipmentId, string employeeId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.GiveDurably(player, requestId, equipmentId, employeeId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Pays for one more floor of a factory; the first one puts the elevator at the given interior cell (AddFloorDurably).
        public void RequestAddFloor(string requestId, string buildingId, int elevatorX, int elevatorZ)
        {
            if (IsClientStarted) ServerAddFloor(requestId, buildingId, elevatorX, elevatorZ);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerAddFloor(string requestId, string buildingId, int elevatorX, int elevatorZ, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.AddFloorDurably(player, requestId, buildingId, elevatorX, elevatorZ, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // One batch per request: the server checks the station, recipe, busy state and inputs (StartJobDurably).
        public void RequestStartJob(string requestId, string stationId, string recipeId)
        {
            if (IsClientStarted) ServerStartJob(requestId, stationId, recipeId);
        }

        // Switches a manually powered machine (the oven) on or off (SetPowerDurably, decision 0037).
        public void RequestSetPower(string requestId, string equipmentId, bool on)
        {
            if (IsClientStarted) ServerSetPower(requestId, equipmentId, on);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerSetPower(string requestId, string equipmentId, bool on, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.SetPowerDurably(player, requestId, equipmentId, on, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Hires an employee for the site the player stands on; it appears beside them (HireDurably, decision 0039).
        public void RequestHire(string requestId)
        {
            if (IsClientStarted) ServerHire(requestId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerHire(string requestId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var pose = _hired == null ? null : _scenePoseOf?.Invoke(sender);
            var result = pose == null
                ? new GoodsOutcome { RequestId = requestId, PlayerId = player, Accepted = false, Reason = "hiring-unavailable" }
                : _world.HireDurably(player, requestId, pose.Value.Position.x, pose.Value.Position.y, pose.Value.Position.z,
                    pose.Value.Yaw, _savePath);
            Reply(sender, result);
            if (!result.Accepted) return;
            // A replayed hire names the same employee; the session spawns a worker only for a record that has none.
            var record = _world.Employees().Find(x => x.Id == result.EquipmentId);
            if (record != null) _hired?.Invoke(record);
            Broadcast();
        }

        // Fires an employee whose hands are empty (FireDurably, decision 0039).
        public void RequestFire(string requestId, string employeeId)
        {
            if (IsClientStarted) ServerFire(requestId, employeeId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerFire(string requestId, string employeeId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.FireDurably(player, requestId, employeeId, _savePath);
            Reply(sender, result);
            if (!result.Accepted) return;
            _fired?.Invoke(result.EquipmentId);
            Broadcast();
        }

        // Asks for this player's wage warning setting; the answer arrives as WageWarningReceived.
        public void RequestWageWarning()
        {
            if (IsClientStarted) ServerWageWarning(-1);
        }

        // Saves this player's wage warning setting (0 to MaxWageWarningHours game hours of wages left in company cash).
        public void RequestSetWageWarning(int hours)
        {
            if (IsClientStarted) ServerWageWarning(Mathf.Clamp(hours, 0, MaxWageWarningHours));
        }

        // hours < 0 only reads. The reply is what the store holds afterwards, so a failed save shows the old value.
        [ServerRpc(RequireOwnership = false)]
        private void ServerWageWarning(int hours, NetworkConnection sender = null)
        {
            if (sender == null || _world == null || _resolvePlayer == null || _wageWarningOf == null) return;
            var player = _resolvePlayer(sender);
            if (string.IsNullOrWhiteSpace(player)) return;
            if (hours >= 0 && _saveWageWarning?.Invoke(player, Mathf.Clamp(hours, 0, MaxWageWarningHours)) != true)
                Debug.LogWarning($"[Goods] Could not save the wage warning setting of {player}.");
            TargetWageWarning(sender, _wageWarningOf(player));
        }

        [TargetRpc]
        private void TargetWageWarning(NetworkConnection connection, int hours) => WageWarningReceived?.Invoke(hours);

        // Places a belt from the player's inventory on an empty cell, or turns the belt already there (PlaceBeltDurably).
        public void RequestPlaceBelt(string requestId, string siteId, int cellX, int cellZ, int direction, int level = 0)
        {
            if (IsClientStarted) ServerPlaceBelt(requestId, siteId, cellX, cellZ, direction, level);
        }

        // Places a conveyor lift (lift +1 up, -1 down) from the player's inventory, or turns the lift already there
        // (PlaceLiftDurably). Level is the floor it takes items on.
        public void RequestPlaceLift(string requestId, string siteId, int cellX, int cellZ, int direction, int level, int lift)
        {
            if (IsClientStarted) ServerPlaceLift(requestId, siteId, cellX, cellZ, direction, level, lift);
        }

        public void RequestRemoveBelt(string requestId, string beltId)
        {
            if (IsClientStarted) ServerRemoveBelt(requestId, beltId);
        }

        // Puts one unit of a lot on a belt (PlaceOnBeltDurably).
        public void RequestPlaceOnBelt(string requestId, string lotId, string beltId)
        {
            if (IsClientStarted) ServerPlaceOnBelt(requestId, lotId, beltId);
        }

        // Buys one supplier offer with the site company's cash: goods or a machine into the requester's inventory, or a truck
        // parked at the site (BuyDurably).
        public void RequestPurchase(string requestId, string siteId, string offerId)
        {
            if (IsClientStarted) ServerPurchase(requestId, siteId, offerId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPurchase(string requestId, string siteId, string offerId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.BuyDurably(player, requestId, siteId, offerId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Buys a listed lot of the world layout with the paying site's company cash (decision 0028, BuyPropertyDurably). An
        // accepted purchase is broadcast, so every subscriber's baseline shows the new owner.
        public void RequestBuyProperty(string requestId, string payingSiteId, string lotId)
        {
            if (IsClientStarted) ServerBuyProperty(requestId, payingSiteId, lotId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerBuyProperty(string requestId, string payingSiteId, string lotId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.BuyPropertyDurably(player, requestId, payingSiteId, lotId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Restaurant building (decision 0034): a shell order (resize, interior walls, doors, windows, removals, wall finish), sent
        // as the JSON of a ShellOrder; the server plans it again and pays or refunds once (OrderShellDurably).
        public void RequestShellOrder(string requestId, ShellOrder order)
        {
            if (IsClientStarted && order != null) ServerShellOrder(requestId, JsonUtility.ToJson(order));
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerShellOrder(string requestId, string orderJson, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.OrderShellDurably(player, requestId, Parse<ShellOrder>(orderJson), _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Buys an equipment offer once per placement and places the pieces at once (BuyAndPlaceDurably), as the JSON of a FurnishOrder.
        public void RequestBuyAndPlace(string requestId, FurnishOrder order)
        {
            if (IsClientStarted && order != null) ServerBuyAndPlace(requestId, JsonUtility.ToJson(order));
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerBuyAndPlace(string requestId, string orderJson, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.BuyAndPlaceDurably(player, requestId, Parse<FurnishOrder>(orderJson), _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Sells a placed or held piece back for exactly what it was charged; its goods go to the seller's inventory (SellDurably).
        public void RequestSell(string requestId, string equipmentId)
        {
            if (IsClientStarted) ServerSell(requestId, equipmentId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerSell(string requestId, string equipmentId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.SellDurably(player, requestId, equipmentId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Sets who works a register: the requester's own player ID, an employee, or empty to leave it (StaffDurably).
        public void RequestStaff(string requestId, string registerId, string staffId)
        {
            if (IsClientStarted) ServerStaff(requestId, registerId, staffId ?? "");
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerStaff(string requestId, string registerId, string staffId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.StaffDurably(player, requestId, registerId, staffId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Server-only: a player who left stops working their register (decision 0034); the change is committed and broadcast.
        public void ReleaseStaff(string playerId)
        {
            if (IsServing && !string.IsNullOrWhiteSpace(playerId) && _world.ReleaseStaffDurably(playerId, _savePath)) Broadcast();
        }

        // A malformed payload reads as null, which every command rejects.
        private static T Parse<T>(string json) where T : class
        {
            try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        // Enters another owned lot, carrying the player's goods and held machines there (decisions 0029, 0031). The position
        // checked is the server's own copy of this connection's avatar, never a value from the request.
        public void RequestEnterSite(string requestId, string siteId)
        {
            if (IsClientStarted) ServerEnterSite(requestId, siteId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerEnterSite(string requestId, string siteId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var position = _mapPositionOf?.Invoke(sender);
            var result = position == null
                ? new GoodsOutcome { RequestId = requestId, PlayerId = player, Accepted = false, Reason = "no-position" }
                : _world.EnterSiteDurably(player, requestId, siteId, position.Value.X, position.Value.Z, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Makes a company route between two docks, with the items its trucks may load (empty for any) (CreateRouteDurably).
        public void RequestCreateRoute(string requestId, string pickupDockId, string dropoffDockId, string[] allowedItemIds)
        {
            if (IsClientStarted) ServerCreateRoute(requestId, pickupDockId, dropoffDockId, allowedItemIds ?? Array.Empty<string>());
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerCreateRoute(string requestId, string pickupDockId, string dropoffDockId, string[] allowedItemIds,
            NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.CreateRouteDurably(player, requestId, pickupDockId, dropoffDockId, allowedItemIds, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Changes a route's docks and cargo; its trucks follow the new route (SetRouteDurably).
        public void RequestSetRoute(string requestId, string routeId, string pickupDockId, string dropoffDockId, string[] allowedItemIds)
        {
            if (IsClientStarted) ServerSetRoute(requestId, routeId, pickupDockId, dropoffDockId, allowedItemIds ?? Array.Empty<string>());
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerSetRoute(string requestId, string routeId, string pickupDockId, string dropoffDockId, string[] allowedItemIds,
            NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.SetRouteDurably(player, requestId, routeId, pickupDockId, dropoffDockId, allowedItemIds, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Removes a route and parks its trucks (DeleteRouteDurably).
        public void RequestDeleteRoute(string requestId, string routeId)
        {
            if (IsClientStarted) ServerDeleteRoute(requestId, routeId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerDeleteRoute(string requestId, string routeId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.DeleteRouteDurably(player, requestId, routeId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Puts a truck on a route, or parks it with an empty route (AssignTruckDurably).
        public void RequestAssignTruck(string requestId, string truckId, string routeId)
        {
            if (IsClientStarted) ServerAssignTruck(requestId, truckId, routeId ?? "");
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerAssignTruck(string requestId, string truckId, string routeId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.AssignTruckDurably(player, requestId, truckId, routeId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPlaceBelt(string requestId, string siteId, int cellX, int cellZ, int direction, int level, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceBeltDurably(player, requestId, siteId, cellX, cellZ, direction, _savePath, level);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPlaceLift(string requestId, string siteId, int cellX, int cellZ, int direction, int level, int lift,
            NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceLiftDurably(player, requestId, siteId, cellX, cellZ, direction, level, lift, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRemoveBelt(string requestId, string beltId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.RemoveBeltDurably(player, requestId, beltId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPlaceOnBelt(string requestId, string lotId, string beltId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceOnBeltDurably(player, requestId, lotId, beltId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Takes one riding item off a belt into the requester's inventory (TakeFromBeltDurably).
        public void RequestTakeFromBelt(string requestId, string lotId)
        {
            if (IsClientStarted) ServerTakeFromBelt(requestId, lotId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerTakeFromBelt(string requestId, string lotId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.TakeFromBeltDurably(player, requestId, lotId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerStartJob(string requestId, string stationId, string recipeId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.StartJobDurably(player, requestId, stationId, recipeId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPickUp(string requestId, string equipmentId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PickUpDurably(player, requestId, equipmentId, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerPlace(string requestId, string equipmentId, int cellX, int cellZ, int rotation, int level, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceDurably(player, requestId, equipmentId, cellX, cellZ, rotation, _savePath, level);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerReserve(string requestId, string lotId, int quantity, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            Reply(sender, _world.ReserveDurably(player, requestId, lotId, quantity, _savePath));
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerCancel(string requestId, string reservationId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            Reply(sender, _world.CancelDurably(player, requestId, reservationId, _savePath));
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerTransfer(string requestId, string lotId, string destinationId, int quantity, string reservationId, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.TransferDurably(player, new TransferIntent
            {
                RequestId = requestId, LotId = lotId, DestinationId = destinationId,
                Quantity = quantity, ReservationId = reservationId
            }, _savePath);
            Reply(sender, result);
            if (result.Accepted) Broadcast();
        }

        // Server-only: true once InitializeServer has handed this bridge its world and commands are not paused by a failing save.
        public bool IsServing => IsServerStarted && _world != null && !_persistenceFailed;

        // Server-only: whether the player behind a connection may command things on a site (a site grant, as for viewing).
        public bool CanCommand(NetworkConnection connection, string siteId)
        {
            if (!IsServing || connection == null) return false;
            var player = _resolvePlayer(connection);
            return !string.IsNullOrWhiteSpace(player) && _world.CanView(player, siteId);
        }

        // Server-only: the saved employee records (PROTOTYPE workers: goods actors that are not connections), to spawn them.
        public IReadOnlyList<GoodsEmployee> Employees() => IsServing ? _world.Employees() : Array.Empty<GoodsEmployee>();

        // Server-only: where a worker stands, kept in memory and saved by the next commit.
        public void RecordWorkerPose(string workerId, Vector3 position, float yaw)
        {
            if (IsServing) _world.SetEmployeePose(workerId, position.x, position.y, position.z, yaw);
        }

        // Server-only: a worker's assigned script and whether it runs (and, unless null, its visual task list), committed
        // before returning. Null when saved, otherwise the reason nothing changed.
        public string RecordWorkerScript(string workerId, string script, bool running, string tasks = null)
        {
            if (!IsServing) return "persistence-unavailable";
            return _world.SetEmployeeScriptDurably(workerId, script, running, _savePath, tasks);
        }

        // Server-only: the worker's authorized view of a site (null if it has no grant or the bridge is not serving).
        public GoodsSnapshot WorkerView(string workerId, string siteId) => IsServing ? _world.View(workerId, siteId) : null;

        // Server-only: a worker's transfer takes the same durable, validated path as a player's request and is broadcast.
        public GoodsOutcome WorkerTransfer(string workerId, TransferIntent intent)
        {
            if (!IsServing) return new GoodsOutcome { Accepted = false, Reason = "persistence-unavailable" };
            if (_world.IsUnpaid(workerId)) return new GoodsOutcome { Accepted = false, Reason = "unpaid" };
            var result = _world.TransferDurably(workerId, intent, _savePath);
            if (result.Accepted) Broadcast();
            return result;
        }

        // Server-only: a worker places a machine it holds, picks one up, or lays a belt from its hands, through the same durable,
        // validated paths as a player's requests; accepted changes are broadcast. Ground floor only (the NavMesh covers it).
        public GoodsOutcome WorkerPlace(string workerId, string equipmentId, int cellX, int cellZ, int rotation) =>
            Worker(workerId, requestId => _world.PlaceDurably(workerId, requestId, equipmentId, cellX, cellZ, rotation, _savePath));

        public GoodsOutcome WorkerPickUp(string workerId, string equipmentId) =>
            Worker(workerId, requestId => _world.PickUpDurably(workerId, requestId, equipmentId, _savePath));

        public GoodsOutcome WorkerPlaceBelt(string workerId, string siteId, int cellX, int cellZ, int direction) =>
            Worker(workerId, requestId => _world.PlaceBeltDurably(workerId, requestId, siteId, cellX, cellZ, direction, _savePath));

        public GoodsOutcome WorkerSetPower(string workerId, string equipmentId, bool on) =>
            Worker(workerId, requestId => _world.SetPowerDurably(workerId, requestId, equipmentId, on, _savePath));

        // Server-only content queries for workers: whether a kind needs switching on, and what its recipes consume.
        public bool RequiresPower(string kind) => IsServing && _world.RequiresPower(kind);

        public IReadOnlyList<string> RecipeInputs(string kind) => IsServing ? _world.RecipeInputs(kind) : Array.Empty<string>();

        // Server-only: whether an employee is waiting to be paid (decision 0039); it does no work meanwhile.
        public bool IsUnpaid(string workerId) => IsServing && _world.IsUnpaid(workerId);

        private GoodsOutcome Worker(string workerId, Func<string, GoodsOutcome> request)
        {
            if (!IsServing) return new GoodsOutcome { Accepted = false, Reason = "persistence-unavailable" };
            if (_world.IsUnpaid(workerId)) return new GoodsOutcome { Accepted = false, Reason = "unpaid" };
            var result = request(Guid.NewGuid().ToString("N"));
            if (result.Accepted) Broadcast();
            return result;
        }

        // A subscriber gets a complete revisioned baseline of each site it watches, never unauthorized state or inferred deltas.
        private void Broadcast()
        {
            foreach (var pair in new List<KeyValuePair<NetworkConnection, HashSet<string>>>(_subscriptions))
            foreach (var siteId in new List<string>(pair.Value))
                SendSite(pair.Key, siteId);
        }

        // Adds a site to the connection's watched sites; a refused site is dropped from them and the others stay.
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubscribe(string siteId, NetworkConnection sender = null)
        {
            if (sender == null || _world == null || _resolvePlayer == null) return;
            var player = _resolvePlayer(sender);
            if (string.IsNullOrWhiteSpace(player) || !_world.CanView(player, siteId))
            {
                Unsubscribe(sender, siteId);
                TargetResult(sender, "", false, "subscription-forbidden", "", "", "", 0, 0);
                return;
            }
            if (!_subscriptions.TryGetValue(sender, out var sites)) _subscriptions[sender] = sites = new HashSet<string>();
            sites.Add(siteId);
            SendSite(sender, siteId);
        }

        private void Unsubscribe(NetworkConnection connection, string siteId)
        {
            if (connection == null || !_subscriptions.TryGetValue(connection, out var sites)) return;
            sites.Remove(siteId);
            if (sites.Count == 0) _subscriptions.Remove(connection);
        }

        private void SendSite(NetworkConnection connection, string siteId)
        {
            if (connection == null || !connection.IsActive)
            {
                if (connection != null) _subscriptions.Remove(connection);
                return;
            }
            if (!_world.CanView(_resolvePlayer(connection), siteId))
            {
                Unsubscribe(connection, siteId);
                return;
            }
            var view = _world.View(_resolvePlayer(connection), siteId);
            TargetSite(connection, JsonUtility.ToJson(view), _serverEpoch);
        }

        private bool TryIdentify(NetworkConnection sender, string requestId, out string player)
        {
            player = null;
            if (sender == null || _world == null || _resolvePlayer == null) return false;
            player = _resolvePlayer(sender);
            if (string.IsNullOrWhiteSpace(player) || _persistenceFailed)
            {
                TargetResult(sender, requestId, false,
                    _persistenceFailed ? "persistence-unavailable" : "unauthenticated", "", "", "", 0, 0);
                return false;
            }
            return true;
        }

        private void Reply(NetworkConnection connection, GoodsOutcome result) => TargetResult(connection,
            result.RequestId ?? "", result.Accepted, result.Reason ?? "", result.MovedLotId ?? "",
            result.ReservationId ?? "", result.EquipmentId ?? "", result.Revision, result.Cents);

        // equipmentId: the machine, piece or truck the request created (its ID is not derived from the request, decision 0038).
        [TargetRpc]
        private void TargetResult(NetworkConnection connection, string requestId, bool accepted, string reason,
            string movedLotId, string reservationId, string equipmentId, long revision, long cents)
        {
            ResultReceived?.Invoke(new GoodsOutcome
            {
                RequestId = requestId, Accepted = accepted, Reason = reason, MovedLotId = movedLotId,
                ReservationId = reservationId, EquipmentId = equipmentId, Revision = revision, Cents = cents
            });
        }

        [TargetRpc]
        private void TargetSite(NetworkConnection connection, string json, long epoch)
        {
            var state = JsonUtility.FromJson<GoodsSnapshot>(json);
            var siteId = GoodsWorld.ViewSiteId(state);
            if (siteId == null) return;
            if (epoch < _clientEpoch) return;
            if (epoch > _clientEpoch)
            {
                _clientRevisions.Clear();
                _clientEpoch = epoch;
            }
            if (_clientRevisions.TryGetValue(siteId, out var revision) && state.Revision < revision) return;
            _clientRevisions[siteId] = state.Revision;
            SiteReceived?.Invoke(state);
        }

        // Crowds arrive in order on the reliable channel; one from an older epoch is dropped. Site baselines own the epoch.
        [TargetRpc]
        private void TargetCrowd(NetworkConnection connection, string json, long epoch)
        {
            if (epoch < _clientEpoch) return;
            CrowdReceived?.Invoke(JsonUtility.FromJson<GoodsCrowdView>(json));
        }
    }
}
