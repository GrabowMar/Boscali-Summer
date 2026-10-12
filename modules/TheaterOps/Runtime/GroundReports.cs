using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>Per-faction maxima and tallies the report and the proof read between two passes.</summary>
    internal sealed class GroundFactionStats
    {
        /// <summary>Longest Contact stay (entry to exit; a stay still running counts), seconds.</summary>
        internal float MaxContactDwell;
        /// <summary>The most key changes any one group made, and the times a group left the effort while usable with the key unchanged; both restart with each operation id.</summary>
        internal int Rekeys, EffortSwaps;
        /// <summary>From the last finished sweep: eligible vehicles no group took, ground vehicles with a support or artillery component, and those that would otherwise be eligible.</summary>
        internal int Ungrouped, SupportSeen, SupportNavigating;
    }

    /// <summary>One member as the proof and the report read it.</summary>
    internal readonly struct GroundMemberReport
    {
        /// <summary>The vehicle.</summary>
        internal readonly GroundVehicle Vehicle;
        /// <summary>Its group, and whether that group is on the effort.</summary>
        internal readonly int GroupId; internal readonly bool OnEffort;
        /// <summary>The staged destination, when it has one (a frozen member has none: it holds where it stood).</summary>
        internal readonly bool HasStaged; internal readonly float StagedX, StagedZ;
        /// <summary>Seconds since the hook last answered this vehicle (-1 never) and the point it answered.</summary>
        internal readonly float AnsweredAgo, AnswerX, AnswerZ;
        /// <summary>Support or artillery component present.</summary>
        internal readonly bool Support;
        /// <summary>The seconds since the group's stage last changed.</summary>
        internal readonly float StageAge;

        internal GroundMemberReport(GroundVehicle vehicle, int groupId, bool onEffort, bool hasStaged, float stagedX, float stagedZ,
            float answeredAgo, float answerX, float answerZ, bool support, float stageAge)
        { Vehicle = vehicle; GroupId = groupId; OnEffort = onEffort; HasStaged = hasStaged; StagedX = stagedX; StagedZ = stagedZ;
          AnsweredAgo = answeredAgo; AnswerX = answerX; AnswerZ = answerZ; Support = support; StageAge = stageAge; }
    }

    /// <summary>One group as the proof reads it.</summary>
    internal readonly struct GroundGroupReport
    {
        /// <summary>Id, key (null when unassigned) and stage.</summary>
        internal readonly int Id; internal readonly string Key; internal readonly GroundStage Stage;
        /// <summary>On the effort, born at a depot exit, usable for the current key.</summary>
        internal readonly bool OnEffort, FromDepot, Usable;
        /// <summary>Live members and original strength.</summary>
        internal readonly int Alive, Formed;
        /// <summary>Centroid, stage target and the group's fix, metres.</summary>
        internal readonly float X, Z, TargetX, TargetZ, FixX, FixZ;
        /// <summary>Seconds since the stage began and since the group was created; seconds from creation to first joining the effort (-1 never).</summary>
        internal readonly float StageAge, BornAgo, FirstOnEffortAfter;

        internal GroundGroupReport(int id, string key, GroundStage stage, bool onEffort, bool fromDepot, bool usable,
            int alive, int formed, float x, float z, float targetX, float targetZ, float fixX, float fixZ,
            float stageAge, float bornAgo, float firstOnEffortAfter)
        { Id = id; Key = key; Stage = stage; OnEffort = onEffort; FromDepot = fromDepot; Usable = usable; Alive = alive; Formed = formed;
          X = x; Z = z; TargetX = targetX; TargetZ = targetZ; FixX = fixX; FixZ = fixZ;
          StageAge = stageAge; BornAgo = bornAgo; FirstOnEffortAfter = firstOnEffortAfter; }
    }
}
