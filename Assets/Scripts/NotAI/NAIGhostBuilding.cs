using DefaultNamespace;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace NotAI
{
    public class NAIGhostBuilding : NetworkBehaviour
    {
        public override void OnStartNetwork() => gameObject.SetActive(false);

        public override void OnStartClient()
        {
            if (!IsOwner) return;
            Owner.GetPlayerComponent<NAIBuildingManager>().SetGhost(gameObject);
        }
    }
}