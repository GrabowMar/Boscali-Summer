using System.Collections.Generic;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The one log of the game, kept by Command: every line the game puts in its message feed (kills, captures, chat, HQ messages),
    /// what went on air on the radio, and the theater wire's breaking news. EVN LOG shows it; the news ticker reads its headlines out of it.
    /// </summary>
    internal interface ITacticalLog
    {
        /// <summary>A line that went on air on the radio: logged as RADIO, but not offered to the news ticker.</summary>
        void Radio(string text);

        /// <summary>Newest first; the mission's last 120 lines.</summary>
        IReadOnlyList<TacticalLogEntry> Entries { get; }

        /// <summary>Moves on every change, so a reader redraws only when the log moved.</summary>
        int Revision { get; }
    }

    /// <summary>
    /// One line of the log: the mission clock it came in at, its category in words, the plain text, and the ground point it happened at
    /// when the game said where (a kill's victim; LOCATE flies the map there through <see cref="IMapProjection.Focus"/>).
    /// </summary>
    internal sealed class TacticalLogEntry
    {
        public const string Air = "AIR", Ground = "GROUND", RadioTraffic = "RADIO", News = "NEWS";

        /// <summary>Every category, in the order a filter lists them.</summary>
        public static readonly string[] Categories = { Air, Ground, RadioTraffic, News };

        public readonly string Stamp, Category, Text;
        public readonly bool HasPlace;
        public readonly float X, Z;

        public TacticalLogEntry(string stamp, string category, string text, bool hasPlace, float x, float z)
        {
            Stamp = stamp ?? "";
            Category = category ?? News;
            Text = text ?? "";
            HasPlace = hasPlace;
            X = x;
            Z = z;
        }
    }
}
