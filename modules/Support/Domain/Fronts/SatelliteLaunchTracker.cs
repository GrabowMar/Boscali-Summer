using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>One launch cue per bird and host event. Reconnect/faction history is quiet; clock corrections never replay it.</summary>
    internal sealed class SatelliteLaunchTracker
    {
        private const float MaxAge = 20f, Tolerance = 1.5f; // wire log ages are rounded to whole seconds
        private readonly float[] seen = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
        private readonly float[] expires = new float[3];
        private byte pending;
        private uint faction;
        private ushort epoch;
        private bool known;

        public void Reset()
        {
            known = false; pending = 0;
            for (int i = 0; i < seen.Length; i++) seen[i] = float.NegativeInfinity;
        }

        public byte Observe(FrontMirror mirror, float now, bool ready = true)
        {
            if (mirror == null || !mirror.Known) { Reset(); return 0; }
            FrontStateData state = mirror.State;
            bool history = !known || faction != state.Faction || epoch != state.Epoch;
            if (history) Reset();
            known = true; faction = state.Faction; epoch = state.Epoch;
            float offset = state.Now - mirror.HostNow;
            foreach (FrontLogRow row in state.Fronts[(int)Front.Space].Log)
            {
                if (row.Code != (byte)FrontLogCode.Effect || row.A != (short)ProgrammeId.LaunchSatellite ||
                    row.B < 0 || row.B >= seen.Length) continue;
                float hostTime = row.Time - offset;
                if (!float.IsFinite(hostTime) || hostTime <= seen[row.B] + Tolerance) continue;
                seen[row.B] = hostTime;
                float age = now - row.Time;
                if (!history && age >= -Tolerance && age < MaxAge)
                {
                    pending |= (byte)(1 << row.B);
                    expires[row.B] = now + MaxAge - age;
                }
            }
            for (int i = 0; i < expires.Length; i++)
                if (now >= expires[i]) pending &= (byte)~(1 << i);
            if (!ready) return 0; // the independent OPS snapshot may still be bringing the live pad
            byte launches = pending;
            pending = 0;
            return launches;
        }
    }
}
