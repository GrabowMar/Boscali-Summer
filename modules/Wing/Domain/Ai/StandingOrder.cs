using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Versioned standing intent; completions may retire only their starting revision.</summary>
    internal sealed class StandingOrder<T>
    {
        private readonly Func<T, T, bool> sameIntent;
        public T Current { get; private set; }
        public int Revision { get; private set; }

        public StandingOrder(T initial, Func<T, T, bool> sameIntent)
        {
            Current = initial;
            this.sameIntent = sameIntent ?? throw new ArgumentNullException(nameof(sameIntent));
        }

        public bool Set(T next)
        {
            if (sameIntent(Current, next)) return false;
            Current = next;
            Revision++;
            return true;
        }
    }
}
