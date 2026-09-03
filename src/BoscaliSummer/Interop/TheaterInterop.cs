using NOAvionics;

namespace BoscaliSummer.Interop
{
    /// <summary>
    /// Public, reflection-safe façade. Wing Command must not compile against this
    /// assembly; it reads the same AppDomain keys via its own copy of PresenceBoard.
    /// </summary>
    public static class TheaterPresence
    {
        public static int ApiVersion => 1;
        public const string Guid = "com.marci.boscalisummer";
    }

    public static class TheaterDoctrine
    {
        public static int ApiVersion => 1;
        public static int Stance => PresenceBoard.GetInts(PresenceBoard.TheaterDoctrine) is int[] values &&
                                    values.Length > 0
            ? values[0]
            : 0;

        public static int[] PriorityIds => PresenceBoard.GetInts(PresenceBoard.TheaterPriorityIds);
    }

    public static class SupportMapMode
    {
        public static int ApiVersion => 1;
        public static bool OwnsMapGesture => MapPicker.IsOwner(MapPicker.Support);
    }
}

namespace BoscaliSummer
{
    internal static class TheaterInteropPush
    {
        public static void PublishGuid()
        {
            PresenceBoard.SetString(PresenceBoard.TheaterGuid, Interop.TheaterPresence.Guid);
        }

        public static void PublishDoctrine(int stance, int[] priorityHashes)
        {
            PresenceBoard.SetInts(PresenceBoard.TheaterDoctrine, new[] { stance });
            PresenceBoard.SetInts(PresenceBoard.TheaterPriorityIds, priorityHashes);
        }

        public static void Clear()
        {
            PresenceBoard.SetString(PresenceBoard.TheaterGuid, null);
            PresenceBoard.SetInts(PresenceBoard.TheaterDoctrine, null);
            PresenceBoard.SetInts(PresenceBoard.TheaterPriorityIds, null);
            BezelRegistry.Release(BezelRegistry.Ops);
            BezelRegistry.Release(BezelRegistry.Rad);
            MapPicker.Disarm(MapPicker.Support);
        }
    }
}
