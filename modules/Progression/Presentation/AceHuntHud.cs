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
    /// Passive local threat display for an ace hunt; all encounter state comes from the squad host. It lives in the top-centre slot (<see cref="TopCentre"/>) under the native HUD canvas, in the HUD's own face and the
    /// standard's sizes and colours, in the grammar of the WARNING line it yields to (2026-11-16, concept A).
    /// <list type="bullet">
    /// <item>Card (the slot, for <see cref="CardS"/> when the hunt starts, only while no warning line is up): two rows inside four corner ticks. Row 1: "ENEMY ACE" (LABEL, WARNING, tracked, three onset blinks), the callsign
    /// (EMPHASIS, INK-HI, typed on) and its skill at the right (VALUE, CAUTION). Row 2 (BODY): who flies with it, its compass bearing and range, the HEAT. The ace's portrait stands at the left, 40 px high.</item>
    /// <item>The rest of the hunt is the ACE mark on the heading tape (Hud's <c>TapeHud</c>); there is no strip.</item>
    /// <item>Marker on the ace's aircraft (host and solo; a client has no position for it): a diamond with its callsign and range, or a double chevron at the screen's edge.</item>
    /// </list>
    /// </summary>
    internal sealed class AceHuntHud : MonoBehaviour, ISceneService
    {
        private const float CardS = 7f, CloseM = 8000f, PortraitSize = 40f, PortraitGap = 8f;

        /// <summary>A graphic and the palette tone it is painted in; <see cref="Recolor"/> repaints every one from the HUD theme.</summary>
        private enum Tone : byte { Ink, Bright, Caution, Warning }
        private struct Tinted { public Graphic Graphic; public Tone Tone; }
        private readonly System.Collections.Generic.List<Tinted> tinted = new System.Collections.Generic.List<Tinted>(16);

        private ISquadView squad;
        private RectTransform canvasRoot, root, card, marker;
        private CanvasGroup cardGroup;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private TMP_Text header, callsign, skillText, lineText, markerLabel;
        private Image portrait;
        private AvVector cardLines, markerLines;
        /// <summary>The card's words' widths (set when the text changes) and the widest its content has been this hunt (the box never shrinks and jitters with a digit).</summary>
        private float headerW, callsignW, skillW, lineW, stickyContent, lastK;
        private bool aceHave;
        private int lineKey = int.MinValue, heatKey = int.MinValue;
        private string portraitKey, flight = "";
        private int shownHuntId = -1, distanceKey = int.MinValue, markerKey = int.MinValue, bearing = -1, heatPercent = -1;
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
            Track(canvasRoot.rect.width / k);      // first: the card's second row reads what it found
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
                stickyContent = 0f;
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
            callsignW = callsign.GetPreferredValues(callsign.text).x;
            skillW = skillText.text.Length > 0 ? skillText.GetPreferredValues(skillText.text).x : 0f;
            flight = AceHuntHudCopy.Flight(wing.WingName, wing.MembersAlive, wing.MemberCount);
            lineKey = int.MinValue;      // the second row is written again with the new flight
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
            root = Holder(canvasRoot, "Boscali Ace Hunt", 0f, 0f, 0f, 0f);
            lastK = 0f;
            // A rebuilt card writes everything again (the old canvas took the texts and pictures with it).
            shown = false;
            portraitKey = null;
            distanceKey = markerKey = lineKey = heatKey = int.MinValue;

            // Card: a holder the size of the box (placed and sized every frame), the ticks' mesh in a zero-size child so its strokes' origin is the card's centre, the portrait and the five words.
            card = Holder(root, "Ace Card", 0f, 0f, TopCentre.Width, TopCentre.CardHeight);
            cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            cardGroup.blocksRaycasts = false;
            cardLines = AvVector.Create(Holder(card, "Lines", 0f, 0f, 0f, 0f), "Lines", 16);
            portrait = ImageBox(card, 0f, 0f, PortraitSize);
            header = Label(card, "ENEMY ACE", TopCentre.LabelPt, HudStep.Label, Tone.Warning, false);
            headerW = header.GetPreferredValues(header.text).x;
            callsign = Label(card, "", TopCentre.NamePt, HudStep.Emphasis, Tone.Bright, false);
            skillText = Label(card, "", TopCentre.ValuePt, HudStep.Value, Tone.Caution, true);
            lineText = Label(card, "", TopCentre.DetailPt, HudStep.Body, Tone.Ink, false);
            lineText.overflowMode = TextOverflowModes.Ellipsis;

            // Marker: drawn in the root's own space (screen centre origin), so it is not placed by the slot.
            marker = Holder(root, "Ace Marker", 0f, 0f, 0f, 0f);
            markerLines = AvVector.Create(marker, "Lines", 24);      // the marker holder is zero-size: its strokes' origin is the screen centre
            markerLabel = MarkerLabel(marker);
            marker.gameObject.SetActive(false);

            Recolor();
            return true;
        }

        /// <summary>
        /// The encounter's timeline, every frame: the card comes in (three blinks of "ENEMY ACE", the callsign typed on), holds until <see cref="CardS"/>, and goes out faster than it came. It is placed and sized every frame from
        /// its words: on the reticle's axis unless its own width would reach the TAC BOARD (then it steps aside, never up or down).
        /// </summary>
        private void Animate(float now)
        {
            float t = now - introducedAt;
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / TopCentre.EnterS));
            float exit = Mathf.Clamp01((t - CardS) / TopCentre.ExitS);
            bool cardUp = exit < 1f && !TopCentre.LineUp;      // the warning line wins the slot
            if (card.gameObject.activeSelf != cardUp) card.gameObject.SetActive(cardUp);
            TopCentre.CardUp = cardUp;
            if (!cardUp) return;
            cardGroup.alpha = enter * (1f - exit);
            bool calm = AvFxDriver.ReducedMotion;      // the kit's REDUCED MOTION: no blink or type-on
            header.alpha = calm || TopCentre.Lit(t, TopCentre.UrgentHz) || t >= TopCentre.OnsetS ? 1f : 0.15f;
            callsign.maxVisibleCharacters = calm ? 999 : Mathf.Clamp((int)(t / TopCentre.TypeS), 0, 999);

            // The box: the portrait, then the wider of the two rows; it never shrinks while the hunt lasts.
            float portraitW = portrait.enabled ? PortraitSize + PortraitGap : 0f;
            float row1 = headerW + TopCentre.WordGap + callsignW + (skillW > 0f ? TopCentre.CallGap + skillW : 0f);
            float content = portraitW + Mathf.Max(row1, lineW);
            if (content > stickyContent) stickyContent = content;
            float w = TopCentre.BoxWidth(stickyContent), h = TopCentre.CardHeight;
            float x = ModuleServices.TryGet(out IHudBoard board) && board.TryGetScreenBox(out float l, out float tp, out float r, out float b)
                ? TopCentre.LaneShift(Screen.width, Screen.height, w, TopCentre.LineTop - TopCentre.Lift, h, l, tp, r, b) : 0f;
            card.anchoredPosition = new Vector2(x, 540f - TopCentre.LineTop - h * 0.5f + TopCentre.Lift);
            card.sizeDelta = new Vector2(w, h);
            float left = -w * 0.5f + TopCentre.Pad, textLeft = left + portraitW, right = w * 0.5f - TopCentre.Pad;
            float row1Y = h * 0.5f - TopCentre.RowTop - TopCentre.RowHeight * 0.5f, row2Y = row1Y - TopCentre.RowHeight * 0.5f - 2f - HudType.BodyRow * 0.5f;
            if (portrait.enabled) { portrait.rectTransform.anchoredPosition = new Vector2(left + PortraitSize * 0.5f, 0f); }
            Place(header.rectTransform, textLeft, row1Y, headerW + 8f, TopCentre.LabelPt * 1.5f);
            Place(callsign.rectTransform, textLeft + headerW + TopCentre.WordGap, row1Y, callsignW + 8f, TopCentre.NamePt * 1.5f);
            Place(skillText.rectTransform, right, row1Y, skillW + 8f, TopCentre.ValuePt * 1.5f);
            Place(lineText.rectTransform, textLeft, row2Y, right - textLeft, TopCentre.DetailPt * 1.5f);

            AvQuadBuffer lines = cardLines.Buffer;
            lines.Clear();
            Rgba ink = VanillaHudStyle.Colours.Alert.ToRgba();
            AvStrokes.Bracket(lines, -w * 0.5f, -h * 0.5f, w, h, TopCentre.Arm, TopCentre.Stroke, ink.WithAlpha(HudInk.LineMid));
            cardLines.Commit();
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            var p = new Vector2(x, y);
            if (rect.anchoredPosition != p) rect.anchoredPosition = p;
            var s = new Vector2(w, h);
            if (rect.sizeDelta != s) rect.sizeDelta = s;
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
            if (!have) { bearing = -1; WriteLine(-1); return; }
            float metres = Vector3.Distance(ace.transform.position, me.transform.position);
            AvUnits units = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Metric ? AvUnits.Metric : AvUnits.Imperial;
            int key = (int)(metres / 100f) * 2 + (units == AvUnits.Imperial ? 1 : 0);
            if (key != distanceKey)
            {
                distanceKey = key;
                distance = new string(scratch, 0, AvUnitTable.DistanceReading(scratch, 0, metres, units)).ToUpperInvariant();
            }
            // The card's second row: the ace's compass bearing (the number the tape shows under its ACE mark), its range and the HEAT.
            Vector3 to = ace.transform.position - me.transform.position;
            bearing = Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f) % 360;
            float heat = squad.Heat;
            heatPercent = float.IsNaN(heat) ? -1 : Mathf.RoundToInt(Mathf.Clamp01(heat) * 100f);
            WriteLine(bearing * 100003 + distanceKey);
            Paint(cam, ace.transform.position, handle, screenW, metres <= CloseM);
        }

        /// <summary>The card's second row, written only when the flight, the bearing, the range or the heat changed.</summary>
        private void WriteLine(int positionKey)
        {
            int key = positionKey * 131 + heatPercent;
            if (key == lineKey) return;
            lineKey = key;
            string text = AceHuntHudCopy.Line(flight, positionKey < 0 ? -1 : bearing, distance, heatPercent);
            lineText.text = text;
            lineW = text.Length > 0 ? lineText.GetPreferredValues(text).x : 0f;
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

        // ---- Building blocks: everything centre-anchored in its parent, y up, in the slot's reference pixels ----

        private static RectTransform Holder(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        /// <summary>A word of the card: its step's size and tracking, bold from VALUE up, anchored at its left edge (or its right, <paramref name="rightAligned"/>); the card places it every frame.</summary>
        private TMP_Text Label(RectTransform parent, string text, float size, HudStep step, Tone tone, bool rightAligned)
        {
            TextMeshProUGUI label = HudText.Make(parent, "Label", font, fontMaterial, rightAligned ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, size, TextOverflowModes.Ellipsis);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(rightAligned ? 1f : 0f, 0.5f);
            rect.sizeDelta = new Vector2(size * 12f, size * 1.5f);
            label.text = text;
            label.richText = false;
            label.characterSpacing = HudType.Tracking(step);
            if (HudType.Bold(step)) label.fontStyle |= FontStyles.Bold;
            Tint(label, tone);
            return label;
        }

        private TMP_Text MarkerLabel(RectTransform parent)
        {
            TextMeshProUGUI label = HudText.Make(parent, "Label", font, fontMaterial, TextAlignmentOptions.MidlineLeft, TopCentre.LabelPt, TextOverflowModes.Ellipsis);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(320f, TopCentre.LabelPt * 1.5f);
            label.richText = false;
            label.characterSpacing = HudType.Tracking(HudStep.Label);
            label.fontStyle |= FontStyles.Bold;
            Tint(label, Tone.Caution);
            Tint(markerLines, Tone.Caution);
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

        private void Tint(Graphic graphic, Tone tone) => tinted.Add(new Tinted { Graphic = graphic, Tone = tone });

        /// <summary>Repaint every tinted graphic in the HUD theme's colours (a colour is written only when it changed), words through the shared glow material.</summary>
        private void Recolor()
        {
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            for (int i = 0; i < tinted.Count; i++)
            {
                Tinted t = tinted[i];
                if (t.Graphic == null) continue;
                Color c = t.Tone == Tone.Warning ? palette.Alert : t.Tone == Tone.Caution ? palette.Warning : t.Tone == Tone.Bright ? VanillaHudStyle.InkHi : palette.AllClear;
                if (t.Graphic.color == c) continue;
                t.Graphic.color = c;
                if (t.Graphic is TMP_Text text)
                {
                    Material glow = VanillaHudStyle.Glow(fontMaterial, c);
                    if (glow != null && text.fontSharedMaterial != glow) text.fontSharedMaterial = glow;
                }
            }
        }

        public void ResetForScene()
        {
            if (root != null) Destroy(root.gameObject);
            root = card = marker = null;
            canvasRoot = null;
            tinted.Clear();
            portraitKey = null;
            shownHuntId = -1;
            distanceKey = markerKey = lineKey = heatKey = int.MinValue;
            TopCentre.CardUp = false;
            aceHave = false;
            shown = false;
            introducedAt = 0f;
            nextRefresh = 0f;
            stickyContent = 0f;
        }

        private void OnDestroy() => ResetForScene();
    }
}
