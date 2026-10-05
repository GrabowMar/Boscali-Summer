using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    /// <summary>Compact wing strip on the game's HUD canvas (spec §8; restyled with the kit's tokens, spec 2026-10-04 §2):
    /// <list type="bullet">
    /// <item>the shape and spacing;</item>
    /// <item>the autopilot annunciator;</item>
    /// <item>one row per wingman: phase (JOIN/SLOT/HOLD/BEHIND), slot error, and the limit that binds it;</item>
    /// <item>up to two ack chips (WILCO green, UNABLE red) for three seconds under the rows.</item>
    /// </list>
    /// While the wing key is held the same box shows the Call Ladder instead (<see cref="WingCallLadder"/>). Refreshes at 5 Hz
    /// (at once when the ladder moves) and hides when there is neither a wing nor an engaged autopilot.</summary>
    internal sealed class WingHudPanel : IWingService
    {
        private const float Width = 250f, LineHeight = 17f, Pad = 8f, ChipHeight = 16f;

        public string Name => "HUD";

        private RectTransform root;
        private Canvas canvas;
        private AvFrame background;
        private Image rail;
        private TMP_Text title, autopilot;
        private readonly TMP_Text[] rows = new TMP_Text[FormationCatalog.MaxSlots];
        private readonly AvFrame[] chipFrames = new AvFrame[AckFeed.MaxChips];
        private readonly TMP_Text[] chipText = new TMP_Text[AckFeed.MaxChips];
        private readonly AckLine[] chips = new AckLine[AckFeed.MaxChips];
        private readonly TMP_Text[] ladder = new TMP_Text[WingCallLadder.MaxLines];
        private readonly string[] ladderLines = new string[WingCallLadder.MaxLines];
        private float nextRefresh;
        private int ladderVersion = -1;
        private bool ladderShown;

        public void Activate() => Reset();

        public void Deactivate() => Reset();

        public void FixedTick(float dt)
        {
        }

        public void Tick(float dt)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            WingService wing = WingService.Instance;
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            bool any = (wing != null && wing.Members.Count > 0) || (ap != null && ap.Session.Engaged) || WingChordInput.Held;
            if (!WingSettings.Instance.ShowHud.Value || hud == null || !hud.isActiveAndEnabled || !any)
            {
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }
            Canvas c = hud.GetComponentInParent<Canvas>();
            if (c == null) return;
            if (root == null || canvas != c)
            {
                Reset();
                Build(hud, c);
            }
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.anchoredPosition = new Vector2(24f + WingSettings.Instance.HudX.Value, 180f + WingSettings.Instance.HudY.Value);
            if (Time.unscaledTime < nextRefresh && ladderVersion == WingCallLadder.Version && ladderShown == WingChordInput.Held) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            ladderVersion = WingCallLadder.Version;
            ladderShown = WingChordInput.Held;
            if (ladderShown) RefreshLadder();
            else Refresh(wing, ap);
        }

        private void Build(CombatHUD hud, Canvas c)
        {
            TMP_Text template = hud.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (template != null) WingUi.Font = template.font;
            canvas = c;
            var go = new GameObject("WingCommand_HudPanel", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(c.transform, worldPositionStays: false);
            root.SetAsLastSibling();
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            background = WingUi.Panel(root, new Rect(0f, 0f, Width, LineHeight * 2f), AvTheme.Unity(AvTokens.HudPanel));
            background.raycastTarget = false;
            // A kit accent rail down the left edge says it is the wing's.
            rail = WingUi.Rule(root, new Rect(0f, 0f, 2f, LineHeight * 2f), AvTheme.Accent);
            rail.raycastTarget = false;
            float inner = Width - Pad - 6f;
            title = WingUi.Label(root, "", new Rect(Pad, -3f, inner, LineHeight), AvTheme.Accent, AvTokens.FontBody,
                FontStyles.Bold, TextAlignmentOptions.Left);
            autopilot = WingUi.Label(root, "", new Rect(Pad, -3f - LineHeight, inner, LineHeight), AvTheme.Dim,
                AvTokens.FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
            for (int i = 0; i < rows.Length; i++)
                rows[i] = WingUi.Label(root, "", new Rect(Pad, -3f - LineHeight * (i + 2), inner, LineHeight),
                    WingUi.TextPrimary, AvTokens.FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
            for (int i = 0; i < chipFrames.Length; i++)
            {
                chipFrames[i] = WingUi.Panel(root, new Rect(Pad, 0f, 120f, ChipHeight), Color.clear);
                chipFrames[i].raycastTarget = false;
                chipText[i] = WingUi.Label(root, "", new Rect(Pad + 5f, 0f, inner - 10f, ChipHeight), WingUi.TextPrimary,
                    AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Left);
                chipFrames[i].gameObject.SetActive(false);
                chipText[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < ladder.Length; i++)
            {
                ladder[i] = WingUi.Label(root, "", new Rect(Pad, -3f - LineHeight * i, inner, LineHeight),
                    WingUi.TextPrimary, AvTokens.FontBody, i == 0 ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.Left);
                ladder[i].color = i == 0 ? AvTheme.Accent : WingUi.TextPrimary;
                ladder[i].gameObject.SetActive(false);
            }
        }

        private void Refresh(WingService wing, PlayerAutopilot ap)
        {
            SetLadder(false);
            int n = wing != null ? wing.Members.Count : 0;
            FormationSelection sel = wing?.Selection;
            title.text = n > 0 && sel != null
                ? $"WING  {sel.Current.Name.ToUpperInvariant()}  {sel.Spacing.ToString().ToUpperInvariant()}  {wing.Doctrine.PatternName}"
                : "AUTOPILOT";
            autopilot.text = ap != null
                ? WingHudText.Autopilot(ap.Session.Spec, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count)
                : "";
            for (int i = 0; i < rows.Length; i++)
            {
                if (i >= n)
                {
                    rows[i].text = "";
                    continue;
                }
                WingMember m = wing.Members[i];
                bool behind = m.Brain.LastRejoin.FallingBehind;
                string phase = WingHudText.Duty(m.Engaged, m.Recovery != null, m.Recovery != null ? m.Recovery.Intent : RecoveryIntent.Rtb)
                               ?? (m.Settle != null ? WingHudText.Settle(m.Settle.Phase) : null)
                               ?? WingHudText.Phase(m.Brain.Mind.Current, behind);
                string binding = WingHudText.Binding(m.Brain.Pipeline.Report);
                WingFrame f = wing.FrameOf(m);
                float error = f != null && m.Brain.Slot < f.Count ? (f.Slots[m.Brain.Slot].Ref.Pos - m.Last.Pos).Length : 0f;
                rows[i].text = WingHudText.Member(m.Seat, phase, error, binding, WingHudText.BingoTime(m.Bingo.SecondsToBingo));
                rows[i].color = behind || binding == "GCAS" || binding == "COLL" ? AvTheme.Warning : WingUi.TextPrimary;
            }
            float height = LineHeight * (2 + n) + 6f;
            int shown = WingAcks.Feed.Chips(Time.unscaledTime, chips);
            for (int i = 0; i < chipFrames.Length; i++)
            {
                bool on = i < shown;
                chipFrames[i].gameObject.SetActive(on);
                chipText[i].gameObject.SetActive(on);
                if (!on) continue;
                // Word and colour: WILCO green, UNABLE red.
                Color state = chips[i].Accepted ? AvTheme.RailReady : AvTheme.RailDanger;
                chipText[i].text = chips[i].Chip();
                chipText[i].color = state;
                chipFrames[i].Paint(state.WithAlpha(0.16f), state.WithAlpha(0.6f));
                float w = Mathf.Min(Width - Pad - 6f, chipText[i].preferredWidth + 12f);
                WingUi.Place(chipFrames[i].rectTransform, new Rect(Pad, -height - 2f, w, ChipHeight));
                WingUi.Place(chipText[i].rectTransform, new Rect(Pad + 6f, -height - 2f, w - 8f, ChipHeight));
                height += ChipHeight + 3f;
            }
            if (shown > 0) height += 3f;
            Size(height);
        }

        /// <summary>The Call Ladder in the strip's place: the same box, the same anchor, taller as the choices need.</summary>
        private void RefreshLadder()
        {
            SetLadder(true);
            int n = WingCallLadder.Lines(ladderLines, Time.unscaledTime);
            int shown = WingAcks.Feed.Chips(Time.unscaledTime, chips);
            for (int i = 0; i < ladder.Length; i++)
            {
                bool on = i < n;
                ladder[i].gameObject.SetActive(on);
                if (!on) continue;
                ladder[i].text = ladderLines[i];
                // The last line is the readback when an ack chip is live: green WILCO, red UNABLE.
                bool readback = i == n - 1 && i > 0 && shown > 0 && ladderLines[i] == chips[0].Chip();
                ladder[i].color = i == 0 ? AvTheme.Accent : readback ? (chips[0].Accepted ? AvTheme.RailReady : AvTheme.RailDanger) : WingUi.TextPrimary;
            }
            Size(LineHeight * n + 6f);
        }

        private void SetLadder(bool on)
        {
            title.gameObject.SetActive(!on);
            autopilot.gameObject.SetActive(!on);
            for (int i = 0; i < rows.Length; i++) rows[i].gameObject.SetActive(!on);
            if (!on) return;
            for (int i = 0; i < chipFrames.Length; i++)
            {
                chipFrames[i].gameObject.SetActive(false);
                chipText[i].gameObject.SetActive(false);
            }
        }

        private void Size(float height)
        {
            background.rectTransform.sizeDelta = new Vector2(Width, height);
            rail.rectTransform.sizeDelta = new Vector2(2f, height);
            root.sizeDelta = new Vector2(Width, height);
        }

        private void Reset()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvas = null;
            nextRefresh = 0f;
            ladderVersion = -1;
        }
    }
}
