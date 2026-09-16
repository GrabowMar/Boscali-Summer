namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// What a staff log line is about. HighCommand owns the event; the console owns the
    /// rail a tone wears, so a new event kind never needs a colour decided at the source.
    /// </summary>
    internal enum CommanderLogTone : byte
    {
        Staff = 0,
        Economy = 1,
        Order = 2,
        Contact = 3,
        Loss = 4,
        Alert = 5,
    }

    /// <summary>
    /// One line of a faction's staff log: the subject post when the event has one, the
    /// tone, the server-authored text, and how long ago it happened (seconds).
    /// </summary>
    internal sealed class CommanderLogLine
    {
        public int TargetId { get; }
        public CommanderLogTone Tone { get; }
        public string Text { get; }
        public float Age { get; }

        public CommanderLogLine(int targetId, CommanderLogTone tone, string text, float age)
        {
            TargetId = targetId;
            Tone = tone;
            Text = text ?? "";
            Age = age;
        }
    }
}
