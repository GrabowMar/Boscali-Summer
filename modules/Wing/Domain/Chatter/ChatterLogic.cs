using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Wing.Domain
{
    internal enum ChatterPersona { Professional, Aggressive, Calm, Dry }

    internal readonly struct ChatterExchange
    {
        public readonly string Opening, Reply, SpeakerTag, ReplyTag;
        public readonly ChatterScene Scene;
        public ChatterExchange(string opening, string reply = null, string speakerTag = null,
            string replyTag = null, ChatterScene scene = ChatterScene.Transit)
        { Opening = opening; Reply = reply; SpeakerTag = speakerTag; ReplyTag = replyTag; Scene = scene; }
    }

    /// <summary>Authored radio dialogue. Urgent calls remain precise; personality lives in delivery and quiet moments.</summary>
    internal static class ChatterDialogue
    {
        private static readonly ChatterExchange[] ambient =
        {
            new ChatterExchange("You ever look down and forget this is home?", "Only until I recognise the roads.", null, null, ChatterScene.Transit),
            new ChatterExchange("I used to drive that coast road in summer.", "Keep the memory. We will need it later.", null, null, ChatterScene.Transit),
            new ChatterExchange("They left the lights on in the hangar for us.", "Then let us give them a reason.", null, null, ChatterScene.Transit),
            new ChatterExchange("Same coastline. Different summer.", "One sortie at a time.", null, null, ChatterScene.Transit),
            new ChatterExchange("I can hear the breathing in my mask again.", "Good. Keep it steady.", null, null, ChatterScene.Transit),
            new ChatterExchange("No speeches from me today. Just bring everyone home.", null, null, null, ChatterScene.Transit),
            new ChatterExchange("There used to be fishing boats all along this coast.", "Maybe there will be again.", null, null, ChatterScene.Transit),
            new ChatterExchange("The ground crew wrote their names inside my gear door.", "Then you had better bring it back.", null, null, ChatterScene.Transit),
            new ChatterExchange("Cobalt, you remember the first time we flew this route?", "We had fewer things to worry about.", "HATCHET", "COBALT", ChatterScene.Transit),
            new ChatterExchange("Hatchet. Save something for the flight home.", "I heard you, Cobalt.", "COBALT", "HATCHET", ChatterScene.Transit),
            new ChatterExchange("Meridian, you still there?", "Still here. Always listening.", "HATCHET", "MERIDIAN", ChatterScene.Transit),
            new ChatterExchange("Ghost, say something.", "I am here, Valkyrie. Keep your scan moving.", "VALKYRIE", "GHOST", ChatterScene.Transit),
            new ChatterExchange("Spectre. How is the ride?", "Better with company.", "COBALT", "SPECTRE", ChatterScene.Transit),
            new ChatterExchange("That got close.", "We are still flying. Start with that.", null, null, ChatterScene.AfterCombat),
            new ChatterExchange("Give me a moment. Hands are still shaking.", "Take your time. I am with you.", null, null, ChatterScene.AfterCombat),
            new ChatterExchange("I could hear every rivet on that last turn.", "Check your aircraft. We will talk on the ground.", null, null, ChatterScene.AfterCombat),
            new ChatterExchange("I will remember that pass for a while.", "Keep flying. The rest comes later.", null, null, ChatterScene.AfterCombat),
            new ChatterExchange("There is always a silence after.", null, null, null, ChatterScene.AfterCombat),
            new ChatterExchange("Keep their place open until we get home.", "Understood.", null, null, ChatterScene.Loss),
            new ChatterExchange("I keep waiting for them to answer.", "I know. Stay with us.", null, null, ChatterScene.Loss),
            new ChatterExchange("Nobody needs to say anything right now.", null, null, null, ChatterScene.Loss),
            new ChatterExchange("I am thinking about the sound of the engine shutting down.", "Best part of a long day.", null, null, ChatterScene.Recovery),
            new ChatterExchange("Save the debrief for the ground.", "Agreed. Fly the approach.", null, null, ChatterScene.Recovery),
            new ChatterExchange("Home cannot come soon enough.", null, null, null, ChatterScene.Recovery),
            new ChatterExchange("Rain on the canopy. Reminds me of the hangar roof.", "Keep your eyes on the instruments.", null, null, ChatterScene.Weather),
            new ChatterExchange("The weather does not care whose side we are on.", "Then we treat it with respect.", null, null, ChatterScene.Weather),
            new ChatterExchange("Strange how small the cockpit feels after dark.", "Stay on my wing. You are not alone.", null, null, ChatterScene.Night),
            new ChatterExchange("Just the panel lights and the engine tonight.", null, null, null, ChatterScene.Night),
        };
        private static readonly Dictionary<string, string[]> orders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "FORMATION", new[] { "Copy. Rejoining your wing.", "Coming back in. Keep it moving.", "Roger. Closing to station.", "Copy. Back where you can see me." } },
            { "ENGAGE", new[] { "Weapons free. Moving to engage.", "Tally. Taking the fight to them.", "Copy. Establishing the intercept.", "Copy. Time to earn our fuel." } },
            { "ATTACK", new[] { "Copy target. Beginning attack.", "Target acquired. Rolling in.", "Roger. Setting up the run.", "Copy. One pass, then we reassess." } },
            { "FIREFOREFFECT", new[] { "Copy. Committing full salvo.", "Full salvo. Keep clear of the run.", "Roger. Concentrating fire.", "Copy. Making this pass count." } },
            { "RETURNTOBASE", new[] { "Copy. Returning to base.", "Turning for home. Cover the egress.", "Roger. Setting course for recovery.", "Copy. Enough sky for today." } },
            { "FALLBACK", new[] { "Copy. Breaking off and regrouping.", "Breaking off. We fight on our terms.", "Roger. Opening separation.", "Copy. We can come back." } },
            { "ORBITHERE", new[] { "Copy. Establishing orbit.", "Holding here. Call the push.", "Roger. Taking up the hold.", "Copy. I will keep the seat warm." } },
            { "DELIVERCARGO", new[] { "Copy. Proceeding to delivery point.", "Moving the package. Cover us.", "Roger. Beginning delivery approach.", "Copy. Cargo stays with us until the drop." } },
            { "LANDHERE", new[] { "Copy. Setting up to land.", "Coming down. Watch our approach.", "Roger. Establishing final.", "Copy. Taking it to the ground." } },
            { "MOVETOPOINT", new[] { "Copy. Proceeding to the mark.", "Moving. Keep the route clear.", "Roger. Taking the assigned course.", "Copy. Following your line." } },
            { "SEEKANDDESTROY", new[] { "Copy. Searching the assigned area.", "Sweeping the area. Ready to engage.", "Roger. Starting the search.", "Copy. Let us see what is still out there." } },
            { "REFIT", new[] { "Copy. Returning to rearm and refuel.", "Heading in. Keep a place for me.", "Roger. Returning for turnaround.", "Copy. Ground crew gets the next shift." } },
            { "JAMTARGET", new[] { "Copy. Beginning radar suppression.", "Jammer coming up. Make your move.", "Roger. Working the assigned emitter.", "Copy. Putting noise on their picture." } },
            { "MANEUVER", new[] { "Copy. Executing manoeuvre.", "Executing. Watch the separation.", "Roger. Manoeuvring now.", "Copy. Keep a little room for me." } },
            { "STANDDOWN", new[] { "Copy. Task cancelled. Holding near friendlies.", "Standing down. Still available.", "Roger. Cancelling task and holding.", "Copy. Still here if you need us." } },
            { "COPY", new[] { "Roger.", "Copy.", "Understood.", "Copy that." } },
        };
        private static readonly Dictionary<string, string[]> events = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ENGAGING", new[] { "Engaging{on}.", "Committing{on}. Cover my exit.", "Moving to engage{on}.", "Taking{target}. Keep the lane clear." } },
            { "DEFENDING", new[] { "Defensive.", "Defensive. Breaking away.", "Defensive. Opening separation.", "Defensive. Give me room." } },
            { "BREAKCALL", new[] { "Lead, break! Missile tracking you!", "Break, Lead! Countermeasures now!", "Lead, missile inbound. Break now.", "Lead, break now. Do not hold that course." } },
            { "SPLASH", new[] { "Target destroyed{detail}.", "Good hit{detail}. Target down.", "Confirmed kill{detail}.", "Target down{detail}. Keep scanning." } },
            { "WINCHESTER", new[] { "Winchester. Returning to base.", "Stores empty. Turning for home.", "Winchester. Beginning recovery.", "Winchester. Heading home with an empty rack." } },
            { "BINGO", new[] { "Bingo fuel. Returning to base.", "Bingo. Cover us on the way out.", "At bingo. Turning for recovery.", "Bingo. That is our margin gone." } },
            { "REJOINING", new[] { "Rejoining your wing.", "Coming back in. Hold your heading.", "Rejoining. Closing steadily.", "Rejoining. Good to see you again." } },
            { "TAXIING", new[] { "Taxiing to the runway.", "Taxiing out. Ready for departure.", "Taxiing. Maintaining interval.", "Taxiing out. Another long day." } },
            { "DEPARTING", new[] { "Beginning takeoff.", "Rolling. See you overhead.", "Beginning departure.", "Departing. Back to work." } },
            { "AIRBORNE", new[] { "Airborne. Continuing on task.", "Wheels up. Climbing out.", "Airborne. Establishing climb.", "Airborne. Field is behind us." } },
            { "AIRBORNEREJOINING", new[] { "Airborne. Joining your wing.", "Off the deck. Coming to you.", "Airborne. Closing for join-up.", "Airborne. Save a place for me." } },
            { "FALLINGBACK", new[] { "Disengaging. Falling back.", "Breaking off. Regroup before we push.", "Opening distance. Disengaging.", "Falling back. We still have options." } },
            { "HOLDING", new[] { "Holding at standoff.", "Holding. Ready for the next push.", "Maintaining the hold.", "Holding here. Listening." } },
            { "COVERING", new[] { "Covering your flight.", "I have your six. Take the shot.", "Cover established. Continue.", "Watching your back. Keep moving." } },
            { "ORBITING", new[] { "Orbit established. On station.", "On station. Ready to move.", "Established in the orbit.", "On station. Waiting on your call." } },
            { "DELIVERING", new[] { "Beginning cargo delivery.", "Taking the package in. Cover the run.", "On delivery approach.", "Beginning delivery. Keep this one quiet." } },
            { "DELIVERED", new[] { "Cargo released. Beginning egress.", "Package away. Coming out.", "Delivery complete. Leaving the drop.", "Cargo delivered. Our part is done." } },
            { "NODROPOFF", new[] { "Unable to deliver. Returning with cargo.", "No usable drop-off. Bringing it home.", "Delivery unavailable. Retaining cargo.", "No drop-off. Package stays with us." } },
            { "FIREFOREFFECT", new[] { "Full salvo{on}.", "Committing all stores{on}. Keep clear.", "Concentrating fire{on}.", "Full salvo{on}. One committed pass." } },
            { "EXPENDED", new[] { "Stores expended. Off target.", "Weapons away. Coming off the run.", "Delivery complete. Off target.", "Off target. Check the results before another pass." } },
            { "OUTOFAMMO", new[] { "Winchester. Rejoining formation.", "Stores empty. Coming back to your wing.", "Winchester. Returning to station.", "Winchester. Still flying with you." } },
            { "DOWN", new[] { "Touchdown. Rolling out.", "On the deck. Ending the sortie.", "Down. Beginning rollout.", "Down in one piece. That will do." } },
            { "UNABLE", new[] { "Unable to maintain station. Returning to base.", "Cannot hold this pace. Returning to base.", "Outside flight limits. Returning to base.", "Cannot keep station. Taking the aircraft home." } },
            { "SLOWLEADER", new[] { "Lead, too slow for close formation. Holding wide.", "Lead, increase airspeed. I am holding wide.", "Holding wide until your speed increases.", "Lead, I need more airspeed to close up." } },
            { "PANIC", new[] { "Missile inbound! Defensive!", "Missile! Breaking hard!", "Missile warning. Going defensive.", "Missile inbound. Breaking now." } },
            { "DEFENSIVECLEAR", new[] { "Missile warning clear. Resuming.", "Threat warning clear. Back on task.", "Warning clear. Recovering the flight path.", "Warning clear. Still here." } },
            { "FOX1", new[] { "Fox one{on}.", "Fox one{on}. Guiding.", "Fox one{on}. Maintaining illumination.", "Fox one{on}." } },
            { "FOX2", new[] { "Fox two{on}.", "Fox two{on}. Missile away.", "Fox two{on}.", "Fox two{on}. Coming off." } },
            { "FOX3", new[] { "Fox three{on}.", "Fox three{on}. Weapon away.", "Fox three{on}.", "Fox three{on}. Staying alert." } },
            { "MAGNUM", new[] { "Magnum{on}.", "Magnum{on}. Coming off the emitter.", "Magnum{on}. Weapon away.", "Magnum{on}." } },
            { "RIFLE", new[] { "Rifle{on}.", "Rifle{on}. Coming off target.", "Rifle{on}. Missile away.", "Rifle{on}." } },
            { "JAMMING", new[] { "Jamming{target}.", "Jammer active{detail}. Make your move.", "Suppression active{detail}.", "Putting noise on{target}." } },
            { "JAMMINGOFF", new[] { "Jammer off.", "Jammer off. Returning to task.", "Jamming ended.", "Jammer off. Back to listening." } },
            { "MANEUVERING", new[] { "Executing{detail}.", "Beginning{detail}. Keep clear.", "Manoeuvring{detail}.", "Executing{detail}. Watch the spacing." } },
            { "MANEUVERDONE", new[] { "Manoeuvre complete. Rejoining.", "Rolling out. Coming back in.", "Manoeuvre complete. Recovering station.", "Finished. Back on your wing." } },
            { "DAMAGED", new[] { "Taking damage. Assessing the aircraft.", "I am hit. Checking what we have left.", "Damage sustained. Checking systems.", "Took a hit. Let me assess it." } },
            { "CRITICAL", new[] { "Critical damage. Requesting cover.", "Heavy damage! I need an exit!", "Aircraft critical. Assessing recovery.", "Heavy damage. Keep them off me." } },
            { "PILOTKILLED", new[] { "Pilot lost{detail}.", "We lost a pilot{detail}.", "Confirmed pilot loss{detail}.", "Pilot lost{detail}." } },
            { "EJECTED", new[] { "Pilot ejected{detail}.", "Ejection observed{detail}. Mark the position.", "Pilot out{detail}. Recovery required.", "Pilot punched out{detail}. Mark that position." } },
            { "AIRFRAMELOST", new[] { "Aircraft lost{detail}.", "We have an aircraft down{detail}.", "Aircraft down{detail}.", "Aircraft lost{detail}." } },
            { "RECOVERED", new[] { "Aircraft recovered.", "Parked. Ready for turnaround.", "Recovery complete.", "Recovered. Ground crew has it." } },
            { "JOKER", new[] { "Joker fuel. Monitoring reserve.", "At joker. Watch our time here.", "Joker fuel. Planning the return.", "Joker. We have less time than we had." } },
            { "FALLINGBEHIND", new[] { "Falling behind. Closing as able.", "Cannot match the pace. Trying to close.", "Lagging the formation. Adjusting.", "Falling behind. Leave me some room to catch up." } },
            { "PULLUP", new[] { "Terrain! Pull up!", "Pull up! Terrain ahead!", "Terrain warning. Climbing now.", "Terrain! Climb now!" } },
            { "BREAKOFF", new[] { "Collision risk! Break away!", "Break off! Too close!", "Collision warning. Opening separation.", "Break away! Clear the flight path!" } },
            { "ESCORTLOST", new[] { "Escort lost. Forming on you.", "Escort down. Returning to your wing.", "Escort lost. Rejoining Lead.", "Lost the escort. Coming back to you." } },
            { "GOAROUND", new[] { "Going around.", "Missed approach. Coming around.", "Going around. Resetting the approach.", "Going around. We have another try." } },
            { "TASKDONE", new[] { "Task complete. Awaiting orders.", "Task done. Ready for the next call.", "Assignment complete. Standing by.", "Task complete. Still available." } },
            { "UNABLEORDER", new[] { "Negative. Unable to comply.", "Cannot execute that order.", "Unable under current conditions.", "Unable. I need another option." } },
        };

        public static IEnumerable<string> EventKeys => events.Keys;
        public static int AmbientCount => ambient.Length;
        public static ChatterExchange AmbientAt(int index) => ambient[Index(index, ambient.Length)];
        public static ChatterExchange Ambient(int seed, bool repliesAllowed = true)
        {
            for (int i = 0; i < ambient.Length; i++)
            {
                ChatterExchange line = AmbientAt(seed + i);
                if (repliesAllowed || line.Reply == null) return line;
            }
            return default;
        }

        public static string Identity(string name, string callsign)
        {
            string cleanName = string.IsNullOrWhiteSpace(name) ? "UNKNOWN" : name.Trim();
            string cleanCallsign = string.IsNullOrWhiteSpace(callsign)
                ? "NO CALLSIGN"
                : callsign.Trim().ToUpperInvariant();

            int split = cleanName.LastIndexOf(' ');
            if (split <= 0 || split >= cleanName.Length - 1)
                return "\"" + cleanCallsign + "\" " + cleanName.ToUpperInvariant();

            string given = cleanName.Substring(0, split).Trim();
            string surname = cleanName.Substring(split + 1).Trim().ToUpperInvariant();
            return given + " \"" + cleanCallsign + "\" " + surname;
        }

        public static string Acknowledge(ChatterPersona persona, string order, int seed)
        {
            if (!orders.TryGetValue(order ?? "COPY", out string[] lines)) lines = orders["COPY"];
            return Variant(lines, persona, seed);
        }

        public static string Event(ChatterPersona persona, string eventName, string detail, int seed)
        {
            if (string.Equals(eventName, "DETACHED", StringComparison.OrdinalIgnoreCase))
                return Acknowledge(persona, "RETURNTOBASE", seed);
            if (!events.TryGetValue(eventName ?? "", out string[] lines)) return Acknowledge(persona, "COPY", seed);
            string subject = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
            if (string.Equals(eventName, "JOKER", StringComparison.OrdinalIgnoreCase) && subject != null)
                return "Joker fuel. About " + subject + " minutes to bingo.";
            return Variant(lines, persona, seed).Replace("{on}", subject == null ? "" : " on " + subject)
                .Replace("{detail}", subject == null ? "" : ", " + subject)
                .Replace("{target}", subject == null ? " the assigned target" : " " + subject);
        }

        private static string Variant(string[] lines, ChatterPersona persona, int seed)
        {
            // Alternate the pilot's own phrasing with the standard call; urgency never becomes a joke.
            int style = (int)persona;
            if (style < 0 || style >= lines.Length) style = 0;
            return lines[((uint)seed % 3 == 0) ? 0 : style];
        }
        private static int Index(int seed, int count) => count <= 0 ? 0 : (int)((uint)seed % (uint)count);
    }
}
