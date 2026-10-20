using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>The rooms' own fixed hues, worn on top of the pilot's scheme (they are different consoles in different rooms). Keep text on them at 4.5:1.</summary>
    internal static class RoomInk
    {
        // ORBIT · space operations centre (deep-space navy glass, ice engraving, amber only for telemetry).
        public static readonly Color Plate = Rgb(0x0A1428, 0.97f), Bezel = Rgb(0x4A6A8F), Engrave = Rgb(0xEAF1FF), Telemetry = Rgb(0xFFB547),
            ConsoleBody = Rgb(0x050A18, 0.93f);
        // NETWORK · intrusion terminal.
        public static readonly Color Crt = Rgb(0x03100A, 0.97f), CrtBar = Rgb(0x062014, 0.98f), Phosphor = Rgb(0x7DFFA0), PhosphorDim = Rgb(0x46B86E),
            Trace = Rgb(0xFF5A4F);
        // SHADOW · dossier.
        public static readonly Color Manila = Rgb(0xCDBA8A), Typewriter = Rgb(0x221C12), Grease = Rgb(0xE5483C), Khaki = Rgb(0x7B7251),
            Board = Rgb(0x14130E, 0.93f);

        /// <summary>The rooms' one colour constructor for their drawn art (paper shadows, pencil, ruling, map tints): room code never builds a raw Color.</summary>
        public static Color Rgba(float r, float g, float b, float a) => new Color(r, g, b, a);

        public static Color Rgb(uint rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }
}
