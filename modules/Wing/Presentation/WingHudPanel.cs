using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>Compact wing strip on the game's HUD canvas (spec §8; restyled with the kit's tokens, spec 2026-10-04 §2):
    /// <list type="bullet">
    /// <item>the shape, spacing, doctrine and how many wingmen are in their slot;</item>
    /// <item>the autopilot annunciator;</item>
    /// <item>one row per wingman: a state lamp, phase (JOIN/SLOT/HOLD/BEHIND…), slot error with a slot-keeping bar, and
    /// the limit that binds it;</item>
    /// <item>up to two ack chips (WILCO green, UNABLE red) for three seconds under the rows.</item>
    /// </list>
    /// Look (2026-10-07, base-game aligned): the native HUD's chamfered black slab and the game's own HUD theme ink
    /// (<see cref="ThemeManager"/>), square status lamps like the native status rows; the edge rail lights in the worst
    /// member's tone -- amber for BEHIND/RTB/REFIT/MNVR or bingo inside two minutes, red for GCAS/COLL/DEFEND.
    /// Not configurable: it shows whenever there is a wing, an engaged autopilot or a held wing key.
    /// While the wing key is held the same box shows the Call Ladder instead (<see cref="WingCallLadder"/>). Refreshes at 5 Hz
    /// (at once when the ladder moves).</summary>
    internal sealed class WingHudPanel : IWingService
    {
        private const float Width = 250f, MaxWidth = 500f, LineHeight = 17f, Pad = 8f, ChipHeight = 16f;
        private const float LampSize = 6f, LampGap = 6f, BarWidth = 40f, BarHeight = 3f, BarColumn = BarWidth + 10f;
        /// <summary>Slot error that fills the slot-keeping bar; in-slot under <see cref="InSlotM"/>.</summary>
        private const float BarFullM = 400f, InSlotM = 60f;
        private const float BingoWarnSeconds = 120f;
        private static readonly Vector2 Anchor = new Vector2(24f, 180f);

        public string Name => "HUD";

        private RectTransform root;
        private Canvas canvas;
        private Image background;
        private Image rail;
        private TMP_Text title, autopilot;
        private readonly TMP_Text[] rows = new TMP_Text[FormationCatalog.MaxSlots];
        private readonly Image[] lamps = new Image[FormationCatalog.MaxSlots];
        private readonly Image[] tracks = new Image[FormationCatalog.MaxSlots];
        private readonly Image[] bars = new Image[FormationCatalog.MaxSlots];
        private readonly AvFrame[] chipFrames = new AvFrame[AckFeed.MaxChips];
        private readonly TMP_Text[] chipText = new TMP_Text[AckFeed.MaxChips];
        private readonly AckLine[] chips = new AckLine[AckFeed.MaxChips];
        private readonly TMP_Text[] ladder = new TMP_Text[WingCallLadder.MaxLines];
        private readonly string[] ladderLines = new string[WingCallLadder.MaxLines];
        private float nextRefresh;
        private int ladderVersion = -1;
        private bool ladderShown;
        private int shownMembers;

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
            if (hud == null || !hud.isActiveAndEnabled || !any)
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
            if (root.anchoredPosition != Anchor) root.anchoredPosition = Anchor;
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
            root.anchoredPosition = Anchor;
            background = HudSprites.Child(root, "Plate", HudSprites.Chamfer, sliced: true);
            background.color = new Color(0.01f, 0.02f, 0.03f, 0.78f);
            // A kit accent rail down the left edge says it is the wing's.
            rail = WingUi.Rule(root, new Rect(0f, -4f, 2f, LineHeight * 2f), AvTheme.Accent);
            rail.raycastTarget = false;
            float inner = Width - Pad - 6f;
            title = WingUi.Label(root, "", new Rect(Pad, -3f, inner, LineHeight), AvTheme.Accent, AvTokens.FontBody,
                FontStyles.Bold, TextAlignmentOptions.Left);
            autopilot = WingUi.Label(root, "", new Rect(Pad, -3f - LineHeight, inner, LineHeight), AvTheme.Dim,
                AvTokens.FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = -3f - LineHeight * (i + 2);
                rows[i] = WingUi.Label(root, "", new Rect(Pad + LampSize + LampGap, y, inner, LineHeight),
                    WingUi.TextPrimary, AvTokens.FontBody, FontStyles.Normal, TextAlignmentOptions.Left);
                lamps[i] = Owned("Lamp", AvSprites.White);
                WmcDraw.Place(lamps[i].rectTransform, new Rect(Pad, y - (LineHeight - LampSize) * 0.5f, LampSize, LampSize));
                tracks[i] = Owned("Track", AvSprites.White);
                bars[i] = HudSprites.Child(tracks[i].rectTransform, "Bar", AvSprites.White, sliced: false);
                bars[i].type = Image.Type.Filled;
                bars[i].fillMethod = Image.FillMethod.Horizontal;
                bars[i].fillOrigin = 0;
                // Right-pinned: Size() widens the strip and the bar follows the right edge.
                RectTransform t = tracks[i].rectTransform;
                t.anchorMin = t.anchorMax = new Vector2(1f, 1f);
                t.pivot = new Vector2(1f, 0.5f);
                t.sizeDelta = new Vector2(BarWidth, BarHeight);
                t.anchoredPosition = new Vector2(-Pad, y - LineHeight * 0.5f);
            }
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

        private Image Owned(string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static ColorTheme Theme => ThemeManager.Active != null ? ThemeManager.Active.ColorTheme : null;
        private static Color Ink => Theme != null ? Theme.AllClear : AvTheme.Accent;
        private static Color InkDim => Ink.WithAlpha(0.7f);
        private static Color Caution => Theme != null ? Theme.Warning : AvTheme.Warning;
        private static Color Danger => Theme != null ? Theme.Alert : AvTheme.Alert;

        /// <summary>0 normal, 1 caution, 2 danger, from what the row says.</summary>
        private static int Severity(string phase, string binding, float bingoSeconds)
        {
            if (binding == "GCAS" || binding == "COLL" || phase == "DEFEND") return 2;
            if (phase == "BEHIND" || phase == "RTB" || phase == "REFIT" || phase == "MNVR" || bingoSeconds < BingoWarnSeconds) return 1;
            return 0;
        }

        private static Color LampColor(string phase, int severity)
        {
            if (severity == 2 || phase == "FIGHT") return Danger;
            if (severity == 1) return Caution;
            return phase == "SLOT" ? Ink : InkDim;
        }

        private void Refresh(WingService wing, PlayerAutopilot ap)
        {
            SetLadder(false);
            int n = wing != null ? Mathf.Min(wing.Members.Count, rows.Length) : 0;
            shownMembers = n;
            int worst = 0, inSlot = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                bool on = i < n;
                if (lamps[i].gameObject.activeSelf != on) lamps[i].gameObject.SetActive(on);
                if (tracks[i].gameObject.activeSelf != on) tracks[i].gameObject.SetActive(on);
                if (!on)
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
                float bingo = m.Bingo.SecondsToBingo;
                rows[i].text = WingHudText.Member(m.Seat, phase, error, binding, WingHudText.BingoTime(bingo));
                int severity = Severity(phase, binding, bingo);
                worst = Mathf.Max(worst, severity);
                if (phase == "SLOT" && error < InSlotM) inSlot++;
                rows[i].color = severity == 2 ? Danger : severity == 1 ? Caution : Ink;
                lamps[i].color = LampColor(phase, severity);
                Color barColor = error < InSlotM ? Ink : error < BarFullM * 0.5f ? InkDim : Caution;
                bars[i].fillAmount = Mathf.Max(Mathf.Clamp01(error / BarFullM), 0.04f);
                bars[i].color = barColor;
                tracks[i].color = new Color(barColor.r * 0.15f, barColor.g * 0.15f, barColor.b * 0.15f, 0.8f);
            }

            FormationSelection sel = wing?.Selection;
            title.text = n > 0 && sel != null
                ? $"{sel.Current.Name.ToUpperInvariant()}  {sel.Spacing.ToString().ToUpperInvariant()}  {wing.Doctrine.PatternName}  {inSlot}/{n} SLOT"
                : "AUTOPILOT";
            autopilot.text = ap != null
                ? WingHudText.Autopilot(ap.Session.Spec, ap.Session.LateralOverride, ap.Session.VerticalOverride, ap.Nav.Index, ap.Nav.Count)
                : "";
            Color lit = worst == 2 ? Danger : worst == 1 ? Caution : Ink;
            rail.color = lit;
            title.color = Ink;
            autopilot.color = InkDim;

            float height = LineHeight * (2 + n) + 8f;
            int shown = WingAcks.Feed.Chips(Time.unscaledTime, chips);
            for (int i = 0; i < chipFrames.Length; i++)
            {
                bool on = i < shown;
                chipFrames[i].gameObject.SetActive(on);
                chipText[i].gameObject.SetActive(on);
                if (!on) continue;
                // Word and colour: WILCO green, UNABLE red.
                Color state = chips[i].Accepted ? Ink : Danger;
                chipText[i].text = chips[i].Chip();
                chipText[i].color = state;
                chipFrames[i].Paint(state.WithAlpha(0.16f), state.WithAlpha(0.6f));
                float w = Mathf.Min(Width - Pad - 6f, chipText[i].preferredWidth + 12f);
                WmcDraw.Place(chipFrames[i].rectTransform, new Rect(Pad, -height - 2f, w, ChipHeight));
                WmcDraw.Place(chipText[i].rectTransform, new Rect(Pad + 6f, -height - 2f, w - 8f, ChipHeight));
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
                ladder[i].color = i == 0 ? Ink : readback ? (chips[0].Accepted ? Ink : Danger) : InkDim;
            }
            rail.color = Ink;
            Size(LineHeight * n + 8f);
        }

        private void SetLadder(bool on)
        {
            title.gameObject.SetActive(!on);
            autopilot.gameObject.SetActive(!on);
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].gameObject.SetActive(!on);
                if (on)
                {
                    lamps[i].gameObject.SetActive(false);
                    tracks[i].gameObject.SetActive(false);
                }
            }
            if (!on) return;
            shownMembers = 0;
            for (int i = 0; i < chipFrames.Length; i++)
            {
                chipFrames[i].gameObject.SetActive(false);
                chipText[i].gameObject.SetActive(false);
            }
        }

        /// <summary>The strip is as wide as its longest shown line plus the bar column, <see cref="Width"/> to
        /// <see cref="MaxWidth"/> (review fix: a full shape name, the AP line or a member row with bingo and a binding
        /// overflowed the fixed 250 px).</summary>
        private void Size(float height)
        {
            float need = 0f, rowNeed = 0f;
            Fit(title, ref need);
            Fit(autopilot, ref need);
            for (int i = 0; i < rows.Length; i++) Fit(rows[i], ref rowNeed);
            for (int i = 0; i < ladder.Length; i++) Fit(ladder[i], ref need);
            if (shownMembers > 0) need = Mathf.Max(need, rowNeed + LampSize + LampGap + BarColumn);
            float w = Mathf.Clamp(need + Pad + 8f, Width, MaxWidth);
            float inner = w - Pad - 6f;
            Inner(title, inner);
            Inner(autopilot, inner);
            for (int i = 0; i < rows.Length; i++) Inner(rows[i], inner - LampSize - LampGap - BarColumn);
            for (int i = 0; i < ladder.Length; i++) Inner(ladder[i], inner);
            rail.rectTransform.sizeDelta = new Vector2(2f, height - 8f);
            root.sizeDelta = new Vector2(w, height);
        }

        private static void Fit(TMP_Text t, ref float need)
        {
            if (t != null && t.gameObject.activeSelf && !string.IsNullOrEmpty(t.text)) need = Mathf.Max(need, t.preferredWidth);
        }

        private static void Inner(TMP_Text t, float width)
        {
            if (t == null) return;
            RectTransform r = t.rectTransform;
            if (!Mathf.Approximately(r.sizeDelta.x, width)) r.sizeDelta = new Vector2(width, r.sizeDelta.y);
        }

        private void Reset()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvas = null;
            nextRefresh = 0f;
            ladderVersion = -1;
            shownMembers = 0;
        }
    }
}
