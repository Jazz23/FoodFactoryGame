using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class CameraFollow : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new(0f, 0.4f, -10f);

    private Camera sceneCamera = null!;

    private void Awake()
    {
        sceneCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (PlayerSceneTransition.LocalOwner == null)
        {
            return;
        }

        var position = PlayerSceneTransition.LocalOwner.position + offset;
        transform.position = position;
        sceneCamera.orthographicSize = SceneGrid.GetForScene(
            PlayerSceneTransition.LocalOwner.gameObject.scene).OrthographicSize;
    }
}
