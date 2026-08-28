using System.Collections.Generic;
using DefaultNamespace;
using FishNet.Object;
using Unity.Collections.NotBurstCompatible;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Generation
{
    /// <summary>
    /// Updates the tile map as the camera moves, generating new tiles procedurally based on seeded noise and a set of tile types.
    /// </summary>
    public class NAIProceduralTiles : MonoBehaviour
    {
        public int seed;
        public TileBase[] tiles;
        public Tilemap tileMap;
        public int chunkSize = 16;

        private Camera _cameraToFollow;

        private void Awake() => _cameraToFollow = Camera.main;

        private void Update()
        {
            var currentPos = Vector3Int.RoundToInt(_cameraToFollow.transform.position);
            var chunkBounds = NAIExtensions.GetBoundsFromCenter(currentPos, chunkSize);
            
            // Populate the tilemap with tiles based on the seed and the chunk bounds
            for (var x = chunkBounds.min.x; x < chunkBounds.max.x; x++)
            {
                for (var y = chunkBounds.min.y; y < chunkBounds.max.y; y++)
                {
                    var tilePos = new Vector3Int(x, y, 0);
                    if (tileMap.HasTile(tilePos)) continue;
                    var noiseValue = noise.snoise(new float2(x + seed, y + seed));
                    var tileIndex = Mathf.Abs((int)(noiseValue * tiles.Length)) % tiles.Length;
                    tileMap.SetTile(tilePos, tiles[tileIndex]);
                }
            }
        }
    }
}