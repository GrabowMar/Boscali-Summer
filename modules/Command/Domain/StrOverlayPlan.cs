using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// A shape the overlay draws in world metres. Notch is legend-only: it is the swatch of the lane bar's hostile-share notch and
    /// never appears in Prims.
    /// </summary>
    internal enum StrShape : byte { Ring, DashedRing, Arrow, DashedArrow, DashedLine, Diamond, DiamondSolid, Hex, Box, Cross, Notch }

    /// <summary>One shape in world metres. Pulse shapes go to the small mesh that is rebuilt while an axis marches.</summary>
    internal struct StrPrim
    {
        public StrShape Shape;
        public StrInkRole Ink;
        public StrItemId Id;
        /// <summary>First point, second point (arrows and lines), all global metres.</summary>
        public float X, Z, X2, Z2;
        /// <summary>Ring radius in metres. Glyph shapes (cross, diamond, box, hex) carry 0: the overlay draws them at a fixed pixel size; so does the highlight ring of a point-like item.</summary>
        public float RadiusMetres;
        /// <summary>Pulse: the marching axis. Selected: the one highlight ring of the selected item.</summary>
        public bool Pulse, Selected;
    }

    /// <summary>What a tag is: the plate that carries the click.</summary>
    internal enum StrTagKind : byte { Effort, Proposal, Front, Sortie, Post }

    /// <summary>One tag: the only raycast object of an item. Its glyph is a StrPrim at the same place.</summary>
    internal struct StrTagSpec
    {
        public StrItemId Id;
        public StrTagKind Kind;
        /// <summary>Anchor in global metres.</summary>
        public float X, Z;
        /// <summary>Plate text and second line ("P1 NORTH RIDGE", "75% ▲ RISING"); the model's words.</summary>
        public string Text, Sub;
        public StrInkRole Ink;
        /// <summary>The lane's own words ("75% ▲ RISING") for a tag whose Sub names the place: the effort's. "" otherwise.</summary>
        public string Note;
        /// <summary>Hostile share and trend for the plate's lane bar (NaN = hatched or none).</summary>
        public float Value, Trend;
        /// <summary>A ghost (proposal while an operation runs), hovered, selected.</summary>
        public bool Ghost, Hot, Selected;
    }

    /// <summary>One legend swatch: the meaning, the shape and the ink.</summary>
    internal struct StrLegendEntry { public string Label; public StrShape Shape; public StrInkRole Ink; }

    /// <summary>
    /// What to draw on the map for a model: world-space shapes in draw order, tag specs and the legend, built in place. It reads the
    /// model only. An item with a non-finite position adds nothing (never a shape at the map origin). A layer owns the items of its
    /// kinds (LayerOf); the selection ring belongs to its item's layer. A rebuild of an unchanged model allocates nothing: the words
    /// that join two model strings are joined once and remembered per item.
    /// </summary>
    internal sealed class StrOverlayPlan
    {
        public const int MaxPrims = 48, MaxTags = 24, MaxLegend = 10;

        public readonly StrPrim[] Prims = new StrPrim[MaxPrims];
        public int PrimCount;
        public readonly StrTagSpec[] Tags = new StrTagSpec[MaxTags];
        public int TagCount;
        public readonly StrLegendEntry[] Legend = new StrLegendEntry[MaxLegend];
        public int LegendCount;

        // Worst-case quads per shape (rings 48 segments, dashed rings 24 dashes, dashed lines 40 dashes, a head is one quad), summed as shapes are added.
        private int inkQuads, pulseQuads;

        // Joined words, remembered per item so that toggling a layer or adding an item moves nobody else's slot.
        private const int EffortSlot = 0, FrontSlots = 1, ProposalSlots = 9, PostSlots = 17, EffortNote = MaxTags * 2 - 2;
        private readonly string[] joinA = new string[MaxTags * 2], joinB = new string[MaxTags * 2], joinSep = new string[MaxTags * 2], joinOut = new string[MaxTags * 2];

        private static readonly string[] LegendLabels =
        {
            "FRONT", "OPERATION AXIS", "OBJECTIVE", "ASSIGNED GROUP", "WAVE ROUTE", "PROPOSAL",
            "SORTIE OFFERED", "SORTIE ACCEPTED", "STRIKE TARGET", "NOTCH = HOSTILE SHARE",
        };
        private static readonly StrShape[] LegendShapes =
        {
            StrShape.Cross, StrShape.DashedArrow, StrShape.DashedRing, StrShape.Box, StrShape.DashedLine, StrShape.DashedRing,
            StrShape.Diamond, StrShape.DiamondSolid, StrShape.Hex, StrShape.Notch,
        };
        private const int NotchEntry = 9;
        private readonly bool[] legendSeen = new bool[MaxLegend];
        private readonly StrInkRole[] legendInk = new StrInkRole[MaxLegend];

        /// <summary>The layer a kind of item belongs to: fronts to FRONTS, the effort and the proposals to OPERATION, sorties and posts to SORTIES.</summary>
        public static StrLayers LayerOf(StrItemKind kind)
        {
            switch (kind)
            {
                case StrItemKind.Front: return StrLayers.Fronts;
                case StrItemKind.Effort:
                case StrItemKind.Proposal: return StrLayers.Operation;
                case StrItemKind.Sortie:
                case StrItemKind.Post: return StrLayers.Sorties;
                default: return StrLayers.None;
            }
        }

        /// <summary>Fills the plan. An item with a non-finite position adds nothing (never a shape at the map origin).</summary>
        public static void Build(StrOverlayPlan into, StrViewModel vm, StrLayers layers)
        {
            if (into == null) return;
            into.PrimCount = into.TagCount = into.LegendCount = 0;
            into.inkQuads = into.pulseQuads = 0;
            if (vm == null || layers == StrLayers.None) return;
            into.Fill(vm, layers);
        }

        /// <summary>Worst-case quads of the ink mesh: the shapes that do not pulse, plus one leader line per tag.</summary>
        public int EstimateInkQuads() => inkQuads + TagCount;

        /// <summary>Quads of the pulse mesh (the marching axis).</summary>
        public int EstimatePulseQuads() => pulseQuads;

        // ---- fill -------------------------------------------------------------------------------------------------------------

        private void Fill(StrViewModel vm, StrLayers layers)
        {
            bool fronts = (layers & StrLayers.Fronts) != 0, op = (layers & StrLayers.Operation) != 0, sorties = (layers & StrLayers.Sorties) != 0;
            StrEffortView e = vm.Effort;
            bool ghost = e != null && e.Live;
            bool live = ghost && Finite(e.X, e.Z);

            // The effort plate is first so that first-fit placement never displaces it.
            if (op && live)
                AddTag(StrTagKind.Effort, e.Id, e.X, e.Z, Join(EffortSlot * 2, e.Tag, " ", e.KindText), Join(EffortSlot * 2 + 1, e.FrontTag, " ", e.Name),
                       StrInkRole.Effort, Unit(e.Pressure), Unit(e.Trend), false, vm, Join(EffortNote, e.PressureText, " ", e.TrendText));

            // 1. a cross at each front anchor
            if (fronts)
            {
                int n = Limit(vm.FrontCount, vm.Fronts);
                for (int i = 0; i < n; i++)
                {
                    StrFrontRow r = vm.Fronts[i];
                    if (r == null || !Finite(r.X, r.Z)) continue;
                    AddPrim(StrShape.Cross, r.Ink, r.Id, r.X, r.Z, 0f, 0f, 0f, false);
                    AddTag(StrTagKind.Front, r.Id, r.X, r.Z, Join((FrontSlots + i) * 2, r.Tag, " ", r.Name),
                           r.Observed ? Join((FrontSlots + i) * 2 + 1, r.PressureText, " ", r.TrendText) : r.StatusText,
                           r.Ink, Unit(r.Pressure), Unit(r.Trend), false, vm);
                }
            }

            // 2-5. the operation: objective ring, axis, groups, wave route
            if (op && live)
            {
                if (Radius(e.Radius) > 0f) AddPrim(StrShape.DashedRing, StrInkRole.Effort, e.Id, e.X, e.Z, 0f, 0f, e.Radius, false);
                if (AxisStart(e, out float sx, out float sz)) AddPrim(StrShape.DashedArrow, StrInkRole.Effort, e.Id, sx, sz, e.X, e.Z, 0f, e.Advancing);
                int marks = e.Marks == null ? 0 : Math.Min(e.MarkCount, e.Marks.Length);
                for (int k = 0; k < marks; k++)
                    if (Finite(e.Marks[k].X, e.Marks[k].Z)) AddPrim(StrShape.Box, StrInkRole.Effort, e.Id, e.Marks[k].X, e.Marks[k].Z, 0f, 0f, 0f, false);
                StrWaveView w = vm.Wave;
                if (w != null && w.Present && w.DepotKnown && Finite(w.DepotX, w.DepotZ))
                    AddPrim(StrShape.DashedLine, StrInkRole.Wave, e.Id, w.DepotX, w.DepotZ, e.X, e.Z, 0f, false);
            }

            // 6. the proposals: ghosts while an operation runs, the hovered or selected one solid
            if (op)
            {
                int n = Limit(vm.ProposalCount, vm.Proposals);
                for (int i = 0; i < n; i++)
                {
                    StrProposalRow p = vm.Proposals[i];
                    if (p == null || !Finite(p.X, p.Z)) continue;
                    bool hot = Is(vm.HoverId, p.Id), chosen = Is(vm.SelectedId, p.Id);
                    StrInkRole ink = ghost && !hot && !chosen ? StrInkRole.Muted : StrInkRole.Effort;
                    if (Radius(p.Radius) > 0f) AddPrim(hot || chosen ? StrShape.Ring : StrShape.DashedRing, ink, p.Id, p.X, p.Z, 0f, 0f, p.Radius, false);
                    AddTag(StrTagKind.Proposal, p.Id, p.X, p.Z, Join((ProposalSlots + i) * 2, p.Tag, " ", p.KindText), p.Name, ink, float.NaN, float.NaN, ghost, vm);
                }
            }

            if (sorties)
            {
                // 7. the jobs: a hollow diamond and a dashed zone while offered, a solid diamond and a solid zone once accepted
                int n = Limit(vm.SortieCount, vm.Sorties);
                for (int i = 0; i < n; i++)
                {
                    StrSortieRow s = vm.Sorties[i];
                    if (s == null || !s.SiteKnown || !Finite(s.X, s.Z) || (!s.Offered && !s.Active)) continue;
                    StrInkRole ink = s.Offered ? StrInkRole.Offered : StrInkRole.Effort;
                    AddPrim(s.Offered ? StrShape.Diamond : StrShape.DiamondSolid, ink, s.Id, s.X, s.Z, 0f, 0f, 0f, false);
                    if (Radius(s.Radius) > 0f) AddPrim(s.Offered ? StrShape.DashedRing : StrShape.Ring, ink, s.Id, s.X, s.Z, 0f, 0f, s.Radius, false);
                    AddTag(StrTagKind.Sortie, s.Id, s.X, s.Z, s.Tag, s.Title, ink, float.NaN, float.NaN, false, vm);
                }

                // 8. a hex at each confirmed enemy post
                n = Limit(vm.PostCount, vm.Posts);
                for (int i = 0; i < n; i++)
                {
                    StrPostRow p = vm.Posts[i];
                    if (p == null || !Finite(p.X, p.Z)) continue;
                    AddPrim(StrShape.Hex, StrInkRole.Strike, p.Id, p.X, p.Z, 0f, 0f, 0f, false);
                    StrSortieRow ticket = p.Ticket.IsNone || vm.Sorties == null ? null : StrGate.Sortie(vm, p.Ticket);
                    int slot = (PostSlots + i) * 2;
                    AddTag(StrTagKind.Post, p.Id, p.X, p.Z,
                           ticket != null ? Join(slot, ticket.Tag, " ", "STRIKE") : p.Name,
                           ticket != null ? Join(slot + 1, p.Name, StrWords.Dot, p.BountyText) : p.BountyText,
                           StrInkRole.Strike, float.NaN, float.NaN, false, vm);
                }
            }

            // 9. one highlight ring at the selected item, in its own layer
            StrItemId sel = vm.SelectedId;
            if (!sel.IsNone && (layers & LayerOf(sel.Kind)) != 0 && Resolve(vm, sel, out float hx, out float hz, out float hr))
                AddPrim(StrShape.Ring, StrInkRole.Effort, sel, hx, hz, 0f, 0f, hr, false, true);

            BuildLegend();
        }

        // The nearest own group outside the objective ring; else the operation's origin; else no axis. A start on the fix has no length.
        private static bool AxisStart(StrEffortView e, out float sx, out float sz)
        {
            sx = sz = 0f;
            float best = float.MaxValue, r = Radius(e.Radius);
            bool found = false;
            int marks = e.Marks == null ? 0 : Math.Min(e.MarkCount, e.Marks.Length);
            for (int k = 0; k < marks; k++)
            {
                if (!Finite(e.Marks[k].X, e.Marks[k].Z)) continue;
                float dx = e.Marks[k].X - e.X, dz = e.Marks[k].Z - e.Z, d2 = dx * dx + dz * dz;
                if (!(d2 > r * r) || !(d2 < best)) continue;
                best = d2; sx = e.Marks[k].X; sz = e.Marks[k].Z; found = true;
            }
            if (found) return true;
            if (!Finite(e.OriginX, e.OriginZ)) return false;
            float ox = e.OriginX - e.X, oz = e.OriginZ - e.Z;
            if (!(ox * ox + oz * oz >= 1f)) return false;
            sx = e.OriginX; sz = e.OriginZ;
            return true;
        }

        private static bool Resolve(StrViewModel vm, StrItemId id, out float x, out float z, out float radius)
        {
            x = z = radius = 0f;
            switch (id.Kind)
            {
                case StrItemKind.Effort:
                {
                    StrEffortView e = vm.Effort;
                    if (e == null || !e.Live || e.Id != id || !Finite(e.X, e.Z)) return false;
                    x = e.X; z = e.Z; radius = Radius(e.Radius);
                    return true;
                }
                case StrItemKind.Proposal:
                {
                    StrProposalRow p = vm.Proposals == null ? null : StrGate.Proposal(vm, id);
                    if (p == null || !Finite(p.X, p.Z)) return false;
                    x = p.X; z = p.Z; radius = Radius(p.Radius);
                    return true;
                }
                case StrItemKind.Front:
                    for (int i = 0; vm.Fronts != null && i < Limit(vm.FrontCount, vm.Fronts); i++)
                        if (vm.Fronts[i] != null && vm.Fronts[i].Id == id) { x = vm.Fronts[i].X; z = vm.Fronts[i].Z; return Finite(x, z); }
                    return false;
                case StrItemKind.Sortie:
                {
                    StrSortieRow s = vm.Sorties == null ? null : StrGate.Sortie(vm, id);
                    if (s == null || !s.SiteKnown || !Finite(s.X, s.Z) || (!s.Offered && !s.Active)) return false;
                    x = s.X; z = s.Z; radius = Radius(s.Radius);
                    return true;
                }
                case StrItemKind.Post:
                    for (int i = 0; vm.Posts != null && i < Limit(vm.PostCount, vm.Posts); i++)
                        if (vm.Posts[i] != null && vm.Posts[i].Id == id) { x = vm.Posts[i].X; z = vm.Posts[i].Z; return Finite(x, z); }
                    return false;
                default:
                    return false;
            }
        }

        // ---- adding -----------------------------------------------------------------------------------------------------------

        private void AddPrim(StrShape shape, StrInkRole ink, StrItemId id, float x, float z, float x2, float z2, float radius, bool pulse, bool selected = false)
        {
            if (PrimCount >= MaxPrims) return;
            if ((shape == StrShape.DashedLine || shape == StrShape.DashedArrow || shape == StrShape.Arrow) && x == x2 && z == z2) return;
            Prims[PrimCount++] = new StrPrim { Shape = shape, Ink = ink, Id = id, X = x, Z = z, X2 = x2, Z2 = z2, RadiusMetres = radius, Pulse = pulse, Selected = selected };
            if (pulse) pulseQuads += Quads(shape); else inkQuads += Quads(shape);
        }

        private void AddTag(StrTagKind kind, StrItemId id, float x, float z, string text, string sub, StrInkRole ink, float value, float trend, bool ghost, StrViewModel vm, string note = "")
        {
            if (TagCount >= MaxTags) return;
            Tags[TagCount++] = new StrTagSpec
            {
                Id = id, Kind = kind, X = x, Z = z, Text = text ?? "", Sub = sub ?? "", Note = note ?? "", Ink = ink, Value = value, Trend = trend,
                Ghost = ghost, Hot = Is(vm.HoverId, id), Selected = Is(vm.SelectedId, id),
            };
        }

        // "a" + sep + "b" once per change: the joined string is remembered against the three references it was made from.
        private string Join(int slot, string a, string sep, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            if (slot >= joinOut.Length) return a + sep + b;
            if (ReferenceEquals(a, joinA[slot]) && ReferenceEquals(b, joinB[slot]) && ReferenceEquals(sep, joinSep[slot])) return joinOut[slot];
            joinA[slot] = a; joinB[slot] = b; joinSep[slot] = sep;
            return joinOut[slot] = a + sep + b;
        }

        // ---- legend -----------------------------------------------------------------------------------------------------------

        private void BuildLegend()
        {
            Array.Clear(legendSeen, 0, legendSeen.Length);
            for (int i = 0; i < PrimCount; i++)
            {
                int at = LegendSlot(in Prims[i]);
                if (at < 0 || legendSeen[at]) continue;
                legendSeen[at] = true; legendInk[at] = Prims[i].Ink;
            }
            for (int i = 0; i < TagCount && !legendSeen[NotchEntry]; i++)
                if (Finite(Tags[i].Value)) { legendSeen[NotchEntry] = true; legendInk[NotchEntry] = StrInkRole.Hostile; }
            for (int i = 0; i < MaxLegend; i++)
                if (legendSeen[i]) Legend[LegendCount++] = new StrLegendEntry { Label = LegendLabels[i], Shape = LegendShapes[i], Ink = legendInk[i] };
        }

        private static int LegendSlot(in StrPrim p)
        {
            if (p.Selected) return -1;
            switch (p.Shape)
            {
                case StrShape.Cross: return 0;
                case StrShape.Arrow:
                case StrShape.DashedArrow: return 1;
                case StrShape.DashedRing:
                case StrShape.Ring: return p.Id.Kind == StrItemKind.Effort ? 2 : p.Id.Kind == StrItemKind.Proposal ? 5 : -1;
                case StrShape.Box: return 3;
                case StrShape.DashedLine: return 4;
                case StrShape.Diamond: return 6;
                case StrShape.DiamondSolid: return 7;
                case StrShape.Hex: return 8;
                default: return -1;
            }
        }

        // ---- small helpers ----------------------------------------------------------------------------------------------------

        private static int Quads(StrShape shape)
        {
            switch (shape)
            {
                case StrShape.Ring: return 48;
                case StrShape.DashedRing: return 24;
                case StrShape.Arrow: return 2;
                case StrShape.DashedArrow: return 41;
                case StrShape.DashedLine: return 40;
                case StrShape.Diamond: return 4;
                case StrShape.DiamondSolid: return 2;
                case StrShape.Hex: return 6;
                case StrShape.Box: return 4;
                case StrShape.Cross: return 2;
                default: return 0;
            }
        }

        private static int Limit(int count, Array rows) => rows == null ? 0 : Math.Max(0, Math.Min(count, rows.Length));

        private static bool Is(StrItemId held, StrItemId id) => !id.IsNone && held == id;

        private static bool Finite(float x, float z) => Finite(x) && Finite(z);

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Unit(float v) => Finite(v) ? v : float.NaN;

        private static float Radius(float r) => Finite(r) && r > 0f ? r : 0f;
    }
}
