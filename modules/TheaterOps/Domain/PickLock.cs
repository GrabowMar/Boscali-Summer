using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.TheaterOps.Domain
{
    /// <summary>A pilot's pick locks other pilots out until a mission second. A staff pick sets no lock.</summary>
    internal static class PickLock
    {
        /// <summary>Null = allowed. Locked when a different actor acts inside a pilot's lock (now &lt; lockUntil); secondsLeft is then the time remaining, else 0.</summary>
        internal static TheaterCommandCode? Check(bool pickedByStaff, float now, float lockUntil, ulong pickedById, ulong actorId, out float secondsLeft)
        {
            secondsLeft = 0f;
            if (pickedByStaff || actorId == pickedById || !(now < lockUntil)) return null;
            secondsLeft = lockUntil - now;
            return TheaterCommandCode.Locked;
        }
    }
}
