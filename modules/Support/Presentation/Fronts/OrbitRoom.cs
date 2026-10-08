using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation.C2;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The SPACE front's room (R5, constellation / satellite view). Top: the orbital situation, a vector map of the theatre with
    /// every geostationary satellite parked at its point with its footprint and fuel, a transfer burn under way, the uplink sites and a card per bird. Bottom: the live
    /// sensor picture (the existing feed picture, brackets and all) and the track file with MARK / TRANSMIT.
    /// </summary>
    internal sealed class OrbitRoom : IFrontRoom
    {
        private const float TopH = 502f, Gap = 8f, CardW = 330f, CardH = 52f, CardStep = 54f;
        private const int GridCols = 10, GridRows = 8, MaxBirdLabels = 8, MaxSiteLabels = 6, TrackRows = SpaceFeedRules.ContactsPerPage;
        private static readonly BirdKind[] Kinds = { BirdKind.Optical, BirdKind.Radar, BirdKind.Kinetic };

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW;
        private FrontMapPlate plate;

        // Situation.
        private FrontBox situation;
        private FrontVector side;
        private TMP_Text aoLabel;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] birdLabels = new TMP_Text[MaxBirdLabels], siteLabels = new TMP_Text[MaxSiteLabels];
        private readonly TMP_Text[] legend = new TMP_Text[5];
        private readonly Card[] ownCards = new Card[3], foeCards = new Card[3];
        private TMP_Text ownHead, foeHead;
        private readonly List<Rect> plates = new List<Rect>(8);
        private readonly Rect[] legendRect = new Rect[1];

        // Relocate: pick an own bird on its card, click the map for the new parked point, confirm the burn.
        private const float BarW = 440f, BarH = 62f;
        private int selKind = -1;
        private bool hasPending;
        private Vector2 pending;
        private FrontRoomView last;
        private TMP_Text relocText;
        private AvControl burnBtn, cancelBtn;

        // Sensor.
        private FrontBox sensor, trackBox;
        private AvControl optical, radar, zoom;
        private OpsFeedPicture picture;
        private TMP_Text sensorNote;
        private FrontVector standby;
        private float picW, picH;
        private bool standbyDrawn;

        // Track file.
        private readonly TrackRow[] rows = new TrackRow[TrackRows];
        private AvControl prev, next, mark, send;
        private TMP_Text noContacts, helpA, helpB, helpC;
        private readonly int[] rowIds = new int[TrackRows];

        public OrbitRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        /// <summary>Own side ink: the kit's ready hue (cyan), never the friendly green, so a word and a track agree.</summary>
        private static Color Own => AvInk.State(AvState.Ready);

        private ISpaceFeedActions Feed => actions.Orbit;

        private void Do(Action a)
        {
            actions.Touch();
            if (Feed != null) a();
        }

        public void Build(RectTransform hero, float w, float h)
        {
            heroW = w;
            BuildSituation(hero);
            float by = TopH + Gap, bh = h - by, sw = 760f;
            BuildSensor(hero, 0f, by, sw, bh);
            BuildTracks(hero, sw + Gap, by, w - sw - Gap, bh);
        }

        // ---- Situation -----------------------------------------------------------------------------------------------

        private void BuildSituation(RectTransform hero)
        {
            situation = new FrontBox(hero, skin, "Orbit", 0f, 0f, heroW, TopH, "ORBITAL SITUATION · GEOSTATIONARY STATIONS", AvIcon.Satellite);
            RectTransform body = situation.Rect;
            plate = new FrontMapPlate(body, skin, heroW, TopH, GridCols, GridRows);
            side = FrontVector.Add(body, "Cards");
            AvLay.Place(side.rectTransform, 0f, 0f, heroW, TopH);
            FrontLabels fx = plate.Labels;

            aoLabel = FrontKit.Mono(body, "AoLabel", 0f, 0f, 150f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
            aoLabel.text = "AREA OF OPERATIONS";
            for (int i = 0; i < MaxBirdLabels; i++)
            {
                birdLabels[i] = FrontKit.Mono(body, "BirdLabel" + i, 0f, 0f, 172f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                birdLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxSiteLabels; i++)
            {
                siteLabels[i] = FrontKit.Mono(body, "SiteLabel" + i, 0f, 0f, 150f, 14f, 10f, TextAlignmentOptions.TopLeft);
                siteLabels[i].gameObject.SetActive(false);
            }

            airbases = new FrontMap.AirbaseLayer(body);

            // Legend card above the plate's bottom row: swatches are drawn in Paint, the words are fixed.
            string[] words = { "OWN BIRD", "HOSTILE BIRD", "FOOTPRINT", "UPLINK SITE", "DASHED = TRANSFER BURN" };
            float[] xs = { 40f, 168f, 318f, 424f, 548f };
            float ly = plate.Y + plate.H - 24f - 28f;
            for (int i = 0; i < legend.Length; i++)
            {
                legend[i] = FrontKit.Mono(body, "Legend" + i, LegendX + xs[i], ly + 5f, 200f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                legend[i].text = words[i];
            }
            legendRect[0] = new Rect(plate.X + 6f, ly, 764f, 24f);
            fx.Fixed(legendRect[0]);

            // Own constellation top-left, hostile top-right, both cards over the map.
            float cy = plate.Y + 8f, fxX = heroW - 12f - CardW;
            ownHead = FrontKit.Mono(body, "OwnHead", 18f, cy, CardW - 8f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            foeHead = FrontKit.Mono(body, "FoeHead", fxX + 6f, cy, CardW - 8f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            for (int i = 0; i < 3; i++)
            {
                int kind = (int)Kinds[i];
                ownCards[i] = new Card(body, skin, 12f, cy + 19f + i * CardStep, true, () => Select(kind));
                foeCards[i] = new Card(body, skin, fxX, cy + 19f + i * CardStep, false, null);
            }
            float colH = 21f + 3f * CardStep;
            fx.Fixed(new Rect(8f, cy - 3f, CardW + 8f, colH));
            fx.Fixed(new Rect(fxX - 4f, cy - 3f, CardW + 8f, colH));
            float bx = (heroW - BarW) * 0.5f;
            relocText = FrontKit.Mono(body, "RelocText", bx + 10f, cy + 2f, BarW - 20f, 32f, 10.5f, TextAlignmentOptions.TopLeft);
            burnBtn = Small(body, "CONFIRM BURN", bx + 8f, cy + BarH - 26f, 150f, () => Confirm(), "Burn to the chosen point: the fuel is spent now and the bird slews there.");
            cancelBtn = Small(body, "CANCEL", bx + 164f, cy + BarH - 26f, 90f, () => { actions.Touch(); selKind = -1; hasPending = false; Repaint(); }, "Drop the selection without burning.");
            burnBtn.SetStyle(AvButtonStyle.Primary);
            fx.Fixed(new Rect(bx - 4f, cy - 3f, BarW + 8f, BarH + 6f));
            SetBar(false);
            skin.Add(() => { aoLabel.color = AvInk.Key; relocText.color = AvInk.Ink; for (int i = 0; i < legend.Length; i++) legend[i].color = AvInk.Dim; });
        }

        private const float LegendX = 14f;

        private AvControl Small(RectTransform p, string label, float x, float y, float w, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            AvLay.Place(c.Rect, x, y, w, 22f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void SetBar(bool on)
        {
            relocText.gameObject.SetActive(on);
            burnBtn.Rect.gameObject.SetActive(on);
            cancelBtn.Rect.gameObject.SetActive(on);
        }

        private void Repaint() { if (last != null) PaintSituation(last); }

        private void Select(int kind)
        {
            actions.Touch();
            selKind = selKind == kind ? -1 : kind;
            hasPending = false;
            Repaint();
        }

        private bool TryBird(int kind, out OrbitTrackView b)
        {
            if (last != null) foreach (OrbitTrackView t in last.Birds) if (t.Own && (int)t.Kind == kind) { b = t; return true; }
            b = default;
            return false;
        }

        private float PendingCost(in OrbitTrackView b) => GeoBird.Cost(GeoBird.Distance(b.U, b.V, pending.x, pending.y));

        private bool CanBurn(in OrbitTrackView b) => b.Alive && !b.Relocating && hasPending && PendingCost(b) > 0f && b.Fuel >= PendingCost(b);

        private void Confirm()
        {
            actions.Touch();
            if (selKind < 0 || !TryBird(selKind, out OrbitTrackView b) || !CanBurn(b)) return;
            actions.RelocateBird(selKind, pending.x, pending.y);
            selKind = -1; hasPending = false;
            Repaint();
        }

        // ---- Sensor and track file -----------------------------------------------------------------------------------

        private void BuildSensor(RectTransform hero, float x, float y, float w, float h)
        {
            sensor = new FrontBox(hero, skin, "Sensor", x, y, w, h, "SATELLITE CAMERA", AvIcon.Camera);
            RectTransform b = sensor.Rect;
            optical = Tool(b, "OPTICAL", 8f, 28f, 112f, () => Do(() => Feed.SetSource(BirdKind.Optical)), "Show the OPTICAL satellite's camera picture. Cloud softens it, night refuses it.");
            radar = Tool(b, "RADAR", 124f, 28f, 112f, () => Do(() => Feed.SetSource(BirdKind.Radar)), "Show the RADAR satellite's SAR picture from the last scan.");
            zoom = Tool(b, "ZOOM", 240f, 28f, 124f, () => Do(() => Feed.CycleZoom()), "Cycle the picture zoom: WIDE, MID, CLOSE. CLOSE centres on the selected track.");
            sensorNote = FrontKit.Mono(b, "Note", 372f, 28f, w - 380f, 26f, 10.5f, TextAlignmentOptions.MidlineRight);
            picture = new OpsFeedPicture(b, id => Do(() => Feed.SelectEntry(id)));
            picture.Place(new AvSlot(8f, 60f, w - 16f, h - 68f));
            picW = w - 16f; picH = h - 68f;
            standby = FrontVector.Add(b, "Standby");
            AvLay.Place(standby.rectTransform, 8f, 60f, picW, picH);
            skin.Add(() => sensorNote.color = AvInk.Dim);
        }

        private AvControl Tool(RectTransform p, string label, float x, float y, float w, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, AvButtonStyle.Default));
            AvLay.Place(c.Rect, x, y, w, 26f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void BuildTracks(RectTransform hero, float x, float y, float w, float h)
        {
            trackBox = new FrontBox(hero, skin, "Tracks", x, y, w, h, "TRACK FILE", AvIcon.Radar2);
            RectTransform b = trackBox.Rect;
            for (int i = 0; i < TrackRows; i++)
            {
                int slot = i;
                rows[i] = new TrackRow(b, skin, 8f, 28f + i * 40f, w - 16f, 38f, () => Do(() => Feed.SelectEntry(rowIds[slot])));
            }
            noContacts = FrontKit.Mono(b, "NoContacts", 12f, 28f, w - 24f, 120f, 11f, TextAlignmentOptions.Center);
            float bw = (w - 16f - 6f) / 2f;
            prev = Tool(b, "PREV", 8f, 272f, bw, () => Do(() => Feed.PrevPage()), "Previous page of the track file.");
            next = Tool(b, "NEXT", 14f + bw, 272f, bw, () => Do(() => Feed.NextPage()), "Next page of the track file.");
            mark = Tool(b, "MARK", 8f, 304f, bw, () => Do(() => Feed.Confirm()), "Mark the selected track so it shows on every pilot's map.");
            send = Tool(b, "TRANSMIT", 14f + bw, 304f, bw, () => Do(() => Feed.Send()), "Post the marked tracks as TASKED targets for any pilot.");
            mark.SetStyle(AvButtonStyle.Primary);
            send.SetStyle(AvButtonStyle.Primary);
            helpA = FrontKit.Mono(b, "HelpA", 10f, 338f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            helpB = FrontKit.Mono(b, "HelpB", 10f, 354f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            helpC = FrontKit.Mono(b, "HelpC", 10f, 370f, w - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            helpA.text = "> CLICK A BRACKET OR A ROW TO SELECT";
            helpB.text = "> MARK PUTS THE TRACK ON THE FACTION MAP";
            helpC.text = "> TRANSMIT POSTS MARKS AS TASKED TARGETS";
            skin.Add(() => { helpA.color = AvInk.Dim; helpB.color = AvInk.Dim; helpC.color = AvInk.Dim; });
        }

        // ---- Paint ---------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            PaintSituation(v);
            PaintSensor(v.Feed);
            PaintTracks(v.Feed);
        }

        private void PaintSituation(FrontRoomView v)
        {
            last = v;
            if (selKind >= 0 && (!TryBird(selKind, out OrbitTrackView sb) || !sb.Alive)) { selKind = -1; hasPending = false; }
            Color own = Own, foe = AvInk.Hostile;
            FrontMapView mv = plate.View;
            // Centre on our own uplink sites and airbases (the operator's pan or zoom takes over afterwards).
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < v.Sites.Count; i++) if (v.Sites[i].Own) { sum += new Vector2(v.Sites[i].X, v.Sites[i].Y); n++; }
            for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) { sum += new Vector2(v.Airbases[i].U, v.Airbases[i].V); n++; }
            if (n > 0) mv.AutoCenter(sum / n);
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Vector2 Pt(float u, float vv) => mv.P(u, vv);

            // Area of operations: a dashed box around the middle third of the theatre.
            Vector2 a0 = Pt(0.30f, 0.28f), a1 = Pt(0.70f, 0.72f);
            Color ao = AvInk.Key.WithAlpha(0.7f);
            g.Dashed(new Vector2(a0.x, a0.y), new Vector2(a1.x, a0.y), 1.4f, ao, 7f, 5f);
            g.Dashed(new Vector2(a1.x, a0.y), new Vector2(a1.x, a1.y), 1.4f, ao, 7f, 5f);
            g.Dashed(new Vector2(a1.x, a1.y), new Vector2(a0.x, a1.y), 1.4f, ao, 7f, 5f);
            g.Dashed(new Vector2(a0.x, a1.y), new Vector2(a0.x, a0.y), 1.4f, ao, 7f, 5f);
            Vector2 mc = Pt(0.5f, 0.5f);
            g.Line(mc + new Vector2(-9f, 0f), mc + new Vector2(9f, 0f), 1.2f, ao);
            g.Line(mc + new Vector2(0f, -9f), mc + new Vector2(0f, 9f), 1.2f, ao);
            Vector2 al = new Vector2(a0.x + 6f, a1.y - 17f);
            bool aoOn = plate.Shown(al, 4f) && plate.Shown(al + new Vector2(150f, 14f), 4f);
            aoLabel.gameObject.SetActive(aoOn);
            if (aoOn) { AvLay.Place(aoLabel.rectTransform, al.x, al.y, 150f, 14f); labels.Take(new Rect(al.x - 2f, al.y - 1f, 154f, 16f)); }

            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, Pt);
            for (int i = 0; i < v.Airbases.Count; i++) if (v.Airbases[i].Side == AirbaseSide.Own) plate.Dot(v.Airbases[i].U, v.Airbases[i].V, own);

            // Uplink sites first (they never move), then the birds adapt around their labels.
            int sl = 0;
            for (int i = 0; i < v.Sites.Count; i++)
            {
                OrbitSiteView s = v.Sites[i];
                Vector2 p = Pt(s.X, s.Y);
                Color k = !s.Live ? AvInk.State(AvState.Danger) : s.Own ? AvInk.State(AvState.Ready) : foe;
                g.Tri(p + new Vector2(0f, -8f), p + new Vector2(8f, 6f), p + new Vector2(-8f, 6f), AvInk.Ground);
                g.Tri(p + new Vector2(0f, -7f), p + new Vector2(7f, 5f), p + new Vector2(-7f, 5f), s.Live ? k : k.WithAlpha(0.25f));
                if (!s.Live) g.Polyline(new[] { p + new Vector2(0f, -7f), p + new Vector2(7f, 5f), p + new Vector2(-7f, 5f) }, 1.4f, k, true);
                g.Line(p + new Vector2(0f, 5f), p + new Vector2(0f, 10f), 1.4f, k);
                if (s.Live) g.Ring(p, mv.Scale * 0.075f, 1.1f, (s.Own ? AvInk.State(AvState.Ready) : foe).WithAlpha(0.35f), 48, 1f, 1f);
                if (s.Own) plate.Dot(s.X, s.Y, AvInk.State(AvState.Ready));
                labels.Take(new Rect(p.x - 9f, p.y - 9f, 18f, 22f));
                if (sl < MaxSiteLabels && plate.Shown(p, 20f))
                {
                    TMP_Text t = siteLabels[sl++];
                    t.gameObject.SetActive(true);
                    FrontKit.Set(t, s.Name + " · " + s.State, s.Live ? FrontKit.Tone(AvState.Ready) : FrontKit.Tone(AvState.Danger));
                    FrontMap.Plate(g, labels.Place(t, p, 8f, true));
                }
            }
            for (int i = sl; i < MaxSiteLabels; i++) siteLabels[i].gameObject.SetActive(false);

            // Footprints, transfers, glyphs, then the labels.
            int overhead = 0, up = 0;
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < v.Birds.Count; i++)
                {
                    OrbitTrackView b = v.Birds[i];
                    Color k = b.Own ? own : foe;
                    if (pass == 0 && b.Alive) DrawFootprint(g, b, k, v.Sites, own);
                    else if (pass == 1) DrawTransfer(g, b, k);
                    else if (pass == 2) DrawGlyph(g, b, k);
                }
            if (selKind >= 0 && hasPending && TryBird(selKind, out OrbitTrackView sel))
            {
                Vector2 f0 = plate.P(sel.U, sel.V), f1 = plate.P(pending.x, pending.y);
                Color sk = AvInk.Select;
                g.Dashed(f0, f1, 1.8f, sk, 7f, 5f);
                g.Ring(f1, sel.Radius * mv.Scale, 1.6f, sk.WithAlpha(0.85f), 56);
                g.Ring(f1, 6f, 1.6f, sk, 20);
                g.Line(f1 + new Vector2(-12f, 0f), f1 + new Vector2(12f, 0f), 1.2f, sk);
                g.Line(f1 + new Vector2(0f, -12f), f1 + new Vector2(0f, 12f), 1.2f, sk);
            }
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (b.Alive) up++;
                if (b.Alive && b.Overhead) overhead++;
                Vector2 p = BirdPoint(b);
                labels.Take(new Rect(p.x - 17f, p.y - 17f, 34f, 34f));
            }
            int bl = 0;
            for (int i = 0; i < v.Birds.Count && bl < MaxBirdLabels; i++)
            {
                OrbitTrackView b = v.Birds[i];
                Vector2 p = BirdPoint(b);
                if (!plate.Shown(p, 14f)) continue;
                TMP_Text t = birdLabels[bl++];
                t.gameObject.SetActive(true);
                string state = !b.Alive ? "LOST · NO SIGNAL" : b.Relocating ? "BURN · ETA " + FrontKit.Clock(b.EtaSeconds) + " · -" + Mathf.RoundToInt(b.BurnCost) + " % FUEL"
                    : "GEO · FUEL " + Mathf.RoundToInt(b.Fuel) + " %" + (b.Own ? "" : " · WATCHING US");
                Color ink = b.Own ? Own : AvInk.Hostile;
                if (!b.Alive) ink = AvInk.Dim;
                t.text = C2Kit.Tint("<b>" + b.Callsign + "</b> · " + C2Orbit.BirdName(b.Kind), ink) + "\n" +
                    C2Kit.Tint(state, b.Relocating && b.Alive ? FrontKit.Tone(AvState.Caution) : b.Overhead && b.Alive ? FrontKit.Tone(b.Own ? AvState.Ready : AvState.Danger) : AvInk.Dim);
                t.color = AvInk.Ink;
                plates.Add(labels.Place(t, p, 18f, false));
            }
            for (int i = bl; i < MaxBirdLabels; i++) birdLabels[i].gameObject.SetActive(false);

            foreach (Rect pr in plates) FrontMap.Plate(g, pr);
            plates.Clear();
            airbases.Names(g, v.Airbases, Pt, mv, (t, a) => labels.Place(t, a, 9f, true, true));
            plate.End();

            // Overlay cards and legend swatches over the map.
            FrontVector c = side;
            c.Clear();
            float cy = plate.Y + 8f, fxX = heroW - 12f - CardW, colH = 21f + 3f * CardStep;
            FrontMapPlate.Card(c, 8f, cy - 3f, CardW + 8f, colH);
            FrontMapPlate.Card(c, fxX - 4f, cy - 3f, CardW + 8f, colH);
            FrontMapPlate.Card(c, legendRect[0].x, legendRect[0].y, legendRect[0].width, legendRect[0].height);
            float ly = legendRect[0].y + 12f, lx = LegendX;
            c.Diamond(new Vector2(lx + 12f, ly), 6f, own);
            c.Diamond(new Vector2(lx + 140f, ly), 6f, foe);
            c.Ring(new Vector2(lx + 290f, ly), 7f, 1.4f, own, 20);
            c.Tri(new Vector2(lx + 398f, ly - 6f), new Vector2(lx + 404f, ly + 4f), new Vector2(lx + 392f, ly + 4f), AvInk.State(AvState.Ready));
            c.Dashed(new Vector2(lx + 508f, ly), new Vector2(lx + 536f, ly), 2f, AvInk.Dim, 6f, 4f);
            string tally = "TRACKING " + v.Birds.Count + " OBJECTS · " + up + " UP · " + overhead + " OVER THE AO";
            situation.SetMeta(tally + " · " + v.Theatre, overhead > 0 ? FrontKit.Tone(AvState.Caution) : AvInk.Dim);

            // Relocate bar.
            bool bar = selKind >= 0 && TryBird(selKind, out OrbitTrackView rb);
            SetBar(bar);
            if (bar)
            {
                TryBird(selKind, out OrbitTrackView rb2);
                float bx = (heroW - BarW) * 0.5f;
                FrontMapPlate.Card(c, bx, cy - 3f, BarW, BarH + 6f);
                string head = "<b>RELOCATE " + rb2.Callsign + "</b> · FUEL " + Mathf.RoundToInt(rb2.Fuel) + " %";
                string line = rb2.Relocating ? "BURN UNDER WAY · ETA " + FrontKit.Clock(rb2.EtaSeconds)
                    : !hasPending ? "CLICK THE MAP TO SET THE NEW PARKED POINT"
                    : CanBurn(rb2) ? "BURN COST -" + Mathf.RoundToInt(PendingCost(rb2)) + " % → " + Mathf.RoundToInt(rb2.Fuel - PendingCost(rb2)) + " % LEFT · ETA " + FrontKit.Clock(GeoBird.Distance(rb2.U, rb2.V, pending.x, pending.y) / GeoBird.Slew)
                    : PendingCost(rb2) <= 0f ? "ALREADY PARKED THERE" : "NOT ENOUGH FUEL · NEED " + Mathf.CeilToInt(PendingCost(rb2)) + " %";
                relocText.text = head + "\n" + C2Kit.Tint(line, hasPending && !CanBurn(rb2) ? FrontKit.Tone(AvState.Danger) : AvInk.Dim);
                OpsKit.Enable(burnBtn, CanBurn(rb2));
            }
            c.Flush();

            // Cards.
            PaintCards(v);
        }

        private Vector2 BirdPoint(in OrbitTrackView b) => plate.P(b.U, b.V);

        private void DrawFootprint(FrontVector g, in OrbitTrackView b, Color k, List<OrbitSiteView> sites, Color own)
        {
            Vector2 p = BirdPoint(b);
            float r = plate.View.Scale * b.Radius;
            g.Disc(p, r, k.WithAlpha(0.11f), 56);
            g.Ring(p, r, 1.8f, k.WithAlpha(0.95f), 72);
            g.Ring(p, r * 0.62f, 1f, k.WithAlpha(0.45f), 48, 1f, 1f);
            g.Line(p + new Vector2(-r - 6f, 0f), p + new Vector2(-r * 0.35f, 0f), 1f, k.WithAlpha(0.6f));
            g.Line(p + new Vector2(r + 6f, 0f), p + new Vector2(r * 0.35f, 0f), 1f, k.WithAlpha(0.6f));
            g.Line(p + new Vector2(0f, -r - 6f), p + new Vector2(0f, -r * 0.35f), 1f, k.WithAlpha(0.6f));
            g.Line(p + new Vector2(0f, r + 6f), p + new Vector2(0f, r * 0.35f), 1f, k.WithAlpha(0.6f));
            if (!b.Own) return;
            for (int i = 0; i < sites.Count; i++)   // the uplink beam: a live own site inside the footprint talks to the bird
            {
                OrbitSiteView s = sites[i];
                Vector2 sp = plate.P(s.X, s.Y);
                if (s.Own && s.Live && (sp - p).magnitude < r) g.Dashed(sp + new Vector2(0f, -7f), p, 1.3f, own.WithAlpha(0.85f), 5f, 4f);
            }
        }

        /// <summary>A burn under way: dashed line from where it left to the new parked point, a ring at the target.</summary>
        private void DrawTransfer(FrontVector g, in OrbitTrackView b, Color k)
        {
            if (!b.Alive || !b.Relocating) return;
            Vector2 a = plate.P(b.FromU, b.FromV), z = plate.P(b.TargetU, b.TargetV);
            g.Dashed(a, z, 4f, AvInk.Ground.WithAlpha(0.45f), 7f, 5f);
            g.Dashed(a, z, 1.8f, k.WithAlpha(0.95f), 7f, 5f);
            g.Ring(a, 4f, 1.4f, k.WithAlpha(0.6f), 16);
            g.Ring(z, plate.View.Scale * b.Radius, 1.2f, k.WithAlpha(0.45f), 56, 1f, 1f);
            g.Ring(z, 5f, 1.6f, k, 16);
        }

        private void DrawGlyph(FrontVector g, in OrbitTrackView b, Color k)
        {
            Vector2 p = BirdPoint(b);
            if (!b.Alive)
            {
                Color dead = AvInk.Muted;
                g.Line(p + new Vector2(-6f, -6f), p + new Vector2(6f, 6f), 2.2f, dead);
                g.Line(p + new Vector2(-6f, 6f), p + new Vector2(6f, -6f), 2.2f, dead);
                return;
            }
            Vector2 u = Vector2.right, n = Vector2.up;
            if (b.Relocating) { Vector2 d = plate.P(b.TargetU, b.TargetV) - plate.P(b.FromU, b.FromV); if (d.sqrMagnitude > 1e-4f) { u = d.normalized; n = new Vector2(-u.y, u.x); } }
            g.Line(p + n * 4f, p + n * 14f, 5f, k.WithAlpha(0.9f));
            g.Line(p - n * 4f, p - n * 14f, 5f, k.WithAlpha(0.9f));
            g.Diamond(p, 7.5f, AvInk.Ground);
            g.Diamond(p, 5.5f, k);
            if (selKind == (int)b.Kind && b.Own) g.Ring(p, 13f, 1.6f, AvInk.Select, 24);
            if (b.Relocating) g.Line(p + u * 5f, p + u * 12f, 1.6f, k);
        }

        public bool Tick(Vector2 mouse)
        {
            bool changed = plate.Tick(mouse);
            if (selKind < 0 || !Input.GetMouseButtonDown(0) || last == null) return changed;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(situation.Rect, mouse, null, out Vector2 lp)) return changed;
            Vector2 p = new Vector2(lp.x - situation.Rect.rect.xMin, situation.Rect.rect.yMax - lp.y);
            FrontMapView mv = plate.View;
            if (p.x < mv.X || p.x > mv.X + mv.W || p.y < mv.Y || p.y > mv.Y + mv.H || plate.Labels.Blocked(new Rect(p.x - 1f, p.y - 1f, 2f, 2f))) return changed;
            Vector2 uv = mv.UV(p);
            pending = new Vector2(Mathf.Clamp(uv.x, 0.02f, 0.98f), Mathf.Clamp(uv.y, 0.02f, 0.98f));
            hasPending = true;
            actions.Touch();
            return true;
        }

        private void PaintCards(FrontRoomView v)
        {
            int ownUp = 0, ownAll = 0, foeSeen = 0;
            var own = new OrbitTrackView?[3];
            var foes = new List<OrbitTrackView>(3);
            for (int i = 0; i < v.Birds.Count; i++)
            {
                OrbitTrackView b = v.Birds[i];
                if (b.Own) { int k = Array.IndexOf(Kinds, b.Kind); if (k >= 0) own[k] = b; ownAll++; if (b.Alive) ownUp++; }
                else if (foes.Count < 3) { foes.Add(b); foeSeen++; }
            }
            FrontKit.Set(ownHead, "OWN CONSTELLATION · " + ownUp + "/3 UP", Own);
            FrontKit.Set(foeHead, "HOSTILE TRACKED · " + foeSeen + "/3 SEEN", AvInk.Hostile);
            for (int i = 0; i < 3; i++)
            {
                ownCards[i].Paint(own[i], Kinds[i]);
                ownCards[i].Mark(selKind == (int)Kinds[i]);
                foeCards[i].Paint(i < foes.Count ? (OrbitTrackView?)foes[i] : null, BirdKind.Optical);
            }
        }

        private void PaintSensor(SpaceFeedView f)
        {
            sensor.SetMeta((f.Source == BirdKind.Radar ? "RADAR SAR" : "OPTICAL") + " · ZOOM " + (f.ZoomEnabled ? SpaceFeedRules.ZoomWord(f.Zoom) : "—"), AvInk.Dim);
            optical.Latched = f.Source == BirdKind.Optical;
            radar.Latched = f.Source == BirdKind.Radar;
            OpsKit.Label(zoom, f.ZoomEnabled ? "ZOOM " + SpaceFeedRules.ZoomWord(f.Zoom) : "ZOOM —");
            OpsKit.Enable(zoom, f.ZoomEnabled);
            FrontKit.Set(sensorNote, f.ConstellationMeta ?? "", AvInk.Dim);
            picture.Paint(f);
            DrawStandby(f.Image == null || f.ImageKind == FeedImageKind.None || (f.Refusal != null && f.Refusal.Length > 0));
        }

        /// <summary>The no-picture state: a sensor test pattern with the predicted swath, so the pane never reads as a black hole.</summary>
        private void DrawStandby(bool on)
        {
            if (on == standbyDrawn) return;
            standbyDrawn = on;
            FrontVector g = standby;
            g.Clear();
            if (on)
            {
                Color k = AvInk.Key;
                Vector2 c = new Vector2(picW * 0.5f, picH * 0.5f);
                for (float x = 0f; x <= picW; x += 40f) g.Line(new Vector2(x, 0f), new Vector2(x, picH), 1f, k.WithAlpha(0.10f));
                for (float y = 0f; y <= picH; y += 40f) g.Line(new Vector2(0f, y), new Vector2(picW, y), 1f, k.WithAlpha(0.10f));
                for (int i = 1; i <= 4; i++) g.Ring(c, 38f * i, 1.2f, k.WithAlpha(0.28f), 72, i % 2 == 0 ? 1f : 0f, 1f);
                g.Line(new Vector2(0f, c.y), new Vector2(picW, c.y), 1f, k.WithAlpha(0.3f));
                g.Line(new Vector2(c.x, 0f), new Vector2(c.x, picH), 1f, k.WithAlpha(0.3f));
                float sw = picW * 0.46f, sh = picH * 0.56f;
                var r = new Rect(c.x - sw * 0.5f, c.y - sh * 0.5f, sw, sh);
                g.Dashed(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), 1.5f, k.WithAlpha(0.75f), 8f, 5f);
                g.Dashed(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), 1.5f, k.WithAlpha(0.75f), 8f, 5f);
                g.Dashed(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), 1.5f, k.WithAlpha(0.75f), 8f, 5f);
                g.Dashed(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), 1.5f, k.WithAlpha(0.75f), 8f, 5f);
                for (float x = 8f; x < picW - 8f; x += 10f) g.Line(new Vector2(x, picH - 22f), new Vector2(x, picH - (x % 50f < 10f ? 12f : 17f)), 1f, k.WithAlpha(0.5f));
                const float cb = 22f;
                foreach (Vector2 q in new[] { new Vector2(4f, 4f), new Vector2(picW - 4f, 4f), new Vector2(4f, picH - 4f), new Vector2(picW - 4f, picH - 4f) })
                {
                    float dx = q.x < c.x ? 1f : -1f, dy = q.y < c.y ? 1f : -1f;
                    g.Line(q, q + new Vector2(cb * dx, 0f), 2f, k.WithAlpha(0.8f));
                    g.Line(q, q + new Vector2(0f, cb * dy), 2f, k.WithAlpha(0.8f));
                }
            }
            g.Flush();
        }

        private void PaintTracks(SpaceFeedView f)
        {
            bool none = f.NoContacts != null && f.NoContacts.Length > 0;
            int shown = 0;
            for (int i = 0; i < TrackRows; i++)
            {
                FeedTileView t = i < f.Tiles.Length ? f.Tiles[i] : default;
                if (none || !t.Present)
                {
                    rows[i].Ghost(none && i == 0 ? "NO TRACKS YET" : "NO CONTACT", none && i == 0 ? f.NoContacts : "AWAITING A SATELLITE PASS");
                    continue;
                }
                rowIds[i] = t.Id;
                rows[i].Paint(t);
                shown++;
            }
            FrontKit.Set(noContacts, "", AvInk.Dim);
            trackBox.SetMeta(none ? "NO TRACKS" : C2Orbit.PageMeta(f.Page, f.Pages) + " · " + shown + " ON PAGE", AvInk.Dim);
            OpsKit.Enable(prev, !none && f.Page > 0);
            OpsKit.Enable(next, !none && f.Page + 1 < f.Pages);
            OpsKit.Label(mark, f.ConfirmFull ? "MARKS FULL" : f.SelectedId > 0 ? "MARK " + C2Orbit.TrackId(f.SelectedId) : "MARK");
            OpsKit.Enable(mark, f.CanConfirm);
            OpsKit.Label(send, C2Orbit.TransmitLabel(f.SendCount));
            OpsKit.Enable(send, f.CanSend);
        }

        // ---- Parts ---------------------------------------------------------------------------------------------------

        private sealed class Card
        {
            private readonly AvFrame frame;
            private readonly Image rail, bar, barFill;
            private readonly TMP_Text glyph, name, state, spec, pass;
            private readonly bool own;
            private bool selected;

            public Card(RectTransform p, FrontSkin skin, float x, float y, bool own, Action click)
            {
                this.own = own;
                RectTransform root = AvLay.Child(p, own ? "OwnCard" : "FoeCard");
                AvLay.Place(root, x, y, CardW, CardH);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, CardW, CardH, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, CardH, Color.clear);
                glyph = AvIcons.Make(root, AvIcon.Satellite, 16f, AvInk.Dim);
                AvLay.Place(glyph.rectTransform, 10f, 3f, 16f, 16f);
                name = FrontKit.Mono(root, "Name", 32f, 1f, 196f, 20f, 12f, TextAlignmentOptions.MidlineLeft, true);
                state = FrontKit.Mono(root, "State", 200f, 1f, CardW - 208f, 20f, 11f, TextAlignmentOptions.MidlineRight, true);
                spec = FrontKit.Mono(root, "Spec", 10f, 19f, CardW - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                bar = FrontKit.Solid(root, "Bar", 10f, 40f, 190f, 8f, Color.clear);
                barFill = FrontKit.Solid(root, "BarFill", 11f, 41f, 0f, 6f, Color.clear);
                pass = FrontKit.Mono(root, "Pass", 206f, 35f, CardW - 214f, 18f, 10.5f, TextAlignmentOptions.MidlineRight, true);
                if (click != null)
                {
                    AvHit hit = AvHit.On(frame);
                    hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                }
                skin.Add(Restyle);
            }

            public void Mark(bool on) { if (selected == on) return; selected = on; Restyle(); }

            private void Restyle()
            {
                frame.Paint(AvInk.Ground.WithAlpha(0.6f), selected ? AvInk.Select : AvInk.Hairline);
                bar.color = AvInk.Ground.WithAlpha(0.95f);
                rail.color = own ? Own : AvInk.Hostile;
            }

            public void Paint(OrbitTrackView? t, BirdKind slot)
            {
                if (!t.HasValue)
                {
                    FrontKit.Set(name, own ? C2Orbit.BirdName(slot) + " · NO BIRD" : "SLOT · NOT TRACKED", AvInk.Dim);
                    FrontKit.Set(state, own ? "DOWN" : "UNSEEN", own ? FrontKit.Tone(AvState.Danger) : AvInk.Dim);
                    FrontKit.Set(spec, own ? "Queue LAUNCH SATELLITE to replace it." : "No object on this plane yet.", AvInk.Dim);
                    FrontKit.Set(pass, "", AvInk.Dim);
                    barFill.rectTransform.sizeDelta = new Vector2(0f, 6f);
                    rail.color = AvInk.Muted;
                    glyph.color = AvInk.Muted;
                    return;
                }
                OrbitTrackView b = t.Value;
                Color k = !b.Alive ? AvInk.Dim : own ? Own : AvInk.Hostile;
                rail.color = b.Alive ? k : AvInk.State(AvState.Danger);
                glyph.color = k;
                FrontKit.Set(name, b.Callsign + " · " + C2Orbit.BirdName(b.Kind), AvInk.Ink);
                FrontKit.Set(state, b.State, FrontKit.Tone(b.Tone));
                FrontKit.Set(spec, b.Orbit, AvInk.Dim);
                barFill.color = k;
                barFill.rectTransform.sizeDelta = new Vector2(188f * Mathf.Clamp01(b.Fuel / 100f), 6f);
                FrontKit.Set(pass, !b.Alive ? "NO SIGNAL" : b.Relocating ? "BURN " + FrontKit.Clock(b.EtaSeconds) + " · -" + Mathf.RoundToInt(b.BurnCost) + " %" : b.Overhead ? "OVER AO · " + Mathf.RoundToInt(b.Fuel) + " %" : "PARKED · " + Mathf.RoundToInt(b.Fuel) + " %",
                    b.Relocating ? FrontKit.Tone(AvState.Caution) : b.Overhead && b.Alive ? FrontKit.Tone(own ? AvState.Ready : AvState.Danger) : AvInk.Dim);
            }
        }

        private sealed class TrackRow
        {
            private readonly RectTransform root;
            private readonly AvFrame frame;
            private readonly Image rail;
            private readonly TMP_Text id, title, sub, pct, cls;
            private bool selected;
            private ProbableClass klass;

            public TrackRow(RectTransform p, FrontSkin skin, float x, float y, float w, float h, Action click)
            {
                root = AvLay.Child(p, "Track");
                AvLay.Place(root, x, y, w, h);
                frame = FrontKit.Panel(root, "Frame", 0f, 0f, w, h, 0f, 0f);
                rail = FrontKit.Solid(root, "Rail", 0f, 0f, 3f, h, Color.clear);
                AvHit hit = AvHit.On(frame);
                hit.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) click(); };
                id = FrontKit.Mono(root, "Id", 8f, 0f, 40f, h, 12f, TextAlignmentOptions.MidlineLeft, true);
                title = FrontKit.Mono(root, "Title", 52f, 2f, w - 140f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
                sub = FrontKit.Mono(root, "Sub", 52f, 19f, w - 140f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                pct = FrontKit.Mono(root, "Pct", w - 82f, 2f, 76f, 18f, 12f, TextAlignmentOptions.MidlineRight, true);
                cls = FrontKit.Mono(root, "Cls", w - 82f, 19f, 76f, 16f, 10f, TextAlignmentOptions.MidlineRight, false, 1f);
                skin.Add(Restyle);
            }

            private void Restyle()
            {
                frame.Paint(selected ? AvInk.Select.WithAlpha(0.18f) : AvInk.Ground.WithAlpha(0.5f), selected ? AvInk.Select : AvInk.Hairline);
                rail.color = ClassTone();
            }

            private Color ClassTone() => klass == ProbableClass.Hostile ? AvInk.State(AvState.Danger) : klass == ProbableClass.Friendly ? AvInk.State(AvState.Ready)
                : klass == ProbableClass.Neutral ? AvInk.State(AvState.Info) : AvInk.State(AvState.Caution);

            public void Ghost(string name, string note)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                selected = false; klass = ProbableClass.Unknown;
                frame.Paint(AvInk.Ground.WithAlpha(0.3f), AvInk.Hairline.WithAlpha(0.5f));
                rail.color = AvInk.Muted;
                FrontKit.Set(id, "T--", AvInk.Dim);
                FrontKit.Set(title, name, AvInk.Dim);
                FrontKit.Set(sub, C2Kit.FitTo(sub, note, sub.rectTransform.rect.width), AvInk.Dim);
                FrontKit.Set(pct, "", AvInk.Dim);
                FrontKit.Set(cls, "", AvInk.Dim);
            }

            public void Paint(in FeedTileView t)
            {
                if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
                selected = t.Selected; klass = t.Class;
                Restyle();
                FrontKit.Set(id, C2Orbit.TrackId(t.Id), selected ? AvInk.Select : AvInk.Ink);
                FrontKit.Set(title, t.Title, AvInk.Ink);
                FrontKit.Set(sub, (t.Moving ? "MOVING" : "STATIC") + (t.Marked ? " · MARKED" : t.FixedPoint ? " · FIXED POINT" : ""), t.Marked ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
                FrontKit.Set(pct, t.Percent > 0 && t.Percent <= 100 ? t.Percent + " %" : "", AvInk.Ink);
                FrontKit.Set(cls, klass == ProbableClass.Hostile ? "HOSTILE" : klass == ProbableClass.Friendly ? "FRIENDLY" : klass == ProbableClass.Neutral ? "NEUTRAL" : "UNKNOWN",
                    klass == ProbableClass.Hostile ? FrontKit.Tone(AvState.Danger) : klass == ProbableClass.Friendly ? FrontKit.Tone(AvState.Ready) : AvInk.Dim);
            }
        }
    }
}
