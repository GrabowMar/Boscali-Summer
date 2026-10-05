using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using NOAvionics;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The support net's cockpit line: armed / pending / recent refusal of the CALL flow, then the SPACE notices: a new TASKED post
    /// (not your own) and a changed ENEMY INTENT, each a four-second line with one chime. The notices come only from the faction
    /// mirror (<see cref="SpaceNoticeTracker"/> owns the rate limits); the QUIET setting silences them and nothing else. There is no
    /// map pin: Support has no map-pin seam, and it must not reach into the map module for one.
    /// </summary>
    internal sealed class SupportHudLine : HudLineWidget
    {
        private const string WidgetOwner = "support-net";

        private CallsController calls;
        private SupportManager manager;
        private readonly SpaceNoticeTracker notices = new SpaceNoticeTracker();
        private readonly CyberNoticeTracker cyberNotices = new CyberNoticeTracker();
        private string text, detail, noticeText;
        private float noticeUntil;
        private HudTone tone;
        private C2HudKind noticeKind;

        protected override string Owner => WidgetOwner;
        protected override string ChannelKey => "support";
        protected override string ChannelLabel => "Support net";

        internal void Configure(SupportManager manager, CallsController calls)
        {
            this.manager = manager;
            this.calls = calls;
            DeclareFeed();
        }

        protected override bool WantsLine()
        {
            if (calls == null || manager == null) return false;
            PollNotices();
            if (calls.Armed.HasValue)
            {
                SupportActionId id = calls.Armed.Value;
                text = "CALL ARMED · " + (CallSheet.TryGet(id, out CallRow row) ? row.Label : id.ToString());
                detail = manager.Quote(id).Cost + " CR · " + Aim.Label(calls.AimNow);
                tone = HudTone.Caution;
                return true;
            }
            if (calls.Pending) { text = "CALL PENDING"; detail = ""; tone = HudTone.Info; return true; }
            if (!string.IsNullOrEmpty(calls.LastWords) && calls.LastWords.StartsWith("NEGATIVE")
                && SupportManager.MissionNow() - calls.LastWordsAt < 4f)
            {
                text = calls.LastWords; detail = ""; tone = HudTone.Warning; return true;
            }
            if (noticeText != null && SupportManager.MissionNow() < noticeUntil)
            {
                // The 14 px C2 strip word rides the line's detail row; the words stay the notice's own.
                text = noticeText; detail = C2Words.HudStrip(noticeKind);
                C2Tone strip = C2Words.HudStripTone(noticeKind);
                tone = strip == C2Tone.Danger ? HudTone.Warning : strip == C2Tone.Warn ? HudTone.Caution : HudTone.Info;
                return true;
            }
            return false;
        }

        private void PollNotices()
        {
            SpaceFeedMirror mirror = manager.SpaceMirror;
            bool quiet = manager.Settings != null && manager.Settings.QuietNotices.Value;
            float now = SupportManager.MissionNow();
            // Mission time restarted (a new scene): a notice from the old clock must not linger.
            if (noticeText != null && noticeUntil - now > SpaceNoticeTracker.ToastSeconds + 0.5f) noticeText = null;
            CyberNoticeKind cyber = cyberNotices.Observe(manager.CyberMirror.Known, manager.CyberMirror.State, now, quiet);
            if (cyber != CyberNoticeKind.None)
            {
                noticeText = cyber == CyberNoticeKind.Traced ? "INTRUSION TRACED · EW TRUCK REVEALED" : "NODE HELD · HOLD EFFECT RUNNING";
                noticeKind = cyber == CyberNoticeKind.Traced ? C2HudKind.CyberTraced : C2HudKind.CyberHeld;
                noticeUntil = now + SpaceNoticeTracker.ToastSeconds;
                AvUiSound.Play(cyber == CyberNoticeKind.Traced ? AvUiCue.Caution : AvUiCue.Confirm);
            }
            SpaceNotice notice = notices.Observe(mirror.Known, mirror.State, now, quiet);
            if (notice.Kind == SpaceNoticeKind.None) return;
            noticeText = notice.Text;
            noticeKind = notice.Kind == SpaceNoticeKind.Tasked ? C2HudKind.Tasked : C2HudKind.Intent;
            noticeUntil = now + SpaceNoticeTracker.ToastSeconds;
            AvUiSound.Play(notice.Kind == SpaceNoticeKind.Tasked ? AvUiCue.Confirm : AvUiCue.Navigate);
        }

        protected override void Write(IHudLine line) => line.Set(tone, text, detail, 0f);
    }
}
