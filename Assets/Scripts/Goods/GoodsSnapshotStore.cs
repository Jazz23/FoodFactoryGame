// Writes a verified versioned goods snapshot atomically and recovers the last valid committed copy.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] internal sealed class GoodsEnvelope
    {
        public string Payload;
        public string Sha256;
    }

    public static class GoodsSnapshotStore
    {
        private static readonly object SaveGate = new();

        public static void Save(GoodsWorld world, string path)
        {
            if (world == null || string.IsNullOrWhiteSpace(path)) throw new ArgumentException("World and explicit save path required.");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("Create an isolated save directory first.");
            var state = world.Snapshot();
            GoodsWorld.Validate(state);
            var payload = JsonUtility.ToJson(state);
            var envelope = JsonUtility.ToJson(new GoodsEnvelope { Payload = payload, Sha256 = Digest(payload) });
            var temporary = full + ".pending";
            lock (SaveGate)
            {
                if (File.Exists(full))
                {
                    GoodsSnapshot prior;
                    try { prior = ReadValid(full); }
                    catch (Exception error) when (error is IOException || error is InvalidOperationException || error is ArgumentException)
                    {
                        // Recover the last valid committed file before rotation so .previous never becomes corrupt.
                        ReadValid(full + ".previous");
                        File.Replace(full + ".previous", full, full + ".corrupt");
                        prior = ReadValid(full);
                    }
                    if (prior.WorldId != state.WorldId || prior.Revision > state.Revision
                        || (prior.Revision == state.Revision && JsonUtility.ToJson(prior) != payload))
                        throw new IOException("Refusing a stale or conflicting world snapshot.");
                }
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(envelope);
                        writer.Flush();
                        stream.Flush(true);
                    }
                    ReadValid(temporary);
                    if (File.Exists(full)) File.Replace(temporary, full, full + ".previous");
                    else File.Move(temporary, full);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        public static GoodsWorld Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit save path required.");
            var full = Path.GetFullPath(path);
            try { return GoodsWorld.Restore(ReadValid(full)); }
            catch (Exception error) when (error is IOException || error is InvalidOperationException || error is ArgumentException)
            {
                // The previous committed snapshot remains available after a torn/corrupt latest write.
                return GoodsWorld.Restore(ReadValid(full + ".previous"));
            }
        }

        private static GoodsSnapshot ReadValid(string path)
        {
            var envelope = JsonUtility.FromJson<GoodsEnvelope>(File.ReadAllText(path, Encoding.UTF8));
            if (envelope == null || string.IsNullOrEmpty(envelope.Payload) || envelope.Sha256 != Digest(envelope.Payload))
                throw new InvalidOperationException("Goods snapshot checksum mismatch.");
            var state = JsonUtility.FromJson<GoodsSnapshot>(envelope.Payload);
            // An unknown new schema is never interpreted as an older backup.
            if (state != null && state.SchemaVersion > GoodsSnapshot.CurrentSchema) throw new NotSupportedException("Newer goods snapshot schema.");
            // v1 had no stations or jobs; it is upgraded in memory and written as v2 by the next commit.
            if (state != null && state.SchemaVersion == 1)
            {
                state.Stations = new();
                state.Jobs = new();
                state.SchemaVersion = 2;
            }
            GoodsWorld.Validate(state);
            return state;
        }

        private static string Digest(string payload)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
