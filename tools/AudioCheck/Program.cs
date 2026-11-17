using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Util;
using NOAvionics;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static byte[] Wav() { using var stream = typeof(Program).Assembly.GetManifestResourceStream("BoscaliSummer.Sfx.fixture-valid.wav"); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray(); }
    static void U32(byte[] b, int at, uint n) { for (int i = 0; i < 4; i++) b[at+i] = (byte)(n >> (i*8)); }
    public static void Main()
    {
        byte[] wav = Wav();
        float[] samples = PcmWav.Decode(wav, 22050);
        Check(samples is { Length: 4 } && samples[0] == -1f && samples[1] == 0f && samples[2] == .5f, "PCM values decode exactly");
        Check(PcmWav.Decode(wav, 44100) == null, "wrong sample rate rejected");
        Check(PcmWav.Decode(wav[..^1], 22050) == null, "truncated RIFF rejected");
        byte[] wrongLength = (byte[])wav.Clone(); U32(wrongLength, 4, (uint)wav.Length);
        Check(PcmWav.Decode(wrongLength, 22050) == null, "incorrect RIFF size rejected");
        byte[] wrongAlign = (byte[])wav.Clone(); wrongAlign[32] = 4;
        Check(PcmWav.Decode(wrongAlign, 22050) == null, "incorrect PCM block alignment rejected");
        byte[] wrongByteRate = (byte[])wav.Clone(); U32(wrongByteRate, 28, 0);
        Check(PcmWav.Decode(wrongByteRate, 22050) == null, "incorrect PCM byte rate rejected");
        byte[] odd = wav[..46]; U32(odd, 4, 38); U32(odd, 40, 1);
        Check(PcmWav.Decode(odd, 22050) == null, "odd PCM data rejected");
        Check(PcmWav.Decode(null, 22050) == null && PcmWav.Decode(wav, 0) == null, "invalid PCM input rejected");

        Check(SfxClips.KnownCount == 3, "only stable Sfx resource IDs indexed");
        AudioClip first = SfxClips.Get("fixture-valid");
        Check(first != null && first.frequency == 22050 && first.channels == 1, "real loader creates mono clip");
        Check(SfxClips.DecodedBytes == 16 && SfxClips.DecodeCount == 1, "loader accounts retained PCM bytes");
        Check(ReferenceEquals(first, SfxClips.Get("fixture-valid")) && SfxClips.DecodeCount == 1, "cache hit does not decode again");
        int before = SfxClips.CachedCount;
        foreach (string id in new[] { null, "../fixture-valid", "BAD", "not-shipped" }) Check(SfxClips.Get(id) == null, "unknown ID rejected");
        Check(SfxClips.CachedCount == before && SfxClips.RejectedCount == 4, "unknown IDs never grow negative cache");
        Check(SfxClips.Get("fixture-invalid") == null && SfxClips.Get("fixture-invalid") == null && SfxClips.FailedCount == 1, "bad known clip checked once");
        UnityEngine.Object.Destroy(first);
        AudioClip reloaded = SfxClips.Get("fixture-valid");
        Check(reloaded != null && !ReferenceEquals(first, reloaded) && SfxClips.DecodedBytes == 16, "destroyed borrowed wrapper reloads without double accounting");

        AvUiSound.Recorded = null;
        int created = AudioClip.Created;
        AvUiSound.Play(AvUiCue.Press);
        Check(AudioClip.Created == created && AudioSource.Played == 0, "missing UI provider never synthesizes a fallback");
        int requested = 0, routed = 0;
        AvUiSound.Recorded = _ => { requested++; return reloaded; };
        AvUiSound.Route = source => { source.priority = 40; routed++; };
        AvUiSound.Volume = .7f; Time.unscaledTime = 10f;
        AvUiSound.Play(AvUiCue.Press);
        AvUiSound.Play(AvUiCue.Press);
        Check(requested == 1 && routed == 1 && AudioSource.Played == 1, "UI lazily resolves, routes and rate-limits recorded cue");
        Time.unscaledTime += .1f; AvUiSound.Play(AvUiCue.Confirm);
        Check(requested == 2 && routed == 2 && AudioSource.Played == 2, "distinct recorded UI cue works");
        AvUiSound.Volume = 0; Time.unscaledTime += 1f; AvUiSound.Play(AvUiCue.Open);
        AvUiSound.Play((AvUiCue)100); AvUiSound.Play(AvUiCue.Hover);
        Check(requested == 2 && AudioSource.Played == 2, "muted, hover and invalid UI cues are silent");
        AvUiSound.Reset();
        Check(reloaded != null, "UI reset does not destroy borrowed clip");

        var gate = new VisorSoundGate();
        Check(!gate.TryPass(VisorClip.Tick, 1f, true, out _), "visor chatter yields to warnings");
        Check(gate.TryPass(VisorClip.PowerUp, 1f, true, out float gain) && gain == .5f, "visor cue ducks under warning");
        Check(gate.TryPass(VisorClip.Warn, 1f, true, out gain) && gain == 1f, "warning accent remains clear");
        Check(!gate.TryPass(VisorClip.Warn, 1.1f, false, out _), "warning cue rate limit retained");

        FxVoiceBus.Clear();
        for (int i = 0; i < 4; i++) Check(FxVoiceBus.TryStartLoop("loop-" + i), "loop budget accepts a free slot");
        Check(!FxVoiceBus.TryStartLoop("fifth"), "loop budget rejects over capacity");
        FxVoiceBus.EndLoop("loop-0");
        Check(FxVoiceBus.TryStartLoop("replacement"), "stopped loop immediately releases its slot");
        FxVoiceBus.Clear();
        for (int i = 0; i < 6; i++) Check(FxVoiceBus.TryOneShot(28f), "long effect reserves one-shot slot");
        Check(!FxVoiceBus.TryOneShot(1f), "one-shot budget rejects over capacity");
        Time.unscaledTime += 29f;
        Check(FxVoiceBus.TryOneShot(28f), "elapsed one-shot reservations expire");
        FxVoiceBus.Clear();
        var budget = new Dictionary<string, object>(); FxVoiceBus.Describe(budget);
        Check((int)budget["fxLoops"] == 0 && (int)budget["fxShots"] == 0, "full reset removes stopped mission reservations");

        Environment.SetEnvironmentVariable("NOMODKIT_SIM_SCENARIO", "audio-check");
        var probe = AudioAutomation.Step(new() { ["action"] = "loop", ["id"] = "fixture-valid", ["volume"] = .4f });
        Check((bool)probe["playing"] && (bool)probe["effectsMixer"] && (int)probe["fxLoops"] == 1, "native probe uses real loop budget and effects routing");
        probe = AudioAutomation.Step(new() { ["action"] = "volume", ["volume"] = 0f });
        Check(!(bool)probe["playing"] && (float)probe["gain"] == 0f && (int)probe["fxLoops"] == 0, "zero volume stops probe and releases loop budget");
        AudioAutomation.Step(new() { ["action"] = "loop", ["id"] = "fixture-valid" });
        probe = AudioAutomation.Step(new() { ["action"] = "mute" });
        Check(!(bool)probe["playing"] && (int)probe["fxLoops"] == 0, "mute releases active probe loop");
        probe = AudioAutomation.Step(new() { ["action"] = "loop", ["id"] = "fixture-valid", ["mute"] = true });
        Check(!(bool)probe["accepted"] && (int)probe["fxLoops"] == 0, "muted request never reserves a loop");

        SfxClips.Clear();
        Check(reloaded == null && SfxClips.CachedCount == 0 && SfxClips.DecodedBytes == 0, "shared shutdown releases owned clips and accounting");
        Console.WriteLine($"audio: {checks} checks passed");
    }
}
