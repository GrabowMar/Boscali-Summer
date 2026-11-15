#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    /// <summary>Exercises file loading on a silent, isolated programme using only generated fixtures.</summary>
    public static class RadioLoadProbe
    {
        private sealed class Fixture : IDisposable
        {
            internal RadioProgram Program;
            internal GameObject Host;
            internal string Folder;

            public void Dispose()
            {
                try { Program?.Dispose(); }
                finally
                {
                    if (Host != null) UnityEngine.Object.Destroy(Host);
                    if (Folder != null)
                    {
                        File.Delete(Path.Combine(Folder, "tone.wav"));
                        File.Delete(Path.Combine(Folder, "invalid.wav"));
                        if (Directory.Exists(Folder)) Directory.Delete(Folder);
                    }
                }
            }
        }

        private static Fixture fixture;

        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            string action = Text(args, "action") ?? "state";
            if (action == "begin")
            {
                if (fixture != null) return Failed("probe already exists; clean it up first");
                fixture = new Fixture();
                try
                {
                    fixture.Folder = Path.Combine(BepInEx.Paths.CachePath, "BoscaliSummer", "radio-probe-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(fixture.Folder);
                    WriteTone(Path.Combine(fixture.Folder, "tone.wav"));
                    File.WriteAllBytes(Path.Combine(fixture.Folder, "invalid.wav"), new byte[] { 0, 1, 2, 3 });
                    fixture.Host = new GameObject("BoscaliRadioLoadProbe");
                    RadioLoadProbeRunner runner = fixture.Host.AddComponent<RadioLoadProbeRunner>();
                    fixture.Program = new RadioProgram(runner, fixture.Host, "SyntheticRadioProbe", null);
                    fixture.Program.SetVolume(0f);
                    fixture.Program.SetCrossfade(0.05f);
                }
                catch
                {
                    try { fixture.Dispose(); }
                    finally { fixture = null; }
                    throw;
                }
            }
            if (fixture == null) return Failed("begin the probe first");
            RadioProgram program = fixture.Program;
            bool accepted = true;
            switch (action)
            {
                case "play": accepted = Play("tone"); break;
                case "supersede": accepted = Play("invalid") && Play("tone"); break;
                case "stop": accepted = Play("tone"); program.Stop(); break;
                case "fail": program.Stop(); accepted = Play("invalid"); break;
                case "missing": program.Stop(); accepted = Play("missing"); break;
                case "dispose": program.Dispose(); break;
                case "cleanup":
                    program.Dispose();
                    if (program.ActiveLoads != 0) return Failed("loads are still completing; retry cleanup next frame");
                    try { fixture.Dispose(); }
                    finally { fixture = null; }
                    return new Dictionary<string, object> { ["ok"] = true, ["cleaned"] = true };
            }
            return new Dictionary<string, object>
            {
                ["ok"] = accepted,
                ["state"] = program.State.ToString(),
                ["active_loads"] = program.ActiveLoads,
                ["duration"] = program.Duration,
                ["clip"] = program.Source != null && program.Source.clip != null ? program.Source.clip.name : "",
                ["source_playing"] = program.Source != null && program.Source.isPlaying,
                ["error"] = program.LastError ?? "",
            };
        }

        private static bool Play(string file) => fixture.Program.PlayFile(Path.Combine(fixture.Folder, file + ".wav"), ".wav", file, 0f, false);

        private static void WriteTone(string path)
        {
            const int rate = 8000, samples = rate * 4;
            using (var writer = new BinaryWriter(File.Create(path), Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++) writer.Write((short)(i % 80 < 40 ? 500 : -500));
            }
        }
    }

    internal sealed class RadioLoadProbeRunner : MonoBehaviour { }
}
#endif
