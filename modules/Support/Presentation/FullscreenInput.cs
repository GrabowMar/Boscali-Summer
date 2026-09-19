namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Input ownership for a full-screen OPS overlay (the station uplink, the CYBER console):
    /// Rewired keyboard and the pause keybind are held while it is up and restored exactly as
    /// found. The map guard patch asks <see cref="AnyOpen"/>.
    /// </summary>
    internal sealed class FullscreenInput
    {
        private bool keyboardTouched;
        private bool keyboardWas;
        private bool pauseWas;

        public bool Held { get; private set; }

        /// <summary>True while any full-screen OPS overlay owns the mouse, and one frame after.</summary>
        public static bool AnyOpen => PlatformUplink.IsOpen || CyberConsole.IsOpen;

        public void Hold()
        {
            if (Held) return;
            Held = true;
            pauseWas = GameplayUI.AllowPauseKeybind;
            GameplayUI.AllowPauseKeybind = false;
            keyboardTouched = false;
            if (Rewired.ReInput.isReady && Rewired.ReInput.controllers != null && Rewired.ReInput.controllers.Keyboard != null)
            {
                keyboardWas = Rewired.ReInput.controllers.Keyboard.enabled;
                Rewired.ReInput.controllers.Keyboard.enabled = false;
                keyboardTouched = true;
            }
        }

        public void Release()
        {
            if (!Held) return;
            Held = false;
            GameplayUI.AllowPauseKeybind = pauseWas;
            if (keyboardTouched && Rewired.ReInput.isReady && Rewired.ReInput.controllers != null &&
                Rewired.ReInput.controllers.Keyboard != null)
                Rewired.ReInput.controllers.Keyboard.enabled = keyboardWas;
            keyboardTouched = false;
        }
    }
}
