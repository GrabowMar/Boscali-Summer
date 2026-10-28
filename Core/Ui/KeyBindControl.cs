using System;
using BepInEx.Configuration;
using NOAvionics;
using Rewired;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// A keyboard binding shown and changed in place: click the button, then press a key. Esc keeps the old key,
    /// Backspace or Delete leaves it unbound. The note under it names the game control already on that key, because a
    /// press then does both. SET and the mission prompt both use it, so anywhere the mod names a key it can change it.
    /// </summary>
    internal sealed class KeyBindControl : MonoBehaviour
    {
        private static KeyCode[] keyboard;
        private ConfigEntry<KeyCode> entry;
        private AvControl control;
        private AvNote note;
        private string label;
        private bool listening;
        private int listenFrame;
        private KeyCode shown = (KeyCode)(-1);

        public static void Add(AvFlow flow, ClientKeySetting key)
        {
            KeyBindControl bind = null;
            AvButtons row = flow.Buttons(new AvControl.Spec(key.Label, () => bind.Toggle(), icon: AvIcon.AdjustmentsHorizontal));
            bind = row.Controls[0].gameObject.AddComponent<KeyBindControl>();
            bind.entry = key.Entry;
            bind.control = row.Controls[0];
            bind.label = key.Label;
            bind.control.Help = key.Help + " Click, then press a key. Esc keeps the current key; Backspace or Delete leaves it unbound.";
            bind.note = flow.Add(new AvNote(flow.Content, AvTextRole.DataSmall));
            bind.Show();
        }

        /// <summary>The key as the mod prints it everywhere: "C", "F7", "NONE".</summary>
        public static string Name(KeyCode key) => key == KeyCode.None ? "NONE"
            : key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ? ((int)(key - KeyCode.Alpha0)).ToString()
            : key.ToString().ToUpperInvariant();

        /// <summary>The game's own keyboard action on this key (for example "Next Countermeasure"), or null.</summary>
        public static string GameActionOn(KeyCode key)
        {
            if (key == KeyCode.None) return null;
            try
            {
                Player player = GameManager.playerInput;
                if (player == null) return null;
                foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
                {
                    if (map == null || !map.enabled) continue;
                    foreach (ActionElementMap element in map.AllMaps)
                        if (element != null && element.enabled && element.keyCode == key) return element.actionDescriptiveName;
                }
            }
            catch (Exception)
            {
                // No player input yet (menus) or a Rewired change: the hint is only advice.
            }
            return null;
        }

        private void Toggle()
        {
            listening = !listening;
            listenFrame = Time.frameCount;
            Show();
        }

        private void OnDisable()
        {
            if (!listening) return;
            listening = false;
            Show();
        }

        private void Update()
        {
            if (entry == null) return;
            if (listening && Time.frameCount != listenFrame) Listen();
            if (!listening && entry.Value != shown) Show();
        }

        private void Listen()
        {
            if (!Input.anyKeyDown) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { listening = false; Show(); return; }
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) { Bind(KeyCode.None); return; }
            keyboard ??= Keyboard();
            foreach (KeyCode key in keyboard)
                if (Input.GetKeyDown(key)) { Bind(key); return; }
        }

        private void Bind(KeyCode key)
        {
            listening = false;
            entry.Value = key;
            Show();
        }

        private void Show()
        {
            shown = entry.Value;
            string key = Name(shown);
            control.Label = listening ? "PRESS A KEY (ESC KEEPS " + key + ")" : label + "  " + key;
            control.Latched = listening;
            string game = listening ? null : GameActionOn(shown);
            note.Set(listening ? "Esc keeps the current key; Backspace or Delete leaves it unbound."
                : shown == KeyCode.None ? "Unbound: this key does nothing until you set one."
                : game != null ? key + " is also " + game.ToUpperInvariant() + " in the game's controls: a press does both. Pick another key if that gets in the way."
                : "", game != null ? AvState.Caution : AvState.Inert);
        }

        /// <summary>Keyboard keys only: mouse and joystick buttons cannot be the key.</summary>
        private static KeyCode[] Keyboard()
        {
            var keys = new System.Collections.Generic.List<KeyCode>();
            foreach (KeyCode key in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
                if (key > KeyCode.None && key < KeyCode.Mouse0) keys.Add(key);
            return keys.ToArray();
        }
    }
}
