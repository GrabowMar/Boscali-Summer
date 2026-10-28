using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Comms.Domain
{
    /// <summary>One line in the comms log.</summary>
    internal sealed class CommsFeedLine
    {
        public float Time;
        public ulong Author;
        public string AuthorName;
        public CommsFeedKind Kind;
        public CommsTone Tone;
        public CommsChannel Channel;
        public string Text;

        /// <summary>A position the line refers to, for the log's GO TO; NaN when it has none.</summary>
        public float X = float.NaN;
        public float Z = float.NaN;

        public bool HasPosition => !float.IsNaN(X) && !float.IsNaN(Z);
    }

    /// <summary>Something that just arrived and may deserve a HUD notice.</summary>
    internal struct CommsArrival
    {
        public CommsTone Tone;
        public string Text;

        /// <summary>The second line; the manager writes bearing and range instead when <see cref="HasPosition"/>.</summary>
        public string Detail;

        /// <summary>Worth the tick: a teammate's ping or an urgent call, never chatter or the other side.</summary>
        public bool Sound;

        /// <summary>World position the notice is about; NaN when it has none.</summary>
        public float X;
        public float Z;

        public bool HasPosition => !float.IsNaN(X) && !float.IsNaN(Z);
    }

    /// <summary>
    /// Everything one peer knows about COMMS: the part of the map its side may see and a log.
    /// It only ever changes by applying what the host sent, plus the peer's own mute list.
    /// Pure, so the whole receive path runs under test.
    /// </summary>
    internal sealed class CommsClientState
    {
        public const int MaxFeed = 60;

        /// <summary>How long after a map or cockpit request a refusal also goes to the HUD.</summary>
        public const float RefusalWatchSeconds = 4f;

        /// <summary>How old a replayed item pretends to be: past its pulse, so a resync never re-pulses the map.</summary>
        public const float ReplayedAgeSeconds = 3600f;

        private readonly List<CommsFeedLine> feed = new List<CommsFeedLine>();
        private readonly List<CommsArrival> arrivals = new List<CommsArrival>();
        private readonly HashSet<ulong> muted = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> seen = new Dictionary<ulong, string>();
        private float refusalWatchUntil = float.NegativeInfinity;

        public CommsBoard Board { get; } = new CommsBoard();

        /// <summary>This peer's own id; posts from it make no HUD noise.</summary>
        public ulong LocalId;

        /// <summary>This peer's side; an ALL post from any other side is announced as such, quietly.</summary>
        public int LocalFaction;

        public string Notice { get; private set; }
        public bool NoticeIsError { get; private set; }
        public float NoticeAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Moves whenever something the panel lists changes.</summary>
        public int Revision { get; private set; }
        public IReadOnlyList<CommsFeedLine> Feed => feed;

        /// <summary>Players this peer has heard from, by id, for the mute list.</summary>
        public IReadOnlyDictionary<ulong, string> Seen => seen;

        public bool IsMuted(ulong author) => author != LocalId && muted.Contains(author);

        public int MutedCount => muted.Count;

        public void ToggleMute(ulong author)
        {
            if (author == LocalId || author == 0UL) return;
            if (!muted.Remove(author)) muted.Add(author);
            Revision++;
        }

        public void Clear()
        {
            Board.Clear();
            feed.Clear();
            arrivals.Clear();
            seen.Clear();
            refusalWatchUntil = float.NegativeInfinity;
            Notice = null;
            NoticeAt = float.NegativeInfinity;
            Revision++;
        }

        /// <summary>
        /// This player changed sides: drop everything the old side could see now, rather than
        /// leaving its marks up until the new side's snapshot lands. The log stays; it is a
        /// record of what was heard.
        /// </summary>
        public void ForgetSide()
        {
            Board.Clear();
            Revision++;
        }

        /// <summary>Take the arrivals gathered since the last call.</summary>
        public void DrainArrivals(List<CommsArrival> into)
        {
            into.AddRange(arrivals);
            arrivals.Clear();
        }

        public void Tick(float now)
        {
            if (Board.Expire(now, null) > 0) Revision++;
        }

        public void Apply(CommsEnvelope e, float now)
        {
            // Reserved legacy wire values must not restore removed social content.
            if ((e.Event == CommsEvent.Item && e.Kind == (byte)CommsItemKind.Sticker) ||
                e.Event == CommsEvent.Poll || e.Event == CommsEvent.Rps ||
                e.Event == CommsEvent.Hunt || e.Event == CommsEvent.Scores ||
                (e.Event == CommsEvent.Feed && e.Kind != (byte)CommsFeedKind.Call && e.Kind != (byte)CommsFeedKind.System)) return;

            if (e.Author != 0UL && !string.IsNullOrEmpty(e.AuthorName) && e.Author != LocalId)
                seen[e.Author] = CommsText.Name(e.AuthorName);

            switch (e.Event)
            {
                case CommsEvent.Item: ApplyItem(e, now); break;
                case CommsEvent.Remove:
                    if (e.Ids != null)
                        for (int i = 0; i < e.Ids.Length; i++) Board.Remove(e.Ids[i]);
                    Revision++;
                    break;
                case CommsEvent.Reset:
                    Board.Clear();
                    Revision++;
                    break;
                case CommsEvent.Feed: ApplyFeed(e, now); break;
                case CommsEvent.Notice: ApplyNotice(e, now); break;
            }
        }

        /// <summary>
        /// A local message for the status strip: a refusal the peer made itself, or a tip. A
        /// refusal of something done on the map or in the cockpit also goes to the HUD, where
        /// the player is looking.
        /// </summary>
        public void SetNotice(string text, bool error, float now, bool hud = false)
        {
            Notice = CommsText.Clean(text, 60);
            NoticeIsError = error;
            NoticeAt = now;
            if (hud && error && Notice.Length > 0) Arrive(0UL, CommsTone.Caution, "COMMS · " + Notice, null, sound: false);
        }

        /// <summary>A map or cockpit request just went up: a refusal in the next few seconds belongs on the HUD.</summary>
        public void WatchRefusal(float now) => refusalWatchUntil = now + RefusalWatchSeconds;

        // ---- Events ------------------------------------------------------------------------

        private void ApplyNotice(CommsEnvelope e, float now)
        {
            if ((e.Flags & CommsFlags.Retry) != 0) return; // for the manager's resync, not for the player
            bool error = (e.Flags & CommsFlags.Self) == 0;
            bool hud = error && now <= refusalWatchUntil;
            if (hud) refusalWatchUntil = float.NegativeInfinity;
            SetNotice(e.Text, error, now, hud);
        }

        private void ApplyItem(CommsEnvelope e, float now)
        {
            if (e.Points == null || e.Points.Length < 2) return;
            bool isNew = Board.Find(e.Id) == null;
            bool replay = (e.Flags & CommsFlags.Replay) != 0;
            var item = new CommsItem
            {
                Id = e.Id,
                Kind = (CommsItemKind)e.Kind,
                Author = e.Author,
                AuthorName = CommsText.Name(e.AuthorName),
                Faction = e.Faction,
                Channel = e.Channel,
                Style = e.Style,
                Size = e.Size,
                Points = (int[])e.Points.Clone(),
                Text = CommsText.Clean(e.Text, CommsText.MaxLabel),
                Created = replay ? now - ReplayedAgeSeconds : now,
                Expires = now + Math.Max(0f, e.Ttl),
            };
            if (item.IsCall && e.Values != null && e.Values.Length > 0)
                item.Height = Math.Clamp(e.Values[0], 0, CommsAuthority.MaxCallAltitude);
            Board.Add(item, false, null);
            Revision++;

            // A call's own mark is announced by the call's line; a replay was announced before.
            if (!isNew || replay || item.IsCall || item.Kind != CommsItemKind.Ping || !CommsCatalog.ValidPing(item.Style)) return;
            PingKind kind = CommsCatalog.Pings[item.Style];
            bool foreign = Foreign(item.Channel, item.Faction);
            Push(new CommsFeedLine
            {
                Time = now,
                Author = item.Author,
                AuthorName = item.AuthorName,
                Kind = CommsFeedKind.Call,
                Tone = foreign ? CommsTone.Info : kind.Tone,
                Channel = item.Channel,
                Text = kind.Phrase + " · " + CommsText.Grid(item.X, item.Z),
                X = item.X,
                Z = item.Z,
            });
            if (foreign) Arrive(item.Author, CommsTone.Info, "[ALL] " + item.AuthorName + " · " + kind.Phrase, null, false, item.X, item.Z);
            else Arrive(item.Author, kind.Tone, item.AuthorName + " · " + kind.Phrase, null, true, item.X, item.Z);
        }

        private void ApplyFeed(CommsEnvelope e, float now)
        {
            string name = CommsText.Name(e.AuthorName);
            var line = new CommsFeedLine
            {
                Time = now,
                Author = e.Author,
                AuthorName = name,
                Kind = (CommsFeedKind)e.Kind,
                Channel = e.Channel,
            };
            if (e.Points != null && e.Points.Length >= 2)
            {
                line.X = StrokeCodec.Restore(e.Points[0]);
                line.Z = StrokeCodec.Restore(e.Points[1]);
            }

            switch (line.Kind)
            {
                case CommsFeedKind.Call:
                    if (!CommsCatalog.ValidCall(e.Style)) return;
                    BrevityCall call = CommsCatalog.Calls[e.Style];
                    bool foreign = Foreign(e.Channel, e.Faction);
                    line.Tone = foreign ? CommsTone.Info : call.Tone;
                    line.Text = call.Code + (line.HasPosition ? " · " + CommsText.Grid(line.X, line.Z) : "");
                    // The call speaks for its own mark: a located call's notice carries bearing
                    // and range to the caller, an unlocated one says what the code means.
                    if (foreign)
                        Arrive(e.Author, CommsTone.Info, "[ALL] " + name + " · " + call.Code, call.Meaning.ToUpperInvariant(), false, line.X, line.Z);
                    else
                        Arrive(e.Author, call.Tone, name + " · " + call.Code, call.Meaning.ToUpperInvariant(),
                            call.Tone == CommsTone.Caution || call.Tone == CommsTone.Danger, line.X, line.Z);
                    break;
                default:
                    line.Tone = CommsTone.Info;
                    line.Text = CommsText.Clean(e.Text, 60);
                    if (line.Text.Length == 0) return;
                    Arrive(e.Author, CommsTone.Caution, name + " · " + line.Text, null, sound: false);
                    break;
            }
            Push(line);
        }

        // ---- Helpers -----------------------------------------------------------------------

        private void Push(CommsFeedLine line)
        {
            feed.Add(line);
            if (feed.Count > MaxFeed) feed.RemoveRange(0, feed.Count - MaxFeed);
            Revision++;
        }

        private void Arrive(ulong author, CommsTone tone, string text, string detail, bool sound,
            float x = float.NaN, float z = float.NaN)
        {
            if (author != 0UL && (author == LocalId || muted.Contains(author))) return;
            arrivals.Add(new CommsArrival { Tone = tone, Text = text, Detail = detail, Sound = sound, X = x, Z = z });
            if (arrivals.Count > 16) arrivals.RemoveAt(0);
        }

        /// <summary>An ALL post from another side: heard, but neither loud nor in its own colours.</summary>
        public bool Foreign(CommsChannel channel, int faction) => channel == CommsChannel.All && faction != LocalFaction;
    }
}
