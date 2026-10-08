namespace BoscaliSummer.Core.Contracts
{
    internal enum ChatterUrgency : byte { Ambient, Status, Tactical, Emergency }

    /// <summary>Local presentation of already validated, locally visible facts. No world mutation or network send.</summary>
    internal interface IChatterChannel
    {
        bool Report(string speaker, string key, string text, ChatterUrgency urgency = ChatterUrgency.Status);
    }
}
