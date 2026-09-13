using System;

namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Flat custom-pilot record mirrored from Wing Command's public companion API.
    /// Field order is fixed by <c>WingSquad.GetCustomPilot</c>/<c>SaveCustomPilot</c>.
    /// </summary>
    internal struct WingPilotRecord
    {
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

        public static bool TryParse(object[] values, out WingPilotRecord record)
        {
            record = default;
            if (values == null || values.Length < 15) return false;
            try
            {
                record = new WingPilotRecord
                {
                    Name = values[0] as string ?? string.Empty,
                    Callsign = values[1] as string ?? string.Empty,
                    DialogueTag = values[2] as string ?? string.Empty,
                    Persona = Convert.ToInt32(values[3]),
                    Background = values[4] as string ?? string.Empty,
                    Xp = Convert.ToInt32(values[5]),
                    Kills = Convert.ToInt32(values[6]),
                    Sorties = Convert.ToInt32(values[7]),
                    HasPortrait = Convert.ToBoolean(values[8]),
                    Body = Convert.ToInt32(values[9]),
                    Face = Convert.ToInt32(values[10]),
                    Hair = Convert.ToInt32(values[11]),
                    Uniform = Convert.ToInt32(values[12]),
                    Accessory = Convert.ToInt32(values[13]),
                    Backdrop = Convert.ToInt32(values[14]),
                };
                return !string.IsNullOrEmpty(record.Callsign);
            }
            catch (Exception)
            {
                record = default;
                return false;
            }
        }

        public object[] ToValues() => new object[]
        {
            Name ?? string.Empty,
            Callsign ?? string.Empty,
            DialogueTag ?? string.Empty,
            Persona,
            Background ?? string.Empty,
            Xp,
            Kills,
            Sorties,
            HasPortrait,
            Body,
            Face,
            Hair,
            Uniform,
            Accessory,
            Backdrop,
        };
    }
}
