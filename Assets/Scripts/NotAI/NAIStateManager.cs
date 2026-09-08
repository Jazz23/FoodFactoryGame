using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using SQLite;
using UnityEngine;

namespace NotAI
{
    public class NAIStateManager : NetworkBehaviour
    {
        private const string DatabasePath = "World.db";
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
            Task.Run(LoadDatabase);
        }

        private async Task LoadDatabase()
        {
            // await _db.CreateTableAsync<BuildableData>();
            // var buildables = await _db.Table<BuildableData>().ToListAsync();
            // foreach (var data in buildables)
            // {
            //     var prefab = FindPrefabById(data.BuildableId);
            //     if (prefab == null) continue;
            //
            //     var go = Instantiate(prefab);
            //     var buildable = go.GetComponent<NAIBuildable>();
            //     buildable.guid = data.Guid;
            //     buildable.buildableId = data.BuildableId;
            //     buildable.State = data.State;
            //
            //     ServerManager.Spawn(go);
            //
            //     Buildables[data.Guid] = buildable;
            // }
        }

        private void OnBuildablesChanged(SyncDictionaryOperation op, Guid key, NAIBuildable value, bool asServer)
        {
            Task.Run(() => SaveDatabase(value));
        }
        
        private async Task SaveDatabase(NAIBuildable buildable)
        {
            var data = new BuildableData
            {
                Guid = buildable.guid,
                BuildableId = buildable.buildableId,
                State = buildable.State
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
        }
    }
}