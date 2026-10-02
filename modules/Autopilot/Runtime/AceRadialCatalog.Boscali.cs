using System.Collections.Generic;
using BoscaliSummer.Modules.Autopilot.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// Boscali integrations, each through its owner's narrow contract: the camera mark and
    /// armed support (<see cref="ICameraTargetService"/>), the contributed TGT preset page
    /// (<see cref="IRadialMenuPage"/>), the local radio (<see cref="IRadioRemote"/>) and team
    /// brevity calls (<see cref="IQuickCalls"/>). A missing owner hides its entries.
    /// </summary>
    internal static partial class AceRadialCatalog
    {
        private const int MaxQuickCalls = 12;

        // ------------------------------------------------------------------ SUPPORT

        private static AceRadialAction Support()
        {
            return Branch("support", "SUPPORT", AceIcon.Support)
                .Add(Leaf("call", "CALL AT MARK", AceIcon.Strike, () => Service<ICameraTargetService>()?.CallAtMark(),
                    visible: () => Service<ICameraTargetService>()?.Available == true,
                    enabled: () => Service<ICameraTargetService>()?.CanCallAtMark == true,
                    status: CallStatus))
                .Add(Leaf("clear", "CLEAR MARK", AceIcon.Clear, () => Service<ICameraTargetService>()?.Clear(),
                    visible: () => Service<ICameraTargetService>()?.HasMark == true))
                .WithChildren(ContributedPage);
        }

        private static AceRadialStatus CallStatus()
        {
            ICameraTargetService camera = Service<ICameraTargetService>();
            if (camera == null) return AceRadialStatus.None;
            if (!camera.HasMark) return new AceRadialStatus("NO MARK", AceTone.Caution);
            string armed = camera.ArmedActionName;
            return string.IsNullOrEmpty(armed) ? new AceRadialStatus("NOTHING ARMED", AceTone.Caution)
                : new AceRadialStatus(Upper(armed, 18), AceTone.Active);
        }

        // ------------------------------------------------------------------ RADIO

        private static AceRadialAction Radio()
        {
            return Branch("radio", "RADIO", AceIcon.Radio)
                .Add(Leaf("deck", "MUSIC", AceIcon.Play, () => Service<IRadioRemote>()?.DeckTogglePlayback(),
                    visible: () => Service<IRadioRemote>()?.DeckAvailable == true,
                    status: DeckStatus))
                .Add(Leaf("next", "NEXT TRACK", AceIcon.Next, () => Service<IRadioRemote>()?.DeckNext(),
                    visible: () => Service<IRadioRemote>()?.DeckAvailable == true))
                .Add(Leaf("prev", "PREV TRACK", AceIcon.Previous, () => Service<IRadioRemote>()?.DeckPrevious(),
                    visible: () => Service<IRadioRemote>()?.DeckAvailable == true))
                .Add(Leaf("receiver", "RECEIVER", AceIcon.Power, () => Service<IRadioRemote>()?.ReceiverToggle(),
                    visible: () => Service<IRadioRemote>()?.ReceiverAvailable == true,
                    status: ReceiverStatus))
                .Add(Leaf("seek", "SEEK STATION", AceIcon.Seek, () => Service<IRadioRemote>()?.ReceiverSeek(1),
                    visible: () => Service<IRadioRemote>()?.ReceiverAvailable == true))
                .Add(Leaf("stop", "STOP ALL", AceIcon.Stop, () => Service<IRadioRemote>()?.StopAll(),
                    visible: () => Service<IRadioRemote>() is IRadioRemote r && (r.DeckPlaying || r.ReceiverOn)));
        }

        private static AceRadialStatus DeckStatus()
        {
            IRadioRemote radio = Service<IRadioRemote>();
            if (radio == null) return AceRadialStatus.None;
            return radio.DeckPlaying ? new AceRadialStatus(Upper(radio.DeckTitle, 18), AceTone.Active) : new AceRadialStatus("PAUSED");
        }

        private static AceRadialStatus ReceiverStatus()
        {
            IRadioRemote radio = Service<IRadioRemote>();
            if (radio == null) return AceRadialStatus.None;
            return radio.ReceiverOn ? new AceRadialStatus(Upper(radio.ReceiverStation, 18), AceTone.Active) : new AceRadialStatus("OFF");
        }

        // ------------------------------------------------------------------ COMMS

        /// <summary>Calls in pages of six so a sub-level never packs tighter than 30 degrees.</summary>
        private static AceRadialAction Comms() =>
            Branch("comms", "COMMS", AceIcon.Comms)
                .Add(Branch("calls", "CALLS", AceIcon.Call).WithChildren(() => QuickCalls(0)))
                .Add(Branch("more", "MORE CALLS", AceIcon.Call).WithChildren(() => QuickCalls(CallsPerPage)));

        private const int CallsPerPage = 6;

        private static IEnumerable<AceRadialAction> QuickCalls(int first)
        {
            IQuickCalls calls = Service<IQuickCalls>();
            if (calls == null) yield break;
            int count = System.Math.Min(System.Math.Min(calls.CallCount, MaxQuickCalls), first + CallsPerPage);
            for (int i = first; i < count; i++)
            {
                int index = i;
                string label = calls.CallLabel(index);
                if (string.IsNullOrEmpty(label)) continue;
                yield return Leaf("c" + index, Upper(label), AceIcon.Comms, () => calls.Call(index),
                    enabled: () => calls.CanCall);
            }
        }
    }
}
