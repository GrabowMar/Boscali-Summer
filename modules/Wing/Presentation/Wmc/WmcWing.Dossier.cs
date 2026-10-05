using NOAvionics;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
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
    // WING's personnel file (one card under the roster table): the file's face (portrait, stamp, rank and XP ladder, record, radio style,
    // ribbon rack), the ASSIGNED line, the PERKS badges and the action row AIR SAR · LOCAL SAR · RELEASE. 2026-10-05 redesign of the
    // dossier + assignment bar + perks 2x2 (every fact kept; rescues are not tracked by the game's record, so the record line has none).
    internal sealed partial class WmcWing
    {
        private AvCard fileCard;
        private WingFileTop fileTop;
        private bool releaseOn;
        private AvControl releaseButton;
        private readonly ConfirmGate releaseGate = new ConfirmGate();
        private readonly FileFace face = new FileFace();
        private readonly RibbonId[] rack = new RibbonId[Ribbons.Max];
        private int fileKey = int.MinValue;

        /// <summary>The file card under the roster: face, ASSIGNED, PERKS, actions.</summary>
        private void BuildFile(AvFlow f)
        {
            fileCard = f.Add(new AvCard(f.Content, f.Ticker, f.Inner));
            AvFlow c = fileCard.Flow;
            fileTop = c.Add(new WingFileTop(c.Content));
            BuildAssignment(c);
            BuildPerks(c);
            AvControl[] actions = c.Buttons(
                new AvControl.Spec("AIR SAR", AirSar, AvButtonStyle.Default, AvIcon.Plane),
                new AvControl.Spec("LOCAL SAR", LocalSar, AvButtonStyle.Default, AvIcon.Flag),
                new AvControl.Spec("RELEASE", Release, AvButtonStyle.Danger, AvIcon.Unlink)).Controls;
            assignAir = actions[0];
            assignLocal = actions[1];
            releaseButton = actions[2];
            releaseButton.Help = SquadronWords.ReleaseTip;
            ids.Add("wing.airsar", assignAir);
            ids.Add("wing.localsar", assignLocal);
            ids.Add("wing.release", releaseButton);
        }

        /// <summary>The selected pilot's file, rebuilt only when the roster, the pilot, RELEASE's ask or a local search's second changed.</summary>
        private void RefreshFile()
        {
            WingPilot p = client ? null : inspected;
            int at = IndexOf(p);
            PilotStatus s = at >= 0 ? status[at] : PilotStatus.Free;
            bool asking = p != null && releaseGate.IsArmed(p.Callsign, Time.unscaledTime);
            int key;
            unchecked
            {
                key = scanVersion * 31 + at * 7 + (client ? 3 : 0) + (asking ? 5 : 0) + (at >= 0 && localLeft[at] >= 0f ? (int)localLeft[at] * 13 + 1 : 0);
            }
            if (key == fileKey) return;
            fileKey = key;
            relayout = true;
            fileTop.SetPilot(p);
            if (p == null)
            {
                face.Tab = SquadronWords.FileTitle;
                face.Callsign = client ? WmcText.Unknown : "NO PILOT";
                face.Name = client ? SquadronWords.ClientWhy : SquadronWords.NoFocus;
                face.RankLine = "";
                face.Stats = "";
                face.Radio = "";
                face.Stamp = WmcText.Unknown;
                face.Rail = "inert";
                face.RibbonHelp = "";
                face.Rank = WingRank.Rookie;
                face.Xp = 0f;
                face.Dim = true;
                face.RibbonCount = 0;
                fileTop.Show(face);
                SetRelease(false, false, client ? SquadronWords.ClientWhy : SquadronWords.NoFocus);
                return;
            }
            bool next = ReferenceEquals(p, upcoming);
            face.Tab = SquadronWords.FileTitle + " · " + PersonnelFile.Number(p.Callsign, p.Name);
            face.Callsign = "\"" + WmcText.Cut(p.Callsign, PilotPick.CallsignChars) + "\"";
            face.Name = WmcText.Cut(p.Name, 24).ToUpperInvariant();
            face.RankLine = PilotXp.RankLine(p.Xp);
            face.Stats = SquadronWords.Record(p.Kills, p.Sorties);
            face.Radio = SquadronWords.Persona(p.Persona.ToString());
            face.Stamp = SquadronWords.Tag(s, next, number[at], localLeft[at]);
            face.Rail = SquadronWords.Rail(s, next);
            face.Rank = p.Rank;
            face.Xp = PilotXp.Fill(p.Xp);
            face.Dim = s == PilotStatus.Kia;
            face.RibbonCount = Ribbons.For(p.Kills, p.Sorties, (int)p.Rank, rack);
            for (int i = 0; i < face.RibbonCount; i++) face.Rack[i] = rack[i];
            face.RibbonHelp = RibbonHelp(face.RibbonCount);
            fileTop.Show(face);
            SetRelease(s == PilotStatus.Flying, asking, SquadronWords.ReleaseWhy(s, false));
        }

        /// <summary>The rack's words, for the footer help (the bars are small): the earned ribbons by name, or what earns the first.</summary>
        private string RibbonHelp(int n)
        {
            if (n == 0) return "No ribbons yet: 5 kills, 5 sorties or the first rank-up earn one.";
            var sb = new System.Text.StringBuilder("Ribbons:");
            for (int i = 0; i < n; i++) sb.Append(i == 0 ? " " : " · ").Append(Ribbons.Word(rack[i]));
            return sb.ToString();
        }

        private void SetRelease(bool on, bool asking, string why)
        {
            releaseOn = on;
            releaseButton.Label = SquadronWords.ReleaseLabel(asking);
            releaseButton.Latched = asking;
            releaseButton.Interactable = on;
            releaseButton.Help = on ? SquadronWords.ReleaseTip : why;
        }

        /// <summary>RELEASE, pressed twice: the member flying the file's pilot goes to the game's AI (the Release order).</summary>
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
                    fileKey = int.MinValue;
                    WmcPanel.Instance?.Refresh();
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Release, Scope = WingScope.OfMembers(m.Aircraft.persistentID.Id) });
                fileKey = int.MinValue;
                WmcPanel.Instance?.Refresh();
            });
        }

        // ---------------------------------------------------------------- PERKS: four badges

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

        /// <summary>The selected pilot's perks in order, the ones not active in 1.0 marked, locked slots naming the rank and XP that earn
        /// them, and "+n MORE" on the fourth card past four (PerkCards).</summary>
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
