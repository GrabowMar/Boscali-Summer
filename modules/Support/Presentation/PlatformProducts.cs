using BoscaliSummer.Features.Support.Domain.Orbital;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Visuals;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The local player's latest radar product. A radar scan accepted from anywhere — the map,
    /// the uplink, a TGT mark — starts forming here, from the station's look at that moment,
    /// and both the PLATFORM page and the uplink show the same image. Client-local.
    /// </summary>
    internal sealed class PlatformProducts
    {
        private readonly SarCollector scan = new SarCollector();
        private int serialSeen = -1;

        public SarCollector Scan => scan;
        public bool HasProduct => scan.Phase != SarPhase.Idle && scan.Image != null;

        /// <summary>Returns true on the tick a new scan begins forming.</summary>
        public bool Tick(SupportManager support, float deltaTime)
        {
            bool started = false;
            if (serialSeen < 0) serialSeen = support.RadarScanSerial;
            if (support.RadarScanSerial != serialSeen)
            {
                serialSeen = support.RadarScanSerial;
                OrbitalPlatform platform = support.LocalPlatform;
                if (platform != null && platform.Exists)
                {
                    GlobalPosition target = support.RadarScanTarget;
                    OrbitState state = platform.State(support.OrbitNow);
                    LookAngles look = TheaterTrack.Look(state, target.x, target.z);
                    if (look.Visible)
                    {
                        scan.Begin(target, look, state,
                            support.GetEffectRadius(SupportActionId.Recon), serialSeen * 7919 + platform.Seed);
                        scan.Contacts = support.RadarScanContacts;
                        started = true;
                    }
                }
            }
            scan.Tick(deltaTime);
            return started;
        }

        public void Reset()
        {
            scan.Dispose();
            serialSeen = -1;
        }
    }
}
