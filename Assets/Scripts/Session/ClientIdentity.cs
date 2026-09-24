// Client-local prototype credential: a random secret created once per identity file and reused on every join.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FoodFactoryGame.Session
{
    public static class ClientIdentity
    {
        // Reads the stored secret, or creates the file with a new 256-bit secret. The file is not encrypted.
        public static string LoadOrCreate(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit identity path required.");
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                var existing = File.ReadAllText(full, Encoding.UTF8).Trim();
                if (existing.Length < PlayerRegistry.MinSecretLength || existing.Length > PlayerRegistry.MaxSecretLength)
                    throw new InvalidDataException($"Identity file {full} is not a valid client secret.");
                return existing;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            var secret = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var temporary = full + ".pending";
            File.WriteAllText(temporary, secret, new UTF8Encoding(false));
            // A concurrent first run may have created it; keep whichever landed first so both processes agree.
            try { File.Move(temporary, full); }
            catch (IOException) when (File.Exists(full)) { File.Delete(temporary); return LoadOrCreate(full); }
            return secret;
        }
    }
}
