using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingInteriorController : MonoBehaviour
{
    private readonly List<Virtual3DSize> players = new();
    private PolygonCollider2D interiorTrigger;
    private float nextPlayerRefreshTime;

    public bool HasPlayerInside
    {
        get
        {
            if (interiorTrigger == null)
            {
                return false;
            }

            foreach (Virtual3DSize player in players)
            {
                if (IsInside(player))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool IsInside(Virtual3DSize player)
    {
        return player != null
            && interiorTrigger != null
            && interiorTrigger.OverlapPoint(new Vector2(player.transform.position.x, player.FrontY));
    }

    private void Awake()
    {
        interiorTrigger = GetComponent<PolygonCollider2D>();
        RefreshPlayers();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPlayerRefreshTime)
        {
            RefreshPlayers();
        }
    }

    private void RefreshPlayers()
    {
        players.Clear();
        players.AddRange(FindObjectsByType<Virtual3DSize>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None));
        nextPlayerRefreshTime = Time.unscaledTime + 0.25f;
    }
}
