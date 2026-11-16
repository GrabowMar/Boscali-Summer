namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// COM's shared plot on the MAP drawer or fallback MAP TOOLS: the tool keys, ink, markers, text and cleanup actions.
    /// Comms builds its own parts onto the flows MAP hands it and keeps every verb (CommsManager, host-authoritative; who owns a
    /// mark and the net team are unchanged). Optional: with no provider MAP shows no PLOT.
    /// </summary>
    internal interface ICommsPlot
    {
        /// <summary>Comms is on (F1). While false MAP hides the parts and says so in one dim line.</summary>
        bool On { get; }

        /// <summary>Adds the PLOT parts to the end of <paramref name="page"/>, once per MAP console, and returns them so MAP can hide them.</summary>
        NOAvionics.AvPart[] Build(NOAvionics.AvFlow page);

        /// <summary>Builds the same tools across the drawer's tools, markers/ink and text flows; returns all parts for availability changes.</summary>
        NOAvionics.AvPart[] BuildDrawer(NOAvionics.AvFlow tools, NOAvionics.AvFlow markers, NOAvionics.AvFlow text);

        /// <summary>Keeps armed map input live for half a second without repainting hidden controls. Call while the map drawer is folded or on another tab.</summary>
        void KeepAlive();

        /// <summary>Latches the armed tool, ink and marker on the visible plot page, and keeps its input live.</summary>
        void Refresh();

        /// <summary>The plot's ink on the map (the MAP INK client setting): MAP's PLOT quick layer.</summary>
        bool ShowOnMap { get; set; }
    }
}
