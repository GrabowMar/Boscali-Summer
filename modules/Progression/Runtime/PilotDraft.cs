namespace BoscaliSummer.Features.Progression.Runtime
{
    /// <summary>
    /// Editable custom-pilot state for the SQD studio. Engine-free: the panel owns Wing Command
    /// calls, persistence and portrait sprites. Limits match the Wing Command Pilot Studio.
    /// </summary>
    internal struct PilotDraft
    {
        public const int MaxCallsign = 14;
        public const int MaxName = 24;
        public const int MaxDialogueTag = 24;
        public const int MaxBackground = 280;
        public const int PersonaCount = 4;

        public string Name;
        public string Callsign;
        public string DialogueTag;
        public string Background;
        public int Persona;
        public int Xp;
        public int Kills;
        public int Sorties;
        public bool HasPortrait;
        public int Body;
        public int Face;
        public int Hair;
        public int Uniform;
        public int Accessory;
        public int Backdrop;

        public static PilotDraft New() => new PilotDraft
        {
            Name = "NEW PILOT",
            Callsign = string.Empty,
            DialogueTag = string.Empty,
            Background = string.Empty,
            Persona = 0,
            Xp = 0,
            Kills = 0,
            Sorties = 0,
            HasPortrait = true,
        };

        public bool IsValid => !string.IsNullOrWhiteSpace(Callsign);

        public PilotDraft CycleBody(int delta, int count)
        {
            Body = Wrap(Body + delta, count);
            return this;
        }

        public PilotDraft CycleFace(int delta, int count)
        {
            Face = Wrap(Face + delta, count);
            return this;
        }

        public PilotDraft CycleHair(int delta, int count)
        {
            Hair = Wrap(Hair + delta, count);
            return this;
        }

        public PilotDraft CycleUniform(int delta, int count)
        {
            Uniform = Wrap(Uniform + delta, count);
            return this;
        }

        public PilotDraft CycleBackdrop(int delta, int count)
        {
            Backdrop = Wrap(Backdrop + delta, count);
            return this;
        }

        public PilotDraft CyclePersona(int delta)
        {
            Persona = Wrap(Persona + delta, PersonaCount);
            return this;
        }

        /// <summary>Clamp selectors and trim/limit text before any Wing Command call.</summary>
        public void Normalize()
        {
            if (Persona < 0 || Persona >= PersonaCount) Persona = 0;
            if (Body < 0) Body = 0;
            if (Face < 0) Face = 0;
            if (Hair < 0) Hair = 0;
            if (Uniform < 0) Uniform = 0;
            if (Accessory < 0) Accessory = 0;
            if (Backdrop < 0) Backdrop = 0;
            Name = Limit(Name, MaxName);
            Callsign = Limit(Callsign, MaxCallsign);
            DialogueTag = Limit(DialogueTag, MaxDialogueTag);
            Background = Limit(Background, MaxBackground);
        }

        private static string Limit(string value, int length)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= length ? value : value.Substring(0, length);
        }

        private static int Wrap(int value, int count) =>
            count <= 0 ? 0 : ((value % count) + count) % count;
    }
}
