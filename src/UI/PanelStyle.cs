using UnityEngine;

namespace ModePuzzle.UI
{
    static class PanelStyle
    {
        public const float PreferredWidth = 980, PreferredHeight = 680, MaxWidthShare = .5f, MaxHeightShare = .8f;
        public const float FrameMargin = 64, MachineNameShare = .38f;
        public const float BodyFont = 16, CaptionFont = 13, ValueFont = 24, MetricFont = 16;
        public const float SectionSpace = 14, TilePadding = 8, TileHeight = 72, IconSlot = 36, IconSize = 30;
        public const float NumberWidth = 28, TableNameShare = .34f, FreeNameShare = .30f, FreeStatShare = .14f;
        public const float ButtonFont = 14, ButtonPadding = 18, WideBar = 760, StatusShare = .16f, MetricRowMinimum = 640;
        public const int FreeListMax = 200, CellInset = 1, Frame = 1, DigitWidth = 3, DigitHeight = 5, DigitGap = 1, NumberPadding = 1;
        public static readonly Color TileColor = new Color32(43, 37, 35, 255);
        public static readonly Px BayBackground = Px.Hex("39352f"), BayCell = Px.Hex("2b2523"), BayFrame = Px.Hex("2b2721");
        public static readonly Px NumberPlate = Px.Hex("211e1b"), NumberInk = Px.Hex("fff3d6");
    }
}
