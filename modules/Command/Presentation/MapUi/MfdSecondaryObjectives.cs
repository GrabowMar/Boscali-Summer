using System;
using System.Globalization;
using System.Text;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>Why a zero-count tab has nothing to show; the copy names the real reason.</summary>
    internal enum BoardEmptyReason
    {
        Ready,
        Unavailable,
        LinkLost,
    }

    /// <summary>
    /// Pure copy rules for the MIS job board. Every label states only what the
    /// host actually reported.
    /// </summary>
    internal static class MfdSecondaryObjectives
    {
        public static string PlainObjective(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "OBJECTIVE";
            var text = new StringBuilder(value.Length);
            bool tag = false, space = true;
            foreach (char c in value)
            {
                if (c == '<') { tag = true; continue; }
                if (c == '>') { tag = false; continue; }
                if (tag) continue;
                if (char.IsWhiteSpace(c) || c == '_')
                { if (!space) text.Append(' '); space = true; }
                else { text.Append(c); space = false; }
            }
            return text.ToString().Trim();
        }

        /// <summary>Splits camelCase, PascalCase and acronym boundaries into readable words.</summary>
        public static string Humanize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0)
                {
                    char prev = value[i - 1];
                    if (char.IsLower(prev) && char.IsUpper(c))
                    {
                        sb.Append(' ');
                    }
                    else if (char.IsUpper(prev) && char.IsUpper(c) && i + 1 < value.Length && char.IsLower(value[i + 1]))
                    {
                        sb.Append(' ');
                    }
                    else if (char.IsLetter(prev) && char.IsDigit(c))
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string TimeLabel(bool complete, float secondsRemaining)
        {
            if (complete) return "COMPLETE";
            if (!float.IsFinite(secondsRemaining)) return "TIME UNKNOWN";
            if (secondsRemaining <= 0f) return "ENDED";
            int seconds = (int)Math.Ceiling(Math.Min(86400f, secondsRemaining));
            return "LEFT " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>"12.4 KM 087°": range and true bearing from one global X/Z to another.</summary>
        public static string RangeBearing(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX, dz = toZ - fromZ;
            if (!float.IsFinite(dx) || !float.IsFinite(dz)) return "";
            double km = Math.Sqrt(dx * (double)dx + dz * (double)dz) / 1000.0;
            int bearing = (int)Math.Round((Math.Atan2(dx, dz) * 180.0 / Math.PI + 360.0) % 360.0) % 360;
            return km.ToString(km < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " KM " + bearing.ToString("000") + "°";
        }

        /// <summary>The cockpit marker's own countdown rule: T-45s under a minute, T-5:00 above it.</summary>
        public static string Countdown(float seconds) => AvNum.TMinus(seconds);

        /// <summary>The urgency chip: the phase is named, then the clock the host reported.</summary>
        public static string ChipLabel(SecondaryObjectiveView objective)
        {
            if (objective == null) return "LINK LOST";
            if (objective.IsComplete) return "PAID";
            if (!float.IsFinite(objective.SecondsRemaining)) return "TIME UNKNOWN";
            if (objective.SecondsRemaining > 0f)
                return objective.IsOffered
                    ? TimeLabel(false, objective.SecondsRemaining).Replace("LEFT", "OFFER")
                    : Countdown(objective.SecondsRemaining);
            return objective.IsOffered ? "OFFER ENDED" : objective.IsActive ? "TIME ENDED" : "CLOSED";
        }

        /// <summary>
        /// The zero-count line. A missing director or a silent host is
        /// named for what it is; the contract-cycle copy only prints when the board is serving.
        /// </summary>
        public static string EmptyMessage(BoardEmptyReason reason)
        {
            switch (reason)
            {
                case BoardEmptyReason.Unavailable:
                    return "CONTRACT BOARD UNAVAILABLE\nDynamic operations are not running in this mission.";
                case BoardEmptyReason.LinkLost:
                    return "WAITING FOR THE HOST BOARD\nNo authoritative contract state has arrived yet.";
                default:
                    return "NO JOBS ON THE BOARD\nContracts are drawn from the live battlefield as the front moves.";
            }
        }
    }
}
