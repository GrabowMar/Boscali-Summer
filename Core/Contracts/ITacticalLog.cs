namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The map screen's tactical log, which keeps the game's own messages. The radio writes what went on air into it.</summary>
    internal interface ITacticalLog
    {
        /// <summary>A line that went on air on the radio: logged with the game's own messages, but not offered to the news ticker.</summary>
        void Radio(string text);
    }
}
