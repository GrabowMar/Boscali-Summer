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
        public bool? FlightAssist, AutoHover, RadarEmitting, NightVision, WeaponSafe, EngineRunning;
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
    /// The always-on annunciator row of the status board (2026-10-07): a real cockpit-style lamp strip of the
    /// aircraft's own switches, read-only. Lit means "this is on" for modes (GEAR down, FA, HOVR, NVG), caution for
    /// things that give you away or stop you shooting (RDR emitting, SAFE), warning for an engine that is not running.
    /// A missing system leaves its lamp out. Pure; fills a caller-owned array, no allocation.
    /// </summary>
    internal static class HudLamps
    {
        public const int Count = 7;

        public static void Fill(in SystemStates s, Lamp[] lamps)
        {
            lamps[0] = new Lamp
            {
                Legend = "GEAR", Present = s.Gear != GearLamp.Unknown,
                State = s.Gear == GearLamp.Down ? LampState.On : s.Gear == GearLamp.Moving ? LampState.Caution : LampState.Dark,
                Flash = s.Gear == GearLamp.Moving,
            };
            lamps[1] = Mode("FA", s.FlightAssist, LampState.On);
            lamps[2] = Mode("HOVR", s.AutoHover, LampState.On);
            lamps[3] = Mode("RDR", s.RadarEmitting, LampState.Caution);
            lamps[4] = Mode("NVG", s.NightVision, LampState.On);
            lamps[5] = Mode("SAFE", s.WeaponSafe, LampState.Caution);
            lamps[6] = new Lamp
            {
                Legend = "ENG", Present = s.EngineRunning.HasValue,
                State = s.EngineRunning == false ? LampState.Warning : LampState.Dark,
            };
        }

        private static Lamp Mode(string legend, bool? on, LampState lit) =>
            new Lamp { Legend = legend, Present = on.HasValue, State = on == true ? lit : LampState.Dark };
    }
}
