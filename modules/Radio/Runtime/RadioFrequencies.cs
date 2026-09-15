using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal enum RadioBand
    {
        Fm,
        Air,
        Mw
    }

    internal enum RadioModulation
    {
        Fm,
        Am
    }

    /// <summary>
    /// The three bands the receiver covers, and the only place their edges, increments and
    /// modulation are written down. FM is wideband stereo broadcast, VHF AIR is the AM
    /// aviation band (25 kHz channels, three-decimal megahertz), MW is medium-wave AM.
    /// </summary>
    internal static class RadioBands
    {
        public const int FmMinKilohertz = 87500;
        public const int FmMaxKilohertz = 108000;
        public const int FmStepKilohertz = 100;
        public const int AirMinKilohertz = 118000;
        public const int AirMaxKilohertz = 136975;
        public const int AirStepKilohertz = 25;
        public const int MwMinKilohertz = 530;
        public const int MwMaxKilohertz = 1700;
        public const int MwStepKilohertz = 10;

        public static readonly RadioBand[] All = { RadioBand.Fm, RadioBand.Air, RadioBand.Mw };

        public static int Min(RadioBand band) => band == RadioBand.Fm
            ? FmMinKilohertz
            : band == RadioBand.Air ? AirMinKilohertz : MwMinKilohertz;

        public static int Max(RadioBand band) => band == RadioBand.Fm
            ? FmMaxKilohertz
            : band == RadioBand.Air ? AirMaxKilohertz : MwMaxKilohertz;

        public static int Step(RadioBand band) => band == RadioBand.Fm
            ? FmStepKilohertz
            : band == RadioBand.Air ? AirStepKilohertz : MwStepKilohertz;

        public static RadioModulation Modulation(RadioBand band) =>
            band == RadioBand.Fm ? RadioModulation.Fm : RadioModulation.Am;

        public static bool IsFm(RadioBand band) => Modulation(band) == RadioModulation.Fm;

        public static string BandText(RadioBand band) => band == RadioBand.Fm
            ? "FM"
            : band == RadioBand.Air ? "VHF" : "MW";

        public static string UnitText(RadioBand band) => band == RadioBand.Mw ? "kHz" : "MHz";

        public static string ModulationText(RadioBand band, RadioModulation mode) =>
            mode == RadioModulation.Fm ? "FM" : "AM";

        public static RadioBand Next(RadioBand band)
        {
            int index = Array.IndexOf(All, band);
            return All[(index + 1 + All.Length) % All.Length];
        }

        public static string Format(RadioBand band, int kilohertz)
        {
            if (band == RadioBand.Mw)
                return kilohertz.ToString(CultureInfo.InvariantCulture);
            return band == RadioBand.Air
                ? (kilohertz / 1000f).ToString("0.000", CultureInfo.InvariantCulture)
                : (kilohertz / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A dial position, kept in integer kilohertz so a station's identity survives a rebuild
    /// exactly. FM and VHF read out in MHz, MW in kHz, exactly as the bands are spoken on air.
    /// </summary>
    internal readonly struct RadioDial : IEquatable<RadioDial>
    {
        public const int FmMinKilohertz = RadioBands.FmMinKilohertz;
        public const int FmMaxKilohertz = RadioBands.FmMaxKilohertz;
        public const int FmStepKilohertz = RadioBands.FmStepKilohertz;
        public const int MwMinKilohertz = RadioBands.MwMinKilohertz;
        public const int MwMaxKilohertz = RadioBands.MwMaxKilohertz;
        public const int MwStepKilohertz = RadioBands.MwStepKilohertz;

        public RadioBand Band { get; }
        public int Kilohertz { get; }

        private RadioDial(RadioBand band, int kilohertz)
        {
            Band = band;
            Kilohertz = kilohertz;
        }

        public static RadioDial Fm(int kilohertz) => new RadioDial(RadioBand.Fm, kilohertz);
        public static RadioDial Mw(int kilohertz) => new RadioDial(RadioBand.Mw, kilohertz);
        public static RadioDial Air(int kilohertz) => new RadioDial(RadioBand.Air, kilohertz);

        public static RadioDial At(RadioBand band, int kilohertz) =>
            new RadioDial(band, kilohertz);

        public bool IsFm => RadioBands.IsFm(Band);
        public RadioModulation Modulation => RadioBands.Modulation(Band);
        public string BandText => RadioBands.BandText(Band);
        public string UnitText => RadioBands.UnitText(Band);

        public string FrequencyText => RadioBands.Format(Band, Kilohertz);

        public string FullText => FrequencyText + " " + UnitText;

        public float Fraction
        {
            get
            {
                int min = RadioBands.Min(Band);
                int max = RadioBands.Max(Band);
                return max <= min ? 0f : (Kilohertz - min) / (float)(max - min);
            }
        }

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
        public static RadioDial Step(RadioDial dial, int direction) =>
            Step(dial, direction, 1);

        public static RadioDial Step(RadioDial dial, int direction, int increments)
        {
            if (direction == 0) return dial;
            int step = RadioBands.Step(dial.Band) * Math.Max(1, increments);
            int min = RadioBands.Min(dial.Band);
            int max = RadioBands.Max(dial.Band);
            int khz = Math.Min(max, Math.Max(min, dial.Kilohertz + direction * step));
            return RadioDial.At(dial.Band, khz);
        }

        /// <summary>A reduced increment for the FINE knob: each band keeps its own grid divisor.</summary>
        public static RadioDial FineStep(RadioDial dial, int direction, int divisor)
        {
            if (direction == 0) return dial;
            int step = Math.Max(1, RadioBands.Step(dial.Band) / Math.Max(1, divisor));
            int min = RadioBands.Min(dial.Band);
            int max = RadioBands.Max(dial.Band);
            int khz = Math.Min(max, Math.Max(min, dial.Kilohertz + direction * step));
            return RadioDial.At(dial.Band, khz);
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
    /// name, so the same folder always lands on the same frequency.
    /// </summary>
    internal static class RadioDialAllocation
    {
        public const int FmSlotCount =
            (RadioBands.FmMaxKilohertz - RadioBands.FmMinKilohertz) / RadioBands.FmStepKilohertz + 1;

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
                return RadioDial.Fm(RadioBands.FmMinKilohertz + slot * RadioBands.FmStepKilohertz);
            }
            return RadioDial.Fm(RadioBands.FmMinKilohertz);
        }

        public static bool TryFmSlot(RadioDial dial, out int slot)
        {
            if (dial.Band == RadioBand.Fm && dial.Kilohertz >= RadioBands.FmMinKilohertz &&
                dial.Kilohertz <= RadioBands.FmMaxKilohertz &&
                (dial.Kilohertz - RadioBands.FmMinKilohertz) % RadioBands.FmStepKilohertz == 0)
            {
                slot = (dial.Kilohertz - RadioBands.FmMinKilohertz) / RadioBands.FmStepKilohertz;
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
