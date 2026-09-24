// Oven presentation: heater glow, chamber light and fans. On DevSite the EquipmentPresenter drives it from the replicated
// job state; Toggle remains only for the SampleScene click prototype.
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Interaction
{

[DisallowMultipleComponent]
public sealed class OvenToggle : MonoBehaviour, IEquipmentRunningDisplay
{
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private Renderer[] heatedRenderers;
    [SerializeField] private Light chamberLight;
    [SerializeField] private Transform[] convectionRotors;
    [SerializeField] private Transform[] sideRotors;
    [SerializeField] private Color heaterEmission = new(1f, 0.16f, 0.01f, 1f);
    [SerializeField] private float minimumEmissionIntensity = 3f;
    [SerializeField] private float maximumEmissionIntensity = 5f;
    [SerializeField] private float maximumLightIntensity = 3.5f;
    [SerializeField] private float convectionRotorDegreesPerSecond = 360f;
    [SerializeField] private float sideRotorDegreesPerSecond = 180f;

    private MaterialPropertyBlock propertyBlock;
    private float elapsedPoweredTime;
    private bool isPowered;

    public bool IsPowered => isPowered;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ApplyVisuals(0f);
    }

    private void Update()
    {
        if (!isPowered)
        {
            return;
        }

        elapsedPoweredTime += Time.deltaTime;
        var pulse = Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(elapsedPoweredTime * Mathf.PI));
        ApplyVisuals(pulse);
        Rotate(convectionRotors, Vector3.up, convectionRotorDegreesPerSecond);
        Rotate(sideRotors, Vector3.right, -sideRotorDegreesPerSecond);
    }

    public void Toggle()
    {
        SetPowered(!isPowered);
    }

    public void SetRunning(bool running)
    {
        if (running != isPowered)
        {
            SetPowered(running);
        }
    }

    public void SetPowered(bool powered)
    {
        isPowered = powered;
        elapsedPoweredTime = 0f;
        ApplyVisuals(powered ? 0.5f : 0f);
    }

    private void ApplyVisuals(float pulse)
    {
        var emissionIntensity = Mathf.Lerp(minimumEmissionIntensity, maximumEmissionIntensity, pulse);
        var emission = heaterEmission * (isPowered ? emissionIntensity : 0f);
        foreach (var heatedRenderer in heatedRenderers)
        {
            heatedRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(EmissionColor, emission);
            heatedRenderer.SetPropertyBlock(propertyBlock);
        }

        chamberLight.intensity = isPowered ? maximumLightIntensity * pulse : 0f;
    }

    private static void Rotate(Transform[] rotors, Vector3 localAxis, float degreesPerSecond)
    {
        foreach (var rotor in rotors)
        {
            rotor.Rotate(localAxis, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
}
