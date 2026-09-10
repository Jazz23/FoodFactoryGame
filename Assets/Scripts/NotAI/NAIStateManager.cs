// Owns persistent buildable state and restores buildables when the server starts.
using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SQLite;
using UnityEngine;

namespace NotAI
{
    public class NAIStateManager : NetworkBehaviour
    {
        private const string DatabasePath = "World.db";
        public List<GameObject> buildingPrefabs;

        // Maps the world pos of a cell to the guid of the building that occupies it. If a 4x4 building is placed, then all 4 cells with have the same guid.
        public static SyncDictionary<Vector2, Guid> OccupiedTiles => _instance._occupiedTiles;
        private readonly SyncDictionary<Vector2, Guid> _occupiedTiles = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));

        public static SyncDictionary<Guid, NAIBuildable> Buildables => _instance._buildables;
        private readonly SyncDictionary<Guid, NAIBuildable> _buildables = new(new SyncTypeSettings(WritePermission.ClientUnsynchronized));

        private static NAIStateManager _instance;

        public override void OnStartNetwork() => _instance = this;

        private SQLiteAsyncConnection _db;

        public override void OnStartServer()
        {
            _db = new SQLiteAsyncConnection(DatabasePath);
            _buildables.OnChange += OnBuildablesChanged;
            LoadDatabase();
        }

        public static NAIBuildablePredictiveSpawn SpawnBuilding(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return Instantiate(prefab, position, rotation).GetComponent<NAIBuildablePredictiveSpawn>();
        }

        private async void LoadDatabase()
        {
            await _db.CreateTableAsync<BuildableData>();
            var buildables = await _db.Table<BuildableData>().ToListAsync();
            foreach (var data in buildables)
            {
                var position = new Vector3(data.PositionX, data.PositionY, data.PositionZ);
                var building = SpawnBuilding(
                    buildingPrefabs[data.BuildableId],
                    position,
                    Quaternion.Euler(0, 0, data.RotationZ));
                var buildable = building.GetComponent<NAIBuildable>();
                buildable.guid = data.Guid;
                buildable.buildableId = data.BuildableId;
                buildable.State = data.State;

                ServerManager.Spawn(building);
                Buildables[data.Guid] = buildable;
                UpdateGrid(position, building.GetComponent<SpriteRenderer>().bounds.size, data.Guid);
            }
        }

        private void OnBuildablesChanged(SyncDictionaryOperation op, Guid key, NAIBuildable value, bool asServer)
        {
            SaveDatabase(value);
        }
        
        private async void SaveDatabase(NAIBuildable buildable)
        {
            var data = new BuildableData
            {
                Guid = buildable.guid,
                BuildableId = buildable.buildableId,
                State = buildable.State,
                PositionX = buildable.transform.position.x,
                PositionY = buildable.transform.position.y,
                PositionZ = buildable.transform.position.z,
                RotationZ = buildable.transform.eulerAngles.z
            };
            
            await _db.InsertOrReplaceAsync(data);
        }
        
        [Table("buildables")]
        private class BuildableData
        {
            [PrimaryKey]
            public Guid Guid { get; set; }
            public int BuildableId { get; set; }
            public byte[] State { get; set; }
            public float PositionX { get; set; }
            public float PositionY { get; set; }
            public float PositionZ { get; set; }
            public float RotationZ { get; set; }
        }

        private void UpdateGrid(Vector3 position, Vector2 size, Guid guid)
        {
            size.x = Mathf.RoundToInt(size.x * 1000f) / 1000f;
            size.y = Mathf.RoundToInt(size.y * 1000f) / 1000f;

            var cellSize = GameObject.Find("Grid").GetComponent<Grid>().cellSize;
            var cellsX = Mathf.CeilToInt(size.x / cellSize.x);
            var cellsY = Mathf.CeilToInt(size.y / cellSize.y);
            for (var x = 0; x < cellsX; x++)
            {
                for (var y = 0; y < cellsY; y++)
                {
                    OccupiedTiles[new Vector2(
                        Mathf.FloorToInt(position.x) + x * cellSize.x,
                        Mathf.FloorToInt(position.y) + y * cellSize.y)] = guid;
                }
            }
        }
    }
}
