using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Burn rate, endurance and BINGO / JOKER for the board's fuel row (2026-10-10), pure. The rule is the Wing module's
    /// <c>BingoMonitor</c> exactly (modules may not import each other, so it is mirrored; the parity test in
    /// tests/BoscaliSummer.Tests drives both with the same sequences and asserts the same flags): the burn rate is learned in
    /// flight as a low-passed fraction of the tanks per second (not while in afterburner), and BINGO trips when the fuel left
    /// no longer covers the flight to the nearest friendly field at cruise speed plus a reserve; JOKER is the margin before it.
    /// With no field known the rate is still learned and endurance still shows, but neither flag can be set.
    /// </summary>
    internal sealed class FuelEndurance
    {
        // Mirrored from the Wing module's BingoMonitor; change them together.
        public const float RateTau = 60f, ReserveFraction = 0.1f, ClearMargin = 0.05f, MinSpeed = 50f, JokerMargin = 0.15f;

        /// <summary>The fuel line cautions at or below this level, and warns at or below <see cref="WarningLevel"/>.</summary>
        public const float CautionLevel = 0.25f, WarningLevel = 0.10f;

        private float lastFuel = float.NaN, rate = float.NaN, need;

        public bool Bingo { get; private set; }
        public bool Joker { get; private set; }
        /// <summary>True on the update JOKER is first reached (not when the fuel went straight past it to BINGO).</summary>
        public bool JokerNow { get; private set; }

        /// <summary>The last fuel level fed in (0..1), NaN before the first update.</summary>
        public float Level => lastFuel;

        /// <summary>Fraction of the tanks burned per second, low-passed; NaN until a rate has been learned.</summary>
        public float RatePerSecond => rate;

        /// <summary>Seconds until the tanks are dry at the learned rate; +infinity before a rate is learned or when none is burning.</summary>
        public float EnduranceSeconds => float.IsNaN(rate) || rate <= 0f || float.IsNaN(lastFuel) ? float.PositiveInfinity : lastFuel / rate;

        /// <summary>Seconds until BINGO at the learned rate (+infinity when unknown).</summary>
        public float SecondsToBingo => float.IsNaN(rate) || rate <= 0f ? float.PositiveInfinity : (lastFuel - need) / rate;

        /// <summary>
        /// Feed one reading. <paramref name="distanceToField"/> is metres to the nearest friendly field (NaN: none known),
        /// <paramref name="cruiseSpeed"/> the speed the flight home is judged at, <paramref name="afterburner"/> keeps a
        /// burst of afterburner from skewing the learned rate. Returns true on the update BINGO is first reached.
        /// </summary>
        public bool Update(float fuel, float distanceToField, float cruiseSpeed, float dt, bool afterburner = false)
        {
            JokerNow = false;
            if (!float.IsNaN(lastFuel) && dt > 0f && !afterburner)
            {
                float instant = Math.Max(0f, (lastFuel - fuel) / dt);
                rate = float.IsNaN(rate) ? instant : rate + (instant - rate) * Math.Min(1f, dt / RateTau);
            }
            lastFuel = fuel;
            if (float.IsNaN(distanceToField))
            {
                Bingo = Joker = false;
                return false;
            }
            if (float.IsNaN(rate) || rate <= 0f) return false;
            float needed = rate * distanceToField / Math.Max(cruiseSpeed, MinSpeed) + ReserveFraction;
            need = needed;
            if (Joker && fuel > needed + JokerMargin + ClearMargin) Joker = false;
            if (!Joker && fuel < needed + JokerMargin)
            {
                Joker = true;
                JokerNow = fuel >= needed;
            }
            if (Bingo && fuel > needed + ClearMargin) Bingo = false;
            if (Bingo || fuel >= needed) return false;
            Bingo = true;
            return true;
        }

        /// <summary>A new aircraft: forget the learned rate and both flags.</summary>
        public void Reset()
        {
            lastFuel = rate = float.NaN;
            need = 0f;
            Bingo = Joker = JokerNow = false;
        }

        /// <summary>How loud the fuel state is: BINGO or a nearly empty tank warns; JOKER or a low tank cautions.</summary>
        public static HudTone Tone(float level, bool bingo, bool joker)
        {
            if (bingo || (float.IsFinite(level) && level <= WarningLevel)) return HudTone.Warning;
            if (joker || (float.IsFinite(level) && level <= CautionLevel)) return HudTone.Caution;
            return HudTone.Info;
        }
    }

    /// <summary>The fuel row's data for one tick (<see cref="Valid"/> false: no aircraft, no row).</summary>
    internal struct FuelState
    {
        public bool Valid;
        /// <summary>Live fuel, 0..1.</summary>
        public float Level;
        /// <summary>Seconds until the tanks are dry at the learned burn rate (+infinity when unknown).</summary>
        public float EnduranceS;
        public bool Bingo, Joker;
        /// <summary>The nearest friendly field and the metres to it; null / NaN when none is known.</summary>
        public string Field;
        public float FieldM;
        public HudTone Tone;
    }

    /// <summary>The words of the fuel row, pure.</summary>
    internal static class FuelText
    {
        private static readonly char[] Scratch = new char[48];

        /// <summary>"42MIN", "1H05", "&lt;1MIN"; "--" when the endurance is unknown.</summary>
        public static string Endurance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "--";
            int minutes = (int)(seconds / 60f);
            if (minutes < 1) return "<1MIN";
            if (minutes < 100) return minutes + "MIN";
            return (minutes / 60) + "H" + (minutes % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>"38%  END 42MIN": the level and what it buys.</summary>
        public static string Level(float level, float enduranceSeconds) =>
            AvNum.PercentWhole(level) + "  END " + Endurance(enduranceSeconds);

        /// <summary>"RTB HIGHWAYSTRIP2 23KM"; "RTB --" when no field is known.</summary>
        public static string Home(string field, float distanceM, AvUnits units)
        {
            if (string.IsNullOrEmpty(field) || float.IsNaN(distanceM)) return "RTB --";
            string name = field.Trim().ToUpperInvariant();
            if (name.Length > 16) name = name.Substring(0, 15) + "…";
            char[] b = Scratch;
            int len = AvNumFormat.Append(b, 0, "RTB ");
            len = AvNumFormat.Append(b, len, name);
            len = AvNumFormat.Append(b, len, " ");
            len = AvUnitTable.DistanceReading(b, len, distanceM, units);
            return new string(b, 0, len).ToUpperInvariant();
        }

        public static string State(bool bingo, bool joker) => bingo ? "BINGO" : joker ? "JOKER" : "";
    }
}
