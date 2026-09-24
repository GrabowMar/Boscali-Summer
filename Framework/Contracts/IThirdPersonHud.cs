namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// The local third-person presentation settings: the external-view HUD and the camera
    /// framing around it. Owned and applied by Hud; read and written only through this seam.
    /// </summary>
    internal interface IThirdPersonHud
    {
        bool IsEnabled { get; }
        bool ModifyVanillaHud { get; set; }
        HudBounds InstrumentBounds { get; }
        void Toggle();

        /// <summary>Hide the floating pitch ladder in third person; keep reticle, ammo and radar.</summary>
        bool HidePitchLadder { get; set; }

        /// <summary>Show the native target camera feed while contacts are selected.</summary>
        bool CameraFeedEnabled { get; set; }

        /// <summary>Smooth aircraft-relative orbit and rear-chase framing.</summary>
        bool FlightCameraEnabled { get; set; }

        bool BoardEnabled { get; set; }
        bool AirframeEnabled { get; set; }
        bool ShotsEnabled { get; set; }
        bool MarkEnabled { get; set; }
        int FlightScaleStep { get; set; }
        int FlightOpacityStep { get; set; }
        int FlightContrast { get; set; }
        int BoardCorner { get; set; }
        int BoardScaleStep { get; set; }
        int BoardOpacityStep { get; set; }
        int BoardContrast { get; set; }
        int BoardInsetX { get; set; }
        int BoardInsetY { get; set; }
        void ResetLayout();
    }
}
