using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Cinematography.Runtime;
using UnityEngine;

// The two native lookups the stage reads that the director's fixture never needed.
public class Airbase { public Transform center = new(); }
public static class FactionRegistry { public static Dictionary<string, Airbase> airbaseLookup = new(); }

/// <summary>The cutscene camera scripts play: taking the free camera, chaining shots, skipping one it cannot play, and giving the camera back.</summary>
static class StageTests
{
    internal static void Run(Action<bool, string> check)
    {
        var camera = new CameraStateManager();
        SceneSingleton<CameraStateManager>.i = camera;
        camera.transform.position = new Vector3(5, 6, 7);
        camera.mainCamera.fieldOfView = 55;
        camera.desiredFOV = 55;
        Datum.offset = Vector3.zero;
        GameManager.ownship = null;
        MissionManager.IsRunning = true;
        GameplayUI.AllowPauseKeybind = true;
        Time.timeScale = 1;
        Time.unscaledTime = 100;
        var airbase = new Airbase();
        airbase.center.position = new Vector3(1000, 100, 2000);
        FactionRegistry.airbaseLookup["Base"] = airbase;
        var ship = new Unit { NetworkUniqueName = "Ship" };
        ship.transform.position = new Vector3(0, 5, 0);
        UnitRegistry.allUnits.Clear();
        UnitRegistry.allUnits.Add(ship);

        var player = new StagePlayer();
        MethodInfo update = typeof(StagePlayer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        void Frame(float seconds)
        {
            Time.unscaledTime += seconds;
            camera.currentState.UpdateState(camera);
            update.Invoke(player, null);
        }
        bool At(float x, float y, float z, float within = 4f)
        {
            Vector3 p = camera.transform.position;
            return Math.Abs(p.x - x) <= within && Math.Abs(p.y - y) <= within && Math.Abs(p.z - z) <= within;
        }
        bool Home() => camera.currentState == camera.freeState && At(5, 6, 7, 0.01f) && camera.mainCamera.fieldOfView == 55 &&
            camera.allowInputs && GameplayUI.AllowPauseKeybind;

        var shots = new[]
        {
            new StageShot { Kind = StageShotKind.OrbitAirbase, Target = "Base", Radius = 500, Height = 200, FromDegrees = 0, ToDegrees = 90, Seconds = 2, Fov = 40 },
            new StageShot { Kind = StageShotKind.OrbitAirbase, Target = "Nowhere", Radius = 500, Height = 200, Seconds = 2, Fov = 45 },
            new StageShot { Kind = StageShotKind.OrbitUnit, Target = "Ship", Radius = 100, Height = 20, FromDegrees = 0, ToDegrees = 45, Seconds = 2, Fov = 30 },
            new StageShot { Kind = StageShotKind.Fly, X = 0, Y = 100, Z = 0, ToX = 100, ToY = 100, ToZ = 0, LookX = 50, LookY = 0, LookZ = 500, Seconds = 2, Fov = 50 },
        };
        var started = new List<int>();
        int ended = 0;

        check(player.Play(shots, started.Add, () => ended++), "the stage takes the detached free camera");
        check(player.Playing && started.Count == 1 && started[0] == 0 && camera.currentState != camera.freeState && !camera.allowInputs &&
            !GameplayUI.AllowPauseKeybind, "the first shot owns the camera and the pause key is held");
        check(At(1000, 300, 2500) && camera.mainCamera.fieldOfView == 40, "an airbase orbit starts on its first bearing, at its height and field of view");
        check(!player.Play(shots, started.Add, () => ended++), "a second cutscene cannot start over a running one");
        Frame(1);
        check(At(1353.6f, 300, 2353.6f, 8f), "the orbit sweeps round the airbase");
        Frame(1.1f);
        check(At(1500, 300, 2000), "the orbit ends on its last bearing");
        Frame(0.1f);
        check(player.Playing && started.Count == 2 && started[1] == 2 && ended == 0, "a shot whose target is missing is skipped and the next one plays");
        check(At(0, 25, 100) && camera.mainCamera.fieldOfView == 30, "a unit orbit starts round the unit");
        ship.transform.position = new Vector3(50, 5, 0);
        ship.transform.rotation = new Quaternion(0, 1, 0, 0);
        Frame(1);
        check(At(50 + 38.3f, 25, 92.4f, 6f), "a unit orbit travels with the unit and keeps its compass bearings while the unit turns");
        Frame(1.1f);
        Frame(0.1f);
        check(started.Count == 3 && started[2] == 3 && At(0, 100, 0), "the next shot follows without the camera going home in between");
        Frame(1);
        check(At(50, 100, 0), "a fly shot travels between its two points");
        Frame(1.1f);
        check(player.Playing && ended == 0, "the last frame of the last shot is still the cutscene");
        Frame(0.1f);
        check(!player.Playing && ended == 1 && Home(), "after the last shot the camera is free again, where and how it was found");

        check(player.Play(shots, started.Add, () => ended++), "the stage can be used again");
        Frame(0.5f);
        player.Stop();
        check(!player.Playing && ended == 2 && Home(), "a skipped cutscene gives the camera back at once");

        check(player.Play(shots, started.Add, () => ended++), "the stage can be used after a skip");
        camera.SwitchState(camera.freeState);   // the game takes the camera: the player spawned, or opened the map
        update.Invoke(player, null);
        check(!player.Playing && ended == 3 && camera.currentState == camera.freeState && GameplayUI.AllowPauseKeybind,
            "when the game takes the camera the cutscene ends and does not take it back");

        int before = ended;
        check(player.Play(new[] { shots[1] }, started.Add, () => ended++) && !player.Playing && ended == before + 1 && Home(),
            "a cutscene with nothing playable ends at once and leaves the camera alone");
        GameManager.ownship = new Aircraft();
        check(!player.Play(shots, started.Add, () => ended++) && ended == before + 1, "a pilot who is flying keeps their camera");
        GameManager.ownship = null;
        camera.followingUnit = ship;
        check(!player.Play(shots, started.Add, () => ended++), "a camera that is following a unit is not taken");
        camera.followingUnit = null;
        MissionManager.IsRunning = false;
        check(!player.Play(shots, started.Add, () => ended++), "no cutscene outside a running mission");
        MissionManager.IsRunning = true;

        FactionRegistry.airbaseLookup.Clear();
        UnitRegistry.allUnits.Clear();
        SceneSingleton<CameraStateManager>.i = null;
    }
}
