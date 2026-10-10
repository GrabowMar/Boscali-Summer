using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>Adapts Vanguard's remaining held-status publishers to the TAC BOARD gauge row.</summary>
    internal sealed class VanguardHudFeeds : MonoBehaviour, ISceneService
    {
        private const int Capacity = 32;
        private static VanguardHudFeeds instance;
        private readonly Dictionary<string, Channel> channels = new Dictionary<string, Channel>();
        private readonly List<Line> lines = new List<Line>(Capacity);
        private void Awake() => instance = this;
        private void OnDestroy() { ResetForScene(); if (instance == this) instance = null; }

        public void ResetForScene()
        {
            foreach (Channel channel in channels.Values) ScopeGauges.Unregister(channel.Id, channel.Provider);
            channels.Clear();
            foreach (Line line in lines) line.Release();
            lines.Clear();
        }

        public static void DeclareChannel(string id, string label)
        {
            if (instance == null || Dedicated(id) || instance.channels.ContainsKey(id) || instance.channels.Count >= Capacity) return;
            var channel = new Channel { Id = id, Label = label };
            channel.Provider = channel.Read;
            instance.channels.Add(id, channel);
            ScopeGauges.Register(id, channel.Provider);
        }

        // These publishers already have dedicated typed providers; their held handles remain liveness signals.
        private static bool Dedicated(string channel) => channel == "lance" || channel == "vg.aegis" || channel == "vg.remora" || channel == "vg.skywell";

        public static IHudLine Acquire(IHudBoard board, string owner, string channel, string key)
        {
            if (Dedicated(channel)) return board.Acquire(owner, channel, key);
            if (instance == null || !instance.channels.TryGetValue(channel, out Channel feed)) return null;
            Line stale = null;
            foreach (Line line in instance.lines)
            {
                if (line.Owner == owner && line.Key == key) return line;
                if (!line.Live && stale == null) stale = line;
            }
            if (instance.lines.Count >= Capacity)
            {
                if (stale == null) return null;
                stale.Channel.Lines.Remove(stale);
                instance.lines.Remove(stale);
            }
            var created = new Line { Owner = owner, Key = key, Channel = feed };
            instance.lines.Add(created);
            feed.Lines.Add(created);
            return created;
        }

        private sealed class Channel
        {
            public string Id, Label;
            public GaugeProvider Provider;
            public readonly List<Line> Lines = new List<Line>();
            public bool Read(out GaugeSpec gauge)
            {
                gauge = default;
                Line best = null;
                foreach (Line line in Lines)
                    if (line.Live && (best == null || line.Tone > best.Tone || line.Tone == best.Tone && line.At > best.At)) best = line;
                if (best == null) return false;
                gauge = new GaugeSpec { Id = Id, Label = Label, Kind = GaugeKind.Mode, Value = best.Bar, Text = best.Text, Sub = best.Detail,
                    Tone = best.Tone == HudTone.Warning ? ScopeTone.Red : best.Tone == HudTone.Caution ? ScopeTone.Amber : ScopeTone.Ink,
                    Rank = best.Tone == HudTone.Warning ? 95 : best.Tone == HudTone.Caution ? 65 : 45 };
                return true;
            }
        }

        private sealed class Line : IHudLine
        {
            public string Owner, Key, Text, Detail;
            public Channel Channel;
            public HudTone Tone;
            public float Bar, At = -99f;
            public bool Live => Time.unscaledTime - At <= 2f;
            private string sourceText, sourceDetail;
            public void Set(HudTone tone, string text, string detail, float bar)
            {
                Tone = tone; Bar = Mathf.Clamp01(bar); At = Time.unscaledTime;
                if (sourceText != text)
                {
                    sourceText = text;
                    string prefix = Channel.Label + " ";
                    string value = text != null && text.StartsWith(prefix, System.StringComparison.Ordinal) ? text.Substring(prefix.Length).TrimStart() : text ?? "";
                    Text = value.Length <= 14 ? value : value.Substring(0, 13) + "…";
                }
                if (sourceDetail != detail)
                {
                    sourceDetail = detail;
                    Detail = detail == null || detail.Length <= 16 ? detail : detail.Substring(0, 15) + "…";
                }
            }
            public void Release() => At = -99f;
        }
    }
}
