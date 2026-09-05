using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace NotAI
{
    public class NAIStateManager : NetworkBehaviour
    {
        // Maps the world pos of a cell to the uuid of the building that occupies it. If a 4x4 building is placed, then all 4 cells with have the same uuid.
        public static SyncDictionary<Vector2, Guid> OccupiedTiles => _instance._occupiedTiles;
        private readonly SyncDictionary<Vector2, Guid> _occupiedTiles = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));

        public static SyncDictionary<Guid, NAIBuildable> Buildables => _instance._buildables;
        private readonly SyncDictionary<Guid, NAIBuildable> _buildables = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));

        private static NAIStateManager _instance;

        public override void OnStartNetwork() => _instance = this;
    }
}