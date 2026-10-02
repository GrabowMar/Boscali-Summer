using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    // WING's dossier card (portrait, identity, stamp, rank line, XP bar with rank ticks, record, radio, RELEASE) and its PERKS 2×2.
    internal sealed partial class WmcWing
    {
        private AvCard dossierCard;
        private WingDossier dossier;
        private bool releaseOn;
        private readonly ConfirmGate releaseGate = new ConfirmGate();
        private int dossierKey = int.MinValue;

        private void BuildDossier(AvFlow f)
        {
            f.Section(AvIcon.Crown, "DOSSIER");
            dossierCard = f.Add(new AvCard(f.Content, f.Ticker, f.Inner));
            dossier = dossierCard.Flow.Add(new WingDossier(dossierCard.Flow.Content, Release));
            ids.Add("wing.release", dossier.Release);
        }

        /// <summary>The dossier of the inspected pilot, rebuilt only when the roster, the pilot or RELEASE's ask changed.</summary>
        private void RefreshDossier()
        {
            WingPilot p = client ? null : inspected;
            int at = IndexOf(p);
            PilotStatus s = at >= 0 ? status[at] : PilotStatus.Free;
            bool asking = p != null && releaseGate.IsArmed(p.Callsign, Time.unscaledTime);
            int key;
            unchecked
            {
                key = scanVersion * 31 + at * 7 + (client ? 3 : 0) + (asking ? 5 : 0);
            }
            if (key == dossierKey) return;
            dossierKey = key;
            dossier.SetPilot(p);
            if (p == null)
            {
                dossier.SetIdentity(client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                dossier.SetStamp(WmcText.Unknown, "inert");
                dossier.SetRail("inert");
                dossier.SetRankLine("");
                dossier.SetXp(0f, false);
                for (int i = 0; i < 5; i++) SetLetter(i, false);
                dossier.SetRecord("");
                dossier.SetRadio("");
                SetRelease(false, false, client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                return;
            }
            bool next = ReferenceEquals(p, upcoming);
            dossier.SetIdentity(PilotPick.NameLine(p.Callsign, p.Name));
            dossier.SetStamp(SquadronWords.Stamp(s, next, number[at]), SquadronWords.Rail(s, next));
            dossier.SetRail(SquadronWords.Rail(s, next));
            dossier.SetRankLine(PilotXp.RankLine(p.Xp));
            dossier.SetXp(PilotXp.Fill(p.Xp), s == PilotStatus.Kia);
            for (int i = 0; i < 5; i++) SetLetter(i, i == (int)p.Rank);
            dossier.SetRecord(SquadronWords.Record(p.Kills, p.Sorties));
            dossier.SetRadio(SquadronWords.Persona(p.Persona.ToString()));
            SetRelease(s == PilotStatus.Flying, asking, SquadronWords.ReleaseWhy(s, false));
        }

        private void SetLetter(int i, bool lit) => dossier.SetLetter(i, SquadronWords.Badge((WingRank)i), (WingRank)i, lit);

        private void SetRelease(bool on, bool asking, string why)
        {
            releaseOn = on;
            AvControl release = dossier.Release;
            release.Label = SquadronWords.ReleaseLabel(asking);
            release.Latched = asking;
            release.Interactable = on;
            release.Help = on ? SquadronWords.ReleaseTip : why;
        }

        /// <summary>RELEASE, pressed twice: the member flying the dossier's pilot goes to the game's AI (the Release order).</summary>
        private void Release()
        {
            WingPilot p = inspected;
            if (last == null || p == null || client) return;
            WmcUi.Order(last, () =>
            {
                WingMember m = MemberOf(p);
                if (m == null) return;
                if (!releaseGate.Press(p.Callsign, Time.unscaledTime))
                {
                    WingToast.Show(SquadronWords.ReleaseAsk(WmcText.Cut(p.Callsign, PilotPick.CallsignChars), m.Number));
                    dossierKey = int.MinValue;
                    WmcPanel.Instance?.Refresh();
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Release, Scope = WingScope.OfMembers(m.Aircraft.persistentID.Id) });
                dossierKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
            });
        }

        // ---------------------------------------------------------------- PERKS 2×2

        private static readonly PilotPerk[] NoPerks = new PilotPerk[0];
        private AvSection perksSection;
        private readonly WingPerkCard[] perks = new WingPerkCard[PerkCards.Slots];
        private int perksKey = int.MinValue;

        private void BuildPerks(AvFlow f)
        {
            perksSection = f.Section(AvIcon.Star, SquadronWords.PerksTitle);
            AvCellGrid grid = f.Grid(2);
            for (int i = 0; i < perks.Length; i++)
            {
                perks[i] = new WingPerkCard(f.Content, i);
                grid.Add(perks[i]);
            }
        }

        /// <summary>The inspected pilot's perks in order, the ones not active in 1.0 marked, locked slots naming the rank and XP
        /// that earn them, and "+n MORE" on the fourth card past four (PerkCards).</summary>
        private void RefreshPerks()
        {
            WingPilot p = client ? null : inspected;
            bool off = !WingSettings.Instance.PilotProgression.Value || WingSettings.Instance.RankEffect.Value <= 0f;
            int key;
            unchecked
            {
                key = scanVersion * 31 + IndexOf(p) * 7 + (off ? 3 : 0) + (client ? 5 : 0);
            }
            if (key == perksKey) return;
            perksKey = key;
            WingRank rank = p != null ? p.Rank : WingRank.Rookie;
            perksSection.SetCaption(p == null ? WmcText.Unknown : PerkCards.Head(p.Perks.Count, rank, p.Lost, off));
            for (int i = 0; i < perks.Length; i++)
            {
                PerkCard card = PerkCards.For(i, p != null ? p.Perks : (System.Collections.Generic.IReadOnlyList<PilotPerk>)NoPerks, rank);
                perks[i].Set(card, off);
            }
            relayout = true;
        }
    }
}
