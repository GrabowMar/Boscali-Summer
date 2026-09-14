using BoscaliSummer.Core;

namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// Engine-free description of a dithered staff portrait. The renderer turns this into a
    /// small green-glass texture; keeping the features separate means the face is testable
    /// and identical on every machine that has the seed.
    /// </summary>
    internal struct PortraitDesign
    {
        public byte Face;
        public byte Brow;
        public byte Eyes;
        public byte Nose;
        public byte Mouth;
        public byte FacialHair;
        public byte Hair;
        public byte Cap;
        public byte Collar;
        public byte Background;
        public byte Scar;
        public byte Glasses;

        public static PortraitDesign FromSeed(int seed)
        {
            uint hash = Deterministic.Hash(seed, 0x51ED, 0xC0DE, 0x7A17);
            return new PortraitDesign
            {
                Face = Draw(hash, 0, 3),
                Brow = Draw(hash, 1, 3),
                Eyes = Draw(hash, 2, 3),
                Nose = Draw(hash, 3, 3),
                Mouth = Draw(hash, 4, 2),
                FacialHair = Draw(hash, 5, 4),
                Hair = Draw(hash, 6, 4),
                Cap = Draw(hash, 7, 3),
                Collar = Draw(hash, 8, 2),
                Background = Draw(hash, 9, 2),
                Scar = Draw(hash, 10, 2),
                Glasses = Draw(hash, 11, 2),
            };
        }

        private static byte Draw(uint hash, int index, int count) =>
            (byte)(Deterministic.Hash(unchecked((int)hash), index, count) % (uint)count);
    }
}
