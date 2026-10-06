using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>What a held node or a fired package does to the enemy. Stable values (they never cross the wire, but logs and tests name them).</summary>
    internal enum EffectKind : byte { RadarJam = 0, SamBlock = 1, HoldFire = 2, RelaySilence = 3, BirdJam = 4, TraceBoost = 5, TraceCut = 6 }

    internal enum EffectSource : byte { Hold = 0, Package = 1 }

    /// <summary>One timed effect. <see cref="Victim"/> 0 means every faction other than <see cref="Owner"/> (packages carry no victim). Mission seconds.</summary>
    internal readonly struct CyberEffect
    {
        public readonly EffectKind Kind;
        public readonly EffectSource Source;
        public readonly int SourceId, Owner, Victim;
        public readonly float X, Z, Radius, Factor, Until;
        public readonly uint UnitId;

        public CyberEffect(EffectKind kind, EffectSource source, int sourceId, int owner, int victim,
            float x, float z, float radius, uint unitId, float factor, float until)
        {
            Kind = kind; Source = source; SourceId = sourceId; Owner = owner; Victim = victim;
            X = x; Z = z; Radius = radius; UnitId = unitId; Factor = factor; Until = until;
        }

        public CyberEffect WithUntil(float until) =>
            new CyberEffect(Kind, Source, SourceId, Owner, Victim, X, Z, Radius, UnitId, Factor, until);

        internal bool Hits(int victim) => Victim == victim || (Victim == 0 && victim != Owner);
    }

    /// <summary>One faction pair's active effects, bounded. The host applies them; every query is allocation-free.</summary>
    internal sealed class CyberEffectBook
    {
        public const int Capacity = 32;
        public const float RadarFactor = 0.4f, BirdCooldownFactor = 1.5f, BirdOpticalFactor = 0.5f, TraceBoostFactor = 1.3f, TraceCutFactor = 0.7f, SamReacquireSeconds = 10f;
        private readonly List<CyberEffect> effects = new List<CyberEffect>(Capacity);

        public int Count => effects.Count;

        /// <summary>Adds or replaces the effect with the same (kind, source, source id). False when the book is full.</summary>
        public bool Add(in CyberEffect e)
        {
            if (float.IsNaN(e.Until) || float.IsNaN(e.X) || float.IsNaN(e.Z) || float.IsNaN(e.Radius) || float.IsNaN(e.Factor)) return false;
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].Kind == e.Kind && effects[i].Source == e.Source && effects[i].SourceId == e.SourceId) { effects[i] = e; return true; }
            if (effects.Count >= Capacity) return false;
            effects.Add(e);
            return true;
        }

        /// <summary>The node is no longer held. A SAM block lingers 10 s (the SAMs reacquire); everything else ends now. Returns the effects touched.</summary>
        public int ReleaseHold(int nodeId, float now)
        {
            int n = 0;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                CyberEffect e = effects[i];
                if (e.Source != EffectSource.Hold || e.SourceId != nodeId) continue;
                n++;
                if (e.Kind == EffectKind.SamBlock) effects[i] = e.WithUntil(Math.Min(e.Until, now + SamReacquireSeconds));
                else effects.RemoveAt(i);
            }
            return n;
        }

        public int Expire(float now)
        {
            int n = 0;
            for (int i = effects.Count - 1; i >= 0; i--)
                if (now >= effects[i].Until) { effects.RemoveAt(i); n++; }
            return n;
        }

        public void Clear() => effects.Clear();

        /// <summary>Cheap prefilter for the launch patch: is any launch-blocking effect active at all.</summary>
        public bool AnyLaunchBlock(float now)
        {
            for (int i = 0; i < effects.Count; i++)
                if (now < effects[i].Until && (effects[i].Kind == EffectKind.SamBlock || effects[i].Kind == EffectKind.HoldFire)) return true;
            return false;
        }

        public bool AnyShareBlock(float now)
        {
            for (int i = 0; i < effects.Count; i++)
                if (now < effects[i].Until && effects[i].Kind == EffectKind.RelaySilence) return true;
            return false;
        }

        /// <summary>The range multiplier for one radar: 1 when nothing jams it, else the strongest (lowest) jam.</summary>
        public float RadarRangeFactor(int victim, uint unitId, float x, float z, float now)
        {
            float f = 1f;
            for (int i = 0; i < effects.Count; i++)
            {
                CyberEffect e = effects[i];
                if (e.Kind != EffectKind.RadarJam || now >= e.Until || !e.Hits(victim)) continue;
                bool hit = e.UnitId != 0 ? e.UnitId == unitId : e.Radius > 0f && CyberGraph.InReach(x, z, e.X, e.Z, e.Radius);
                if (hit && e.Factor < f) f = e.Factor;
            }
            return f;
        }

        /// <summary>A launcher of <paramref name="victim"/> at (x, z) may not fire: SAM block hits SAM launchers only, hold-fire hits every ground shooter.</summary>
        public bool LaunchBlocked(int victim, bool isSam, float x, float z, float now)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                CyberEffect e = effects[i];
                if (now >= e.Until || !e.Hits(victim)) continue;
                if (e.Kind == EffectKind.SamBlock && !isSam) continue;
                if (e.Kind != EffectKind.SamBlock && e.Kind != EffectKind.HoldFire) continue;
                if (CyberGraph.InReach(x, z, e.X, e.Z, e.Radius)) return true;
            }
            return false;
        }

        /// <summary>A sighting made by a detector of <paramref name="victim"/> at (x, z) is not shared with its faction.</summary>
        public bool ShareBlocked(int victim, float x, float z, float now)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                CyberEffect e = effects[i];
                if (e.Kind == EffectKind.RelaySilence && now < e.Until && e.Hits(victim) && CyberGraph.InReach(x, z, e.X, e.Z, e.Radius)) return true;
            }
            return false;
        }

        /// <summary>SPACE cooldown multiplier of a jammed faction (BIRD JAM).</summary>
        public float CooldownFactor(int victim, float now) => Any(EffectKind.BirdJam, victim, now) ? BirdCooldownFactor : 1f;

        /// <summary>OPTICAL footprint multiplier of a jammed faction (BIRD JAM).</summary>
        public float OpticalFactor(int victim, float now) => Any(EffectKind.BirdJam, victim, now) ? BirdOpticalFactor : 1f;

        /// <summary>Trace multiplier for intrusions of <paramref name="intruder"/> (an enemy holds its data center node).</summary>
        public float TraceFactor(int intruder, float now) => Any(EffectKind.TraceBoost, intruder, now) ? TraceBoostFactor : 1f;

        /// <summary>Trace multiplier of <paramref name="intruder"/> after a SOF NETWORK TAP on the enemy network (-30 %); 1 when none runs.</summary>
        public float TraceCut(int intruder, float now) => Any(EffectKind.TraceCut, intruder, now) ? TraceCutFactor : 1f;

        private bool Any(EffectKind kind, int victim, float now)
        {
            for (int i = 0; i < effects.Count; i++)
                if (effects[i].Kind == kind && now < effects[i].Until && effects[i].Hits(victim)) return true;
            return false;
        }
    }

    internal readonly struct PackageDef
    {
        public readonly NodeKind Node;
        public readonly SupportActionId Action;
        public readonly string Label;
        public readonly CallTier Tier;
        public readonly EffectKind Effect;
        public readonly float Seconds, Radius, Factor;

        public PackageDef(NodeKind node, SupportActionId action, string label, CallTier tier, EffectKind effect, float seconds, float radius, float factor)
        { Node = node; Action = action; Label = label; Tier = tier; Effect = effect; Seconds = seconds; Radius = radius; Factor = factor; }

        /// <summary>The payoff line of a posted package, e.g. <c>HOLDS SAM LAUNCH 3 KM · 1:00</c>.</summary>
        public string Payoff(bool exploit)
        {
            float s = exploit ? Seconds * CyberPackages.ExploitDurationFactor : Seconds;
            int m = (int)(s / 60f), sec = (int)Math.Round(s - m * 60f);
            return What + " · " + m + ":" + (sec < 10 ? "0" : "") + sec;
        }

        private string What
        {
            get
            {
                switch (Effect)
                {
                    case EffectKind.RadarJam: return "RADAR RANGE -60 % WITHIN " + (Radius / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM";
                    case EffectKind.SamBlock: return "SAMS CANNOT LAUNCH WITHIN " + (Radius / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM";
                    case EffectKind.HoldFire: return "GROUND UNITS HOLD FIRE WITHIN " + (Radius / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KM";
                    case EffectKind.BirdJam: return "ENEMY SPACE +50 % COOLDOWN, OPTICAL -50 %";
                    default: return "NETWORK EFFECT";
                }
            }
        }
    }

    /// <summary>Spec §1.2 tables: what a held node does (HOLD) and the TASKED package its BURN posts. Honest closest effects, see the plan.</summary>
    internal static class CyberPackages
    {
        public const float ExploitDurationFactor = 1.5f;

        private static readonly PackageDef[] all =
        {
            new PackageDef(NodeKind.Radar, SupportActionId.CyberJamRadar, "JAM RADAR", CallTier.Light, EffectKind.RadarJam, 90f, 1500f, CyberEffectBook.RadarFactor),
            new PackageDef(NodeKind.SamC2, SupportActionId.CyberSamNetDown, "SAM NET DOWN", CallTier.Heavy, EffectKind.SamBlock, 60f, 3000f, 1f),
            new PackageDef(NodeKind.Relay, SupportActionId.CyberSpoofIff, "SPOOF IFF", CallTier.Heavy, EffectKind.HoldFire, 60f, 3000f, 1f),
            new PackageDef(NodeKind.Uplink, SupportActionId.CyberBirdJam, "BIRD JAM", CallTier.Heavy, EffectKind.BirdJam, 180f, 0f, CyberEffectBook.BirdCooldownFactor),
            new PackageDef(NodeKind.DataCenter, SupportActionId.CyberBlackout, "BLACKOUT", CallTier.Heavy, EffectKind.RadarJam, 45f, 12000f, CyberEffectBook.RadarFactor),
        };

        public static IReadOnlyList<PackageDef> All => all;

        public static bool TryOf(NodeKind node, out PackageDef def)
        {
            for (int i = 0; i < all.Length; i++) if (all[i].Node == node) { def = all[i]; return true; }
            def = default; return false;
        }

        public static bool TryOfAction(SupportActionId action, out PackageDef def)
        {
            for (int i = 0; i < all.Length; i++) if (all[i].Action == action) { def = all[i]; return true; }
            def = default; return false;
        }

        /// <summary>The HOLD effect of a node while its intrusion holds it. Runs until released.</summary>
        public static CyberEffect Hold(in CyberNode node, int owner)
        {
            switch (node.Kind)
            {
                case NodeKind.Radar:
                    return new CyberEffect(EffectKind.RadarJam, EffectSource.Hold, node.Id, owner, node.Victim, node.X, node.Z, 0f, node.UnitId, CyberEffectBook.RadarFactor, float.PositiveInfinity);
                case NodeKind.SamC2:
                    return new CyberEffect(EffectKind.SamBlock, EffectSource.Hold, node.Id, owner, node.Victim, node.X, node.Z, 3000f, 0u, 1f, float.PositiveInfinity);
                case NodeKind.Relay:
                    return new CyberEffect(EffectKind.RelaySilence, EffectSource.Hold, node.Id, owner, node.Victim, node.X, node.Z, 8000f, 0u, 1f, float.PositiveInfinity);
                case NodeKind.Uplink:
                    return new CyberEffect(EffectKind.BirdJam, EffectSource.Hold, node.Id, owner, node.Victim, node.X, node.Z, 0f, 0u, CyberEffectBook.BirdCooldownFactor, float.PositiveInfinity);
                default:
                    return new CyberEffect(EffectKind.TraceBoost, EffectSource.Hold, node.Id, owner, node.Victim, node.X, node.Z, 0f, 0u, CyberEffectBook.TraceBoostFactor, float.PositiveInfinity);
            }
        }

        /// <summary>A SOF NETWORK TAP (spec 2.3): the owner's own intrusions trace 30 % slower for <paramref name="seconds"/>. It lives in the owner's own book, victim = owner.</summary>
        public static CyberEffect Tap(int owner, int sourceId, float seconds, float now) =>
            new CyberEffect(EffectKind.TraceCut, EffectSource.Package, sourceId, owner, owner, 0f, 0f, 0f, 0u, CyberEffectBook.TraceCutFactor, now + seconds);

        /// <summary>The effect of a fired package: area effects centred on the posted node point, against every other faction, for the package seconds (x1.5 EXPLOIT).</summary>
        public static CyberEffect Package(in PackageDef def, int callId, float x, float z, int owner, bool exploit, float now)
        {
            float seconds = exploit ? def.Seconds * ExploitDurationFactor : def.Seconds;
            return new CyberEffect(def.Effect, EffectSource.Package, callId, owner, 0, x, z, def.Radius, 0u, def.Factor, now + seconds);
        }
    }
}
