using System;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    internal enum NavalRole { Patrol, Screen, CoastalSupport, Withdraw }

    internal static class NavalTask
    {
        // Separate ships along and across the objective instead of sending a fleet to one point.
        internal static void Position(NavalRole role, float x, float z, int shipId, int leg,
            out float destinationX, out float destinationZ)
        {
            int side = (shipId & 1) == 0 ? 1 : -1;
            int turn = (leg & 1) == 0 ? 1 : -1;
            switch (role)
            {
                case NavalRole.Patrol:
                    destinationX = x + side * 1800f;
                    destinationZ = z + turn * 2200f;
                    break;
                case NavalRole.Screen:
                    destinationX = x + side * 1200f;
                    destinationZ = z + turn * 900f;
                    break;
                case NavalRole.CoastalSupport:
                    destinationX = x + side * 600f;
                    destinationZ = z + turn * 500f;
                    break;
                default:
                    destinationX = x + side * 1000f;
                    destinationZ = z;
                    break;
            }
        }

        internal static bool CanRedirect(bool host, bool commanded, bool holding, bool inCombat,
            bool hasTask) => host && !commanded && !holding && !inCombat && hasTask;
    }
}
