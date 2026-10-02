using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Modules.Support.Domain.Calls
{
    internal enum ArmStep : byte { Armed, Fire }

    /// <summary>Core §5.3: first press arms, second press on the same CALL within 8 s fires.</summary>
    internal sealed class ArmState
    {
        public const float ArmSeconds = 8f;

        private float armedAt;

        public SupportActionId? Armed { get; private set; }

        public ArmStep Press(SupportActionId id, float now)
        {
            if (Armed == id && now - armedAt <= ArmSeconds)
            {
                Armed = null;
                return ArmStep.Fire;
            }
            Armed = id;
            armedAt = now;
            return ArmStep.Armed;
        }

        public bool Tick(float now)
        {
            if (Armed == null || now - armedAt <= ArmSeconds) return false;
            Armed = null;
            return true;
        }

        public void Clear() => Armed = null;
    }

    internal enum AimSource : byte { None, Pod, Map }

    /// <summary>Own designation / targeting-pod point first, then the last map pick.</summary>
    internal static class Aim
    {
        public static AimSource Pick(bool hasPod, bool hasMap) =>
            hasPod ? AimSource.Pod : hasMap ? AimSource.Map : AimSource.None;

        public static string Label(AimSource source) =>
            source == AimSource.Pod ? "AIM: POD" : source == AimSource.Map ? "AIM: MAP" : "AIM: NONE";
    }

    /// <summary>Client side of the request state machine: PENDING → CONFIRMED | REFUSED, refused after 3 s silence.</summary>
    internal sealed class CallRequestTracker
    {
        public const float TimeoutSeconds = 3f;

        private float startedAt;

        public bool Pending { get; private set; }
        public int PendingId { get; private set; }
        public float PendingCost { get; private set; }

        public bool Begin(int id, float cost, float now)
        {
            if (Pending) return false;
            Pending = true;
            PendingId = id;
            PendingCost = cost;
            startedAt = now;
            return true;
        }

        public bool Resolve(int id)
        {
            if (!Pending || id != PendingId) return false;
            Pending = false;
            return true;
        }

        public bool Tick(float now, out float refund)
        {
            refund = 0f;
            if (!Pending || now - startedAt < TimeoutSeconds) return false;
            Pending = false;
            refund = PendingCost;
            return true;
        }

        public void Clear() => Pending = false;
    }
}
