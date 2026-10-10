namespace BoscaliSummer.Core.Util
{
    /// <summary>
    /// Reads a mono 16-bit PCM WAV into floats. That is the only shape tools/Build-SoundClips.py writes;
    /// any other channel count, depth or rate, or a damaged file, decodes to null and the caller keeps its fallback.
    /// </summary>
    internal static class PcmWav
    {
        public static float[] Decode(byte[] wav, int rate)
        {
            if (wav == null || wav.Length < 12 || !Tag(wav, 0, "RIFF") || !Tag(wav, 8, "WAVE")) return null;
            bool format = false;
            for (long at = 12; at + 8 <= wav.Length;)
            {
                long size = U32(wav, at + 4), body = at + 8;
                if (body + size > wav.Length) return null;
                if (Tag(wav, at, "fmt "))
                    format = size >= 16 && U16(wav, body) == 1 && U16(wav, body + 2) == 1 && U32(wav, body + 4) == rate &&
                        U16(wav, body + 14) == 16;
                else if (Tag(wav, at, "data"))
                {
                    if (!format) return null;
                    var samples = new float[size / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = (short)U16(wav, body + i * 2L) / 32768f;
                    return samples;
                }
                at = body + size + (size & 1); // chunks are padded to an even size
            }
            return null;
        }

        private static bool Tag(byte[] b, long at, string tag) =>
            b[at] == tag[0] && b[at + 1] == tag[1] && b[at + 2] == tag[2] && b[at + 3] == tag[3];
        private static int U16(byte[] b, long at) => b[at] | b[at + 1] << 8;
        private static long U32(byte[] b, long at) => b[at] | (long)b[at + 1] << 8 | (long)b[at + 2] << 16 | (long)b[at + 3] << 24;
    }
}
