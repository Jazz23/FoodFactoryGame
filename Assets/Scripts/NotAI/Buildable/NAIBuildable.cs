using DefaultNamespace;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;

namespace NotAI
{
    public class NAIBuildable : NetworkBehaviour
    {
        [field: SerializeField, ReadOnly]
        public int uuid { get; set; }
        public int buildableId;

        public override void WritePayload(NetworkConnection connection, Writer writer)
        {
            writer.WriteInt32(uuid);
            writer.WriteInt32(buildableId);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader)
        {
            uuid = reader.ReadInt32();
            buildableId = reader.ReadInt32();
        }
    }
}