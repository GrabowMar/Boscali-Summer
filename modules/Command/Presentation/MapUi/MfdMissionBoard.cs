using System;
using System.Collections.Generic;
using NOAvionics;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>Lifecycle phase of a mission objective or contract, as the MIS log reads it.</summary>
    internal enum MissionPhase { Offered, Active, Done, Closed }

    /// <summary>
    /// Pure copy rules for the MIS briefing board: distances, ranks and the bounded, session-local
    /// mission log. No game types, so the whole surface is checkable offline.
    /// </summary>
    internal static class MfdMissionBoard
    {
        /// <summary>The log keeps this many entries; the oldest is overwritten first.</summary>
        public const int LogCapacity = 16;

        /// <summary>The BRIEF page shows only the newest few; the rest are history.</summary>
        public const int LogShown = 5;

        /// <summary>Metres to a short mono readout. An unknown range is a dash, never a confident zero.</summary>
        public static string Distance(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f) return "—";
            if (meters < 1000f) return AvNum.Fixed(Math.Round(meters / 10f) * 10f, 0) + " M";
            return AvNum.Fixed(meters / 1000f, meters < 100000f ? 1 : 0) + " KM";
        }

        /// <summary>Short mono threshold under a ladder rung; an unset gate never reads as a zero.</summary>
        public static string RungThreshold(int rung, float tactical, float strategic)
        {
            if (rung <= 0) return "BASELINE";
            float value = rung == 1 ? tactical : strategic;
            return value > 0f ? MfdMissionOverview.Whole(value) : "UNSET";
        }

        /// <summary>Fraction of the ladder track that is lit: stage rungs cleared plus progress toward the next gate.</summary>
        public static float LadderFill(int stage, float nextGateProgress)
        {
            if (stage >= 2) return 1f;
            float fill = (stage + Math.Max(0f, Math.Min(1f, nextGateProgress))) / 2f;
            return Math.Max(0f, Math.Min(1f, fill));
        }
    }

    /// <summary>A bounded ring of short event lines, newest first when read.</summary>
    internal sealed class MissionLog
    {
        internal struct Entry
        {
            public string Text;
            public string Stamp;
            public AvState State;
        }

        private readonly Entry[] ring = new Entry[MfdMissionBoard.LogCapacity];
        private int next;

        public int Count { get; private set; }

        public void Add(string text, string stamp, AvState state)
        {
            ring[next] = new Entry { Text = text ?? "", Stamp = stamp ?? "", State = state };
            next = (next + 1) % ring.Length;
            if (Count < ring.Length) Count++;
        }

        /// <summary>0 is the newest entry.</summary>
        public Entry Newest(int index)
        {
            if (index < 0 || index >= Count) return default(Entry);
            return ring[((next - 1 - index) % ring.Length + ring.Length) % ring.Length];
        }

        public void Clear()
        {
            Array.Clear(ring, 0, ring.Length);
            next = 0;
            Count = 0;
        }
    }

    /// <summary>
    /// Turns snapshots of objectives and contracts into log lines. The first snapshot is the
    /// baseline: only changes seen after it are logged, so opening the panel never replays history.
    /// </summary>
    internal sealed class MissionEventTracker
    {
        private const int MaxTracked = 64;

        private sealed class Seen
        {
            public MissionPhase Phase;
            public string Title, Kind;
            public bool Touched;
        }

        private readonly Dictionary<string, Seen> known = new Dictionary<string, Seen>(32);
        private readonly List<string> gone = new List<string>(8);
        private bool baseline;

        public void Reset()
        {
            known.Clear();
            gone.Clear();
            baseline = false;
        }

        public void Begin()
        {
            foreach (Seen seen in known.Values) seen.Touched = false;
        }

        public void Observe(MissionLog log, string stamp, string kind, string key, string title, MissionPhase phase)
        {
            if (log == null || string.IsNullOrEmpty(key)) return;
            title = string.IsNullOrEmpty(title) ? kind : title;
            if (!known.TryGetValue(key, out Seen seen))
            {
                if (known.Count >= MaxTracked) return;
                known.Add(key, new Seen { Phase = phase, Title = title, Kind = kind, Touched = true });
                if (baseline)
                    log.Add(kind + (phase == MissionPhase.Offered ? " OFFERED" : phase == MissionPhase.Active ? " ISSUED"
                        : phase == MissionPhase.Done ? " COMPLETE" : " CLOSED") + "  ·  " + title,
                        stamp, phase == MissionPhase.Done ? AvState.Ready : AvState.Info);
                return;
            }
            seen.Touched = true;
            seen.Title = title;
            if (seen.Phase == phase) return;
            MissionPhase before = seen.Phase;
            seen.Phase = phase;
            if (!baseline) return;
            switch (phase)
            {
                case MissionPhase.Active:
                    log.Add(kind + (before == MissionPhase.Offered ? " ACCEPTED" : " ACTIVE") + "  ·  " + title, stamp, AvState.Info);
                    break;
                case MissionPhase.Done:
                    log.Add(kind + " COMPLETE  ·  " + title, stamp, AvState.Ready);
                    break;
                case MissionPhase.Closed:
                    log.Add(kind + " CLOSED  ·  " + title, stamp, AvState.Caution);
                    break;
            }
        }

        /// <summary>Finish a snapshot. An item that vanished without ever reading Done/Closed was resolved elsewhere.</summary>
        public void End(MissionLog log, string stamp)
        {
            gone.Clear();
            foreach (KeyValuePair<string, Seen> pair in known)
                if (!pair.Value.Touched) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                Seen seen = known[gone[i]];
                if (baseline && log != null && seen.Phase != MissionPhase.Done && seen.Phase != MissionPhase.Closed)
                    log.Add(seen.Kind + " RESOLVED  ·  " + seen.Title, stamp, AvState.Info);
                known.Remove(gone[i]);
            }
            baseline = true;
        }
    }
}
