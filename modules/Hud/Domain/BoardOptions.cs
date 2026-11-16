using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// What the TAC BOARD may show once the declutter level is applied on top of the switches (<see cref="BoardOptions.Effective"/>). A level only ever takes things away: a
    /// block whose switch is off stays off at every level. The restriction flags (<see cref="DarkLamps"/> false, <see cref="ThreatLampsOnly"/>, the two SYSTEMS strip ones) say HOW a
    /// block that is on is drawn; they hold whether or not the block's own switch is on.
    /// </summary>
    internal struct BoardEffective
    {
        /// <summary>The pages that may appear, one <see cref="CuePriority.Bit"/> each: empty with the scope off.</summary>
        public ushort Pages;
        /// <summary>All registered pages may post notices or call for a dispenser, independently of the visible slots.</summary>
        public ushort Voiced;
        public bool ShowStores;
        public bool ShowLamps;
        /// <summary>Unlit lamps are drawn (as dim outlines). False: only a lamp that is lit shows.</summary>
        public bool DarkLamps;
        /// <summary>Of the lamps only the threat ones: LOCK, SPKE and MSL.</summary>
        public bool ThreatLampsOnly;
        /// <summary>SYSTEMS notices speak only in their critical form (Minimal). The block itself shows by <see cref="ShowSystems"/>, at every declutter level.</summary>
        public bool SystemsCriticalOnly;
        public bool ShowSystems;
        public bool ShowGauges;
        public bool ShowTicker;
        /// <summary>Decorative captions and shelves are drawn (the painters' <c>ScopeCanvas.Furniture</c> flag; brackets, ticks and markers stay at every level).</summary>
        public bool Furniture;

        /// <summary>A page may post a notice of <paramref name="tone"/> to the ticker: SYSTEMS at Minimal only its warnings (a fire, an engine out, BINGO), its cautions stay quiet.</summary>
        public readonly bool Posts(ScopePage page, HudTone tone) => page != ScopePage.Systems || !SystemsCriticalOnly || tone == HudTone.Warning;
    }

    /// <summary>
    /// The pilot's TAC BOARD options as one plain value, pure (no Unity, no BepInEx: it compiles into the unit tests). <c>HudSettings.Snapshot</c> fills one per 10 Hz tick from the
    /// config entries; <see cref="Sanitize"/> keeps whatever a hand-edited file or a settings page wrote inside the ranges; <see cref="Effective"/> applies the declutter rules.
    /// The ranges and defaults here are the config entries' (single source).
    /// </summary>
    internal struct BoardOptions
    {
        public const float ScaleMin = 0.6f, ScaleMax = 1.4f;

        /// <summary>Master switch. Off hides the board and gives back the native weapon, countermeasure and power panels.</summary>
        public bool Enabled;
        public BoardLayout Layout;
        public float Scale, Shade;
        public BoardDeclutter Declutter;
        public BoardScopeMode ScopeMode;
        /// <summary>Page slots: two at most, one column (Page3 and Page4 stay in old configs but are never shown).</summary>
        public const int MaxPages = ScopeLayout.MaxSlots;
        public int PageCount;
        /// <summary>The slot choices. Never <see cref="BoardPage.Systems"/>: SYSTEMS is the board's own always-on block, not a page choice (<see cref="Sanitize"/>).</summary>
        public BoardPage Page1, Page2, Page3, Page4;
        public bool ShowStores;
        public bool PageMissileCam;
        public bool ShowLamps, ShowGauges, ShowTicker, ShowSystems;
        public bool Sway, Corners;
        /// <summary>The centre combat layer (2026-10-28): the WARNING banner, the THREAT RING round the reticle and the shot callouts. Independent of <see cref="Enabled"/>.</summary>
        public bool CentreAlerts;
        /// <summary>The visor's brow rail and frame furniture (2026-11-16). Independent of <see cref="Enabled"/>.</summary>
        public bool Brow;
        /// <summary>The two segmented fuel and throttle arcs round the reticle in place of the game's plain ones (2026-11-16). Cockpit view only. Independent of <see cref="Enabled"/>.</summary>
        public bool Arcs;
        /// <summary>Width of the thin dark edge round every glyph of the mod's HUD text, 0 (off) to <see cref="TextEdgeMax"/> (TMP outline units).</summary>
        public float TextEdge;
        public const float TextEdgeMax = 0.25f;

        /// <summary>What a pilot gets who has touched nothing. (A zeroed struct is NOT this: it has the board off.)</summary>
        public static BoardOptions Default => new BoardOptions
        {
            Enabled = true, Layout = BoardLayout.Wide, Scale = 0.85f, Shade = 0f,
            Declutter = BoardDeclutter.Full, ScopeMode = BoardScopeMode.Auto,
            PageCount = 1, Page1 = BoardPage.Threat, Page2 = BoardPage.Shot, Page3 = BoardPage.Approach, Page4 = BoardPage.Strike, ShowStores = true,
            PageMissileCam = true,
            ShowLamps = true, ShowGauges = true, ShowTicker = true, ShowSystems = true,
            Sway = true, Corners = true, CentreAlerts = true, Brow = true, Arcs = true, TextEdge = 0.16f,
        };

        /// <summary>Clamp every range and replace NaN (and an enum that names nothing) with the default.</summary>
        public void Sanitize()
        {
            BoardOptions d = Default;
            Scale = Fit(Scale, ScaleMin, ScaleMax, d.Scale);
            Shade = Fit(Shade, 0f, 1f, d.Shade);
            TextEdge = Fit(TextEdge, 0f, TextEdgeMax, d.TextEdge);
            if ((uint)Layout > (uint)BoardLayout.Stack) Layout = d.Layout;
            if ((uint)Declutter > (uint)BoardDeclutter.Minimal) Declutter = d.Declutter;
            if ((uint)ScopeMode > (uint)BoardScopeMode.Auto) ScopeMode = d.ScopeMode;
            PageCount = System.Math.Clamp(PageCount, 1, MaxPages);
            // Every slot names a real page that is not SYSTEMS (an old config may hold it), and no page twice: a hand-edited duplicate must not paint the same stateful page
            // twice in one frame, and a hidden slot must not turn into one when the count goes up.
            Page1 = Fix(Page1, d.Page1, 0);
            // A removed page (DOCK, CARGO, OBJECTIVE, MISSILECAM: they have their own homes) in slot 2 becomes WING, or SHOT when slot 1 already holds WING.
            BoardPage second = Choosable(Page2) ? d.Page2 : Earlier(BoardPage.Wing, 1) ? BoardPage.Shot : BoardPage.Wing;
            Page2 = Fix(Page2, second, 1);
            Page3 = Fix(Page3, d.Page3, 2);
            Page4 = Fix(Page4, d.Page4, 3);
        }

        private readonly BoardPage SlotChoice(int slot) => slot == 0 ? Page1 : slot == 1 ? Page2 : slot == 2 ? Page3 : Page4;

        /// <summary>Slot <paramref name="slot"/>'s page kept if it is valid and not in an earlier slot, else <paramref name="fallback"/> if that is free, else the first free page.</summary>
        private readonly BoardPage Fix(BoardPage page, BoardPage fallback, int slot)
        {
            if (Choosable(page) && !Earlier(page, slot)) return page;
            if (Choosable(fallback) && !Earlier(fallback, slot)) return fallback;
            for (BoardPage candidate = BoardPage.Threat; candidate <= BoardPage.Cargo; candidate++)
                if (Choosable(candidate) && !Earlier(candidate, slot)) return candidate;
            return fallback;
        }

        private readonly bool Earlier(BoardPage page, int slot)
        {
            for (int i = 0; i < slot; i++) if (SlotChoice(i) == page) return true;
            return false;
        }

        /// <summary>A page a slot may hold: THREAT, SHOT, APPROACH, WING or STRIKE. SYSTEMS is the board's own block; DOCK, CARGO and OBJECTIVE live in the left column's ROUTE slot, MISSILECAM in the TARGET CAM card.</summary>
        public static bool Choosable(BoardPage page) =>
            page == BoardPage.Threat || page == BoardPage.Shot || page == BoardPage.Approach || page == BoardPage.Wing || page == BoardPage.Strike;

        /// <summary>A persisted slot choice, independent of feed activity, declutter and retired page switches.</summary>
        public readonly ScopePage PageAt(int slot) => (ScopePage)SlotChoice(slot);

        public readonly ushort SelectedPages()
        {
            ushort mask = 0;
            for (int i = 0; i < PageCount; i++) mask |= CuePriority.Bit(PageAt(i));
            return mask;
        }

        private static float Fit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : value < min ? min : value > max ? max : value;

        /// <summary>
        /// The major sections with declutter applied to their detail. Manual page choices are always respected;
        /// Off hides the tactical pages while every feed keeps reporting alerts.
        /// </summary>
        public readonly BoardEffective Effective()
        {
            bool full = Declutter == BoardDeclutter.Full, minimal = Declutter == BoardDeclutter.Minimal;
            return new BoardEffective
            {
                Pages = ScopeMode == BoardScopeMode.Off ? (ushort)0 : SelectedPages(),
                Voiced = (ushort)((1 << CuePriority.PageCount) - 1),
                ShowStores = ShowStores,
                ShowLamps = ShowLamps,
                DarkLamps = full,
                ThreatLampsOnly = minimal,
                SystemsCriticalOnly = minimal,
                ShowGauges = ShowGauges && full,
                ShowTicker = ShowTicker,
                ShowSystems = ShowSystems,
                Furniture = full,
            };
        }
    }
}
