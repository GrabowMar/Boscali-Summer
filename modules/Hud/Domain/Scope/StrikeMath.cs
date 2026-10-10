using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>Where a strike track is in its life.</summary>
    internal enum StrikePhase : byte { Warn, Fall, Flight, Impact, Effect, Done }

    /// <summary>
    /// The STRIKE / OPS page's rules (pages/strike.js), pure: the phase and progress of a track from its feed numbers, the soonest track, danger
    /// close, the page layout (how tall a track is when fewer than three share the page, where the lower blocks sit) and the CYBER trace tones.
    /// The prototype's scenario times live only in the offline fixture; here everything is a function of the feed.
    /// </summary>
    internal static class StrikeMath
    {
        public const int MaxTracks = 3;
        /// <summary>How long a landing stays on its track as IMPACT / SPLASH before the track reads DONE.</summary>
        public const float Hold = 3.2f;
        /// <summary>The kinetic rod's physical fall: 20 km at 2.5 km/s (<c>RodTiming</c>); before it the rod is only warned.</summary>
        public const float RodFall = 8f;
        /// <summary>A friendly kinetic strike this close to the pilot is DANGER CLOSE.</summary>
        public const float DangerCloseM = 1500f;
        /// <summary>The ring around the target closes in over this many seconds before impact.</summary>
        public const float ConvergeSeconds = 3.2f;
        /// <summary>The trace tones: caution at 60 percent, warning at 85.</summary>
        public const float TraceCaution = 60f, TraceWarning = 85f;

        // Layout, design units (the prototype's numbers): tracks start at Top; a track is BasePitch tall when three share the page; the lower blocks (SAT, CYBER)
        // are LowerH tall and the prototype draws them from LowerRef down; they sit at the foot of the page body.
        public const float Top = 32f, BasePitch = 46f, LowerH = 100f, LowerRef = 174f, MaxPitch = 150f;
        /// <summary>The room tracks share beside the lower blocks (the prototype's 3 x 46 plus a margin).</summary>
        public const float BesideLower = 140f;

        public static bool Kinetic(StrikeKind kind) => kind == StrikeKind.Rod || kind == StrikeKind.Prsm || kind == StrikeKind.Cruise;

        public static StrikePhase Phase(in StrikeTrack t)
        {
            if (t.Eta > 0f)
                return t.Kind == StrikeKind.Rod && t.Eta > RodFall ? StrikePhase.Warn : t.Kind == StrikeKind.Rod ? StrikePhase.Fall : StrikePhase.Flight;
            float since = -t.Eta;
            if (since < Hold) return StrikePhase.Impact;
            return since < t.Linger ? StrikePhase.Effect : StrikePhase.Done;
        }

        /// <summary>In the air: not landed yet.</summary>
        public static bool InFlight(in StrikeTrack t) => t.Eta > 0f;

        /// <summary>Worth the page's attention: in the air, just landed or its effect still running.</summary>
        public static bool Live(in StrikeTrack t) => Phase(t) != StrikePhase.Done;

        /// <summary>0..1 along the flight (the rod: along its fall, nothing while only warned).</summary>
        public static float Along(in StrikeTrack t)
        {
            if (t.Eta <= 0f) return 1f;
            if (t.Kind == StrikeKind.Rod) return t.Eta >= RodFall ? 0f : 1f - t.Eta / RodFall;
            return Clamp01(1f - t.Eta / Math.Max(0.1f, t.Lead));
        }

        /// <summary>The position on the track's curve: the rod accelerates down its fall (s^1.7), the others fly evenly.</summary>
        public static float Curve(in StrikeTrack t)
        {
            float s = Along(t);
            return t.Kind == StrikeKind.Rod ? (float)Math.Pow(s, 1.7) : s;
        }

        /// <summary>The rod's warning dwell: 1 at the call, 0 at the release (the ring that counts down on the platform).</summary>
        public static float Dwell(in StrikeTrack t) => Clamp01((t.Eta - RodFall) / Math.Max(1f, t.Lead - RodFall));

        /// <summary>CRUISE: the leg now being flown (1-based) of the legs the route has (waypoints plus the last run).</summary>
        public static int Leg(in StrikeTrack t) => 1 + Math.Min(Math.Max(0, t.Legs), (int)Math.Floor(Along(t) * (t.Legs + 1)));

        public static int LegCount(in StrikeTrack t) => Math.Max(0, t.Legs) + 1;

        /// <summary>Index of the track that lands first among those still in the air, -1 when none is.</summary>
        public static int Soonest(StrikeTrack[] tracks, int count)
        {
            int best = -1;
            for (int i = 0; i < count && i < tracks.Length; i++)
                if (InFlight(tracks[i]) && (best < 0 || tracks[i].Eta < tracks[best].Eta)) best = i;
            return best;
        }

        /// <summary>A friendly kinetic strike lands within <see cref="DangerCloseM"/> of the pilot (or three times its own effect radius, if that is wider) and has not landed yet.</summary>
        public static bool Close(in StrikeTrack t) => Kinetic(t.Kind) && InFlight(t) && t.RangeM <= Math.Max(DangerCloseM, 3f * t.RadiusM);

        // ------------------------------------------------------------------------------------------------ layout

        /// <summary>The height of one track when <paramref name="n"/> share <paramref name="avail"/> units: the prototype's 46 with three, taller with fewer.</summary>
        public static float Pitch(int n, float avail) => n <= 0 ? BasePitch : Math.Max(BasePitch, Math.Min(MaxPitch, (float)Math.Floor(avail / n)));

        /// <summary>Where the stack of <paramref name="n"/> tracks of <paramref name="pitch"/> starts in <paramref name="avail"/> units of room: at the top beside the lower blocks, centred in the room when they are not shown.</summary>
        public static float Start(int n, float pitch, float avail, bool lower) => lower ? Top : Top + Math.Max(0f, (avail - n * pitch) / 2f);

        /// <summary>Top of the lower blocks in a body <paramref name="bodyH"/> tall: at its foot, or centred in the free room when no track is shown.</summary>
        public static float LowerTop(int tracks, float bodyH) => tracks > 0 ? bodyH - LowerH : Top + (bodyH - Top - LowerH) / 2f;

        /// <summary>Vertical scale of a track's drawing against the prototype's 30-unit-deep one (platform to ground line).</summary>
        public static float Depth(float pitch) => (pitch - 16f) / 30f;

        // ------------------------------------------------------------------------------------------------ tones and bars

        public static ScopeTone TraceTone(float trace) => trace >= TraceWarning ? ScopeTone.Red : trace >= TraceCaution ? ScopeTone.Amber : ScopeTone.Ink;

        /// <summary>The perk ring's fill: cooled share while cooling, full otherwise.</summary>
        public static float PerkFill(in PerkTile p) => p.State != PerkPhase.Cooling ? 1f : Clamp01(1f - p.Cooldown / Math.Max(1f, p.Total));

        /// <summary>The SAT window's share left (the countdown ring) and whether it is about to close (the last four seconds).</summary>
        public static float SatFill(float left, float total) => total <= 0f ? 0f : Clamp01(left / total);

        public static bool SatLate(float left) => left > 0f && left < 4f;

        /// <summary>A bar of a tenth-of-the-whole price scale: the allocation reads against 60 (a rod costs 30 at most).</summary>
        public const float AllocFull = 60f;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
