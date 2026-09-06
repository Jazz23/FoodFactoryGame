using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NotAI.UI
{
    // Attached to each game object with a clickable UI
    public class NAIOpenableUI : MonoBehaviour
    {
        public GameObject UIPrefab;
        
        public GameObject OpenUI(Transform parent)
        {
            var ui = Instantiate(UIPrefab, parent);
            return ui;
        }
    }
}