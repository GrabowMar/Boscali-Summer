using NOAvionics;
using System.Collections.Generic;
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
    /// <summary>TACTICAL › ORDERS › POSTURE (spec FUI §TACTICAL) on kit v2: the PROFILE picker, then TARGETS, RANGE, WEAPONS and RADAR
    /// (<see cref="AvSegmented"/> rows) for the scope, and WHOLE WING (power, and the winchester, bingo and fall-back pickers) for the
    /// whole wing. They send exactly what BEHAVIOUR › TUNING sends (<see cref="WmcPostureActions"/>); a mixed scope latches nothing.
    /// The pickers open the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed partial class WmcTactical
    {
        private const string WholeWing = "Whole wing: ";
        private const int AxisTargets = 0, AxisReach = 1, AxisWeapons = 2, AxisRadar = 3, AxisPower = 4;

        private readonly List<AvPopupEntry> popupEntries = new List<AvPopupEntry>(8);
        private readonly AvSegmented[] segments = new AvSegmented[5];
        private readonly string[][] segmentTips = new string[5][];
        private readonly int[] segmentValues = { -1, -1, -1, -1, -1 };
        private AvControl pProfile, pWinch, pBingo, pFallBack;
        private AvSection postureSection;
        private string profileShown, winchShown, bingoShown, fallBackShown, postureNoteShown;
        private bool posturePickersOn = true;

        private void BuildPosture(AvFlow f)
        {
            postureSection = f.Section(AvIcon.Shield, "POSTURE");
            pProfile = f.Buttons(new AvControl.Spec("—", OpenProfiles, AvButtonStyle.Default, AvIcon.AdjustmentsHorizontal)).Controls[0];
            pProfile.Help = "Sets TARGETS, RANGE and missile defence at once for the scope: RESERVE holds fire close in, ESCORT covers you, SWEEP hunts wide.";
            ids.Add("tac.posture.profile", pProfile);

            Segment(f, AxisTargets, "TARGETS", new[] { "HOLD", "AIR", "GROUND", "BOTH", "COVER" },
                new[]
                {
                    "Hold fire: shoot only when ordered.", "Shoot at enemy aircraft in reach on their own.",
                    "Shoot at ground targets in reach on their own.", "Shoot at air and ground targets in reach on their own.",
                    "Cover: take the one air threat nearest the protected aircraft.",
                }, "tac.posture.targets.", new[] { "hold", "air", "ground", "both", "cover" }, i => WmcPostureActions.Axis(last, DoctrineAxis.Targets, i));
            Segment(f, AxisReach, "RANGE", new[] { "6 KM", "12 KM" },
                new[] { "Close: shoot at what comes within 6 km on their own.", "Long: reach out to 12 km on their own." },
                "tac.posture.reach.", new[] { "slot", "long" }, i => WmcPostureActions.Axis(last, DoctrineAxis.Reach, i));
            Segment(f, AxisWeapons, "WEAPONS", new[] { "AUTO", "MISSILE", "GUN", "NO A-G" },
                new[] { "Every weapon aboard.", "Missiles only: no guns, no bombs.", "Guns only.", "Every weapon, at air targets only." },
                "tac.posture.weapons.", new[] { "auto", "missiles", "guns", "noag" }, i => WmcPostureActions.Axis(last, DoctrineAxis.Weapons, i));
            Segment(f, AxisRadar, "RADAR", new[] { "RDR ON", "SILENT", "OFF" },
                new[]
                {
                    "Radar on: the scope sees and shares its picture.",
                    "Silent: radar off until engaged, then on (enemy warners stay quiet).",
                    "Off: no radar, no radar-guided (SARH) shots.",
                }, "tac.posture.radar.", new[] { "on", "silent", "off" }, i => WmcPostureActions.Axis(last, DoctrineAxis.Radar, i));

            f.Section(AvIcon.Plane, "WHOLE WING", "ALL ELEMENTS");
            Segment(f, AxisPower, "POWER", new[] { "BUSTER", "GATE" },
                new[] { WholeWing + "full power, no afterburner.", WholeWing + "afterburner allowed." },
                "tac.posture.power.", new[] { "buster", "gate" }, i => WmcPostureActions.Power(last, i));
            AvControl[] pickers = f.Buttons(
                new AvControl.Spec("—", OpenWinchester), new AvControl.Spec("—", OpenBingo),
                new AvControl.Spec("—", OpenFallBack)).Controls;
            pWinch = pickers[0];
            pBingo = pickers[1];
            pFallBack = pickers[2];
            pWinch.Help = WholeWing + "what a wingman does out of ammunition: rejoin, RTB, or refit and come back.";
            pBingo.Help = WholeWing + "what a wingman does at bingo fuel: RTB, or refit and come back.";
            pFallBack.Help = WholeWing + "fall back into formation when this outnumbered (enemy aircraft per fighting wingman).";
            ids.Add("tac.posture.winchester", pWinch);
            ids.Add("tac.posture.bingo", pBingo);
            ids.Add("tac.posture.fallback", pFallBack);
        }

        private void Segment(AvFlow f, int axis, string label, string[] labels, string[] tips, string prefix, string[] keys, System.Action<int> pick)
        {
            AvSegmented seg = f.Add(new AvSegmented(f.Content, label, labels, () => segmentValues[axis], pick));
            segments[axis] = seg;
            segmentTips[axis] = tips;
            for (int i = 0; i < labels.Length; i++)
            {
                seg.Options[i].Help = tips[i];
                ids.Add(prefix + keys[i], seg.Options[i]);
            }
        }

        private void SetPick(AvControl b, ref string shown, string text)
        {
            if (text == shown) return;
            shown = text;
            b.Label = text;
        }

        private void RefreshPosture(WmcContext c)
        {
            bool host = c.CanOrder;
            string cannot = WmcPostureActions.Cannot(c);
            WingService w = host ? c.Wing : null;

            bool same = c.ScopeDoctrine(out WingDoctrine d);
            string profile = !host ? "PROFILE" : same ? d.PatternName : "MIXED";
            SetPick(pProfile, ref profileShown, profile + " ›");
            pProfile.Interactable = host;
            string note = host ? profile + " · " + c.ScopeLabel : cannot.ToUpperInvariant();
            if (note != postureNoteShown)
            {
                postureNoteShown = note;
                postureSection.SetCaption(note);
                relayout = true;
            }
            Axis(AxisTargets, c, DoctrineAxis.Targets, host, cannot);
            Axis(AxisReach, c, DoctrineAxis.Reach, host, cannot);
            Axis(AxisWeapons, c, DoctrineAxis.Weapons, host, cannot);
            Axis(AxisRadar, c, DoctrineAxis.Radar, host, cannot);
            SetSegment(AxisPower, w != null ? (w.AfterburnerAllowed ? 1 : 0) : -1, host, cannot);

            WingConfig cfg = WingSettings.Instance;
            int winch = cfg != null ? (int)cfg.AfterWinchester.Value : -1, bingo = cfg != null ? (int)cfg.AfterBingo.Value : -1;
            int fall = cfg != null ? WmcPostureActions.FallBackIndex(cfg.FallBackRatio.Value) : -1;
            SetPick(pWinch, ref winchShown, "WINCH › " + Word(WmcPostureActions.WinchesterWords, winch));
            SetPick(pBingo, ref bingoShown, "BINGO › " + Word(WmcPostureActions.BingoWords, bingo));
            SetPick(pFallBack, ref fallBackShown, "FALL › " + Word(WmcPostureActions.FallBackWords, fall));
            if (host != posturePickersOn)
            {
                posturePickersOn = host;
                pWinch.Interactable = host;
                pBingo.Interactable = host;
                pFallBack.Interactable = host;
            }
        }

        private static string Word(string[] words, int i) => i >= 0 && i < words.Length ? words[i] : WmcText.Unknown;

        private void Axis(int slot, WmcContext c, DoctrineAxis axis, bool host, string cannot) =>
            SetSegment(slot, host ? c.ScopeValue(axis) : -1, host, cannot);

        /// <summary>Latches <paramref name="value"/> (-1: none, MIXED) and enables the row for the host; a disabled row says why in
        /// every segment's help.</summary>
        private void SetSegment(int slot, int value, bool host, string cannot)
        {
            AvSegmented seg = segments[slot];
            if (segmentValues[slot] != value)
            {
                segmentValues[slot] = value;
                seg.Refresh();
            }
            AvControl[] options = seg.Options;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].Interactable != host) options[i].Interactable = host;
                string tip = host ? segmentTips[slot][i] : cannot;
                if (!ReferenceEquals(options[i].Help, tip)) options[i].Help = tip;
            }
        }

        private void ShowPopup(AvControl at, System.Action<int> pick) =>
            popup.Show(WmcPopup.Area(flow.Content, at.Rect, popupEntries.Count), popupEntries, pick);

        private void OpenProfiles()
        {
            if (last == null || !last.CanOrder) return;
            string current = last.ScopeDoctrine(out WingDoctrine scoped) ? scoped.PatternName : null;
            popupEntries.Clear();
            foreach (WingDoctrine d in WmcPostureActions.Profiles)
                popupEntries.Add(new AvPopupEntry(d.PatternName, null, d.PatternName == current));
            ShowPopup(pProfile, i =>
            {
                WmcPostureActions.Profile(last, i);
                FlashScope();
            });
        }

        private void OpenWords(AvControl at, string[] words, int current, System.Action<int> pick)
        {
            if (last == null || !last.CanOrder) return;
            popupEntries.Clear();
            for (int i = 0; i < words.Length; i++) popupEntries.Add(new AvPopupEntry(words[i], null, i == current));
            ShowPopup(at, pick);
        }

        private void OpenWinchester() => OpenWords(pWinch, WmcPostureActions.WinchesterWords,
            WingSettings.Instance != null ? (int)WingSettings.Instance.AfterWinchester.Value : -1, i => WmcPostureActions.Winchester(last, i));

        private void OpenBingo() => OpenWords(pBingo, WmcPostureActions.BingoWords,
            WingSettings.Instance != null ? (int)WingSettings.Instance.AfterBingo.Value : -1, i => WmcPostureActions.Bingo(last, i));

        private void OpenFallBack() => OpenWords(pFallBack, WmcPostureActions.FallBackWords,
            WingSettings.Instance != null ? WmcPostureActions.FallBackIndex(WingSettings.Instance.FallBackRatio.Value) : -1, i => WmcPostureActions.FallBack(last, i));
    }
}
