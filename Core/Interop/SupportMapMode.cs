namespace BoscaliSummer.Interop
{
    /// <summary>
    /// Backward-compatible reflection flag for Wing Command builds predating the shared
    /// <c>MapPicker</c>. New builds arbitrate through NOAvionics; this mirror keeps mixed
    /// installations from consuming one map click twice.
    ///
    /// <para>Resolved by name — <c>Type.GetType("BoscaliSummer.Interop.SupportMapMode,
    /// BoscaliSummer")</c> — with no assembly reference. It mirrors Wing Command's
    /// <c>WingCommand.Interop.WingMapMode.GestureArmed</c>.</para>
    /// </summary>
    public static class SupportMapMode
    {
        public static int ApiVersion => 1;

        /// <summary>Whether a support action is armed for a map click right now.</summary>
        public static bool GestureArmed { get; internal set; }
    }
}
