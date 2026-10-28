namespace BoscaliSummer.Modules.Comms.Domain
{
    /// <summary>How loud a comms item is. Decides its ink and its HUD tone; the words say the same.</summary>
    internal enum CommsTone : byte
    {
        Info = 0,
        Friendly = 1,
        Caution = 2,
        Danger = 3,
        Fun = 4,
    }

    /// <summary>Who hears it: the sender's own side, or every player on the server.</summary>
    internal enum CommsChannel : byte
    {
        Team = 0,
        All = 1,
    }

    /// <summary>One tactical ping type: a code for the map, a phrase for the feed, a glyph and a tone.</summary>
    internal readonly struct PingKind
    {
        public PingKind(string code, string phrase, string glyph, CommsTone tone, bool ring)
        {
            Code = code;
            Phrase = phrase;
            Glyph = glyph;
            Tone = tone;
            Ring = ring;
        }

        public readonly string Code;
        public readonly string Phrase;
        public readonly string Glyph;
        public readonly CommsTone Tone;

        /// <summary>Draw a threat ring under the glyph: an area, not a point, is what matters.</summary>
        public readonly bool Ring;
    }

    /// <summary>A pen colour, as bytes so the rules and the wire never touch Unity.</summary>
    internal readonly struct PenInk
    {
        public PenInk(string name, byte r, byte g, byte b)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
        }

        public readonly string Name;
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
    }

    /// <summary>
    /// A canned radio call. Brevity first: the words a pilot would actually key, not prose.
    /// A call that means "here" can drop a ping at the caller's own position.
    /// </summary>
    internal readonly struct BrevityCall
    {
        public BrevityCall(string code, string meaning, CommsTone tone, int pingAtSelf = -1)
        {
            Code = code;
            Meaning = meaning;
            Tone = tone;
            PingAtSelf = pingAtSelf;
        }

        public readonly string Code;
        public readonly string Meaning;
        public readonly CommsTone Tone;

        /// <summary>Ping kind dropped at the caller's position, or -1 for a call without one.</summary>
        public readonly int PingAtSelf;

        public bool MarksPosition => PingAtSelf >= 0;
    }

    /// <summary>
    /// Every table the COMMS screen and its rules share. Indices are the wire values, so a
    /// row is only ever appended, never inserted or reordered.
    /// </summary>
    internal static class CommsCatalog
    {
        public const int PingAttack = 3;
        public const int PingRally = 5;
        public const int PingHelp = 6;
        public const int PingSpike = 7;

        public static readonly PingKind[] Pings =
        {
            new PingKind("MARK", "LOOK HERE", "mark", CommsTone.Info, false),
            new PingKind("ENEMY", "ENEMY SPOTTED", "enemy", CommsTone.Danger, false),
            new PingKind("SAM", "SAM THREAT", "sam", CommsTone.Danger, true),
            new PingKind("ATTACK", "ATTACK HERE", "attack", CommsTone.Caution, false),
            new PingKind("DEFEND", "DEFEND HERE", "defend", CommsTone.Friendly, true),
            new PingKind("RALLY", "RALLY HERE", "rally", CommsTone.Friendly, false),
            // Not offered on the palette: the NEED SUPPORT and SPIKE calls drop these at the caller.
            new PingKind("HELP", "NEEDS SUPPORT", "help", CommsTone.Caution, true),
            new PingKind("SPIKE", "SPIKED", "spike", CommsTone.Danger, false),
        };

        /// <summary>Ping kinds a player picks from; HELP and SPIKE only come from their calls.</summary>
        public const int PalettePings = 6;

        public static readonly PenInk[] Pens =
        {
            new PenInk("WHITE", 240, 244, 246),
            new PenInk("CYAN", 70, 214, 255),
            new PenInk("YELLOW", 255, 214, 64),
            new PenInk("RED", 255, 84, 72),
            new PenInk("GREEN", 96, 236, 128),
            new PenInk("PINK", 255, 110, 214),
        };

        /// <summary>Stroke half-widths in screen pixels, by width index.</summary>
        public static readonly float[] PenWidths = { 1.1f, 1.9f, 3.2f };

        public static readonly string[] PenWidthNames = { "THIN", "MED", "THICK" };

        public static readonly BrevityCall[] Calls =
        {
            new BrevityCall("ON MY WAY", "Moving to support.", CommsTone.Friendly, PingRally),
            new BrevityCall("NEED SUPPORT", "Requesting help at my position.", CommsTone.Caution, PingHelp),
            new BrevityCall("ENGAGING", "Engaging targets near my position.", CommsTone.Caution, PingAttack),
            new BrevityCall("SPIKE", "Being tracked by enemy radar.", CommsTone.Danger, PingSpike),
            new BrevityCall("WINCHESTER", "Out of weapons.", CommsTone.Info),
            new BrevityCall("BINGO FUEL", "Fuel low, heading home soon.", CommsTone.Caution),
            new BrevityCall("RTB", "Returning to base.", CommsTone.Info),
            new BrevityCall("SPLASH ONE", "Target destroyed.", CommsTone.Friendly),
            new BrevityCall("COPY", "Understood.", CommsTone.Info),
            new BrevityCall("NEGATIVE", "No / unable.", CommsTone.Info),
            new BrevityCall("THANKS", "Thanks for the help.", CommsTone.Fun),
            new BrevityCall("GG", "Good game.", CommsTone.Fun),
        };

        public static bool ValidPing(int index) => index >= 0 && index < Pings.Length;
        public static bool ValidPen(int index) => index >= 0 && index < Pens.Length;
        public static bool ValidWidth(int index) => index >= 0 && index < PenWidths.Length;
        public static bool ValidCall(int index) => index >= 0 && index < Calls.Length;
    }
}
