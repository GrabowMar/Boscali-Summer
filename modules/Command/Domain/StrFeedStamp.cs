using System;
using System.Runtime.CompilerServices;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The change stamp of one tick of feeds (StrFeeds.Stamp): StrViewBuilder returns without touching the model when the stamp, the whole
    /// second and the interface state are all equal, so every feed the board shows must move the stamp. Objects enter by identity (a snapshot, each
    /// sortie card and a command result are never mutated after they are published; the list holding the cards is not), values by value. Left out on purpose: the
    /// decaying seconds (the builder keys those by whole second itself, so a tick that only aged a report still builds once a second, not
    /// four times) and the census age (nothing displays it). The own position enters in 100 m cells, so a flying pilot rebuilds the distance
    /// words when they can change, not on every tick. No allocation.
    /// </summary>
    internal static class StrFeedStamp
    {
        private const float OwnCellMetres = 100f;

        public static int Of(in StrFeeds f)
        {
            unchecked
            {
                int h = 17;
                h = Mix(h, f.IsHost ? 1 : 0);
                h = Mix(h, f.LocalId.GetHashCode());
                h = Mix(h, f.FactionCode == null ? 0 : f.FactionCode.GetHashCode());
                h = Mix(h, f.HasOwn ? 1 : 0);
                h = Mix(h, Cell(f.OwnX));
                h = Mix(h, Cell(f.OwnZ));
                h = Mix(h, f.WarAvailable ? 1 : 0);
                h = Mix(h, (int)f.WarState);
                h = Mix(h, Id(f.War));
                h = Mix(h, f.WarCanCommand ? 1 : 0);
                h = Mix(h, f.WarPending ? 1 : 0);
                h = Mix(h, Id(f.WarLast));
                if (f.WarLast != null)
                {
                    h = Mix(h, (int)f.WarLast.Code);
                    h = Mix(h, f.WarLast.Detail == null ? 0 : f.WarLast.Detail.GetHashCode());
                    h = Mix(h, f.WarLast.LockSeconds.GetHashCode());
                }
                h = Mix(h, f.SortiesAvailable ? 1 : 0);
                h = Mix(h, f.SortiesFresh ? 1 : 0);
                h = Mix(h, f.SortieActionPending ? 1 : 0);
                h = Mix(h, f.SortieActiveLimit);
                int cards = f.Sorties == null ? 0 : f.Sorties.Count;
                h = Mix(h, cards);
                for (int i = 0; i < cards; i++) h = Mix(h, Id(f.Sorties[i]));
                h = Mix(h, f.PostsAvailable ? 1 : 0);
                h = Mix(h, f.PostCount);
                for (int i = 0; f.Posts != null && i < f.PostCount && i < f.Posts.Length; i++) h = Mix(h, Post(in f.Posts[i]));
                h = Mix(h, f.StaffPresent ? 1 : 0);
                h = Mix(h, f.StaffStatus == null ? 0 : f.StaffStatus.GetHashCode());
                h = Mix(h, f.OfficerCount);
                for (int i = 0; f.Officers != null && i < f.OfficerCount && i < f.Officers.Length; i++) h = Mix(h, Officer(in f.Officers[i]));
                h = Mix(h, f.WingCount);
                for (int i = 0; f.Wing != null && i < f.WingCount && i < f.Wing.Length; i++) h = Mix(h, Wing(in f.Wing[i]));
                h = Mix(h, f.PickerPrompt == null ? 0 : f.PickerPrompt.GetHashCode());
                h = Mix(h, f.KnownAirDefence);
                h = Mix(h, Census(in f.Census));
                h = Mix(h, (f.SupplyKnown ? 1 : 0) | (f.DonationForbidden ? 2 : 0));
                h = Mix(h, Whole(f.Allocation));
                h = Mix(h, Whole(f.DonateCooldown));
                h = Mix(h, f.ConvoyCount);
                for (int i = 0; f.Convoys != null && i < f.ConvoyCount && i < f.Convoys.Length; i++)
                {
                    h = Mix(h, f.Convoys[i].Name == null ? 0 : f.Convoys[i].Name.GetHashCode());
                    h = Mix(h, Whole(f.Convoys[i].Cost));
                    h = Mix(h, Whole(f.Convoys[i].Cooldown));
                }
                return h;
            }
        }

        private static int Mix(int h, int v) { unchecked { return (h * 31) ^ v; } }

        // Funds and cooldowns as the board prints them: whole numbers, so a fraction that shows nowhere builds nothing.
        private static int Whole(float v) => !float.IsFinite(v) ? int.MinValue : (int)Math.Ceiling(Math.Max(-2e9f, Math.Min(2e9f, v)));

        private static int Id(object o) => o == null ? 0 : RuntimeHelpers.GetHashCode(o);

        private static int Cell(float metres) =>
            !float.IsFinite(metres) ? int.MinValue : (int)Math.Floor(Math.Max(-2e9f, Math.Min(2e9f, metres)) / OwnCellMetres);

        private static int Post(in StrPostIn p)
        {
            unchecked
            {
                int h = Mix(p.Id, p.Bounty);
                h = Mix(h, p.Name == null ? 0 : p.Name.GetHashCode());
                h = Mix(h, p.Rank == null ? 0 : p.Rank.GetHashCode());
                h = Mix(h, p.Role == null ? 0 : p.Role.GetHashCode());
                h = Mix(h, p.Site == null ? 0 : p.Site.GetHashCode());
                h = Mix(h, p.Trait == null ? 0 : p.Trait.GetHashCode());
                h = Mix(h, p.IntelAge.GetHashCode());
                h = Mix(h, p.X.GetHashCode());
                h = Mix(h, p.Z.GetHashCode());
                return Mix(h, p.BountyCapped ? 1 : 0);
            }
        }

        private static int Officer(in StrOfficerIn o)
        {
            unchecked
            {
                int h = Mix(o.Id, o.Tier);
                h = Mix(h, (o.Own ? 1 : 0) | (o.Alert ? 2 : 0) | (o.Confirmed ? 4 : 0));
                h = Mix(h, o.Name == null ? 0 : o.Name.GetHashCode());
                h = Mix(h, o.Rank == null ? 0 : o.Rank.GetHashCode());
                h = Mix(h, o.Role == null ? 0 : o.Role.GetHashCode());
                h = Mix(h, o.Site == null ? 0 : o.Site.GetHashCode());
                h = Mix(h, o.Trait == null ? 0 : o.Trait.GetHashCode());
                h = Mix(h, o.IntelAge.GetHashCode());
                h = Mix(h, o.X.GetHashCode());
                h = Mix(h, o.Z.GetHashCode());
                return Mix(h, o.Portrait == null ? 0 : RuntimeHelpers.GetHashCode(o.Portrait));
            }
        }

        private static int Wing(in StrWingIn w)
        {
            unchecked
            {
                int h = Mix(w.SortieId, (int)w.State);
                h = Mix(h, (int)w.Intent);
                h = Mix(h, w.Refusal == null ? 0 : w.Refusal.GetHashCode());
                return Mix(h, w.Words == null ? 0 : w.Words.GetHashCode());
            }
        }

        private static int Census(in TheaterCensus c)
        {
            unchecked
            {
                int h = c.Known ? 1 : 0;
                h = Mix(h, c.AirFriendly); h = Mix(h, c.AirParked); h = Mix(h, c.AirHostileTracked);
                h = Mix(h, c.GroundFriendly); h = Mix(h, c.GroundHostileTracked); h = Mix(h, c.ShipsFriendly); h = Mix(h, c.ShipsHostileTracked);
                h = Mix(h, c.BasesFriendly); h = Mix(h, c.BasesHostile); h = Mix(h, c.BasesNeutral); h = Mix(h, c.BasesContested); h = Mix(h, c.Radars);
                h = Mix(h, c.SectorsFriendly); h = Mix(h, c.SectorsHostile); h = Mix(h, c.SectorsContested); h = Mix(h, c.SectorsNeutral);
                h = Mix(h, c.SortieCap); h = Mix(h, c.SortieSead); h = Mix(h, c.SortieCas); h = Mix(h, c.SortieStrike); h = Mix(h, c.SortieTransit);
                h = Mix(h, c.FrontMetres.GetHashCode());
                h = Mix(h, c.Threat);
                return Mix(h, (int)c.ThreatWhy);
            }
        }
    }
}
