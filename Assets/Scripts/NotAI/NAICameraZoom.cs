// Adjusts the local camera zoom without responding to inventory UI scroll input.

using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

public class NAICameraZoom : NetworkBehaviour
{
    [SerializeField, Min(0.01f)] private float zoomSpeed = 100f;
    [SerializeField, Min(0.01f)] private float minOrthographicSize = 2f;
    [SerializeField, Min(0.01f)] private float maxOrthographicSize = 20f;

    private Camera _camera;
    private InputAction _scrollWheel;

    private void Awake() => enabled = false;
    
    public override void OnStartClient()
    {
        if (!IsOwner) return;

        enabled = true;
        _camera = Camera.main!;
        (_scrollWheel = InputSystem.actions["ScrollWheel"]).Enable();
    }

    public override void OnStopClient()
    {
        if (!IsOwner) return;
        
        _scrollWheel.Disable();
    }

    private void Update()
    {
        var scroll = _scrollWheel.ReadValue<Vector2>().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        var nextSize = _camera.orthographicSize - scroll * zoomSpeed * 0.01f;
        _camera.orthographicSize = Mathf.Clamp(
            nextSize,
            minOrthographicSize,
            Mathf.Max(minOrthographicSize, maxOrthographicSize));
    }
}
