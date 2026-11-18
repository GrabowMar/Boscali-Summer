using System;

namespace BoscaliSummer.Modules.Maps.Domain
{
    /// <summary>One 20-byte record of a map's <c>scenery.bin</c> (little endian, contract 3.1): position in map metres,
    /// scale, the index into the manifest's <c>kinds</c>, the kit variant and the yaw.</summary>
    internal struct SceneryRecord
    {
        public const int Size = 20;

        public float x, y, z, scale;
        public byte kind, variant;
        /// <summary>0..65535 = 0..360 degrees clockwise from north.</summary>
        public ushort yaw;

        public float YawDegrees => yaw * (360f / 65536f);

        /// <summary>Whole records in the file; a trailing partial record is not counted.</summary>
        public static int Count(byte[] bytes) => bytes == null ? 0 : bytes.Length / Size;

        public static SceneryRecord Read(byte[] bytes, int index)
        {
            int at = index * Size;
            return new SceneryRecord
            {
                x = BitConverter.ToSingle(bytes, at),
                y = BitConverter.ToSingle(bytes, at + 4),
                z = BitConverter.ToSingle(bytes, at + 8),
                scale = BitConverter.ToSingle(bytes, at + 12),
                kind = bytes[at + 16],
                variant = bytes[at + 17],
                yaw = BitConverter.ToUInt16(bytes, at + 18),
            };
        }
    }
}
