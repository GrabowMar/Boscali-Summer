using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Framework.Features;
using UnityEngine;
namespace BoscaliSummer.Features.Hud.Presentation
{
    // Service and lifecycle only. Feed handles contain data; the view owns a fixed widget pool.
    internal sealed class HudBoard : MonoBehaviour, ISceneService, IHudBoard
    {
        private readonly HudFeedStore store = new HudFeedStore(() => Time.unscaledTime);
        private readonly List<Channel> channels = new List<Channel>(HudLayout.MaxChannels);
        private readonly HudMessage[] snapshot = new HudMessage[HudLayout.MaxRows];
        private HudSettings settings;
        private StatusFeedView view;
        private float nextRead;
        public void Configure(HudSettings config, ManualLogSource logger) => settings = config;
        public IReadOnlyList<IHudChannel> Channels => channels;
        public void DeclareChannel(string key, string label)
        {
            if (string.IsNullOrEmpty(key) || channels.Count >= HudLayout.MaxChannels) return;
            foreach (Channel channel in channels) if (channel.Key == key) return;
            channels.Add(new Channel(this, key, string.IsNullOrEmpty(label) ? key : label));
        }
        public IHudLine Acquire(string owner, string channel, string key) => store.Acquire(owner, channel, key);
        public void Notice(string channel, HudTone tone, string text, string detail = null)
        {
            if (NoticesEnabled && ChannelEnabled(channel)) store.Notice(channel, tone, text, detail, NoticeSeconds);
        }
        private bool ChannelEnabled(string channel) => settings == null || settings.ChannelEnabled(channel);
        public bool Enabled
        {
            get => settings == null || settings.Enabled.Value;
            set
            {
                if (settings != null) settings.Enabled.Value = value;
            }
        }

        public HudAnchor Anchor
        {
            get => settings != null ? settings.ResolvedAnchor() : HudAnchor.UnderWeapons;
            set
            {
                if (settings == null) return;
                settings.Anchor.Value = HudLayout.ClampAnchor((int)value);
            }
        }

        public int ScaleStep
        {
            get => settings != null ? HudLayout.ClampScale(settings.ScaleStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.ScaleStep.Value = HudLayout.ClampScale(value);
            }
        }

        public int OpacityStep
        {
            get => settings != null ? HudLayout.ClampOpacity(settings.OpacityStep.Value) : 1;
            set
            {
                if (settings == null) return;
                settings.OpacityStep.Value = HudLayout.ClampOpacity(value);
            }
        }

        public int MaxRows
        {
            get => settings != null ? HudLayout.ClampRows(settings.MaxRows.Value) : 4;
            set
            {
                if (settings == null) return;
                settings.MaxRows.Value = HudLayout.ClampRows(value);
            }
        }

        public bool NoticesEnabled
        {
            get => settings == null || settings.Notices.Value;
            set
            {
                if (settings == null || settings.Notices.Value == value) return;
                settings.Notices.Value = value;
                if (!value) store.ClearNotices();
            }
        }

        public float NoticeSeconds
        {
            get => settings != null
                ? HudLayout.ClampNoticeSeconds(settings.NoticeSeconds.Value)
                : HudLayout.DefaultNoticeSeconds;
            set
            {
                if (settings == null) return;
                settings.NoticeSeconds.Value = HudLayout.ClampNoticeSeconds(value);
            }
        }

        public int Contrast { get => settings?.Contrast.Value ?? 1; set { if (settings != null) settings.Contrast.Value = Mathf.Clamp(value, 0, 2); } }
        public bool ShowDetails { get => settings == null || settings.ShowDetails.Value; set { if (settings != null) settings.ShowDetails.Value = value; } }
        public int OffsetX { get => settings?.OffsetX.Value ?? 0; set { if (settings != null) settings.OffsetX.Value = Mathf.Clamp(value, -600, 600); } }
        public int OffsetY { get => settings?.OffsetY.Value ?? 0; set { if (settings != null) settings.OffsetY.Value = Mathf.Clamp(value, -600, 600); } }
        public void ResetLayout()
        {
            Enabled = ShowDetails = NoticesEnabled = true;
            Anchor = HudAnchor.UnderWeapons;
            ScaleStep = Contrast = 1;
            OpacityStep = 0;
            MaxRows = 4;
            OffsetX = OffsetY = 0;
            NoticeSeconds = HudLayout.DefaultNoticeSeconds;
        }


        private void LateUpdate()
        {
            if (settings == null) return;
            // Visibility is frame-driven; a paused/map-open frame never waits for the data tick.
            if (!Enabled || !CanShow() || HudLayout.Opacity(OpacityStep) <= 0) { view?.Hide(); return; }
            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .1f;
            foreach (Channel channel in channels) if (!channel.Enabled) store.Mute(channel.Key);
            int count = store.Snapshot(snapshot, MaxRows, ChannelEnabled, NoticesEnabled);
            if (count == 0) { view?.Hide(); return; }
            if (view == null) view = new StatusFeedView(transform);
            HudBounds avoid = ModServices.TryGet(out IThirdPersonHud external) ? external.InstrumentBounds : default;
            view.Present(snapshot, count, settings, avoid);
        }
        private static bool CanShow()
        {
            if (Application.isBatchMode || DynamicMap.mapMaximized || GameplayUI.GameIsPaused || PlayerSettings.cinematicMode) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null || aircraft.disabled || aircraft.HasEjected()) return false;
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            return camera != null && camera.followingUnit == aircraft &&
                (camera.currentState == camera.cockpitState || camera.currentState == camera.orbitState || camera.currentState == camera.chaseState);
        }
        public void ResetForScene() { store.Reset(); view?.Destroy(); view = null; nextRead = 0; }
        private void OnDisable() => view?.Hide();
        private void OnDestroy() => ResetForScene();
        private sealed class Channel : IHudChannel
        {
            private readonly HudBoard board;

            internal Channel(HudBoard owner, string key, string label)
            {
                board = owner;
                Key = key;
                Label = label;
            }

            public string Key { get; }
            public string Label { get; }
            public bool Enabled => board.settings == null || board.settings.ChannelEnabled(Key);

            public void Toggle()
            {
                if (board.settings == null) return;
                board.settings.SetChannel(Key, !Enabled);
            }
        }
    }
}
