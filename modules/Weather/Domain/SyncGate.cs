namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>What a client does with one weather sync message (spec 9.4, 9.5).</summary>
    internal enum SyncDecision : byte
    {
        /// <summary>The client has not captured its own baseline yet: hold the newest message and replay it after the capture.</summary>
        Buffer,
        /// <summary>A server never applies a sync, and a moving key from a host whose mission clock is not within 10 s of ours is not adopted (the next resend retries).</summary>
        Ignore,
        /// <summary>Build the key from the message fields and keep it.</summary>
        Adopt,
    }

    /// <summary>The client handshake as one pure decision, so the order of its guards is tested: server first, then capture, then the clock rule of <see cref="WeatherKeyWire.CanAdopt"/>.</summary>
    internal static class SyncGate
    {
        public static SyncDecision Decide(bool server, bool clientCaptured, bool dynamic, bool haveClock, double localMissionTime, uint messageSeconds)
        {
            if (server) return SyncDecision.Ignore;
            if (!clientCaptured) return SyncDecision.Buffer;
            return WeatherKeyWire.CanAdopt(dynamic, haveClock, localMissionTime, messageSeconds) ? SyncDecision.Adopt : SyncDecision.Ignore;
        }
    }

    /// <summary>
    /// The one message a client holds while it waits for its own capture: the newest wins (the host re-sends every 8 s, so an older key is stale), and it is handed out once.
    /// A scene reset clears it, so a message from the last mission is never replayed into the next.
    /// </summary>
    internal sealed class PendingSync<T> where T : struct
    {
        private T held;
        private bool has;

        public bool Has => has;

        public void Hold(T message) { held = message; has = true; }

        public bool TryTake(out T message)
        {
            message = held;
            if (!has) return false;
            has = false;
            held = default;
            return true;
        }

        public void Clear() { has = false; held = default; }
    }
}
