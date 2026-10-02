using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
// Unity calls Awake by reflection.
#pragma warning disable IDE0051

namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Live snapshot for nomodkit <c>bridge_find</c>/<c>bridge_inspect</c> (spec §8). Lives on the
    /// runtime object; <see cref="DevService"/> refreshes it at 2 Hz while dev tools are on.</summary>
    internal sealed class BridgeState : MonoBehaviour
    {
        public static BridgeState Instance { get; private set; }

        public string Summary = "";
        public string Autopilot = "";
        public string[] Members = new string[FormationCatalog.MaxSlots];
        public double AiMsPerFrame;

        private void Awake() => Instance = this;
    }
}
