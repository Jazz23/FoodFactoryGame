// Simulates one stacked OutsideTest interior's isolated 2D physics scene per fixed step.
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class OutsideTestLocalPhysicsDriver : MonoBehaviour
{
    private void FixedUpdate()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        var physicsScene = scene.GetPhysicsScene2D();
        if (physicsScene.IsValid()
            && !physicsScene.Equals(Physics2D.defaultPhysicsScene))
        {
            physicsScene.Simulate(Time.fixedDeltaTime);
        }
    }
}
