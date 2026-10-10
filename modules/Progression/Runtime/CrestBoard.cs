using System.Collections.Generic;

namespace BoscaliSummer.Modules.Progression.Runtime
{
    /// <summary>
    /// The squadron crests other players wear, as the host published them: player id to design and paint.
    /// Pure (no Unity or game types) so the test suite compiles it. Bounded at <see cref="Capacity"/>: a new owner
    /// past the cap is refused, never made room for by dropping someone else's crest.
    /// </summary>
    internal sealed class CrestBoard
    {
        public const int Capacity = 32;

        /// <summary>Id 0 means "no identity" (PlayerIdentity.None); it never owns a crest.</summary>
        public const ulong NoOwner = 0UL;

        /// <summary>One player's crest. A stored entry never carries <see cref="EmblemPaint.Off"/>: that is how a crest is withdrawn.</summary>
        public readonly struct Entry
        {
            public readonly EmblemDesign Design;
            public readonly EmblemPaint Paint;

            public Entry(EmblemDesign design, EmblemPaint paint) { Design = design; Paint = paint; }
        }

        private readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>(Capacity);

        public int Count => entries.Count;

        /// <summary>
        /// Stores or replaces an owner's crest; <see cref="EmblemPaint.Off"/> withdraws it instead. False when
        /// refused: no owner id, or a new owner while the board is full.
        /// </summary>
        public bool Set(ulong owner, EmblemDesign design, EmblemPaint paint)
        {
            if (owner == NoOwner) return false;
            if (paint == EmblemPaint.Off) { entries.Remove(owner); return true; }
            if (entries.Count >= Capacity && !entries.ContainsKey(owner)) return false;
            entries[owner] = new Entry(design, paint);
            return true;
        }

        public bool Remove(ulong owner) => entries.Remove(owner);

        public void Clear() => entries.Clear();

        public bool TryGet(ulong owner, out Entry entry) => entries.TryGetValue(owner, out entry);

        /// <summary>Replaces the contents of <paramref name="destination"/> with every entry; allocates nothing once the list has room.</summary>
        public void CopyTo(List<KeyValuePair<ulong, Entry>> destination)
        {
            destination.Clear();
            foreach (KeyValuePair<ulong, Entry> pair in entries) destination.Add(pair);
        }

        /// <summary>
        /// Reads eight raw layer bytes (in <see cref="EmblemLayer"/> order) and a paint byte off the wire.
        /// False when any layer is outside its catalogue or the paint is not a defined <see cref="EmblemPaint"/>;
        /// the design is only built from bytes that passed, so a hostile peer cannot wrap into a valid crest.
        /// </summary>
        public static bool TryDecode(byte shape, byte division, byte charge, byte accent, byte field, byte second,
            byte ink, byte number, byte paint, out EmblemDesign design, out EmblemPaint mode)
        {
            design = default;
            mode = EmblemPaint.Off;
            if (shape >= EmblemDesign.Count(EmblemLayer.Shape) || division >= EmblemDesign.Count(EmblemLayer.Division) ||
                charge >= EmblemDesign.Count(EmblemLayer.Charge) || accent >= EmblemDesign.Count(EmblemLayer.Accent) ||
                field >= EmblemDesign.Count(EmblemLayer.Field) || second >= EmblemDesign.Count(EmblemLayer.Second) ||
                ink >= EmblemDesign.Count(EmblemLayer.Ink) || number >= EmblemDesign.Count(EmblemLayer.Number) ||
                paint > (byte)EmblemPaint.LowVis)
                return false;
            design = new EmblemDesign(shape, division, charge, accent, field, second, ink, number);
            mode = (EmblemPaint)paint;
            return true;
        }
    }
}
