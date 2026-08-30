using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace NotAI
{
    public class NAIStateManager : NetworkBehaviour
    {
        // Maps the world pos of a cell to the building id that occupies it. If a 4x4 building is placed, then all 4 cells with have the same building id.
        public static Dictionary<Vector2, int> Buildables => _instance._buildables.Collection;
        private readonly SyncDictionary<Vector2, int> _buildables = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));

        private static NAIStateManager _instance;

        public override void OnStartNetwork() => _instance = this;
    }
}