using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>The rooms' own fixed hues, worn on top of the pilot's scheme (they are different consoles in different rooms). Keep text on them at 4.5:1.</summary>
    internal static class RoomInk
    {
        // ORBIT · space operations centre (deep-space navy glass, ice accent, amber only for telemetry).
        public static readonly Color Plate = Rgb(0x0A1428, 0.97f), Bezel = Rgb(0x4F86C6), Engrave = Rgb(0xEAF1FF), Telemetry = Rgb(0xFFB547),
            ConsoleBody = Rgb(0x050A18, 0.93f), Ice = Rgb(0x6CC8FF);
        // NETWORK · intrusion terminal, ICE: cyan glass over circuit, magenta for a hop in flight.
        public static readonly Color Crt = Rgb(0x04101A, 0.97f), CrtBar = Rgb(0x07202B, 0.98f), Phosphor = Rgb(0x22E6FF), PhosphorDim = Rgb(0x7FB3C4),
            PhosphorInk = Rgb(0xD8F7FF), Trace = Rgb(0xFF5A4F), Hop = Rgb(0xFF3DF2);
        // SHADOW · field tablet on the map table: gold ink on olive glass; paper only for the documents (dossier, cards, orders).
        public static readonly Color Manila = Rgb(0xCDBA8A), Typewriter = Rgb(0x221C12), Grease = Rgb(0xE5483C), Khaki = Rgb(0x7B7251),
            Board = Rgb(0x16170F, 0.93f), Gold = Rgb(0xE9C46A), OliveGlass = Rgb(0x1E2014, 0.96f), FieldInk = Rgb(0xEFE6CF);

        /// <summary>The rooms' one colour constructor for their drawn art (paper shadows, pencil, ruling, map tints): room code never builds a raw Color.</summary>
        public static Color Rgba(float r, float g, float b, float a) => new Color(r, g, b, a);

        public static Color Rgb(uint rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }
}
