// Presentation only: the figures on foot in this client's scene (player avatars and drawn customers), so the drawn city cars
// can stop for anyone in their lane. Destroyed figures drop out on the next read; hidden ones are skipped.
using System.Collections.Generic;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    public static class Pedestrians
    {
        private static readonly List<Transform> Figures = new();

        public static void Add(Transform figure)
        {
            if (figure != null && !Figures.Contains(figure)) Figures.Add(figure);
        }

        public static void Remove(Transform figure) => Figures.Remove(figure);

        // Scene positions of every live, shown figure.
        public static void Positions(List<Vector3> result)
        {
            result.Clear();
            Figures.RemoveAll(x => x == null);
            foreach (var figure in Figures)
                if (figure.gameObject.activeInHierarchy) result.Add(figure.position);
        }
    }
}
