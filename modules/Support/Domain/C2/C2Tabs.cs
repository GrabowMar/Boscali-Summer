namespace BoscaliSummer.Modules.Support.Domain.C2
{
    internal static class C2Tabs
    {
        /// <summary>Digit hotkeys are taken only over the OPS page or in the full-screen window, never while typing.</summary>
        public static bool KeyAllowed(bool pointerOverPage, bool fullScreenOpen, bool textFieldFocused) =>
            (pointerOverPage || fullScreenOpen) && !textFieldFocused;
    }
}
