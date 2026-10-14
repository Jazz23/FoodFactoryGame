// Client-local preferences kept in one SQLite key/value table (decision 0011): display name, mouse sensitivity, volume, key binding
// overrides and the last session the menu started, which Resume repeats (decision 0041). Loading never creates the database,
// so reading settings (and tests that pass no path) leave application data untouched; only Save writes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SQLite;

namespace FoodFactoryGame.Session
{
    public enum LastSessionKind
    {
        None,
        // A world this client hosted (singleplayer or for others).
        World,
        // A server this client joined by address.
        Server
    }

    public sealed class ClientSettings
    {
        public const int SchemaVersion = 1;
        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 3f;
        public const float DefaultVolume = 0.8f;

        // Null until the player sets one; the menu then falls back to the session's default name.
        public string DisplayName;
        public float LookSensitivity = 1f;
        // Master volume, 0 (muted) to 1; applied to the AudioListener.
        public float Volume = DefaultVolume;
        // Each group's share of the master volume, 0 to 1 (GameAudio channels).
        public float EffectsVolume = 1f;
        public float InterfaceVolume = 1f;
        public float AmbienceVolume = 1f;
        // InputActionAsset.SaveBindingOverridesAsJson output; empty means the authored bindings.
        public string BindingOverrides = "";
        public LastSessionKind LastKind;
        public string LastWorld = "";
        public HostVisibility LastVisibility = HostVisibility.Singleplayer;
        public string LastAddress = "";
        public DateTime LastPlayedUtc;

        // Defaults when the path is null or the database does not exist yet; a newer schema is refused, never overwritten.
        public static ClientSettings Load(string path)
        {
            var settings = new ClientSettings();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return settings;
            using var db = new SQLiteConnection(Path.GetFullPath(path), SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            db.BusyTimeout = TimeSpan.FromSeconds(5);
            var version = db.ExecuteScalar<int>("PRAGMA user_version");
            if (version > SchemaVersion) throw new NotSupportedException("Newer client settings schema.");
            if (version == 0) return settings;
            var values = new Dictionary<string, string>();
            foreach (var row in db.Query<Row>("SELECT key AS Key, value AS Value FROM settings")) values[row.Key] = row.Value;
            settings.DisplayName = values.TryGetValue("display_name", out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
            if (values.TryGetValue("look_sensitivity", out var sensitivity)
                && float.TryParse(sensitivity, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                settings.LookSensitivity = ClampSensitivity(parsed);
            settings.Volume = ReadVolume(values, "volume", DefaultVolume);
            settings.EffectsVolume = ReadVolume(values, "effects_volume", 1f);
            settings.InterfaceVolume = ReadVolume(values, "interface_volume", 1f);
            settings.AmbienceVolume = ReadVolume(values, "ambience_volume", 1f);
            settings.BindingOverrides = values.TryGetValue("binding_overrides", out var bindings) ? bindings ?? "" : "";
            if (values.TryGetValue("last_kind", out var kind) && Enum.TryParse(kind, out LastSessionKind lastKind)) settings.LastKind = lastKind;
            settings.LastWorld = values.TryGetValue("last_world", out var world) ? world ?? "" : "";
            if (values.TryGetValue("last_visibility", out var visibility) && Enum.TryParse(visibility, out HostVisibility lastVisibility))
                settings.LastVisibility = lastVisibility;
            settings.LastAddress = values.TryGetValue("last_address", out var address) ? address ?? "" : "";
            if (values.TryGetValue("last_played_utc", out var played)
                && DateTime.TryParse(played, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var playedUtc))
                settings.LastPlayedUtc = playedUtc;
            return settings;
        }

        // Writes every value in one transaction, creating the database on first use.
        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit settings path required.");
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using var db = new SQLiteConnection(full, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            db.BusyTimeout = TimeSpan.FromSeconds(5);
            var version = db.ExecuteScalar<int>("PRAGMA user_version");
            if (version > SchemaVersion) throw new NotSupportedException("Newer client settings schema.");
            db.RunInTransaction(() =>
            {
                if (version == 0)
                {
                    db.Execute("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                    db.Execute($"PRAGMA user_version = {SchemaVersion}");
                }
                Put(db, "display_name", DisplayName ?? "");
                Put(db, "look_sensitivity", ClampSensitivity(LookSensitivity).ToString("R", CultureInfo.InvariantCulture));
                Put(db, "volume", ClampVolume(Volume).ToString("R", CultureInfo.InvariantCulture));
                Put(db, "effects_volume", ClampVolume(EffectsVolume).ToString("R", CultureInfo.InvariantCulture));
                Put(db, "interface_volume", ClampVolume(InterfaceVolume).ToString("R", CultureInfo.InvariantCulture));
                Put(db, "ambience_volume", ClampVolume(AmbienceVolume).ToString("R", CultureInfo.InvariantCulture));
                Put(db, "binding_overrides", BindingOverrides ?? "");
                Put(db, "last_kind", LastKind.ToString());
                Put(db, "last_world", LastWorld ?? "");
                Put(db, "last_visibility", LastVisibility.ToString());
                Put(db, "last_address", LastAddress ?? "");
                Put(db, "last_played_utc", LastPlayedUtc.ToString("O", CultureInfo.InvariantCulture));
            });
        }

        public static float ClampSensitivity(float value) =>
            float.IsNaN(value) ? 1f : Math.Min(MaxSensitivity, Math.Max(MinSensitivity, value));

        public static float ClampVolume(float value) =>
            float.IsNaN(value) ? DefaultVolume : Math.Min(1f, Math.Max(0f, value));

        private static float ReadVolume(Dictionary<string, string> values, string key, float fallback) =>
            values.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? ClampVolume(parsed) : fallback;

        private static void Put(SQLiteConnection db, string key, string value) =>
            db.Execute("INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value", key, value);

        private sealed class Row
        {
            public string Key { get; set; }
            public string Value { get; set; }
        }
    }
}
