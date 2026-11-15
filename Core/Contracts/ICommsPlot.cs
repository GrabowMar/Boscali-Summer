namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// COM's shared plot on MAP TOOLS (2026-11-14 panels fold): the tool keys, ink, markers, text and CLEAR that filled COM's old MAP
    /// tab. Comms builds its own parts onto the page MAP hands it and keeps every verb (CommsManager, host-authoritative; who owns a
    /// mark and the net team are unchanged). Optional: with no provider MAP shows no PLOT.
    /// </summary>
    internal interface ICommsPlot
    {
        /// <summary>Comms is on (F1). While false MAP hides the parts and says so in one dim line.</summary>
        bool On { get; }

        /// <summary>Adds the PLOT parts to the end of <paramref name="page"/>, once per MAP console, and returns them so MAP can hide them.</summary>
        NOAvionics.AvPart[] Build(NOAvionics.AvFlow page);

        /// <summary>Latches the armed tool, ink and marker. Asking keeps the plot tools live for half a second: they work only while a plot shows.</summary>
        void Refresh();

        /// <summary>The plot's ink on the map (the MAP INK client setting): MAP's PLOT quick layer.</summary>
        bool ShowOnMap { get; set; }
    }
}
