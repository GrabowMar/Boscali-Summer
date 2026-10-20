using System;
using System.Collections.Generic;
using System.Text;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Presentation.C2;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The CYBER front's room (R4, an Uplink-style intrusion terminal). Top: the theatre node graph under a BOUNCE CHAIN strip (your route as
    /// linked chips, the trace crawling back along it), beside YOUR session (trace dial, hop countdown, held pips, the faction's network, INFOCON,
    /// key). Bottom: the selected node's inspector as a remote-host login (HOP / BURN / DROP), the node list as an `ls -l`, and the session log
    /// as a real shell. Paints from <see cref="NetworkRoomView"/> only; every flavour line (addresses, banners, permission bits, hex dump) is
    /// derived from existing fields and never read as data.
    /// </summary>
    internal sealed class NetworkRoom : IFrontRoom
    {
        private const float PurgeW = 96f, TopH = 540f, Gap = 8f, ColW = 270f, LinkKm = 12f, InspW = 440f, ListW = 410f, RowH = 24f, ListY = 42f, LogY = 30f, LogPitch = 19f, StripH = 34f, Adv = 6.3f;
        private const int Cols = 12, Rows = 8, MaxNodes = 16, MaxAnchors = 4, MaxHops = 3, LogRows = 15, ListRows = 12, MaxChips = 8, HexLines = 22, DosRows = 7;
        private static readonly string[] KindName = { "RADAR", "SAM C2", "RELAY", "UPLINK", "DATA CENTER" };

        // The terminal's own fixed inks (a different console in a different room: they sit on top of the pilot's scheme). Keep text on them at 4.5:1 over the glass.
        private static readonly Color Phos = RoomInk.Phosphor, PhosDim = RoomInk.PhosphorDim, TraceRed = RoomInk.Trace,
            Faint = RoomInk.Rgb(0x2F8F55), OutInk = RoomInk.Rgb(0x5C8F70), Bright = RoomInk.Rgb(0xC9FFD8), Ally = RoomInk.Rgb(0x62D6F2),
            HopInk = RoomInk.Rgb(0xE9FFD2), Amber = RoomInk.Rgb(0xFFB547), WarnRed = RoomInk.Rgb(0xD9605A), Glass = RoomInk.Rgb(0x020A06, 0.95f),
            Black = RoomInk.Rgba(0f, 0f, 0f, 1f);

        private readonly IFrontActions actions;
        private readonly FrontSkin skin;
        private float heroW, colX;
        private FrontMapPlate plate;

        private FrontBox graph, inspector, list, log;
        private FrontVector side, routeVec, inspGlyph, listGlyph, logGlyph;
        private FrontMap.AirbaseLayer airbases;
        private readonly TMP_Text[] nodeLabels = new TMP_Text[MaxNodes], anchorLabels = new TMP_Text[MaxAnchors], hopLabels = new TMP_Text[MaxHops];
        private readonly Image[] nodeHits = new Image[MaxNodes];
        private readonly int[] hitIds = new int[MaxNodes];
        // Node screen points for one paint: true map points, and the drawn ones (installations sharing a spot fanned apart) with their cluster.
        private readonly Vector2[] nodeTrue = new Vector2[MaxNodes], nodeAt = new Vector2[MaxNodes];
        private readonly int[] nodeCluster = new int[MaxNodes];
        private const float StackGap = 22f;
        private TMP_Text hexGlyph;
        private int hexKey = int.MinValue;

        // The bounce chain strip over the top of the graph.
        private readonly TMP_Text[] chipText = new TMP_Text[MaxChips];
        private TMP_Text routeTag, routeNote, routeHead, routeSub, routeMeta;
        private readonly string[] chipWord = new string[MaxChips];
        private readonly byte[] chipKind = new byte[MaxChips];   // 0 LOCALHOST, 1 EW truck or data centre, 2 held node, 3 the node being hopped
        private readonly float[] chipX = new float[MaxChips], chipW = new float[MaxChips];
        private readonly int[] walk = new int[MaxChips];
        private int chipCount, chainAnchor;
        private bool chainPreview;

        // Session column (right of the map).
        private TMP_Text traceHead, opTag, traceValue, traceWord, traceLo, traceHi, hopLine, estLine, nextLine, heldHead, heldVal, facHead, threatHead, threatWord, threatNote, keyHead;
        private readonly TMP_Text[] facKey = new TMP_Text[4], facVal = new TMP_Text[4], infocon = new TMP_Text[5], keyText = new TMP_Text[6];
        private FrontBar hopBar;

        // Inspector.
        private TMP_Text dosTitle, dosHandle, dosStamp, dosNext, bannerA, bannerB;
        private readonly TMP_Text[] dosKey = new TMP_Text[DosRows], dosVal = new TMP_Text[DosRows];
        private readonly float[] dosTop = new float[DosRows], dosHigh = new float[DosRows];
        private AvControl hop, burn, drop, purge;
        private string hopHelpShown, poorNote;
        private int poorCost = -1, poorHave = -1;

        // Node list: twelve 24 px rows over the ranked nodes; the wheel scrolls the rest.
        private readonly Image[] rowHits = new Image[ListRows];
        private readonly TMP_Text[] rowMode = new TMP_Text[ListRows], rowHandle = new TMP_Text[ListRows], rowOwner = new TMP_Text[ListRows], rowState = new TMP_Text[ListRows];
        private readonly int[] rowIds = new int[ListRows], order = new int[MaxNodes];
        private TMP_Text[] listHead;
        private TMP_Text listMore;
        private float listW;
        private int scroll, listCount, lastPick = int.MinValue;
        private static readonly Dictionary<string, string> PermInk = new Dictionary<string, string>(8);

        // Session log.
        private readonly TMP_Text[] logStamp = new TMP_Text[LogRows], logText = new TMP_Text[LogRows];
        private TMP_Text logPrompt;
        private float logTextW, logW;

        private int selected = -1, locateId;

        public NetworkRoom(IFrontActions actions, FrontSkin skin)
        {
            this.actions = actions;
            this.skin = skin;
        }

        private static Color Own => Phos;

        private void Do(Action a) { actions.Touch(); a(); }

        public void Build(RectTransform hero, float w, float h)
        {
            heroW = w;
            colX = w - 12f - ColW;
            BuildGraph(hero);
            float by = TopH + Gap, bh = h - by, lx = InspW + ListW + 2f * Gap;
            BuildInspector(hero, 0f, by, InspW, bh);
            BuildList(hero, InspW + Gap, by, ListW, bh);
            BuildLog(hero, lx, by, w - lx, bh);
        }

        // ---- Graph and session column ------------------------------------------------------------------------------------

        private TMP_Text Head(RectTransform b, string name, float x, float y, float w, string text)
        {
            TMP_Text t = FrontKit.Mono(b, name, x, y, w, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            t.text = text;
            return t;
        }

        /// <summary>A wrapping mono block (two or three lines) pinned to the top-left of its rect.</summary>
        private static TMP_Text Wrapped(RectTransform p, string name, float x, float y, float w, float h, float size, bool strong = false)
        {
            TMP_Text t = AvText.Make(p, name, strong ? AvTextRole.DataStrong : AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft, true);
            AvText.Size(t, size);
            t.richText = true;
            t.raycastTarget = false;
            t.color = Bright;
            AvLay.Place(t.rectTransform, x, y, w, h);
            return t;
        }

        private void BuildGraph(RectTransform hero)
        {
            graph = new FrontBox(hero, skin, "Network", 0f, 0f, heroW, TopH, "CYBER SITUATION · THEATRE NODE GRAPH", AvIcon.ShieldLock, FrontLook.Terminal).Tagged("tty1");
            RectTransform b = graph.Rect;
            // The plate fills the box body left of the session column (which therefore never covers a node) and clips its own vector layer.
            plate = new FrontMapPlate(b, skin, colX - 4f, TopH, Cols, Rows);
            plate.Labels.Fixed(new Rect(plate.X, plate.Y, plate.W, StripH));   // the bounce chain strip sits over the top of the map: labels give way to it
            // A hex-dump watermark down the right edge of the glass: pure ornament (Glyph-named, so it is exempt from the text gate), under every label.
            hexGlyph = FrontKit.Mono(b, "GlyphHex", plate.X + plate.W - 96f, plate.Y + StripH + 10f, 90f, HexLines * 11.5f, 9f, TextAlignmentOptions.TopLeft);
            hexGlyph.color = Phos.WithAlpha(0.11f);
            side = FrontVector.Add(b, "Side"); // the session column's dial, pips, blocks and key swatches
            AvLay.Place(side.rectTransform, 0f, 0f, heroW, TopH);
            airbases = new FrontMap.AirbaseLayer(b);
            for (int i = 0; i < MaxNodes; i++)
            {
                nodeLabels[i] = FrontKit.Mono(b, "NodeLabel" + i, 0f, 0f, 150f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                nodeLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxAnchors; i++)
            {
                anchorLabels[i] = FrontKit.Mono(b, "AnchorLabel" + i, 0f, 0f, 150f, 28f, 10.5f, TextAlignmentOptions.TopLeft);
                anchorLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxHops; i++)
            {
                hopLabels[i] = FrontKit.Mono(b, "HopLabel" + i, 0f, 0f, 50f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                hopLabels[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < MaxNodes; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "NodeHit" + i, 0f, 0f, 30f, 30f, Color.clear);
                hit.raycastTarget = true;
                AvHit.On(hit).Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => actions.SelectNode(hitIds[slot])); };
                hit.gameObject.SetActive(false);
                nodeHits[i] = hit;
            }

            // The bounce chain: a glass strip across the top of the map. Its vector layer sits above every map label; the chips' words above it.
            routeVec = FrontVector.Add(b, "Route");
            AvLay.Place(routeVec.rectTransform, 0f, 0f, heroW, TopH);
            routeTag = FrontKit.Mono(b, "RouteTag", plate.X + 10f, plate.Y + 2f, 50f, 20f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            routeTag.text = "ROUTE";
            for (int i = 0; i < MaxChips; i++)
            {
                chipText[i] = FrontKit.Mono(b, "Chip" + i, 0f, 0f, 80f, 18f, 10.5f, TextAlignmentOptions.Midline, true);
                chipText[i].gameObject.SetActive(false);
            }
            routeNote = FrontKit.Mono(b, "RouteNote", plate.X + 232f, plate.Y + 2f, 320f, 20f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 0.5f);
            routeHead = FrontKit.Mono(b, "RouteHead", plate.X + plate.W - 226f, plate.Y + 2f, 216f, 15f, 12f, TextAlignmentOptions.MidlineRight, true, 0.5f);
            routeMeta = FrontKit.Mono(b, "RouteMeta", plate.X + 400f, plate.Y + 2f, 200f, 20f, 10.5f, TextAlignmentOptions.MidlineLeft);
            routeSub = FrontKit.Mono(b, "RouteSub", plate.X + plate.W - 226f, plate.Y + 17f, 216f, 15f, 10.5f, TextAlignmentOptions.MidlineRight);

            // YOUR session.
            traceHead = Head(b, "TraceHead", colX, 30f, 130f, "// YOUR TRACE");
            opTag = FrontKit.Mono(b, "OpTag", colX + 134f, 30f, ColW - 134f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true, 1f);
            traceValue = FrontKit.Mono(b, "TraceValue", colX + ColW * 0.5f - 65f, 84f, 130f, 34f, 26f, TextAlignmentOptions.Midline, true);
            traceWord = FrontKit.Mono(b, "TraceWord", colX, 126f, ColW, 24f, 16f, TextAlignmentOptions.Midline, true, 2f);
            traceLo = FrontKit.Mono(b, "TraceLo", colX + 6f, 106f, 40f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            traceHi = FrontKit.Mono(b, "TraceHi", colX + ColW - 46f, 106f, 40f, 14f, 10f, TextAlignmentOptions.MidlineRight);
            traceLo.text = "0"; traceHi.text = "100";
            hopLine = FrontKit.Mono(b, "HopLine", colX, 154f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            hopBar = new FrontBar(b, skin, colX, 173f, ColW, 8f);
            estLine = FrontKit.Mono(b, "EstLine", colX, 184f, ColW, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            nextLine = Wrapped(b, "NextLine", colX + 9f, 205f, ColW - 9f, 42f, 10.5f, true);
            heldHead = FrontKit.Mono(b, "HeldHead", colX, 258f, 96f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            heldHead.text = "// HELD";
            heldVal = FrontKit.Mono(b, "HeldVal", colX + ColW - 60f, 258f, 60f, 18f, 11f, TextAlignmentOptions.MidlineRight, true);

            // The FACTION's network (a separate line: its total is never divided by your cap).
            facHead = Head(b, "FacHead", colX, 284f, ColW, "// FACTION NETWORK");
            string[] fk = { "NODES HELD", "SESSIONS", "EW TRUCKS", "DATA CENTER" };
            for (int i = 0; i < 4; i++)
            {
                facKey[i] = FrontKit.Mono(b, "FacKey" + i, colX, 302f + i * 18f, 110f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
                facKey[i].text = fk[i];
                facVal[i] = FrontKit.Mono(b, "FacVal" + i, colX + 110f, 302f + i * 18f, ColW - 110f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            }
            threatHead = Head(b, "ThreatHead", colX, 380f, ColW, "// NETWORK THREAT · INFOCON");
            for (int i = 0; i < 5; i++)
            {
                infocon[i] = FrontKit.Mono(b, "Infocon" + i, colX + i * 55f, 398f, 50f, 22f, 14f, TextAlignmentOptions.Midline, true);
                infocon[i].text = (5 - i).ToString();
            }
            threatWord = FrontKit.Mono(b, "ThreatWord", colX, 422f, ColW - PurgeW - 6f, 18f, 12.5f, TextAlignmentOptions.MidlineLeft, true, 1.5f);
            threatNote = FrontKit.Mono(b, "ThreatNote", colX, 440f, ColW - PurgeW - 6f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            purge = Button(b, "PURGE", colX + ColW - PurgeW, 420f, PurgeW, AvButtonStyle.Danger, () => Do(() => actions.Purge()),
                "Cut every enemy hold on our own network: lands " + (int)CyberRules.PurgeSeconds + " s after the press, costs " + CyberRules.PurgeCost + " CR, then " +
                (int)CyberRules.PurgeCooldownSeconds + " s to recharge. Each cut intrusion's trace jumps " + (int)CyberRules.PurgeTraceKick + " %.");
            purge.Interactable = false;
            keyHead = Head(b, "KeyHead", colX, 464f, ColW, "// KEY");
            string[] words = { "HELD", "HOPPING", "IN REACH", "OUT OF REACH", "TRACED", "OWN ANCHOR" };
            for (int i = 0; i < 6; i++)
            {
                keyText[i] = FrontKit.Mono(b, "Key" + i, colX + 26f + (i / 3) * 130f, 482f + (i % 3) * 16f, 100f, 16f, 10f, TextAlignmentOptions.MidlineLeft, false, 0.5f);
                keyText[i].text = words[i];
            }
            skin.Add(() =>
            {
                traceHead.color = PhosDim; heldHead.color = PhosDim; facHead.color = PhosDim; threatHead.color = PhosDim; keyHead.color = PhosDim;
                traceLo.color = PhosDim; traceHi.color = PhosDim; routeTag.color = PhosDim;
                foreach (TMP_Text t in facKey) t.color = PhosDim;
                foreach (TMP_Text t in keyText) t.color = PhosDim;
            });
        }

        // ---- Inspector, node list, session log ---------------------------------------------------------------------------

        private void BuildInspector(RectTransform hero, float x, float y, float w, float h)
        {
            inspector = new FrontBox(hero, skin, "Inspector", x, y, w, h, "NODE INSPECTOR", AvIcon.ListDetails, FrontLook.Terminal).Tagged("tty2");
            RectTransform b = inspector.Rect;
            inspGlyph = FrontVector.Add(b, "Glyph");
            AvLay.Place(inspGlyph.rectTransform, 0f, 0f, w, h);
            dosTitle = FrontKit.Cond(b, "DosTitle", 58f, 27f, w - 58f - 146f, 22f, 18f, TextAlignmentOptions.MidlineLeft);
            dosHandle = FrontKit.Mono(b, "DosHandle", 58f, 49f, w - 58f - 146f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            dosStamp = FrontKit.Mono(b, "DosStamp", w - 136f, 31f, 126f, 30f, 12f, TextAlignmentOptions.Midline, true, 1f);
            bannerA = FrontKit.Mono(b, "BannerA", 20f, 72f, w - 32f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
            bannerB = FrontKit.Mono(b, "BannerB", 20f, 86f, w - 32f, 14f, 10.5f, TextAlignmentOptions.MidlineLeft);
            string[] keys = { "owner", "reach", "ports", "holding", "hop", "burn", "drop" };
            for (int i = 0; i < DosRows; i++)
            {
                dosKey[i] = FrontKit.Mono(b, "DosKey" + i, 10f, 108f, 74f, 14f, 10f, TextAlignmentOptions.TopLeft, false, 1f);
                dosKey[i].text = keys[i];
                dosVal[i] = Wrapped(b, "DosVal" + i, 88f, 108f, w - 98f, 16f, 10.5f);
            }
            dosNext = Wrapped(b, "DosNext", 22f, 268f, w - 32f, 30f, 10.5f, true);
            float bw = (w - 16f - 12f) / 3f, byy = h - 44f;
            hop = Button(b, "HOP", 8f, byy, bw, AvButtonStyle.Primary, () => Do(() => actions.Hop(selected)), "Pick a node first.");
            burn = Button(b, "BURN", 14f + bw, byy, bw, AvButtonStyle.Danger, () => Do(() => actions.Burn(selected)),
                "Burn this node: post its strike package once and release the node. Your other held nodes stay. Only a node your own session holds.");
            drop = Button(b, "DROP", 20f + 2f * bw, byy, bw, AvButtonStyle.Default, () => Do(() => actions.Drop(selected)),
                "Let go of this node without a package. Your other held nodes stay; with none left the session ends. Only a node your own session holds.");
            skin.Add(() => { foreach (TMP_Text t in dosKey) t.color = PhosDim; });
        }

        private AvControl Button(RectTransform p, string label, float x, float y, float w, AvButtonStyle style, Action click, string help)
        {
            AvControl c = AvControl.Make(p, new AvControl.Spec(label, click, style));
            AvLay.Place(c.Rect, x, y, w, 34f);
            c.SingleLine();
            c.Help = help;
            return c;
        }

        private void BuildList(RectTransform hero, float x, float y, float w, float h)
        {
            list = new FrontBox(hero, skin, "NodeList", x, y, w, h, "NODE LIST", AvIcon.Link, FrontLook.Terminal).Tagged("tty3");
            listW = w;
            RectTransform b = list.Rect;
            for (int i = 0; i < ListRows; i++)   // the row hits sit under the text and glyph layers
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "RowHit" + i, 4f, ListY + i * RowH, w - 8f, RowH - 1f, Color.clear);
                AvHit av = AvHit.On(hit);
                av.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => Pick(rowIds[slot])); };
                av.Hover = on => hit.color = on ? Phos.WithAlpha(0.12f) : Color.clear;
                hit.gameObject.SetActive(false);
                rowHits[i] = hit;
            }
            listGlyph = FrontVector.Add(b, "Glyphs");
            AvLay.Place(listGlyph.rectTransform, 0f, 0f, w, h);
            string[] heads = { "MODE", "HANDLE", "OWNER", "STATE · REACH" };
            float[] hx = { 40f, 76f, 168f, 222f };
            listHead = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                listHead[i] = FrontKit.Mono(b, "ListHead" + i, hx[i], 25f, 120f, 14f, 10f, TextAlignmentOptions.MidlineLeft, false, 1f);
                listHead[i].text = heads[i];
            }
            for (int i = 0; i < ListRows; i++)
            {
                float ry = ListY + i * RowH;
                rowMode[i] = FrontKit.Mono(b, "RowMode" + i, 40f, ry, 32f, RowH - 1f, 10.5f, TextAlignmentOptions.MidlineLeft);
                rowHandle[i] = FrontKit.Mono(b, "RowHandle" + i, 76f, ry, 90f, RowH - 1f, 10.5f, TextAlignmentOptions.MidlineLeft);
                rowOwner[i] = FrontKit.Mono(b, "RowOwner" + i, 168f, ry, 52f, RowH - 1f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
                rowState[i] = FrontKit.Mono(b, "RowState" + i, 222f, ry, w - 222f - 4f, RowH - 1f, 10.5f, TextAlignmentOptions.MidlineLeft);
            }
            listMore = FrontKit.Mono(b, "ListMore", 40f, ListY + ListRows * RowH + 2f, w - 48f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() => { foreach (TMP_Text t in listHead) t.color = PhosDim; listMore.color = PhosDim; });
        }

        /// <summary>A list click: selects exactly like a map click, and brings the node into view when the map is not showing it.</summary>
        private void Pick(int id)
        {
            if (id != selected) locateId = id;
            actions.SelectNode(id);
        }

        private void BuildLog(RectTransform hero, float x, float y, float w, float h)
        {
            log = new FrontBox(hero, skin, "Log", x, y, w, h, "SESSION LOG", AvIcon.Terminal, FrontLook.Terminal).Tagged("tty4");
            logW = w;
            logTextW = w - 12f - 40f - 8f;
            RectTransform b = log.Rect;
            FrontKit.Solid(b, "Well", 8f, 26f, w - 16f, h - 34f, Glass.WithAlpha(0.8f));
            logGlyph = FrontVector.Add(b, "Glyph");
            AvLay.Place(logGlyph.rectTransform, 0f, 0f, w, h);
            for (int i = 0; i < LogRows; i++)
            {
                logStamp[i] = FrontKit.Mono(b, "LogStamp" + i, 12f, LogY + i * LogPitch, 40f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
                logText[i] = FrontKit.Mono(b, "LogText" + i, 52f, LogY + i * LogPitch, logTextW, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            }
            logPrompt = FrontKit.Mono(b, "LogPrompt", 12f, LogY + LogRows * LogPitch, w - 36f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft, true);
        }

        // ---- Paint -----------------------------------------------------------------------------------------------------

        public void Paint(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            selected = n.SelectedId;
            PaintGraph(v, n);
            PaintInspector(v, n);
            PaintList(n);
            PaintLog(v, n);
        }

        private Vector2 P(float x, float y) => plate.P(x, y);

        private static Color StateInk(NetNodeState s) =>
            s == NetNodeState.Held ? Own : s == NetNodeState.Hopping ? HopInk : s == NetNodeState.InReach ? Amber
            : s == NetNodeState.Traced ? TraceRed : OutInk;

        private static bool AllyHeld(in NetNodeView n) => n.State == NetNodeState.Held && n.Owner != "YOU";

        /// <summary>A node held by an ally wears the cool cyan and a dashed ring: ownership is a shape and a word, never colour alone.</summary>
        private static Color NodeInk(in NetNodeView n) => AllyHeld(n) ? Ally : StateInk(n.State);

        /// <summary>The terminal's reading ink of a state word (ready phosphor, caution amber, danger trace red, info the in-flight white).</summary>
        private static Color Tone(AvState s) =>
            s == AvState.Ready ? Phos : s == AvState.Caution ? Amber : s == AvState.Danger ? TraceRed : s == AvState.Info ? HopInk : PhosDim;

        private static string StateWord(in NetNodeView n) =>
            n.State == NetNodeState.Held ? (n.Owner == "YOU" || n.Owner.Length == 0 ? "HELD" : "HELD · " + n.Owner)
            : n.State == NetNodeState.Hopping ? "HOPPING " + n.HopPct + " %" + (n.Owner == "YOU" || n.Owner.Length == 0 ? "" : " · " + n.Owner)
            : n.State == NetNodeState.InReach ? "IN REACH" : n.State == NetNodeState.Traced ? "TRACED" : "OUT OF REACH";

        private static int IndexOf(List<NetNodeView> l, int id)
        {
            for (int i = 0; i < l.Count; i++) if (l[i].Id == id) return i;
            return -1;
        }

        /// <summary>A link with a wide faint underlay and a bright core: phosphor on glass.</summary>
        private static void Glow(FrontVector g, Vector2 a, Vector2 b, Color k, float core)
        {
            g.Line(a, b, core + 9f, k.WithAlpha(0.06f));
            g.Line(a, b, core + 5f, k.WithAlpha(0.13f));
            g.Line(a, b, core, k);
        }

        /// <summary>Data packets running along a live link.</summary>
        private static void Packets(FrontVector g, Vector2 a, Vector2 b, float time, float phase, Color k, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float f = (i / (float)count + time * 0.3f + phase) % 1f;
                Vector2 p = Vector2.Lerp(a, b, f);
                g.Disc(p, 5f, k.WithAlpha(0.16f), 12);
                g.Disc(p, 2.4f, k, 10);
            }
        }

        /// <summary>A dotted leader (the `key ...... value` of a terminal line).</summary>
        private static void Leader(FrontVector g, float x0, float x1, float y, Color k)
        {
            for (float x = x0; x < x1 - 1f; x += 4f) g.Rect(x, y, 1.4f, 1.4f, k);
        }

        /// <summary>Cosmetic flavour: a stable hex dump for the watermark, seeded from the net's size and the pick so it changes when the picture does.</summary>
        private static string HexDump(int seed)
        {
            var sb = new StringBuilder(HexLines * 16);
            uint s = (uint)seed * 2654435761u + 12345u;
            for (int i = 0; i < HexLines; i++)
            {
                sb.Append((0x4A00 + i * 8).ToString("X4"));
                for (int k = 0; k < 4; k++) { s = s * 1664525u + 1013904223u; sb.Append(' ').Append(((s >> 24) & 0xFF).ToString("X2")); }
                if (i < HexLines - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The nodes' screen points for this paint. Installations that share a spot (closer than <see cref="StackGap"/> on screen) are fanned onto a small
        /// ring around their common point, so each keeps its own glyph, label and click; the glyph pass draws a tick back to the true point.
        /// </summary>
        private void Spread(NetworkRoomView n, int count)
        {
            for (int i = 0; i < count; i++) { nodeTrue[i] = nodeAt[i] = P(n.Nodes[i].X, n.Nodes[i].Y); nodeCluster[i] = -1; }
            for (int i = 0; i < count; i++)
            {
                if (nodeCluster[i] >= 0) continue;
                int m = 0;
                Vector2 mid = Vector2.zero;
                for (int j = i; j < count; j++)
                    if (nodeCluster[j] < 0 && (nodeTrue[j] - nodeTrue[i]).sqrMagnitude < StackGap * StackGap) { nodeCluster[j] = i; mid += nodeTrue[j]; m++; }
                if (m < 2) continue;
                mid /= m;
                float r = StackGap * (m == 2 ? 0.55f : 0.75f);
                for (int j = i, k = 0; j < count; j++)
                {
                    if (nodeCluster[j] != i) continue;
                    float a = Mathf.PI * (2f * k++ / m - (m == 2 ? 0f : 0.5f));
                    nodeAt[j] = mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
            }
        }

        private void PaintGraph(FrontRoomView v, NetworkRoomView n)
        {
            FrontMapView mv = plate.View;
            Vector2 sum = Vector2.zero;
            int na = 0;
            for (int i = 0; i < n.Anchors.Count; i++) { sum += new Vector2(n.Anchors[i].X, n.Anchors[i].Y); na++; }
            if (na == 0) for (int i = 0; i < n.Nodes.Count; i++) { sum += new Vector2(n.Nodes[i].X, n.Nodes[i].Y); na++; }
            if (na > 0) mv.AutoCenter(sum / na);
            if (locateId != 0)
            {
                int li = IndexOf(n.Nodes, locateId);
                if (li >= 0 && !mv.Visible(mv.P(n.Nodes[li].X, n.Nodes[li].Y), 24f)) mv.CenterOn(new Vector2(n.Nodes[li].X, n.Nodes[li].Y));
                locateId = 0;
            }
            plate.Begin();
            FrontVector g = plate.G;
            FrontLabels labels = plate.Labels;
            Color own = Own, amber = Amber, danger = TraceRed, key = HopInk, dim = OutInk;
            int count = Math.Min(n.Nodes.Count, MaxNodes);
            Spread(n, count);

            // The glass: a green-black tint over the satellite picture, scanlines and a CRT vignette. Every layer is under 0.2 alpha, so it reads as a tint.
            float px = mv.X, py = mv.Y, pw = mv.W, ph = mv.H;
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x04180E, 0.19f));
            g.Rect(px, py, pw, ph, RoomInk.Rgb(0x0B4A25, 0.11f));
            for (float sy = py + 1f; sy < py + ph; sy += 3f) g.Rect(px, sy, pw, 1f, Black.WithAlpha(0.13f));
            Color v0 = RoomInk.Crt.WithAlpha(0.19f), v1 = RoomInk.Crt.WithAlpha(0f);
            const float e = 110f;
            g.Grad(px, py, e, ph, v0, v1, v1, v0);
            g.Grad(px + pw - e, py, e, ph, v1, v0, v0, v1);
            g.Grad(px, py, pw, e * 0.55f, v0, v0, v1, v1);
            g.Grad(px, py + ph - e * 0.55f, pw, e * 0.55f, v1, v1, v0, v0);

            int hk = n.Nodes.Count * 977 + n.Trace * 31 + n.SelectedId * 7 + (n.HasIntrusion ? 1 : 0);
            if (hk != hexKey) { hexKey = hk; hexGlyph.text = HexDump(hk); }

            FrontMap.AirbaseLayer.Glyphs(g, v.Airbases, P);

            // Edges: nearby nodes first, then the reach of every node you can hop to (from the source the host would hop from), then the intrusions' own paths on top.
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    NetNodeView a = n.Nodes[i], b = n.Nodes[j];
                    float km = new Vector2((a.X - b.X) * n.MapKmW, (a.Y - b.Y) * n.MapKmH).magnitude;
                    if (km > LinkKm) continue;
                    bool both = a.State == NetNodeState.Held && b.State == NetNodeState.Held, burned = a.State == NetNodeState.Traced || b.State == NetNodeState.Traced;
                    if (both) Glow(g, nodeAt[i], nodeAt[j], own, 2f);
                    else if (burned) g.Dashed(nodeAt[i], nodeAt[j], 1.1f, danger.WithAlpha(0.55f), 4f, 4f);
                    else g.Line(nodeAt[i], nodeAt[j], 1f, dim.WithAlpha(0.42f));
                }
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.State != NetNodeState.InReach) continue;
                Vector2 to = nodeAt[i], from;
                int s = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (s >= 0) from = s < count ? nodeAt[s] : P(n.Nodes[s].X, n.Nodes[s].Y);
                else if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) from = P(n.Anchors[nd.FromAnchor].X, n.Anchors[nd.FromAnchor].Y);
                else continue;
                bool pick = nd.Id == n.SelectedId;
                g.Dashed(from, to, pick ? 2f : 1.1f, pick ? AvInk.Select : amber.WithAlpha(0.6f), 4f, 4f);
            }
            int hl = 0;
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.State != NetNodeState.Held && nd.State != NetNodeState.Hopping) continue;
                Vector2 to = nodeAt[i], from;
                int s = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (s >= 0) from = s < count ? nodeAt[s] : P(n.Nodes[s].X, n.Nodes[s].Y);
                else if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) from = P(n.Anchors[nd.FromAnchor].X, n.Anchors[nd.FromAnchor].Y);
                else continue;
                if (nd.State == NetNodeState.Held)
                {
                    Color path = NodeInk(nd);
                    Glow(g, from, to, path, 2.2f);
                    Packets(g, from, to, v.Now, i * 0.37f, path, 2);
                    continue;
                }
                float f = Mathf.Clamp01(nd.HopPct / 100f);
                Vector2 lead = Vector2.Lerp(from, to, f), u = (to - from).normalized, nrm = new Vector2(-u.y, u.x);
                g.Dashed(from, to, 1.6f, key.WithAlpha(0.5f), 7f, 5f);
                g.Line(from, lead, 9f, key.WithAlpha(0.07f));
                g.Line(from, lead, 5f, key.WithAlpha(0.14f));
                g.Line(from, lead, 2.6f, key);
                for (int k = 0; k < 3; k++)   // packets crawling along the opened part
                {
                    float tt = (k / 3f + (v.Now * 0.45f) % 1f) % 1f;
                    g.Disc(Vector2.Lerp(from, lead, tt), 2.4f, key, 10);
                }
                g.Tri(lead + u * 8f, lead - u * 3f + nrm * 5f, lead - u * 3f - nrm * 5f, key);
                if (hl < MaxHops)
                {
                    TMP_Text t = hopLabels[hl++];
                    t.gameObject.SetActive(true);
                    FrontKit.Set(t, nd.HopPct + " %", key);
                    Vector2 mid = (from + to) * 0.5f + nrm * 10f;
                    AvLay.Place(t.rectTransform, mid.x - 2f, mid.y - 7f, 44f, 14f);
                    labels.Take(new Rect(mid.x - 6f, mid.y - 8f, 42f, 16f));
                    g.Rect(mid.x - 6f, mid.y - 8f, 38f, 16f, AvInk.Ground.WithAlpha(0.78f));
                }
            }
            for (int i = hl; i < MaxHops; i++) hopLabels[i].gameObject.SetActive(false);

            // Glyphs.
            for (int t = 0; t < n.Anchors.Count && t < MaxAnchors; t++)
            {
                NetAnchorView a = n.Anchors[t];
                Vector2 p = P(a.X, a.Y);
                Color k = a.Health == AnchorHealth.Live ? own : a.Health == AnchorHealth.Damaged ? amber : danger;
                if (a.Kind == AnchorKind.EwTruck && a.Reach > 0f)
                {
                    g.Disc(p, a.Reach * mv.Scale, k.WithAlpha(a.Health == AnchorHealth.Down ? 0.02f : 0.05f), 64);
                    g.Ring(p, a.Reach * mv.Scale, 1.2f, k.WithAlpha(a.Health == AnchorHealth.Down ? 0.2f : 0.6f), 72, 1f, 1f);
                }
                g.Disc(p, 13f, AvInk.Ground.WithAlpha(0.9f), 20);
                if (a.Kind == AnchorKind.EwTruck) Truck(g, p, k);
                else
                {
                    g.Frame(p.x - 11f, p.y - 11f, 22f, 22f, 1.4f, k);
                    g.Frame(p.x - 8f, p.y - 8f, 16f, 16f, 1.2f, k);
                    for (int r = -1; r <= 1; r += 2) g.Line(p + new Vector2(-4f, r * 3f), p + new Vector2(4f, r * 3f), 1.4f, k);
                }
                if (a.Health == AnchorHealth.Down) FrontKit.Cross(g, p, 9f, danger);
                if (a.LockLeft > 0) g.Ring(p, 16f, 1.4f, danger.WithAlpha(0.9f), 24, 1f, 1f);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                Vector2 p = nodeAt[i];
                Color k = NodeInk(nd);
                if ((p - nodeTrue[i]).sqrMagnitude > 4f) { g.Line(nodeTrue[i], p, 1f, k.WithAlpha(0.5f)); g.Disc(nodeTrue[i], 2f, k.WithAlpha(0.8f), 8); } // fanned out of a shared spot: a tick to where it really stands
                if (nd.State == NetNodeState.Held) { g.Disc(p, 19f, k.WithAlpha(0.07f), 24); g.Disc(p, 15f, k.WithAlpha(0.18f), 24); }
                g.Disc(p, 12f, AvInk.Ground.WithAlpha(0.88f), 20);
                Glyph(g, nd.Kind, p, 9f, k, nd.State == NetNodeState.Held ? 2f : 1.5f);
                if (AllyHeld(nd)) g.Ring(p, 17f, 1.2f, k.WithAlpha(0.9f), 28, 1f, 1f);
                if (nd.State == NetNodeState.Traced)
                {
                    FrontKit.Cross(g, p, 10f, danger);
                    g.Ring(p, 17f, 1.2f, danger.WithAlpha(0.8f), 28, 1f, 1f);
                }
                if (nd.Id == n.SelectedId) FrontKit.Brackets(g, p, 18f, AvInk.Select);
                labels.Take(new Rect(p.x - 14f, p.y - 14f, 28f, 28f));
            }
            // The EW trucks and the data center name themselves first: the session starts from them.
            for (int t = 0; t < MaxAnchors; t++)
            {
                TMP_Text lt = anchorLabels[t];
                if (t >= n.Anchors.Count) { lt.gameObject.SetActive(false); continue; }
                NetAnchorView a = n.Anchors[t];
                if (!plate.Shown(P(a.X, a.Y), 12f)) { lt.gameObject.SetActive(false); continue; }
                lt.gameObject.SetActive(true);
                Color k = a.Health == AnchorHealth.Live ? own : a.Health == AnchorHealth.Damaged ? amber : danger;
                string state = a.Health == AnchorHealth.Down ? "DOWN · REBUILD " + a.Rebuild + " %"
                    : a.LockLeft > 0 ? "TRACED · LOCKED " + FrontKit.Clock(a.LockLeft)
                    : (a.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE") + (a.Reach > 0f ? " · REACH " + Mathf.RoundToInt(a.Reach * n.MapKmW) + " KM" : "");
                lt.text = C2Kit.Tint("<b>" + a.Label + "</b>", k) + "\n" + C2Kit.Tint(state, a.LockLeft > 0 ? danger : PhosDim);
                lt.color = Bright;
                FrontMap.Plate(g, labels.Place(lt, P(a.X, a.Y), 16f, false));
            }
            for (int i = 0; i < count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                Vector2 p = nodeAt[i];
                TMP_Text t = nodeLabels[i];
                if (!plate.Shown(p, 12f)) { t.gameObject.SetActive(false); nodeHits[i].gameObject.SetActive(false); continue; }
                t.gameObject.SetActive(true);
                Color k = NodeInk(nd);
                bool live = nd.State == NetNodeState.Held || nd.State == NetNodeState.Hopping;
                t.text = C2Kit.Tint("<b>" + nd.Label + "</b> · " + KindName[(int)nd.Kind], nd.State == NetNodeState.OutOfReach ? dim : k) + "\n" + C2Kit.Tint(StateWord(nd), live ? k : dim);
                t.color = Bright;
                // The picked node and the nodes in play are always named; the others take a clear spot or stay unlabelled (the node list names every one).
                Rect spot = labels.Place(t, p, 16f, false, !(live || nd.Id == n.SelectedId));
                if (spot.width <= 0f) t.gameObject.SetActive(false); else FrontMap.Plate(g, spot);
                Image hit = nodeHits[i];
                hit.gameObject.SetActive(true);
                hitIds[i] = nd.Id;
                AvLay.Place(hit.rectTransform, p.x - 15f, p.y - 15f, 30f, 30f);
            }
            for (int i = count; i < MaxNodes; i++) { nodeLabels[i].gameObject.SetActive(false); nodeHits[i].gameObject.SetActive(false); }
            airbases.Names(g, v.Airbases, P, mv, (t, a) => labels.Place(t, a, 9f, true, true));

            int inReach = 0, held = 0;
            for (int i = 0; i < count; i++) { if (n.Nodes[i].State == NetNodeState.InReach) inReach++; if (n.Nodes[i].State == NetNodeState.Held) held++; }
            for (int i = 0; i < n.Anchors.Count; i++) plate.Dot(n.Anchors[i].X, n.Anchors[i].Y, own);
            for (int i = 0; i < count; i++) plate.Dot(n.Nodes[i].X, n.Nodes[i].Y, NodeInk(n.Nodes[i]));
            plate.End();

            side.Clear();
            PaintSession(side, n);
            side.Flush();
            PaintRoute(v, n);
            graph.SetMeta(n.Active ? count + " NODES MAPPED · " + inReach + " IN REACH · " + held + " HELD" : n.Meta, held > 0 ? Phos : PhosDim);
        }

        // ---- Bounce chain ------------------------------------------------------------------------------------------------

        /// <summary>How many nodes deep a node's route runs (walking FromNode back to an anchor).</summary>
        private static int Depth(NetworkRoomView n, int i)
        {
            int d = 1;
            for (; d < MaxChips; d++)
            {
                int f = n.Nodes[i].FromNode >= 0 ? IndexOf(n.Nodes, n.Nodes[i].FromNode) : -1;
                if (f < 0) break;
                i = f;
            }
            return d;
        }

        /// <summary>
        /// Builds the chips of the route: LOCALHOST, the EW truck or data centre it entered from, then the held nodes down to the target (the node
        /// being hopped, else your deepest held node). With no session it previews the picked in-reach node's route instead.
        /// </summary>
        private void ChainOf(NetworkRoomView n)
        {
            chipCount = 0; chainAnchor = -1; chainPreview = false;
            if (!n.Active) return;
            int target = -1, best = -1;
            if (n.HasIntrusion)
            {
                for (int i = 0; i < n.Nodes.Count; i++)
                {
                    NetNodeView nd = n.Nodes[i];
                    if (nd.Owner != "YOU" || nd.State != NetNodeState.Held && nd.State != NetNodeState.Hopping) continue;
                    int depth = Depth(n, i) + (nd.State == NetNodeState.Hopping ? 100 : 0);
                    if (depth > best) { best = depth; target = i; }
                }
            }
            else
            {
                int sel = IndexOf(n.Nodes, n.SelectedId);
                if (sel >= 0 && n.Nodes[sel].State == NetNodeState.InReach) { target = sel; chainPreview = true; }
            }
            if (target < 0) return;
            int len = 0, cur = target;
            while (len < MaxChips - 2)
            {
                walk[len++] = cur;
                NetNodeView nd = n.Nodes[cur];
                int from = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
                if (from < 0 || Array.IndexOf(walk, from, 0, len) >= 0)
                {
                    if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) chainAnchor = nd.FromAnchor;
                    break;
                }
                cur = from;
            }
            chipWord[chipCount] = "LOCALHOST"; chipKind[chipCount++] = 0;
            if (chainAnchor >= 0) { chipWord[chipCount] = n.Anchors[chainAnchor].Label; chipKind[chipCount++] = 1; }
            for (int k = len - 1; k >= 0; k--)
            {
                NetNodeView nd = n.Nodes[walk[k]];
                bool hopping = nd.State == NetNodeState.Hopping;
                chipWord[chipCount] = hopping ? nd.Label + " " + nd.HopPct + "%" : nd.Label;
                chipKind[chipCount++] = (byte)(hopping ? 3 : 2);
            }
        }

        /// <summary>The strip: linked chips from LOCALHOST to the target, and a red bar crawling back along them from the target in step with the trace.</summary>
        private void PaintRoute(FrontRoomView v, NetworkRoomView n)
        {
            FrontVector g = routeVec;
            g.Clear();
            float sx = plate.X, sy = plate.Y, sw = plate.W;
            g.Rect(sx, sy, sw, StripH, Glass);
            g.Rect(sx, sy + StripH - 1f, sw, 1f, PhosDim.WithAlpha(0.65f));
            g.Rect(sx, sy, 3f, StripH, Phos);
            ChainOf(n);
            float x0 = sx + 62f, cy = sy + 13f, ch = 18f, gap = 20f, barY = sy + 26f;
            TMP_Text head = routeHead, sub = routeSub;

            if (chipCount == 0)
            {
                // No route: a dim LOCALHOST, a dashed link into an empty socket and the reason.
                Chip(g, x0, cy, "LOCALHOST", OutInk, false, -1f);
                float ex = x0 + "LOCALHOST".Length * Adv + 18f + 8f;
                g.Dashed(new Vector2(ex, cy), new Vector2(ex + gap, cy), 1.2f, Faint, 3f, 3f);
                float kx = ex + gap, ky = cy - ch * 0.5f;   // the empty socket: a dashed frame
                g.Dashed(new Vector2(kx, ky), new Vector2(kx + 44f, ky), 1f, Faint, 3f, 3f);
                g.Dashed(new Vector2(kx, ky + ch), new Vector2(kx + 44f, ky + ch), 1f, Faint, 3f, 3f);
                g.Dashed(new Vector2(kx, ky), new Vector2(kx, ky + ch), 1f, Faint, 3f, 3f);
                g.Dashed(new Vector2(kx + 44f, ky), new Vector2(kx + 44f, ky + ch), 1f, Faint, 3f, 3f);
                g.Rect(x0, barY, 160f, 3f, Faint.WithAlpha(0.45f));
                for (int i = 1; i < MaxChips; i++) chipText[i].gameObject.SetActive(false);
                routeMeta.gameObject.SetActive(false);
                TMP_Text c0 = chipText[0];
                c0.gameObject.SetActive(true);
                AvLay.Place(c0.rectTransform, x0, cy - ch * 0.5f, "LOCALHOST".Length * Adv + 18f, ch);
                FrontKit.Set(c0, "LOCALHOST", OutInk);
                routeNote.gameObject.SetActive(true);
                AvLay.Place(routeNote.rectTransform, ex + gap + 56f, sy + 2f, 330f, 20f);
                FrontKit.Set(routeNote, !n.Active ? (n.Meta.Length > 0 ? "NO ACTIVE ROUTE · " + n.Meta : "NO ACTIVE ROUTE") : "NO ACTIVE ROUTE · PICK A NODE IN REACH", PhosDim);
                FrontKit.Set(head, "TRACE --", PhosDim);
                FrontKit.Set(sub, n.Active ? "NO SESSION" : "NOT CONNECTED", PhosDim);
                g.Flush();
                return;
            }

            routeNote.gameObject.SetActive(false);
            float cx = x0;
            for (int i = 0; i < chipCount; i++)
            {
                chipW[i] = chipWord[i].Length * Adv + 18f;
                chipX[i] = cx;
                cx += chipW[i] + gap;
            }
            int last = chipCount - 1;
            float x1 = chipX[last] + chipW[last];
            float trace = n.HasIntrusion ? n.Trace : 0f;
            float fillX = x1 - (x1 - x0) * trace / 100f;
            bool traced = n.HasIntrusion && trace > 0f;
            NetAnchorView anc = chainAnchor >= 0 ? n.Anchors[chainAnchor] : default;
            // Cosmetic flavour: the bounce count (real, one per link) and a round-trip time that grows with every bounce (made up, never a reading).
            int hops = chipCount - 1, rtt = hops * 41 + (n.Nodes.Count * 13 + hops * 7) % 29;
            string meta = hops + (hops == 1 ? " HOP" : " HOPS") + " · RTT ~" + rtt + " MS";
            float mx = x1 + 26f;
            bool metaFits = mx + meta.Length * Adv <= sx + sw - 236f;
            routeMeta.gameObject.SetActive(metaFits);
            if (metaFits) { AvLay.Place(routeMeta.rectTransform, mx, sy + 2f, meta.Length * Adv + 8f, 20f); FrontKit.Set(routeMeta, meta, PhosDim); }

            // Links first (under the chips), each red once the trace has reached it.
            for (int i = 0; i + 1 < chipCount; i++)
            {
                float a = chipX[i] + chipW[i], b = chipX[i + 1];
                bool hit = traced && (a + b) * 0.5f >= fillX;
                bool toHop = chipKind[i + 1] == 3;
                Color k = hit ? TraceRed : chainPreview ? Amber : toHop ? HopInk : Phos;
                Vector2 pa = new Vector2(a + 1f, cy), pb = new Vector2(b - 1f, cy);
                if (chainPreview || toHop) g.Dashed(pa, pb - new Vector2(5f, 0f), 1.8f, k, 4f, 3f);
                else { g.Line(pa, pb - new Vector2(4f, 0f), 6f, k.WithAlpha(0.13f)); g.Line(pa, pb - new Vector2(4f, 0f), 1.8f, k); }
                g.Tri(pb, pb + new Vector2(-6f, -4f), pb + new Vector2(-6f, 4f), k);
                if (!chainPreview && !hit) { float f = (v.Now * 0.5f + i * 0.31f) % 1f; g.Disc(Vector2.Lerp(pa, pb - new Vector2(6f, 0f), f), 1.8f, Bright, 8); }
            }
            // The chips.
            for (int i = 0; i < MaxChips; i++)
            {
                if (i >= chipCount) { chipText[i].gameObject.SetActive(false); continue; }
                bool hit = traced && chipX[i] + chipW[i] * 0.5f >= fillX;
                bool preview = chainPreview && i > 0;
                Color k = chipKind[i] == 3 ? HopInk : chipKind[i] == 1 ? (anc.LockLeft > 0 || anc.Health == AnchorHealth.Down ? TraceRed : anc.Health == AnchorHealth.Damaged ? Amber : Phos) : Phos;
                if (preview && chipKind[i] != 1) k = Amber;
                if (hit) k = TraceRed;
                float hopFill = chipKind[i] == 3 ? Mathf.Clamp01(HopPctOf(n) / 100f) : -1f;
                Chip(g, chipX[i], cy, chipWord[i], k, preview || chipKind[i] == 3, hopFill);
                TMP_Text t = chipText[i];
                t.gameObject.SetActive(true);
                AvLay.Place(t.rectTransform, chipX[i], cy - ch * 0.5f, chipW[i], ch);
                FrontKit.Set(t, chipWord[i], k);
            }
            // The trace bar: a track under the whole chain, the red fill growing from the target back toward LOCALHOST.
            g.Rect(x0, barY, x1 - x0, 3f, Faint.WithAlpha(0.45f));
            for (int q = 1; q < 4; q++) g.Rect(x0 + (x1 - x0) * q / 4f, barY - 2f, 1f, 7f, Faint.WithAlpha(0.6f));
            if (traced)
            {
                g.Rect(fillX, barY - 3f, x1 - fillX, 9f, TraceRed.WithAlpha(0.12f));
                g.Rect(fillX, barY, x1 - fillX, 3f, TraceRed);
                g.Tri(new Vector2(fillX - 7f, barY + 1.5f), new Vector2(fillX, barY - 4f), new Vector2(fillX, barY + 7f), TraceRed);
            }

            // The right-hand readout.
            if (chainPreview)
            {
                bool lockedTruck = anc.LockLeft > 0;
                NetDossierView d = n.Dossier;
                bool poor = d.Has && d.HopCost > v.Allocation;
                FrontKit.Set(head, lockedTruck ? "ROUTE BLOCKED" : "ROUTE PREVIEW", lockedTruck ? TraceRed : Amber);
                FrontKit.Set(sub, lockedTruck ? anc.Label + " LOCKED " + FrontKit.Clock(anc.LockLeft)
                    : poor ? "NEEDS " + d.HopCost + " AP · HAVE " + v.Allocation : "HOP TO OPEN · " + d.HopCost + " AP", lockedTruck ? TraceRed : Amber);
            }
            else
            {
                FrontKit.Set(head, "TRACE " + n.Trace + " %", TraceRed);
                FrontKit.Set(sub, n.Stale ? "STALE · HOST DATA " + n.AgeSeconds + " S" : n.EstTraceSeconds >= 0 ? "TRACED IN ~" + FrontKit.Clock(n.EstTraceSeconds) : "TRACE HOLDING", n.Stale || n.EstTraceSeconds >= 0 ? TraceRed : PhosDim);
            }
            g.Flush();
        }

        private static int HopPctOf(NetworkRoomView n)
        {
            for (int i = 0; i < n.Nodes.Count; i++) if (n.Nodes[i].State == NetNodeState.Hopping && n.Nodes[i].Owner == "YOU") return n.Nodes[i].HopPct;
            return 0;
        }

        /// <summary>One chip: tinted body, a socket bar on its left edge, a frame (dashed while it is only planned or still hopping) and the hop's fill.</summary>
        private static void Chip(FrontVector g, float x, float cy, string word, Color k, bool dashed, float fill)
        {
            float w = word.Length * Adv + 18f, h = 18f, y = cy - h * 0.5f;
            g.Rect(x, y, w, h, k.WithAlpha(0.13f));
            if (fill >= 0f) g.Rect(x, y, w * fill, h, k.WithAlpha(0.2f));
            g.Rect(x, y, 3f, h, k);
            if (!dashed) g.Frame(x, y, w, h, 1.2f, k);
            else
            {
                g.Dashed(new Vector2(x, y + 0.6f), new Vector2(x + w, y + 0.6f), 1.2f, k, 4f, 3f);
                g.Dashed(new Vector2(x, y + h - 0.6f), new Vector2(x + w, y + h - 0.6f), 1.2f, k, 4f, 3f);
                g.Rect(x + w - 1.2f, y, 1.2f, h, k);
            }
        }

        public bool CancelMode() => false; // no local mode here

        public bool Tick(Vector2 mouse)
        {
            bool changed = plate.Tick(mouse);
            float wheel = FrontPointer.Wheel;
            if (Mathf.Abs(wheel) > 0.01f && list.Rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(list.Rect, mouse, null))
            {
                int to = Mathf.Clamp(scroll + (wheel > 0f ? -1 : 1), 0, Math.Max(0, listCount - ListRows));
                if (to != scroll) { scroll = to; changed = true; }
            }
            return changed;
        }

        private void PaintSession(FrontVector g, NetworkRoomView n)
        {
            float w = ColW;
            // The column is its own pane of glass beside the map: a faint wash and a phosphor edge.
            g.Rect(colX - 5f, FrontKit.HeaderH + 2f, w + 11f, TopH - FrontKit.HeaderH - 4f, Phos.WithAlpha(0.025f));
            g.Rect(colX - 6f, FrontKit.HeaderH + 2f, 1.2f, TopH - FrontKit.HeaderH - 4f, PhosDim.WithAlpha(0.5f));
            // Trace gauge: a half dial of ticks, lit up to the trace, coloured by zone.
            Vector2 c = new Vector2(colX + w * 0.5f, 118f);
            const float r = 70f;
            float trace = n.HasIntrusion ? n.Trace : 0f;
            const int ticks = 36;
            for (int i = 0; i < ticks; i++)
            {
                float a = Mathf.PI + Mathf.PI * (i + 0.5f) / ticks, pct = (i + 0.5f) / ticks * 100f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                bool lit = n.HasIntrusion && pct <= trace;
                g.Line(c + d * (r - 14f), c + d * r, 4f, lit ? Zone(pct) : Faint.WithAlpha(0.55f));
            }
            Vector2 pa = c + new Vector2(-r - 4f, 0f);
            for (int i = 1; i <= 60; i++)
            {
                float a = Mathf.PI + Mathf.PI * i / 60f;
                Vector2 pb = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 4f);
                g.Line(pa, pb, 1f, PhosDim.WithAlpha(0.6f));
                pa = pb;
            }
            // Zone marks on the rim: where the trace turns amber and red.
            for (int q = 0; q < 2; q++)
            {
                float a = Mathf.PI + Mathf.PI * (q == 0 ? 0.25f : 0.60f);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                g.Line(c + d * (r + 4f), c + d * (r + 9f), 1.4f, q == 0 ? Amber : TraceRed);
            }
            float na = Mathf.PI + Mathf.PI * Mathf.Clamp01(trace / 100f);
            Vector2 nd = new Vector2(Mathf.Cos(na), Mathf.Sin(na));
            Color zone = n.HasIntrusion ? Zone(trace) : OutInk;
            g.Disc(c, 40f, zone.WithAlpha(0.05f), 28);
            g.Line(c + nd * 22f, c + nd * (r - 18f), 2f, zone);
            g.Disc(c, 3.5f, zone, 14);
            string word = !n.Active ? "NO LINK" : !n.HasIntrusion ? "NO INTRUSION" : n.Stale ? "STALE" : trace < 25f ? "CLEAN" : trace < 60f ? "WARM" : trace < 85f ? "HOT" : "BURN NOW";
            FrontKit.Set(opTag, n.Operator.Length > 0 ? "OP " + n.Operator : "", Bright);
            FrontKit.Set(traceValue, n.HasIntrusion ? n.Trace + " %" : "--", n.HasIntrusion ? zone : PhosDim);
            FrontKit.Set(traceWord, word, n.HasIntrusion && !n.Stale ? zone : n.Stale ? TraceRed : PhosDim);
            if (n.HasIntrusion && (trace >= 85f || n.Stale)) g.Frame(colX + 50f, 126f, w - 100f, 24f, 1.6f, n.Stale ? TraceRed : zone);

            // The hop in flight: its countdown and a bar that fills as the hop lands.
            FrontKit.Set(hopLine, Fit(hopLine, n.HopLine, w), n.Hopping ? HopInk : PhosDim);
            hopBar.Show(n.Hopping);
            if (n.Hopping) hopBar.Set(n.HopFraction, HopInk);
            FrontKit.Set(estLine, !n.HasIntrusion ? "TRACE STARTS WHEN YOU HOP" : n.EstTraceSeconds >= 0 ? "EST. TRACED IN ~" + FrontKit.Clock(n.EstTraceSeconds) + " AT THIS RATE" : "NOTHING CLIMBS THE TRACE RIGHT NOW", PhosDim);
            FrontKit.Set(nextLine, n.Next, Tone(n.NextTone));
            if (n.Next.Length > 0) g.Rect(colX, 205f, 3f, 38f, Tone(n.NextTone));
            g.Dashed(new Vector2(colX, 250f), new Vector2(colX + w, 250f), 1f, Faint.WithAlpha(0.8f), 3f, 3f);

            // YOUR held nodes: pips (filled held, dashed the hop in flight, hollow free) and the count against the per-intrusion cap.
            int cap = CyberRules.MaxHeldPerIntrusion;
            for (int i = 0; i < cap; i++)
            {
                float px = colX + 100f + i * 30f;
                bool held = i < n.YourHeld, hopping = i == n.YourHeld && n.Hopping;
                if (held) { g.Rect(px, 262f, 26f, 11f, Own.WithAlpha(0.45f)); g.Frame(px, 262f, 26f, 11f, 1.2f, Own); }
                else if (hopping) { g.Dashed(new Vector2(px, 262f), new Vector2(px + 26f, 262f), 1.2f, HopInk, 4f, 3f); g.Dashed(new Vector2(px, 273f), new Vector2(px + 26f, 273f), 1.2f, HopInk, 4f, 3f); g.Line(new Vector2(px, 262f), new Vector2(px, 273f), 1.2f, HopInk); g.Line(new Vector2(px + 26f, 262f), new Vector2(px + 26f, 273f), 1.2f, HopInk); }
                else g.Frame(px, 262f, 26f, 11f, 1f, Faint);
            }
            FrontKit.Set(heldVal, n.YourHeld + " / " + cap, n.YourHeld > 0 ? Phos : PhosDim);

            // The FACTION's network, as `key ...... value` lines.
            int trucksUp = 0, trucks = 0, dc = 0, dcUp = 0;
            for (int i = 0; i < n.Anchors.Count; i++)
            {
                NetAnchorView a = n.Anchors[i];
                if (a.Kind == AnchorKind.EwTruck) { trucks++; if (a.Health != AnchorHealth.Down) trucksUp++; }
                else { dc++; if (a.Health != AnchorHealth.Down) dcUp++; }
            }
            int fcap = CyberRules.MaxHeldPerFaction;
            string[] fv =
            {
                n.Active ? n.HeldTotal + " / " + fcap : "—",
                n.Active ? n.Sessions + " / " + n.IntrusionCap + " RUNNING" : "—",
                n.Active ? trucksUp + " / " + trucks + " UP" : "—",
                !n.Active ? "—" : dc == 0 ? "NONE BUILT" : dcUp > 0 ? "UP" : "DOWN"
            };
            Color[] fc =
            {
                n.HeldTotal >= fcap ? Amber : n.HeldTotal > 0 ? Phos : PhosDim,
                n.Active && n.Sessions >= n.IntrusionCap ? Amber : Bright,
                trucksUp == trucks && trucks > 0 ? Phos : Amber,
                dcUp > 0 ? Phos : dc == 0 ? PhosDim : TraceRed
            };
            for (int i = 0; i < 4; i++)
            {
                FrontKit.Set(facVal[i], fv[i], fc[i]);
                float x0 = colX + facKey[i].text.Length * (Adv + 0f) + 5f, x1 = colX + w - fv[i].Length * Adv - 5f;
                Leader(g, x0, x1, 302f + i * 18f + 11f, Faint.WithAlpha(0.85f));
            }
            g.Dashed(new Vector2(colX, 376f), new Vector2(colX + w, 376f), 1f, Faint.WithAlpha(0.8f), 3f, 3f);

            // Threat to our own network: five blocks, lit from calm (5) down to the current level.
            byte level = (byte)Mathf.Clamp(n.Infocon, 1, 5);
            Color tone = level >= 4 ? Phos : level == 3 ? Amber : TraceRed;
            for (int i = 0; i < 5; i++)
            {
                int lv = 5 - i;
                bool lit = lv >= level;
                float bx = colX + i * 55f;
                g.Rect(bx, 398f, 50f, 22f, lit ? tone.WithAlpha(0.28f) : Glass);
                g.Frame(bx, 398f, 50f, 22f, 1.2f, lit ? tone : Faint);
                FrontKit.Set(infocon[i], lv.ToString(), lit ? Bright : OutInk);
            }
            FrontKit.Set(threatWord, "INFOCON " + level + " · " + n.ThreatWord, tone);
            FrontKit.Set(threatNote, Fit(threatNote, n.ThreatNote, w - PurgeW - 6f), n.Breaches > 0 ? Amber : PhosDim);
            purge.Label = n.PurgeLabel;
            purge.Interactable = n.CanPurge;
            g.Dashed(new Vector2(colX, 462f), new Vector2(colX + w, 462f), 1f, Faint.WithAlpha(0.8f), 3f, 3f);

            // Key swatches.
            Color[] ink = { Own, HopInk, Amber, OutInk, TraceRed, Own };
            for (int i = 0; i < 6; i++)
            {
                Vector2 p = new Vector2(colX + 12f + (i / 3) * 130f, 490f + (i % 3) * 16f);
                if (i == 0) { g.Line(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 2.2f, ink[i]); g.Disc(p, 3.5f, ink[i], 10); }
                else if (i == 1) g.Dashed(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 2f, ink[i], 4f, 3f);
                else if (i == 2) g.Dashed(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 1.2f, ink[i], 3f, 3f);
                else if (i == 3) g.Line(p + new Vector2(-8f, 0f), p + new Vector2(8f, 0f), 1f, ink[i].WithAlpha(0.5f));
                else if (i == 4) FrontKit.Cross(g, p, 5f, ink[i]);
                else { g.Ring(p, 7f, 1.2f, ink[i].WithAlpha(0.7f), 18, 1f, 1f); g.Disc(p, 2.5f, ink[i], 8); }
            }
        }

        /// <summary>Truncates to the width only when the text could overflow it (7.5 px a mono glyph is the ceiling at 10.5 px), so a repaint measures few strings.</summary>
        private static string Fit(TMP_Text t, string s, float width) => s.Length * 7.5f <= width ? s : C2Kit.FitTo(t, s, width);

        /// <summary>The trace gauge's zones: phosphor while clean, amber while warm, trace red once it is hot.</summary>
        private static Color Zone(float pct) => pct < 25f ? Phos : pct < 60f ? Amber : TraceRed;

        private static void Truck(FrontVector g, Vector2 p, Color k)
        {
            g.Frame(p.x - 9f, p.y - 2f, 18f, 8f, 1.4f, k);
            g.Rect(p.x + 3f, p.y - 2f, 6f, 8f, k.WithAlpha(0.5f));
            g.Disc(p + new Vector2(-5f, 7f), 2f, k, 8);
            g.Disc(p + new Vector2(5f, 7f), 2f, k, 8);
            g.Line(p + new Vector2(-3f, -2f), p + new Vector2(-3f, -8f), 1.4f, k);
            g.Line(p + new Vector2(-8f, -9f), p + new Vector2(2f, -9f), 2f, k);
        }

        /// <summary>One glyph per node kind, centred on <paramref name="p"/>.</summary>
        internal static void Glyph(FrontVector g, NodeKind kind, Vector2 p, float r, Color k, float w)
        {
            switch (kind)
            {
                case NodeKind.Radar:
                    g.Ring(p, r, w, k, 24);
                    g.Line(p, p + new Vector2(r * 0.85f, -r * 0.55f), w, k);
                    g.Disc(p, 2f, k, 8);
                    break;
                case NodeKind.SamC2:
                    g.Polyline(new[] { p + new Vector2(0f, -r), p + new Vector2(r * 0.95f, r * 0.8f), p + new Vector2(-r * 0.95f, r * 0.8f) }, w, k, true);
                    g.Line(p + new Vector2(0f, -r * 0.2f), p + new Vector2(0f, r * 0.45f), w, k);
                    break;
                case NodeKind.Relay:
                    var hex = new Vector2[6];
                    for (int i = 0; i < 6; i++) { float a = Mathf.PI / 3f * i; hex[i] = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
                    g.Polyline(hex, w, k, true);
                    g.Disc(p, 2.4f, k, 8);
                    break;
                case NodeKind.Uplink:
                    g.Polyline(new[] { p + new Vector2(0f, r * 0.2f - r), p + new Vector2(r, r * 0.2f), p + new Vector2(0f, r * 1.1f), p + new Vector2(-r, r * 0.2f) }, w, k, true);
                    g.Line(p + new Vector2(0f, -r), p + new Vector2(0f, -r - 4f), w, k);
                    break;
                default:
                    g.Frame(p.x - r * 0.85f, p.y - r * 0.85f, r * 1.7f, r * 1.7f, w, k);
                    for (int i = -1; i <= 1; i += 2) g.Line(p + new Vector2(-r * 0.5f, i * r * 0.4f), p + new Vector2(r * 0.5f, i * r * 0.4f), w * 0.8f, k);
                    break;
            }
        }

        // ---- Inspector -----------------------------------------------------------------------------------------------

        /// <summary>Cosmetic flavour: a stable made-up address for a node, from its kind and id. It is never a reading.</summary>
        private static string Ip(in NetNodeView nd) => "10." + (20 + (int)nd.Kind) + "." + ((nd.Id * 7 + 3) % 200 + 10) + "." + ((nd.Id * 37 + 17) % 240 + 10);

        private void PaintInspector(FrontRoomView v, NetworkRoomView n)
        {
            FrontVector g = inspGlyph;
            g.Clear();
            NetDossierView d = n.Dossier;
            float w = inspector.Width;
            inspector.SetMeta(d.Has ? "SELECTED" : n.Active ? "NO NODE SELECTED" : n.Meta, d.Has ? Bright : PhosDim);
            Color bar = PhosDim;
            string[] vals = new string[DosRows];
            if (d.Has)
            {
                Color k = Own;
                Vector2 p = new Vector2(30f, 47f);
                g.Frame(p.x - 20f, p.y - 20f, 40f, 40f, 1.2f, PhosDim.WithAlpha(0.7f));
                int at = IndexOf(n.Nodes, n.SelectedId);
                NetNodeView sel = at >= 0 ? n.Nodes[at] : default;
                if (at >= 0) { k = NodeInk(sel); Glyph(g, sel.Kind, p, 13f, k, 2.2f); }
                // A stencilled stamp: double frame and a word, so the state never rests on colour alone.
                Color st = d.Stamp.Contains("ALLY") ? Ally : Tone(d.StampTone);
                g.Frame(w - 138f, 29f, 130f, 36f, 1.6f, st);
                g.Frame(w - 135f, 32f, 124f, 30f, 0.8f, st.WithAlpha(0.6f));
                FrontKit.Set(dosStamp, d.Stamp, st);
                FrontKit.Set(dosTitle, d.Title, Bright);
                int gi = d.Handle.IndexOf(" · GRID ", StringComparison.Ordinal);
                FrontKit.Set(dosHandle, Fit(dosHandle, gi >= 0 ? d.Handle.Substring(gi + 3) : d.Handle, w - 58f - 146f), PhosDim);

                // The login banner (cosmetic flavour from the node's handle, kind and owner).
                string handle = at >= 0 ? sel.Handle : "host";
                bool mine = at >= 0 && (sel.Owner == "YOU" && (sel.State == NetNodeState.Held || sel.State == NetNodeState.Hopping));
                bool ally = at >= 0 && sel.Owner.Length > 0 && sel.Owner != "YOU" && (sel.State == NetNodeState.Held || sel.State == NetNodeState.Hopping);
                string kind = at >= 0 ? KindName[(int)sel.Kind] : "HOST";
                FrontKit.Set(bannerA, C2Kit.Tint("CONNECTED TO ", PhosDim) + C2Kit.Tint(handle, Phos) + C2Kit.Tint(at >= 0 ? " [" + Ip(sel) + "]" : "", PhosDim), Phos);
                FrontKit.Set(bannerB, Fit(bannerB, "** " + (mine ? kind + " // ROOTED BY YOU" : ally ? kind + " // ROOTED BY " + sel.Owner : "ENEMY " + kind + " // AUTHORISED USE ONLY") + " **", w - 32f), mine ? PhosDim : ally ? Ally : WarnRed);
                bar = mine ? Phos : ally ? Ally : WarnRed;
                vals[0] = d.Owner; vals[1] = d.Reach; vals[2] = at >= 0 ? Ports(sel) : "—"; vals[3] = d.Hold; vals[4] = d.Hop; vals[5] = d.Burn; vals[6] = d.Drop;
                for (int i = 0; i < DosRows; i++)
                {
                    bool dead = i >= 5 && !n.CanBurn;   // BURN and DROP speak of a node your session does not hold
                    Color ink = dead || i == 4 && !n.CanHop ? PhosDim : i == 0 ? (mine ? Phos : ally ? Ally : Bright) : i == 2 ? PhosDim : Bright;
                    FrontKit.Set(dosVal[i], vals[i], ink);
                }
            }
            else
            {
                FrontKit.Set(dosTitle, "NO NODE SELECTED", PhosDim);
                FrontKit.Set(dosHandle, "—", PhosDim);
                FrontKit.Set(dosStamp, "", PhosDim);
                FrontKit.Set(bannerA, Fit(bannerA, "NO REMOTE HOST · " + (n.Active ? "PICK A NODE ON THE MAP OR IN THE LIST" : n.Meta), w - 32f), PhosDim);
                FrontKit.Set(bannerB, "** NOT CONNECTED **", PhosDim);
                g.Frame(10f, 27f, 40f, 40f, 1.2f, Faint);
                for (int i = 0; i < DosRows; i++) { vals[i] = "—"; FrontKit.Set(dosVal[i], "—", PhosDim); }
            }
            // The `key ...... value` lines: each row is as tall as its text (one line or two), the leaders run from the key to the value.
            float ry = 108f, vw = w - 98f;
            for (int i = 0; i < DosRows; i++)
            {
                float rh = vals[i].Length * Adv <= vw - 3f ? 16f : 29f;
                if (dosTop[i] != ry || dosHigh[i] != rh)
                {
                    dosTop[i] = ry; dosHigh[i] = rh;
                    AvLay.Place(dosKey[i].rectTransform, 10f, ry + 1f, 74f, 14f);
                    AvLay.Place(dosVal[i].rectTransform, 88f, ry, vw, rh);
                }
                Leader(g, 10f + dosKey[i].text.Length * (Adv + 1f) + 4f, 84f, ry + 10.5f, Faint.WithAlpha(0.85f));
                ry += rh + 3f;
            }
            // The banner block: a darker inset with a bar in the host's colour.
            g.Rect(10f, 70f, w - 20f, 32f, Glass);
            g.Frame(10f, 70f, w - 20f, 32f, 1f, PhosDim.WithAlpha(0.4f));
            g.Rect(10f, 70f, 3f, 32f, bar);

            // The pilot's balance against the host's quote: shown as a refusal, never silently hidden.
            bool poor = d.Has && n.CanHop && d.HopCost > v.Allocation;
            string next = d.Next;
            AvState nextTone = d.NextTone;
            if (poor)
            {
                if (d.HopCost != poorCost || v.Allocation != poorHave) { poorCost = d.HopCost; poorHave = v.Allocation; poorNote = "BLOCKED · NEEDS " + d.HopCost + " AP · YOU HAVE " + v.Allocation; }
                next = poorNote; nextTone = AvState.Caution;
            }
            Color nc = Tone(nextTone);
            FrontKit.Set(dosNext, next ?? "", nc);
            if (!string.IsNullOrEmpty(next)) g.Rect(10f, 268f, 3f, 28f, nc);
            g.Flush();
            hop.Interactable = n.CanHop && !poor;
            burn.Interactable = n.CanBurn;
            drop.Interactable = n.CanDrop;
            string help = d.Has ? "Hop to this node. " + d.Hop + ". The host charges exactly this quote. Going deeper from a node your session holds is free." : "Pick a node on the map or in the list first.";
            if (help != hopHelpShown) { hopHelpShown = help; hop.Help = help; }
        }

        // ---- Node list -----------------------------------------------------------------------------------------------

        private static int Rank(NetNodeState s) =>
            s == NetNodeState.Held ? 0 : s == NetNodeState.Hopping ? 1 : s == NetNodeState.InReach ? 2 : s == NetNodeState.Traced ? 3 : 4;

        /// <summary>Cosmetic flavour: the services a host of this kind would answer on (an nmap line). Derived from the node's kind and reach only; it is never a reading.</summary>
        private static string Ports(in NetNodeView nd)
        {
            if (nd.State == NetNodeState.OutOfReach) return "NO ROUTE · ALL FILTERED";
            string list = nd.Kind == NodeKind.Radar ? "554/rtsp  4840/opc-ua  22/ssh" : nd.Kind == NodeKind.SamC2 ? "102/s7  20000/dnp3  22/ssh"
                : nd.Kind == NodeKind.Relay ? "179/bgp  2404/iec104  22/ssh" : nd.Kind == NodeKind.Uplink ? "443/tls  5060/sip  22/ssh" : "22/ssh  443/https  3306/sql";
            return list + " · 3 OPEN";
        }

        /// <summary>Cosmetic flavour: `ls -l` mode bits (held by you drwx, an ally dr-x, in reach d---, out of reach ----). Not a reading of anything.</summary>
        private static string Perms(in NetNodeView n) =>
            n.State == NetNodeState.Held ? (AllyHeld(n) ? "dr-x" : "drwx") : n.State == NetNodeState.Hopping ? "drw-" : n.State == NetNodeState.InReach ? "d---" : n.State == NetNodeState.Traced ? "d!--" : "----";

        private static string PermRich(string p)
        {
            if (PermInk.TryGetValue(p, out string s)) return s;
            var sb = new StringBuilder(80);
            foreach (char c in p)
                sb.Append(C2Kit.Tint(c.ToString(), c == 'd' ? PhosDim : c == 'r' ? Amber : c == 'w' ? TraceRed : c == 'x' ? Phos : c == '!' ? TraceRed : Faint));
            return PermInk[p] = sb.ToString();
        }

        private void PaintList(NetworkRoomView n)
        {
            FrontVector g = listGlyph;
            g.Clear();
            int count = Math.Min(n.Nodes.Count, MaxNodes), rows = 0, inReach = 0, held = 0;
            for (int pass = 0; pass < 5; pass++)
                for (int i = 0; i < count; i++)
                    if (Rank(n.Nodes[i].State) == pass) order[rows++] = i;
            for (int i = 0; i < count; i++) { if (n.Nodes[i].State == NetNodeState.InReach) inReach++; if (n.Nodes[i].State == NetNodeState.Held) held++; }
            list.SetMeta(n.Active ? count + " LISTED · " + inReach + " IN REACH · " + held + " HELD" : n.Meta, held > 0 ? Phos : PhosDim);
            g.Line(new Vector2(6f, ListY - 3f), new Vector2(listW - 6f, ListY - 3f), 1f, Faint.WithAlpha(0.7f));
            // The window follows the pick (a map click can pick a node the list has scrolled past), else the wheel's scroll stays.
            listCount = rows;
            if (n.SelectedId != lastPick)
            {
                lastPick = n.SelectedId;
                for (int i = 0; i < rows; i++)
                    if (n.Nodes[order[i]].Id == n.SelectedId) { if (i < scroll) scroll = i; else if (i >= scroll + ListRows) scroll = i - ListRows + 1; }
            }
            scroll = Mathf.Clamp(scroll, 0, Math.Max(0, rows - ListRows));
            FrontKit.Set(listMore, rows > ListRows ? "ROWS " + (scroll + 1) + "-" + Math.Min(rows, scroll + ListRows) + " OF " + rows + " · WHEEL SCROLLS" : "", PhosDim);
            for (int r = 0; r < ListRows; r++)
            {
                int at = scroll + r;
                if (at >= rows)
                {
                    rowHits[r].gameObject.SetActive(false);
                    rowHits[r].color = Color.clear;
                    bool first = r == 0;
                    FrontKit.Set(rowMode[r], "", PhosDim);
                    FrontKit.Set(rowHandle[r], first && !n.Active ? "—" : "", PhosDim);
                    FrontKit.Set(rowOwner[r], "", PhosDim);
                    FrontKit.Set(rowState[r], first ? (n.Active ? "NO ENEMY NODE SIGHTED YET" : n.Meta) : "", PhosDim);
                    continue;
                }
                NetNodeView nd = n.Nodes[order[at]];
                float y = ListY + r * RowH, mid = y + (RowH - 1f) * 0.5f;
                bool pick = nd.Id == n.SelectedId, out_ = nd.State == NetNodeState.OutOfReach;
                Color k = NodeInk(nd);
                rowIds[r] = nd.Id;
                rowHits[r].gameObject.SetActive(true);
                if ((r & 1) == 1) g.Rect(4f, y, listW - 8f, RowH - 1f, Phos.WithAlpha(0.035f));
                if (pick)
                {
                    g.Rect(4f, y, listW - 8f, RowH - 1f, Phos.WithAlpha(0.15f));
                    g.Rect(4f, y, 3f, RowH - 1f, Phos);
                    g.Tri(new Vector2(10f, mid - 4f), new Vector2(10f, mid + 4f), new Vector2(16f, mid), Phos);
                }
                Glyph(g, nd.Kind, new Vector2(26f, mid), 5.5f, k, 1.4f);
                if (AllyHeld(nd)) g.Ring(new Vector2(26f, mid), 8.5f, 1f, k, 20, 1f, 1f);
                FrontKit.Set(rowMode[r], PermRich(Perms(nd)), Phos);
                FrontKit.Set(rowHandle[r], Fit(rowHandle[r], nd.Handle, 90f), out_ ? OutInk : Bright);
                FrontKit.Set(rowOwner[r], nd.Owner.Length == 0 ? "—" : Fit(rowOwner[r], nd.Owner, 52f), nd.Owner.Length == 0 ? PhosDim : nd.Owner == "YOU" ? Phos : Ally);
                string state = nd.State == NetNodeState.Held ? "HELD" + (AllyHeld(nd) ? " · ALLY" : "")
                    : nd.State == NetNodeState.Hopping ? "HOPPING " + nd.HopPct + " %" + (nd.Owner == "YOU" ? "" : " · ALLY")
                    : (nd.State == NetNodeState.InReach ? "IN REACH" : "OUT OF REACH") + (nd.Reach.Length > 0 ? " · " + nd.Reach.Replace(" · ", " ") : "");
                FrontKit.Set(rowState[r], Fit(rowState[r], state, listW - 222f - 4f), nd.State == NetNodeState.InReach ? Amber : out_ ? OutInk : k);
            }
            g.Flush();
        }

        // ---- Session log ---------------------------------------------------------------------------------------------

        private void Line(int row, string stamp, string text, Color stampInk, Color ink)
        {
            FrontKit.Set(logStamp[row], stamp, stampInk);
            FrontKit.Set(logText[row], Fit(logText[row], text, logTextW), ink);
        }

        /// <summary>Who did it: burns and trace events print in trace red, your lines in phosphor, an ally's in cool cyan.</summary>
        private static Color ActorInk(string text, AvState tone)
        {
            if (tone == AvState.Danger || text.Contains(" burned")) return TraceRed;
            if (text.EndsWith("[ALLY]", StringComparison.Ordinal)) return Ally;
            if (text.EndsWith("[YOU]", StringComparison.Ordinal)) return Phos;
            return Tone(tone);
        }

        private void PaintLog(FrontRoomView v, NetworkRoomView n)
        {
            log.SetMeta(n.Session, PhosDim);
            FrontVector g = logGlyph;
            g.Clear();
            Color muted = PhosDim;
            int row = 0, last = LogRows;
            Line(row++, "#", (v.Faction.Length > 0 ? v.Faction + " " : "") + "NET · ALL SESSIONS LOGGED", muted, PhosDim);
            Line(row++, "$", "who", muted, Bright);
            for (int i = 0; i < n.Roster.Count && row < last; i++, row++)
            {
                FrontLogLine l = n.Roster[i];
                bool you = l.Stamp == "YOU";
                Color ink = you ? (l.Tone == AvState.Ready ? Phos : Tone(l.Tone)) : Ally;
                Line(row, l.Stamp, l.Text, you ? Phos : Ally, ink);
            }
            if (n.Roster.Count == 0 && row < last) Line(row++, "", n.Active ? "no operator online" : "no link", muted, PhosDim);
            if (row < last) Line(row++, "$", "tail net.log", muted, Bright);
            if (n.History.Count == 0 && row < last) Line(row++, "", "(empty) hops and burns print here", muted, PhosDim);
            int from = Math.Max(0, n.History.Count - (last - row));
            for (int i = from; i < n.History.Count && row < last; i++, row++)
            {
                FrontLogLine l = n.History[i];
                Line(row, l.Stamp, l.Text, muted, ActorInk(l.Text, l.Tone));
            }
            for (; row < last; row++) Line(row, "", "", muted, PhosDim);

            // The shell prompt on the last row, a block cursor after it.
            int colon = n.Prompt.IndexOf(':');
            FrontKit.Set(logPrompt, colon > 0 ? C2Kit.Tint(n.Prompt.Substring(0, colon), Phos) + C2Kit.Tint(n.Prompt.Substring(colon), PhosDim) : n.Prompt, Phos);
            float py = LogY + LogRows * LogPitch;
            g.Line(new Vector2(12f, py - 2f), new Vector2(logW - 12f, py - 2f), 1f, Faint.WithAlpha(0.6f));
            g.Rect(12f + n.Prompt.Length * Adv + 6f, py + 3f, 7f, 12f, Phos.WithAlpha(0.9f));
            g.Line(new Vector2(50f, LogY), new Vector2(50f, py - 6f), 1f, Faint.WithAlpha(0.35f));
            g.Flush();
        }
    }
}
