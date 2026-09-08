using System;
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
        public Guid guid { get; set; }
        public int buildableId;
        
        [NonSerialized]
        public byte[] State;

        public override void WritePayload(NetworkConnection connection, Writer writer)
        {
            writer.Write(guid);
            writer.WriteInt32(buildableId);
        }

        public override void ReadPayload(NetworkConnection connection, Reader reader)
        {
            guid = reader.ReadGuid();
            buildableId = reader.ReadInt32();
        }
    }
}