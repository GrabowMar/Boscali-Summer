using BoscaliSummer.Modules.Support.Domain.SpecOps;
using NOAvionics;
using BoscaliSummer.Modules.Support.Presentation.Views;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// The semantic hue of a team state and of a held post, the same on every SPEC OPS surface
    /// (desk, MFD, vanilla map). Only the hue is shared; every surface still writes the state in
    /// words and draws it in its own style.
    /// </summary>
    internal static class FieldTones
    {
        /// <summary>Observation post green, saboteur cell amber, listening post teal, safehouse blue.</summary>
        public static Color Post(FieldMission post) =>
            post == FieldMission.Recon ? RoomPaint.Ready : post == FieldMission.Sabotage ? AvTheme.RailCaution : RoomPaint.Instrument;

        public static Color State(TeamState state)
        {
            switch (state)
            {
                case TeamState.Ready: return RoomPaint.Ready;
                case TeamState.EnRoute: return RoomPaint.Instrument;
                case TeamState.Deciding: return AvTheme.RailCaution;
                case TeamState.OnTask: return AvTheme.RailCaution;
                case TeamState.Holding: return RoomPaint.Ready;
                case TeamState.Recovering: return AvTheme.Dim;
                default: return AvTheme.RailInert;
            }
        }

        /// <summary>The on-task state pulses; everything else is steady.</summary>
        public static Color StatePulsed(TeamState state, float time) =>
            state == TeamState.OnTask ? State(state).WithAlpha(0.7f + 0.3f * Mathf.PingPong(time * 2f, 1f)) : State(state);
    }
}
