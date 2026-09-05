// Validates predicted building spawns against the server's authoritative occupancy map.
using FishNet.Component.Ownership;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;

namespace NotAI
{
    public class NAIBuildablePredictiveSpawn : PredictedSpawn
    {
        private NAIBuildableManager _buildableManager;
        private int _buildingId;
        
        public void SetBuildingManager(NAIBuildableManager buildableManager) => _buildableManager = buildableManager;
        public void SetBuildingId(int buildingId) => _buildingId = buildingId;

        public override void WritePayload(NetworkConnection connection, Writer writer)
        {
            writer.WriteNetworkBehaviour(_buildableManager);
            writer.WriteInt32(_buildingId);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader)
        {
            _buildableManager = reader.ReadNetworkBehaviour() as NAIBuildableManager;
            _buildingId = reader.ReadInt32();
        }

        public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null)
        {
            var size = GetComponent<UnityEngine.SpriteRenderer>().bounds.size;
            if (!_buildableManager.CanBuildHere(transform.position, size)) return false;

            // Spawn the buildable on the server
            var guid = System.Guid.NewGuid();
            _buildableManager.UpdateGrid(transform.position, size, guid);
            NAIStateManager.Buildables[guid] = GetComponent<NAIBuildable>();
            return true;
        }
    }
}
