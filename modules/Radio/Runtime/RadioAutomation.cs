#if !PUBLIC_RELEASE
using System.Collections.Generic;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>Client-local sim hooks for the station talk breaks.</summary>
    public static class RadioAutomation
    {
        /// <summary>Args, all optional: station ("agrapol-fm" / "maris-network" / "base-broadcast"), faction (stands in for the
        /// player's side), advance (bool: end the current song or segment now). Starts the receiver if it is off.</summary>
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            Dictionary<string, object> state = RadioManager.AutomationStep(
                Has(args, "station") ? Text(args, "station") : null,
                Has(args, "faction") ? Text(args, "faction") : null,
                Has(args, "advance") && Bool(args, "advance"));
            return state ?? Failure("RadioAutomation", "Step", "no radio in this scene");
        }
    }
}
#endif
