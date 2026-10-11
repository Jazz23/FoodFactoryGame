// The UI palette for code that draws with a painter (the removal wheels), mirroring the tokens in Assets/UI/Theme/Theme.uss.
// Every element's look lives in USS (decision 0042); keep these in step with the --ff-* tokens there.
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public static class HudTheme
    {
        public static readonly Color Text = Hex(0xECE8E1);
        public static readonly Color Accent = Hex(0xF2A33A);

        public static Color Hex(int rgb, float alpha = 1f) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}
