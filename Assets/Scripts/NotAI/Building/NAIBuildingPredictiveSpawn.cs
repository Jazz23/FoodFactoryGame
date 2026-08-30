using FishNet.Component.Ownership;
using FishNet.Connection;
using FishNet.Serializing;

namespace NotAI
{
    public class NAIBuildingPredictiveSpawn : PredictedSpawn
    {
        private NAIBuildingManager _buildingManager;
        public void SetBuildingManager(NAIBuildingManager buildingManager) => _buildingManager = buildingManager;

        public override void WritePayload(NetworkConnection connection, Writer writer) =>
            writer.WriteNetworkBehaviour(_buildingManager);
        
        public override void ReadPayload(NetworkConnection connection, Reader reader) =>
            _buildingManager = reader.ReadNetworkBehaviour() as NAIBuildingManager;

        public override bool OnTrySpawnServer(NetworkConnection spawner, NetworkConnection owner = null)
        {
            if (!_buildingManager.CanBuildHere()) return false;

            _buildingManager.Build();
            return true;
        }
    }
}