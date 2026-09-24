using System;
using BoscaliSummer.Core;
using BoscaliSummer.Tests.Architecture;
using BoscaliSummer.Tests.Features.Command;
using BoscaliSummer.Tests.Features.FireAndDestruction;
using BoscaliSummer.Tests.Features.Hud;
using BoscaliSummer.Tests.Features.Radio;
using BoscaliSummer.Tests.Features.Progression;
using BoscaliSummer.Tests.Features.Support;
using BoscaliSummer.Tests.Features.Trenches;
using BoscaliSummer.Tests.Features.UrbanCombat;
using BoscaliSummer.Tests.Framework;

namespace BoscaliSummer.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            FrameworkTests.Run();
            ImpactScorchTests.Run();
            FireScorchTests.Run();
            CookoffTests.Run();
            HitEscalationTests.Run();
            GroundSnapTests.Run();
            CloudDeckTests.Run();
            ForestIndexTests.Run();
            TroopDeploymentTests.Run();
            GarrisonMarkerInfoTests.Run();
            StrongpointHitPolicyTests.Run();
            SiegeMathTests.Run();
            UrbanRuinMathTests.Run();
            WarzoneDressingMathTests.Run();
            UrbanAmbienceMathTests.Run();
            RadioTests.Run();
            Features.Squad.AceCareerTests.Run();
            ProgressionTests.Run();
            PlaneEngineMapTests.Run();
            EmblemTests.Run();
            PilotStudioTests.Run();
            ProgressionPresentationTests.Run();
            SupportTests.Run();
            SpecOpsDetachmentTests.Run();
            OpsLayoutTests.Run();
            Features.QoL.ObservationTests.Run();
            Features.PlayerSpawnPriority.PlayerSpawnPriorityTests.Run();
            Features.Hud.CameraTests.Run();
            Features.Hud.TargetBoardTests.Run();
            Features.Hud.ShotTests.Run();
            Features.Autopilot.AutopilotLandTests.Run();
            CommandTests.Run();
            ThreatEnvelopeTests.Run();
            MfdNewsTickerTests.Run();
            MfdSecondaryObjectivesTests.Run();
            MfdMissionOverviewTests.Run();
            MfdMissionLabelsTests.Run();
            Features.DynamicOperations.OperationTests.Run();
            Features.DynamicOperations.OperationTitlesTests.Run();
            Features.DynamicOperations.OperationDirectorTests.Run();
            Features.DynamicOperations.OperationMarkerCopyTests.Run();
            Features.DynamicOperations.ContractMarkerTests.Run();
            Features.HighCommand.HighCommandTests.Run();
            Features.TheaterOps.PriorityTests.Run();
            Features.TheaterOps.LogisticsTests.Run();
            Features.TheaterOps.OffensiveTests.Run();
            Features.TheaterOps.FrontlineTacticsTests.Run();
            Features.TheaterOps.InfluenceTests.Run();
            Features.TheaterOps.DirectorDecisionTests.Run();
            Features.TheaterOps.DirectorScenarioTests.Run();
            Features.TheaterOps.StaffLogTests.Run();
            Features.Events.EventSelectorTests.Run();
            Features.Events.EventDirectorTests.Run();
            Features.Comms.CommsTests.Run();
            Features.Campaign.MissionInstallPlanTests.Run();
            HudTests.Run();
            Features.Support.SupportHudCopyTests.Run();
            Features.Autopilot.AutopilotHudCopyTests.Run();
            Features.QoL.FuelHudCopyTests.Run();
            Features.Progression.AceHuntHudCopyTests.Run();
            Features.Radio.RadioHudCopyTests.Run();
            Features.TheaterOps.TheaterOpsHudCopyTests.Run();
            TrenchTests.Run();
            Features.Weather.WeatherRegimeTests.Run();
            Features.Weather.WeatherForecastTests.Run();
            Features.Weather.WeatherDebugTests.Run();
            Features.Weather.RainAudioMathTests.Run();
            Features.Weather.RainVisualMathTests.Run();
            Features.Weather.CanopyScoringTests.Run();
            Features.Weather.RainSkyMathTests.Run();
            Features.Visuals.VisualsTests.Run();
            ModuleBoundaryTests.Run();

            TestAssert.That(
                Deterministic.Hash(1, 2, 3, 4) == Deterministic.Hash(1, 2, 3, 4),
                "hash must be stable");
            TestAssert.That(
                Deterministic.Hash(1, 2, 3, 4) != Deterministic.Hash(1, 2, 3, 5),
                "salt must affect hash");
            TestAssert.That(
                Deterministic.HashString("Airbase Alpha") == Deterministic.HashString("Airbase Alpha"),
                "string hash must be stable");
            TestAssert.That(
                Deterministic.HashString("Airbase Alpha") != Deterministic.HashString("Airbase Bravo"),
                "names must separate seeds");

            for (int i = -1000; i <= 1000; i++)
            {
                float value = Deterministic.UnitFloat(Deterministic.Hash(i, i * 7, -i));
                TestAssert.That(value >= 0f && value < 1f, "unit float outside [0,1)");
            }

            TestAssert.That(
                Deterministic.CellKey(0f, 0f, 32f) == Deterministic.CellKey(31.99f, 31.99f, 32f),
                "same positive cell split");
            TestAssert.That(
                Deterministic.CellKey(-0.01f, -0.01f, 32f) ==
                Deterministic.CellKey(-31.99f, -31.99f, 32f),
                "negative floor cell split");
            TestAssert.That(
                Deterministic.CellKey(-0.01f, 0f, 32f) != Deterministic.CellKey(0f, 0f, 32f),
                "negative and positive cells collided");

            Console.WriteLine("BoscaliSummer.Tests: all module, framework, and architecture assertions passed.");
            return 0;
        }
    }
}
