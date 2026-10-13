using System;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// What the NAV DATA block is told each tick (the cockpit view's bottom-right corner): the way to the nearest friendly field, the selected target, the fuel's endurance and the mission clock. A part with no data
    /// is left at its default and its row does not exist. Filled by <c>NavReader</c>; pure so the rules below are tested without Unity.
    /// </summary>
    internal struct NavInput
    {
        /// <summary>The unit system distances and speeds are written in (the game's own setting).</summary>
        public AvUnits Units;
        /// <summary>The nearest friendly field: its name (upper case; may be empty), the compass bearing to it (degrees clockwise from north) and the metres to it.</summary>
        public bool HasHome;
        public string HomeName;
        public float HomeBearingDeg, HomeM;
        /// <summary>The first selected target: its name, the compass bearing to it, the metres to it and the closing speed (positive = closing; NaN until two reads of the same target have been made).</summary>
        public bool HasTarget;
        public string TargetName;
        public float TargetBearingDeg, TargetM, ClosureMps;
        /// <summary>The fuel row's data: seconds to dry tanks at the learned burn rate (infinity or NaN: unknown) and the BINGO / JOKER flags.</summary>
        public bool HasFuel;
        public float EnduranceS;
        public bool Bingo, Joker;
        /// <summary>The mission clock in seconds (not finite or negative: no clock).</summary>
        public bool HasClock;
        public float ClockS;
    }

    internal enum NavRowKind : byte { Home, Target, Closure, Fuel, Clock }

    /// <summary>One row of the block as words: a DIM label, the value in <see cref="Tone"/> (its characters are <see cref="Value"/>[0..<see cref="Length"/>)) and, for the fuel row, a flag word in bold amber.</summary>
    internal sealed class NavRow
    {
        public NavRowKind Kind;
        public string Label;
        public readonly char[] Value = new char[48];
        public int Length;
        /// <summary>BINGO or JOKER after the value (bold AMBER), else null.</summary>
        public string Flag;
        public ScopeTone Tone;

        /// <summary>The characters the row takes: label, a space, the value and, with a flag, a space and the flag.</summary>
        public int Chars => Label.Length + 1 + Length + (Flag != null ? 1 + Flag.Length : 0);
    }

    /// <summary>
    /// The NAV DATA block's rows and the words in them (HUD holo pass C), pure. A row exists only while it has data: HOME (<c>name bearing / distance</c>), TGT (<c>name bearing / range</c>), CLS (the target's closure),
    /// FUEL (endurance, and BINGO or JOKER while the fuel watch says so), CLOCK (mission time). Every row fits <see cref="MaxChars"/> characters (the 260 unit block at the board's 12 unit mono face); a name that
    /// would not fit loses whole words from its end and, if one word alone is too long, characters, never dots. The words are written into the rows' own buffers: composing allocates nothing.
    /// </summary>
    internal sealed class NavBlock
    {
        /// <summary>The block is this wide (design units); the 12 unit mono face advances 7.8 a character, so a row holds 33.</summary>
        public const float Width = 260f;
        public const int MaxChars = 33;

        public readonly NavRow[] Rows = { new NavRow(), new NavRow(), new NavRow(), new NavRow(), new NavRow() };
        private readonly char[] tail = new char[32];
        private int count;

        /// <summary>How many rows of <see cref="Rows"/> are live.</summary>
        public int Count => count;

        /// <summary>Read this tick's rows out of <paramref name="input"/>; returns how many rows exist (0: nothing to show, the block is not drawn).</summary>
        public int Compose(in NavInput input)
        {
            count = 0;
            if (input.HasHome && float.IsFinite(input.HomeM) && float.IsFinite(input.HomeBearingDeg))
                Place(NavRowKind.Home, "HOME", input.HomeName, input.HomeBearingDeg, input.HomeM, input.Units, ScopeTone.Ink);
            if (input.HasTarget && float.IsFinite(input.TargetM) && float.IsFinite(input.TargetBearingDeg))
            {
                Place(NavRowKind.Target, "TGT", input.TargetName, input.TargetBearingDeg, input.TargetM, input.Units, ScopeTone.Hi);
                if (float.IsFinite(input.ClosureMps)) Closure(input.ClosureMps, input.Units);
            }
            bool endurance = HasEndurance(input.EnduranceS);
            if (input.HasFuel && (endurance || input.Bingo || input.Joker))
            {
                NavRow row = Next(NavRowKind.Fuel, "FUEL", ScopeTone.Ink);
                row.Length = WriteEndurance(row.Value, 0, input.EnduranceS);
                row.Flag = input.Bingo ? "BINGO" : input.Joker ? "JOKER" : null;
            }
            if (input.HasClock && float.IsFinite(input.ClockS) && input.ClockS >= 0f)
            {
                NavRow row = Next(NavRowKind.Clock, "CLOCK", ScopeTone.Ink);
                row.Length = WriteClock(row.Value, 0, input.ClockS);
            }
            return count;
        }

        private NavRow Next(NavRowKind kind, string label, ScopeTone tone)
        {
            NavRow row = Rows[count++];
            row.Kind = kind;
            row.Label = label;
            row.Tone = tone;
            row.Length = 0;
            row.Flag = null;
            return row;
        }

        /// <summary>HOME / TGT: <c>NAME 097 / 23KM</c>, the name fitted to what the bearing and the distance leave.</summary>
        private void Place(NavRowKind kind, string label, string name, float bearingDeg, float metres, AvUnits units, ScopeTone tone)
        {
            int t = WriteBearing(tail, 0, bearingDeg);
            t = AvNumFormat.Append(tail, t, " / ");
            t = WriteDistance(tail, t, metres, units);
            NavRow row = Next(kind, label, tone);
            int room = MaxChars - label.Length - 1 - t - 1, at = 0;
            if (room > 0 && !string.IsNullOrEmpty(name))
            {
                at = FitWords(name, room, row.Value, 0);
                if (at > 0) row.Value[at++] = ' ';
            }
            Array.Copy(tail, 0, row.Value, at, t);
            row.Length = at + t;
        }

        /// <summary>CLS: <c>CLOSING 1020KM/H</c>, <c>OPENING 300KM/H</c>, or <c>STEADY</c> under 5 m/s either way (the third-person target card's own words).</summary>
        private void Closure(float mps, AvUnits units)
        {
            NavRow row = Next(NavRowKind.Closure, "CLS", ScopeTone.Ink);
            if (Math.Abs(mps) < 5f) { row.Length = AvNumFormat.Append(row.Value, 0, "STEADY"); return; }
            int at = AvNumFormat.Append(row.Value, 0, mps > 0f ? "CLOSING " : "OPENING ");
            int from = at;
            at = AvUnitTable.SpeedReading(row.Value, at, Math.Abs(mps), units);
            Upper(row.Value, from, at);
            row.Length = at;
        }

        // ---------------------------------------------------------------------------------------------------- the words

        /// <summary>A compass bearing as a whole number 0..359: rounded half away from zero and wrapped (360 reads 0, -1 reads 359).</summary>
        public static int Bearing(float deg)
        {
            int r = (int)MathF.Round(deg, MidpointRounding.AwayFromZero) % 360;
            return r < 0 ? r + 360 : r;
        }

        /// <summary>The bearing as three digits ("007", "097", "359").</summary>
        public static int WriteBearing(char[] b, int at, float deg)
        {
            int r = Bearing(deg);
            b[at] = (char)('0' + r / 100);
            b[at + 1] = (char)('0' + r / 10 % 10);
            b[at + 2] = (char)('0' + r % 10);
            return at + 3;
        }

        /// <summary>A distance in the player's units, upper case ("900M", "1.5KM", "23KM", "547YD", "10.8NM"): the game's own breakpoints (<see cref="AvUnitTable.DistanceReading"/>).</summary>
        public static int WriteDistance(char[] b, int at, float metres, AvUnits units)
        {
            int end = AvUnitTable.DistanceReading(b, at, metres, units);
            Upper(b, at, end);
            return end;
        }

        /// <summary>The endurance is a number worth showing: finite, not negative and under the 100 hours past which a parked aircraft's decayed burn rate means nothing.</summary>
        public static bool HasEndurance(float seconds) => !(float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds >= 360000f);

        /// <summary>"42MIN", "1H05", "&lt;1MIN"; "--" when unknown: the words of <see cref="FuelText.Endurance"/>, written without building a string.</summary>
        public static int WriteEndurance(char[] b, int at, float seconds)
        {
            if (!HasEndurance(seconds)) return AvNumFormat.Unknown(b, at);
            int minutes = (int)(seconds / 60f);
            if (minutes < 1) return AvNumFormat.Append(b, at, "<1MIN");
            if (minutes < 100)
            {
                at = AvNumFormat.Write(b, at, minutes, 0);
                return AvNumFormat.Append(b, at, "MIN");
            }
            at = AvNumFormat.Write(b, at, minutes / 60, 0);
            b[at++] = 'H';
            b[at++] = (char)('0' + minutes % 60 / 10);
            b[at++] = (char)('0' + minutes % 10);
            return at;
        }

        /// <summary>The mission clock as hours, minutes and seconds ("0:12:34", "1:02:05"); the hours are not capped.</summary>
        public static int WriteClock(char[] b, int at, float seconds)
        {
            long s = (long)Math.Floor(seconds);
            long h = s / 3600, m = s / 60 % 60, sec = s % 60;
            at = AvNumFormat.Write(b, at, h, 0);
            b[at++] = ':';
            b[at++] = (char)('0' + m / 10);
            b[at++] = (char)('0' + m % 10);
            b[at++] = ':';
            b[at++] = (char)('0' + sec / 10);
            b[at++] = (char)('0' + sec % 10);
            return at;
        }

        /// <summary>
        /// The longest run of whole words of <paramref name="name"/> (single spaces between) that fits <paramref name="room"/> characters, written at <paramref name="at"/>. When even the first word is longer than the
        /// room it is cut to the room: whole words are dropped from the end, never an ellipsis. Returns the index after the last character written (<paramref name="at"/> when nothing fits).
        /// </summary>
        public static int FitWords(string name, int room, char[] b, int at)
        {
            if (string.IsNullOrWhiteSpace(name) || room <= 0) return at;
            int used = 0, i = 0;
            while (i < name.Length)
            {
                while (i < name.Length && name[i] == ' ') i++;
                int j = i;
                while (j < name.Length && name[j] != ' ') j++;
                if (j == i) break;
                int need = (used > 0 ? 1 : 0) + (j - i);
                if (used + need > room)
                {
                    if (used == 0)      // the first word alone is too long: its first characters, no dots
                    {
                        int cut = Math.Min(room, j - i);
                        name.CopyTo(i, b, at, cut);
                        at += cut;
                    }
                    return at;
                }
                if (used > 0) b[at++] = ' ';
                name.CopyTo(i, b, at, j - i);
                at += j - i;
                used += need;
                i = j;
            }
            return at;
        }

        private static void Upper(char[] b, int from, int to)
        {
            for (int i = from; i < to; i++)
                if (b[i] >= 'a' && b[i] <= 'z') b[i] = (char)(b[i] - 32);
        }
    }
}
