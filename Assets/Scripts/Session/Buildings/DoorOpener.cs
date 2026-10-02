// Presentation only: marks a figure that opens restaurant doors (DoorSwing) as it comes near: player avatars, walking customers
// and employees. Opens is false while the figure should not hold a door open (a customer standing still or seated at a table).
using System.Collections.Generic;
using UnityEngine;

namespace FoodFactoryGame.Session.Buildings
{
    [DisallowMultipleComponent]
    public sealed class DoorOpener : MonoBehaviour
    {
        private static readonly List<DoorOpener> Active = new();

        public bool Opens { get; set; } = true;

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        // The nearest opener within reach of a point, measured on the floor plane (and within one storey's height), or null.
        public static DoorOpener Nearest(Vector3 point, float reach)
        {
            DoorOpener nearest = null;
            var best = reach * reach;
            foreach (var opener in Active)
            {
                if (!opener.Opens) continue;
                var position = opener.transform.position;
                if (Mathf.Abs(position.y - point.y) > 2.5f) continue;
                var distance = new Vector2(position.x - point.x, position.z - point.z).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                nearest = opener;
            }
            return nearest;
        }
    }
}
