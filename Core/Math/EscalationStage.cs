namespace BoscaliSummer.Core.Math
{
    /// <summary>
    /// The mission's escalation ladder, shared by the MIS main tab and the contract tempo.
    /// A tactical or strategic threshold above zero gates that stage; zero means the mission
    /// never gated it and can never invent one.
    /// </summary>
    internal static class EscalationStage
    {
        /// <summary>0 conventional, 1 tactical, 2 strategic.</summary>
        public static int Of(float current, float tactical, float strategic)
        {
            if (strategic > 0f) return current >= strategic ? 2 : tactical > 0f && current < tactical ? 0 : 1;
            if (tactical > 0f) return current < tactical ? 0 : 1;
            return 2;
        }
    }
}
