namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>
    /// The receiver's link state: everything a real set shows that this build cannot yet do.
    ///
    /// <para>Deliberately inert, and read by the panel so the placeholders are visible rather
    /// than implied. The three that will need a real implementation are voice receive (and the
    /// deck duck that goes with it), the peer/relay session, and the crypto switch. Any of them
    /// arriving means an explicit handshake, an authority story and a bounded wire contract
    /// first — the module still sends nothing today, and must not grow a network path from
    /// this file alone.</para>
    /// </summary>
    internal sealed class RadioLinkStub
    {
        public const string TransmitStatus =
            "TX offline — receive-only build. Voice transmit arrives with the NORS-class link.";
        public const string SecureStatus =
            "NET LOCAL · crypto is a placeholder; no channel or key is transmitted.";
        public const string NetStatus = "LOCAL NET · NO PEERS";
        public const string JamStatus = "NO JAMMING DETECTED";

        /// <summary>Deck attenuation while another player is transmitting at us.</summary>
        public const float TransmissionDuck = 0.35f;

        public bool TransmitReady => false;
        public bool Secure { get; private set; }
        public int NetMembers => 1;

        /// <summary>
        /// Future voice receive: true while a transmission addressed to this player is being
        /// played. Nothing sets it yet, so the deck's duck path is wired but never fires.
        /// </summary>
        public bool TransmissionActive => false;

        public float JamLevel => 0f;

        public bool CanTransmit() => false;

        public void ToggleSecure() => Secure = !Secure;

        public void Reset() => Secure = false;

        /// <summary>Pure so the duck rule is assertable before it has a trigger.</summary>
        public static float DeckGain(bool transmissionActive) =>
            transmissionActive ? TransmissionDuck : 1f;
    }
}
