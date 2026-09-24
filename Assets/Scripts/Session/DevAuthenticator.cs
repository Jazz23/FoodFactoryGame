// PROTOTYPE FishNet authenticator: clients prove a locally stored secret; the server alone maps connections to player IDs.
// No transport encryption or accounts. Swap for Unity Authentication/Steam without changing the connection→player contract.
using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public struct JoinRequestBroadcast : IBroadcast
    {
        public string DisplayName;
        public string Secret;
    }

    public struct JoinResponseBroadcast : IBroadcast
    {
        public bool Accepted;
        public string Reason;
        public string PlayerId;
    }

    public sealed class DevAuthenticator : Authenticator
    {
        // PROTOTYPE cap; the real player count is an open GDD decision.
        public const int DefaultMaxPlayers = 8;
        private const float RejectionGraceSeconds = 2f;

        private readonly Dictionary<NetworkConnection, string> _players = new();
        private readonly Dictionary<string, NetworkConnection> _connections = new();
        private readonly HashSet<NetworkConnection> _rejecting = new();
        private SessionAdmission _admission;
        private int _maxPlayers = DefaultMaxPlayers;
        private string _clientName;
        private string _clientSecret;

        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        // Client side: raised when the server answers this client's join request.
        public event Action<JoinResponseBroadcast> ClientJoinAnswered;

        public string LocalPlayerId { get; private set; }
        public string LastRejection { get; private set; }
        public int AuthenticatedCount => _players.Count;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            NetworkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            NetworkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            NetworkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            NetworkManager.ServerManager.RegisterBroadcast<JoinRequestBroadcast>(OnJoinRequest, false);
            NetworkManager.ClientManager.RegisterBroadcast<JoinResponseBroadcast>(OnJoinResponse);
        }

        private void OnDestroy()
        {
            if (NetworkManager == null) return;
            NetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            NetworkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            NetworkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            NetworkManager.ServerManager.UnregisterBroadcast<JoinRequestBroadcast>(OnJoinRequest);
            NetworkManager.ClientManager.UnregisterBroadcast<JoinResponseBroadcast>(OnJoinResponse);
        }

        public void ConfigureServer(SessionAdmission admission, int maxPlayers = DefaultMaxPlayers)
        {
            if (admission == null || maxPlayers < 1) throw new ArgumentException("Admission and a positive player cap are required.");
            _admission = admission;
            _maxPlayers = maxPlayers;
        }

        public void SetClientCredentials(string displayName, string secret)
        {
            _clientName = displayName;
            _clientSecret = secret;
        }

        // The only connection→player source for gameplay; null for unauthenticated or departed connections.
        public string PlayerIdOf(NetworkConnection connection)
        {
            return connection != null && _players.TryGetValue(connection, out var player) ? player : null;
        }

        public bool TryGetConnection(string playerId, out NetworkConnection connection)
        {
            connection = null;
            return playerId != null && _connections.TryGetValue(playerId, out connection);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            // The ID ends with the connection; the rejection reason survives it so the menu can show why.
            if (args.ConnectionState == LocalConnectionState.Stopped) LocalPlayerId = null;
            if (args.ConnectionState == LocalConnectionState.Starting) LastRejection = null;
            if (args.ConnectionState != LocalConnectionState.Started) return;
            NetworkManager.ClientManager.Broadcast(new JoinRequestBroadcast { DisplayName = _clientName, Secret = _clientSecret });
        }

        private void OnJoinResponse(JoinResponseBroadcast response, Channel channel)
        {
            if (response.Accepted) LocalPlayerId = response.PlayerId;
            else LastRejection = response.Reason;
            ClientJoinAnswered?.Invoke(response);
            // The client leaves on its own once it has read the reason; a server-forced close can race the reply.
            if (!response.Accepted) NetworkManager.ClientManager.StopConnection();
        }

        private void OnJoinRequest(NetworkConnection connection, JoinRequestBroadcast request, Channel channel)
        {
            if (_rejecting.Contains(connection)) return;
            // A second request on one connection is never a legitimate client; drop it.
            if (connection.IsAuthenticated || _players.ContainsKey(connection))
            {
                connection.Disconnect(true);
                return;
            }
            if (_admission == null)
            {
                Reject(connection, "server-not-ready");
                return;
            }
            if (_players.Count >= _maxPlayers)
            {
                Reject(connection, "server-full");
                return;
            }
            var result = _admission.Admit(request.DisplayName, request.Secret, _connections.ContainsKey);
            if (!result.Accepted)
            {
                Reject(connection, result.Reason);
                return;
            }
            _players[connection] = result.PlayerId;
            _connections[result.PlayerId] = connection;
            Send(connection, true, result.Reason, result.PlayerId);
            OnAuthenticationResult?.Invoke(connection, true);
        }

        // The rejected client disconnects itself after reading the reason; the server only kicks it if it lingers.
        private void Reject(NetworkConnection connection, string reason)
        {
            Debug.Log($"[Session] Rejected connection {connection.ClientId}: {reason}");
            _rejecting.Add(connection);
            Send(connection, false, reason, null);
            StartCoroutine(KickIfLingering(connection));
        }

        private IEnumerator KickIfLingering(NetworkConnection connection)
        {
            yield return new WaitForSecondsRealtime(RejectionGraceSeconds);
            if (!_rejecting.Remove(connection) || !connection.IsActive) yield break;
            OnAuthenticationResult?.Invoke(connection, false);
        }

        private void Send(NetworkConnection connection, bool accepted, string reason, string playerId)
        {
            NetworkManager.ServerManager.Broadcast(connection,
                new JoinResponseBroadcast { Accepted = accepted, Reason = reason ?? "", PlayerId = playerId ?? "" }, false);
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            _rejecting.Remove(connection);
            if (!_players.TryGetValue(connection, out var player)) return;
            _players.Remove(connection);
            _connections.Remove(player);
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            _players.Clear();
            _connections.Clear();
            _rejecting.Clear();
        }
    }
}
