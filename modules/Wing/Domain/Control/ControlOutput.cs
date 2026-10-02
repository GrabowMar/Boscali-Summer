using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
// Filled by the flight controller (Task 6) and the ground writer (M3); read by the engine writer.
#pragma warning disable CS0649

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Normalised demands in the Pure sign convention (pitch + nose-up, roll + right, yaw +
    /// nose-right). The engine writer maps them onto the game's ControlInputs.</summary>
    internal struct ControlOutput
    {
        public float Pitch, Roll, Yaw, Throttle, Brake;
        /// <summary>customAxis1: the compound helicopter's pusher (0.5 neutral); set by the rotary pipeline, written by
        /// the engine writer from M2c.</summary>
        public float Aux;
        /// <summary>Airbrake demanded; throttle is exactly 0 while set (the game opens brakes only there).</summary>
        public bool Airbrake;
    }
}
