using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Landing gear as an annunciator sees it.</summary>
    internal enum GearLamp { Unknown, Up, Down, Moving }

    /// <summary>How one annunciator lamp is lit. Dark is "nothing to say"; the rest light in a tone.</summary>
    internal enum LampState { Dark, On, Caution, Warning }

    /// <summary>One tick of own-aircraft system states; null = the game does not expose it on this aircraft.</summary>
    internal struct SystemStates
    {
        public GearLamp Gear;
        public bool? FlightAssist, AutoHover, RadarEmitting, NightVision;
        /// <summary>The laser designator: null = the aircraft has none; true while it holds a lased target (LASR).</summary>
        public bool? Laser;
        /// <summary>The fuel state (its JOKER / BINGO calls light FUEL; not <see cref="FuelState.Valid"/>: no aircraft, no FUEL or DMG lamp) and the damage state (DMG).</summary>
        public FuelState Fuel;
        public DamageState Damage;
        /// <summary>Enemy HEAT on the pilot, 0..1 of the ace threshold; NaN: ace hunts are off (no ACE lamp). <see cref="AcesInbound"/>: an ace wing is hunting the pilot.</summary>
        public float Heat;
        public bool AcesInbound;
        /// <summary>The NVG lamp's word when the goggles report a gain state (NVG A, NVG 2, NVG GATE); null = plain NVG.</summary>
        public string NvgLegend;
    }

    /// <summary>One lamp: its legend and how it is lit. Flashing is for state in transition.</summary>
    internal struct Lamp
    {
        public string Legend;
        public LampState State;
        public bool Flash;
        public bool Present;
    }

    /// <summary>
    /// The aircraft's own annunciator lamps (bar v3), read-only: the SYS cell's fixed 4 x 2 grid, R2 the urgent row LASR (or the fuel call) DMG ACE GEAR, R3 the modes RDR NVG HOVR FA OFF (<see cref="Slot"/>). FUEL and DMG are the one
    /// home of the fuel calls (JOKER amber, BINGO red) and the damage state (FIRE / ENG red, LEAK / STR amber, STR red past StructureWarning). Lit means "this is on" for modes, and a mode that is normally on (flight assist)
    /// lights only when it is off (FA OFF); RDR emitting is On, never amber; ACE is a steady caution while the heat is high or aces hunt the pilot (the hunt's red home is the ACE HUNT gauge). A missing system leaves its
    /// slot empty. Pure; fills a caller-owned array, no allocation.
    /// </summary>
    internal static class HudLamps
    {
        public const int Count = 8;
        /// <summary>The grid slots: R2 0..3, R3 4..7.</summary>
        public const int Fuel = 0, Dmg = 1, Ace = 2, Gear = 3, Rdr = 4, Nvg = 5, Hovr = 6, FaOff = 7;
        /// <summary>The heat (of the ace threshold) at which ACE lights amber.</summary>
        public const float AceCaution = 0.75f;
        /// <summary>The longest NVG legend the grid's column holds (NVG A, NVG 2...); a longer one reads NVG.</summary>
        public const int NvgMaxLegend = 5;

        public static void Fill(in SystemStates s, Lamp[] lamps)
        {
            // Slot 0 (2026-11-19, user: the fuel tank with its % read as a second capacitor): LASR, lit while the designator holds a target; a fuel call (JOKER amber, BINGO red flashing) takes the slot while it stands.
            lamps[Fuel] = s.Fuel.Valid && (s.Fuel.Joker || s.Fuel.Bingo)
                ? new Lamp { Legend = s.Fuel.Bingo ? "BINGO" : "JOKER", Present = true, State = s.Fuel.Bingo ? LampState.Warning : LampState.Caution, Flash = s.Fuel.Bingo }
                : Mode("LASR", s.Laser, LampState.On);
            SystemsHeadline h = SystemsMath.Headline(s.Damage, default(FuelState));
            ScopeTone tone = SystemsMath.HeadlineTone(h, s.Damage);
            lamps[Dmg] = new Lamp
            {
                Legend = DamageWord(h), Present = s.Fuel.Valid,
                State = tone == ScopeTone.Red ? LampState.Warning : tone == ScopeTone.Amber ? LampState.Caution : LampState.Dark,
                Flash = h == SystemsHeadline.Fire,
            };
            lamps[Ace] = AceLamp(s.Heat, s.AcesInbound);
            lamps[Gear] = new Lamp
            {
                Legend = "GEAR", Present = s.Gear != GearLamp.Unknown,
                State = s.Gear == GearLamp.Down ? LampState.On : s.Gear == GearLamp.Moving ? LampState.Caution : LampState.Dark,
                Flash = s.Gear == GearLamp.Moving,
            };
            lamps[Rdr] = Mode("RDR", s.RadarEmitting, LampState.On);
            lamps[Nvg] = Mode(s.NightVision == true && s.NvgLegend != null && s.NvgLegend.Length <= NvgMaxLegend ? s.NvgLegend : "NVG", s.NightVision, LampState.On);
            lamps[Hovr] = Mode("HOVR", s.AutoHover, LampState.On);
            lamps[FaOff] = Mode("FA OFF", s.FlightAssist.HasValue ? !s.FlightAssist.Value : (bool?)null, LampState.On);      // normally on: lit only when flight assist is off (C10)
        }

        /// <summary>DMG's word for the damage headline: FIRE, ENG, LEAK or STR, else DMG.</summary>
        public static string DamageWord(SystemsHeadline h) =>
            h == SystemsHeadline.Fire ? "FIRE" : h == SystemsHeadline.EngineOut ? "ENG" : h == SystemsHeadline.FuelLeak ? "LEAK" : h == SystemsHeadline.Structure ? "STR" : "DMG";

        /// <summary>Lamp <paramref name="i"/>'s place in a grid cell <paramref name="w"/> wide: its cell-local x (4 columns, <paramref name="w"/> / 4 apart) and its row (0: R2, 1: R3).</summary>
        public static void Slot(int i, float w, out float x, out int row) { x = (i % 4) * (w / 4f); row = i / 4; }

        /// <summary>ACE: absent while hunts are off (NaN heat); a steady caution from <see cref="AceCaution"/> of the threshold or while aces hunt the pilot (C9), else dark.</summary>
        public static Lamp AceLamp(float heat, bool hunting) => new Lamp
        {
            Legend = "ACE", Present = !float.IsNaN(heat),
            State = !float.IsNaN(heat) && (heat >= AceCaution || hunting) ? LampState.Caution : LampState.Dark,
        };

        /// <summary>
        /// Whether a lamp is drawn: any lamp while unlit ones are (<paramref name="showDark"/>: the Full level, or the spawn lamp test), else only one that is lit or has just changed
        /// (<paramref name="changed"/>, the "just changed" window: a lamp that went out is seen once before it leaves the row).
        /// </summary>
        public static bool Drawn(LampState state, bool changed, bool showDark) => showDark || changed || state != LampState.Dark;

        private static Lamp Mode(string legend, bool? on, LampState lit) =>
            new Lamp { Legend = legend, Present = on.HasValue, State = on == true ? lit : LampState.Dark };
    }

    /// <summary>
    /// Where a shelf's lamps go (the CM shelf's threat lamps), pure. A lamp is its word in bold 12 (a 2 unit bar under it when lit) (7.8 units a letter at the board's type boost, rounded up). A group is right-aligned in its cell's shelf: at the
    /// Full level every lamp has its slot, <see cref="SlotGap"/> apart; at Lean and Minimal only the lit ones are drawn and they pack <see cref="PackedGap"/> apart; either gap shrinks to fit, never under <see cref="MinGap"/>.
    /// </summary>
    internal static class LampLine
    {
        public const float Marker = 0f, SlotGap = 10f, PackedGap = 14f, MinGap = 4f;

        /// <summary>The width of a lamp whose word has <paramref name="letters"/> characters.</summary>
        public static float Width(int letters) => Marker + (float)Math.Ceiling(letters * 7.8f * BoscaliSummer.Core.Contracts.HudType.BoardBoost);

        /// <summary>
        /// The left edge of every lamp of a group (<paramref name="widths"/> in order) right-aligned to <paramref name="right"/>, the gap shrunk (never under <see cref="MinGap"/>) so the group fits from 0. <paramref name="slots"/>:
        /// the Full level's gap, else the packed one. Returns the group's left edge (<paramref name="right"/> for an empty group).
        /// </summary>
        public static float PlaceRight(ReadOnlySpan<float> widths, float right, bool slots, Span<float> xs)
        {
            if (widths.Length == 0) return right;
            float sum = 0f, gap = slots ? SlotGap : PackedGap;
            foreach (float w in widths) sum += w;
            int gaps = widths.Length - 1;
            if (gaps > 0 && sum + gaps * gap > right) gap = Math.Max(MinGap, (right - sum) / gaps);
            float x = right - sum - gaps * gap, left = x;
            for (int i = 0; i < widths.Length; i++) { xs[i] = x; x += widths[i] + gap; }
            return left;
        }
    }
}
