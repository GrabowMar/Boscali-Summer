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
        public const float OffsetXMax = 1200f, OffsetYMax = 600f;
        public const float OpacityMin = 0.25f;
        public const float HoldMin = 2f, HoldMax = 30f;

        /// <summary>Master switch. Off hides the board and gives back the native weapon, countermeasure and power panels.</summary>
        public bool Enabled;
        public BoardLayout Layout;
        public float Scale, OffsetX, OffsetY, Opacity, Shade;
        public BoardDeclutter Declutter;
        public BoardScopeMode ScopeMode;
        /// <summary>Page slots: a 2 x 2 grid at most (<see cref="ScopeLayout.Columns"/>).</summary>
        public const int MaxPages = 4;
        public int PageCount;
        /// <summary>The slot choices. Never <see cref="BoardPage.Systems"/>: SYSTEMS is the board's own always-on block, not a page choice (<see cref="Sanitize"/>).</summary>
        public BoardPage Page1, Page2, Page3, Page4;
        public bool ShowStores;
        public bool PageMissileCam;
        public float ThreatHoldSeconds;
        public bool ShowLamps, ShowGauges, ShowTicker, ShowSystems;
        public bool Sway, Corners;

        /// <summary>What a pilot gets who has touched nothing. (A zeroed struct is NOT this: it has the board off.)</summary>
        public static BoardOptions Default => new BoardOptions
        {
            Enabled = true, Layout = BoardLayout.Wide, Scale = 0.85f, OffsetX = 0f, OffsetY = 0f, Opacity = 1f, Shade = 0.45f,
            Declutter = BoardDeclutter.Full, ScopeMode = BoardScopeMode.Manual,
            PageCount = 1, Page1 = BoardPage.Threat, Page2 = BoardPage.Shot, Page3 = BoardPage.Wing, Page4 = BoardPage.Objective, ShowStores = true,
            PageMissileCam = true,
            ThreatHoldSeconds = 6f,
            ShowLamps = true, ShowGauges = true, ShowTicker = true, ShowSystems = true,
            Sway = true, Corners = true,
        };

        /// <summary>Clamp every range and replace NaN (and an enum that names nothing) with the default.</summary>
        public void Sanitize()
        {
            BoardOptions d = Default;
            Scale = Fit(Scale, ScaleMin, ScaleMax, d.Scale);
            OffsetX = Fit(OffsetX, 0f, OffsetXMax, d.OffsetX);
            OffsetY = Fit(OffsetY, 0f, OffsetYMax, d.OffsetY);
            Opacity = Fit(Opacity, OpacityMin, 1f, d.Opacity);
            Shade = Fit(Shade, 0f, 1f, d.Shade);
            ThreatHoldSeconds = Fit(ThreatHoldSeconds, HoldMin, HoldMax, d.ThreatHoldSeconds);
            if ((uint)Layout > (uint)BoardLayout.Stack) Layout = d.Layout;
            if ((uint)Declutter > (uint)BoardDeclutter.Minimal) Declutter = d.Declutter;
            if ((uint)ScopeMode > (uint)BoardScopeMode.Off) ScopeMode = d.ScopeMode;
            PageCount = System.Math.Clamp(PageCount, 1, MaxPages);
            // Every slot names a real page that is not SYSTEMS (an old config may hold it), and no page twice: a hand-edited duplicate must not paint the same stateful page
            // twice in one frame, and a hidden slot must not turn into one when the count goes up.
            Page1 = Fix(Page1, d.Page1, 0);
            Page2 = Fix(Page2, d.Page2, 1);
            Page3 = Fix(Page3, d.Page3, 2);
            Page4 = Fix(Page4, d.Page4, 3);
        }

        private readonly BoardPage SlotChoice(int slot) => slot == 0 ? Page1 : slot == 1 ? Page2 : slot == 2 ? Page3 : Page4;

        /// <summary>Slot <paramref name="slot"/>'s page kept if it is valid and not in an earlier slot, else <paramref name="fallback"/> if that is free, else the first free page.</summary>
        private readonly BoardPage Fix(BoardPage page, BoardPage fallback, int slot)
        {
            if (Choosable(page) && !Earlier(page, slot)) return page;
            if (!Earlier(fallback, slot)) return fallback;
            for (BoardPage candidate = BoardPage.Threat; candidate <= BoardPage.Cargo; candidate++)
                if (Choosable(candidate) && !Earlier(candidate, slot)) return candidate;
            return fallback;
        }

        private readonly bool Earlier(BoardPage page, int slot)
        {
            for (int i = 0; i < slot; i++) if (SlotChoice(i) == page) return true;
            return false;
        }

        /// <summary>A page a slot may hold: a registered page that is not SYSTEMS.</summary>
        public static bool Choosable(BoardPage page) => (uint)page <= (uint)BoardPage.Cargo && page != BoardPage.Systems;

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
