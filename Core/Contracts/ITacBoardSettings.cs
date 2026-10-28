namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Where the TAC BOARD sits: spread along the top edge, or the classic narrow column.</summary>
    internal enum BoardLayout { Wide, Stack }

    /// <summary>How much the TAC BOARD shows: everything the switches allow, a leaner board, or only what a pilot in a fight needs.</summary>
    internal enum BoardDeclutter { Full, Lean, Minimal }

    /// <summary>Manual page display, or no tactical pages. Auto is a legacy config spelling for Manual.</summary>
    internal enum BoardScopeMode { Manual = 0, Off = 1, Auto = Manual }

    /// <summary>
    /// The information page selected for a TAC BOARD slot. Values match the HUD's page registry. <see cref="Systems"/> is not a slot choice (the board always shows SYSTEMS in a
    /// block of its own); it stays in the list so the values, and saved configs, keep their meaning.
    /// </summary>
    internal enum BoardPage { Threat, Shot, MissileCam, Approach, Wing, Strike, Dock, Objective, Systems, Cargo }

    /// <summary>
    /// The TAC BOARD's options for a settings surface (the SET console, the C-key menu) to read and change without importing the Hud module. Client-local; the board
    /// picks a change up on its next 10 Hz tick. Setters persist to config.
    /// </summary>
    internal interface ITacBoardSettings
    {
        /// <summary>Off hides the board and gives back the game's own weapon, countermeasure and power panels. The kill feed and map restyle (<see cref="Corners"/>) is a switch of its own and stays.</summary>
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
        /// <summary>Full information pages shown, 1 to 4, at most two across and two down.</summary>
        int PageCount { get; set; }
        BoardPage Page1 { get; set; }
        BoardPage Page2 { get; set; }
        BoardPage Page3 { get; set; }
        BoardPage Page4 { get; set; }
        /// <summary>The stores, countermeasure and power section. Off restores the native panels.</summary>
        bool ShowStores { get; set; }
        /// <summary>The MISSILE CAM feed picture (the missile card); the page slots do not consult it.</summary>
        bool PageMissileCam { get; set; }
        /// <summary>Seconds the radar-warning page keeps a contact after its last warning, 2 to 30.</summary>
        float ThreatHoldSeconds { get; set; }
        bool ShowLamps { get; set; }
        bool ShowGauges { get; set; }
        bool ShowTicker { get; set; }
        /// <summary>The SYSTEMS block under the lamp line. Off hides the block only; its warnings still post.</summary>
        bool ShowSystems { get; set; }
        /// <summary>The symbology lags slightly against head movement.</summary>
        bool Sway { get; set; }
        /// <summary>Restyle the kill feed and the map frame in the board's look.</summary>
        bool Corners { get; set; }
        /// <summary>Put every option back to its default, the board's own values, so a settings surface keeps no copy of them.</summary>
        void ResetToDefaults();
    }
}
