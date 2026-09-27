// Replicates the world's stored layout (decision 0026) to each client that asks once it starts. The layout is public map data
// that never changes, so every authenticated connection gets the same canonical text, gzip-compressed, with its SHA-256;
// a client keeps it only if the hash matches. It carries no simulation state and accepts no commands.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FishNet.Connection;
using FishNet.Object;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Goods.Network
{
    public sealed class WorldLayoutBridge : NetworkBehaviour
    {
        private byte[] _serverPayload = Array.Empty<byte>();
        private string _serverSha = "";

        // Client side: true once the server answered, even when the world has no layout.
        public bool Answered { get; private set; }
        public WorldLayout Layout { get; private set; }
        public string LayoutSha256 { get; private set; } = "";
        public event Action<WorldLayout> LayoutReceived;

        // Server side, before spawning: null serves "no layout" (a save made before world generation).
        public void InitializeServer(StoredWorldLayout stored)
        {
            _serverPayload = stored == null ? Array.Empty<byte>() : Compress(stored.Text);
            _serverSha = stored?.Sha256 ?? "";
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ServerRequestLayout();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Answered = false;
            Layout = null;
            LayoutSha256 = "";
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestLayout(NetworkConnection sender = null)
        {
            if (sender == null || !sender.IsAuthenticated) return;
            TargetLayout(sender, _serverPayload, _serverSha);
        }

        [TargetRpc]
        private void TargetLayout(NetworkConnection connection, byte[] payload, string sha)
        {
            Answered = true;
            if (payload == null || payload.Length == 0)
            {
                Layout = null;
                LayoutSha256 = "";
                LayoutReceived?.Invoke(null);
                return;
            }
            var text = Decompress(payload);
            if (WorldLayoutText.Hash(text) != sha)
            {
                Debug.LogError("[World] Received a world layout whose checksum does not match; ignoring it.");
                return;
            }
            Layout = WorldLayoutText.Read(text);
            LayoutSha256 = sha;
            LayoutReceived?.Invoke(Layout);
        }

        private static byte[] Compress(string text)
        {
            using var output = new MemoryStream();
            using (var zip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                zip.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }

        private static string Decompress(byte[] payload)
        {
            using var input = new GZipStream(new MemoryStream(payload), CompressionMode.Decompress);
            using var reader = new StreamReader(input, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
