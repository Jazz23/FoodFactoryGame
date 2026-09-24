// Resolves session mode and save/identity locations from command-line switches, with persistentDataPath defaults.
using System;
using System.IO;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public enum SessionMode
    {
        None,
        Host,
        Client,
        Server
    }

    public sealed class SessionOptions
    {
        public const string DefaultAddress = "127.0.0.1";
        public const string WorldFileName = "world.db";
        // Pre-SQLite file names, read once for import and never written.
        public const string LegacyWorldFileName = "world.snapshot";
        public const string LegacyIdentityFileName = "client.secret";
        public const string RegistryFileName = "players.db";

        public SessionMode Mode = SessionMode.None;
        public string Address = DefaultAddress;
        public string DisplayName;
        public string SaveDirectory;
        public string IdentityPath;
        // Set only for the default identity location; an explicit -identity path is never paired with a legacy file.
        public string LegacyIdentityPath;

        public string WorldPath => Path.Combine(SaveDirectory, WorldFileName);
        public string RegistryPath => Path.Combine(SaveDirectory, RegistryFileName);
        public string LegacyWorldPath => Path.Combine(SaveDirectory, LegacyWorldFileName);

        public static string DefaultSaveDirectory => Path.Combine(Application.persistentDataPath, "Saves", "dev-world");
        public static string DefaultIdentityPath => Path.Combine(Application.persistentDataPath, "Identity", "identity.db");
        public static string DefaultLegacyIdentityPath => Path.Combine(Application.persistentDataPath, "Identity", LegacyIdentityFileName);

        // Switches: -host | -server | -connect <address>, -name <display>, -save <directory>, -identity <file>.
        public static SessionOptions FromCommandLine(string[] args)
        {
            var options = new SessionOptions
            {
                SaveDirectory = DefaultSaveDirectory,
                IdentityPath = DefaultIdentityPath,
                LegacyIdentityPath = DefaultLegacyIdentityPath,
                DisplayName = Environment.UserName
            };
            for (var index = 0; index < args.Length; index++)
            {
                var value = index + 1 < args.Length ? args[index + 1] : null;
                switch (args[index].ToLowerInvariant())
                {
                    case "-host": options.Mode = SessionMode.Host; break;
                    case "-server": options.Mode = SessionMode.Server; break;
                    case "-connect" when value != null: options.Mode = SessionMode.Client; options.Address = value; index++; break;
                    case "-name" when value != null: options.DisplayName = value; index++; break;
                    case "-save" when value != null: options.SaveDirectory = Path.GetFullPath(value); index++; break;
                    case "-identity" when value != null: options.IdentityPath = Path.GetFullPath(value); options.LegacyIdentityPath = null; index++; break;
                }
            }
            return options;
        }
    }
}
