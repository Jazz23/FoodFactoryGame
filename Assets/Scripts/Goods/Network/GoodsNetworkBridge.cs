// Transports intent and authorized site baselines; FishNet connections, not payloads, determine identity.
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
        private readonly Dictionary<NetworkConnection, string> _subscriptions = new();
        private readonly Dictionary<string, long> _clientRevisions = new();
        private const float StatsIntervalSeconds = 60f;
        private float _clockRemainder;
        private float _statsRemainder;
        private string _savePath;
        private bool _persistenceFailed;

        public event Action<GoodsOutcome> ResultReceived;
        public event Action<GoodsSnapshot> SiteReceived;

        // Called by the server's session/bootstrap owner after authenticating connection identities.
        public void InitializeServer(GoodsWorld world, Func<NetworkConnection, string> resolvePlayer, string savePath)
        {
            if (!IsServerStarted || world == null || resolvePlayer == null || string.IsNullOrWhiteSpace(savePath))
                throw new InvalidOperationException("A running server world, identity resolver and save path are required.");
            // The composition owner must first create or explicitly recover this save; never overwrite it implicitly.
            if (!File.Exists(savePath) || JsonUtility.ToJson(GoodsSnapshotStore.Load(savePath).Snapshot()) != JsonUtility.ToJson(world.Snapshot()))
                throw new InvalidOperationException("Server world must match its committed snapshot before accepting requests.");
            _world = world;
            _resolvePlayer = resolvePlayer;
            _savePath = savePath;
            // Decision 0012 measurements describe this served world, not earlier saves in the same process.
            GoodsSnapshotStore.Stats.Reset();
        }

        public override void OnStopServer()
        {
            _subscriptions.Clear();
            _world = null;
            _resolvePlayer = null;
            _savePath = null;
            _clockRemainder = 0;
            _statsRemainder = 0;
            _persistenceFailed = false;
        }

        public override void OnStopClient() => _clientRevisions.Clear();

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
            var seconds = (long)_clockRemainder;
            if (!_world.TryAdvanceDurably(seconds, _savePath))
            {
                _persistenceFailed = true;
                return;
            }
            _clockRemainder -= seconds;
            _persistenceFailed = false;
            Broadcast();
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

        public void RequestPlace(string requestId, string equipmentId, int cellX, int cellZ, int rotation)
        {
            if (IsClientStarted) ServerPlace(requestId, equipmentId, cellX, cellZ, rotation);
        }

        // One batch per request: the server checks the station, recipe, busy state and inputs (StartJobDurably).
        public void RequestStartJob(string requestId, string stationId, string recipeId)
        {
            if (IsClientStarted) ServerStartJob(requestId, stationId, recipeId);
        }

        // Places a belt from the player's inventory on an empty cell, or turns the belt already there (PlaceBeltDurably).
        public void RequestPlaceBelt(string requestId, string siteId, int cellX, int cellZ, int direction)
        {
            if (IsClientStarted) ServerPlaceBelt(requestId, siteId, cellX, cellZ, direction);
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

        // Buys one pack of a supplier offer with the site company's cash into the requester's inventory (BuyDurably).
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

        [ServerRpc(RequireOwnership = false)]
        private void ServerPlaceBelt(string requestId, string siteId, int cellX, int cellZ, int direction, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceBeltDurably(player, requestId, siteId, cellX, cellZ, direction, _savePath);
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
        private void ServerPlace(string requestId, string equipmentId, int cellX, int cellZ, int rotation, NetworkConnection sender = null)
        {
            if (!TryIdentify(sender, requestId, out var player)) return;
            var result = _world.PlaceDurably(player, requestId, equipmentId, cellX, cellZ, rotation, _savePath);
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

        // A subscriber gets a complete revisioned baseline, never unauthorized state or inferred deltas.
        private void Broadcast()
        {
            foreach (var pair in new List<KeyValuePair<NetworkConnection, string>>(_subscriptions))
                SendSite(pair.Key, pair.Value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerSubscribe(string siteId, NetworkConnection sender = null)
        {
            if (sender == null || _world == null || _resolvePlayer == null) return;
            var player = _resolvePlayer(sender);
            if (string.IsNullOrWhiteSpace(player) || !_world.CanView(player, siteId))
            {
                _subscriptions.Remove(sender);
                TargetResult(sender, "", false, "subscription-forbidden", "", "", 0);
                return;
            }
            _subscriptions[sender] = siteId;
            SendSite(sender, siteId);
        }

        private void SendSite(NetworkConnection connection, string siteId)
        {
            if (connection == null || !connection.IsActive || !_world.CanView(_resolvePlayer(connection), siteId))
            {
                _subscriptions.Remove(connection);
                return;
            }
            var view = _world.View(_resolvePlayer(connection), siteId);
            TargetSite(connection, JsonUtility.ToJson(view));
        }

        private bool TryIdentify(NetworkConnection sender, string requestId, out string player)
        {
            player = null;
            if (sender == null || _world == null || _resolvePlayer == null) return false;
            player = _resolvePlayer(sender);
            if (string.IsNullOrWhiteSpace(player) || _persistenceFailed)
            {
                TargetResult(sender, requestId, false,
                    _persistenceFailed ? "persistence-unavailable" : "unauthenticated", "", "", 0);
                return false;
            }
            return true;
        }

        private void Reply(NetworkConnection connection, GoodsOutcome result) => TargetResult(connection,
            result.RequestId ?? "", result.Accepted, result.Reason ?? "", result.MovedLotId ?? "",
            result.ReservationId ?? "", result.Revision);

        [TargetRpc]
        private void TargetResult(NetworkConnection connection, string requestId, bool accepted, string reason,
            string movedLotId, string reservationId, long revision)
        {
            ResultReceived?.Invoke(new GoodsOutcome
            {
                RequestId = requestId, Accepted = accepted, Reason = reason,
                MovedLotId = movedLotId, ReservationId = reservationId, Revision = revision
            });
        }

        [TargetRpc]
        private void TargetSite(NetworkConnection connection, string json)
        {
            var state = JsonUtility.FromJson<GoodsSnapshot>(json);
            if (state?.Locations == null || state.Locations.Count == 0) return;
            var siteId = state.Locations[0].SiteId;
            if (_clientRevisions.TryGetValue(siteId, out var revision) && state.Revision < revision) return;
            _clientRevisions[siteId] = state.Revision;
            SiteReceived?.Invoke(state);
        }
    }
}
