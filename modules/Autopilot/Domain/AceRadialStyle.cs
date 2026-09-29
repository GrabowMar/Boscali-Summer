namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>
    /// Glyph drawn inside an option's disc (ACE3 draws an icon per action). The presentation
    /// owns the strokes; the catalog only names which one.
    /// </summary>
    internal enum AceIcon
    {
        None,
        Aircraft,
        Flight,
        Autopilot,
        Assist,
        Hover,
        Gear,
        Engine,
        Eject,
        Lights,
        Beam,
        NightVision,
        Weapons,
        Next,
        Previous,
        Station,
        Link,
        Turret,
        Defence,
        Flare,
        Cycle,
        Radar,
        View,
        Cockpit,
        Orbit,
        Map,
        Mark,
        Hud,
        Camera,
        Support,
        Strike,
        Clear,
        Radio,
        Play,
        Power,
        Seek,
        Stop,
        Comms,
        Call,
        Preset,
        Confirm,
    }

    /// <summary>How the state line under a label reads: plain, engaged, needs a look, or dangerous.</summary>
    internal enum AceTone
    {
        Normal,
        Active,
        Caution,
        Danger,
    }

    /// <summary>The short live state printed under an option's label (ON, DOWN, FLARE 24).</summary>
    internal readonly struct AceRadialStatus
    {
        public AceRadialStatus(string text, AceTone tone = AceTone.Normal)
        {
            Text = text;
            Tone = tone;
        }

        public string Text { get; }
        public AceTone Tone { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Text);

        public static AceRadialStatus None => default(AceRadialStatus);

        public static AceRadialStatus OnOff(bool on) =>
            on ? new AceRadialStatus("ON", AceTone.Active) : new AceRadialStatus("OFF");

        public static implicit operator AceRadialStatus(string text) => new AceRadialStatus(text);
    }
}
