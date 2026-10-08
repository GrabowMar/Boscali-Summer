using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum ChatterScene : byte { Transit, AfterCombat, Loss, Recovery, Weather, Night }

    /// <summary>Quiet time earns an exchange; combat and loss leave silence before anyone speaks again.</summary>
    internal sealed class ChatterPacing
    {
        public float NextExchange { get; private set; }
        public ChatterScene Scene { get; private set; }
        private float quietAfter;

        public void Reset(float now, int seed)
        {
            Scene = ChatterScene.Transit;
            quietAfter = now;
            NextExchange = now + 55f + (uint)seed % 30;
        }

        public void Observe(float now, bool loss = false)
        {
            Scene = loss ? ChatterScene.Loss : Scene == ChatterScene.Loss ? Scene : ChatterScene.AfterCombat;
            Suppress(now, loss ? 45f : 25f);
        }

        public void Suppress(float now, float seconds = 25f)
        {
            quietAfter = Math.Max(quietAfter, now + seconds);
            NextExchange = Math.Max(NextExchange, quietAfter);
        }

        public bool Ready(float now, bool full, bool airborne, bool combat, bool channelBusy) =>
            full && airborne && !combat && !channelBusy && now >= quietAfter && now >= NextExchange;

        public void Scheduled(float now, int seed)
        {
            NextExchange = now + 85f + (uint)seed % 50;
            Scene = ChatterScene.Transit;
        }
    }
}
