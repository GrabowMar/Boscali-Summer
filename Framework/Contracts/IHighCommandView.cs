using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only view of the chain of command, published by the HighCommand module and
    /// consumed by the STR console. Actions are intents: only the host validates and
    /// applies them. Commands are addressed by commander id, never by position or asset.
    /// </summary>
    internal interface IHighCommandView
    {
        bool Available { get; }
        string Status { get; }
        string Signal { get; }
        float FriendlyCohesion { get; }
        int FriendlyActive { get; }
        int FriendlyKia { get; }
        int CommandPoints { get; }
        IReadOnlyList<CommanderView> Commanders { get; }

        /// <summary>Ask the host for a fresh staff snapshot. Rate-limited by the transport.</summary>
        void Refresh();
        void RequestCommend(int id);
        void RequestRelocate(int id);
        void RequestBounty(int id);
    }

    /// <summary>
    /// One staff post as the local faction may see it. Names and flags are authoritative;
    /// bio text and the portrait are regenerated locally from <see cref="PortraitSeed"/>.
    /// </summary>
    internal sealed class CommanderView
    {
        public int Id { get; }
        public int ParentId { get; }
        public int Tier { get; }
        public bool IsFriendly { get; }
        public bool IsKnown { get; }
        public bool IsKia { get; }
        public bool InTransit { get; }
        public bool Disrupted { get; }
        public bool BountyMarked { get; }
        public string Name { get; }
        public string Rank { get; }
        public string Role { get; }
        public string Location { get; }
        public string Traits { get; }
        public string Decoration { get; }
        public string Bio { get; }
        public int PortraitSeed { get; }

        /// <summary>Borrowed from Wing Command's generated pilot pool; never destroyed by the consumer.</summary>
        public Sprite Portrait { get; }

        /// <summary>Seconds since last confirmed contact, or -1 for own living staff.</summary>
        public float IntelAge { get; }

        /// <summary>Share of the roster this post represents, 0..1, for the row bar.</summary>
        public float Weight { get; }

        public float X { get; }
        public float Z { get; }
        public bool CanCommend { get; }
        public bool CanRelocate { get; }
        public bool CanBounty { get; }

        public CommanderView(int id, int parentId, int tier, bool isFriendly, bool isKnown,
            bool isKia, bool inTransit, bool disrupted, bool bountyMarked, string name, string rank,
            string role, string location, string traits, string decoration, string bio,
            int portraitSeed, Sprite portrait, float intelAge, float weight, float x, float z,
            bool canCommend, bool canRelocate, bool canBounty)
        {
            Id = id; ParentId = parentId; Tier = tier;
            IsFriendly = isFriendly; IsKnown = isKnown; IsKia = isKia;
            InTransit = inTransit; Disrupted = disrupted; BountyMarked = bountyMarked;
            Name = name; Rank = rank; Role = role; Location = location;
            Traits = traits; Decoration = decoration; Bio = bio;
            PortraitSeed = portraitSeed; Portrait = portrait; IntelAge = intelAge; Weight = weight; X = x; Z = z;
            CanCommend = canCommend; CanRelocate = canRelocate; CanBounty = canBounty;
        }
    }
}
