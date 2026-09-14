using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Radio.Configuration;
using UnityEngine;
using UnityEngine.Audio;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal sealed class RadioBroadcastFx
    {
        private const int SampleRate = 22050;
        private const int StaticSamples = SampleRate * 3 / 2;
        private const int LevelBufferSize = 64;
        private const int MaximumIdents = 16;

        private readonly GameObject host;
        private readonly Dictionary<string, AudioClip> idents =
            new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly float[] levelBuffer = new float[LevelBufferSize];

        private AudioSource bed;
        private AudioSource accent;
        private AudioClip staticClip;
        private AudioClip squelchClip;
        private AudioMixerGroup mixer;
        private float staticUntil;
        private float identAt = -1f;
        private string identCode;
        private float level;
        private float volume = 1f;
        private bool carrierOn;

        public RadioBroadcastFx(GameObject host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public float Level => level;

        public void SetMixer(AudioMixerGroup group)
        {
            mixer = group;
            if (bed != null) bed.outputAudioMixerGroup = group;
            if (accent != null) accent.outputAudioMixerGroup = group;
        }

        public void SetVolume(float value) => volume = Mathf.Clamp01(value);

        public void SetCarrier(bool on, float value)
        {
            volume = Mathf.Clamp01(value);
            carrierOn = on;
            if (!on) return;
            if (!Ensure()) return;
            bed.volume = 0.33f * volume;
            if (!bed.isPlaying) bed.Play();
        }

        public void Tune(string code, bool carrierNoise, bool ident)
        {
            if (!Ensure()) return;
            accent.PlayOneShot(squelchClip, 0.45f * volume);
            if (carrierNoise)
            {
                bed.volume = 0.4f * volume;
                if (!bed.isPlaying) bed.Play();
                staticUntil = Time.unscaledTime + 0.35f;
            }

            identAt = ident ? Time.unscaledTime + 0.55f : -1f;
            identCode = ident ? code : null;
        }

        public void CarrierBurst(float seconds)
        {
            if (!Ensure()) return;
            bed.volume = 0.32f * volume;
            if (!bed.isPlaying) bed.Play();
            staticUntil = Time.unscaledTime + Mathf.Clamp(seconds, 0.1f, 2f);
        }

        public void Tick()
        {
            if (bed != null)
            {
                if (carrierOn)
                {
                    if (!bed.isPlaying) bed.Play();
                    bed.volume = 0.33f * volume;
                }
                else if (bed.isPlaying && Time.unscaledTime >= staticUntil)
                {
                    bed.Stop();
                }
            }

            if (identAt >= 0f && Time.unscaledTime >= identAt)
            {
                identAt = -1f;
                AudioClip clip = IdentFor(identCode);
                if (clip != null) accent.PlayOneShot(clip, 0.3f * volume);
            }
        }

        public float Sample(AudioSource source)
        {
            if (source == null || !source.isPlaying)
            {
                level = Mathf.MoveTowards(level, 0f, Time.unscaledDeltaTime * 2.5f);
                return level;
            }

            source.GetOutputData(levelBuffer, 0);
            float sum = 0f;
            for (int i = 0; i < levelBuffer.Length; i++) sum += levelBuffer[i] * levelBuffer[i];
            float rms = Mathf.Sqrt(sum / levelBuffer.Length);
            level = Mathf.Lerp(level, Mathf.Clamp01(rms * 4.5f), 0.35f);
            return level;
        }

        public void Silence()
        {
            carrierOn = false;
            if (bed != null) bed.Stop();
            staticUntil = 0f;
            identAt = -1f;
            level = 0f;
        }

        public void Dispose()
        {
            Silence();
            Destroy(staticClip);
            Destroy(squelchClip);
            foreach (AudioClip clip in idents.Values) Destroy(clip);
            idents.Clear();
            if (bed != null) UnityEngine.Object.Destroy(bed);
            if (accent != null) UnityEngine.Object.Destroy(accent);
            bed = null;
            accent = null;
            staticClip = null;
            squelchClip = null;
        }

        public static void ApplyCharacter(AudioSource source, BroadcastFilterMode mode, bool amBand)
        {
            if (source == null) return;
            if (amBand) mode = BroadcastFilterMode.Broadcast;

            AudioLowPassFilter low = source.GetComponent<AudioLowPassFilter>();
            AudioHighPassFilter high = source.GetComponent<AudioHighPassFilter>();
            AudioDistortionFilter crunch = source.GetComponent<AudioDistortionFilter>();

            if (mode == BroadcastFilterMode.Clean)
            {
                if (low != null) low.enabled = false;
                if (high != null) high.enabled = false;
                if (crunch != null) crunch.enabled = false;
                return;
            }

            if (low == null) low = source.gameObject.AddComponent<AudioLowPassFilter>();
            if (high == null) high = source.gameObject.AddComponent<AudioHighPassFilter>();
            if (crunch == null) crunch = source.gameObject.AddComponent<AudioDistortionFilter>();

            low.enabled = true;
            if (mode == BroadcastFilterMode.Light)
            {
                low.cutoffFrequency = 6500f;
                low.lowpassResonanceQ = 0.7f;
                if (high != null) high.enabled = false;
                if (crunch != null) crunch.enabled = false;
                return;
            }

            // Measured AM speech band, with a touch of receiver crunch over it.
            low.cutoffFrequency = 3000f;
            low.lowpassResonanceQ = 1.1f;
            high.enabled = true;
            high.cutoffFrequency = 260f;
            high.highpassResonanceQ = 0.7f;
            crunch.enabled = true;
            crunch.distortionLevel = 0.12f;
        }

        private bool Ensure()
        {
            if (bed != null && accent != null && staticClip != null) return true;
            try
            {
                bed = CreateSource("BoscaliRadio.Carrier");
                accent = CreateSource("BoscaliRadio.Accent");
                staticClip = staticClip ?? BuildStatic();
                squelchClip = squelchClip ?? BuildSquelch();
                bed.clip = staticClip;
                bed.loop = true;
                return staticClip != null && squelchClip != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Radio receiver audio unavailable: " + e.Message);
                return false;
            }
        }

        private AudioSource CreateSource(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(host.transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private static AudioClip BuildStatic()
        {
            var clip = AudioClip.Create("BoscaliRadio.Static", StaticSamples, 1, SampleRate, false);
            if (clip == null) return null;

            var data = new float[StaticSamples];
            var rng = new System.Random(0x5A17);
            float state = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                state += (white - state) * 0.22f;
                data[i] = state * 0.85f;
            }

            // Blend the tail into the head so the loop seam is inaudible.
            const int seam = 220;
            for (int i = 0; i < seam; i++)
            {
                float t = i / (float)seam;
                data[data.Length - seam + i] =
                    data[data.Length - seam + i] * (1f - t) + data[i] * t;
            }

            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildSquelch()
        {
            const int length = SampleRate * 9 / 100;
            var clip = AudioClip.Create("BoscaliRadio.Squelch", length, 1, SampleRate, false);
            if (clip == null) return null;

            var data = new float[length];
            var rng = new System.Random(0x1C0DE);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                float noise = white * Mathf.Exp(-t * 90f);
                float click = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 420f) * 0.4f;
                data[i] = (noise + click) * 0.5f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip IdentFor(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (idents.TryGetValue(code, out AudioClip cached)) return cached;

            AudioClip built = BuildIdent(code);
            if (built == null) return null;
            if (idents.Count >= MaximumIdents)
            {
                // Codes are two letters at most; anything past the cap is a pathological
                // caller, so the newest ident simply does not get cached.
                idents.Clear();
            }
            idents[code] = built;
            return built;
        }

        private static AudioClip BuildIdent(string code)
        {
            var marks = new List<(float Start, float Length)>();
            float cursor = 0f;
            const float dot = 0.08f;
            const float dash = 0.24f;
            const float gap = 0.08f;
            const float letterGap = 0.24f;

            int letters = 0;
            for (int i = 0; i < code.Length && letters < 4; i++)
            {
                char c = char.ToUpperInvariant(code[i]);
                string morse = Morse(c);
                if (morse == null) continue;
                for (int m = 0; m < morse.Length; m++)
                {
                    float length = morse[m] == '-' ? dash : dot;
                    marks.Add((cursor, length));
                    cursor += length + gap;
                }
                cursor += letterGap;
                letters++;
            }
            if (marks.Count == 0) return null;

            int total = Mathf.CeilToInt(cursor * SampleRate) + SampleRate / 20;
            var clip = AudioClip.Create("BoscaliRadio.Ident." + code, total, 1, SampleRate, false);
            if (clip == null) return null;

            var data = new float[total];
            for (int m = 0; m < marks.Count; m++)
            {
                int start = Mathf.RoundToInt(marks[m].Start * SampleRate);
                int count = Mathf.RoundToInt(marks[m].Length * SampleRate);
                for (int i = 0; i < count && start + i < data.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    float ramp = Mathf.Min(1f, i / (SampleRate * 0.005f));
                    ramp = Mathf.Min(ramp, (count - i) / (SampleRate * 0.005f));
                    data[start + i] += Mathf.Sin(2f * Mathf.PI * 640f * t) * 0.5f * Mathf.Clamp01(ramp);
                }
            }

            clip.SetData(data, 0);
            return clip;
        }

        private static string Morse(char c)
        {
            switch (c)
            {
                case 'A': return ".-";
                case 'B': return "-...";
                case 'C': return "-.-.";
                case 'D': return "-..";
                case 'E': return ".";
                case 'F': return "..-.";
                case 'G': return "--.";
                case 'H': return "....";
                case 'I': return "..";
                case 'J': return ".---";
                case 'K': return "-.-";
                case 'L': return ".-..";
                case 'M': return "--";
                case 'N': return "-.";
                case 'O': return "---";
                case 'P': return ".--.";
                case 'Q': return "--.-";
                case 'R': return ".-.";
                case 'S': return "...";
                case 'T': return "-";
                case 'U': return "..-";
                case 'V': return "...-";
                case 'W': return ".--";
                case 'X': return "-..-";
                case 'Y': return "-.--";
                case 'Z': return "--..";
                case '0': return "-----";
                case '1': return ".----";
                case '2': return "..---";
                case '3': return "...--";
                case '4': return "....-";
                case '5': return ".....";
                case '6': return "-....";
                case '7': return "--...";
                case '8': return "---..";
                case '9': return "----.";
                default: return null;
            }
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value != null) UnityEngine.Object.Destroy(value);
        }
    }
}
