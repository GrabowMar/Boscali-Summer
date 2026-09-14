namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// The local third-person presentation settings: the external-view HUD and the camera
    /// framing around it. Owned and applied by QoL; read and written only through this seam.
    /// </summary>
    internal interface IThirdPersonHud
    {
        bool IsEnabled { get; }
        void Toggle();

        /// <summary>Hide the floating pitch ladder in third person; keep reticle, ammo and radar.</summary>
        bool HidePitchLadder { get; set; }

        /// <summary>Show the native target camera feed while contacts are selected.</summary>
        bool CameraFeedEnabled { get; set; }

        /// <summary>Smooth aircraft-relative orbit and rear-chase framing.</summary>
        bool FlightCameraEnabled { get; set; }
    }
}
