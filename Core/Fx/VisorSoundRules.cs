using System;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>The HMD-9 visor's sounds (clips Core/Fx/Sfx/visor-*.wav, made by tools/Build-VisorSounds.py).</summary>
    internal enum VisorClip : byte
    {
        PowerUp, Resync, Tick, Ok, Fault, Align, Online, Glitch, Emp, Warn, Wipe, Caret,
        MenuOpen, MenuMove, MenuSelect, MenuBack, MenuDeny, Yes, No,
    }

    /// <summary>Chatter (ticks, wipes, carets, detents) is skipped while an alarm or a voice is on; a Cue is lowered; an Accent is the alarm's own companion and never ducked.</summary>
    internal enum VisorSoundClass : byte { Chatter, Cue, Accent }

    /// <summary>
    /// The visor sound rules, pure (no Unity, so they compile into the unit tests): which clip names and how many variations each sound has, the round-robin pick that never repeats back to back, the +-2 % pitch,
    /// and <see cref="VisorSoundGate"/>, which decides per play whether it may sound and how loud (rate limits and the duck).
    /// </summary>
    internal static class VisorSoundRules
    {
        public const float PitchSpread = 0.02f, DuckedCueGain = 0.5f;
        /// <summary>Chatter classes share one budget: at most 8 per second.</summary>
        public const float ChatterGap = 1f / 8f;
        public const float GlitchGap = 0.15f;

        private static readonly string[] Names =
        {
            "visor-powerup", "visor-resync", "visor-tick", "visor-ok", "visor-fault", "visor-align", "visor-online", "visor-glitch", "visor-emp", "visor-warn", "visor-wipe", "visor-caret",
            "visor-menu-open", "visor-menu-move", "visor-menu-select", "visor-menu-back", "visor-menu-deny", "visor-yes", "visor-no",
        };
        private static readonly byte[] Counts = { 2, 1, 3, 1, 1, 1, 1, 3, 1, 1, 2, 2, 1, 3, 1, 1, 1, 1, 1 };

        public static string Name(VisorClip c) => Names[(int)c];
        public static int Variants(VisorClip c) => Counts[(int)c];

        public static VisorSoundClass ClassOf(VisorClip c)
        {
            switch (c)
            {
                case VisorClip.Tick: case VisorClip.Wipe: case VisorClip.Caret: case VisorClip.MenuMove: return VisorSoundClass.Chatter;
                case VisorClip.Warn: return VisorSoundClass.Accent;
                default: return VisorSoundClass.Cue;
            }
        }

        /// <summary>The least time between two plays of the same sound.</summary>
        public static float MinGap(VisorClip c) =>
            c == VisorClip.Glitch ? GlitchGap : c == VisorClip.Ok ? 0.12f : c == VisorClip.Emp ? 0.28f : c == VisorClip.Warn ? 0.45f : ClassOf(c) == VisorSoundClass.Chatter ? ChatterGap : 0.05f;

        /// <summary>The variation to play: never the one just played (when there are two or more), picked by <paramref name="seed"/>.</summary>
        public static int NextVariant(int last, int count, uint seed)
        {
            if (count <= 1) return 0;
            int step = 1 + (int)(Mix(seed) % (uint)(count - 1));
            return ((last < 0 ? -1 : last) + step + count) % count;
        }

        /// <summary>Playback pitch, 1 +- 2 %.</summary>
        public static float Pitch(uint seed) => 1f + ((Mix(seed + 0x9e3779b9u) & 0xFFFF) / 32767.5f - 1f) * PitchSpread;

        private static uint Mix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }
    }

    /// <summary>
    /// Whether a visor sound may play now and its duck gain (1 normal, <see cref="VisorSoundRules.DuckedCueGain"/> lowered, 0 never). Rules: no sound twice inside its own gap; every chatter sound shares the 8-a-second budget;
    /// chatter is skipped, a cue lowered, while <c>busy</c> (a real warning alarm or a voice line is sounding).
    /// </summary>
    internal sealed class VisorSoundGate
    {
        private readonly float[] last = new float[Enum.GetValues(typeof(VisorClip)).Length];
        private float lastChatter = float.NegativeInfinity;

        public VisorSoundGate() { Reset(); }

        public void Reset()
        {
            for (int i = 0; i < last.Length; i++) last[i] = float.NegativeInfinity;
            lastChatter = float.NegativeInfinity;
        }

        /// <summary>True when <paramref name="clip"/> may sound at <paramref name="now"/> (unscaled seconds); <paramref name="gain"/> is the duck. A refused sound leaves no trace (it does not push the gap).</summary>
        public bool TryPass(VisorClip clip, float now, bool busy, out float gain)
        {
            gain = 1f;
            VisorSoundClass c = VisorSoundRules.ClassOf(clip);
            if (now - last[(int)clip] < VisorSoundRules.MinGap(clip)) return false;
            if (c == VisorSoundClass.Chatter)
            {
                if (busy || now - lastChatter < VisorSoundRules.ChatterGap) return false;
                lastChatter = now;
            }
            else if (c == VisorSoundClass.Cue && busy) gain = VisorSoundRules.DuckedCueGain;
            last[(int)clip] = now;
            return true;
        }
    }
}
