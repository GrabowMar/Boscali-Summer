using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Read-only board of the chain of command: the local faction's own posts plus every enemy post it has confirmed.
    /// Published by the HighCommand module, read by the Radio module, the Station Board and the sortie generator.
    /// The staff lives, moves and dies on the host: there is nothing to order here, only a board to read.
    /// </summary>
    internal interface IHighCommandView
    {
        /// <summary>A board for the local faction has arrived and is not stale (older than 20 s).</summary>
        bool Available { get; }

        /// <summary>Why the board is empty or old, in words with what to do, at most 96 characters; "" when there is nothing to explain.</summary>
        string Status { get; }

        /// <summary>Real (unscaled) seconds since the board was applied; +Infinity before the first.</summary>
        float AgeSeconds { get; }

        /// <summary>Own posts, then enemy posts this faction has confirmed; positions are GLOBAL metres.</summary>
        IReadOnlyList<CommanderView> Commanders { get; }

        /// <summary>The local faction's own staff log, newest first (the radio reads it).</summary>
        IReadOnlyList<CommanderLogLine> Log { get; }

        /// <summary>Ask for a fresh board now (throttled to 4 s). Not needed for liveness: the manager polls itself.</summary>
        void Refresh();

        /// <summary>Host only: the best confirmed standing enemy post for a STRIKE COMMAND POST sortie. False on a client, with the module off, or when none qualifies.</summary>
        bool TryPickStrikePost(FactionHQ attacker, float nearX, float nearZ, float maxMetres,
                               Predicate<int> skip, out CommandPostTarget target);
    }

    /// <summary>
    /// One staff post as the local faction may see it. Names and flags are authoritative;
    /// bio text and the portrait are regenerated locally from <see cref="PortraitSeed"/>.
    /// </summary>
    internal sealed class CommanderView
    {
        /// <summary>Stable for the mission: factionIndex * 8 + slot, 0..63.</summary>
        public int Id { get; }
        /// <summary>0 theater, 1 component, 2 base.</summary>
        public int Tier { get; }
        /// <summary>An own post.</summary>
        public bool IsFriendly { get; }
        /// <summary>Under fire now (own posts only).</summary>
        public bool Alert { get; }
        public string Name { get; }
        /// <summary>The post's rank after promotion, e.g. "COL".</summary>
        public string Rank { get; }
        public string Role { get; }
        /// <summary>Display name of the base, never a scene object id.</summary>
        public string Site { get; }
        /// <summary>Own post: what the commander pays you. Enemy post: what killing them means.</summary>
        public string Bonus { get; }
        public string Bio { get; }
        public int PortraitSeed { get; }
        /// <summary>Borrowed from Wing Command's generated pilot pool; never destroyed by the consumer.</summary>
        public Sprite Portrait { get; }
        /// <summary>Seconds since last confirmed at snapshot time, 0..600; -1 for an own post. Display age = IntelAge + the view's AgeSeconds.</summary>
        public float IntelAge { get; }
        /// <summary>GLOBAL metres: the building's last confirmed position (enemy) or its current one (own).</summary>
        public float X { get; }
        public float Z { get; }
        /// <summary>Funds that killing this post pays your faction now; 0 when claimed, own, or pay is off.</summary>
        public int Bounty { get; }
        /// <summary>Your faction already took this office's bounty.</summary>
        public bool BountyClaimed { get; }

        public CommanderView(int id, int tier, bool isFriendly, bool alert, string name, string rank, string role, string site,
            string bonus, string bio, int portraitSeed, Sprite portrait, float intelAge, float x, float z, int bounty, bool bountyClaimed)
        {
            Id = id; Tier = tier; IsFriendly = isFriendly; Alert = alert; Name = name ?? ""; Rank = rank ?? ""; Role = role ?? "";
            Site = site ?? ""; Bonus = bonus ?? ""; Bio = bio ?? ""; PortraitSeed = portraitSeed; Portrait = portrait;
            IntelAge = intelAge; X = x; Z = z; Bounty = bounty; BountyClaimed = bountyClaimed;
        }
    }

    /// <summary>A confirmed standing enemy post, host only.</summary>
    internal readonly struct CommandPostTarget
    {
        /// <summary>The office id (CommanderView.Id), also the card's SubjectId.</summary>
        public readonly int Id;
        /// <summary>The building to destroy.</summary>
        public readonly Unit Post;
        /// <summary>GLOBAL metres where this faction last confirmed it.</summary>
        public readonly float X, Y, Z;
        public readonly string Rank, Name, Role, Site;
        /// <summary>Funds the kill pays this faction now.</summary>
        public readonly int Bounty;

        public CommandPostTarget(int id, Unit post, float x, float y, float z, string rank, string name, string role, string site, int bounty)
        { Id = id; Post = post; X = x; Y = y; Z = z; Rank = rank; Name = name; Role = role; Site = site; Bounty = bounty; }
    }
}
