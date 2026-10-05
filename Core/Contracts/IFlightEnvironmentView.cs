using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>Optional client view of Weather's last completed flight environment. Never an authority.</summary>
    internal interface IFlightEnvironmentView
    {
        bool TryGet(out FlightEnvironmentSnapshot snapshot);
    }

    internal readonly struct FlightEnvironmentSnapshot
    {
        public int FrameIndex { get; }
        public int SceneGeneration { get; }
        public int AircraftInstanceId { get; }
        public int CameraInstanceId { get; }
        public double MissionTimeSeconds { get; }
        public bool IsCockpit { get; }
        public bool ValidWeather { get; }
        public float Precipitation01 { get; }
        public float CloudDensity01 { get; }
        public float Condensation01 { get; }
        public float Exposure01 { get; }
        public float TemperatureC { get; }
        public float DewpointC { get; }
        public Vector3 WindWorldMps { get; }
        public float GustMps { get; }
        public float Turbulence01 { get; }
        public float DirectLightTransmission01 { get; }
        public float CloudShade01 { get; }

        public FlightEnvironmentSnapshot(int frameIndex, int sceneGeneration, int aircraftInstanceId,
            int cameraInstanceId, double missionTimeSeconds, bool isCockpit, bool validWeather,
            float precipitation01, float cloudDensity01, float condensation01, float exposure01,
            float temperatureC, float dewpointC, Vector3 windWorldMps, float gustMps,
            float turbulence01, float directLightTransmission01, float cloudShade01)
        {
            FrameIndex = frameIndex;
            SceneGeneration = sceneGeneration;
            AircraftInstanceId = aircraftInstanceId;
            CameraInstanceId = cameraInstanceId;
            MissionTimeSeconds = missionTimeSeconds;
            IsCockpit = isCockpit;
            ValidWeather = validWeather;
            Precipitation01 = precipitation01;
            CloudDensity01 = cloudDensity01;
            Condensation01 = condensation01;
            Exposure01 = exposure01;
            TemperatureC = temperatureC;
            DewpointC = dewpointC;
            WindWorldMps = windWorldMps;
            GustMps = gustMps;
            Turbulence01 = turbulence01;
            DirectLightTransmission01 = directLightTransmission01;
            CloudShade01 = cloudShade01;
        }

        public bool MatchesView(int frameIndex, int aircraftInstanceId, int cameraInstanceId) =>
            ValidWeather && SceneGeneration > 0 && CameraInstanceId != 0 &&
            AircraftInstanceId == aircraftInstanceId && CameraInstanceId == cameraInstanceId &&
            frameIndex >= FrameIndex && (long)frameIndex - FrameIndex <= 1;
    }
}
