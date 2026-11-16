using BoscaliSummer.Modules.Progression.Domain;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Progression.Presentation
{
    /// <summary>
    /// Passive local threat display for an ace hunt; all encounter state comes from the squad host. It lives in the top-centre lane (<see cref="TopCentre"/>) under the native
    /// HUD canvas, in the HUD's own face and colours and the lane's strokes, brackets, chevrons and motion, the same as the centre WARNING banner under it (2026-10-28 review).
    /// <list type="bullet">
    /// <item>Card (the lane's one slot, 112..204, shared with the WARNING banner, which wins), for <see cref="CardS"/> when the hunt starts: "ENEMY ACE" between two hazard bars (three onset blinks),
    /// the ace's portrait, its callsign typed on with its skill at the right, who flies with it ("TALON 01 · 3/4 UP"), what to expect of it ("TOUGH · NOTCH · RETURNING x2") and a last row with an
    /// arrow at the ace's bearing off our nose, its range and the HEAT.</item>
    /// <item>Strip (a 16-high line under the slot, 208..224) for the rest of the hunt: the wing's crest, "ACE HUNT · VULTURE" (the TAC BOARD's own hunt line, <see cref="AceHuntHudCopy"/>) and how many are up.</item>
    /// <item>Marker on the ace's aircraft (host and solo; a client has no position for it): a diamond with its callsign and range, or a double chevron at the screen's edge.</item>
    /// </list>
    /// </summary>
    internal sealed class AceHuntHud : MonoBehaviour, ISceneService
    {
        private const float CardS = 7f, CloseM = 8000f;
        private static readonly string[] AbilityWords = { "TOUGH", "CM", "NOTCH", "GHOST" };

        /// <summary>A graphic and the palette tone it is painted in; <see cref="Recolor"/> repaints every one from the HUD theme.</summary>
        private enum Tone : byte { Ink, Bright, Caution }
        private struct Tinted { public Graphic Graphic; public Tone Tone; public float Alpha; }
        private readonly System.Collections.Generic.List<Tinted> tinted = new System.Collections.Generic.List<Tinted>(24);
        /// <summary>The card's and the strip's backdrops: the one backdrop of the HUD (<see cref="HudSprites.Backdrop"/>), repainted with the colours.</summary>
        private readonly System.Collections.Generic.List<Image> shades = new System.Collections.Generic.List<Image>(2);

        private ISquadView squad;
        private RectTransform canvasRoot, root, card, strip, marker;
        private CanvasGroup cardGroup, stripGroup;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private TMP_Text header, crown, callsign, skillText, meta, traits, lineText, heatText, stripText, stripCount, markerLabel;
        private Image portrait, stripCrest;
        private AvVector cardLines, stripLines, markerLines;
        private float headerHalf, lastK, stripTextW, stripCountW;
        /// <summary>The ace's bearing off our nose (degrees, + right) while it is known (<see cref="aceHave"/>), and the card's bearing row and heat as last written.</summary>
        private float aceRel;
        private bool aceHave;
        private int lineKey = int.MinValue, heatKey = int.MinValue;
        private string crestKey, portraitKey;
        private int shownHuntId = -1, distanceKey = int.MinValue, markerKey = int.MinValue;
        private bool shown;
        private EnemyWingView shownWing;
        private float introducedAt, nextRefresh;
        private readonly char[] scratch = new char[24];
        private string distance = "";

        public void Configure(ISquadView view) => squad = view;

        private void Update()
        {
            if (Application.isBatchMode) return;
            float now = Time.unscaledTime;
            if (now >= nextRefresh)
            {
                nextRefresh = now + 0.25f;
                Refresh(now);
            }
            if (root == null || !root.gameObject.activeSelf) return;
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            float x = ModuleServices.TryGet(out IHudBoard board) && board.TryGetScreenBox(out float l, out float t, out float r, out float b)
                ? TopCentre.AceShift(Screen.width, Screen.height, l, t, r, b) : 0f;
            card.anchoredPosition = new Vector2(x, 540f - TopCentre.CardTop - TopCentre.CardHeight * 0.5f + TopCentre.Lift);
            strip.anchoredPosition = new Vector2(x, 540f - TopCentre.StripTop - TopCentre.StripHeight * 0.5f + TopCentre.Lift);
            Track(canvasRoot.rect.width / k);      // first: the card's bearing row reads what it found
            Animate(now);
        }

        /// <summary>Four times a second: whether a hunt is showing at all, and its words when they changed.</summary>
        private void Refresh(float now)
        {
            Canvas map = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas;
            if (squad == null || !squad.HuntActive || squad.ActiveEnemyWingIndex < 0 ||
                map != null && map.isActiveAndEnabled ||
                !GameManager.GetLocalPlayer<Player>(out Player player) || player == null ||
                player.Aircraft == null || player.Aircraft.disabled || player.Aircraft.HasEjected() || !Ensure())
            {
                if (root != null) root.gameObject.SetActive(false);
                shown = false;
                TopCentre.CardUp = false;
                return;
            }
            if (shownHuntId != squad.ActiveHuntId)
            {
                shownHuntId = squad.ActiveHuntId;
                introducedAt = now;
            }
            root.gameObject.SetActive(true);
            Recolor();
            EnemyWingView wing = squad.GetEnemyWing(squad.ActiveEnemyWingIndex);
            if (shown && SameWing(shownWing, wing)) return;
            shown = true;
            shownWing = wing;
            string ace = wing.AceName ?? "UNKNOWN";
            int separator = ace.IndexOf(" / ", System.StringComparison.Ordinal);
            string name = separator < 0 ? ace : ace.Substring(0, separator);
            string handle = separator < 0 ? "UNIDENTIFIED" : ace.Substring(separator + 3);
            callsign.text = handle.ToUpperInvariant();
            skillText.text = (wing.Skill ?? "").ToUpperInvariant();
            meta.text = Meta(wing);
            traits.text = Traits(wing);
            stripText.text = AceHuntHudCopy.Text(handle.ToUpperInvariant(), wing.WingName);
            stripCount.text = wing.MemberCount > 0 ? wing.MembersAlive + "/" + wing.MemberCount + " UP" : "";
            stripTextW = stripText.text.Length > 0 ? stripText.GetPreferredValues(stripText.text).x : 0f;
            stripCountW = stripCount.text.Length > 0 ? stripCount.GetPreferredValues(stripCount.text).x : 0f;
            StripRule();
            string crestIdentity = (wing.Symbol ?? string.Empty) + "|" + (wing.WingName ?? string.Empty);
            if (crestKey != crestIdentity)
            {
                crestKey = crestIdentity;
                Sprite mark = EmblemRenderer.Procedural(EmblemDesign.Hostile(crestIdentity));
                stripCrest.sprite = mark;
                stripCrest.enabled = mark != null;
            }
            int faction = PortraitFactions.OpposingLocal;
            string key = ace + "|" + faction;
            if (portraitKey != key)
            {
                portraitKey = key;
                // Borrow the same identity-based portrait as Wing Command; never own/destroy it.
                portrait.sprite = WingLink.PersonnelPortrait(name, separator < 0 ? "" : handle, PortraitRole.Pilot, faction);
                portrait.enabled = portrait.sprite != null;
            }
        }

        /// <summary>"TALON 01 · 3/4 UP": who flies with it (its skill is the card's name row).</summary>
        private static string Meta(EnemyWingView wing)
        {
            var s = new System.Text.StringBuilder(48);
            if (!string.IsNullOrEmpty(wing.WingName)) s.Append(wing.WingName.ToUpperInvariant());
            if (wing.MemberCount > 0) s.Append(s.Length > 0 ? " · " : "").Append(wing.MembersAlive).Append('/').Append(wing.MemberCount).Append(" UP");
            return s.ToString();
        }

        /// <summary>"TOUGH · NOTCH · RETURNING x2": what to expect of it (empty when nothing).</summary>
        private static string Traits(EnemyWingView wing)
        {
            var s = new System.Text.StringBuilder(48);
            for (int i = 0; i < AbilityWords.Length; i++)
                if ((wing.AbilityMask & (1 << i)) != 0) s.Append(s.Length > 0 ? " · " : "").Append(AbilityWords[i]);
            if (wing.Returns > 0) s.Append(s.Length > 0 ? " · " : "").Append("RETURNING x").Append(wing.Returns);
            return s.ToString();
        }

        private static bool SameWing(EnemyWingView a, EnemyWingView b) =>
            a.Tier == b.Tier && a.MembersAlive == b.MembersAlive && a.MemberCount == b.MemberCount &&
            a.Returns == b.Returns && a.AbilityMask == b.AbilityMask &&
            a.Symbol == b.Symbol && a.WingName == b.WingName && a.AceName == b.AceName &&
            a.Skill == b.Skill && a.Status == b.Status;

        /// <summary>Build once per HUD canvas (the native canvas, found as the board finds it); false while there is none.</summary>
        private bool Ensure()
        {
            if (root != null) return true;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Canvas canvas = hud != null && hud.iconLayer != null && hud.iconLayer.gameObject.activeInHierarchy ? hud.iconLayer.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out font, out fontMaterial);
            tinted.Clear();
            shades.Clear();
            root = Holder(canvasRoot, "Boscali Ace Hunt", 0f, 0f, 0f, 0f);
            lastK = 0f;
            // A rebuilt card writes everything again (the old canvas took the texts and pictures with it).
            shown = false;
            crestKey = portraitKey = null;
            distanceKey = markerKey = lineKey = heatKey = int.MinValue;

            // Card (the lane's slot): the header row (the crown and "ENEMY ACE" between two hazard bars), then a bracketed body: the portrait, the callsign with its skill at the right, who flies with it,
            // what to expect, and the bearing / range / HEAT row. Centre origin, y up: the card is 92 high, so its top is y 46.
            float w = TopCentre.Width, h = TopCentre.CardHeight, left = -w * 0.5f, top = h * 0.5f, text = left + 78f, right = w * 0.5f - 8f, headY = top - 7f;
            card = Holder(root, "Ace Card", 0f, 0f, w, h);
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            cardGroup.blocksRaycasts = false;
            Shade(card, w, h);
            cardLines = AvVector.Create(Holder(card, "Lines", 0f, 0f, 0f, 0f), "Lines", 48);      // a zero-size holder: the strokes' origin is the card's centre, as the labels'
            Tint(cardLines, Tone.Caution);
            header = Label(card, "ENEMY ACE", 0f, headY, 120f, TopCentre.LabelPt, Tone.Caution, TextAlignmentOptions.Center, bold: true, spacing: TopCentre.WordSpacing * 0.6f);
            // The header block: a crown, then the words, centred together between the bars.
            float headerText = header.GetPreferredValues(header.text).x, crownSize = TopCentre.LabelPt + 2f;
            headerHalf = (crownSize + 4f + headerText) * 0.5f;
            header.rectTransform.anchoredPosition = new Vector2(headerHalf - headerText * 0.5f, headY);
            crown = AvIcons.Make(card, AvIcon.Crown, crownSize, Color.white);
            crown.rectTransform.anchorMin = crown.rectTransform.anchorMax = crown.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            crown.rectTransform.sizeDelta = new Vector2(crownSize * 1.4f, crownSize * 1.4f);
            crown.rectTransform.anchoredPosition = new Vector2(-headerHalf + crownSize * 0.5f, headY);
            Tint(crown, Tone.Caution);
            float bodyY = (top - 18f - h * 0.5f) * 0.5f;      // the body box runs from 18 under the top to the card's foot
            portrait = ImageBox(card, left + 8f + 30f, bodyY, 60f);
            callsign = Label(card, "", text, top - 32f, w - 160f, TopCentre.NamePt, Tone.Bright, TextAlignmentOptions.MidlineLeft, bold: true, spacing: TopCentre.LineSpacing * 2f);
            skillText = Label(card, "", right, top - 32f, 90f, TopCentre.DetailPt, Tone.Caution, TextAlignmentOptions.MidlineRight, bold: true, spacing: TopCentre.LineSpacing);
            meta = Label(card, "", text, top - 50f, right - text, TopCentre.LabelPt, Tone.Ink, TextAlignmentOptions.MidlineLeft, spacing: TopCentre.LineSpacing);
            traits = Label(card, "", text, top - 64f, right - text, TopCentre.LabelPt, Tone.Caution, TextAlignmentOptions.MidlineLeft, bold: true, spacing: TopCentre.LineSpacing);
            lineText = Label(card, "", text + 14f, top - 80f, 140f, TopCentre.LabelPt, Tone.Bright, TextAlignmentOptions.MidlineLeft, bold: true, spacing: TopCentre.LineSpacing);
            heatText = Label(card, "", right, top - 80f, 90f, TopCentre.LabelPt, Tone.Caution, TextAlignmentOptions.MidlineRight, bold: true, spacing: TopCentre.LineSpacing);

            // Strip (under the slot): crest, "ACE HUNT · VULTURE", a rule, and how many of the wing are up. One 16-high line.
            float sw = TopCentre.Width, sh = TopCentre.StripHeight;
            strip = Holder(root, "Ace Strip", 0f, 0f, sw, sh);
            stripGroup = strip.gameObject.AddComponent<CanvasGroup>();
            stripGroup.blocksRaycasts = false;
            Shade(strip, sw, sh);
            stripLines = AvVector.Create(Holder(strip, "Lines", 0f, 0f, 0f, 0f), "Lines", 8);
            Tint(stripLines, Tone.Caution);
            stripCrest = ImageBox(strip, -sw * 0.5f + 7f, 0f, 14f);
            stripCrest.enabled = false;
            stripText = Label(strip, "", -sw * 0.5f + 20f, 0f, sw - 100f, TopCentre.LabelPt, Tone.Caution, TextAlignmentOptions.MidlineLeft, bold: true, spacing: TopCentre.LineSpacing);
            stripCount = Label(strip, "", sw * 0.5f - 2f, 0f, 72f, TopCentre.LabelPt, Tone.Ink, TextAlignmentOptions.MidlineRight, spacing: TopCentre.LineSpacing);

            // Marker: drawn in the root's own space (screen centre origin), so it is not placed by the lane.
            marker = Holder(root, "Ace Marker", 0f, 0f, 0f, 0f);
            markerLines = AvVector.Create(marker, "Lines", 24);      // the marker holder is zero-size: its strokes' origin is the screen centre
            Tint(markerLines, Tone.Caution);
            markerLabel = Label(marker, "", 0f, 0f, 320f, TopCentre.LabelPt, Tone.Caution, TextAlignmentOptions.MidlineLeft, bold: true, spacing: TopCentre.LineSpacing);
            marker.gameObject.SetActive(false);

            Recolor();
            return true;
        }

        /// <summary>The strip's hairline between its words and its count (redrawn when they change).</summary>
        private void StripRule()
        {
            if (stripLines == null) return;
            float sw = TopCentre.Width, from = -sw * 0.5f + 20f + stripTextW + 8f, to = sw * 0.5f - 2f - stripCountW - 8f;
            AvQuadBuffer b = stripLines.Buffer;
            b.Clear();
            if (to > from) AvStrokes.Line(b, from, 0f, to, 0f, 1f, VanillaHudStyle.InkHi.ToRgba().WithAlpha(HudInk.LineLow));
            stripLines.Commit();
        }

        /// <summary>
        /// The encounter's timeline, every frame, on the lane's motion: the card comes in (the body's brackets closing in, three blinks of "ENEMY ACE" with its bars marching, the callsign typed on), holds until
        /// <see cref="CardS"/>, and goes out faster than it came while the strip comes in.
        /// </summary>
        private void Animate(float now)
        {
            float t = now - introducedAt;
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / TopCentre.EnterS));
            float exit = Mathf.Clamp01((t - CardS) / TopCentre.ExitS);
            bool cardUp = exit < 1f && !TopCentre.BannerUp;      // the banner wins the shared slot
            if (card.gameObject.activeSelf != cardUp) card.gameObject.SetActive(cardUp);
            TopCentre.CardUp = cardUp;
            cardGroup.alpha = enter * (1f - exit);
            bool calm = AvFxDriver.ReducedMotion;      // the kit's REDUCED MOTION: no blink, march, type-on or breath
            header.alpha = crown.alpha = calm || TopCentre.Lit(t, TopCentre.UrgentHz) || t >= TopCentre.OnsetS ? 1f : 0.15f;
            callsign.maxVisibleCharacters = calm ? 999 : Mathf.Clamp((int)(t / TopCentre.TypeS), 0, 999);
            if (cardUp)
            {
                float h = TopCentre.CardHeight * 0.5f, w = TopCentre.Width * 0.5f + (calm ? 0f : (1f - enter) * 40f), headY = h - 7f;
                var white = new Rgba(1f, 1f, 1f, 1f);
                AvQuadBuffer b = cardLines.Buffer;
                b.Clear();
                // The header's two hazard bars, up to the crown and the words.
                float phase = t < TopCentre.OnsetS && !calm ? (int)(t * 6f) % 4 * 4f : 0f, reach = TopCentre.Width * 0.5f, gap = headerHalf + 10f;
                TopCentre.Stripes(b, -reach, -gap, headY - 3f, headY + 3f, phase, white);
                TopCentre.Stripes(b, gap, reach, headY - 3f, headY + 3f, phase, white);
                AvStrokes.Bracket(b, -w, -h, w * 2f, h * 2f - 18f, TopCentre.Arm, TopCentre.Stroke, white);
                // The bearing row: an arrow at where the ace is off our nose (host and solo; a client has no position for it).
                bool row = aceHave;
                if (lineText.gameObject.activeSelf != row) lineText.gameObject.SetActive(row);
                if (row)
                {
                    Dart(b, -TopCentre.Width * 0.5f + 78f + 5f, h - 80f, aceRel, 9f, white);
                }
                cardLines.Commit();
            }
            float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - CardS) / TopCentre.EnterS));
            if (strip.gameObject.activeSelf != fold > 0f) strip.gameObject.SetActive(fold > 0f);
            stripGroup.alpha = fold;
        }

        /// <summary>A dart (an arrowhead: tip, right wing, notch, left wing) at (<paramref name="cx"/>, <paramref name="cy"/>) pointing <paramref name="degrees"/> clockwise from straight up, <paramref name="size"/> from tip to centre.</summary>
        private static void Dart(AvQuadBuffer b, float cx, float cy, float degrees, float size, Rgba c)
        {
            float a = degrees * Mathf.Deg2Rad, s = Mathf.Sin(a), co = Mathf.Cos(a), k = size / 9f;
            b.Add(cx + 9f * k * s, cy + 9f * k * co, cx + 6f * k * co - 6f * k * s, cy - 6f * k * s - 6f * k * co,
                cx - 3f * k * s, cy - 3f * k * co, cx - 6f * k * co - 6f * k * s, cy + 6f * k * s - 6f * k * co, c, c, c, c);
        }

        /// <summary>Every frame while a hunt shows: the ace's marker, from the hunt on the host (solo too; a client has no position for it, and shows none).</summary>
        private void Track(float screenW)
        {
            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            Aircraft ace = null, me = null;
            string handle = null;
            bool have = cam != null && squad != null && squad.TryAce(out ace, out handle) && GameManager.GetLocalAircraft(out me) && me != null;
            if (marker.gameObject.activeSelf != have) marker.gameObject.SetActive(have);
            aceHave = have;
            if (!have) return;
            float metres = Vector3.Distance(ace.transform.position, me.transform.position);
            AvUnits units = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric ? AvUnits.Metric : AvUnits.Imperial;
            int key = (int)(metres / 100f) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (key != distanceKey)
            {
                distanceKey = key;
                distance = new string(scratch, 0, AvUnitTable.DistanceReading(scratch, 0, metres, units)).ToUpperInvariant();
            }
            // The card's last row: the ace's bearing off our nose (the dart), "034° · 12KM", and the HEAT.
            Vector3 to = ace.transform.position - me.transform.position;
            aceRel = Mathf.DeltaAngle(me.transform.eulerAngles.y, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
            int rel = Mathf.RoundToInt(aceRel + 360f) % 360, lineNow = rel * 100003 + distanceKey;
            if (lineNow != lineKey)
            {
                lineKey = lineNow;
                lineText.text = (rel < 100 ? "0" : "") + (rel < 10 ? "0" : "") + rel + "° · " + distance;
            }
            float heat = squad.Heat;
            int percent = float.IsNaN(heat) ? -1 : Mathf.RoundToInt(Mathf.Clamp01(heat) * 100f);
            if (percent != heatKey)
            {
                heatKey = percent;
                heatText.text = percent < 0 ? "" : "HEAT " + percent + "%";
            }
            Paint(cam, ace.transform.position, handle, screenW, metres <= CloseM);
        }

        /// <summary>The marker in the root's units (1080 high, centre origin): a breathing diamond on the ace with its words, or a double chevron at the edge pointing the way.</summary>
        private void Paint(Camera cam, Vector3 world, string handle, float screenW, bool close)
        {
            float k = 1080f / Mathf.Max(1f, Screen.height), hw = screenW * 0.5f, hh = 540f, margin = 60f;
            Vector3 local = cam.transform.InverseTransformPoint(world), s = cam.WorldToScreenPoint(world);
            float x = (s.x - Screen.width * 0.5f) * k, y = (s.y - Screen.height * 0.5f) * k;
            bool onScreen = local.z > 0f && Mathf.Abs(x) < hw - margin && Mathf.Abs(y) < hh - margin;
            var white = new Rgba(1f, 1f, 1f, 1f);
            AvQuadBuffer b = markerLines.Buffer;
            b.Clear();
            RectTransform label = markerLabel.rectTransform;
            if (onScreen)
            {
                float r = AvFxDriver.ReducedMotion ? 16f : 16f * (1f + 0.1f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI));      // a slow 1 Hz breath, not a blink
                AvStrokes.Diamond(b, x, y, r, TopCentre.Stroke, white);
                AvStrokes.Diamond(b, x, y, r + 6f, TopCentre.Stroke, white.WithAlpha(close ? HudInk.LineMid : HudInk.LineLow));
                label.pivot = new Vector2(0f, 0.5f);
                label.anchoredPosition = new Vector2(x + r + 14f, y);
                Words(1, handle, TextAlignmentOptions.MidlineLeft);
            }
            else
            {
                // Ahead but off the screen: the way from the centre; behind: the way to turn (dead astern reads six o'clock).
                float angle = local.z > 0f ? Mathf.Atan2(y, x) : local.x * local.x + local.y * local.y > 1e-6f ? Mathf.Atan2(local.y, local.x) : -Mathf.PI * 0.5f;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle), ex = cos * (hw - 80f), ey = sin * (hh - 80f), deg = angle * Mathf.Rad2Deg;
                // The ace's own shape at the edge (its diamond, with one pointer out): the THREAT RING's double chevrons mean a missile.
                AvStrokes.Diamond(b, ex, ey, 9f, TopCentre.Stroke, white);
                AvStrokes.Chevron(b, ex + cos * 16f, ey + sin * 16f, 12f, deg, TopCentre.ChevronStroke, white);
                // The words stand inside the chevron, their near edge at the point (as the THREAT RING's labels do).
                label.pivot = new Vector2(cos > 0.35f ? 1f : cos < -0.35f ? 0f : 0.5f, 0.5f + 0.5f * sin);
                label.anchoredPosition = new Vector2(ex - cos * 24f, ey - sin * 24f);
                Words(2 + (cos > 0.35f ? 1 : cos < -0.35f ? 2 : 0), null,
                    cos > 0.35f ? TextAlignmentOptions.MidlineRight : cos < -0.35f ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            }
            markerLines.Commit();
        }

        /// <summary>The marker's words ("ACE  FLINT  4.2KM" on the ace, "ACE 12KM" at the edge), built and written only when the mode, its side or the range shown changed.</summary>
        private void Words(int mode, string handle, TextAlignmentOptions align)
        {
            int key = mode * 1000003 + distanceKey + shownHuntId * 7919;
            if (key == markerKey) return;
            markerKey = key;
            markerLabel.alignment = align;
            markerLabel.text = mode == 1 ? "ACE  " + (handle ?? "").ToUpperInvariant() + "  " + distance : "ACE " + distance;
        }

        // ---- Building blocks: everything centre-anchored in its parent, y up, in the lane's reference pixels ----

        private static RectTransform Holder(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        /// <summary>A soft feathered patch of shade a little larger than its panel, under everything of it: the ink is read through it, not on a plate.</summary>
        private void Shade(RectTransform panel, float w, float h)
        {
            Image shade = HudSprites.Child(panel, "Shade", HudSprites.Feather, sliced: true);
            RectTransform rect = shade.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w + TopCentre.ShadeMargin * 2f, h + TopCentre.ShadeMargin * 2f);
            rect.anchoredPosition = Vector2.zero;
            shade.raycastTarget = false;
            shade.transform.SetAsFirstSibling();
            shades.Add(shade);
        }

        private TMP_Text Label(RectTransform parent, string text, float x, float y, float w, float size, Tone tone, TextAlignmentOptions align,
            bool bold = false, float spacing = 0f)
        {
            TextMeshProUGUI label = HudText.Make(parent, "Label", font, fontMaterial, align, size, TextOverflowModes.Ellipsis);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(align == TextAlignmentOptions.MidlineLeft ? 0f : align == TextAlignmentOptions.MidlineRight ? 1f : 0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, size * 1.5f);
            label.text = text;
            label.richText = false;
            label.characterSpacing = spacing;
            if (bold) label.fontStyle |= FontStyles.Bold;
            Tint(label, tone);
            return label;
        }

        private static Image ImageBox(RectTransform parent, float x, float y, float size)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, size);
            return image;
        }

        private void Tint(Graphic graphic, Tone tone, float alpha = 1f) => tinted.Add(new Tinted { Graphic = graphic, Tone = tone, Alpha = alpha });

        /// <summary>Repaint every tinted graphic in the HUD theme's colours (a colour is written only when it changed).</summary>
        private void Recolor()
        {
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            for (int i = 0; i < shades.Count; i++)
                if (shades[i] != null && shades[i].color != HudSprites.BackdropColor) shades[i].color = HudSprites.BackdropColor;
            for (int i = 0; i < tinted.Count; i++)
            {
                Tinted t = tinted[i];
                if (t.Graphic == null) continue;
                Color c = t.Tone == Tone.Caution ? palette.Warning : t.Tone == Tone.Bright ? VanillaHudStyle.InkHi : palette.AllClear;
                c.a *= t.Alpha;
                if (t.Graphic.color != c) t.Graphic.color = c;
            }
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root.gameObject);
            root = card = strip = marker = null;
            canvasRoot = null;
            tinted.Clear();
            shades.Clear();
            crestKey = portraitKey = null;
            shownHuntId = -1;
            distanceKey = markerKey = lineKey = heatKey = int.MinValue;
            TopCentre.CardUp = false;
            aceHave = false;
            shown = false;
            introducedAt = 0f;
            nextRefresh = 0f;
        }

        private void OnDestroy() => ResetForScene();
    }
}
