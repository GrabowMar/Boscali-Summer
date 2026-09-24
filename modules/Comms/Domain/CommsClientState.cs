using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
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
        public ulong Author;
        public CommsTone Tone;
        public string Text;
        public string Detail;
        public bool Ping;
    }

    /// <summary>A map hunt as a peer sees it: open with a clock and a guess count, then revealed.</summary>
    internal sealed class HuntView
    {
        public uint Id;
        public ulong Author;
        public string AuthorName;
        public CommsChannel Channel;
        public float Ends;
        public int GuessCount;
        public bool Revealed;
        public float RevealUntil;
        public float HiddenX;
        public float HiddenZ;
        public int HiderPoints;
        public readonly List<HuntPlacingView> Placings = new List<HuntPlacingView>();

        /// <summary>
        /// This peer's own mark, shown only to them until the reveal: their guess, or — on
        /// the hider's own screen — where they hid the target.
        /// </summary>
        public bool HasLocalGuess;
        public float LocalGuessX;
        public float LocalGuessZ;
    }

    internal struct HuntPlacingView
    {
        public ulong Player;
        public string Name;
        public float X;
        public float Z;
        public int Metres;
        public int Points;
    }

    /// <summary>A rock-paper-scissors challenge as a peer sees it.</summary>
    internal sealed class DuelView
    {
        public uint Id;
        public ulong Challenger;
        public string ChallengerName;
        public CommsChannel Channel;
        public float Expires;
    }

    /// <summary>
    /// Everything one peer knows about COMMS: the part of the map its side may see, the polls
    /// and games it can join, the leaderboard, and a log. It only ever changes by applying what
    /// the host sent, plus the peer's own mute list and its optimistic poll and hunt choices.
    /// Pure, so the whole receive path runs under test.
    /// </summary>
    internal sealed class CommsClientState
    {
        public const int MaxFeed = 60;
        public const int MaxClosedPolls = 4;
        public const float RevealSeconds = 40f;
        public const float ClosedPollSeconds = 90f;

        private readonly List<CommsPoll> polls = new List<CommsPoll>();
        private readonly List<DuelView> duels = new List<DuelView>();
        private readonly List<HuntView> hunts = new List<HuntView>();
        private readonly List<CommsFeedLine> feed = new List<CommsFeedLine>();
        private readonly List<CommsArrival> arrivals = new List<CommsArrival>();
        private readonly HashSet<ulong> muted = new HashSet<ulong>();
        private readonly Dictionary<ulong, string> seen = new Dictionary<ulong, string>();
        private readonly Dictionary<uint, float> pollClosedAt = new Dictionary<uint, float>();
        private bool pendingHide;
        private float pendingHideX;
        private float pendingHideZ;

        public CommsBoard Board { get; } = new CommsBoard();
        public CommsScoreboard Scores { get; } = new CommsScoreboard();

        /// <summary>This peer's own id; posts from it make no HUD noise.</summary>
        public ulong LocalId;

        public string Notice { get; private set; }
        public bool NoticeIsError { get; private set; }
        public float NoticeAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Moves whenever something the panel lists changes.</summary>
        public int Revision { get; private set; }

        public IReadOnlyList<CommsPoll> Polls => polls;
        public IReadOnlyList<DuelView> Duels => duels;
        public IReadOnlyList<HuntView> Hunts => hunts;
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
            Scores.Clear();
            polls.Clear();
            duels.Clear();
            hunts.Clear();
            feed.Clear();
            arrivals.Clear();
            seen.Clear();
            pollClosedAt.Clear();
            Notice = null;
            NoticeAt = float.NegativeInfinity;
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
            for (int i = duels.Count - 1; i >= 0; i--)
            {
                if (duels[i].Expires > now) continue;
                duels.RemoveAt(i);
                Revision++;
            }
            for (int i = hunts.Count - 1; i >= 0; i--)
            {
                if (!hunts[i].Revealed || hunts[i].RevealUntil > now) continue;
                hunts.RemoveAt(i);
                Revision++;
            }
            for (int i = polls.Count - 1; i >= 0; i--)
            {
                CommsPoll poll = polls[i];
                if (!poll.Closed || !pollClosedAt.TryGetValue(poll.Id, out float at) || now - at < ClosedPollSeconds) continue;
                polls.RemoveAt(i);
                pollClosedAt.Remove(poll.Id);
                Revision++;
            }
        }

        public void Apply(CommsEnvelope e, float now)
        {
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
                    if ((e.Flags & CommsFlags.Snapshot) != 0)
                    {
                        polls.Clear();
                        duels.Clear();
                        pollClosedAt.Clear();
                        for (int i = hunts.Count - 1; i >= 0; i--)
                            if (!hunts[i].Revealed) hunts.RemoveAt(i);
                    }
                    Revision++;
                    break;
                case CommsEvent.Feed: ApplyFeed(e, now); break;
                case CommsEvent.Poll: ApplyPoll(e, now); break;
                case CommsEvent.Rps: ApplyDuel(e, now); break;
                case CommsEvent.Hunt: ApplyHunt(e, now); break;
                case CommsEvent.Scores: ApplyScores(e); break;
                case CommsEvent.Notice:
                    SetNotice(e.Text, (e.Flags & CommsFlags.Self) == 0, now);
                    break;
            }
        }

        /// <summary>A local message for the status strip: a refusal the peer made itself, or a tip.</summary>
        public void SetNotice(string text, bool error, float now)
        {
            Notice = CommsText.Clean(text, 60);
            NoticeIsError = error;
            NoticeAt = now;
        }

        /// <summary>Remember this peer's choice at click time; the host's tally follows.</summary>
        public void NoteLocalVote(uint pollId, int option)
        {
            for (int i = 0; i < polls.Count; i++)
                if (polls[i].Id == pollId && !polls[i].Closed) polls[i].LocalVote = option;
            Revision++;
        }

        public void NoteLocalGuess(uint huntId, float x, float z)
        {
            HuntView hunt = FindHunt(huntId);
            if (hunt == null || hunt.Revealed) return;
            hunt.HasLocalGuess = true;
            hunt.LocalGuessX = x;
            hunt.LocalGuessZ = z;
            Revision++;
        }

        /// <summary>
        /// Remember where this peer just hid a target: the host never sends the point back
        /// before the reveal, so the hider's own marker comes from here.
        /// </summary>
        public void NoteLocalHide(float x, float z)
        {
            pendingHide = true;
            pendingHideX = x;
            pendingHideZ = z;
        }

        public HuntView FindHunt(uint id)
        {
            for (int i = 0; i < hunts.Count; i++)
                if (hunts[i].Id == id) return hunts[i];
            return null;
        }

        public CommsPoll FindPoll(uint id)
        {
            for (int i = 0; i < polls.Count; i++)
                if (polls[i].Id == id) return polls[i];
            return null;
        }

        public void AddLocalLine(CommsFeedKind kind, CommsTone tone, string text, float now)
        {
            Push(new CommsFeedLine
            {
                Time = now,
                Author = LocalId,
                AuthorName = "YOU",
                Kind = kind,
                Tone = tone,
                Text = text,
            });
        }

        // ---- Events ------------------------------------------------------------------------

        private void ApplyItem(CommsEnvelope e, float now)
        {
            if (e.Points == null || e.Points.Length < 2) return;
            bool isNew = Board.Find(e.Id) == null;
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
                Created = now,
                Expires = now + Math.Max(0f, e.Ttl),
            };
            Board.Add(item, false, null);
            Revision++;

            if (!isNew || item.Kind != CommsItemKind.Ping || !CommsCatalog.ValidPing(item.Style)) return;
            PingKind kind = CommsCatalog.Pings[item.Style];
            string grid = CommsText.Grid(item.X, item.Z);
            Push(new CommsFeedLine
            {
                Time = now,
                Author = item.Author,
                AuthorName = item.AuthorName,
                Kind = CommsFeedKind.Call,
                Tone = kind.Tone,
                Channel = item.Channel,
                Text = kind.Phrase + " · " + grid,
                X = item.X,
                Z = item.Z,
            });
            Arrive(item.Author, kind.Tone, item.AuthorName + " · " + kind.Phrase, grid, ping: true);
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
                    line.Tone = call.Tone;
                    line.Text = call.Code + (line.HasPosition ? " · " + CommsText.Grid(line.X, line.Z) : "");
                    // A located call already announces itself through its ping on the HUD.
                    if (!line.HasPosition)
                        Arrive(e.Author, call.Tone, name + " · " + call.Code, call.Meaning.ToUpperInvariant(), ping: false);
                    break;
                case CommsFeedKind.Roll:
                    if (e.Values == null || e.Values.Length < 2) return;
                    int sides = e.Values[0], value = e.Values[1];
                    line.Tone = CommsTone.Fun;
                    line.Text = sides == 2
                        ? "FLIPPED A COIN: " + CommsCatalog.DiceFace(sides, value)
                        : "ROLLED " + value + " ON A D" + sides + (value == sides ? " — NATURAL!" : value == 1 && sides > 2 ? " — OUCH" : "");
                    break;
                default:
                    line.Tone = CommsTone.Info;
                    line.Text = CommsText.Clean(e.Text, 60);
                    if (line.Text.Length == 0) return;
                    Arrive(e.Author, CommsTone.Caution, name + " · " + line.Text, null, ping: false);
                    break;
            }
            Push(line);
        }

        private void ApplyPoll(CommsEnvelope e, float now)
        {
            if (e.Items == null || e.Items.Length < CommsPoll.MinOptions) return;
            CommsPoll poll = FindPoll(e.Id);
            bool isNew = poll == null;
            bool wasClosed = poll != null && poll.Closed;
            if (isNew)
            {
                poll = new CommsPoll { Id = e.Id };
                polls.Add(poll);
            }

            int count = Math.Min(e.Items.Length, CommsPoll.MaxOptions);
            poll.Author = e.Author;
            poll.AuthorName = CommsText.Name(e.AuthorName);
            poll.Faction = e.Faction;
            poll.Channel = e.Channel;
            poll.Question = CommsText.Clean(e.Text, CommsText.MaxQuestion);
            poll.Options = new string[count];
            poll.Tally = new int[count];
            for (int i = 0; i < count; i++)
            {
                poll.Options[i] = CommsText.Clean(e.Items[i], CommsText.MaxOption);
                poll.Tally[i] = e.Values != null && i < e.Values.Length ? Math.Max(0, e.Values[i]) : 0;
            }
            poll.Closes = now + Math.Max(0f, e.Ttl);
            poll.Closed = (e.Flags & CommsFlags.Closed) != 0;
            if (e.Style > 0 && e.Style <= count) poll.LocalVote = e.Style - 1;
            Revision++;

            if (isNew && !poll.Closed)
            {
                Push(new CommsFeedLine
                {
                    Time = now, Author = poll.Author, AuthorName = poll.AuthorName, Kind = CommsFeedKind.Poll,
                    Tone = CommsTone.Info, Channel = poll.Channel, Text = "ASKS: " + poll.Question,
                });
                Arrive(poll.Author, CommsTone.Caution, "POLL · " + poll.Question, "OPEN COM › POLL TO VOTE", ping: false);
            }
            if (poll.Closed && !wasClosed)
            {
                pollClosedAt[poll.Id] = now;
                Push(new CommsFeedLine
                {
                    Time = now, Author = poll.Author, AuthorName = poll.AuthorName, Kind = CommsFeedKind.Poll,
                    Tone = CommsTone.Friendly, Channel = poll.Channel,
                    Text = "POLL CLOSED: " + poll.Question + " → " + poll.Verdict(),
                });
                Arrive(poll.Author, CommsTone.Info, "POLL RESULT · " + poll.Verdict(), poll.Question, ping: false);
                TrimClosedPolls();
            }
        }

        private void ApplyDuel(CommsEnvelope e, float now)
        {
            int index = -1;
            for (int i = 0; i < duels.Count; i++)
                if (duels[i].Id == e.Id) index = i;
            string name = CommsText.Name(e.AuthorName);

            if ((e.Flags & CommsFlags.Lapsed) != 0)
            {
                if (index >= 0) duels.RemoveAt(index);
                Revision++;
                return;
            }

            if ((e.Flags & CommsFlags.Closed) != 0)
            {
                if (index >= 0) duels.RemoveAt(index);
                Revision++;
                if (e.Values == null || e.Values.Length < 3 || e.Items == null || e.Items.Length < 2) return;
                int a = e.Values[0], b = e.Values[1], outcome = e.Values[2];
                if (!CommsCatalog.ValidThrow(a) || !CommsCatalog.ValidThrow(b)) return;
                string left = CommsText.Name(e.Items[0]), right = CommsText.Name(e.Items[1]);
                string text = outcome == 0
                    ? "RPS DRAW: " + left + " AND " + right + " BOTH THREW " + CommsCatalog.Throws[a]
                    : outcome > 0
                        ? "RPS: " + left + " (" + CommsCatalog.Throws[a] + ") BEATS " + right + " (" + CommsCatalog.Throws[b] + ")"
                        : "RPS: " + right + " (" + CommsCatalog.Throws[b] + ") BEATS " + left + " (" + CommsCatalog.Throws[a] + ")";
                Push(new CommsFeedLine
                {
                    Time = now, Author = e.Author, AuthorName = name, Kind = CommsFeedKind.Duel,
                    Tone = CommsTone.Fun, Channel = e.Channel, Text = text,
                });
                bool involved = e.Players != null && Array.IndexOf(e.Players, LocalId) >= 0;
                if (involved) Arrive(0UL, CommsTone.Info, text, null, ping: false);
                return;
            }

            if (index >= 0)
            {
                duels[index].Expires = now + Math.Max(0f, e.Ttl);
                Revision++;
                return;
            }
            duels.Add(new DuelView
            {
                Id = e.Id,
                Challenger = e.Author,
                ChallengerName = name,
                Channel = e.Channel,
                Expires = now + Math.Max(0f, e.Ttl),
            });
            Revision++;
            Push(new CommsFeedLine
            {
                Time = now, Author = e.Author, AuthorName = name, Kind = CommsFeedKind.Duel,
                Tone = CommsTone.Fun, Channel = e.Channel, Text = "THROWS DOWN A ROCK-PAPER-SCISSORS CHALLENGE",
            });
            Arrive(e.Author, CommsTone.Info, name + " CHALLENGES YOU · RPS", "OPEN COM › GAME TO ANSWER", ping: false);
        }

        private void ApplyHunt(CommsEnvelope e, float now)
        {
            HuntView hunt = FindHunt(e.Id);
            string name = CommsText.Name(e.AuthorName);
            bool isNew = hunt == null;
            if (isNew)
            {
                if ((e.Flags & CommsFlags.Revealed) == 0 && e.Ttl <= 0f) return;
                hunt = new HuntView { Id = e.Id };
                hunts.Add(hunt);
            }
            hunt.Author = e.Author;
            hunt.AuthorName = name;
            hunt.Channel = e.Channel;

            if ((e.Flags & CommsFlags.Revealed) == 0)
            {
                hunt.Ends = now + Math.Max(0f, e.Ttl);
                hunt.GuessCount = e.Values != null && e.Values.Length > 0 ? Math.Max(0, e.Values[0]) : 0;
                Revision++;
                if (!isNew) return;
                if (e.Author == LocalId && pendingHide)
                {
                    pendingHide = false;
                    hunt.HasLocalGuess = true;
                    hunt.LocalGuessX = pendingHideX;
                    hunt.LocalGuessZ = pendingHideZ;
                }
                Push(new CommsFeedLine
                {
                    Time = now, Author = e.Author, AuthorName = name, Kind = CommsFeedKind.Hunt, Tone = CommsTone.Fun,
                    Channel = e.Channel, Text = "HID A TARGET — FIND IT ON THE MAP (" + CommsText.Countdown(e.Ttl) + ")",
                });
                Arrive(e.Author, CommsTone.Info, "MAP HUNT · " + name + " HID A TARGET", "OPEN COM › GAME TO GUESS", ping: false);
                return;
            }

            if (hunt.Revealed) return;
            hunt.Revealed = true;
            hunt.RevealUntil = now + RevealSeconds;
            hunt.Ends = now;
            hunt.HiderPoints = e.Size;
            hunt.Placings.Clear();
            if (e.Points != null && e.Points.Length >= 2)
            {
                hunt.HiddenX = StrokeCodec.Restore(e.Points[0]);
                hunt.HiddenZ = StrokeCodec.Restore(e.Points[1]);
                int count = (e.Points.Length - 2) / 2;
                for (int i = 0; i < count; i++)
                {
                    hunt.Placings.Add(new HuntPlacingView
                    {
                        Player = e.Players != null && i < e.Players.Length ? e.Players[i] : 0UL,
                        Name = CommsText.Name(e.Items != null && i < e.Items.Length ? e.Items[i] : null),
                        X = StrokeCodec.Restore(e.Points[2 + i * 2]),
                        Z = StrokeCodec.Restore(e.Points[3 + i * 2]),
                        Metres = e.Values != null && i * 2 < e.Values.Length ? e.Values[i * 2] : 0,
                        Points = e.Values != null && i * 2 + 1 < e.Values.Length ? e.Values[i * 2 + 1] : 0,
                    });
                }
            }
            hunt.GuessCount = hunt.Placings.Count;
            Revision++;

            string text = hunt.Placings.Count == 0
                ? "NOBODY GUESSED " + name + "'S TARGET"
                : hunt.Placings[0].Name + " FOUND " + name + "'S TARGET — " +
                  CommsText.Distance(hunt.Placings[0].Metres, true) + " OFF" +
                  (hunt.HiderPoints > 0 ? " (HIDER WINS " + hunt.HiderPoints + ")" : "");
            Push(new CommsFeedLine
            {
                Time = now, Author = e.Author, AuthorName = name, Kind = CommsFeedKind.Hunt, Tone = CommsTone.Fun,
                Channel = e.Channel, Text = "HUNT OVER: " + text, X = hunt.HiddenX, Z = hunt.HiddenZ,
            });
            Arrive(0UL, CommsTone.Info, "HUNT OVER · " + text, null, ping: false);
        }

        private void ApplyScores(CommsEnvelope e)
        {
            var table = new List<ScoreRow>();
            int count = e.Players == null ? 0 : e.Players.Length;
            for (int i = 0; i < count && i < CommsScoreboard.MaxRows; i++)
            {
                table.Add(new ScoreRow
                {
                    Player = e.Players[i],
                    Name = CommsText.Name(e.Items != null && i < e.Items.Length ? e.Items[i] : null),
                    Points = e.Values != null && i * 2 < e.Values.Length ? e.Values[i * 2] : 0,
                    Wins = e.Values != null && i * 2 + 1 < e.Values.Length ? e.Values[i * 2 + 1] : 0,
                });
            }
            Scores.Set(table);
            Revision++;
        }

        // ---- Helpers -----------------------------------------------------------------------

        private void Push(CommsFeedLine line)
        {
            feed.Add(line);
            if (feed.Count > MaxFeed) feed.RemoveRange(0, feed.Count - MaxFeed);
            Revision++;
        }

        private void Arrive(ulong author, CommsTone tone, string text, string detail, bool ping)
        {
            if (author != 0UL && (author == LocalId || muted.Contains(author))) return;
            arrivals.Add(new CommsArrival { Author = author, Tone = tone, Text = text, Detail = detail, Ping = ping });
            if (arrivals.Count > 16) arrivals.RemoveAt(0);
        }

        private void TrimClosedPolls()
        {
            int closed = 0;
            for (int i = polls.Count - 1; i >= 0; i--)
            {
                if (!polls[i].Closed) continue;
                if (++closed <= MaxClosedPolls) continue;
                pollClosedAt.Remove(polls[i].Id);
                polls.RemoveAt(i);
            }
        }
    }
}
