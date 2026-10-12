using System;

namespace NOAvionics
{
    /// <summary>
    /// Ambient cockpit facts for idle chrome: a footer's status column and the bay plate's maker line.
    /// The host mod supplies them; nothing here invents a reading. With no provider the consumers stay hidden.
    /// </summary>
    public static class AvAmbient
    {
        public const int Lines = 3;

        /// <summary>Line <c>0..2</c> of the footer status column (mission clock, link, airframe). Null or empty hides the line.</summary>
        public static Func<int, string> Status;

        /// <summary>Maker stencilled on equipment data plates. Fiction, not a reading.</summary>
        public static string Maker = "AVIONICS WORKS";

        internal static string Line(int index)
        {
            if (Status == null) return "";
            try { return Status(index) ?? ""; }
            catch (Exception) { Status = null; return ""; }   // a faulting provider is dropped, not retried every tick
        }
    }
}
