// Shared Session test inputs: the real item content the dev seed counts in slots, and pre-SQLite world files for import tests.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FoodFactoryGame.Session.Equipment;
using UnityEditor;

namespace FoodFactoryGame.Session.Tests
{
    internal static class SessionTestFiles
    {
        // The dev seed counts its goods in slots, so it needs the real item content (max stacks) just as the server has.
        public static ItemDefinition[] ContentItems() => AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Content/Items" })
            .Select(x => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(x))).ToArray();

        // Writes a pre-SQLite world.snapshot envelope around the payload; returns its checksum.
        public static string WriteLegacyWorld(string path, string payload)
        {
            string digest;
            using (var sha = SHA256.Create()) digest = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            File.WriteAllText(path, "{\"Payload\":" + JsonString(payload) + ",\"Sha256\":\"" + digest + "\"}");
            return digest;
        }

        private static string JsonString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
