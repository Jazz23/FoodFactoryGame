// Resolves session mode and save/identity locations from command-line switches, with persistentDataPath defaults.
using System;
using System.IO;
using System.Linq;
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
        // Seed for a world created by this server in a scene that generates worlds (decision 0026); blank means random.
        // Ignored for a world that already exists.
        public string WorldSeed = "";

        public string WorldPath => Path.Combine(SaveDirectory, WorldFileName);
        public string RegistryPath => Path.Combine(SaveDirectory, RegistryFileName);
        public string LegacyWorldPath => Path.Combine(SaveDirectory, LegacyWorldFileName);

        public const string DefaultSaveFolder = "dev-world";
        public static string SavesRoot => Path.Combine(Application.persistentDataPath, "Saves");
        public static string DefaultSaveDirectory => Path.Combine(SavesRoot, DefaultSaveFolder);

        // True when -save named the directory; a scene's own save folder then does not replace it.
        public bool SaveDirectoryExplicit;

        // A world's save folder name: letters, digits, '-' and '_' only, so it can never leave the saves directory.
        public static bool IsValidWorldName(string name) =>
            !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && name.All(x => char.IsLetterOrDigit(x) || x == '-' || x == '_');
        public static string DefaultIdentityPath => Path.Combine(Application.persistentDataPath, "Identity", "identity.db");
        public static string DefaultLegacyIdentityPath => Path.Combine(Application.persistentDataPath, "Identity", LegacyIdentityFileName);

        // Switches: -host | -server | -connect <address>, -name <display>, -save <directory>, -identity <file>, -seed <text>.
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
                    case "-save" when value != null: options.SaveDirectory = Path.GetFullPath(value); options.SaveDirectoryExplicit = true; index++; break;
                    case "-identity" when value != null: options.IdentityPath = Path.GetFullPath(value); options.LegacyIdentityPath = null; index++; break;
                    case "-seed" when value != null: options.WorldSeed = value; index++; break;
                }
            }
            return options;
        }
    }
}
