#if !PUBLIC_RELEASE
using System.Collections.Generic;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>Client-local sim hooks for the station talk breaks.</summary>
    public static class RadioAutomation
    {
        /// <summary>station selects a preset; advance ends the item; action=state is read-only, action=stop stops music.
        /// receive, military_volume and music_volume control the independent local receivers. faction is retained for compatibility.
        /// Default action starts the station if stopped. No live API calls or network messages are involved.</summary>
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            Dictionary<string, object> state = RadioManager.AutomationStep(
                Has(args, "station") ? Text(args, "station") : null,
                Has(args, "faction") ? Text(args, "faction") : null,
                Has(args, "advance") && Bool(args, "advance"),
                Text(args, "action"),
                Has(args, "receive") ? (bool?)Bool(args, "receive") : null,
                Has(args, "military_volume") ? (float?)Number(args, "military_volume", .9f) : null,
                Has(args, "music_volume") ? (float?)Number(args, "music_volume", .8f) : null);
            return state ?? Failure("RadioAutomation", "Step", "no radio in this scene");
        }
    }
}
#endif
