using System;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>What a peer asks the host to do. Values are wire bytes; append only.</summary>
    internal enum CommsOp : byte
    {
        /// <summary>Put an item on the map. Kind picks ping, sticker, stroke or label.</summary>
        Place = 0,

        /// <summary>Remove one of your own items (the host may remove anyone's).</summary>
        Erase = 1,

        /// <summary>Remove all of your own items.</summary>
        ClearMine = 2,

        /// <summary>Host only: wipe the whole board.</summary>
        ClearAll = 3,

        /// <summary>A brevity call, optionally with the caller's own position.</summary>
        Call = 4,

        PollCreate = 5,
        PollVote = 6,
        PollClose = 7,

        /// <summary>Roll a coin or a die; the host's dice, so nobody can load them.</summary>
        Roll = 8,

        RpsChallenge = 9,
        RpsAccept = 10,

        /// <summary>Hide a point for a map hunt.</summary>
        HuntStart = 11,

        HuntGuess = 12,

        /// <summary>Send me everything I should be able to see: on join and after a side change.</summary>
        Sync = 13,

        /// <summary>Withdraw your own open rock-paper-scissors challenge.</summary>
        RpsCancel = 14,
    }

    /// <summary>
    /// One request from a peer. A single flexible shape rather than one message per verb:
    /// the transport serialises it once, and each verb reads only the fields it needs.
    /// </summary>
    internal struct CommsIntent
    {
        public CommsOp Op;
        public CommsChannel Channel;

        /// <summary>Item kind for <see cref="CommsOp.Place"/>.</summary>
        public byte Kind;

        /// <summary>Ping kind, sticker, pen ink, call, option, die, throw — by verb.</summary>
        public byte Style;

        /// <summary>Stroke width, poll or hunt duration index — by verb.</summary>
        public byte Size;

        /// <summary>The item, poll, challenge or hunt the verb acts on.</summary>
        public uint Target;

        /// <summary>Quantised interleaved points.</summary>
        public int[] Points;

        public string Text;
        public string[] Items;
    }

    /// <summary>What the host tells a peer. Values are wire bytes; append only.</summary>
    internal enum CommsEvent : byte
    {
        /// <summary>An item appeared or changed. Carries the whole item.</summary>
        Item = 0,

        /// <summary>Items went away. <see cref="CommsEnvelope.Ids"/> lists them.</summary>
        Remove = 1,

        /// <summary>Forget the board: the host wiped it, or a snapshot follows.</summary>
        Reset = 2,

        /// <summary>A line for the comms log: calls, rolls, and verdicts.</summary>
        Feed = 3,

        /// <summary>A poll's full state.</summary>
        Poll = 4,

        /// <summary>A rock-paper-scissors challenge opened, was taken, or lapsed.</summary>
        Rps = 5,

        /// <summary>A map hunt opened, gained a guess, or was revealed.</summary>
        Hunt = 6,

        /// <summary>The whole leaderboard.</summary>
        Scores = 7,

        /// <summary>Only to the requester: why a request was refused, or a quiet confirmation.</summary>
        Notice = 8,
    }

    /// <summary>
    /// One message down from the host, as flexible as <see cref="CommsIntent"/>. Every field
    /// has a meaning per event and a harmless default otherwise.
    /// </summary>
    internal struct CommsEnvelope
    {
        public CommsEvent Event;
        public uint Id;
        public ulong Author;
        public string AuthorName;
        public int Faction;
        public CommsChannel Channel;
        public byte Kind;
        public byte Style;
        public byte Size;

        /// <summary>State bits, by event: closed, accepted, revealed, lapsed…</summary>
        public byte Flags;

        /// <summary>Seconds left to live on the host's clock when the message left.</summary>
        public float Ttl;

        public int[] Points;
        public string Text;
        public string[] Items;
        public int[] Values;
        public ulong[] Players;
        public uint[] Ids;
    }

    /// <summary>Flag bits carried in <see cref="CommsEnvelope.Flags"/>.</summary>
    internal static class CommsFlags
    {
        public const byte Closed = 1;
        public const byte Lapsed = 2;
        public const byte Revealed = 4;
        public const byte Self = 8;

        /// <summary>On a reset: a snapshot follows, so forget polls and games as well as the map.</summary>
        public const byte Snapshot = 16;
    }

    /// <summary>Feed line categories; they pick the rail colour and the HUD channel's tone.</summary>
    internal enum CommsFeedKind : byte
    {
        Call = 0,
        Roll = 1,
        Poll = 2,
        Duel = 3,
        Hunt = 4,
        System = 5,
    }

    /// <summary>Who a host message goes to.</summary>
    internal enum CommsRoute : byte
    {
        /// <summary>Every player whose side matches <see cref="CommsOutbound.Faction"/>.</summary>
        Faction = 0,

        /// <summary>Every player.</summary>
        All = 1,

        /// <summary>One player: <see cref="CommsOutbound.Player"/>.</summary>
        One = 2,
    }

    /// <summary>A host message and its audience, before the transport resolves players.</summary>
    internal struct CommsOutbound
    {
        public CommsRoute Route;
        public int Faction;
        public ulong Player;
        public CommsEnvelope Envelope;

        public static CommsOutbound For(CommsChannel channel, int faction, CommsEnvelope envelope) =>
            new CommsOutbound
            {
                Route = channel == CommsChannel.All ? CommsRoute.All : CommsRoute.Faction,
                Faction = faction,
                Envelope = envelope,
            };

        public static CommsOutbound To(ulong player, CommsEnvelope envelope) =>
            new CommsOutbound { Route = CommsRoute.One, Player = player, Envelope = envelope };

        public static CommsOutbound Everyone(CommsEnvelope envelope) =>
            new CommsOutbound { Route = CommsRoute.All, Envelope = envelope };

        /// <summary>Whether a recipient with this id and side is in the audience.</summary>
        public bool Reaches(ulong player, int faction)
        {
            switch (Route)
            {
                case CommsRoute.All: return true;
                case CommsRoute.One: return player == Player;
                default: return faction == Faction;
            }
        }
    }

    /// <summary>The sender of an intent, as the host resolved it: never as the peer claimed.</summary>
    internal struct CommsSender
    {
        public ulong Id;
        public string Name;
        public int Faction;

        /// <summary>The server's own player (or the server itself): may moderate the board.</summary>
        public bool Moderator;
    }

    /// <summary>
    /// The host-authoritative knobs, as plain values so the rules run under test. The live
    /// feature fills this from its BepInEx settings each tick.
    /// </summary>
    internal struct CommsRules
    {
        public float PingSeconds;
        public float StickerSeconds;
        public float DrawingSeconds;
        public bool AllowAllChannel;
        public bool AllowDrawing;
        public bool AllowGames;

        public static CommsRules Default => new CommsRules
        {
            PingSeconds = 60f,
            StickerSeconds = 900f,
            DrawingSeconds = 1200f,
            AllowAllChannel = true,
            AllowDrawing = true,
            AllowGames = true,
        };

        public float LifetimeOf(CommsItemKind kind)
        {
            switch (kind)
            {
                case CommsItemKind.Ping: return Math.Max(5f, PingSeconds);
                case CommsItemKind.Stroke: return Math.Max(10f, DrawingSeconds);
                default: return Math.Max(10f, StickerSeconds);
            }
        }
    }
}
