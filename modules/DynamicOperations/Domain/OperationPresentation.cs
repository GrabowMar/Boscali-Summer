using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
{
    internal static class OperationPresentation
    {
        public static ObjectiveTasking Build(Operation op, bool onStation, bool neutralized, ObjectiveAsset asset,
            ObjectiveContact contact, float contactAgeSeconds, string effect, string returnBase,
            ObjectiveAllegiance allegiance = ObjectiveAllegiance.Unknown)
        {
            ObjectiveLifecycle lifecycle = op.State switch
            {
                OperationState.Offered => ObjectiveLifecycle.Offered,
                OperationState.Completed => ObjectiveLifecycle.Completed,
                OperationState.Expired => ObjectiveLifecycle.Expired,
                OperationState.Cancelled => ObjectiveLifecycle.Cancelled,
                _ => ObjectiveLifecycle.Active
            };
            bool completed = op.State == OperationState.Completed;
            bool active = op.State == OperationState.Active;
            bool covering = onStation || op.HoldSeconds > 0f;
            bool held = op.HoldSeconds >= op.HoldRequired;
            int current;
            string[] titles, conditions;
            bool continuous = false;
            string hold = ((int)op.HoldRequired) + " continuous seconds";
            switch (op.Kind)
            {
                case OperationKind.SupplyEscort:
                case OperationKind.RepairCover:
                    bool supply = op.Kind == OperationKind.SupplyEscort;
                    titles = new[] { "JOIN", "COVER", supply ? "TRANSFER" : "REPAIR" };
                    conditions = new[] { "Fly within 1.5 km of the asset, at least 50 m above it.",
                        "Keep the same aircraft on station for " + hold + ". Interruption resets cover.",
                        supply ? "Stay on station until the truck transfers supplies to a friendly unit." :
                            "Stay on station until native engineers finish repairing this site." };
                    current = held ? 2 : covering ? 1 : 0;
                    continuous = true;
                    break;
                case OperationKind.Rescue:
                case OperationKind.SortieReport:
                    bool rescue = op.Kind == OperationKind.Rescue;
                    titles = new[] { rescue ? "RECOVER" : "OBSERVE", "RETURN" };
                    conditions = new[] { rescue ? "Recover the marked friendly pilot using native rescue." :
                        "Observe within 1.5 km, 50+ m above the contact, for 30s with clear sightline and fresh tracking.",
                        "Land the same aircraft within 1 km of " + (string.IsNullOrEmpty(returnBase) ? "the marked friendly base" : returnBase) + "." };
                    current = op.Returning ? 1 : 0;
                    continuous = !rescue && !op.Returning;
                    break;
                case OperationKind.DamageAssessment:
                    titles = new[] { "STRIKE", "SURVEY" };
                    conditions = new[] { "Neutralize the marked hostile target after accepting this task.",
                        "Survey its last known site for 20s within 1.5 km, 50+ m above it, with a clear terrain sightline." };
                    current = neutralized ? 1 : 0;
                    continuous = neutralized;
                    break;
                case OperationKind.Defend:
                case OperationKind.Patrol:
                case OperationKind.Recon:
                case OperationKind.BattlefieldSurvey:
                    bool survey = op.Kind == OperationKind.Recon || op.Kind == OperationKind.BattlefieldSurvey;
                    titles = new[] { "JOIN", survey ? "SURVEY" : "HOLD" };
                    conditions = new[] { "Fly within 1.5 km of the marked area, at least 50 m above it.",
                        "Remain on station for " + hold + (survey ? " with clear terrain sightline" : "") +
                        (op.Kind == OperationKind.Recon ? " and fresh faction tracking." : ".") };
                    current = covering ? 1 : 0;
                    continuous = true;
                    break;
                case OperationKind.Jam:
                    titles = new[] { "JAM" };
                    conditions = new[] { "Apply a directed jammer to this live emitter for 45 continuous seconds." };
                    current = 0;
                    break;
                case OperationKind.Rappel:
                case OperationKind.Rooftop:
                    titles = new[] { "INSERT" };
                    conditions = new[] { op.Kind == OperationKind.Rooftop
                        ? "Ibis + 8 troops: hover below 45 m above the marked roof and finish fast-roping onto that building."
                        : "Ibis + 8 troops: hover below 45 m and finish fast-roping within 100 m of the ground mark." };
                    current = 0;
                    break;
                case OperationKind.Capture:
                    titles = new[] { "SECURE" };
                    conditions = new[] { "Capture this forward base. Faction effort counts after acceptance." };
                    current = 0;
                    break;
                default:
                    titles = new[] { op.Kind == OperationKind.Intercept ? "INTERCEPT" : "STRIKE" };
                    conditions = new[] { op.Kind == OperationKind.Intercept
                        ? "Neutralize the tracked hostile aircraft before the deadline."
                        : "Neutralize the marked hostile target before the deadline." };
                    current = 0;
                    break;
            }
            var phases = new ObjectivePhase[titles.Length];
            float holdProgress = System.Math.Min(1f, System.Math.Max(0f, op.HoldSeconds / op.HoldRequired));
            for (byte i = 0; i < phases.Length; i++)
            {
                ObjectivePhaseStatus state = completed || i < current ? ObjectivePhaseStatus.Done :
                    active && i == current ? ObjectivePhaseStatus.Current : ObjectivePhaseStatus.Pending;
                phases[i] = new ObjectivePhase(i, titles[i], conditions[i], state,
                    state == ObjectivePhaseStatus.Done ? 1f : state == ObjectivePhaseStatus.Current && TimedGate(op.Kind, i) ? holdProgress : 0f);
            }
            string next = completed ? "Task complete. Review the host result." :
                !op.IsLive ? "Task " + op.State.ToString().ToLowerInvariant() + "." : Instruction(op, current, returnBase);
            if (op.State == OperationState.Offered) next = "Accept this task to begin. " + next;
            string blocker = !active ? "" : contact == ObjectiveContact.Lost || contact == ObjectiveContact.Unavailable
                ? "Contact unavailable. Restore faction tracking before continuing." :
                op.Kind == OperationKind.Recon || op.Kind == OperationKind.SortieReport && !op.Returning
                    ? contact == ObjectiveContact.LastKnown ? "Fresh faction tracking required for observation." :
                        !onStation ? "No eligible aircraft on station. Check range, height and sightline." : "" :
                continuous && !onStation ? "No eligible aircraft on station. Check range, height and sightline." :
                (op.Kind == OperationKind.SupplyEscort || op.Kind == OperationKind.RepairCover) && held
                    ? "Awaiting native " + (op.Kind == OperationKind.SupplyEscort ? "supply transfer." : "repair completion.") : "";
            return new ObjectiveTasking(Family(op.Kind), asset, contact, contactAgeSeconds, next, blocker, effect, phases, lifecycle,
                active ? OperationFailure.DismissalMessage(true) : string.Empty, allegiance);
        }

        private static bool TimedGate(OperationKind kind, int index) => kind switch
        {
            OperationKind.SupplyEscort or OperationKind.RepairCover or OperationKind.Defend or OperationKind.Patrol or
                OperationKind.Recon or OperationKind.BattlefieldSurvey or OperationKind.DamageAssessment => index == 1,
            OperationKind.SortieReport or OperationKind.Jam => index == 0,
            _ => false
        };

        private static string Instruction(Operation op, int phase, string returnBase) => op.Kind switch
        {
            OperationKind.SupplyEscort => phase == 0 ? "Join the truck's cover area." : phase == 1
                ? "Cover the truck from above for 60 continuous seconds." : "Maintain cover until the truck transfers supplies.",
            OperationKind.RepairCover => phase == 0 ? "Join the repair site's cover area." : phase == 1
                ? "Cover the repair site for 30 continuous seconds." : "Maintain cover until native engineers finish repairs.",
            OperationKind.Rescue or OperationKind.SortieReport when op.Returning =>
                "Land the same aircraft at " + (string.IsNullOrEmpty(returnBase) ? "the marked friendly base" : returnBase) + ".",
            OperationKind.Rescue => "Recover the marked friendly pilot using native rescue.",
            OperationKind.SortieReport => "Observe the tracked contact for 30 continuous seconds.",
            OperationKind.DamageAssessment => phase == 0 ? "Neutralize the marked target." : "Survey the strike site for 20 continuous seconds.",
            OperationKind.Defend => phase == 0 ? "Join the friendly base's defense area." : "Hold overhead for 180 continuous seconds; keep the base friendly.",
            OperationKind.Patrol => phase == 0 ? "Join the marked patrol area." : "Watch the approach for 90 continuous seconds.",
            OperationKind.Recon => phase == 0 ? "Join the contact's observation area." : "Observe for 20 continuous seconds with fresh tracking.",
            OperationKind.BattlefieldSurvey => phase == 0 ? "Join the marked survey area." : "Survey the wreck or damaged site for 30 continuous seconds.",
            OperationKind.Jam => "Keep the directed jammer on the live emitter for 45 continuous seconds.",
            OperationKind.Rappel => "Fast-rope 8 troops from an Ibis onto the ground mark.",
            OperationKind.Rooftop => "Fast-rope 8 troops from an Ibis onto the marked roof.",
            OperationKind.Capture => "Capture the marked forward base.",
            OperationKind.Intercept => "Intercept and neutralize the tracked hostile aircraft.",
            OperationKind.SupplyInterdict => "Neutralize the tracked hostile supply truck.",
            OperationKind.ElectronicWarfare => "Neutralize the tracked hostile jammer.",
            _ => "Neutralize the marked hostile target."
        };

        public static ObjectiveFamily Family(OperationKind kind) => kind switch
        {
            OperationKind.Capture or OperationKind.Defend => ObjectiveFamily.Control,
            OperationKind.Interdict => ObjectiveFamily.Strike,
            OperationKind.Intercept or OperationKind.Patrol => ObjectiveFamily.AirCover,
            OperationKind.Jam or OperationKind.ElectronicWarfare => ObjectiveFamily.Electronic,
            OperationKind.Rappel or OperationKind.Rooftop => ObjectiveFamily.Insertion,
            OperationKind.Rescue => ObjectiveFamily.Recovery,
            OperationKind.Recon or OperationKind.DamageAssessment or OperationKind.SortieReport or OperationKind.BattlefieldSurvey
                => ObjectiveFamily.Reconnaissance,
            OperationKind.SupplyEscort or OperationKind.SupplyInterdict or OperationKind.RepairCover => ObjectiveFamily.Logistics,
            _ => ObjectiveFamily.Unknown
        };
    }
}
