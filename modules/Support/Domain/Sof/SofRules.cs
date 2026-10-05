using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>Team lifecycle. Wire values (3 bits); never renumber.</summary>
    internal enum TeamState : byte { Raising = 0, Ready = 1, Moving = 2, OnSite = 3, Pinned = 4, Returning = 5, Recovering = 6, Lost = 7 }

    /// <summary>The five SOF missions (spec 2.3). Wire values (3 bits).</summary>
    internal enum MissionKind : byte { None = 0, Recon = 1, Lase = 2, Sabotage = 3, Seize = 4, Tap = 5 }

    /// <summary>What a mission target is. Ground = a revealed enemy ground unit, Anchor = an enemy anchor (see <see cref="AnchorSub"/>), Building = a holdable enemy building, Relay = an enemy airbase network relay.</summary>
    internal enum TargetKind : byte { Ground = 0, Anchor = 1, Building = 2, Relay = 3 }

    internal enum AnchorSub : byte { Uplink = 0, EwTruck = 1, DataCenter = 2, Camp = 3 }

    /// <summary>The mid-route and lift orders a team takes. Wire values; DIVERT and missions have their own commands because they carry a point or a target.</summary>
    internal enum TeamVerb : byte { Push = 1, Hold = 2, Exfil = 3, Lift = 4, Cancel = 5 }

    /// <summary>How a team travels: on foot at 35 km/h, or inside a player helicopter (+20 % odds).</summary>
    internal enum Insertion : byte { Ground = 0, Helicopter = 1 }

    /// <summary>Spec 2.2 and 2.3 numbers in one place. Metres, mission seconds, CR, percent points.</summary>
    internal static class SofRules
    {
        public const int RaiseCost = 60, MaxTeams = 4, HeldCap = 4;
        public const float RaiseSeconds = 90f, SpeedMetresPerSecond = 35000f / 3600f, PushSpeedFactor = 1.5f, PushExposureFactor = 1.5f;
        public const float ExposureRadius = 1000f, CoverRadius = 2000f, ExposurePerEnemyPerSecond = 0.5f, BirdStareFactor = 1.25f, ExposureDecayPerSecond = 0.75f, HoldDecayFactor = 2f;
        public const float PinExposure = 100f, UnpinExposure = 60f, PinnedLostSeconds = 120f, CoverExtendSeconds = 60f, CoverKillRelief = 25f, LostExposure = 90f;
        public const float WoundedSeconds = 60f, LaseMaxSeconds = 300f, MaxAdvanceSeconds = 5f, ArrivalMetres = 40f;
        public const float ReconRadius = 2000f, ReconRevealSeconds = 300f, TapSeconds = 600f, ExploitDurationFactor = 1.5f, ExploitCostFactor = 0.75f;
        public const float HeldSeconds = 600f, HeldObserveRadius = 3000f, HeldRetakeMetres = 300f, HeldRetakeSeconds = 60f, HeldRevealPulseSeconds = 20f;
        public const float TapTraceFactor = 0.7f, RingBoostMetres = 12000f;
        public const float LiftPickupMetres = 150f, LiftDropMetres = 300f, LiftDwellSeconds = 10f, LiftMinPayMetres = 2000f;
        public const int LiftPay = 40, ExtractionPay = 40, CoverPay = 25;
        public const int BaseOdds = 70, MinOdds = 10, MaxOdds = 95;

        public static int TeamCap(int humans, bool fob) => (humans >= 5 ? 3 : 2) + (fob ? 1 : 0);

        public static string Callsign(int slot) => (char)('A' + Math.Max(0, Math.Min(MaxTeams - 1, slot))) + "-1";

        public static int MissionCost(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Recon: return 25;
                case MissionKind.Lase: return 25;
                case MissionKind.Tap: return 40;
                case MissionKind.Seize: return 50;
                case MissionKind.Sabotage: return 60;
                default: return 0;
            }
        }

        /// <summary>Spec 0 ring: EXPLOIT takes a quarter off.</summary>
        public static int CostOf(MissionKind kind, bool exploit) =>
            exploit ? (int)Math.Ceiling(MissionCost(kind) * ExploitCostFactor - 1e-6) : MissionCost(kind);

        /// <summary>Seconds on site before the odds roll (LASE has no roll: it holds until stopped or <see cref="LaseMaxSeconds"/>).</summary>
        public static float OnSiteSeconds(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Recon: return 60f;
                case MissionKind.Lase: return LaseMaxSeconds;
                case MissionKind.Sabotage: return 90f;
                case MissionKind.Seize: return 120f;
                case MissionKind.Tap: return 60f;
                default: return 0f;
            }
        }

        public static bool Valid(MissionKind kind, TargetKind target, AnchorSub sub)
        {
            switch (kind)
            {
                case MissionKind.Lase: return target == TargetKind.Ground;
                case MissionKind.Sabotage: return target == TargetKind.Anchor;
                case MissionKind.Seize: return target == TargetKind.Building;
                case MissionKind.Tap: return target == TargetKind.Relay || (target == TargetKind.Anchor && sub == AnchorSub.DataCenter);
                default: return false;
            }
        }

        /// <summary>SOF beats CYBER (the ring): an EW truck, a data center or a relay is an EXPLOIT target.</summary>
        public static bool Exploit(TargetKind target, AnchorSub sub) =>
            target == TargetKind.Relay || (target == TargetKind.Anchor && (sub == AnchorSub.EwTruck || sub == AnchorSub.DataCenter));

        /// <summary>SPACE beats SOF: an uplink target is RESISTED (a grey tag; it changes no number in M5a).</summary>
        public static bool Resisted(TargetKind target, AnchorSub sub) => target == TargetKind.Anchor && sub == AnchorSub.Uplink;

        /// <summary>Spec 2.3 odds: 70 - exposure / 2 - 10 per enemy armoured unit within 1 km + 20 helicopter + 25 EXPLOIT + 15 CYBER ring, clamped 10..95.</summary>
        public static int Odds(float exposure, int armoredNear, bool helicopter, bool exploit, bool cyberNear)
        {
            if (float.IsNaN(exposure) || float.IsInfinity(exposure)) exposure = 100f;
            float v = BaseOdds - Math.Max(0f, Math.Min(100f, exposure)) * 0.5f - 10f * Math.Max(0, Math.Min(20, armoredNear))
                + (helicopter ? 20f : 0f) + (exploit ? 25f : 0f) + (cyberNear ? 15f : 0f);
            return (int)Math.Max(MinOdds, Math.Min(MaxOdds, Math.Round(v)));
        }

        /// <summary>Change of exposure over <paramref name="dt"/> seconds (positive rises). Enemies within 1 km add 0.5 %/s each (the mission target's own unit is never counted) (x1.25 under a bird, x1.5 pushed); against the 0.5 %/s a team always recovers (x2 held), so nothing near falls 0.5 %/s.</summary>
        public static float ExposureDelta(int enemies, bool stare, bool pushed, bool held, float dt)
        {
            if (dt <= 0f || float.IsNaN(dt)) return 0f;
            if (enemies > 0)
            {
                float rate = Math.Min(60, enemies) * ExposurePerEnemyPerSecond * (stare ? BirdStareFactor : 1f) * (pushed ? PushExposureFactor : 1f);
                return (rate - ExposureDecayPerSecond * (held ? HoldDecayFactor : 1f)) * dt; // a team always recovers: one unpushed defender is a stand-off, two are a slow climb
            }
            return -ExposureDecayPerSecond * (held ? HoldDecayFactor : 1f) * dt;
        }

        public static float Distance(float ax, float az, float bx, float bz)
        {
            double dx = (double)ax - bx, dz = (double)az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// How a SOF command packs its ints (SpaceCommand carries no coordinates, only ints). A point is two 40 m cells of 15 bits each (+-655 km), so a
    /// client can name a place without ever sending a unit key; a team slot is 2 bits, a verb or mission kind sits above it.
    /// </summary>
    internal static class SofCodes
    {
        public const float CellMetres = 40f;
        private const int Bias = 16384, Mask = 0x7FFF;

        public static int PackPoint(float x, float z)
        {
            int cx = (int)Math.Max(0, Math.Min(Mask, Math.Round(x / CellMetres) + Bias)), cz = (int)Math.Max(0, Math.Min(Mask, Math.Round(z / CellMetres) + Bias));
            return (cx << 15) | cz;
        }

        public static bool TryUnpackPoint(int packed, out float x, out float z)
        {
            x = z = 0f;
            if (packed < 0) return false;
            x = (((packed >> 15) & Mask) - Bias) * CellMetres;
            z = ((packed & Mask) - Bias) * CellMetres;
            return true;
        }

        public static int PackOrder(int slot, TeamVerb verb) => (slot & 3) | ((int)verb << 2);

        public static bool TryUnpackOrder(int packed, out int slot, out TeamVerb verb)
        {
            slot = packed & 3; verb = (TeamVerb)(packed >> 2);
            return packed >= 0 && (int)verb >= (int)TeamVerb.Push && (int)verb <= (int)TeamVerb.Cancel;
        }

        public static int PackMission(int slot, MissionKind kind) => (slot & 3) | ((int)kind << 2);

        public static bool TryUnpackMission(int packed, out int slot, out MissionKind kind)
        {
            slot = packed & 3; kind = (MissionKind)(packed >> 2);
            return packed >= 0 && (int)kind >= (int)MissionKind.Recon && (int)kind <= (int)MissionKind.Tap;
        }
    }

    /// <summary>The words of the SOF page and console. Pure.</summary>
    internal static class SofWords
    {
        public static string Kind(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Recon: return "RECON";
                case MissionKind.Lase: return "LASE";
                case MissionKind.Sabotage: return "SABOTAGE";
                case MissionKind.Seize: return "SEIZE";
                case MissionKind.Tap: return "NETWORK TAP";
                default: return "NONE";
            }
        }

        public static string State(TeamState state)
        {
            switch (state)
            {
                case TeamState.Raising: return "RAISING";
                case TeamState.Ready: return "READY";
                case TeamState.Moving: return "MOVING";
                case TeamState.OnSite: return "ON SITE";
                case TeamState.Pinned: return "PINNED";
                case TeamState.Returning: return "EXFIL";
                case TeamState.Recovering: return "WIA";
                default: return "LOST";
            }
        }

        public static string Target(TargetKind kind, AnchorSub sub, int id)
        {
            string name = kind == TargetKind.Ground ? "GROUND CONTACT" : kind == TargetKind.Building ? "BUILDING" : kind == TargetKind.Relay ? "RELAY" :
                sub == AnchorSub.Uplink ? "UPLINK" : sub == AnchorSub.EwTruck ? "EW TRUCK" : sub == AnchorSub.DataCenter ? "DATA CENTER" : "CAMP";
            return name + " #" + id.ToString(CultureInfo.InvariantCulture);
        }

        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) seconds = 0f;
            int s = (int)Math.Ceiling(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
