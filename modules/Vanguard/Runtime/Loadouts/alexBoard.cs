using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>What every peer knows about one ALE-X decoy (host: written by its flight; clients: by AlexNet).</summary>
    internal struct AlexView
    {
        public uint Decoy, Host;
        public KiteState State;
        public byte Pips;
        public byte Captured;   // enemy missiles currently riding the kite
        public byte Dragged;    // ... riding it when it was cut
        public KiteCut Reason;
        public float Line;      // fiber paid out, m
        public float Target;    // fiber the reel is heading for, m
        public float FreeLeft;  // seconds before a cut decoy self-destructs
        public float Stamp;     // receive time (unscaled), for the FreeLeft countdown
    }

    internal static class AlexBoard
    {
        public static readonly Dictionary<uint, AlexView> Views = new Dictionary<uint, AlexView>();
        public static Action<AlexView> Changed; // host -> AlexNet broadcast

        public static void Publish(AlexView view)
        {
            view.Stamp = Time.unscaledTime;
            Views[view.Decoy] = view;
            Changed?.Invoke(view);
        }

        public static void Remove(uint decoy)
        {
            if (!Views.TryGetValue(decoy, out AlexView view)) return;
            Views.Remove(decoy);
            view.State = KiteState.Gone;
            Changed?.Invoke(view);
        }

        /// <summary>The host's decoy that is still on the fiber (paying out or towed), or false.</summary>
        public static bool TowedBy(uint host, out AlexView found)
        {
            foreach (AlexView view in Views.Values)
                if (view.Host == host && (view.State == KiteState.Deploy || view.State == KiteState.Towed))
                {
                    found = view;
                    return true;
                }
            found = default;
            return false;
        }

        /// <summary>The host's decoy: on the fiber if there is one, else the latest cut one.</summary>
        public static bool ForHost(uint host, out AlexView found)
        {
            if (TowedBy(host, out found)) return true;
            foreach (AlexView view in Views.Values)
                if (view.Host == host && view.State == KiteState.Free)
                {
                    found = view;
                    return true;
                }
            found = default;
            return false;
        }

        public static void Clear() => Views.Clear();
    }

    /// <summary>DEFLECT: radar missiles tracking a kite host get their aimpoint pulled toward the decoy (server).</summary>
    internal static class AlexDeflect
    {
        private static readonly AccessTools.FieldRef<Missile, GlobalPosition> AimPoint = AccessTools.FieldRefAccess<Missile, GlobalPosition>("aimPoint");
        private static readonly AccessTools.FieldRef<Missile, Vector3> TargetVel = AccessTools.FieldRefAccess<Missile, Vector3>("targetVel");
        private static readonly Dictionary<Missile, Missile> Pulled = new Dictionary<Missile, Missile>();

        public static int Count => Pulled.Count;

        public static void Pull(Missile threat, Missile decoy) => Pulled[threat] = decoy;

        public static void Release(Missile threat) => Pulled.Remove(threat);

        public static void Clear() => Pulled.Clear();

        /// <summary>Seeker.Seek postfix: seekers write their aimpoint every tick, so the pull never compounds.</summary>
        public static void Apply(Missile threat)
        {
            if (Pulled.Count == 0 || threat == null || !Pulled.TryGetValue(threat, out Missile decoy)) return;
            if (decoy == null || decoy.disabled || threat.disabled)
            {
                Pulled.Remove(threat);
                return;
            }
            GlobalPosition aim = AimPoint(threat);
            threat.SetAimpoint(aim + AlexKite.DeflectShift(decoy.GlobalPosition() - aim), TargetVel(threat));
            VanguardStats.Bump("alex.deflected_ticks");
        }
    }

    /// <summary>SUPPRESS: turret locks on a towing host accrue slower (server and every peer that simulates the turret).</summary>
    internal static class AlexSuppress
    {
        private struct Hold
        {
            public int Decoys;   // decoys of this host that are on the fiber
            public float At;     // last time a turret was slowed
        }

        private static readonly Dictionary<Unit, Hold> Hosts = new Dictionary<Unit, Hold>();

        public static int Count => Hosts.Count;

        public static void Add(Unit host)
        {
            if (host == null) return;
            Hosts.TryGetValue(host, out Hold hold);
            if (hold.Decoys == 0) hold.At = -99f;
            hold.Decoys++;
            Hosts[host] = hold;
        }

        public static void Remove(Unit host)
        {
            if (host == null || !Hosts.TryGetValue(host, out Hold hold)) return;
            if (--hold.Decoys <= 0) Hosts.Remove(host);
            else Hosts[host] = hold;
        }

        public static bool Applies(Unit unit) => unit != null && Hosts.ContainsKey(unit);

        public static void Touch(Unit host)
        {
            Hold hold = Hosts[host];
            hold.At = Time.timeSinceLevelLoad;
            Hosts[host] = hold;
        }

        public static bool Recent(Unit host, float window) =>
            host != null && Hosts.TryGetValue(host, out Hold hold) && Time.timeSinceLevelLoad - hold.At <= window;

        public static void Clear() => Hosts.Clear();
    }
}
