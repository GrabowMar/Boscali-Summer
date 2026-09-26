namespace BoscaliSummer.Features.Support.Presentation.Window
{
    /// <summary>Root geometry and motion only. Nothing here describes a room interior.</summary>
    internal static class OpsWindowStyle
    {
        public const float ShadowFalloff = 48f;
        public const float ShadowOffsetY = -8f;
        public const float ShadowAlpha = 0.55f;
        public const float NotchHeight = NOAvionics.Ui.AvRoomFrame.NotchHeight;
        public const float NotchWidth = 164f;
        public const float NotchGap = 4f;
        public const float NotchInset = NOAvionics.Ui.AvRoomFrame.NotchInset;
        public const float NotchCut = 7f;
        public const float CloseWidth = 132f;
        public const int SortingOrder = 30000;
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;
    }
}
