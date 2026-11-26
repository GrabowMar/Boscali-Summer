using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// The Skylark's page of the C menu (ISkylarkControl): reads the local player's own Skylark and routes drive choices through
    /// SkylarkNet; fan tilt is set on the owner peer directly (it replicates as the aircraft's inputs). Inactive for any other aircraft.
    /// </summary>
    internal sealed class SkylarkControl : MonoBehaviour, ISkylarkControl, ISceneService
    {
        private Aircraft cached;
        private SkylarkDrive drive;
        private SkylarkFlightMode vector;
        private SkylarkMast mast;
        private int battery = -1;
        private string batteryText = "";
        private static readonly string[] Flags = { "", " HEAVY", " BOOST", " DRY" };

        public void ResetForScene()
        {
            cached = null;
            FansHeld = false;
        }

        private bool Local()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft mine) || mine == null || mine.disabled || mine.definition == null || mine.definition.jsonKey != SkylarkTemplate.Key)
                return false;
            if (mine != cached)
            {
                cached = mine;
                drive = mine.GetComponent<SkylarkDrive>();
                vector = mine.GetComponent<SkylarkFlightMode>();
                mast = mine.GetComponent<SkylarkMast>();
            }
            return drive != null && vector != null;
        }

        public bool Active => Local();
        public int Drive => Local() ? (int)drive.Mode : 0;
        public int Vector => Local() ? (int)vector.Mode : 0;
        public bool MastDeployed => Local() && mast != null && mast.Deployed;

        public string Battery
        {
            get
            {
                if (!Local()) return "";
                // One cached line per (charge, warning) pair: BAT 78%, plus HEAVY (turbine alone cannot lift), BOOST or DRY.
                int percent = Mathf.RoundToInt(drive.Charge * 100f), flag = drive.Dry ? 3 : drive.Boost ? 2 : drive.Heavy ? 1 : 0, key = percent * 4 + flag;
                if (key != battery) { battery = key; batteryText = "BAT " + percent + "%" + Flags[flag]; }
                return batteryText;
            }
        }

        public void SetDrive(int mode)
        {
            if (Local() && mode >= 0 && mode <= (int)DriveMode.Hybrid) SkylarkNet.Request(cached, SkylarkNet.DriveAction, (byte)mode);
        }

        public void SetVector(int mode)
        {
            if (Local() && mode >= 0 && mode <= (int)VectorMode.Plane) vector.Set((VectorMode)mode);
        }

        public bool AutopilotOwnsFans { get => FansHeld; set => FansHeld = value; }
        /// <summary>The autopilot is landing the local aircraft (read by SkylarkFlightMode on the owner peer).</summary>
        internal static bool FansHeld;

        public void ToggleMast()
        {
            if (Local() && mast != null) mast.Request(!mast.Deployed);
        }

        /// <summary>A short line on the pilot's action feed.</summary>
        internal static void Say(string text) => SceneSingleton<AircraftActionsReport>.i?.ReportText(text, 3f);
    }
}
