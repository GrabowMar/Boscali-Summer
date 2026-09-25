namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>
    /// The three OPS warfare domains, in tab order. Panel-local identity: nothing here
    /// crosses the wire, so the order may change without a protocol bump.
    /// </summary>
    internal enum OpsDomain : byte
    {
        Space = 0,
        Cyber = 1,
        SpecialOperations = 2
    }

    /// <summary>Tab labels and one-line mission statements for each domain.</summary>
    internal static class OpsDomains
    {
        public static readonly OpsDomain[] All =
        {
            OpsDomain.Space,
            OpsDomain.Cyber,
            OpsDomain.SpecialOperations
        };

        /// <summary>Short tab label. Eight characters at most so every tab fits the 480px bezel.</summary>
        public static string Tab(OpsDomain domain)
        {
            switch (domain)
            {
                case OpsDomain.Space: return "SPACE";
                case OpsDomain.Cyber: return "CYBER";
                case OpsDomain.SpecialOperations: return "SPEC OPS";
                default: return string.Empty;
            }
        }

        public static string Mission(OpsDomain domain)
        {
            switch (domain)
            {
                case OpsDomain.Space:
                    return "Responsive launch and constellation command.";
                case OpsDomain.Cyber:
                    return "Build the spectrum-defence net, hold it, trace the attackers, strike back.";
                case OpsDomain.SpecialOperations:
                    return "Send teams to real objectives; the posts they hold arm SPOT, SUPPRESS and FORTIFY.";
                default:
                    return string.Empty;
            }
        }

        public static string[] TabLabels()
        {
            var labels = new string[All.Length];
            for (int i = 0; i < All.Length; i++) labels[i] = Tab(All[i]);
            return labels;
        }
    }
}
