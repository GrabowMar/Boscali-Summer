using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Configuration;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Reads the wing key and its chords (spec 2026-10-04 §2): while the key (or its joystick button) is held, 1-6 pick a
    /// stance, a command-card letter gives that order to the wing, and Enter opens the Call Ladder, whose digits then walk it.
    /// Keys are read only while the wing key is held, so no key is taken from the game otherwise; text being typed in the WMC
    /// swallows everything (<see cref="ChordResolver"/>).</summary>
    internal static class WingChordInput
    {
        private static readonly KeyCode[] letterCodes = BuildLetterCodes();
        private static string hotasText;
        private static HotasBinding hotas;
        private static bool hotasBound;

        /// <summary>The wing key is held now (the HUD shows the ladder in place of the strip).</summary>
        public static bool Held { get; private set; }

        /// <summary>The hotkeys went off while the key was held (review fix): nothing stays shown.</summary>
        public static void Release() => Held = false;

        public static void Tick(WingConfig s, bool typing)
        {
            bool held = !typing && (KeyHeld(s) || HotasHeld(s));
            if (!held)
            {
                if (Held)
                {
                    Held = false;
                    WingCallLadder.Close();
                }
                return;
            }
            Held = true;
            KeyCode wingKey = s.KeyWing.Value.MainKey;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (WingCallLadder.Open) WingCallLadder.Close();
                else WingCallLadder.Begin();
                return;
            }
            for (int d = 0; d <= 9; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + d) && !Input.GetKeyDown(KeyCode.Keypad0 + d)) continue;
                Chord c = ChordResolver.Resolve(true, typing, WingCallLadder.Open, (char)('0' + d));
                if (c.Kind == ChordKind.Ladder) WingCallLadder.Press(c.Index);
                else if (c.Kind == ChordKind.Stance) WingCallLadder.RunStance(c.Index);
            }
            string keys = ChordResolver.OrderKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (letterCodes[i] == wingKey || !Input.GetKeyDown(letterCodes[i])) continue;
                Chord c = ChordResolver.Resolve(true, typing, WingCallLadder.Open, keys[i]);
                if (c.Kind == ChordKind.Order) WingCallLadder.RunOrderKey(c.Index);
            }
        }

        private static bool KeyHeld(WingConfig s) => s.KeyWing.Value.MainKey != KeyCode.None && s.KeyWing.Value.IsPressed();

        private static bool HotasHeld(WingConfig s)
        {
            string text = s.HotasWingKey.Value;
            if (text != hotasText)
            {
                hotasText = text;
                hotasBound = HotasBinding.TryParse(text, out hotas);
            }
            if (!hotasBound || !Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
            var sticks = Rewired.ReInput.controllers.Joysticks;
            if (sticks == null) return false;
            for (int j = 0; j < sticks.Count; j++)
            {
                Rewired.Joystick stick = sticks[j];
                if (stick != null && hotas.Button <= stick.buttonCount && hotas.Matches(stick.name) && stick.GetButton(hotas.Button - 1)) return true;
            }
            return false;
        }

        /// <summary>The Unity key of each <see cref="ChordResolver.OrderKeys"/> character (a letter or the comma).</summary>
        private static KeyCode[] BuildLetterCodes()
        {
            string keys = ChordResolver.OrderKeys;
            var codes = new KeyCode[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                codes[i] = keys[i] == ',' ? KeyCode.Comma : KeyCode.A + (keys[i] - 'A');
            return codes;
        }
    }
}
