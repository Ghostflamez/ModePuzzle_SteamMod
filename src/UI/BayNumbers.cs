using System.Linq;
using ModePuzzle.Model;

namespace ModePuzzle.UI
{
    /// <summary>Upright 3x5 digits, anchored to the last occupied cell in legend order.</summary>
    static class BayNumbers
    {
        static readonly string[] Digits = {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"
        };

        public static void Stamp(Px[] dst, int width, int height, PlacedModule placed, int number)
        {
            var cells = placed.Cells.Where(c => c.X >= 0 && c.Y >= 0 &&
                (c.X + 1) * ModuleArt.CellPixels <= width && (c.Y + 1) * ModuleArt.CellPixels <= height)
                .OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
            if (cells.Length == 0) return;
            var cell = cells[cells.Length - 1];
            string text = number.ToString();
            int pad = PanelStyle.NumberPadding, pitch = PanelStyle.DigitWidth + PanelStyle.DigitGap;
            int w = text.Length * pitch - PanelStyle.DigitGap + 2 * pad, h = PanelStyle.DigitHeight + 2 * pad;
            int left = (cell.X + 1) * ModuleArt.CellPixels - w - PanelStyle.CellInset;
            int top = (cell.Y + 1) * ModuleArt.CellPixels - h - PanelStyle.CellInset;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) dst[(top + y) * width + left + x] = PanelStyle.NumberPlate;
            for (int i = 0; i < text.Length; i++)
                for (int y = 0; y < PanelStyle.DigitHeight; y++)
                    for (int x = 0; x < PanelStyle.DigitWidth; x++)
                        if (Digits[text[i] - '0'][y * PanelStyle.DigitWidth + x] == '1')
                            dst[(top + pad + y) * width + left + pad + i * pitch + x] = PanelStyle.NumberInk;
        }
    }
}
