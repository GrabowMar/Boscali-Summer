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
        /// <summary>The pages that may speak (post ticker notices, call for a dispenser): <see cref="Pages"/> without the scope mode, because Off hides the scope, not the warnings.</summary>
        public ushort Voiced;
        public bool ShowLamps;
        /// <summary>Unlit lamps are drawn (as dim outlines). False: only a lamp that is lit shows.</summary>
        public bool DarkLamps;
        /// <summary>Of the lamps only the threat ones: LOCK, SPKE and MSL.</summary>
        public bool ThreatLampsOnly;
        /// <summary>The compact SYSTEMS strip may show: its own switch AND the SYSTEMS page's (the strip is that page in its cruise form).</summary>
        public bool ShowSystemsStrip;
        /// <summary>The compact SYSTEMS strip shows only while something is in a caution or warning state.</summary>
        public bool SystemsStripOnlyWhenNotNominal;
        /// <summary>SYSTEMS shows only in its critical form (a fire, an engine out, BINGO, lost structure).</summary>
        public bool SystemsCriticalOnly;
        public bool ShowGauges;
        public bool ShowTicker;
        /// <summary>Decorative captions (the later drawing code reads this).</summary>
        public bool Furniture;

        /// <summary>
        /// Whether the compact SYSTEMS strip, the scope's cruise form, shows right now: its switch, and under Lean only while something is not nominal (a caution or a warning), under
        /// Minimal only while that is critical (<see cref="SystemsCriticalOnly"/>). When it does not, the scope takes no room in cruise.
        /// </summary>
        public readonly bool SystemsStripShows(bool notNominal, bool critical) =>
            ShowSystemsStrip && (SystemsCriticalOnly ? critical : !SystemsStripOnlyWhenNotNominal || notNominal);
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
        public const float DwellMax = 15f;

        /// <summary>Master switch. Off hides the board and gives back the native weapon, countermeasure and power panels.</summary>
        public bool Enabled;
        public BoardLayout Layout;
        public float Scale, OffsetX, OffsetY, Opacity, Shade;
        public BoardDeclutter Declutter;
        public BoardScopeMode ScopeMode;
        public bool PageThreat, PageShot, PageMissileCam, PageApproach, PageWing, PageStrike, PageDock, PageObjective, PageSystems;
        public float ThreatHoldSeconds, PageMinSeconds;
        public bool ShowLamps, ShowSystemsStrip, ShowGauges, ShowTicker;
        public bool Sway, Corners;

        /// <summary>The pages Minimal keeps: THREAT, MISSILE CAM, SHOT, APPROACH and SYSTEMS (in its critical form).</summary>
        private static readonly ushort MinimalPages = (ushort)(CuePriority.Bit(ScopePage.Threat) | CuePriority.Bit(ScopePage.MissileCam) |
            CuePriority.Bit(ScopePage.Shot) | CuePriority.Bit(ScopePage.Approach) | CuePriority.Bit(ScopePage.Systems));

        /// <summary>What a pilot gets who has touched nothing. (A zeroed struct is NOT this: it has the board off.)</summary>
        public static BoardOptions Default => new BoardOptions
        {
            Enabled = true, Layout = BoardLayout.Wide, Scale = 1f, OffsetX = 0f, OffsetY = 0f, Opacity = 1f, Shade = 0.45f,
            Declutter = BoardDeclutter.Full, ScopeMode = BoardScopeMode.Auto,
            PageThreat = true, PageShot = true, PageMissileCam = true, PageApproach = true, PageWing = true, PageStrike = true, PageDock = true, PageObjective = true, PageSystems = true,
            ThreatHoldSeconds = 6f, PageMinSeconds = 3f,
            ShowLamps = true, ShowSystemsStrip = true, ShowGauges = true, ShowTicker = true,
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
            PageMinSeconds = Fit(PageMinSeconds, 0f, DwellMax, d.PageMinSeconds);
            if ((uint)Layout > (uint)BoardLayout.Stack) Layout = d.Layout;
            if ((uint)Declutter > (uint)BoardDeclutter.Minimal) Declutter = d.Declutter;
            if ((uint)ScopeMode > (uint)BoardScopeMode.Off) ScopeMode = d.ScopeMode;
        }

        private static float Fit(float value, float min, float max, float fallback) =>
            float.IsNaN(value) ? fallback : value < min ? min : value > max ? max : value;

        /// <summary>The pages the nine page switches allow, one <see cref="CuePriority.Bit"/> each. Says nothing about the scope mode or the declutter level.</summary>
        public readonly ushort PageMask() => (ushort)(
            (PageThreat ? CuePriority.Bit(ScopePage.Threat) : 0) | (PageShot ? CuePriority.Bit(ScopePage.Shot) : 0) |
            (PageMissileCam ? CuePriority.Bit(ScopePage.MissileCam) : 0) | (PageApproach ? CuePriority.Bit(ScopePage.Approach) : 0) |
            (PageWing ? CuePriority.Bit(ScopePage.Wing) : 0) | (PageStrike ? CuePriority.Bit(ScopePage.Strike) : 0) |
            (PageDock ? CuePriority.Bit(ScopePage.Dock) : 0) | (PageObjective ? CuePriority.Bit(ScopePage.Objective) : 0) |
            (PageSystems ? CuePriority.Bit(ScopePage.Systems) : 0));

        /// <summary>
        /// The switches with the declutter level applied. Full: everything the switches allow. Lean: no gauge row, no unlit lamps, no decorative captions, and the compact SYSTEMS
        /// strip only while something is not nominal. Minimal: as Lean, and of the lamps only LOCK / SPKE / MSL, of the pages only THREAT, MISSILE CAM, SHOT, APPROACH and the
        /// critical form of SYSTEMS. The scope mode Off leaves no pages at all (but <see cref="BoardEffective.Voiced"/> keeps the ones the switches allow).
        /// </summary>
        public readonly BoardEffective Effective()
        {
            bool full = Declutter == BoardDeclutter.Full, minimal = Declutter == BoardDeclutter.Minimal;
            ushort voiced = PageMask();
            if (minimal) voiced &= MinimalPages;
            return new BoardEffective
            {
                Pages = ScopeMode == BoardScopeMode.Off ? (ushort)0 : voiced,
                Voiced = voiced,
                ShowLamps = ShowLamps,
                DarkLamps = full,
                ThreatLampsOnly = minimal,
                ShowSystemsStrip = ShowSystemsStrip && CuePriority.Has(voiced, ScopePage.Systems),
                SystemsStripOnlyWhenNotNominal = !full,
                SystemsCriticalOnly = minimal,
                ShowGauges = ShowGauges && full,
                ShowTicker = ShowTicker,
                Furniture = full,
            };
        }
    }
}
