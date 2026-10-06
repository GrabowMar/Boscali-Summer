using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>How a staff post reads on the chart: colour is the tone, the status word is the reading.</summary>
    internal enum StrTone : byte { Allied, Hostile, Unconfirmed, Alert, Caution, Info, Kia }

    /// <summary>One post as the chart draws it. Identity and parentage are the host's.</summary>
    internal struct StrOrgNode
    {
        public int Id, ParentId, Tier;
        public string Name, Rank, Role, Status, Help;
        public StrTone Tone;
        public bool Selected;
    }

    internal static class StrTones
    {
        public static Color Rail(StrTone t)
        {
            switch (t)
            {
                case StrTone.Allied: return AvInk.State(AvState.Ready);
                case StrTone.Hostile: return AvInk.Hostile;
                case StrTone.Alert: return AvInk.State(AvState.Danger);
                case StrTone.Caution: return AvInk.State(AvState.Caution);
                case StrTone.Info: return AvInk.State(AvState.Info);
                default: return AvInk.State(AvState.Inert);
            }
        }

        /// <summary>The status word's colour: readable ink for calm states, tone for the ones that need eyes.</summary>
        public static Color Text(StrTone t)
        {
            switch (t)
            {
                case StrTone.Alert: return AvInk.State(AvState.Danger);
                case StrTone.Caution: return AvInk.State(AvState.Caution);
                case StrTone.Hostile: return Color.Lerp(AvInk.Hostile, AvInk.Ink, 0.5f);
                case StrTone.Allied: return AvInk.State(AvState.Ready);
                case StrTone.Info: return AvInk.State(AvState.Info);
                default: return AvInk.Muted;
            }
        }

        public static string Glyph(StrTone t) =>
            t == StrTone.Alert ? AvStates.Glyph(AvState.Danger) : t == StrTone.Caution ? AvStates.Glyph(AvState.Caution) : "";
    }

    /// <summary>
    /// The chain of command as an org chart: theatre commander on top, component commanders in columns
    /// below with a connector bus, base commanders stacked under the post they answer to on a trunk line.
    /// Bounded to eight posts (the host's ceiling). Height comes from the tree shape, never from text,
    /// because every card is a fixed size whose lines shrink instead of spilling.
    /// </summary>
    internal sealed class StrOrgChart : AvPart
    {
        public const int MaxNodes = 8;
        private const float HeadH = 48f, ChildH = 48f, RowGap = 4f, ColGap = 6f, BusGap = 16f, MaxRootW = 250f, Indent = 12f;

        private sealed class Card
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Name, Sub, Status, Rank;
            public AvHelpTip Tip;
            public bool Hover;
        }

        private readonly Action<int> onSelect;
        private readonly Card[] cards = new Card[MaxNodes];
        private readonly StrOrgNode[] data = new StrOrgNode[MaxNodes];
        private readonly AvQuadGraphic lines;
        private int count;

        // Layout scratch (fixed, reused; nothing allocates while laying out).
        private readonly int[] parent = new int[MaxNodes], level = new int[MaxNodes];
        private readonly int[] heads = new int[MaxNodes], order = new int[MaxNodes];
        private readonly bool[] seen = new bool[MaxNodes];
        private readonly float[] cx = new float[MaxNodes], cy = new float[MaxNodes], cw = new float[MaxNodes], ch = new float[MaxNodes];
        private int emitted;

        public StrOrgChart(RectTransform parentRect, Action<int> select)
        {
            Rect = AvLay.Child(parentRect, "OrgChart");
            onSelect = select;
            var go = new GameObject("Lines", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            lines = go.AddComponent<AvQuadGraphic>();
            lines.raycastTarget = false;
            AvLay.Fill(lines.rectTransform);
            for (int i = 0; i < MaxNodes; i++) cards[i] = MakeCard(i);
            Restyle();
        }

        private Card MakeCard(int index)
        {
            var c = new Card { Root = AvLay.Child(Rect, "Post " + index) };
            c.Frame = AvFrame.Add(c.Root, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(c.Frame.rectTransform);
            c.Rail = AvLay.Solid(c.Root, "Rail", Color.clear);
            c.Name = StrPaint.Fit(c.Root, "Name", AvTextRole.Label);
            c.Sub = StrPaint.Fit(c.Root, "Sub", AvTextRole.Micro);
            c.Rank = StrPaint.Fit(c.Root, "Rank", AvTextRole.Micro);
            c.Status = StrPaint.Fit(c.Root, "Status", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            AvHit hit = AvHit.On(c.Frame);
            hit.Hover = h => { c.Hover = h; StyleCard(index); };
            hit.Click = e => { if (index < count) onSelect?.Invoke(data[index].Id); };
            c.Tip = AvHelpTip.Attach(c.Frame.gameObject, null);
            c.Root.gameObject.SetActive(false);
            return c;
        }

        public int Count => count;

        /// <summary>Replace the posts. Cheap to call at refresh rate: text is written only when it differs.</summary>
        public void SetNodes(StrOrgNode[] nodes, int n)
        {
            int use = nodes == null ? 0 : Mathf.Clamp(n, 0, MaxNodes);
            bool structure = use != count;
            int before = count;
            count = use;
            for (int i = 0; i < use; i++)
            {
                if (i < before && (data[i].Id != nodes[i].Id || data[i].ParentId != nodes[i].ParentId || data[i].Tier != nodes[i].Tier))
                    structure = true;
                data[i] = nodes[i];
                Card c = cards[i];
                c.Root.gameObject.SetActive(true);
                AvText.Set(c.Name, nodes[i].Name);
                AvText.Set(c.Sub, nodes[i].Role ?? "");
                AvText.Set(c.Rank, nodes[i].Rank ?? "");
                AvText.Set(c.Status, StrTones.Glyph(nodes[i].Tone) + nodes[i].Status);
                c.Tip.Text = nodes[i].Help;
                StyleCard(i);
            }
            for (int i = use; i < MaxNodes; i++) cards[i].Root.gameObject.SetActive(false);
            if (structure) Changed();
        }

        public override float Measure(float width) => Compute(width, false);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            Compute(s.W, true);
        }

        private void Emit(int node)
        {
            if (seen[node] || emitted >= count) return;
            seen[node] = true;
            order[emitted++] = node;
            for (int j = 0; j < count; j++)
                if (parent[j] == node) Emit(j);
        }

        /// <summary>Tree shape -> card rectangles and connector lines. Returns the chart height.</summary>
        private float Compute(float width, bool apply)
        {
            if (apply) lines.Begin();
            if (count == 0) { if (apply) lines.End(); return 0f; }

            for (int i = 0; i < count; i++)
            {
                parent[i] = -1;
                seen[i] = false;
                for (int j = 0; j < count; j++)
                    if (j != i && data[j].Id == data[i].ParentId && data[i].ParentId != data[i].Id) { parent[i] = j; break; }
            }
            for (int i = 0; i < count; i++)
            {
                int hops = 0, cur = i;
                while (parent[cur] >= 0 && hops < MaxNodes) { cur = parent[cur]; hops++; }
                level[i] = (data[cur].Tier > 0 ? 1 : 0) + hops;
            }

            int headCount = 0;
            for (int i = 0; i < count; i++) if (level[i] == 1) heads[headCount++] = i;

            float y = 0f;
            int rootLast = -1;
            for (int i = 0; i < count; i++)
            {
                if (level[i] != 0) continue;
                float w = Mathf.Min(width, MaxRootW);
                Set(i, (width - w) * 0.5f, y, w, HeadH);
                y += HeadH + RowGap;
                rootLast = i;
            }
            if (rootLast >= 0) y -= RowGap;

            float busY = y + BusGap * 0.5f;
            if (headCount > 0 && rootLast >= 0) y += BusGap;
            else if (rootLast >= 0) y += 0f;

            int perBand = Mathf.Clamp(headCount, 1, 3);
            float colW = (width - (perBand - 1) * ColGap) / perBand;
            float busMin = float.MaxValue, busMax = float.MinValue;
            if (rootLast >= 0)
            {
                float rc = cx[rootLast] + cw[rootLast] * 0.5f;
                busMin = busMax = rc;
            }

            for (int band = 0; band * perBand < headCount; band++)
            {
                float bandH = 0f;
                for (int k = 0; k < perBand; k++)
                {
                    int slot = band * perBand + k;
                    if (slot >= headCount) break;
                    int head = heads[slot];
                    float x0 = k * (colW + ColGap);
                    Set(head, x0, y, colW, HeadH);
                    float colBottom = y + HeadH;
                    emitted = 0;
                    for (int i = 0; i < count; i++) seen[i] = false;
                    seen[head] = true;
                    for (int j = 0; j < count; j++)
                        if (parent[j] == head) Emit(j);
                    int kids = emitted;
                    float ky = y + HeadH + RowGap + 2f;
                    for (int q = 0; q < kids; q++)
                    {
                        int node = order[q];
                        float indent = Indent * Mathf.Max(1, level[node] - 1);
                        Set(node, x0 + indent, ky, colW - indent, ChildH);
                        ky += ChildH + RowGap;
                        colBottom = ky - RowGap;
                    }
                    bandH = Mathf.Max(bandH, colBottom - y);

                    if (apply)
                    {
                        float hcx = x0 + colW * 0.5f;
                        if (band == 0 && rootLast >= 0)
                        {
                            busMin = Mathf.Min(busMin, hcx);
                            busMax = Mathf.Max(busMax, hcx);
                            lines.Add(hcx - 0.5f, busY, 1f, y - busY);
                        }
                        for (int q = 0; q < kids; q++)
                        {
                            int node = order[q];
                            int p = parent[node];
                            float trunk = cx[p] + 7f;
                            float top = cy[p] + ch[p];
                            float mid = cy[node] + ch[node] * 0.5f;
                            lines.Add(trunk - 0.5f, top, 1f, mid - top);
                            lines.Add(trunk - 0.5f, mid - 0.5f, cx[node] - trunk + 0.5f, 1f);
                        }
                    }
                    else
                    {
                        // Height pass: positions are already in cx/cy; nothing else to record.
                    }
                }
                y += bandH + RowGap + 2f;
            }
            y -= RowGap + 2f;

            if (apply)
            {
                if (rootLast >= 0 && headCount > 0)
                {
                    float rc = cx[rootLast] + cw[rootLast] * 0.5f;
                    lines.Add(rc - 0.5f, cy[rootLast] + ch[rootLast], 1f, busY - (cy[rootLast] + ch[rootLast]));
                    lines.Add(busMin - 0.5f, busY - 0.5f, busMax - busMin + 1f, 1f);
                }
                lines.End();
                for (int i = 0; i < count; i++)
                {
                    Card c = cards[i];
                    AvLay.Place(c.Root, cx[i], cy[i], cw[i], ch[i]);
                    LayoutCard(c, cw[i], ch[i]);
                }
            }
            return Mathf.Max(0f, y);
        }

        private void Set(int i, float x, float y, float w, float h)
        {
            cx[i] = x; cy[i] = y; cw[i] = w; ch[i] = h;
        }

        private static void LayoutCard(Card c, float w, float h)
        {
            // Three short lines beat one crowded one: rank and status, then the name, then the office.
            float statusW = Mathf.Min(AvText.Width(c.Status) + 2f, w * 0.55f);
            float left = 12f, inner = Mathf.Max(20f, w - left - 8f);
            AvLay.Place(c.Rail.rectTransform, 0f, 0f, 3f, h);
            AvLay.Place(c.Rank.rectTransform, left, 3f, Mathf.Max(20f, inner - statusW - 6f), 15f);
            AvLay.Place(c.Status.rectTransform, w - 8f - statusW, 3f, statusW, 15f);
            AvLay.Place(c.Name.rectTransform, left, 17f, inner, 16f);
            AvLay.Place(c.Sub.rectTransform, left, 32f, inner, 15f);
        }

        private void StyleCard(int i)
        {
            if (i >= count) return;
            Card c = cards[i];
            StrOrgNode n = data[i];
            Color tone = StrTones.Rail(n.Tone);
            bool dead = n.Tone == StrTone.Kia;
            Color stroke = n.Selected ? AvInk.Select : c.Hover ? AvInk.Frame : AvInk.Hairline;
            c.Frame.Paint(n.Selected || c.Hover ? AvInk.Raised : AvInk.Inert, stroke);
            c.Rail.color = n.Selected ? AvInk.Select : tone;
            bool quiet = dead || n.Tone == StrTone.Unconfirmed;
            c.Name.color = quiet ? AvInk.Dim : AvInk.Ink;
            c.Sub.color = quiet ? AvInk.Muted : AvInk.Dim;
            c.Rank.color = quiet ? AvInk.Muted : AvInk.Key;
            c.Status.color = StrTones.Text(n.Tone);
        }

        public override void Restyle()
        {
            lines.color = AvInk.Frame.WithAlpha(0.75f);
            lines.SetVerticesDirty();
            for (int i = 0; i < count; i++) StyleCard(i);
        }
    }

    /// <summary>The selected commander's personnel file, drawn as a dossier card.</summary>
    internal sealed class StrDossier : AvPart
    {
        internal struct Data
        {
            public string Name, Rank, Role, Status, Station, Share, Intel, Bio;
            public StrTone Tone;
            public float Share01;
            public string[] TraitLabels, TraitPays;
            public bool[] TraitPenalty;
            public int TraitCount;
            public bool Sealed;
            public Sprite Portrait;
        }

        private const float Pad = 10f, PortraitSize = 56f, TraitH = 20f, MaxTraits = 3;
        private readonly AvFrame frame, portraitFrame;
        private readonly Image rail, portrait;
        private readonly TMP_Text portraitIcon;
        private readonly TMP_Text name, role, status;
        private readonly RectTransform statusBox;
        private readonly AvFrame statusFrame;
        private readonly Image rule;
        private readonly TMP_Text[] fieldKeys = new TMP_Text[3];
        private readonly TMP_Text[] fieldValues = new TMP_Text[3];
        private readonly AvGaugeGraphic shareBar;
        private readonly TMP_Text[] traitLabel = new TMP_Text[(int)MaxTraits];
        private readonly TMP_Text[] traitPay = new TMP_Text[(int)MaxTraits];
        private readonly Image[] traitRule = new Image[(int)MaxTraits];
        private readonly TMP_Text bio;
        private StrTone tone = StrTone.Allied;
        private int traits = 1;
        private bool noTraits = true;

        public StrDossier(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Dossier");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            frame.raycastTarget = true;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            portraitFrame = AvFrame.Add(Rect, "PortraitFrame", AvChamfer.Diagonal(5f));
            portrait = AvLay.Solid(portraitFrame.rectTransform, "Portrait", Color.white);
            portrait.preserveAspect = true;
            AvLay.Fill(portrait.rectTransform, 2f);
            portraitIcon = AvIcons.Make(Rect, AvIcon.User, 30f, Color.white);
            name = AvText.Make(Rect, "Name", AvTextRole.Title, "", TextAlignmentOptions.TopLeft, true);
            role = AvText.Make(Rect, "Role", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            statusBox = AvLay.Child(Rect, "StatusChip");
            statusFrame = AvFrame.Add(statusBox, "Frame", AvChamfer.Diagonal(4f));
            AvLay.Fill(statusFrame.rectTransform);
            status = AvText.Make(statusBox, "Status", AvTextRole.Micro, "", TextAlignmentOptions.Center);
            AvText.Fit(status, false);
            AvLay.Fill(status.rectTransform, 1f);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            string[] keys = { "STATION", "SHARE", "INTEL" };
            for (int i = 0; i < 3; i++)
            {
                fieldKeys[i] = StrPaint.Fit(Rect, "Key " + keys[i], AvTextRole.Micro);
                fieldKeys[i].text = keys[i];
                fieldValues[i] = StrPaint.Fit(Rect, "Value " + keys[i], AvTextRole.DataStrong);
            }
            var go = new GameObject("ShareBar", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            shareBar = go.AddComponent<AvGaugeGraphic>();
            shareBar.Shape = AvGaugeShape.Bar;
            shareBar.raycastTarget = false;
            for (int i = 0; i < traitLabel.Length; i++)
            {
                traitLabel[i] = StrPaint.Fit(Rect, "Trait " + i, AvTextRole.Label);
                traitPay[i] = StrPaint.Fit(Rect, "Pay " + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                traitRule[i] = AvLay.Solid(Rect, "TraitRule " + i, Color.clear);
            }
            bio = AvText.Make(Rect, "Bio", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        /// <summary>Hover help for the whole file (whose file this is and how sure the reader can be of it).</summary>
        public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

        public void Show(Data d)
        {
            bool changed = AvText.Set(name, d.Name);
            changed |= AvText.Set(role, string.IsNullOrEmpty(d.Rank) ? d.Role : string.IsNullOrEmpty(d.Role) ? d.Rank : d.Rank + " · " + d.Role);
            changed |= AvText.Set(status, StrTones.Glyph(d.Tone) + d.Status);
            AvText.Set(fieldValues[0], d.Station);
            AvText.Set(fieldValues[1], d.Share);
            AvText.Set(fieldValues[2], d.Intel);
            shareBar.Value = d.Sealed ? 0f : d.Share01;
            changed |= AvText.Set(bio, d.Bio);

            int n = d.TraitLabels == null ? 0 : Mathf.Clamp(d.TraitCount, 0, (int)MaxTraits);
            bool none = n == 0;
            int rows = Mathf.Max(1, n);
            if (rows != traits || none != noTraits) changed = true;
            traits = rows;
            noTraits = none;
            for (int i = 0; i < traitLabel.Length; i++)
            {
                bool on = i < rows;
                traitLabel[i].gameObject.SetActive(on);
                traitPay[i].gameObject.SetActive(on);
                traitRule[i].gameObject.SetActive(on);
                if (!on) continue;
                if (none)
                {
                    AvText.Set(traitLabel[i], d.Sealed ? "NO RECORD" : "NO NOTABLE TRAITS");
                    AvText.Set(traitPay[i], "");
                }
                else
                {
                    AvText.Set(traitLabel[i], d.TraitLabels[i]);
                    AvText.Set(traitPay[i], d.TraitPays != null ? d.TraitPays[i] : "");
                }
                traitLabel[i].color = none ? AvInk.Muted : AvInk.Ink;
                bool penalty = d.TraitPenalty != null && d.TraitPenalty[i];
                traitPay[i].color = string.IsNullOrEmpty(traitPay[i].text) ? AvInk.Muted
                    : penalty ? AvInk.State(AvState.Caution) : AvInk.State(AvState.Ready);
            }

            bool hasPortrait = d.Portrait != null && !d.Sealed;
            portrait.sprite = hasPortrait ? d.Portrait : null;
            portrait.enabled = hasPortrait;
            portraitIcon.gameObject.SetActive(!hasPortrait);
            if (tone != d.Tone) { tone = d.Tone; Restyle(); }
            if (changed) Changed();
        }

        private float TextW(float width) => Mathf.Max(20f, width - 2f * Pad - PortraitSize - 12f);

        private float ChipW(float width) => Mathf.Min(TextW(width) * 0.45f, AvText.Width(status) + 22f);

        private float HeadH(float width)
        {
            float w = TextW(width);
            float h = AvText.Height(name, Mathf.Max(20f, w - ChipW(width) - 6f)) + 2f + AvText.Height(role, w);
            return Mathf.Max(PortraitSize, h);
        }

        public override float Measure(float width)
        {
            float h = Pad + HeadH(width) + 6f + 1f + 7f + 38f + 4f;
            h += traits * TraitH;
            if (bio.text.Length > 0) h += 6f + AvText.Height(bio, width - 2f * Pad);
            return h + Pad;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), y = Pad;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(portraitFrame.rectTransform, Pad, y, PortraitSize, PortraitSize);
            AvLay.Place(portraitIcon.rectTransform, Pad, y, PortraitSize, PortraitSize);
            float x = Pad + PortraitSize + 12f, chip = ChipW(s.W), nameW = Mathf.Max(20f, w - chip - 6f);
            float nh = AvText.Height(name, nameW), rh = AvText.Height(role, w);
            AvLay.Place(name.rectTransform, x, y, nameW, nh);
            AvLay.Place(statusBox, x + w - chip, y + 2f, chip, 20f);
            AvLay.Place(role.rectTransform, x, y + nh + 2f, w, rh);
            y += HeadH(s.W) + 6f;
            AvLay.Place(rule.rectTransform, Pad, y, s.W - 2f * Pad, 1f);
            y += 8f;
            float span = s.W - 2f * Pad - 16f;
            float[] fws = { span * 0.50f, span * 0.18f, span * 0.32f };
            float fx = Pad;
            for (int i = 0; i < 3; i++)
            {
                AvLay.Place(fieldKeys[i].rectTransform, fx, y, fws[i], 15f);
                AvLay.Place(fieldValues[i].rectTransform, fx, y + 15f, fws[i], 18f);
                if (i == 1) AvLay.Place((RectTransform)shareBar.transform, fx, y + 34f, fws[i], 3f);
                fx += fws[i] + 8f;
            }
            y += 38f + 4f;
            for (int i = 0; i < traits && i < traitLabel.Length; i++)
            {
                float ty = y + i * TraitH;
                AvLay.Place(traitRule[i].rectTransform, Pad, ty, s.W - 2f * Pad, 1f);
                AvLay.Place(traitLabel[i].rectTransform, Pad, ty + 1f, s.W - 2f * Pad - 196f, TraitH - 1f);
                AvLay.Place(traitPay[i].rectTransform, s.W - Pad - 190f, ty + 1f, 190f, TraitH - 1f);
            }
            y += traits * TraitH;
            if (bio.text.Length > 0)
                AvLay.Place(bio.rectTransform, Pad, y + 6f, s.W - 2f * Pad, AvText.Height(bio, s.W - 2f * Pad));
        }

        public override void Restyle()
        {
            Color rail_ = StrTones.Rail(tone);
            frame.Paint(AvInk.Raised, AvInk.Frame.WithAlpha(0.8f));
            frame.BracketColor = AvInk.Frame;
            frame.SetVerticesDirty();
            rail.color = rail_;
            portraitFrame.Paint(AvInk.Inert, rail_.WithAlpha(0.8f));
            portraitIcon.color = AvInk.Muted;
            name.color = AvInk.Ink;
            role.color = AvInk.Dim;
            statusFrame.Paint(rail_.WithAlpha(0.16f), rail_.WithAlpha(0.7f));
            status.color = tone == StrTone.Unconfirmed || tone == StrTone.Kia ? AvInk.Dim : AvInk.Ink;
            rule.color = AvInk.Hairline;
            for (int i = 0; i < 3; i++)
            {
                fieldKeys[i].color = AvInk.Muted;
                fieldValues[i].color = AvInk.Ink;
            }
            shareBar.Track = AvInk.Hairline;
            shareBar.FillColor = shareBar.FillEnd = AvInk.Key;
            shareBar.SetVerticesDirty();
            for (int i = 0; i < traitLabel.Length; i++)
            {
                traitLabel[i].color = noTraits ? AvInk.Muted : AvInk.Ink;
                traitRule[i].color = AvInk.Hairline.WithAlpha(0.6f);
            }
            bio.color = AvInk.Dim;
        }
    }

    /// <summary>
    /// The staff log as a timestamped log: age stamp, tone rail, wrapped line. Rows can open a post. Its
    /// natural height is the first <c>minRows</c> entries; a slot taller than that shows further entries
    /// (up to the pool size) instead of leaving a blank band under the log.
    /// </summary>
  }
