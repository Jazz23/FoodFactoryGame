// Authored presentation content for restaurant structure (decision 0034): which art-kit models draw each wall finish, door
// leaf and window style the server accepts (GoodsWorld.WallStyles, DoorStyles, WindowStyles), plus the shared doorway bays and
// frames. The server never reads it; a shell is drawn with plain boxes where a style has no entry. Authored by
// AgentScripts/BuildRestaurantContent.cs; SessionAuthoringTests checks every server style has its models.
using System;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Session.Buildings
{
    [CreateAssetMenu(menuName = "Food Factory/Restaurant Style Catalog")]
    public sealed class RestaurantStyleCatalog : ScriptableObject
    {
        [Serializable] public sealed class WallFinish
        {
            public string style;
            public string displayName;
            // Straight pieces 1 m and 2 m long (pivot at the base centreline) and the post that covers ends and junctions.
            public GameObject wall1m;
            public GameObject wall2m;
            public GameObject end;
        }

        [Serializable] public sealed class DoorLeaf
        {
            public string style;
            public string displayName;
            // Hinged leaf, hinge at its origin, extending toward local -X in Unity (art kit README).
            public GameObject leaf;
        }

        [Serializable] public sealed class WindowStyle
        {
            public string style;
            public string displayName;
            // The 2 m wall bay with its opening, and the glazed unit set into it (none for a serving hatch, whose bay is all of it).
            public GameObject bay;
            public GameObject window;
        }

        [SerializeField] private WallFinish[] wallFinishes = Array.Empty<WallFinish>();
        [SerializeField] private DoorLeaf[] doorLeaves = Array.Empty<DoorLeaf>();
        [SerializeField] private WindowStyle[] windowStyles = Array.Empty<WindowStyle>();
        // A single door's 2 m bay (1.04 m opening) and frame; a pair of door cells uses the 3 m bay (2.04 m opening) and double frame.
        [SerializeField] private GameObject singleDoorway;
        [SerializeField] private GameObject doubleDoorway;
        [SerializeField] private GameObject singleFrame;
        [SerializeField] private GameObject doubleFrame;

        public WallFinish[] WallFinishes => wallFinishes;
        public DoorLeaf[] DoorLeaves => doorLeaves;
        public WindowStyle[] WindowStyles => windowStyles;
        public GameObject SingleDoorway => singleDoorway;
        public GameObject DoubleDoorway => doubleDoorway;
        public GameObject SingleFrame => singleFrame;
        public GameObject DoubleFrame => doubleFrame;

        // Empty or unknown styles fall back to the first entry (plaster walls, panel doors, picture windows).
        public WallFinish Finish(string style) => wallFinishes.FirstOrDefault(x => x.style == style) ?? wallFinishes.FirstOrDefault();
        public DoorLeaf Leaf(string style) => doorLeaves.FirstOrDefault(x => x.style == style) ?? doorLeaves.FirstOrDefault();
        public WindowStyle Window(string style) => windowStyles.FirstOrDefault(x => x.style == style) ?? windowStyles.FirstOrDefault();

        public string NameOf(string style) =>
            wallFinishes.FirstOrDefault(x => x.style == style)?.displayName ?? doorLeaves.FirstOrDefault(x => x.style == style)?.displayName
            ?? windowStyles.FirstOrDefault(x => x.style == style)?.displayName ?? style;
    }
}
