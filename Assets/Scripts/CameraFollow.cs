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

        var player = PlayerSceneTransition.LocalOwner;
        var position = player.transform.position + offset;
        transform.position = position;

        if (SceneGrid.TryGetForScene(player.gameObject.scene, out var grid))
        {
            sceneCamera.orthographicSize = grid.OrthographicSize;
        }
    }
}
