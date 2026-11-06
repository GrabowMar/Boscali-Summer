using System;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Presentation.Ops;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Fronts
{
    /// <summary>
    /// The NETWORK room's NODES tab (spec 6.3), top to bottom: the session strip (your route as chips, truck › held nodes › the hop in flight, the trace bar and
    /// the time until you are traced, DROP ALL; idle: how to start); a breach bar with PURGE while an enemy holds part of our net; filter chips ALL / MINE / ENEMY /
    /// IN REACH; one list of every node the mirror carries, ours and theirs, the picked row open in place (what holding it does, the HOP quote, HOP / BURN / DROP,
    /// or for one of ours what it does and PURGE when breached); when the list leaves room, how a session runs; ZERO-DAY at the foot. Every press is a request.
    /// </summary>
    internal sealed partial class NetworkRoom
    {
        private const float NodeTop = 28f, NodeRowH = 26f, ChipH = 18f, ChipStep = 24f, ChipGap = 18f, KeyW = 52f, LineH = 15f, OpsH = 88f, GuideH = 92f;
        private const int MaxChips = 8, BodyLines = 4;

        private enum NodeFilter : byte { All, Mine, Enemy, Reach }
        private static readonly string[] FilterWord = { "ALL", "MINE", "ENEMY", "IN REACH" };
        /// <summary>Why the enemy's side of the list is empty: the network map shows a node only while a sighting of it is fresh (<see cref="NodeReveal"/>).</summary>
        private static readonly string Unsighted = "NO ENEMY NODE SIGHTED · A RADAR, SAM C2, RELAY, UPLINK OR DATA CENTER SHOWS HERE FOR " + (int)NodeReveal.VisibleSeconds +
            " S AFTER YOUR SIDE SEES IT";

        private FrontBox nodesBox;
        private FrontVector nodesG;
        private float nodesW, nodesH;
        private NodeFilter filter;

        // The session strip: the route's chips (1 the truck, 2 a held node, 3 the node being hopped).
        private TMP_Text sessHead, sessRight, sessLine, traceWord, traceEta;
        private readonly TMP_Text[] chipText = new TMP_Text[MaxChips];
        private readonly string[] chipWord = new string[MaxChips];
        private readonly byte[] chipKind = new byte[MaxChips];
        private readonly float[] chipX = new float[MaxChips], chipY = new float[MaxChips], chipW = new float[MaxChips];
        private readonly int[] walk = new int[MaxChips], chipAt = new int[MaxChips]; // chipAt: the node row behind a chip, -1 for the truck
        private int chipCount, chainAnchor;
        private AvControl dropAll, purgeAll;
        private TMP_Text breachLine;

        // Filters and rows.
        private readonly AvControl[] filterChips = new AvControl[4];
        private readonly int[] filterCount = new int[4];
        private readonly Image[] rowHits = new Image[MaxNodes];
        private readonly TMP_Text[] rowName = new TMP_Text[MaxNodes], rowReach = new TMP_Text[MaxNodes];
        private readonly int[] rowIds = new int[MaxNodes], order = new int[MaxNodes], rankOf = new int[MaxNodes], vis = new int[MaxNodes];
        private TMP_Text listMore, listEmpty;
        private int scroll, listCount, lastOpen, rowsShown = MaxNodes;
        private bool listMoreBelow, handPicked;

        // The open row's body.
        private readonly TMP_Text[] bodyKey = new TMP_Text[BodyLines], bodyVal = new TMP_Text[BodyLines];
        private readonly string[] bodyK = new string[BodyLines], bodyV = new string[BodyLines];
        private int bodyN, bodyButtons; // 0 none, 1 HOP / BURN / DROP, 2 PURGE
        private string bodyNext = "";
        private Color bodyInk;
        private TMP_Text bodyNextText;
        private AvControl hop, burn, drop, purge;
        private string hopHelpShown, poorNote;
        private int poorCost = -1, poorHave = -1;

        // OPERATIONS and the guide.
        private TMP_Text opsFunds, zdFoot, guideHead, guideText;
        private AvControl zdQueue;

        private void BuildNodes(RectTransform column, float cw, float ch)
        {
            nodesBox = new FrontBox(column, skin, "Nodes", 0f, 0f, cw, ch, "NODES · NETWORK", AvIcon.Link, FrontLook.Terminal).Tagged("tty3");
            RectTransform b = nodesBox.Rect;
            nodesW = cw; nodesH = ch;
            float secW = cw - 2f * SecX, opsY = ch - 8f - OpsH, guideY = opsY - 8f - GuideH;
            nodesG = FrontVector.Add(b, "Glyphs");
            AvLay.Place(nodesG.rectTransform, 0f, 0f, cw, ch);

            // Controls before the row hits: a press by words finds a button before a row that happens to say the same word.
            float fw = (secW - 18f) / 4f;
            string[] filterHelp =
            {
                "Every node the network map carries, ours and theirs.", "Only ours: the data centers and EW trucks.", "Only the enemy's nodes.",
                "The enemy nodes a hop reaches now, and the ones your session holds or is hopping.",
            };
            for (int i = 0; i < filterChips.Length; i++)
            {
                NodeFilter f = (NodeFilter)i;
                filterChips[i] = Small(b, FilterWord[i], SecX + i * (fw + 6f), 0f, fw, () => { actions.Touch(); SetFilter(f); }, filterHelp[i]);
            }
            dropAll = Small(b, "DROP ALL", SecX + 10f, 0f, 130f, () => Do(() => actions.DropAll()),
                "Ends your whole session at once: every node you hold is let go without a package and your trace clears. To cash a node in first, BURN it.");
            dropAll.SetStyle(AvButtonStyle.Danger);
            string purgeHelp = "Cut every enemy hold on our own network: lands " + (int)CyberRules.PurgeSeconds + " s after the press, costs " + CyberRules.PurgeCost + " AP, then " +
                (int)CyberRules.PurgeCooldownSeconds + " s to recharge. Each cut intrusion's trace jumps " + (int)CyberRules.PurgeTraceKick + " %.";
            purgeAll = Small(b, "PURGE", SecX + secW - 10f - 140f, 0f, 140f, () => Do(() => actions.Purge()), purgeHelp);
            purgeAll.SetStyle(AvButtonStyle.Danger);
            float bw = (secW - 28f - 12f) / 3f;
            hop = Small(b, "HOP", SecX + 14f, 0f, bw, () => Do(() => actions.Hop(selected)), "Pick a node first.");
            hop.SetStyle(AvButtonStyle.Primary);
            burn = Small(b, "BURN", SecX + 20f + bw, 0f, bw, () => Do(() => actions.Burn(selected)),
                "Burn this node: post its strike package once and release the node. Your other held nodes stay. Only a node your own session holds.");
            burn.SetStyle(AvButtonStyle.Danger);
            drop = Small(b, "DROP", SecX + 26f + 2f * bw, 0f, bw, () => Do(() => actions.Drop(selected)),
                "Let go of this node without a package. Your other held nodes stay; with none left the session ends. Only a node your own session holds.");
            purge = Small(b, "PURGE", SecX + 14f, 0f, bw, () => Do(() => actions.Purge()), purgeHelp);
            purge.SetStyle(AvButtonStyle.Danger);
            int zdSeconds = (int)OpsRules.ZeroDaySeconds, zdHalved = (int)OpsRules.ZeroDayHalvedSeconds;
            zdQueue = Small(b, "QUEUE", SecX + secW - 8f - 160f, opsY + 4f, 160f,() => { actions.Touch(); actions.Queue(Front.Cyber, ProgrammeId.ZeroDay, 0); },
                "Queues a ZERO-DAY like a build: front funds pay for it, then it runs. Their SAM C2 node nearest the front goes dark: no SAM within 3 km of it launches for " +
                FrontKit.Clock(zdSeconds) + " (" + FrontKit.Clock(zdHalved) + " when one of their EW trucks stands within " + (int)(OpsRules.EwHalveMetres / 1000f) +
                " km). Needs a SAM C2 node of theirs on the map.");
            zdQueue.SetStyle(AvButtonStyle.Primary);

            for (int i = 0; i < MaxNodes; i++)
            {
                int slot = i;
                Image hit = FrontKit.Solid(b, "RowHit" + i, SecX, 0f, secW, NodeRowH, Color.clear);
                AvHit av = AvHit.On(hit);
                av.Click = e => { if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Do(() => Pick(rowIds[slot], true)); };
                av.Hover = on => hit.color = on ? Phos.WithAlpha(0.1f) : Color.clear;
                hit.gameObject.SetActive(false);
                rowHits[i] = hit;
            }

            sessHead = FrontKit.Mono(b, "SessHead", SecX + 10f, 0f, secW * 0.5f, 18f, 11.5f, TextAlignmentOptions.MidlineLeft, true, 1f);
            sessRight = FrontKit.Mono(b, "SessRight", SecX + secW * 0.4f, 0f, secW * 0.6f - 10f, 18f, 10.5f, TextAlignmentOptions.MidlineRight);
            sessLine = Wrapped(b, "SessLine", SecX + 10f, 0f, secW - 20f, 30f, 10.5f);
            for (int i = 0; i < MaxChips; i++)
            {
                chipText[i] = FrontKit.Mono(b, "Chip" + i, 0f, 0f, 80f, ChipH, 10.5f, TextAlignmentOptions.Midline, true);
                chipText[i].gameObject.SetActive(false);
            }
            traceWord = FrontKit.Mono(b, "TraceWord", SecX + 10f, 0f, 220f, 16f, 11.5f, TextAlignmentOptions.MidlineLeft, true);
            traceEta = FrontKit.Mono(b, "TraceEta", SecX + secW - 10f - 300f, 0f, 300f, 16f, 10.5f, TextAlignmentOptions.MidlineRight, true);
            breachLine = Wrapped(b, "BreachLine", SecX + 30f, 0f, secW - 30f - 160f, 30f, 10.5f, true);

            for (int i = 0; i < MaxNodes; i++)
            {
                rowName[i] = FrontKit.Mono(b, "RowName" + i, SecX + 32f, 0f, 290f, NodeRowH, 11f, TextAlignmentOptions.MidlineLeft, true);
                rowReach[i] = FrontKit.Mono(b, "RowReach" + i, SecX + secW - 8f - 262f, 0f, 262f, NodeRowH, 10.5f, TextAlignmentOptions.MidlineRight);
                rowName[i].gameObject.SetActive(false);
                rowReach[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < BodyLines; i++)
            {
                bodyKey[i] = FrontKit.Mono(b, "BodyKey" + i, SecX + 14f, 0f, KeyW, 14f, 10f, TextAlignmentOptions.TopLeft, false, 1f);
                bodyVal[i] = Wrapped(b, "BodyVal" + i, SecX + 14f + KeyW, 0f, secW - 28f - KeyW, LineH, 10.5f);
            }
            bodyNextText = Wrapped(b, "BodyNext", SecX + 22f, 0f, secW - 36f, 30f, 10.5f, true);
            listMore = FrontKit.Mono(b, "ListMore", SecX + 10f, 0f, secW - 20f, 14f, 10f, TextAlignmentOptions.MidlineLeft);
            listEmpty = Wrapped(b, "ListEmpty", SecX + 10f, 0f, secW - 20f, 30f, 10.5f);

            // How a session runs: shown in the room the list leaves above OPERATIONS.
            guideHead = SecHead(b, "GuideHead", guideY, secW);
            guideHead.text = "HOW A SESSION RUNS";
            guideText = Wrapped(b, "Guide", SecX + 10f, guideY + 24f, secW - 20f, GuideH - 28f, 10.5f);
            guideText.text = "1 · HOP A NODE IN REACH OF AN EW TRUCK: " + CyberRules.StartCost + " AP OPENS YOUR SESSION\n"
                + "2 · WHILE YOU HOLD IT, ITS EFFECT RUNS AND YOUR TRACE CLIMBS\n"
                + "3 · HOP ON FROM IT (" + (int)(CyberGraph.ChainReach / 1000f) + " KM) FOR FREE, UP TO " + CyberRules.MaxHeldPerIntrusion + " HELD\n"
                + "4 · BURN A NODE FOR ITS STRIKE PACKAGE · DROP ALL BEFORE THE TRACE FILLS";

            TMP_Text opsHead = SecHead(b, "OpsHead", opsY, secW);
            opsHead.text = "OPERATIONS";
            opsFunds = FrontKit.Mono(b, "OpsFunds", SecX + 190f, opsY + 5f, secW - 190f - 176f, 18f, 10.5f, TextAlignmentOptions.MidlineLeft);
            TMP_Text zdLine = Wrapped(b, "ZeroDay", SecX + 10f, opsY + 31f, secW - 20f, 32f, 10.5f);
            zdLine.text = "<b>ZERO-DAY</b> · THEIR SAM C2 NEAREST THE FRONT GOES DARK: NO SAM WITHIN 3 KM LAUNCHES FOR " + FrontKit.Clock(zdSeconds)
                + " (" + FrontKit.Clock(zdHalved) + " NEAR ONE OF THEIR EW TRUCKS)";
            zdFoot = FrontKit.Mono(b, "ZeroDayFoot", SecX + 10f, opsY + 66f, secW - 20f, 16f, 10.5f, TextAlignmentOptions.MidlineLeft);
            skin.Add(() =>
            {
                guideHead.color = PhosDim; guideText.color = PhosDim; opsHead.color = Amber; opsFunds.color = PhosDim; listMore.color = PhosDim;
                foreach (TMP_Text t in bodyKey) t.color = PhosDim;
            });
        }

        // ---- Picks and the filter -------------------------------------------------------------------------------------

        private static bool Passes(NodeFilter f, in NetNodeView nd) =>
            f == NodeFilter.All || (f == NodeFilter.Mine ? nd.Own
                : !nd.Own && (f == NodeFilter.Enemy || nd.State == NetNodeState.InReach || nd.Owner == "YOU" && (nd.State == NetNodeState.Held || nd.State == NetNodeState.Hopping)));

        private bool Shows(NetworkRoomView n, int id)
        {
            int i = IndexOf(n.Nodes, id);
            return i >= 0 && Passes(filter, n.Nodes[i]);
        }

        /// <summary>
        /// A row or map pick. One of ours is room-local (the host never hears of it); the enemy's goes to the controller. A row header clicked again closes it
        /// (<paramref name="toggle"/>); a map click always opens. A row the filter hides brings ALL back, so the pick opens where it can be seen.
        /// </summary>
        private void Pick(int id, bool toggle)
        {
            if (last == null) return;
            NetworkRoomView n = last.Network;
            handPicked = true;
            int at = IndexOf(n.Nodes, id);
            if (at >= 0 && !Passes(filter, n.Nodes[at])) filter = NodeFilter.All;
            if (id < 0)
            {
                selOwn = toggle && selOwn == id ? 0 : id;
                if (selOwn != 0) locateId = id;
                Repaint();
                return;
            }
            if (id == selected)
            {
                if (selOwn != 0) { selOwn = 0; Repaint(); }      // the controller's pick already: open it again
                else if (toggle) actions.SelectNode(id);         // the same id again drops it
                return;
            }
            selOwn = 0;
            locateId = id;
            actions.SelectNode(id);
        }

        /// <summary>A filter chip: a pick it hides is dropped and the first row it shows is picked instead (never a hidden one).</summary>
        private void SetFilter(NodeFilter f)
        {
            filter = f;
            scroll = 0;
            if (last == null) return;
            NetworkRoomView n = last.Network;
            if (selOwn != 0 && !Shows(n, selOwn)) selOwn = 0;
            if (selOwn == 0 && !(n.SelectedId > 0 && Shows(n, n.SelectedId))) AutoPick(n);
            Repaint();
        }

        /// <summary>Picks the first row the filter shows (the list's order: your route, then what a hop reaches). False when it shows none.</summary>
        private bool AutoPick(NetworkRoomView n)
        {
            int count = Order(n);
            for (int k = 0; k < count; k++)
            {
                NetNodeView nd = n.Nodes[order[k]];
                if (!Passes(filter, nd)) continue;
                if (nd.Own) selOwn = nd.Id;
                else if (nd.Id != n.SelectedId) actions.SelectNode(nd.Id);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Until the pilot picks by hand the room picks for them (the view leaves the first pick at 0): the best row the filter shows whenever nothing is open, also
        /// after its own pick left the net. A hand pick is never swapped for another.
        /// </summary>
        private void AutoPickOnce(NetworkRoomView n)
        {
            if (!handPicked && n.Active && selOwn == 0 && (n.SelectedId == 0 || n.SelectedId == FrontViews.Gone)) AutoPick(n);
        }

        /// <summary>The list's order: your route, hops in flight, what a hop reaches now, then by hops away, others' holds, out of reach; ours after, breached first.</summary>
        private static int Rank(in NetNodeView nd) =>
            nd.Own ? (nd.Breached ? 80 : 90)
            : nd.State == NetNodeState.Held ? (nd.Owner == "YOU" ? 0 : 40)
            : nd.State == NetNodeState.Hopping ? (nd.Owner == "YOU" ? 1 : 41)
            : nd.State == NetNodeState.InReach ? 10
            : nd.Hops >= 2 ? 10 + nd.Hops
            : 50;

        /// <summary>Fills <see cref="order"/> with the row indices in list order (stable); returns the count.</summary>
        private int Order(NetworkRoomView n)
        {
            int count = Math.Min(n.Nodes.Count, MaxNodes);
            for (int i = 0; i < count; i++)
            {
                int r = Rank(n.Nodes[i]), k = i;
                while (k > 0 && rankOf[k - 1] > r) { order[k] = order[k - 1]; rankOf[k] = rankOf[k - 1]; k--; }
                order[k] = i; rankOf[k] = r;
            }
            return count;
        }

        // ---- Paint ------------------------------------------------------------------------------------------------------

        private static void Put(RectTransform t, float x, float y, float w, float h)
        {
            Vector2 p = t.anchoredPosition, s = t.sizeDelta;
            if (p.x != x || p.y != -y || s.x != w || s.y != h) AvLay.Place(t, x, y, w, h);
        }

        private static void Show(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }

        /// <summary>Lines a wrapped mono text takes at 10.5 px (a word that does not fit moves down whole, hence the slack).</summary>
        private static int Lines(string s, float w) => string.IsNullOrEmpty(s) ? 1 : Math.Max(1, Mathf.CeilToInt(s.Length * Adv / (w - 24f)));

        private void PaintNodes(FrontRoomView v)
        {
            NetworkRoomView n = v.Network;
            FrontVector g = nodesG;
            g.Clear();
            float secW = nodesW - 2f * SecX, opsY = nodesH - 8f - OpsH, guideY = opsY - 8f - GuideH;
            nodesBox.SetMeta(n.Active ? n.HasIntrusion ? "SESSION LIVE · TRACE " + n.Trace + " %" : "NO SESSION" : n.Meta, n.HasIntrusion ? HopInk : PhosDim);

            float y = PaintStrip(n, g, NodeTop, secW) + 6f;
            y = PaintBreach(n, g, y, secW);

            // Filter chips with their counts.
            int count = Order(n);
            for (int f = 0; f < filterCount.Length; f++) filterCount[f] = 0;
            for (int i = 0; i < count; i++) for (int f = 0; f < filterCount.Length; f++) if (Passes((NodeFilter)f, n.Nodes[i])) filterCount[f]++;
            float fw = (secW - 18f) / 4f;
            for (int f = 0; f < filterChips.Length; f++)
            {
                AvControl c = filterChips[f];
                OpsKit.Label(c, FilterWord[f] + " " + filterCount[f]);
                c.Latched = (int)filter == f;
                Put(c.Rect, SecX + f * (fw + 6f), y, fw, 24f);
            }
            y += 32f;

            // The rows the filter shows; the open one (one of ours, else the controller's pick when it shows) takes its body.
            listCount = 0;
            for (int k = 0; k < count; k++) if (Passes(filter, n.Nodes[order[k]])) vis[listCount++] = order[k];
            int open = selOwn != 0 ? selOwn : n.SelectedId > 0 && Shows(n, n.SelectedId) ? n.SelectedId : 0;
            float bodyH = open != 0 ? Body(v, n, open, secW) : 0f;
            if (open != lastOpen)
            {
                lastOpen = open;
                for (int k = 0; k < listCount; k++) if (n.Nodes[vis[k]].Id == open && (k < scroll || k >= scroll + rowsShown)) scroll = k;
            }
            scroll = Mathf.Clamp(scroll, 0, Math.Max(0, listCount - 1));
            // Measure first (the list's well is drawn under the rows): the rows that fit above OPERATIONS, then whether the guide still fits under them.
            float bottom = opsY - 8f, ry = y + 4f;
            int shown = 0;
            listMoreBelow = false;
            for (int k = scroll; k < listCount; k++)
            {
                float h = NodeRowH + (n.Nodes[vis[k]].Id == open ? bodyH : 0f), reserve = k + 1 < listCount ? 18f : 0f;
                if (shown > 0 && ry + h > bottom - reserve) { listMoreBelow = true; break; }
                ry += h + 2f;
                shown++;
            }
            bool more = scroll > 0 || listMoreBelow;
            string note = count == 0 && !n.Active ? n.Next
                : filter != NodeFilter.Mine && filterCount[(int)NodeFilter.Enemy] == 0 ? Unsighted
                : listCount > 0 ? ""
                : filter == NodeFilter.Mine ? "NONE OF OURS STANDS · BUILD A DATA CENTER ON INFRASTRUCTURE"
                : "NO NODE MATCHES " + FilterWord[(int)filter] + " · ALL SHOWS EVERY ONE";
            int noteLines = note.Length > 0 ? Math.Min(3, Lines(note, secW - 20f)) : 0;
            bool guide = ry + (more ? 18f : 0f) + noteLines * LineH + 16f <= guideY;
            Slab(g, SecX, y, secW, (guide ? guideY : opsY) - 8f - y, Phos);

            ry = y + 4f;
            bool bodyShown = false;
            for (int k = scroll, slot = 0; slot < shown; k++, slot++)
            {
                NetNodeView nd = n.Nodes[vis[k]];
                bool isOpen = nd.Id == open;
                float h = NodeRowH + (isOpen ? bodyH : 0f);
                Row(n, nd, slot, ry, h, isOpen, secW, g);
                if (isOpen) { PlaceBody(ry + NodeRowH, secW, g); bodyShown = true; }
                ry += h + 2f;
            }
            rowsShown = Math.Max(1, shown);
            for (int i = shown; i < MaxNodes; i++) { Show(rowHits[i].gameObject, false); Show(rowName[i].gameObject, false); Show(rowReach[i].gameObject, false); }
            if (!bodyShown) HideBody();

            Show(listMore.gameObject, more);
            if (more)
            {
                Put(listMore.rectTransform, SecX + 10f, ry, secW - 20f, 14f);
                FrontKit.Set(listMore, "ROWS " + (scroll + 1) + "-" + (scroll + shown) + " OF " + listCount + " · WHEEL SCROLLS", PhosDim);
                ry += 18f;
            }
            Show(listEmpty.gameObject, noteLines > 0);
            if (noteLines > 0)
            {
                Put(listEmpty.rectTransform, SecX + 10f, ry + 6f, secW - 20f, noteLines * LineH + 2f);
                FrontKit.Set(listEmpty, note, PhosDim);
            }

            // How a session runs, where the list leaves room for it; OPERATIONS at the foot.
            Show(guideText.gameObject, guide);
            Show(guideHead.gameObject, guide);
            if (guide) Slab(g, SecX, guideY, secW, GuideH, PhosDim);
            PaintOps(v, n, g, opsY, secW);
            g.Flush();
        }

        /// <summary>One row: the hex (ours bright and solid, theirs by state), name and place, the reach word; the open row framed with its body under it.</summary>
        private void Row(NetworkRoomView n, in NetNodeView nd, int slot, float y, float h, bool isOpen, float secW, FrontVector g)
        {
            Image hit = rowHits[slot];
            Show(hit.gameObject, true);
            Put(hit.rectTransform, SecX, y, secW, NodeRowH);
            rowIds[slot] = nd.Id;
            if (isOpen)
            {
                g.Rect(SecX, y, secW, h, Phos.WithAlpha(0.07f));
                g.Frame(SecX, y, secW, h, 1.2f, Phos.WithAlpha(0.85f));
                g.Rect(SecX, y, 3f, h, Phos);
                g.Rect(SecX + 3f, y + NodeRowH - 1f, secW - 3f, 1f, Phos.WithAlpha(0.35f));
            }
            else
            {
                g.Rect(SecX, y, secW, NodeRowH, Glass.WithAlpha(0.8f));
                g.Rect(SecX, y + NodeRowH - 1f, secW, 1f, Phos.WithAlpha(0.14f));
            }
            Vector2 c0 = new Vector2(SecX + 17f, y + NodeRowH * 0.5f);
            Color k;
            g.Disc(c0, 9f, Glass, 6);
            if (nd.Own)
            {
                NetAnchorView a = n.Anchors[nd.AnchorAt];
                k = OwnInk(a);
                if (a.Packing) DashedHex(g, c0, 9f, k); else Hex(g, c0, 9f, k, 1.6f);
                if (a.Kind == AnchorKind.EwTruck) g.Disc(c0, 2.6f, k, 8); else Glyph(g, NodeKind.DataCenter, c0, 5f, k, 1.1f);
                if (a.Health == AnchorHealth.Down) FrontKit.Cross(g, c0, 6f, TraceRed);
                if (nd.Breached) Flag(g, c0 + new Vector2(7f, -3f), 10f, TraceRed);
            }
            else
            {
                k = NodeInk(nd);
                Hex(g, c0, 9f, k.WithAlpha(0.5f), 1f);
                Glyph(g, nd.Kind, c0, 5.5f, k, 1.3f);
                if (AllyHeld(nd)) g.Ring(c0, 12f, 1f, k, 20, 1f, 1f);
            }
            TMP_Text name = rowName[slot], reach = rowReach[slot];
            Show(name.gameObject, true);
            Show(reach.gameObject, true);
            Put(name.rectTransform, SecX + 32f, y, 290f, NodeRowH);
            Put(reach.rectTransform, SecX + secW - 8f - 262f, y, 262f, NodeRowH);
            string nm = nd.Label + (nd.Own ? " · MINE" : "") + (string.IsNullOrEmpty(nd.Place) ? "" : " · " + nd.Place);
            bool dim = !nd.Own && nd.State == NetNodeState.OutOfReach && nd.Hops == 0 && !isOpen;
            FrontKit.Set(name, Fit(name, nm, 290f), dim ? OutInk : Bright);
            string word = ReachWord(n, nd, last.Now, out Color rk);
            FrontKit.Set(reach, Fit(reach, word, 262f), rk);
        }

        /// <summary>A row's reach state, worded (the shape beside it says the same): IN REACH VIA EW-1 · 2 HOPS · OUT OF REACH · 41 KM · HELD · BREACHED.</summary>
        private string ReachWord(NetworkRoomView n, in NetNodeView nd, float now, out Color k)
        {
            if (nd.Own)
            {
                NetAnchorView a = n.Anchors[nd.AnchorAt];
                if (nd.Breached) { k = TraceRed; return "BREACHED · AN ENEMY INSIDE"; }
                if (a.Health == AnchorHealth.Down) { k = TraceRed; return a.Kind == AnchorKind.EwTruck ? "DOWN" : "LOST"; }
                if (a.Kind == AnchorKind.DataCenter)
                {
                    k = a.Health == AnchorHealth.Damaged ? Amber : Phos;
                    string runs = CenterOf(n, a.Slot, out InfraCenterView c) && c.Runs.Length > 0 ? " · " + c.Runs : "";
                    return (a.Health == AnchorHealth.Damaged ? "DAMAGED" : "CLEAN") + runs;
                }
                if (a.Packing) { k = Amber; int left = PackLeft(a.Slot, now); return "PACKING UP" + (left >= 0 ? " · ~" + FrontKit.Clock(left) : ""); }
                if (a.LockLeft > 0) { k = TraceRed; return "TRACED · LOCKED " + FrontKit.Clock(a.LockLeft); }
                k = a.Health == AnchorHealth.Damaged ? Amber : Phos;
                return (a.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE") + (TruckOf(n, a.Slot, out InfraTruckView t) ? " · " + t.InReach + " IN REACH" : "");
            }
            bool you = nd.Owner == "YOU" || nd.Owner.Length == 0;
            switch (nd.State)
            {
                case NetNodeState.Held: k = you ? Phos : Ally; return you ? "HELD" : "HELD · " + nd.Owner;
                case NetNodeState.Hopping: k = you ? HopInk : Ally; return "HOPPING " + nd.HopPct + " %" + (you ? "" : " · " + nd.Owner);
                case NetNodeState.InReach: { k = Amber; string via = Via(n, nd); return via.Length > 0 ? "IN REACH VIA " + via : "IN REACH"; }
                case NetNodeState.Traced: k = TraceRed; return "TRACED";
            }
            if (nd.Hops >= 2) { k = PhosDim; return nd.Hops + " HOPS"; }
            k = OutInk;
            return nd.SourceKm >= 0f ? "OUT OF REACH · " + Mathf.RoundToInt(nd.SourceKm) + " KM" : "OUT OF REACH";
        }

        /// <summary>What a hop to an in-reach node starts from: the truck's tag or the held node's.</summary>
        private static string Via(NetworkRoomView n, in NetNodeView nd)
        {
            if (nd.FromAnchor >= 0 && nd.FromAnchor < n.Anchors.Count) return n.Anchors[nd.FromAnchor].Label;
            int at = nd.FromNode >= 0 ? IndexOf(n.Nodes, nd.FromNode) : -1;
            return at >= 0 ? n.Nodes[at].Label : "";
        }

        private static bool TruckOf(NetworkRoomView n, int slot, out InfraTruckView t)
        {
            foreach (InfraTruckView x in n.Trucks) if (x.Ordinal == slot && x.State != InfraState.Building) { t = x; return true; }
            t = default;
            return false;
        }

        private static bool CenterOf(NetworkRoomView n, int slot, out InfraCenterView c)
        {
            foreach (InfraCenterView x in n.Centers) if (x.Slot == slot && x.State != InfraState.Building) { c = x; return true; }
            c = default;
            return false;
        }

        // ---- The open row ----------------------------------------------------------------------------------------------

        private void Line(string key, string val)
        {
            if (bodyN >= BodyLines) return;
            bodyK[bodyN] = key; bodyV[bodyN++] = val ?? "";
        }

        /// <summary>
        /// The open row's words and its height. An enemy node: what holding it does, the HOP quote, BURN (and DROP when your session holds it), the next step (or the
        /// poor-allocation refusal) and HOP / BURN / DROP. One of ours: what it does, where, its state, and PURGE when an enemy holds it.
        /// </summary>
        private float Body(FrontRoomView v, NetworkRoomView n, int open, float secW)
        {
            bodyN = 0; bodyButtons = 0;
            int at = IndexOf(n.Nodes, open);
            if (at < 0) return 0f;
            NetNodeView nd = n.Nodes[at];
            if (!nd.Own)
            {
                NetDossierView d = n.Dossier;
                Line("HOLD", d.Hold);
                Line("HOP", d.Hop);
                Line("BURN", d.Burn);
                if (n.CanDrop) Line("DROP", d.Drop);
                bool poor = d.Has && n.CanHop && d.HopCost > v.Allocation;
                if (poor && (d.HopCost != poorCost || v.Allocation != poorHave)) { poorCost = d.HopCost; poorHave = v.Allocation; poorNote = "BLOCKED · NEEDS " + d.HopCost + " AP · YOU HAVE " + v.Allocation; }
                bodyNext = poor ? poorNote : d.Next ?? "";
                bodyInk = Tone(poor ? AvState.Caution : d.NextTone);
                bodyButtons = 1;
                hop.Interactable = n.CanHop && !poor;
                burn.Interactable = n.CanBurn;
                drop.Interactable = n.CanDrop;
                string help = d.Has ? "Hop to this node. " + d.Hop + ". The host charges exactly this quote. Going deeper from a node your session holds is free." : "Pick a node first.";
                if (help != hopHelpShown) { hopHelpShown = help; hop.Help = help; }
            }
            else
            {
                NetAnchorView a = n.Anchors[nd.AnchorAt];
                if (a.Kind == AnchorKind.EwTruck)
                {
                    TruckOf(n, a.Slot, out InfraTruckView t);
                    Line("REACH", t.InReachWord);
                    Line("TRACE", t.TraceWord);
                    Line("WHERE", t.Grid);
                    bodyNext = a.Health == AnchorHealth.Down ? "DOWN · DEPLOY TRUCK ON INFRASTRUCTURE BUILDS ANOTHER"
                        : a.Packing ? "OFFLINE WHILE IT PACKS UP · NO REACH UNTIL IT REDEPLOYS"
                        : a.LockLeft > 0 ? "TRACED · NO NEW SESSION STARTS FROM IT FOR " + FrontKit.Clock(a.LockLeft)
                        : "HACKS START INSIDE ITS RING · RELOCATE ON INFRASTRUCTURE MOVES IT";
                    bodyInk = a.Health == AnchorHealth.Down || a.LockLeft > 0 ? TraceRed : a.Packing ? Amber : PhosDim;
                }
                else
                {
                    CenterOf(n, a.Slot, out InfraCenterView c);
                    Line("DOES", (c.Runs ?? "") + (string.IsNullOrEmpty(c.Effect) ? "" : " · " + c.Effect) + " · EACH DATA CENTER RUNS ONE TRUCK");
                    Line("WHERE", c.Place);
                    if (nd.Breached)
                    {
                        Line("BREACH", "AN ENEMY SESSION HOLDS IT RIGHT NOW");
                        bodyNext = "PURGE CUTS EVERY ENEMY HOLD ON OUR NET: " + CyberRules.PurgeCost + " AP, LANDS " + (int)CyberRules.PurgeSeconds + " S AFTER THE PRESS";
                        bodyInk = TraceRed;
                        bodyButtons = 2;
                    }
                    else
                    {
                        bodyNext = a.Health == AnchorHealth.Down ? "LOST · REBUILD IT ON INFRASTRUCTURE" : "CLEAN · NO ENEMY INSIDE";
                        bodyInk = a.Health == AnchorHealth.Down ? TraceRed : PhosDim;
                    }
                }
            }
            float valW = secW - 28f - KeyW, h = 4f;
            for (int i = 0; i < bodyN; i++) h += Lines(bodyV[i], valW) * LineH + 3f;
            h += Lines(bodyNext, secW - 36f) * LineH + 6f;
            if (bodyButtons != 0) h += 30f;
            return h + 4f;
        }

        /// <summary>Lays the open row's words and buttons out from <paramref name="top"/> (the row's header bottom).</summary>
        private void PlaceBody(float top, float secW, FrontVector g)
        {
            float valW = secW - 28f - KeyW, by = top + 4f;
            for (int i = 0; i < BodyLines; i++)
            {
                bool on = i < bodyN;
                Show(bodyKey[i].gameObject, on);
                Show(bodyVal[i].gameObject, on);
                if (!on) continue;
                int ln = Lines(bodyV[i], valW);
                Put(bodyKey[i].rectTransform, SecX + 14f, by + 1f, KeyW, 14f);
                Put(bodyVal[i].rectTransform, SecX + 14f + KeyW, by, valW, ln * LineH + 2f);
                FrontKit.Set(bodyKey[i], bodyK[i], PhosDim);
                FrontKit.Set(bodyVal[i], bodyV[i], Bright);
                by += ln * LineH + 3f;
            }
            int nl = Lines(bodyNext, secW - 36f);
            Show(bodyNextText.gameObject, true);
            Put(bodyNextText.rectTransform, SecX + 22f, by + 1f, secW - 36f, nl * LineH + 2f);
            FrontKit.Set(bodyNextText, bodyNext, bodyInk);
            g.Rect(SecX + 14f, by + 2f, 3f, nl * LineH - 1f, bodyInk);
            by += nl * LineH + 6f;
            float bw = (secW - 28f - 12f) / 3f;
            Show(hop.Rect.gameObject, bodyButtons == 1);
            Show(burn.Rect.gameObject, bodyButtons == 1);
            Show(drop.Rect.gameObject, bodyButtons == 1);
            Show(purge.Rect.gameObject, bodyButtons == 2);
            if (bodyButtons == 1)
            {
                Put(hop.Rect, SecX + 14f, by, bw, 24f);
                Put(burn.Rect, SecX + 20f + bw, by, bw, 24f);
                Put(drop.Rect, SecX + 26f + 2f * bw, by, bw, 24f);
            }
            else if (bodyButtons == 2)
            {
                Put(purge.Rect, SecX + 14f, by, bw, 24f);
                OpsKit.Label(purge, last.Network.PurgeLabel); // the countdown or the recharge rides the button
                OpsKit.Enable(purge, last.Network.CanPurge);
            }
        }

        private void HideBody()
        {
            for (int i = 0; i < BodyLines; i++) { Show(bodyKey[i].gameObject, false); Show(bodyVal[i].gameObject, false); }
            Show(bodyNextText.gameObject, false);
            Show(hop.Rect.gameObject, false); Show(burn.Rect.gameObject, false); Show(drop.Rect.gameObject, false); Show(purge.Rect.gameObject, false);
        }

        // ---- Session strip --------------------------------------------------------------------------------------------

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

        /// <summary>The chips of your route: the truck it entered from, then the held nodes down to the target (the node being hopped, else your deepest held node).</summary>
        private void ChainOf(NetworkRoomView n)
        {
            chipCount = 0; chainAnchor = -1;
            if (!n.HasIntrusion) return;
            int target = -1, best = -1;
            for (int i = 0; i < n.Nodes.Count; i++)
            {
                NetNodeView nd = n.Nodes[i];
                if (nd.Own || nd.Owner != "YOU" || nd.State != NetNodeState.Held && nd.State != NetNodeState.Hopping) continue;
                int depth = Depth(n, i) + (nd.State == NetNodeState.Hopping ? 100 : 0);
                if (depth > best) { best = depth; target = i; }
            }
            if (target < 0) return;
            int len = 0, cur = target;
            while (len < MaxChips - 1)
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
            if (chainAnchor >= 0) { chipWord[chipCount] = n.Anchors[chainAnchor].Label; chipAt[chipCount] = -1; chipKind[chipCount++] = 1; }
            for (int k = len - 1; k >= 0 && chipCount < MaxChips; k--)
            {
                NetNodeView nd = n.Nodes[walk[k]];
                bool hopping = nd.State == NetNodeState.Hopping;
                chipWord[chipCount] = nd.Label + (hopping ? " " + nd.HopPct + " %" : " HELD");
                chipAt[chipCount] = walk[k];
                chipKind[chipCount++] = (byte)(hopping ? 3 : 2);
            }
        }

        /// <summary>One chip: tinted body, a socket bar on its left edge, a frame (dashed while the hop is still in flight) and the hop's fill.</summary>
        private static void Chip(FrontVector g, float x, float y, float w, Color k, bool dashed, float fill)
        {
            g.Rect(x, y, w, ChipH, k.WithAlpha(0.13f));
            if (fill >= 0f) g.Rect(x, y, w * fill, ChipH, k.WithAlpha(0.2f));
            g.Rect(x, y, 3f, ChipH, k);
            if (!dashed) { g.Frame(x, y, w, ChipH, 1.2f, k); return; }
            g.Dashed(new Vector2(x, y + 0.6f), new Vector2(x + w, y + 0.6f), 1.2f, k, 4f, 3f);
            g.Dashed(new Vector2(x, y + ChipH - 0.6f), new Vector2(x + w, y + ChipH - 0.6f), 1.2f, k, 4f, 3f);
            g.Rect(x + w - 1.2f, y, 1.2f, ChipH, k);
        }

        /// <summary>The trace's zone ink: calm under 25 %, caution to 60 %, then trace red.</summary>
        private static Color TraceInk(float pct) => pct >= 60f ? TraceRed : pct >= 25f ? Amber : Phos;

        /// <summary>The strip on top: live, the route, the trace and DROP ALL with the next step; idle, one line on how to start. Returns its bottom.</summary>
        private float PaintStrip(NetworkRoomView n, FrontVector g, float y, float secW)
        {
            float x = SecX, x0 = x + 10f, x1 = x + secW - 10f;
            bool live = n.HasIntrusion;
            Put(sessHead.rectTransform, x0, y + 4f, secW * 0.5f, 18f);
            Put(sessRight.rectTransform, x + secW * 0.4f, y + 4f, secW * 0.6f - 10f, 18f);
            Show(traceWord.gameObject, live); Show(traceEta.gameObject, live); Show(dropAll.Rect.gameObject, live);
            if (!live)
            {
                for (int i = 0; i < MaxChips; i++) Show(chipText[i].gameObject, false);
                string line = n.Next ?? "";
                int ln = Math.Min(2, Lines(line, secW - 20f));
                float h = 30f + ln * LineH;
                Slab(g, x, y, secW, h, PhosDim);
                FrontKit.Set(sessHead, "SESSION · IDLE", PhosDim);
                FrontKit.Set(sessRight, n.Sessions > 0 ? n.Sessions + " ALLY SESSION" + (n.Sessions == 1 ? "" : "S") + " RUNNING" : n.Active ? "NO SESSION RUNNING" : "OFFLINE", PhosDim);
                Put(sessLine.rectTransform, x0, y + 24f, secW - 20f, ln * LineH + 2f);
                FrontKit.Set(sessLine, line, Tone(n.NextTone));
                return y + h;
            }

            // The chips, wrapping to a second row when the route outgrows the column.
            ChainOf(n);
            float cx = x0, cy = y + 26f;
            int rows = 1;
            for (int i = 0; i < chipCount; i++)
            {
                chipW[i] = chipWord[i].Length * Adv + 18f;
                if (i > 0 && cx + chipW[i] > x1) { rows++; cy += ChipStep; cx = x0 + ChipGap; }
                chipX[i] = cx; chipY[i] = cy;
                cx += chipW[i] + ChipGap;
            }
            float traceY = y + 26f + rows * ChipStep, barY = traceY + 19f, actY = barY + 16f;
            string next = n.Next ?? "";
            int nl = Math.Min(2, Lines(next, secW - 160f));
            float h2 = actY - y + Math.Max(24f, nl * LineH + 2f) + 8f;
            Slab(g, x, y, secW, h2, HopInk);
            FrontKit.Set(sessHead, "SESSION · LIVE" + (n.Operator.Length > 0 ? " · " + n.Operator : ""), Bright);
            int up = CyberRules.Upkeep(n.YourHeld, n.CentersUp > 0);
            FrontKit.Set(sessRight, n.YourHeld + " / " + CyberRules.MaxHeldPerIntrusion + " HELD · " + (up > 0 ? "UPKEEP " + up + " AP / " + CyberRules.UpkeepSeconds + " S" : "NO UPKEEP"), PhosDim);

            NetAnchorView anc = chainAnchor >= 0 ? n.Anchors[chainAnchor] : default;
            for (int i = 0; i < MaxChips; i++)
            {
                TMP_Text t = chipText[i];
                if (i >= chipCount) { Show(t.gameObject, false); continue; }
                byte kind = chipKind[i];
                Color k = kind == 3 ? HopInk : kind == 1 ? (anc.LockLeft > 0 || anc.Health == AnchorHealth.Down ? TraceRed : anc.Health == AnchorHealth.Damaged ? Amber : Phos) : Phos;
                float fill = kind == 3 ? Mathf.Clamp01(n.Nodes[chipAt[i]].HopPct / 100f) : -1f;
                Chip(g, chipX[i], chipY[i], chipW[i], k, kind == 3, fill);
                if (i > 0)
                {
                    bool wrapped = chipY[i] > chipY[i - 1];
                    float my = chipY[i] + ChipH * 0.5f;
                    Vector2 a = wrapped ? new Vector2(x0 + 2f, my) : new Vector2(chipX[i - 1] + chipW[i - 1] + 2f, my), b = new Vector2(chipX[i] - 2f, my);
                    if (kind == 3) g.Dashed(a, b - new Vector2(5f, 0f), 1.6f, k, 4f, 3f); else g.Line(a, b - new Vector2(4f, 0f), 1.6f, k);
                    g.Tri(b, b + new Vector2(-6f, -4f), b + new Vector2(-6f, 4f), k);
                    if (kind != 3) { float f = (last.Now * 0.5f + i * 0.31f) % 1f; g.Disc(Vector2.Lerp(a, b - new Vector2(6f, 0f), f), 1.8f, Bright, 8); }
                }
                Show(t.gameObject, true);
                Put(t.rectTransform, chipX[i], chipY[i], chipW[i], ChipH);
                FrontKit.Set(t, chipWord[i], k);
            }

            // The trace: its words, a bar with the 25 / 60 % zone ticks, the time until traced.
            float trace = n.Trace, bw = x1 - x0;
            Color tk = TraceInk(trace);
            Put(traceWord.rectTransform, x0, traceY, 220f, 16f);
            Put(traceEta.rectTransform, x1 - 300f, traceY, 300f, 16f);
            FrontKit.Set(traceWord, "TRACE " + n.Trace + " %", tk);
            FrontKit.Set(traceEta, n.Stale ? "STALE · HOST DATA " + n.AgeSeconds + " S" : n.EstTraceSeconds >= 0 ? "TRACED IN ~" + FrontKit.Clock(n.EstTraceSeconds) : "TRACE HOLDING",
                n.Stale || n.EstTraceSeconds >= 0 ? TraceRed : PhosDim);
            g.Rect(x0, barY, bw, 8f, TraceRed.WithAlpha(0.12f));
            g.Frame(x0, barY, bw, 8f, 1f, PhosDim.WithAlpha(0.4f));
            g.Rect(x0, barY, bw * Mathf.Clamp01(trace / 100f), 8f, tk);
            g.Rect(x0 + bw * 0.25f, barY - 3f, 1f, 14f, PhosDim.WithAlpha(0.8f));
            g.Rect(x0 + bw * 0.6f, barY - 3f, 1f, 14f, PhosDim.WithAlpha(0.8f));

            Put(dropAll.Rect, x0, actY, 130f, 24f);
            OpsKit.Enable(dropAll, true);
            Put(sessLine.rectTransform, x0 + 140f, actY + (nl == 1 ? 4f : 0f), secW - 160f, nl * LineH + 2f);
            FrontKit.Set(sessLine, next, Tone(n.NextTone));
            return y + h2;
        }

        /// <summary>While an enemy holds part of our net (or a PURGE is landing): the holds and PURGE. Returns the y under it.</summary>
        private float PaintBreach(NetworkRoomView n, FrontVector g, float y, float secW)
        {
            bool on = n.Breaches > 0 || n.ThreatWord == "PURGING";
            Show(breachLine.gameObject, on);
            Show(purgeAll.Rect.gameObject, on);
            if (!on) return y;
            string s = n.Breaches > 0 ? n.ThreatNote + " · PURGE CUTS EVERY ONE: " + CyberRules.PurgeCost + " AP, LANDS " + (int)CyberRules.PurgeSeconds + " S AFTER THE PRESS"
                : "PURGE UNDER WAY · EVERY ENEMY HOLD ON OUR NET IS CUT WHEN IT LANDS";
            float tw = secW - 30f - 160f;
            int ln = Math.Min(2, Lines(s, tw));
            float h = Math.Max(24f, ln * LineH + 2f) + 12f;
            Slab(g, SecX, y, secW, h, TraceRed);
            Flag(g, new Vector2(SecX + 14f, y + h - 8f), 14f, TraceRed);
            Put(breachLine.rectTransform, SecX + 30f, y + (ln == 1 ? 10f : 6f), tw, ln * LineH + 2f);
            FrontKit.Set(breachLine, s, TraceRed);
            Put(purgeAll.Rect, SecX + secW - 10f - 140f, y + (h - 24f) * 0.5f, 140f, 24f);
            OpsKit.Label(purgeAll, n.PurgeLabel);
            OpsKit.Enable(purgeAll, n.CanPurge);
            return y + h + 6f;
        }

        // ---- OPERATIONS -----------------------------------------------------------------------------------------------

        /// <summary>ZERO-DAY: queued like a build (front funds pay, then it runs), greyed with its refusal; a queued one shows its funding or its countdown.</summary>
        private void PaintOps(FrontRoomView v, NetworkRoomView n, FrontVector g, float opsY, float secW)
        {
            Slab(g, SecX, opsY, secW, OpsH, Amber);
            bool wait = FrontViews.IsWaiting(v.Row);
            FrontKit.Set(opsFunds, wait ? "FRONT FUNDS —" : "FRONT FUNDS " + v.Row.Budget, PhosDim);
            FrontOfferView zd = n.ZeroDay;
            OpsKit.Label(zdQueue, "QUEUE · " + Mathf.RoundToInt(zd.Cost));
            OpsKit.Enable(zdQueue, string.IsNullOrEmpty(zd.Refusal));
            int queued = 0;
            FrontQueueView first = default;
            for (int i = 0; i < v.Queue.Count && i < v.Row.Queue.Count; i++)
                if (v.Row.Queue[i].Id == ProgrammeId.ZeroDay) { if (queued++ == 0) first = v.Queue[i]; }
            string foot;
            Color ink;
            if (queued > 0)
            {
                foot = (first.Building ? "ZERO-DAY RUNS IN " + FrontKit.Clock(first.BuildLeft) : "ZERO-DAY QUEUED · FUNDING " + Mathf.RoundToInt(Mathf.Clamp01(first.Progress) * 100f) + " % · FRONT FUNDS PAY IT")
                    + (queued > 1 ? " · " + (queued - 1) + " MORE QUEUED" : "");
                ink = first.Building ? Amber : HopInk;
            }
            else if (!string.IsNullOrEmpty(zd.Refusal)) { foot = "ZERO-DAY · " + zd.Refusal; ink = Amber; }
            else { foot = "FRONT FUNDS PAY " + Mathf.RoundToInt(zd.Cost) + ", THEN IT RUNS · THE HOST PICKS THE SAM C2"; ink = PhosDim; }
            FrontKit.Set(zdFoot, FrontKit.Fit(zdFoot, foot, 10.5f), ink);
        }
    }
}
