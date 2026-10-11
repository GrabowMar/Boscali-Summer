namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Where the TAC BOARD sits: spread along the top edge, or the classic narrow column.</summary>
    internal enum BoardLayout { Wide, Stack }

    /// <summary>How much the TAC BOARD shows: everything the switches allow, a leaner board, or only what a pilot in a fight needs.</summary>
    internal enum BoardDeclutter { Full, Lean, Minimal }

    /// <summary>Whether the scope's information pages appear when they are relevant, or never.</summary>
    internal enum BoardScopeMode { Auto, Off }

    /// <summary>
    /// The TAC BOARD's options for a settings surface (the SET console, the C-key menu) to read and change without importing the Hud module. Client-local; the board
    /// picks a change up on its next 10 Hz tick. Setters persist to config.
    /// </summary>
    internal interface ITacBoardSettings
    {
        /// <summary>Off hides the board and gives back the game's own weapon, countermeasure and power panels.</summary>
        bool Enabled { get; set; }
        BoardLayout Layout { get; set; }
        /// <summary>Size multiplier, 0.6 to 1.4.</summary>
        float Scale { get; set; }
        /// <summary>Units moved left from the right screen edge, 0 to 1200.</summary>
        float OffsetX { get; set; }
        /// <summary>Units moved down from the top edge, 0 to 600.</summary>
        float OffsetY { get; set; }
        /// <summary>Ink opacity of the whole board, 0.25 to 1.</summary>
        float Opacity { get; set; }
        /// <summary>Strength of the dark band behind the symbology, 0 (none) to 1.</summary>
        float Shade { get; set; }
        BoardDeclutter Declutter { get; set; }
        BoardScopeMode ScopeMode { get; set; }
        bool PageThreat { get; set; }
        bool PageShot { get; set; }
        bool PageMissileCam { get; set; }
        bool PageApproach { get; set; }
        bool PageWing { get; set; }
        bool PageStrike { get; set; }
        bool PageDock { get; set; }
        bool PageObjective { get; set; }
        bool PageSystems { get; set; }
        /// <summary>Seconds the radar-warning page keeps a contact after its last warning, 2 to 30.</summary>
        float ThreatHoldSeconds { get; set; }
        /// <summary>Minimum seconds a page stays once it has appeared, 0 to 15.</summary>
        float PageMinSeconds { get; set; }
        bool ShowLamps { get; set; }
        bool ShowSystemsStrip { get; set; }
        bool ShowGauges { get; set; }
        bool ShowTicker { get; set; }
        /// <summary>The symbology lags slightly against head movement.</summary>
        bool Sway { get; set; }
        /// <summary>Restyle the kill feed and the map frame in the board's look.</summary>
        bool Corners { get; set; }
        /// <summary>Put every option back to its default, the board's own values, so a settings surface keeps no copy of them.</summary>
        void ResetToDefaults();
    }
}
