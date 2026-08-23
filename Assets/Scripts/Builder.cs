using FishNet.Object;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Builder : NetworkBehaviour
{
    [SerializeField] private Tilemap ground;
    [SerializeField] private TileBase grass;
    
    private void Start()
    {
        // ground.SetTile(Vector3Int.zero, grass);
        // ground.SetTile(new Vector3Int(1, 0, 0), grass);
        // ground.SetTile(new Vector3Int(0, 1, 0), grass);
        // ground.SetTile(new Vector3Int(1, 1, 0), grass);
    }
}
