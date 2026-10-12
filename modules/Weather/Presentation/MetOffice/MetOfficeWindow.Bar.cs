using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    internal sealed partial class MetOfficeWindow
    {
        private static readonly MetControl[] Bar =
        {
            MetControl.Hold, MetControl.PaceUp, MetControl.StoryNext, MetControl.PinSelected,
            MetControl.Front5, MetControl.Reroll, MetControl.RainAuto, MetControl.ScenReset,
        };
        private static readonly string[] BarName = { "HOLD", "PACE", "STORY", "PIN SKY", "FRONT IN 5", "RE-ROLL", "RAIN", "RESET ALL" };
        private static readonly string[] BarKeys = { "H", ", .", "S", "P", "F", "R", "W", "BKSP" };
        private static readonly KeyCode[] HotKeys = { KeyCode.H, KeyCode.Comma, KeyCode.Period, KeyCode.S, KeyCode.P, KeyCode.F, KeyCode.R, KeyCode.Backspace };
        private static readonly MetControl[] HotControls =
        {
            MetControl.Hold, MetControl.PaceDown, MetControl.PaceUp, MetControl.StoryNext,
            MetControl.PinSelected, MetControl.Front5, MetControl.Reroll, MetControl.ScenReset,
        };
        private AvControl[] barButtons;

        partial void BuildCommandBar()
        {
            RectTransform r = window.Root;
            TMP_Text prompt = AvText.Make(r, "CommandPrompt", AvTextRole.Micro, "MET>", TextAlignmentOptions.Center);
            AvLay.Place(prompt, MetOfficeLayout.Pad, MetOfficeLayout.CommandTop, 34f, MetOfficeLayout.Command);
            prompt.color = EnvInk.Role("key");
            var specs = new AvControl.Spec[Bar.Length];
            for (int i = 0; i < Bar.Length; i++)
            {
                MetControl id = Bar[i];
                Action click = id == MetControl.RainAuto ? (Action)CycleRain : id == MetControl.PaceUp ? (Action)PaceWrap : () => Press(id);
                specs[i] = new AvControl.Spec(BarKeys[i] + "  " + BarName[i] + "\n" + MetCatalog.EffectWord(MetCatalog.Info(id).Effect), click,
                    id == MetControl.ScenReset ? AvButtonStyle.Danger : AvButtonStyle.Default);
            }
            var bar = new MetControlGrid(r, specs, Bar.Length, MetOfficeLayout.Command);
            bar.Place(new AvSlot(MetOfficeLayout.Pad + 38f, MetOfficeLayout.CommandTop, MetOfficeLayout.Width - 2f * MetOfficeLayout.Pad - 38f, MetOfficeLayout.Command));
            window.Ticker.Register(bar);
            barButtons = bar.Controls;
            for (int i = 0; i < Bar.Length; i++) repaint.Track(MetRail.Attach(barButtons[i].Rect, MetCatalog.Info(Bar[i]).Effect));
            probes["bar"] = () => barButtons.Length;                       // the harness counts and clicks the command line through the shell's probes
            clicks["bar"] = i => ClickControl(barButtons[i]);
        }

        /// <summary>The bar's one PACE button: a step faster and, from the fastest pace, back to the slowest, so it does not go dead while a slower pace is valid (, and . step down and up).</summary>
        private void PaceWrap()
        {
            source.Fill(model);
            if (model.Pace >= TapePace.MaxPace && MetOfficeRules.State(MetControl.PaceDown, model).Enabled)
            {
                commands.SetPace(TapePace.MinPace);
                nextRefresh = 0f;
                return;
            }
            Press(MetControl.PaceUp);
        }

        partial void Keys()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            for (int i = 0; i < HotKeys.Length; i++)
                if (Input.GetKeyDown(HotKeys[i])) Press(HotControls[i]);
            if (Input.GetKeyDown(KeyCode.W)) CycleRain();
        }

        partial void RefreshCommandBar()
        {
            for (int i = 0; i < Bar.Length; i++)
            {
                MetControl id = Bar[i];
                MetState s = MetOfficeRules.State(id, model);
                bool pace = id == MetControl.PaceUp;
                bool usable = pace ? s.Enabled || MetOfficeRules.State(MetControl.PaceDown, model).Enabled : s.Enabled;
                barButtons[i].Interactable = usable;
                // The bar's PACE and RAIN buttons are not the controls whose tips they would otherwise show (PACE + / RAIN AUTO): they wrap and they cycle, and the tip says so.
                barButtons[i].Help = pace && usable ? MetCatalog.BarPaceHelp : id == MetControl.RainAuto && s.Enabled ? MetCatalog.BarRainHelp : MetOfficeRules.Tip(id, model);
                string effect = id == MetControl.RainAuto ? RainWord() : s.Enabled || MetCatalog.Info(id).Avail == MetAvail.Live ? MetCatalog.EffectWord(MetCatalog.Info(id).Effect)
                    : MetCatalog.AvailTag(MetCatalog.Info(id).Avail) + " · DISABLED";
                Label(barButtons[i], BarKeys[i] + "  " + (id == MetControl.Hold ? MetOfficeText.HoldLabel(model.Running).Replace(" TAPE", "") : BarName[i]) + "\n" + effect);
            }
        }

        private string RainWord()
        {
            int now = 0;
            for (int i = 0; i < MetCatalog.RainValues.Length; i++) if (RainMatches(i)) now = i;
            return MetCatalog.RainNames[now] + " › " + MetCatalog.RainNames[(now + 1) % MetCatalog.RainNames.Length];
        }
    }
}
