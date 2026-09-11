// Holds runtime references to the project's existing conveyor animation and sprite.
using UnityEngine;

public sealed class FactoryConveyorArt : ScriptableObject
{
    public Sprite sprite = null!;
    public RuntimeAnimatorController controller = null!;
}
