using UnityEngine;

namespace ModePuzzle.UI
{
    static class PanelStyle
    {
        public const float PreferredWidth = 840, PreferredHeight = 760, MaxWidthShare = .5f, MaxHeightShare = .8f;
        public const float FrameMargin = 64, ChromeFallback = 96, RightSafety = 24;
        public const float MachineNameShare = .35f, StatusGap = 24, SpinnerSize = 20;
        public const float BodyFont = 15, CaptionFont = 13, ButtonFont = 15, ButtonPadding = 28;
        public const float ColumnGap = 16, SettingsGap = 24, ProgressLabelWidth = 72;
        public const float MaxBayShare = .5f, MaxBayHeight = 260;
        public const int MaxBayScale = 3;
        public const float NumberMargin = 12, NameFlexShare = .4f, ToggleBox = 32, ToggleScale = .75f, ToggleTextGap = 16, LockButtonWidth = 44;
        public const float StatColumn = 84, StatNamePad = 16;
        public const float MachineArtSize = 56, PickerCell = 104, PickerIcon = 80, PickerName = 22, PickerGap = 8;
        public const float PickerChrome = 72, PickerChromeHeight = 130, PickerMinWidth = 520, LockScale = 2, LockOutset = 2;
        public const float ConfirmWidth = 520, ConfirmHeight = 330;
        public const float BoundInput = 96, BoundArrow = 16, BoundHeight = 30;
        public const int BoundDigits = 6;
        public const float DropArrowSpace = 14, DropArrowWidth = 14, DropArrowHeight = 8, DropArrowInset = 6, MenuPadding = 4, MenuGap = 2, DropPadding = 14;
        public const int PickerColumns = 4;
        public const float IconSlot = 28, IconSize = 24, NumberWidth = 28, StatWidth = 64, RowHeight = 28;
        public const float NameShare = .22f, TraitShare = .18f;
        public const int FreeListMax = 200, CellInset = 1, Frame = 1, DigitWidth = 3, DigitHeight = 5, DigitGap = 1, NumberPadding = 1;
        public const string MutedHex = "9c968c", PositiveHex = "8cc56a", NegativeHex = "d9675a", SearchingHex = "e6c35c", StoppedHex = "d8a657";
        public static readonly Color Selected = new Color32(150, 88, 20, 255);
        public static readonly Color Muted = new Color32(156, 150, 140, 255);
        public static readonly Color PanelColor = new Color32(51, 44, 61, 255);
        public static readonly Px BayBackground = Px.Hex("39352f"), BayCell = Px.Hex("2b2523"), BayFrame = Px.Hex("2b2721");
        public static readonly Px LockPlate = Px.Hex("1c1820"), LockedInk = Px.Hex("ffb0a0"), LockedBody = Px.Hex("d9483b"),
            UnlockedInk = Px.Hex("c8f5a8"), UnlockedBody = Px.Hex("5fb83a");
        public static readonly Color InputFill = new Color32(74, 65, 85, 255), InputEdge = new Color32(156, 150, 140, 255);
        public static readonly Px SpinnerInk = Px.Hex("e6c35c");
        public static readonly Px ArrowInk = Px.Hex("e8e0d0"), ArrowDark = Px.Hex("2b2523");
        public static readonly Color MenuPlate = new Color32(43, 37, 35, 245);
        public static readonly Px NumberPlate = Px.Hex("211e1b"), NumberInk = Px.Hex("fff3d6");
    }
}
