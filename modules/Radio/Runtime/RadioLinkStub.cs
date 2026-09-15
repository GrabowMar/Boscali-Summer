namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// The two knobs a real set has that this build cannot honour yet: a transmit key and a
    /// crypto switch. Both are wired to the panel so the receiver reads as a radio rather
    /// than a music player, and both say plainly what they are.
    ///
    /// <para>Deliberately inert. NORS-class voice needs mic capture, a second transport and
    /// a relay; until that exists, transmit is receive-only and SECURE is local fiction.
    /// The radio still sends nothing over the network and never will without an explicit
    /// handshake — see the module AGENTS.md.</para>
    /// </summary>
    internal sealed class RadioLinkStub
    {
        public const string TransmitStatus =
            "TX offline — receive-only build. Voice transmit arrives with the NORS-class link.";
        public const string SecureStatus =
            "NET LOCAL · crypto is a placeholder; no channel or key is transmitted.";

        public bool TransmitReady => false;
        public bool Secure { get; private set; }
        public int NetMembers => 1;
        public string NetName => "LOCAL";
        public float JamLevel => 0f;

        public bool CanTransmit() => false;

        public void ToggleSecure() => Secure = !Secure;

        public void Reset() => Secure = false;
    }
}
