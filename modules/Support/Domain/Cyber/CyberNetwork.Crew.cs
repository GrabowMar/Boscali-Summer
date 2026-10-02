using System;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    internal sealed partial class CyberNetwork
    {
        // Independent preparation can be fast; ingress still leaves a real defender response window.
        public const float CrewHandshakeSeconds = 15f;
        public const float CrewForceHandshakeSeconds = 10f;
        private double crewReleaseAt;
        public static float CrewIngressSeconds(bool quiet) => quiet ? CrewHandshakeSeconds : CrewForceHandshakeSeconds;
        public static float CrewIngressCost(bool quiet) => PhaseCost(BreachPhase.Probe, 1, quiet);
        public double CrewReleaseRemaining(double now) => Math.Max(0, crewReleaseAt - now);
        public byte CrewLinks { get; private set; }
        public bool LinkSolved(int link) => link >= 0 && link < 3 && (CrewLinks & (1 << link)) != 0;
        public static string LinkName(int link) => link == 0 ? "IDENTITY GATE" : link == 1 ? "FIREWALL" : "EXFIL RELAY";
        public static string ToolName(int tool) => tool == 0 ? "REPLAY TOKEN" : tool == 1 ? "TUNNEL SERVICE" : "CLONE RELAY";

        public int LinkSignature(int link)
        {
            if (!BreachActive || link < 0 || link >= 3) return 0;
            // The existing replicated project identity supplies a new service layout for each session
            // and defensive invalidation. Each gate is independent, not one of three permutations.
            CyberNode target = nodes[breachTarget];
            uint site = unchecked((uint)BitConverter.SingleToInt32Bits(target.X) * 0x9E3779B9u ^
                (uint)BitConverter.SingleToInt32Bits(target.Z) * 0x85EBCA6Bu ^ (uint)target.Kind ^ (uint)breachTarget);
            uint seed = unchecked(site ^ (uint)WorkRevision * 0x85EBCA6Bu ^ (uint)(link + 1) * 0xC2B2AE35u);
            seed ^= seed >> 16; seed = unchecked(seed * 0x7FEB352Du);
            seed ^= seed >> 15; seed = unchecked(seed * 0x846CA68Bu);
            return (int)((seed ^ (seed >> 16)) % 3);
        }

        public string LinkClue(int link) => WorkAnalysis == 0 ? "Unprofiled. A teammate can map the services."
            : LinkSignature(link) == 0 ? "ROTATING CREDENTIAL / reuse its signed token"
            : LinkSignature(link) == 1 ? "FILTERED TRANSPORT / encapsulate its service"
            : "TRUSTED UPLINK / impersonate its relay";

        public float CrewCost(int command)
        {
            if (command == 14) return 16f;
            float cost = command == 4 ? 12f : command >= 5 && command <= 13 ? 18f : 0f;
            return cost * (breachQuiet ? 1f : CyberLocations.ForceCostScale);
        }

        /// <summary>Signed trace forecast for the live contribution; scrubbing is never diminished by upgrades.</summary>
        public float CrewTrace(int command)
        {
            if (command == 14) return -0.30f;
            float trace = command == 4 ? 0.08f : command >= 5 && command <= 13
                ? ((command - 5) % 3 == LinkSignature((command - 5) / 3) ? 0.12f : 0.32f) : 0f;
            return TraceResistance(trace * (breachQuiet ? 1f : CyberLocations.ForceTraceScale));
        }

        private BreachDenial CheckCrewSession(int revision, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || !BreachActive || now >= breachPhaseEnds) return BreachDenial.NoSession;
            if (!CommandOnline || CommandCompromised) return BreachDenial.NoCommand;
            return revision != WorkRevision ? BreachDenial.StaleWork : BreachDenial.None;
        }

        // Commands 4: profile, 5..13: three tools at three parallel gates,
        // 14: clear trace, 15: release the complete package to the faction.
        public BreachDenial CheckCrewWork(int command, int revision, double now)
        {
            BreachDenial denial = CheckCrewSession(revision, now);
            if (denial != BreachDenial.None) return denial;
            if (command < 4 || command > 15) return BreachDenial.NoTarget;
            if (command == 4 && WorkAnalysis != 0) return BreachDenial.AlreadyAnalyzed;
            if (command == 15 && CrewLinks != 7) return BreachDenial.Incomplete;
            if (command == 15 && CrewReleaseRemaining(now) > 0) return BreachDenial.Recharging;
            if (command >= 5 && command <= 13 && LinkSolved((command - 5) / 3)) return BreachDenial.AlreadyAnalyzed;
            if ((command == 14 && SpoofRechargeRemaining(now) > 0) || (command != 14 && now < workReady)) return BreachDenial.Recharging;
            return Computing + 0.001f < CrewCost(command) ? BreachDenial.LowComputing : BreachDenial.None;
        }

        public BreachDenial CrewWork(int command, int revision, double now)
        {
            BreachDenial denial = CheckCrewWork(command, revision, now);
            if (denial != BreachDenial.None) return denial;
            SpendComputing(CrewCost(command));
            breachTrace = Math.Max(0f, Math.Min(1f, breachTrace + CrewTrace(command)));
            if (command == 4) WorkAnalysis = 1;
            else if (command == 14) capstoneReady[0] = now + 3;
            else if (command == 15)
            {
                breachPhase = BreachPhase.Extract;
                Complete(now);
                return BreachDenial.None;
            }
            else
            {
                int link = (command - 5) / 3, tool = (command - 5) % 3;
                if (tool == LinkSignature(link))
                {
                    CrewLinks |= (byte)(1 << link);
                    WorkProgress++;
                    WorkQuality = Math.Min(6, WorkQuality + (WorkAnalysis > 0 ? 2 : 1));
                    breachPhase = WorkProgress < 2 ? BreachPhase.Probe : BreachPhase.Exploit;
                }
            }
            breachPhaseEnds = now + CyberLocations.SessionSeconds;
            if (breachTrace >= 1f) Backtrace(now);
            return BreachDenial.None;
        }

        /// <summary>A completed field intercept can map an actual nearby breach once. It grants no resources
        /// or retroactive quality, and cannot work through a defender's interruption.</summary>
        public bool TryFieldProfile(float x, float z, float radius, double now)
        {
            if (!Finite(x) || !Finite(z) || !Finite(radius) || radius <= 0f ||
                CheckCrewSession(WorkRevision, now) != BreachDenial.None || WorkAnalysis != 0 || now < workReady) return false;
            float dx = nodes[breachTarget].X - x, dz = nodes[breachTarget].Z - z;
            if ((double)dx * dx + (double)dz * dz > (double)radius * radius) return false;
            WorkAnalysis = 1;
            breachTrace = Math.Min(1f, breachTrace + CrewTrace(4));
            breachPhaseEnds = now + CyberLocations.SessionSeconds;
            if (breachTrace >= 1f) Backtrace(now);
            return true;
        }
    }
}
