using System;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime.Actions
{
    /// <summary>
    /// SAT CAMERA: the OPTICAL bird looks at the aim. The host reads the sky there, refuses at night or with no sky state (RADAR
    /// stays usable), otherwise opens an OPTICAL reveal window sized by cloud and rain. What the camera shows is the world as it
    /// is; what it reveals (and what can be MARKed) is only what that window admits. Not yet a catalogue row: the SAT CAMERA CALL,
    /// its action id and its CALLS/feed control arrive with the feed UI, which registers this action.
    /// </summary>
    internal sealed class SatelliteCameraAction : ISupportAction
    {
        public float BaseCost(in SupportContext context) => 1f; // availability flag; the price is CallSheet x CallPricing

        public SupportResult Execute(in SupportContext context)
        {
            try
            {
                if (context.SpaceTask == null || !context.SpaceTask.CanLaunch) return SupportResult.BirdNotReady;
                int contacts = context.Host.OpenOpticalWindow(context.Owner, context.Target,
                    context.Settings.OpticalSceneRadius.Value, out SupportResult refusal);
                if (contacts < 0) return refusal == SupportResult.Accepted ? SupportResult.SpawnFailed : refusal;
                context.Host.ReportContacts(context.RequestId, contacts);
                return SupportResult.Accepted;
            }
            catch (Exception e)
            {
                context.Logger.LogWarning("[Support] Optical camera task failed: " + e.Message);
                return SupportResult.SpawnFailed;
            }
        }
    }
}
