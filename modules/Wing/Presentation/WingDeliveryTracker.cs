using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Bounded, allocation-free tracking of wing ordnance for HUD flight-time and splash
    /// readouts.</summary>
    internal static class WingDeliveryTracker
    {
        private sealed class Delivery
        {
            public Aircraft Shooter;
            public Unit Target;
            public float ImpactTime;
            public bool Splashed;
            public float SplashUntil;
        }

        private const int MaxDeliveries = 16;
        private static readonly List<Delivery> active = new List<Delivery>(MaxDeliveries);

        public static void Reset()
        {
            active.Clear();
        }

        public static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Delivery d = active[i];
                if (d.Shooter == null || !d.Shooter.gameObject.activeInHierarchy)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (!d.Splashed && d.Target != null && d.Target.disabled)
                {
                    d.Splashed = true;
                    d.SplashUntil = now + 3f;
                }

                if (d.Splashed)
                {
                    if (now >= d.SplashUntil)
                    {
                        active.RemoveAt(i);
                    }
                }
                else if (now > d.ImpactTime + 3f)
                {
                    active.RemoveAt(i);
                }
            }
        }
    }
}
