using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Core.Game
{
    /// <summary>One answer to "is this hostile a fresh contact": the census, the engine's contact scan and ground all read it.</summary>
    internal static class HostileContact
    {
        /// <summary>True when hq.trackingDatabase holds the unit and now - info.lastSpottedTime &lt;= ContactWindow.FreshSeconds; position = info.lastKnownPosition.</summary>
        internal static bool TryRead(FactionHQ hq, Unit unit, float now, out GlobalPosition position)
        {
            position = default;
            if (hq == null || unit == null || hq.trackingDatabase == null) return false;
            if (!hq.trackingDatabase.TryGetValue(unit.persistentID, out TrackingInfo info) || info == null) return false;
            if (!(now - info.lastSpottedTime <= ContactWindow.FreshSeconds)) return false;   // a NaN age fails
            position = info.lastKnownPosition;
            return true;
        }
    }
}
