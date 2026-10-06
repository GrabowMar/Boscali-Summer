using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The client's link to one faction mirror (CYBER, SOF or OPERATIONS): applies what the host pushes, and while a page that reads the mirror is
    /// on screen keeps asking the host for a state until one arrives (and again after a lost link).
    /// </summary>
    internal sealed class MirrorFeed<T> where T : FactionStateData<T>, new()
    {
        private readonly FactionMirror<T> mirror;
        private readonly SpaceCommandKind sync;
        private bool wanted;
        private float nextSync;

        public MirrorFeed(FactionMirror<T> mirror, SpaceCommandKind sync) { this.mirror = mirror; this.sync = sync; }

        public void Receive(T data)
        {
            if (data == null || data.Protocol != SupportNet.ProtocolVersion) return;
            mirror.Apply(data, SupportNet.ProtocolVersion, SupportManager.MissionNow());
        }

        public void Want(bool on)
        {
            if (on && !wanted) nextSync = 0f;
            wanted = on;
        }

        public void Update(SupportNet network)
        {
            if (!wanted || mirror.Known) return;
            float t = Time.unscaledTime;
            if (t < nextSync) return;
            nextSync = t + 2f;
            network.RequestSpace(new SpaceCommand(SupportNet.ProtocolVersion, sync, 0));
        }
    }
}
