using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>
    /// The host's side of COMMS, with no Unity and no transport in it: a peer's request goes
    /// in with the identity the host resolved for it, and routed messages come out. The
    /// transport only has to turn each <see cref="CommsOutbound"/> into sends.
    ///
    /// <para>Everything a peer could lie about is checked here: the verb, every index, every
    /// coordinate, every string, who may erase or close what, who may vote or guess where,
    /// and how fast. A refused request earns the sender one quiet notice and nothing else.
    /// Team posts only ever route to the poster's side, which is what keeps a SAM ping or
    /// an attack arrow from being intelligence for the other team.</para>
    /// </summary>
    internal sealed class CommsAuthority
    {
        public const int MaxDuels = 6;
        public const int MaxHunts = 2;
        public const float DuelSeconds = 60f;

        /// <summary>Most map items one snapshot carries.</summary>
        public const int SnapshotItems = 200;

        private readonly CommsBoard board = new CommsBoard();
        private readonly CommsPollBook polls = new CommsPollBook();
        private readonly List<RpsChallenge> duels = new List<RpsChallenge>();
        private readonly List<HuntRound> hunts = new List<HuntRound>();
        private readonly CommsScoreboard scores = new CommsScoreboard();
        private readonly TokenBucket bucket = new TokenBucket(16f, 2.5f);
        private readonly List<uint> scratchIds = new List<uint>();
        private readonly List<CommsPoll> scratchPolls = new List<CommsPoll>();
        private readonly Random random;
        private uint nextId = 1;

        public CommsRules Rules = CommsRules.Default;

        public CommsAuthority(int seed)
        {
            random = new Random(seed);
        }

        public CommsBoard Board => board;
        public IReadOnlyList<CommsPoll> Polls => polls.Open;
        public IReadOnlyList<RpsChallenge> Duels => duels;
        public IReadOnlyList<HuntRound> Hunts => hunts;
        public CommsScoreboard Scores => scores;

        public void Reset()
        {
            board.Clear();
            polls.Clear();
            duels.Clear();
            hunts.Clear();
            scores.Clear();
            bucket.Clear();
            nextId = 1;
        }

        // ---- Requests ----------------------------------------------------------------------

        public void Handle(CommsSender sender, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (output == null) return;
            if (!bucket.TryTake(sender.Id, now, Cost(intent.Op)))
            {
                Refuse(sender, "SLOW DOWN — TOO MANY COMMS", output);
                return;
            }

            string name = CommsText.Name(sender.Name);
            switch (intent.Op)
            {
                case CommsOp.Place: Place(sender, name, intent, now, output); break;
                case CommsOp.Erase: Erase(sender, intent.Target, output); break;
                case CommsOp.ClearMine: ClearMine(sender, output); break;
                case CommsOp.ClearAll: ClearAll(sender, name, output); break;
                case CommsOp.Call: Call(sender, name, intent, now, output); break;
                case CommsOp.PollCreate: CreatePoll(sender, name, intent, now, output); break;
                case CommsOp.PollVote: Vote(sender, intent, now, output); break;
                case CommsOp.PollClose: ClosePoll(sender, intent.Target, now, output); break;
                case CommsOp.Roll: Roll(sender, name, intent, output); break;
                case CommsOp.RpsChallenge: Challenge(sender, name, intent, now, output); break;
                case CommsOp.RpsAccept: Accept(sender, name, intent, now, output); break;
                case CommsOp.RpsCancel: CancelDuel(sender, intent.Target, now, output); break;
                case CommsOp.HuntStart: StartHunt(sender, name, intent, now, output); break;
                case CommsOp.HuntGuess: Guess(sender, name, intent, now, output); break;
                case CommsOp.Sync: Snapshot(sender, now, output); break;
                default: Refuse(sender, "UNKNOWN COMMS REQUEST", output); break;
            }
        }

        /// <summary>
        /// Close what has timed out. Items are not announced — every peer expires them on its
        /// own clock from the time-to-live it was given — but polls, duels and hunts end with
        /// a verdict someone wants to read.
        /// </summary>
        public void Tick(float now, List<CommsOutbound> output)
        {
            board.Expire(now, null);

            scratchPolls.Clear();
            polls.Expire(now, scratchPolls);
            for (int i = 0; i < scratchPolls.Count; i++)
                output?.Add(CommsOutbound.For(scratchPolls[i].Channel, scratchPolls[i].Faction,
                    PollEnvelope(scratchPolls[i], now)));

            for (int i = duels.Count - 1; i >= 0; i--)
            {
                if (duels[i].Expires > now) continue;
                RpsChallenge duel = duels[i];
                duels.RemoveAt(i);
                output?.Add(CommsOutbound.For(duel.Channel, duel.Faction,
                    DuelEnvelope(duel, now, CommsFlags.Lapsed)));
            }

            for (int i = hunts.Count - 1; i >= 0; i--)
            {
                if (hunts[i].Ends > now) continue;
                HuntRound hunt = hunts[i];
                hunts.RemoveAt(i);
                Reveal(hunt, now, output);
            }
        }

        /// <summary>Everything this viewer may see, addressed to them alone, after a reset.</summary>
        public void Snapshot(CommsSender viewer, float now, List<CommsOutbound> output)
        {
            output.Add(CommsOutbound.To(viewer.Id, new CommsEnvelope { Event = CommsEvent.Reset, Flags = CommsFlags.Snapshot }));

            // The newest items the viewer may see, bounded so a join never bursts the whole
            // board down one reliable channel, then sent oldest first so UNDO order survives.
            IReadOnlyList<CommsItem> items = board.Items;
            int first = items.Count;
            for (int visible = 0; first > 0 && visible < SnapshotItems; first--)
                if (CommsBoard.Visible(items[first - 1].Channel, items[first - 1].Faction, viewer.Faction)) visible++;
            for (int i = first; i < items.Count; i++)
                if (CommsBoard.Visible(items[i].Channel, items[i].Faction, viewer.Faction))
                    output.Add(CommsOutbound.To(viewer.Id, ItemEnvelope(items[i], now)));
            IReadOnlyList<CommsPoll> open = polls.Open;
            for (int i = 0; i < open.Count; i++)
                if (CommsBoard.Visible(open[i].Channel, open[i].Faction, viewer.Faction))
                    output.Add(CommsOutbound.To(viewer.Id, PollEnvelope(open[i], now, viewer.Id)));
            for (int i = 0; i < duels.Count; i++)
                if (CommsBoard.Visible(duels[i].Channel, duels[i].Faction, viewer.Faction))
                    output.Add(CommsOutbound.To(viewer.Id, DuelEnvelope(duels[i], now, 0)));
            for (int i = 0; i < hunts.Count; i++)
                if (CommsBoard.Visible(hunts[i].Channel, hunts[i].Faction, viewer.Faction))
                    output.Add(CommsOutbound.To(viewer.Id, HuntEnvelope(hunts[i], now)));
            output.Add(CommsOutbound.To(viewer.Id, ScoresEnvelope()));
        }

        // ---- Map ---------------------------------------------------------------------------

        private void Place(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!ChannelAllowed(sender, intent.Channel, output)) return;

            var kind = (CommsItemKind)intent.Kind;
            string text = "";
            switch (kind)
            {
                case CommsItemKind.Ping:
                    if (!CommsCatalog.ValidPing(intent.Style) || !StrokeCodec.Valid(intent.Points, 1, 1))
                    {
                        Refuse(sender, "BAD PING", output);
                        return;
                    }
                    break;
                case CommsItemKind.Sticker:
                    if (!CommsCatalog.ValidSticker(intent.Style) || !StrokeCodec.Valid(intent.Points, 1, 1))
                    {
                        Refuse(sender, "BAD STICKER", output);
                        return;
                    }
                    break;
                case CommsItemKind.Label:
                    text = CommsText.Clean(intent.Text, CommsText.MaxLabel);
                    if (text.Length == 0 || !StrokeCodec.Valid(intent.Points, 1, 1))
                    {
                        Refuse(sender, "TYPE A LABEL FIRST", output);
                        return;
                    }
                    break;
                case CommsItemKind.Stroke:
                    if (!Rules.AllowDrawing)
                    {
                        Refuse(sender, "HOST HAS DRAWING OFF", output);
                        return;
                    }
                    if (!CommsCatalog.ValidPen(intent.Style) || !CommsCatalog.ValidWidth(intent.Size) ||
                        !StrokeCodec.Valid(intent.Points, 2, StrokeCodec.MaxPoints))
                    {
                        Refuse(sender, "BAD STROKE", output);
                        return;
                    }
                    break;
                default:
                    Refuse(sender, "UNKNOWN MAP ITEM", output);
                    return;
            }

            var item = new CommsItem
            {
                Id = NextId(),
                Kind = kind,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Style = intent.Style,
                Size = kind == CommsItemKind.Stroke ? intent.Size : (byte)0,
                Points = (int[])intent.Points.Clone(),
                Text = text,
                Created = now,
                Expires = now + Rules.LifetimeOf(kind),
            };
            Store(item, now, output);
        }

        private void Store(CommsItem item, float now, List<CommsOutbound> output)
        {
            scratchIds.Clear();
            board.Add(item, true, scratchIds);
            output.Add(CommsOutbound.For(item.Channel, item.Faction, ItemEnvelope(item, now)));
            if (scratchIds.Count > 0) output.Add(CommsOutbound.Everyone(RemoveEnvelope(scratchIds)));
        }

        private void Erase(CommsSender sender, uint id, List<CommsOutbound> output)
        {
            CommsItem item = board.Find(id);
            if (item == null) return; // already gone: expired, or erased twice
            if (item.Author != sender.Id && !sender.Moderator)
            {
                Refuse(sender, "ONLY THE AUTHOR OR HOST CAN ERASE THAT", output);
                return;
            }
            board.Remove(id);
            scratchIds.Clear();
            scratchIds.Add(id);
            output.Add(CommsOutbound.Everyone(RemoveEnvelope(scratchIds)));
        }

        private void ClearMine(CommsSender sender, List<CommsOutbound> output)
        {
            scratchIds.Clear();
            ulong author = sender.Id;
            if (board.RemoveWhere(item => item.Author == author, scratchIds) > 0)
                output.Add(CommsOutbound.Everyone(RemoveEnvelope(scratchIds)));
        }

        private void ClearAll(CommsSender sender, string name, List<CommsOutbound> output)
        {
            if (!sender.Moderator)
            {
                Refuse(sender, "ONLY THE HOST CAN CLEAR THE MAP", output);
                return;
            }
            board.Clear();
            output.Add(CommsOutbound.Everyone(new CommsEnvelope { Event = CommsEvent.Reset }));
            output.Add(CommsOutbound.Everyone(new CommsEnvelope
            {
                Event = CommsEvent.Feed,
                Kind = (byte)CommsFeedKind.System,
                Author = sender.Id,
                AuthorName = name,
                Text = "CLEARED THE MAP",
            }));
        }

        // ---- Calls -------------------------------------------------------------------------

        private void Call(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!CommsCatalog.ValidCall(intent.Style))
            {
                Refuse(sender, "UNKNOWN CALL", output);
                return;
            }
            if (!ChannelAllowed(sender, intent.Channel, output)) return;

            BrevityCall call = CommsCatalog.Calls[intent.Style];
            bool located = call.MarksPosition && StrokeCodec.Valid(intent.Points, 1, 1);
            output.Add(CommsOutbound.For(intent.Channel, sender.Faction, new CommsEnvelope
            {
                Event = CommsEvent.Feed,
                Kind = (byte)CommsFeedKind.Call,
                Style = intent.Style,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Points = located ? (int[])intent.Points.Clone() : null,
            }));

            if (!located) return;
            Store(new CommsItem
            {
                Id = NextId(),
                Kind = CommsItemKind.Ping,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Style = (byte)call.PingAtSelf,
                Points = (int[])intent.Points.Clone(),
                Text = "",
                Created = now,
                Expires = now + Rules.LifetimeOf(CommsItemKind.Ping),
            }, now, output);
        }

        // ---- Polls -------------------------------------------------------------------------

        private void CreatePoll(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!ChannelAllowed(sender, intent.Channel, output)) return;
            if (!CommsPollBook.TryShape(intent.Text, intent.Items, out string question, out string[] options))
            {
                Refuse(sender, "A POLL NEEDS A QUESTION AND 2-4 OPTIONS", output);
                return;
            }
            string refusal = polls.Refusal(sender.Id);
            if (refusal != null)
            {
                Refuse(sender, refusal, output);
                return;
            }

            var poll = new CommsPoll
            {
                Id = NextId(),
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Question = question,
                Options = options,
                Tally = new int[options.Length],
                Closes = now + CommsCatalog.PollDuration(intent.Size),
            };
            polls.Add(poll);
            output.Add(CommsOutbound.For(poll.Channel, poll.Faction, PollEnvelope(poll, now)));
        }

        private void Vote(CommsSender sender, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            CommsPoll poll = polls.Find(intent.Target);
            if (poll == null || !CommsBoard.Visible(poll.Channel, poll.Faction, sender.Faction))
            {
                Refuse(sender, "THAT POLL HAS CLOSED", output);
                return;
            }
            if (CommsPollBook.Vote(poll, sender.Id, intent.Style))
                output.Add(CommsOutbound.For(poll.Channel, poll.Faction, PollEnvelope(poll, now)));
        }

        private void ClosePoll(CommsSender sender, uint id, float now, List<CommsOutbound> output)
        {
            CommsPoll poll = polls.Find(id);
            if (poll == null) return;
            if (poll.Author != sender.Id && !sender.Moderator)
            {
                Refuse(sender, "ONLY THE ASKER OR HOST CAN CLOSE A POLL", output);
                return;
            }
            polls.Close(id);
            output.Add(CommsOutbound.For(poll.Channel, poll.Faction, PollEnvelope(poll, now)));
        }

        // ---- Games -------------------------------------------------------------------------

        private void Roll(CommsSender sender, string name, CommsIntent intent, List<CommsOutbound> output)
        {
            if (!GamesAllowed(sender, output) || !ChannelAllowed(sender, intent.Channel, output)) return;
            if (!CommsCatalog.ValidDice(intent.Style))
            {
                Refuse(sender, "UNKNOWN DIE", output);
                return;
            }
            int sides = CommsCatalog.DiceSides[intent.Style];
            int value = random.Next(1, sides + 1);
            output.Add(CommsOutbound.For(intent.Channel, sender.Faction, new CommsEnvelope
            {
                Event = CommsEvent.Feed,
                Kind = (byte)CommsFeedKind.Roll,
                Style = intent.Style,
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Values = new[] { sides, value },
            }));
        }

        private void Challenge(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!GamesAllowed(sender, output) || !ChannelAllowed(sender, intent.Channel, output)) return;
            if (!CommsCatalog.ValidThrow(intent.Style))
            {
                Refuse(sender, "PICK ROCK, PAPER OR SCISSORS", output);
                return;
            }
            for (int i = 0; i < duels.Count; i++)
            {
                if (duels[i].Challenger != sender.Id) continue;
                Refuse(sender, "YOUR CHALLENGE IS ALREADY OUT", output);
                return;
            }
            if (duels.Count >= MaxDuels)
            {
                Refuse(sender, "TOO MANY OPEN CHALLENGES", output);
                return;
            }

            var duel = new RpsChallenge
            {
                Id = NextId(),
                Challenger = sender.Id,
                ChallengerName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Expires = now + DuelSeconds,
                Throw = intent.Style,
            };
            duels.Add(duel);
            output.Add(CommsOutbound.For(duel.Channel, duel.Faction, DuelEnvelope(duel, now, 0)));
        }

        private void Accept(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            RpsChallenge duel = FindDuel(intent.Target);
            if (duel == null || !CommsBoard.Visible(duel.Channel, duel.Faction, sender.Faction))
            {
                Refuse(sender, "THAT CHALLENGE IS GONE", output);
                return;
            }
            if (duel.Challenger == sender.Id)
            {
                Refuse(sender, "YOU CAN'T DUEL YOURSELF", output);
                return;
            }
            if (!CommsCatalog.ValidThrow(intent.Style))
            {
                Refuse(sender, "PICK ROCK, PAPER OR SCISSORS", output);
                return;
            }

            duels.Remove(duel);
            int outcome = RpsChallenge.Resolve(duel.Throw, intent.Style);
            CommsEnvelope result = DuelEnvelope(duel, now, CommsFlags.Closed);
            result.Values = new[] { duel.Throw, intent.Style, outcome };
            result.Players = new[] { duel.Challenger, sender.Id };
            result.Items = new[] { duel.ChallengerName, name };
            output.Add(CommsOutbound.For(duel.Channel, duel.Faction, result));

            if (outcome == 0) return;
            if (outcome > 0) scores.Award(duel.Challenger, duel.ChallengerName, 2, true);
            else scores.Award(sender.Id, name, 2, true);
            output.Add(CommsOutbound.Everyone(ScoresEnvelope()));
        }

        private void CancelDuel(CommsSender sender, uint id, float now, List<CommsOutbound> output)
        {
            RpsChallenge duel = FindDuel(id);
            if (duel == null || (duel.Challenger != sender.Id && !sender.Moderator)) return;
            duels.Remove(duel);
            output.Add(CommsOutbound.For(duel.Channel, duel.Faction, DuelEnvelope(duel, now, CommsFlags.Lapsed)));
        }

        private void StartHunt(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            if (!GamesAllowed(sender, output) || !ChannelAllowed(sender, intent.Channel, output)) return;
            if (!StrokeCodec.Valid(intent.Points, 1, 1))
            {
                Refuse(sender, "CLICK THE MAP TO HIDE A TARGET", output);
                return;
            }
            for (int i = 0; i < hunts.Count; i++)
            {
                if (hunts[i].Author != sender.Id) continue;
                Refuse(sender, "YOUR HUNT IS STILL RUNNING", output);
                return;
            }
            if (hunts.Count >= MaxHunts)
            {
                Refuse(sender, "A HUNT IS ALREADY RUNNING", output);
                return;
            }

            var hunt = new HuntRound
            {
                Id = NextId(),
                Author = sender.Id,
                AuthorName = name,
                Faction = sender.Faction,
                Channel = intent.Channel,
                Ends = now + CommsCatalog.HuntDuration(intent.Size),
                HiddenX = intent.Points[0],
                HiddenZ = intent.Points[1],
            };
            hunts.Add(hunt);
            output.Add(CommsOutbound.For(hunt.Channel, hunt.Faction, HuntEnvelope(hunt, now)));
        }

        private void Guess(CommsSender sender, string name, CommsIntent intent, float now, List<CommsOutbound> output)
        {
            HuntRound hunt = FindHunt(intent.Target);
            if (hunt == null || !CommsBoard.Visible(hunt.Channel, hunt.Faction, sender.Faction))
            {
                Refuse(sender, "THAT HUNT IS OVER", output);
                return;
            }
            if (!StrokeCodec.Valid(intent.Points, 1, 1))
            {
                Refuse(sender, "CLICK THE MAP TO GUESS", output);
                return;
            }
            if (hunt.Author == sender.Id)
            {
                Refuse(sender, "YOU HID THIS ONE", output);
                return;
            }
            if (!hunt.TryGuess(sender.Id, name, intent.Points[0], intent.Points[1]))
            {
                Refuse(sender, "ONE GUESS PER HUNT", output);
                return;
            }
            output.Add(CommsOutbound.For(hunt.Channel, hunt.Faction, HuntEnvelope(hunt, now)));
            output.Add(CommsOutbound.To(sender.Id, new CommsEnvelope
            {
                Event = CommsEvent.Notice,
                Flags = CommsFlags.Self,
                Id = hunt.Id,
                Text = "GUESS LOCKED IN",
            }));
        }

        private void Reveal(HuntRound hunt, float now, List<CommsOutbound> output)
        {
            List<HuntPlacing> placings = hunt.Rank();
            int count = placings.Count;
            var points = new int[2 + count * 2];
            var names = new string[count];
            var players = new ulong[count];
            var values = new int[count * 2];
            points[0] = hunt.HiddenX;
            points[1] = hunt.HiddenZ;
            bool awarded = false;
            bool anyClose = false;
            for (int i = 0; i < count; i++)
            {
                HuntPlacing placing = placings[i];
                points[2 + i * 2] = placing.Guess.X;
                points[3 + i * 2] = placing.Guess.Z;
                names[i] = placing.Guess.Name;
                players[i] = placing.Guess.Player;
                values[i * 2] = (int)Math.Round(placing.Metres);
                values[i * 2 + 1] = placing.Points;
                if (placing.Metres <= HuntRound.BullseyeMetres * 2f) anyClose = true;
                if (placing.Points <= 0) continue;
                scores.Award(placing.Guess.Player, placing.Guess.Name, placing.Points, i == 0);
                awarded = true;
            }

            // Hiding well is a skill too: a target nobody came near pays its hider.
            int hiderPoints = count > 0 && !anyClose ? 2 : 0;
            if (hiderPoints > 0)
            {
                scores.Award(hunt.Author, hunt.AuthorName, hiderPoints, false);
                awarded = true;
            }

            CommsEnvelope reveal = HuntEnvelope(hunt, now);
            reveal.Flags = CommsFlags.Revealed | CommsFlags.Closed;
            reveal.Points = points;
            reveal.Items = names;
            reveal.Players = players;
            reveal.Values = values;
            reveal.Size = (byte)hiderPoints;
            output?.Add(CommsOutbound.For(hunt.Channel, hunt.Faction, reveal));
            if (awarded) output?.Add(CommsOutbound.Everyone(ScoresEnvelope()));
        }

        // ---- Envelopes ---------------------------------------------------------------------

        internal static CommsEnvelope ItemEnvelope(CommsItem item, float now) => new CommsEnvelope
        {
            Event = CommsEvent.Item,
            Id = item.Id,
            Author = item.Author,
            AuthorName = item.AuthorName,
            Faction = item.Faction,
            Channel = item.Channel,
            Kind = (byte)item.Kind,
            Style = item.Style,
            Size = item.Size,
            Ttl = Math.Max(0f, item.Expires - now),
            Points = item.Points,
            Text = item.Text,
        };

        private static CommsEnvelope RemoveEnvelope(List<uint> ids) => new CommsEnvelope
        {
            Event = CommsEvent.Remove,
            Ids = ids.ToArray(),
        };

        /// <summary>
        /// A poll's state. When a snapshot is built for one viewer, their own ballot rides in
        /// <see cref="CommsEnvelope.Style"/> (plus one, zero for none) so a rejoin keeps the
        /// latched option without ever sending anyone else's ballot.
        /// </summary>
        internal static CommsEnvelope PollEnvelope(CommsPoll poll, float now, ulong viewer = 0UL) => new CommsEnvelope
        {
            Event = CommsEvent.Poll,
            Id = poll.Id,
            Author = poll.Author,
            AuthorName = poll.AuthorName,
            Faction = poll.Faction,
            Channel = poll.Channel,
            Flags = poll.Closed ? CommsFlags.Closed : (byte)0,
            Style = viewer != 0UL && poll.Ballots.TryGetValue(viewer, out int ballot) ? (byte)(ballot + 1) : (byte)0,
            Ttl = Math.Max(0f, poll.Closes - now),
            Text = poll.Question,
            Items = poll.Options,
            Values = (int[])poll.Tally.Clone(),
        };

        private static CommsEnvelope DuelEnvelope(RpsChallenge duel, float now, byte flags) => new CommsEnvelope
        {
            Event = CommsEvent.Rps,
            Id = duel.Id,
            Author = duel.Challenger,
            AuthorName = duel.ChallengerName,
            Faction = duel.Faction,
            Channel = duel.Channel,
            Flags = flags,
            Ttl = Math.Max(0f, duel.Expires - now),
        };

        private static CommsEnvelope HuntEnvelope(HuntRound hunt, float now) => new CommsEnvelope
        {
            Event = CommsEvent.Hunt,
            Id = hunt.Id,
            Author = hunt.Author,
            AuthorName = hunt.AuthorName,
            Faction = hunt.Faction,
            Channel = hunt.Channel,
            Ttl = Math.Max(0f, hunt.Ends - now),
            Values = new[] { hunt.GuessCount },
        };

        private CommsEnvelope ScoresEnvelope()
        {
            IReadOnlyList<ScoreRow> rows = scores.Rows;
            var players = new ulong[rows.Count];
            var names = new string[rows.Count];
            var values = new int[rows.Count * 2];
            for (int i = 0; i < rows.Count; i++)
            {
                players[i] = rows[i].Player;
                names[i] = rows[i].Name;
                values[i * 2] = rows[i].Points;
                values[i * 2 + 1] = rows[i].Wins;
            }
            return new CommsEnvelope { Event = CommsEvent.Scores, Players = players, Items = names, Values = values };
        }

        // ---- Rules -------------------------------------------------------------------------

        private bool ChannelAllowed(CommsSender sender, CommsChannel channel, List<CommsOutbound> output)
        {
            if (channel == CommsChannel.Team) return true;
            if (channel == CommsChannel.All && Rules.AllowAllChannel) return true;
            Refuse(sender, channel == CommsChannel.All ? "HOST HAS THE ALL CHANNEL OFF" : "UNKNOWN CHANNEL", output);
            return false;
        }

        private bool GamesAllowed(CommsSender sender, List<CommsOutbound> output)
        {
            if (Rules.AllowGames) return true;
            Refuse(sender, "HOST HAS GAMES OFF", output);
            return false;
        }

        private static void Refuse(CommsSender sender, string reason, List<CommsOutbound> output) =>
            output.Add(CommsOutbound.To(sender.Id, new CommsEnvelope { Event = CommsEvent.Notice, Text = reason }));

        private static float Cost(CommsOp op)
        {
            switch (op)
            {
                case CommsOp.Erase:
                case CommsOp.PollVote:
                case CommsOp.RpsCancel:
                case CommsOp.Sync:
                    return 0.5f;
                case CommsOp.Call:
                case CommsOp.Roll:
                case CommsOp.RpsChallenge:
                    return 2f;
                case CommsOp.PollCreate:
                case CommsOp.HuntStart:
                    return 4f;
                default:
                    return 1f;
            }
        }

        private RpsChallenge FindDuel(uint id)
        {
            for (int i = 0; i < duels.Count; i++)
                if (duels[i].Id == id) return duels[i];
            return null;
        }

        private HuntRound FindHunt(uint id)
        {
            for (int i = 0; i < hunts.Count; i++)
                if (hunts[i].Id == id) return hunts[i];
            return null;
        }

        private uint NextId()
        {
            uint id = nextId++;
            if (nextId == 0) nextId = 1;
            return id;
        }
    }
}
