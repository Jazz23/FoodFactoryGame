using System;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Jobs;

public class Movement : NetworkBehaviour
{
    private InputAction _move;

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        enabled = true;
        (_move = InputSystem.actions["Move"]).Enable();
    }

    private void Update()
    {
        transform.position += Time.deltaTime * (Vector3)_move.ReadValue<Vector2>();
    }
}
