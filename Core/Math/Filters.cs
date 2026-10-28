
namespace BoscaliSummer.Core.Math
{
    /// <summary>First-order low-pass with exact discretisation. The first sample initialises it; a
    /// non-positive time constant passes the input through.</summary>
    internal struct FirstOrder
    {
        public float Value;
        public bool Primed;

        public float Update(float input, float tau, float dt)
        {
            if (!Primed || tau <= 0f)
            {
                Value = input;
                Primed = true;
                return Value;
            }
            Value += (input - Value) * (1f - (float)System.Math.Exp(-dt / tau));
            return Value;
        }

        public void Reset(float value)
        {
            Value = value;
            Primed = true;
        }
    }

    /// <summary>True once a condition has held continuously for the required time.</summary>
    internal struct Persistence
    {
        public float Elapsed;

        public bool Update(bool condition, float seconds, float dt)
        {
            Elapsed = condition ? Elapsed + dt : 0f;
            return condition && Elapsed >= seconds - 1e-6f;
        }
    }

    internal static class Slew
    {
        /// <summary>Move <paramref name="current"/> toward <paramref name="target"/> by at most rate·dt.</summary>
        public static float Step(float current, float target, float rate, float dt)
        {
            float max = rate * dt;
            return current + Scalar.Clamp(target - current, -max, max);
        }
    }
}
