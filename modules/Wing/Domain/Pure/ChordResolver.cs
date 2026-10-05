namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    internal enum ChordKind : byte { None, Stance, Order, Ladder }

    internal struct Chord
    {
        public ChordKind Kind;
        public int Index;
    }

    /// <summary>Wing key + key (user ruling 2026-10-04): 1-6 a stance slot, a letter the command-card key in
    /// <see cref="OrderKeys"/> order, digits the open Call Ladder. Nothing while a text field is typed in.</summary>
    internal static class ChordResolver
    {
        public const string OrderKeys = "QWERASDFZXCVTYUIGHJKBNM,";

        public static Chord Resolve(bool wingHeld, bool typing, bool ladderOpen, char key)
        {
            if (!wingHeld || typing) return default;
            if (key >= '0' && key <= '9')
            {
                if (ladderOpen) return new Chord { Kind = ChordKind.Ladder, Index = key - '0' };
                if (key >= '1' && key <= '6') return new Chord { Kind = ChordKind.Stance, Index = key - '1' };
                return default;
            }
            int i = OrderKeys.IndexOf(char.ToUpperInvariant(key));
            return i < 0 ? default : new Chord { Kind = ChordKind.Order, Index = i };
        }
    }
}
