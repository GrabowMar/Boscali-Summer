using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Runtime
{
    /// <summary>Caches native countermeasure reflection at startup; disables access if the private field
    /// changes.</summary>
    internal static class CountermeasureAccess
    {
        private static readonly Dictionary<Type, MethodInfo> firstCountermeasureMethods =
            new Dictionary<Type, MethodInfo>();

        private static FieldInfo stationsField;
        private static bool initialised;

        public static bool Available { get; private set; }

        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;
            stationsField = typeof(CountermeasureManager).GetField(
                "countermeasureStations", BindingFlags.Instance | BindingFlags.NonPublic);
            Available = stationsField != null;

            if (!Available)
                WingLog.Logger.LogWarning(
                    "Countermeasure station access unavailable; panic ECM support is disabled.");
        }

        public static bool TryFindRadarJammer(CountermeasureManager manager, out int index,
                                              out string reason)
        {
            index = -1;
            reason = null;
            if (!initialised) Initialise();
            if (!Available || manager == null)
            {
                reason = "native countermeasure station list is unavailable";
                return false;
            }

            try
            {
                IList stations = stationsField.GetValue(manager) as IList;
                if (stations == null)
                {
                    reason = "native countermeasure station list is unreadable";
                    return false;
                }

                for (int i = 0; i < stations.Count; i++)
                {
                    if (!(FirstCountermeasure(stations[i]) is RadarJammer)) continue;
                    index = i;
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + " - " + e.Message;
                return false;
            }
        }

        /// <summary>Invokes the public GetFirstCountermeasure method on its private station type; caches
        /// the method per type.</summary>
        private static Countermeasure FirstCountermeasure(object station)
        {
            if (station == null) return null;

            Type type = station.GetType();
            if (!firstCountermeasureMethods.TryGetValue(type, out MethodInfo method))
            {
                method = type.GetMethod(
                    "GetFirstCountermeasure", BindingFlags.Instance | BindingFlags.Public);
                firstCountermeasureMethods[type] = method;
            }

            return method?.Invoke(station, null) as Countermeasure;
        }
    }
}
