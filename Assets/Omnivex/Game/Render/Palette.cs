using OMX.Core;
using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// ART BIBLE — Stage 1, "Vireth Tidepool"
    /// Style: Bioluminescent micro-abyss: glossy wet organisms glowing against a deep teal tide pool; everything alive
    ///        glows, everything dead goes dark.
    /// Palette: silt #0E2A33 · reef #1B3B47 · flora #2FD39A · flesh #FF4F7B · mineral #7FD6FF · volt #FFE04A ·
    ///          toxin #B25CFF · void #8C5CFF · abyss shadow #06141A · accent (Omnivex) #3CF2D8.
    /// Mood: cold shafts of surface light from above, warm living light from below; teal depth fog swallows the edges.
    /// Every colour in the game reads from this file.
    /// </summary>
    public static class Palette
    {
        public static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

        public static readonly Color Silt = Hex(0x0E2A33);
        public static readonly Color SiltLight = Hex(0x1A4550);
        public static readonly Color Reef = Hex(0x1B3B47);
        public static readonly Color ReefDark = Hex(0x10252E);
        public static readonly Color Sand = Hex(0x3C5A55);
        public static readonly Color Abyss = Hex(0x06141A);
        public static readonly Color Fog = Hex(0x0A3440);
        public static readonly Color Accent = Hex(0x3CF2D8);
        public static readonly Color Danger = Hex(0xFF3B4E);
        public static readonly Color Warning = Hex(0xFFB03A);
        public static readonly Color KeyLight = Hex(0xCFF6FF);
        public static readonly Color AmbSky = Hex(0x2E6E80);
        public static readonly Color AmbGround = Hex(0x14202A);
        public static readonly Color Ui = Hex(0xE6FFFB);
        public static readonly Color UiDim = Hex(0x8FB8B4);
        public static readonly Color UiPanel = new Color(0.02f, 0.09f, 0.11f, 0.86f);

        public static Color Of(Dna d) => d switch
        {
            Dna.Flesh => Hex(0xFF4F7B),
            Dna.Flora => Hex(0x2FD39A),
            Dna.Mineral => Hex(0x7FD6FF),
            Dna.Volt => Hex(0xFFE04A),
            Dna.Toxin => Hex(0xB25CFF),
            _ => Hex(0x8C5CFF),
        };

        public static Color Rarity(Rarity r) => r switch { Core.Rarity.Rare => Hex(0x5FB8FF), Core.Rarity.Legendary => Hex(0xFFB03A), _ => Hex(0xBFEDE6) };

        public static Color Hdr(Color c, float intensity) => new Color(c.r * intensity, c.g * intensity, c.b * intensity, 1);
    }
}
