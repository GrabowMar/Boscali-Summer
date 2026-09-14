using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal enum RadioBand
    {
        Fm,
        Mw
    }

    /// <summary>
    /// A dial position, kept in integer kilohertz so a station's identity and any stored
    /// preset survive a rebuild exactly. FM reads out in MHz, MW in kHz, exactly as the
    /// bands are spoken on air.
    /// </summary>
    internal readonly struct RadioDial : IEquatable<RadioDial>
    {
        public const int FmMinKilohertz = 87900;
        public const int FmMaxKilohertz = 107900;
        public const int FmStepKilohertz = 200;
        public const int MwMinKilohertz = 530;
        public const int MwMaxKilohertz = 1700;
        public const int MwStepKilohertz = 10;

        public RadioBand Band { get; }
        public int Kilohertz { get; }

        private RadioDial(RadioBand band, int kilohertz)
        {
            Band = band;
            Kilohertz = kilohertz;
        }

        public static RadioDial Fm(int kilohertz) => new RadioDial(RadioBand.Fm, kilohertz);
        public static RadioDial Mw(int kilohertz) => new RadioDial(RadioBand.Mw, kilohertz);

        public bool IsFm => Band == RadioBand.Fm;
        public string BandText => IsFm ? "FM" : "MW";
        public string UnitText => IsFm ? "MHz" : "kHz";

        public string FrequencyText => IsFm
            ? (Kilohertz / 1000f).ToString("0.0", CultureInfo.InvariantCulture)
            : Kilohertz.ToString(CultureInfo.InvariantCulture);

        public string FullText => FrequencyText + " " + UnitText;

        public float Fraction => IsFm
            ? (Kilohertz - FmMinKilohertz) / (float)(FmMaxKilohertz - FmMinKilohertz)
            : (Kilohertz - MwMinKilohertz) / (float)(MwMaxKilohertz - MwMinKilohertz);

        public bool Equals(RadioDial other) => Band == other.Band && Kilohertz == other.Kilohertz;
        public override bool Equals(object obj) => obj is RadioDial other && Equals(other);
        public override int GetHashCode() => ((int)Band * 397) ^ Kilohertz;
    }

    /// <summary>
    /// Fine and coarse dial movement, kept pure so a tuning step is testable without the
    /// receiver. Stations are exactly on the grid, so a fine step lands on or between them.
    /// </summary>
    internal static class RadioDialTuning
    {
        public static RadioDial Step(RadioDial dial, int direction)
        {
            if (direction == 0) return dial;
            int step = dial.IsFm ? RadioDial.FmStepKilohertz : RadioDial.MwStepKilohertz;
            int min = dial.IsFm ? RadioDial.FmMinKilohertz : RadioDial.MwMinKilohertz;
            int max = dial.IsFm ? RadioDial.FmMaxKilohertz : RadioDial.MwMaxKilohertz;
            int khz = Math.Min(max, Math.Max(min, dial.Kilohertz + direction * step));
            return dial.IsFm ? RadioDial.Fm(khz) : RadioDial.Mw(khz);
        }

        public static int IndexAt(IReadOnlyList<RadioDial> dials, RadioDial dial)
        {
            if (dials == null) return -1;
            for (int i = 0; i < dials.Count; i++)
                if (dials[i].Equals(dial)) return i;
            return -1;
        }

        public static int Seek(IReadOnlyList<RadioDial> dials, RadioDial from, int direction)
        {
            if (dials == null || dials.Count == 0 || direction == 0) return -1;
            int found = -1;
            int foundKhz = 0;
            for (int i = 0; i < dials.Count; i++)
            {
                if (dials[i].Band != from.Band) continue;
                int khz = dials[i].Kilohertz;
                bool beyond = direction > 0 ? khz > from.Kilohertz : khz < from.Kilohertz;
                if (!beyond) continue;
                if (found < 0 || (direction > 0 ? khz < foundKhz : khz > foundKhz))
                {
                    found = i;
                    foundKhz = khz;
                }
            }
            if (found >= 0) return found;

            // Nothing beyond the dial: wrap to the band's other end.
            for (int i = 0; i < dials.Count; i++)
            {
                if (dials[i].Band != from.Band) continue;
                int khz = dials[i].Kilohertz;
                if (found < 0 || (direction > 0 ? khz < foundKhz : khz > foundKhz))
                {
                    found = i;
                    foundKhz = khz;
                }
            }
            return found;
        }

        public static int FirstInBand(IReadOnlyList<RadioDial> dials, RadioBand band)
        {
            int found = -1;
            int foundKhz = 0;
            for (int i = 0; dials != null && i < dials.Count; i++)
            {
                if (dials[i].Band != band) continue;
                if (found < 0 || dials[i].Kilohertz < foundKhz)
                {
                    found = i;
                    foundKhz = dials[i].Kilohertz;
                }
            }
            return found;
        }
    }

    /// <summary>
    /// Assigns dial positions. The three built-in stations keep canonical frequencies an
    /// operator would recognise; every other station gets a stable slot derived from its
    /// name, so a preset stored today still lands on the same station tomorrow.
    /// </summary>
    internal static class RadioDialAllocation
    {
        public const int FmSlotCount =
            (RadioDial.FmMaxKilohertz - RadioDial.FmMinKilohertz) / RadioDial.FmStepKilohertz + 1;

        public static bool TryBuiltIn(string stationId, out RadioDial dial)
        {
            switch (stationId)
            {
                case BuiltInStationRules.AgrapolId:
                    dial = RadioDial.Fm(88500);
                    return true;
                case BuiltInStationRules.MarisId:
                    dial = RadioDial.Fm(101900);
                    return true;
                case BuiltInStationRules.BaseId:
                    dial = RadioDial.Mw(780);
                    return true;
                default:
                    dial = default;
                    return false;
            }
        }

        /// <summary>
        /// A deterministic FM slot for a user station. Collisions probe forward, bounded by
        /// the number of slots; the catalogue ceiling is far below that, so the probe always
        /// terminates inside the band.
        /// </summary>
        public static RadioDial Allocate(string name, HashSet<int> usedSlots)
        {
            if (usedSlots == null) usedSlots = new HashSet<int>();
            int start = SlotForName(name ?? string.Empty);
            for (int probe = 0; probe < FmSlotCount; probe++)
            {
                int slot = (start + probe) % FmSlotCount;
                if (!usedSlots.Add(slot))
                    continue;
                return RadioDial.Fm(RadioDial.FmMinKilohertz + slot * RadioDial.FmStepKilohertz);
            }
            return RadioDial.Fm(RadioDial.FmMinKilohertz);
        }

        public static bool TryFmSlot(RadioDial dial, out int slot)
        {
            if (dial.IsFm && dial.Kilohertz >= RadioDial.FmMinKilohertz &&
                dial.Kilohertz <= RadioDial.FmMaxKilohertz &&
                (dial.Kilohertz - RadioDial.FmMinKilohertz) % RadioDial.FmStepKilohertz == 0)
            {
                slot = (dial.Kilohertz - RadioDial.FmMinKilohertz) / RadioDial.FmStepKilohertz;
                return true;
            }
            slot = -1;
            return false;
        }

        private static int SlotForName(string name)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < name.Length; i++)
            {
                hash ^= char.ToLowerInvariant(name[i]);
                hash *= 16777619u;
            }
            return (int)(hash % FmSlotCount);
        }
    }
}
