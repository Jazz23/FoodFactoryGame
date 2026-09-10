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
        [SerializeField, HideInInspector] private string guidString = string.Empty;
        [field: NonSerialized]
        public Guid guid
        {
            get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
                ? value
                : Guid.Empty;
            set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
        }
        public int buildableId;
        
        [NonSerialized]
        public byte[] State;

        public override void OnStartNetwork()
        {
            if (NAIStateManager.Instance is not null)
            {
                NAIStateManager.Instance.RegisterBuildableView(this);
            }
        }

        public override void OnStopNetwork()
        {
            if (NAIStateManager.Instance is not null)
            {
                NAIStateManager.Instance.UnregisterBuildableView(guid, this);
            }
        }

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
