using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using UnityEngine;

namespace DefaultNamespace
{
    public static class NAIExtensions
    {
        public static BoundsInt GetBoundsFromCenter(Vector3Int center, int size)
        {
            var halfSize = size / 2;
            var min = new Vector3Int(center.x - halfSize, center.y - halfSize, 0);
            var max = new Vector3Int(center.x + halfSize, center.y + halfSize, 0);
            return new BoundsInt(min, max - min);
        }

        /// <summary>
        /// Returns a component of type T from the player object associated with the given NetworkConnection. Caches the component for future retrievals.
        /// </summary>
        public static T GetPlayerComponent<T>(this NetworkConnection conn) where T : Component
        {
            // Make sure the custom data dict is instantiated and contains a dictionary for player components
            conn.CustomData ??= new Dictionary<string, object>();
            ((Dictionary<string, object>)conn.CustomData).TryAdd("PlayerComponents", new Dictionary<string, Component>());
            
            // Retrieve the cached component dictionary
            var componentDict = ((Dictionary<string, object>)conn.CustomData)["PlayerComponents"] as Dictionary<string, Component>;
            var key = typeof(T).FullName!;

            // Check if the component is already cached; if so, return it
            if (componentDict!.TryGetValue(key, out var value)) return (T)value;
            
            // If not cached, find the component on the player's objects, cache it, and return it
            var component = conn.Objects.First(no => no.TryGetComponent<T>(out _)).GetComponent<T>();
            return (T)(componentDict[key] = component);
        }
    }
}