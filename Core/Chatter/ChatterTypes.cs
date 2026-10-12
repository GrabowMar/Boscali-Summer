namespace BoscaliSummer.Core.Chatter
{
    /// <summary>Who is talking: each net has its own look on the board and its own share of the settings.</summary>
    internal enum ChatterNet : byte { Wing, Awacs, Tower, Command, Enemy }

    /// <summary>How badly a line needs the channel. A Tactical line cuts an Ambient or Status line that is on air; an Emergency cuts anything but another Emergency.</summary>
    internal enum ChatterPriority : byte { Ambient, Status, Tactical, Emergency }

    /// <summary>The pilot's radio setting: Essential drops Ambient lines and exchanges.</summary>
    internal enum ChatterLevel : byte { Off, Essential, Full }

    /// <summary>
    /// One voice on the radio. <see cref="Persona"/> and <see cref="Character"/> (both lower case, either may be null) choose its lines in the packs: a line written for the
    /// character wins over one for the persona, which wins over one for anybody.
    /// </summary>
    internal sealed class ChatterSpeaker
    {
        public readonly string Id;
        public readonly ChatterNet Net;
        public string Callsign, Persona, Character;
        /// <summary>Asked the moment a line would go on air: false and the line is skipped (null: always able to talk).</summary>
        public System.Func<bool> CanTalk;

        public ChatterSpeaker(string id, ChatterNet net, string callsign, string persona = null, string character = null)
        {
            Id = id;
            Net = net;
            Callsign = callsign;
            Persona = persona;
            Character = character;
        }
    }

    /// <summary>A line that went on air: what every output (the board, the log, the audio) is handed.</summary>
    internal struct ChatterAired
    {
        /// <summary>The clock the director was ticked with.</summary>
        public float Time;
        public ChatterNet Net;
        public ChatterPriority Priority;
        public string Speaker, SpeakerId, Event, LineId, Text;
        /// <summary>How long the channel is held for it.</summary>
        public float Seconds;
        /// <summary>It took the channel from a line that was still on air.</summary>
        public bool Cut;
    }

    /// <summary>The lines that went on air, oldest first: a fixed ring that overwrites the oldest when full.</summary>
    internal sealed class ChatterLog
    {
        public const int Capacity = 128;
        private readonly ChatterAired[] lines = new ChatterAired[Capacity];
        private int next;

        public int Count { get; private set; }
        /// <summary>Lines ever pushed: moves whenever the log changed, also once it is full.</summary>
        public long Total { get; private set; }

        public ChatterAired this[int i] => lines[(next - Count + i + Capacity) % Capacity];

        public void Push(in ChatterAired line)
        {
            lines[next] = line;
            next = (next + 1) % Capacity;
            if (Count < Capacity) Count++;
            Total++;
        }

        public float TimeAt(int i) => this[i].Time;

        /// <summary>"CALLSIGN: words", as a one-column log prints it.</summary>
        public string TextAt(int i)
        {
            ChatterAired line = this[i];
            return string.IsNullOrEmpty(line.Speaker) ? line.Text : line.Speaker + ": " + line.Text;
        }

        public void Clear()
        {
            Count = 0;
            next = 0;
            Total = 0;
        }
    }
}
