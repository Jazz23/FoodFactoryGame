using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NotAI
{
    public class NAICameraSortMode : MonoBehaviour
    {
        private void Awake()
        {
            Camera.main!.GetComponent<UniversalAdditionalCameraData>().SetRenderer(1);
        }
    }
}