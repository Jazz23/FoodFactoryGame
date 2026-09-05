using System;
using FishNet.Object;
using UnityEngine;

namespace NotAI.UI
{
    // Attached to the UI prefab for a certain type of inventory
    public abstract class NAIUI : MonoBehaviour
    {
        [field: SerializeField]
        public GameObject AttachedGameObject { get; set; }
        public event Action OnClose;
        
        private void OnDestroy() => OnClose?.Invoke();
    }
}