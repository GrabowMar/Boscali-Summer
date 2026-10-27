using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// The script's in-flight pressure: a countdown at the top of the screen (amber, red in its last 30 seconds, blinking in its last 10) and a named enemy's
    /// health bar at the top right. Both stand aside while a cutscene plays. Built the first time a script asks for one.
    /// </summary>
    internal sealed partial class ScenarioStageView
    {
        private const float AceWidth = 440f;

        private RectTransform clockBox, aceBox;
        private Image clockPlate, acePlate, aceTrack, aceFill;
        private TextMeshProUGUI clockLabel, clockDigits, aceName, aceLine;
        private float clockEnds = -1f, aceHealth = 1f;
        private bool aceOn;

        /// <summary>A countdown ending at game time <paramref name="endsAt"/>; a null label takes it down.</summary>
        internal void Countdown(string label, float endsAt)
        {
            if (label == null) { clockEnds = -1f; return; }
            BuildCombat();
            clockLabel.text = label.ToUpperInvariant();
            clockEnds = endsAt;
        }

        /// <summary>A named enemy's bar; a null callsign takes it down.</summary>
        internal void Ace(string callsign, string line)
        {
            aceOn = callsign != null;
            if (!aceOn) return;
            BuildCombat();
            aceName.text = callsign;
            aceLine.text = line ?? "";
            aceHealth = 1f;
        }

        /// <summary>The ace's health share; a negative share (not in the world yet) reads as full.</summary>
        internal void AceHealth(float share) => aceHealth = share < 0f ? 1f : Mathf.Clamp01(share);

        private bool CombatShown => !cut && (clockEnds >= 0f || aceOn);

        private void BuildCombat()
        {
            if (clockBox != null) return;
            clockBox = AvLay.Child(root, "Countdown");
            Anchor(clockBox, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(300f, 82f));
            clockPlate = AvLay.Solid(clockBox, "Plate", Color.clear);
            AvLay.Fill(clockPlate.rectTransform);
            clockLabel = Text(clockBox, "Label", AvTextRole.Label, 15f, TextAlignmentOptions.Top, false);
            clockLabel.characterSpacing = 6f;
            Anchor(clockLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(0f, 20f));
            clockDigits = Text(clockBox, "Digits", AvTextRole.DataStrong, 44f, TextAlignmentOptions.Bottom, false);
            Anchor(clockDigits.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(0f, 52f));

            aceBox = AvLay.Child(root, "Ace");
            Anchor(aceBox, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-48f, -120f), new Vector2(AceWidth, 78f));
            acePlate = AvLay.Solid(aceBox, "Plate", Color.clear);
            AvLay.Fill(acePlate.rectTransform);
            aceName = Text(aceBox, "Callsign", AvTextRole.Head, 24f, TextAlignmentOptions.TopLeft, false);
            aceName.characterSpacing = 4f;
            Anchor(aceName.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(-32f, 30f));
            aceLine = Text(aceBox, "Line", AvTextRole.Label, 14f, TextAlignmentOptions.TopLeft, false);
            Anchor(aceLine.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(16f, -38f), new Vector2(-32f, 18f));
            aceTrack = AvLay.Solid(aceBox, "Track", Color.clear);
            Anchor(aceTrack.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 10f), new Vector2(AceWidth - 32f, 8f));
            aceFill = AvLay.Solid(aceBox, "Fill", Color.clear);
            Anchor(aceFill.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 10f), new Vector2(AceWidth - 32f, 8f));
        }

        private void PaintCombat(float game, float real)
        {
            if (clockBox == null) return;
            float clockAlpha = !cut && clockEnds >= 0f ? 1f : 0f;
            float left = Mathf.Max(0f, clockEnds - game);
            Color tone = left < 30f ? AvTheme.Hostile : AvTheme.Warning;
            // The last ten seconds blink twice a second.
            float blink = left < 10f && Mathf.Repeat(real * 2f, 1f) > 0.6f ? 0.35f : 1f;
            clockPlate.color = AvTheme.Ground.WithAlpha(0.72f * clockAlpha);
            clockLabel.color = AvTheme.Dim.WithAlpha(clockAlpha);
            clockDigits.color = tone.WithAlpha(clockAlpha * blink);
            if (clockAlpha > 0f)
            {
                int tenths = Mathf.FloorToInt(left * 10f);
                clockDigits.text = (tenths / 600).ToString("00") + ":" + (tenths / 10 % 60).ToString("00") + "." + (tenths % 10);
            }

            float aceAlpha = !cut && aceOn ? 1f : 0f;
            acePlate.color = AvTheme.Ground.WithAlpha(0.72f * aceAlpha);
            aceName.color = AvTheme.Hostile.WithAlpha(aceAlpha);
            aceLine.color = AvTheme.Dim.WithAlpha(aceAlpha);
            aceTrack.color = AvTheme.Dim.WithAlpha(0.3f * aceAlpha);
            aceFill.color = AvTheme.Hostile.WithAlpha(aceAlpha);
            aceFill.rectTransform.sizeDelta = new Vector2((AceWidth - 32f) * aceHealth, 8f);
        }
    }
}
