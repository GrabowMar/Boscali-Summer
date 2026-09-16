using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only view of the chain of command, published by the HighCommand module and
    /// consumed by the STR console. The staff lives, moves and dies on the host: there is
    /// nothing to order here, only a board to read.
    /// </summary>
    internal interface IHighCommandView
    {
        bool Available { get; }
        string Status { get; }
        string Signal { get; }
        float FriendlyCohesion { get; }
        int FriendlyActive { get; }
        int FriendlyKia { get; }
        IReadOnlyList<CommanderView> Commanders { get; }

        /// <summary>The local faction's own staff log, newest first, bounded by the source.</summary>
        IReadOnlyList<CommanderLogLine> Log { get; }

        /// <summary>Enemy staff events this faction has sight on, newest first, bounded.</summary>
        IReadOnlyList<CommanderLogLine> HostileLog { get; }

        /// <summary>Ask the host for a fresh staff snapshot. Rate-limited by the transport.</summary>
        void Refresh();
    }

    /// <summary>
    /// One staff post as the local faction may see it. Names and flags are authoritative;
    /// bio text, bonus line and the portrait are regenerated locally from <see cref="PortraitSeed"/>.
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
        public bool Alert { get; }
        public string Name { get; }
        public string Rank { get; }
        public string Role { get; }
        public string Location { get; }

        /// <summary>What this commander is worth to their faction, as the board states it.</summary>
        public string Bonus { get; }

        public string Bio { get; }
        public int PortraitSeed { get; }

        /// <summary>Borrowed from Wing Command's generated pilot pool; never destroyed by the consumer.</summary>
        public Sprite Portrait { get; }

        /// <summary>Seconds since last confirmed contact, or -1 for own living staff.</summary>
        public float IntelAge { get; }

        /// <summary>Share of the staff this post represents, 0..1, for the row bar.</summary>
        public float Weight { get; }

        public float X { get; }
        public float Z { get; }

        public CommanderView(int id, int parentId, int tier, bool isFriendly, bool isKnown,
            bool isKia, bool inTransit, bool disrupted, bool alert, string name,
            string rank, string role, string location, string bonus, string bio,
            int portraitSeed, Sprite portrait, float intelAge, float weight, float x, float z)
        {
            Id = id; ParentId = parentId; Tier = tier;
            IsFriendly = isFriendly; IsKnown = isKnown; IsKia = isKia;
            InTransit = inTransit; Disrupted = disrupted; Alert = alert;
            Name = name; Rank = rank; Role = role; Location = location;
            Bonus = bonus; Bio = bio;
            PortraitSeed = portraitSeed; Portrait = portrait; IntelAge = intelAge; Weight = weight; X = x; Z = z;
        }
    }
}
