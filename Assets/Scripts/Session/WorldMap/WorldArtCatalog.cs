// Content: the world art meshes and materials from ArtSource/World (WorldArt.fbx), looked up by piece name. Authored by
// AgentScripts/BuildWorldArt.cs. Presentation only; nothing here is game state.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoodFactoryGame.Session.WorldMap
{
    [CreateAssetMenu(menuName = "Food Factory/World Art Catalog")]
    public sealed class WorldArtCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Piece
        {
            public string name;
            public Mesh mesh;
            public Material[] materials = Array.Empty<Material>();
        }

        [SerializeField] private Piece[] pieces = Array.Empty<Piece>();
        // Ground cover drawn on the shared ground quad; tiled per instance.
        public Material grass;
        public Material paving;
        public Material yard;
        public Material wheat;
        public Material greens;
        // River water and the carved river banks (generator v2 layouts).
        public Material water;
        public Material bank;

        private Dictionary<string, Piece> _byName;

        public IReadOnlyList<Piece> Pieces => pieces;

        public Piece Get(string pieceName)
        {
            if (_byName == null || _byName.Count != pieces.Length)
            {
                _byName = new Dictionary<string, Piece>(StringComparer.Ordinal);
                foreach (var piece in pieces) _byName[piece.name] = piece;
            }
            return _byName.TryGetValue(pieceName, out var found) ? found : throw new KeyException(pieceName);
        }

        public void SetPieces(Piece[] value)
        {
            pieces = value;
            _byName = null;
        }

        public sealed class KeyException : Exception
        {
            public KeyException(string pieceName) : base($"World art piece '{pieceName}' is missing from the catalog.") { }
        }
    }
}
