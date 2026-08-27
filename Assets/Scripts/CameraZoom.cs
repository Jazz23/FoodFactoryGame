// Adjusts the local camera zoom without responding to inventory UI scroll input.
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public sealed class CameraZoom : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float zoomSpeed = 100f;
    [SerializeField, Min(0.01f)] private float minOrthographicSize = 2f;
    [SerializeField, Min(0.01f)] private float maxOrthographicSize = 20f;

    private Camera _sceneCamera;
    private InputAction _scrollWheel;

    private void Awake()
    {
        _sceneCamera = GetComponent<Camera>();
        _scrollWheel = InputSystem.actions["ScrollWheel"];
    }

    private void OnEnable()
    {
        _scrollWheel.Enable();
    }

    private void OnDisable()
    {
        _scrollWheel.Disable();
    }

    private void Update()
    {
        if (PlayerInventory.LocalOwner is not null && PlayerInventory.LocalOwner.IsOpen)
        {
            return;
        }

        var scroll = _scrollWheel.ReadValue<Vector2>().y;
        if (Mathf.Approximately(scroll, 0f))
        {
            return;
        }

        var nextSize = _sceneCamera.orthographicSize - scroll * zoomSpeed * 0.01f;
        _sceneCamera.orthographicSize = Mathf.Clamp(
            nextSize,
            minOrthographicSize,
            Mathf.Max(minOrthographicSize, maxOrthographicSize));
    }
}
