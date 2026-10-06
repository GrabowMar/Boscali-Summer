using NOAvionics;
using System.Collections.Generic;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Configuration;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL › ORDERS › WHOLE WING (spec FUI §TACTICAL): POWER and the winchester, bingo and fall-back pickers, which are
    /// wing-wide settings. The scope's own settings (TARGETS, RANGE, RADAR, WEAPONS) are ORDERS' fine-tune strip. They send exactly what
    /// BEHAVIOUR sends (<see cref="WmcPostureActions"/>). The pickers open the toolkit popup (<see cref="AvPopup"/>).</summary>
    internal sealed partial class WmcTactical
    {
        private const string WholeWing = "Whole wing: ";

        private readonly List<AvPopupEntry> popupEntries = new List<AvPopupEntry>(8);
        private AvSegmented powerRow;
        private int powerValue = -1;
        private AvControl pWinch, pBingo, pFallBack;
        private string profileShown, winchShown, bingoShown, fallBackShown;
        private bool posturePickersOn = true;
        private static readonly string[] PowerTips = { WholeWing + "full power, no afterburner.", WholeWing + "afterburner allowed." };

        private void BuildWholeWing(AvFlow f)
        {
            f.Section(AvIcon.Plane, "WHOLE WING", "ALL ELEMENTS");
            powerRow = f.Add(new AvSegmented(f.Content, "POWER", new[] { "BUSTER", "GATE" }, () => powerValue, i => WmcPostureActions.Power(last, i)));
            string[] keys = { "buster", "gate" };
            for (int i = 0; i < keys.Length; i++)
            {
                powerRow.Options[i].Help = PowerTips[i];
                ids.Add("tac.posture.power." + keys[i], powerRow.Options[i]);
            }
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

        private void SetPick(AvControl b, ref string shown, string text)
        {
            if (text == shown) return;
            shown = text;
            b.Label = text;
        }

        private void RefreshWholeWing(WmcContext c)
        {
            bool host = c.CanOrder;
            WingService w = host ? c.Wing : null;
            int power = w != null ? (w.AfterburnerAllowed ? 1 : 0) : -1;
            if (powerValue != power)
            {
                powerValue = power;
                powerRow.Refresh();
            }
            string cannot = WmcPostureActions.Cannot(c);
            for (int i = 0; i < powerRow.Options.Length; i++)
            {
                AvControl o = powerRow.Options[i];
                if (o.Interactable != host) o.Interactable = host;
                string tip = host ? PowerTips[i] : cannot;
                if (o.Help != tip) o.Help = tip;
            }
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

        private void ShowPopup(AvControl at, System.Action<int> pick) =>
            popup.Show(WmcPopup.Area(flow.Content, at.Rect, popupEntries.Count), popupEntries, pick);

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
