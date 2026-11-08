using ModePuzzle.UI;
using UnityEngine;

namespace ModePuzzle
{
    static partial class OptimizerPanel
    {
        static CustomUIElement spinnerElement;
        static readonly Sprite[] spinnerFrames = new Sprite[8];
        // Top-left corners of the eight 2x2 dots on a 10x10 ring, clockwise from the top.
        static readonly int[,] SpinnerDots = { { 4, 0 }, { 7, 1 }, { 8, 4 }, { 7, 7 }, { 4, 8 }, { 1, 7 }, { 0, 4 }, { 1, 1 } };

        /// <summary>Original pixel-art spinner, frame <paramref name="index"/> (mod 8).</summary>
        static Sprite SpinnerFrame(int index)
        {
            int frame = ((index % 8) + 8) % 8;
            if (spinnerFrames[frame] != null) return spinnerFrames[frame];
            const int side = 10;
            var px = new Px[side * side];
            for (int d = 0; d < 8; d++)
            {
                int age = ((frame - d) % 8 + 8) % 8;   // 0 = head
                var c = PanelStyle.SpinnerInk;
                byte alpha = (byte)(age == 0 ? 255 : age == 1 ? 190 : age == 2 ? 130 : 60);
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        px[(SpinnerDots[d, 1] + y) * side + SpinnerDots[d, 0] + x] = new Px(c.R, c.G, c.B, alpha);
            }
            return spinnerFrames[frame] = Pixels.ToSprite(px, side, side, "modepuzzle_spinner_" + frame);
        }
    }
}
