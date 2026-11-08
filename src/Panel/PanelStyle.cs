using UnityEngine;

namespace ModePuzzle.UI
{
    static class PanelStyle
    {
        public const float PreferredWidth = 980, PreferredHeight = 680, MaxWidthShare = .5f, MaxHeightShare = .8f;
        public const float FrameMargin = 64, ChromeFallback = 96, RightSafety = 24;
        public const float MachineNameShare = .35f, StatusShare = .20f;
        public const float BodyFont = 15, CaptionFont = 13, ButtonFont = 15, ButtonPadding = 28;
        public const float ColumnGap = 16, SettingsGap = 24, MetricRowMinimum = 420, ProgressLabelWidth = 90;
        public const float MinBayShare = .30f, MaxBayShare = .40f, MaxBayHeight = 220;
        public const int MaxBayScale = 2;
        public const float IconSlot = 28, IconSize = 24, NumberWidth = 28, StatWidth = 64, RowHeight = 28;
        public const float NameShare = .22f, TraitShare = .18f, StatCaptionShare = .28f, StatShare = .24f;
        public const int FreeListMax = 200, CellInset = 1, Frame = 1, DigitWidth = 3, DigitHeight = 5, DigitGap = 1, NumberPadding = 1;
        public const string MutedHex = "9c968c", PositiveHex = "8cc56a", NegativeHex = "d9675a", SearchingHex = "e6c35c", StoppedHex = "d8a657";
        public static readonly Color Muted = new Color32(156, 150, 140, 255);
        public static readonly Color PanelColor = new Color32(51, 44, 61, 255);
        public static readonly Px BayBackground = Px.Hex("39352f"), BayCell = Px.Hex("2b2523"), BayFrame = Px.Hex("2b2721");
        public static readonly Px NumberPlate = Px.Hex("211e1b"), NumberInk = Px.Hex("fff3d6");
    }
}
