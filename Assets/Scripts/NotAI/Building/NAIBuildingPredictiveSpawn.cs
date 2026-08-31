// Validates predicted building spawns against the server's authoritative occupancy map.
using FishNet.Component.Ownership;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;

namespace NotAI
{
    public class NAIBuildingPredictiveSpawn : PredictedSpawn
    {
        private NAIBuildingManager _buildingManager;
        private int _buildingId;
        
        public void SetBuildingManager(NAIBuildingManager buildingManager) => _buildingManager = buildingManager;
        public void SetBuildingId(int buildingId) => _buildingId = buildingId;

        public override void WritePayload(NetworkConnection connection, Writer writer)
        {
            writer.WriteNetworkBehaviour(_buildingManager);
            writer.WriteInt32(_buildingId);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader)
        {
            _buildingManager = reader.ReadNetworkBehaviour() as NAIBuildingManager;
            _buildingId = reader.ReadInt32();
        }

        public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null)
        {
            var size = GetComponent<UnityEngine.SpriteRenderer>().bounds.size;
            if (!_buildingManager.CanBuildHere(transform.position, size)) return false;

            _buildingManager.UpdateGrid(transform.position, size, _buildingId);
            return true;
        }
    }
}
