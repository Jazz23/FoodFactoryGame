// Evaluates every player and explicit building surface once per scene in LateUpdate.
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class DepthOcclusionCoordinator : MonoBehaviour
{
    private const float SortingDistanceEpsilon = 0.0001f;

    private readonly struct SortingStateKey
    {
        public SortingStateKey(int surfaceId, int playerId)
        {
            SurfaceId = surfaceId;
            PlayerId = playerId;
        }

        public int SurfaceId { get; }
        public int PlayerId { get; }
    }

    [SerializeField, Range(0.05f, 1f)] private float occludedAlpha = 0.2f;
    [SerializeField, Min(0f)] private float sortingHysteresis = 0.05f;
    [SerializeField, Min(0f)] private float surfaceRefreshInterval = 0.1f;

    private readonly List<DepthOcclusionSurface> surfaces = new();
    private readonly List<DepthOcclusionPresentation> presentations = new();
    private readonly List<Virtual3DSize> players = new();
    private readonly List<Vector2> playerPolygon = new();
    private readonly List<Vector2> surfacePolygon = new();
    private readonly HashSet<DepthOcclusionSurface> knownSurfaces = new();
    private readonly HashSet<DepthOcclusionPresentation> knownPresentations = new();
    private readonly HashSet<DepthOcclusionPresentation> occludedPresentations = new();
    private readonly HashSet<DepthOcclusionPresentation> insidePresentations = new();
    private readonly HashSet<DepthOcclusionPresentation> playerInsidePresentations = new();
    private readonly HashSet<DepthOcclusionPresentation> playerOccludedPresentations = new();
    private readonly Dictionary<SortingStateKey, bool> sortingStates = new();
    private readonly Dictionary<SpriteRenderer, int> baseSortingOrders = new();

    private float nextSurfaceRefreshTime;

    public int SurfaceCount => surfaces.Count;

    private void OnEnable()
    {
        RefreshSceneObjects();
    }

    private void OnDisable()
    {
        RestorePresentation();
    }

    private void LateUpdate()
    {
        RefreshSceneObjectsIfRequired();
        players.Clear();
        players.AddRange(FindObjectsByType<Virtual3DSize>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None));

        occludedPresentations.Clear();
        insidePresentations.Clear();

        foreach (var presentation in presentations)
        {
            presentation.SetOcclusionState(1f, false);
        }

        foreach (var player in players)
        {
            if (player is null || !player || player.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            var playerRenderer = player.GetComponent<SpriteRenderer>();
            if (!baseSortingOrders.ContainsKey(playerRenderer))
            {
                baseSortingOrders[playerRenderer] = playerRenderer.sortingOrder;
            }

            var hasSortingSurface = false;
            var bestSortingDistance = float.PositiveInfinity;
            var bestSortingOrder = baseSortingOrders[playerRenderer];
            var bestIsBehind = false;
            var bestIsInside = false;

            var affectsLocalOpacity = AffectsLocalOpacity(player);
            playerInsidePresentations.Clear();
            playerOccludedPresentations.Clear();
            foreach (var presentation in presentations)
            {
                if (!presentation.IsInside(player))
                {
                    continue;
                }

                playerInsidePresentations.Add(presentation);
                if (affectsLocalOpacity)
                {
                    insidePresentations.Add(presentation);
                }
            }

            player.GetProjectedPolygon(playerPolygon);
            var playerFootprint = player.FootprintBounds;
            var playerDepthReference = new Vector2(
                playerFootprint.center.x,
                playerFootprint.center.y);
            foreach (var surface in surfaces)
            {
                if (surface is null || !surface || !surface.IsConfigured)
                {
                    continue;
                }

                var surfaceDepthKey = surface.GetDepthKey(playerDepthReference);
                var isBehind = ResolveBehind(surface, player, surfaceDepthKey);
                var presentation = surface.Presentation;
                var isInside = presentation is not null
                    && presentation
                    && playerInsidePresentations.Contains(presentation);

                surface.GetProjectedPolygon(surfacePolygon);
                var overlapsVisibleShape = BuildingDepthGeometry.IntersectsPolygon(
                    surfacePolygon,
                    playerPolygon);
                if (affectsLocalOpacity
                    && presentation is not null
                    && presentation
                    && isBehind
                    && overlapsVisibleShape)
                {
                    occludedPresentations.Add(presentation);
                    playerOccludedPresentations.Add(presentation);
                }

                if (!overlapsVisibleShape && !isInside)
                {
                    continue;
                }

                var surfaceSortingOrder = surface.GetSortingOrder();
                var distance = Mathf.Abs(player.FrontY - surfaceDepthKey);
                if (isInside)
                {
                    if (bestIsInside && surfaceSortingOrder >= bestSortingOrder)
                    {
                        continue;
                    }

                    bestIsInside = true;
                    bestSortingDistance = distance;
                    bestSortingOrder = surfaceSortingOrder;
                    bestIsBehind = true;
                    hasSortingSurface = true;
                    continue;
                }

                if (bestIsInside)
                {
                    continue;
                }

                if (hasSortingSurface
                    && (distance > bestSortingDistance
                        || (Mathf.Abs(distance - bestSortingDistance)
                            <= SortingDistanceEpsilon
                            && !ShouldPreferSurface(
                                isBehind,
                                surfaceSortingOrder,
                                bestIsBehind,
                                bestSortingOrder))))
                {
                    continue;
                }

                bestSortingDistance = distance;
                bestSortingOrder = surfaceSortingOrder;
                bestIsBehind = isBehind;
                hasSortingSurface = true;
            }

            var sortingOrder = hasSortingSurface
                ? bestIsBehind ? bestSortingOrder - 1 : bestSortingOrder + 1
                : baseSortingOrders[playerRenderer];
            playerRenderer.sortingOrder = ClampToOccludedPresentations(
                sortingOrder,
                playerOccludedPresentations);
        }

        foreach (var presentation in presentations)
        {
            var isInside = insidePresentations.Contains(presentation);
            var isOccluded = occludedPresentations.Contains(presentation);
            var targetAlpha = isInside ? 0f : isOccluded ? occludedAlpha : 1f;
            presentation.SetOcclusionState(targetAlpha, isInside);
        }
    }

    public static bool ResolveBehind(
        float playerFeetY,
        float surfaceDepthKey,
        bool previousValue,
        float hysteresis)
    {
        if (previousValue)
        {
            return playerFeetY >= surfaceDepthKey - hysteresis;
        }

        return playerFeetY > surfaceDepthKey + hysteresis;
    }

    private bool ResolveBehind(
        DepthOcclusionSurface surface,
        Virtual3DSize player,
        float surfaceDepthKey)
    {
        var key = new SortingStateKey(surface.GetInstanceID(), player.GetInstanceID());
        if (!sortingStates.TryGetValue(key, out var previousValue))
        {
            previousValue = false;
        }

        var resolvedValue = ResolveBehind(
            player.FrontY,
            surfaceDepthKey,
            previousValue,
            sortingHysteresis);
        sortingStates[key] = resolvedValue;
        return resolvedValue;
    }

    private static bool ShouldPreferSurface(
        bool candidateIsBehind,
        int candidateSortingOrder,
        bool currentIsBehind,
        int currentSortingOrder)
    {
        if (candidateIsBehind != currentIsBehind)
        {
            return candidateIsBehind;
        }

        return candidateIsBehind
            ? candidateSortingOrder < currentSortingOrder
            : candidateSortingOrder > currentSortingOrder;
    }

    private static int ClampToOccludedPresentations(
        int sortingOrder,
        HashSet<DepthOcclusionPresentation> occludedPresentations)
    {
        foreach (var presentation in occludedPresentations)
        {
            if (presentation is not TestBuildingPresentation testBuilding
                || testBuilding.MinimumSortingOrder == int.MaxValue)
            {
                continue;
            }

            sortingOrder = Mathf.Min(
                sortingOrder,
                testBuilding.MinimumSortingOrder - 1);
        }

        return sortingOrder;
    }

    private void RefreshSceneObjectsIfRequired()
    {
        if (Time.unscaledTime < nextSurfaceRefreshTime)
        {
            return;
        }

        RefreshSceneObjects();
    }

    private void RefreshSceneObjects()
    {
        surfaces.Clear();
        presentations.Clear();
        knownSurfaces.Clear();
        knownPresentations.Clear();
        var collectedSurfaces = new List<DepthOcclusionSurface>();

        var sceneBehaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var behaviour in sceneBehaviours)
        {
            if (behaviour is null
                || !behaviour
                || behaviour.gameObject.scene != gameObject.scene)
            {
                continue;
            }

            if (behaviour is IDepthOcclusionSurfaceProvider provider)
            {
                provider.CollectOcclusionSurfaces(collectedSurfaces);
            }

            if (behaviour is DepthOcclusionPresentation presentation
                && knownPresentations.Add(presentation))
            {
                presentations.Add(presentation);
            }
        }

        var sceneSurfaces = FindObjectsByType<DepthOcclusionSurface>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var surface in sceneSurfaces)
        {
            AddSurface(surface);
        }

        foreach (var surface in collectedSurfaces)
        {
            AddSurface(surface);
        }

        nextSurfaceRefreshTime = Time.unscaledTime + surfaceRefreshInterval;
    }

    private void AddSurface(DepthOcclusionSurface surface)
    {
        if (surface is null
            || !surface
            || surface.gameObject.scene != gameObject.scene
            || !surface.isActiveAndEnabled
            || !knownSurfaces.Add(surface))
        {
            return;
        }

        surfaces.Add(surface);
    }

    private void RestorePresentation()
    {
        var scenePresentations = FindObjectsByType<DepthOcclusionPresentation>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var presentation in scenePresentations)
        {
            if (presentation.gameObject.scene == gameObject.scene)
            {
                presentation.SetOcclusionState(1f, false);
            }
        }

        foreach (var pair in baseSortingOrders)
        {
            if (pair.Key is not null && pair.Key)
            {
                pair.Key.sortingOrder = pair.Value;
            }
        }

        baseSortingOrders.Clear();
        sortingStates.Clear();
    }

    private static bool AffectsLocalOpacity(Virtual3DSize player)
    {
        if (!player.TryGetComponent<NetworkObject>(out var networkObject))
        {
            return true;
        }

        return networkObject.IsOwner;
    }
}
