using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Scenario.Presentation
{
    /// <summary>
    /// The pilot's reply wheel (the script's <c>reply</c>): a prompt over two lines, PARAGON in blue on top (key Y or 1) and RENEGADE in red under it (key N or 2), with a bar
    /// that runs down the time left. It takes no input itself: the host reads the keys. Built the first time a script asks for one.
    /// </summary>
    internal sealed partial class ScenarioStageView
    {
        private const float ReplyWidth = 1040f, ReplyPad = 24f;
        private static readonly Color ParagonTone = new Color(0.36f, 0.64f, 1f, 1f);

        private RectTransform replyBox;
        private Image replyPlate, replyParagonBar, replyRenegadeBar, replyTimer;
        private TextMeshProUGUI replyPrompt, replyParagon, replyRenegade;
        private float replyFrom, replyUntil = -1f;

        private bool ReplyShown => replyUntil >= 0f;

        /// <summary>Shows the offer until game time <paramref name="until"/>; a null paragon line takes it down.</summary>
        internal void Reply(string prompt, string paragon, string renegade, float from, float until)
        {
            if (paragon == null) { replyUntil = -1f; Paint(); return; }
            BuildReply();
            replyPrompt.text = prompt ?? "";
            replyParagon.text = "[Y]  PARAGON   " + paragon;
            replyRenegade.text = "[N]  RENEGADE   " + renegade;
            float width = ReplyWidth - 2f * ReplyPad;
            float promptHeight = string.IsNullOrEmpty(prompt) ? 0f : Mathf.Ceil(replyPrompt.GetPreferredValues(prompt, width, 0f).y) + 10f;
            float paragonHeight = Mathf.Ceil(replyParagon.GetPreferredValues(replyParagon.text, width - 12f, 0f).y) + 14f;
            float renegadeHeight = Mathf.Ceil(replyRenegade.GetPreferredValues(replyRenegade.text, width - 12f, 0f).y) + 14f;
            replyBox.sizeDelta = new Vector2(ReplyWidth, 20f + promptHeight + paragonHeight + renegadeHeight + 14f);
            Anchor(replyPrompt.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(ReplyPad, -12f), new Vector2(-2f * ReplyPad, promptHeight));
            Anchor(replyParagon.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(ReplyPad + 12f, -12f - promptHeight), new Vector2(-2f * ReplyPad - 12f, paragonHeight));
            Anchor(replyParagonBar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ReplyPad, -12f - promptHeight), new Vector2(4f, paragonHeight));
            Anchor(replyRenegade.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(ReplyPad + 12f, -12f - promptHeight - paragonHeight), new Vector2(-2f * ReplyPad - 12f, renegadeHeight));
            Anchor(replyRenegadeBar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ReplyPad, -12f - promptHeight - paragonHeight), new Vector2(4f, renegadeHeight));
            replyFrom = from;
            replyUntil = until;
            Paint();
        }

        private void BuildReply()
        {
            if (replyBox != null) return;
            replyBox = AvLay.Child(root, "Reply");
            Anchor(replyBox, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(ReplyWidth, 160f));
            replyPlate = AvLay.Solid(replyBox, "Plate", Color.clear);
            AvLay.Fill(replyPlate.rectTransform);
            replyPrompt = Text(replyBox, "Prompt", AvTextRole.Label, 18f, TextAlignmentOptions.TopLeft, true);
            replyParagonBar = AvLay.Solid(replyBox, "ParagonBar", Color.clear);
            replyParagon = Text(replyBox, "Paragon", AvTextRole.Prose, 24f, TextAlignmentOptions.TopLeft, true);
            replyRenegadeBar = AvLay.Solid(replyBox, "RenegadeBar", Color.clear);
            replyRenegade = Text(replyBox, "Renegade", AvTextRole.Prose, 24f, TextAlignmentOptions.TopLeft, true);
            replyTimer = AvLay.Solid(replyBox, "Timer", Color.clear);
            Anchor(replyTimer.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(ReplyPad, 8f), new Vector2(ReplyWidth - 2f * ReplyPad, 3f));
        }

        private void PaintReply(float game)
        {
            if (replyBox == null) return;
            float alpha = ReplyShown ? 1f : 0f;
            replyPlate.color = AvTheme.Ground.WithAlpha(0.88f * alpha);
            replyPrompt.color = AvTheme.Dim.WithAlpha(alpha);
            replyParagon.color = ParagonTone.WithAlpha(alpha);
            replyParagonBar.color = ParagonTone.WithAlpha(alpha);
            replyRenegade.color = AvTheme.Hostile.WithAlpha(alpha);
            replyRenegadeBar.color = AvTheme.Hostile.WithAlpha(alpha);
            replyTimer.color = AvTheme.Dim.WithAlpha(0.5f * alpha);
            if (!ReplyShown) return;
            float share = Mathf.Clamp01((replyUntil - game) / Mathf.Max(0.01f, replyUntil - replyFrom));
            replyTimer.rectTransform.sizeDelta = new Vector2((ReplyWidth - 2f * ReplyPad) * share, 3f);
        }
    }
}
