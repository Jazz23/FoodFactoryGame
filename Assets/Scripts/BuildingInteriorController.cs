using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingInteriorController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float doorOpenDistance = 0.9f;
    [SerializeField, Min(0f)] private float doorOpenSpeed = 5f;
    [SerializeField] private Vector3 doorOpenOffset = new(0.38f, 0.19f, 0f);

    private readonly List<Virtual3DSize> players = new();
    private Transform door;
    private Transform doorProximity;
    private EdgeCollider2D doorCollider;
    private PolygonCollider2D interiorTrigger;
    private Vector3 closedDoorPosition;
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
        door = transform.Find("Door");
        doorProximity = transform.Find("Door Proximity");
        doorCollider = transform.Find("Door Collision")?.GetComponent<EdgeCollider2D>();
        interiorTrigger = GetComponent<PolygonCollider2D>();
        closedDoorPosition = door != null ? door.localPosition : Vector3.zero;
        RefreshPlayers();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPlayerRefreshTime)
        {
            RefreshPlayers();
        }

        bool playerNearDoor = false;
        if (door != null)
        {
            foreach (Virtual3DSize player in players)
            {
                Vector3 proximityPosition = doorProximity != null ? doorProximity.position : door.position;
                if (player != null && Vector2.Distance(player.transform.position, proximityPosition) <= doorOpenDistance)
                {
                    playerNearDoor = true;
                    break;
                }
            }

            bool doorOpen = playerNearDoor || HasPlayerInside;
            Vector3 targetPosition = closedDoorPosition + (doorOpen ? doorOpenOffset : Vector3.zero);
            door.localPosition = Vector3.MoveTowards(
                door.localPosition,
                targetPosition,
                doorOpenSpeed * Time.deltaTime);

            if (doorCollider != null)
            {
                doorCollider.enabled = !doorOpen;
            }
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
