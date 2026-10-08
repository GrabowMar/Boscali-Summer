using NOAvionics;
using BoscaliSummer.Modules.Comms.Domain;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    internal sealed partial class CommsMfdPanel
    {
        private static readonly AvIcon[] CallIcons =
        {
            AvIcon.ArrowUpRight, AvIcon.AlertTriangle, AvIcon.Target, AvIcon.Radar2, AvIcon.AlertCircle,
            AvIcon.Clock, AvIcon.ArrowBackUp, AvIcon.CircleCheck, AvIcon.InfoCircle, AvIcon.X, AvIcon.Heart, AvIcon.Flag,
        };

        private void BuildCommsPage(AvFlow p)
        {
            p.Section(AvIcon.Message2, "BREVITY CALLS", "ALWAYS TO TEAM");
            var callSpecs = new AvControl.Spec[CommsCatalog.Calls.Length];
            var callHelps = new string[callSpecs.Length];
            for (int i = 0; i < callSpecs.Length; i++)
            {
                int call = i;
                BrevityCall brevity = CommsCatalog.Calls[i];
                callSpecs[i] = new AvControl.Spec(brevity.Code, () => comms.Call(call), ToneStyle(brevity.Tone), CallIcons[i % CallIcons.Length]);
                callHelps[i] = brevity.Code + ": " + brevity.Meaning +
                    (brevity.MarksPosition ? " Also drops a ping at your aircraft." : "") +
                    " Always goes to your team.";
            }
            ButtonGrid(p, callSpecs, 3, callHelps);

            BuildLogPage(p);
        }
    }
}
