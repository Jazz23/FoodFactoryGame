// Marks a presentation-only belt instance with the stable belt ID it shows, so aim rays can name the belt. Never owns state.
using UnityEngine;

namespace FoodFactoryGame.Session.Belts
{
    [DisallowMultipleComponent]
    public sealed class BeltVisual : MonoBehaviour
    {
        public string BeltId { get; private set; }

        public void Bind(string beltId) => BeltId = beltId;
    }
}
