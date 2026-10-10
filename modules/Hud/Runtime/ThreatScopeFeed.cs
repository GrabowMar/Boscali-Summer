using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Hud's own provider of the THREAT page's feed (<see cref="ThreatScope"/>): every missile inbound on the local aircraft (and the ones that just lost it) and every radar
    /// painting it, read from <see cref="ThreatWatch"/> once per frame at most (positions are read when asked: the page repaints every frame while it shows); the countermeasure
    /// stations as the 10 Hz board frame has them; and, once a second, how many known radar SAM envelopes the aircraft is inside (<see cref="IThreatPicture"/>; without a picture
    /// the page draws no SAM line, it never invents a calm sky). Live while anything is inbound, painting us, or has just lost us. Client-local: the game's own warning events.
    /// </summary>
    internal sealed class ThreatScopeFeed
    {
        private const float SamEvery = 1f;

        private readonly ThreatWatch watch;
        private readonly ThreatMissile[] missiles = new ThreatMissile[8];
        private readonly ThreatEmitter[] emitters = new ThreatEmitter[8];
        private readonly AirDefenceRing[] rings = new AirDefenceRing[ThreatPictureLimits.MaximumRings];
        private ThreatScope cache;
        private bool live;
        private int stamp = -1;
        private Aircraft aircraft;
        private CmStation[] cm;
        private int cmCount, samRings;
        private bool hasSam;
        private float nextSam;
        private IThreatPicture picture;

        public ThreatScopeFeed(ThreatWatch watch) { this.watch = watch; }

        /// <summary>The 10 Hz board frame's countermeasure stations and the HUD's aircraft (<c>HudBoard</c>).</summary>
        public void Update(Aircraft hud, BoardFrame frame)
        {
            aircraft = hud;
            cm = frame.Cm;
            cmCount = frame.CmCount;
            stamp = -1;
        }

        public bool Read(out ThreatScope data)
        {
            if (stamp != Time.frameCount)
            {
                stamp = Time.frameCount;
                live = Build();
            }
            data = cache;
            return live;
        }

        private bool Build()
        {
            cache = default;
            if (aircraft == null || aircraft.disabled) return false;
            float now = Time.unscaledTime;
            watch.Fill(aircraft, now, missiles, out int mc, emitters, out int ec);
            if (mc == 0 && ec == 0) return false;
            Sam(now);
            cache = new ThreatScope { MissileCount = mc, Missiles = missiles, EmitterCount = ec, Emitters = emitters, Cm = cm, CmCount = cmCount, HasSam = hasSam, SamRings = samRings };
            return true;
        }

        /// <summary>How many known radar SAM envelopes cover the aircraft at its height now, from the faction's own picture, once a second.</summary>
        private void Sam(float now)
        {
            if (now < nextSam) return;
            nextSam = now + SamEvery;
            hasSam = false;
            samRings = 0;
            if (picture == null) ModuleServices.TryGet(out picture);
            FactionHQ hq = aircraft.NetworkHQ;
            if (picture == null || hq == null) return;
            int observer = hq.GetInstanceID();
            if (!picture.IsReady(observer)) return;
            GlobalPosition at = aircraft.transform.position.ToGlobalPosition();
            int n = picture.CopyAirDefence(observer, rings);
            hasSam = true;
            for (int i = 0; i < n; i++)
                if (ObjectiveMath.Radar(rings[i]) && ObjectiveMath.Expose(rings[i], at.x, at.z, aircraft.radarAlt).Inside) samRings++;
        }

        public void Clear()
        {
            live = false;
            aircraft = null;
            stamp = -1;
        }
    }
}
