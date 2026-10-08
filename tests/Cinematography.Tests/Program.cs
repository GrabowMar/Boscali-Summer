using System.Numerics;
using BoscaliSummer.Modules.Cinematography;
using BoscaliSummer.Modules.Cinematography.Domain;

static class Program
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static ShotPlan Plan() => new ShotPlan { id = "shot01", mission = "TestMission", scene = "TestScene", duration = 10,
        keys = new[] { new ShotKey { time = 0, qW = 1, fov = 40 }, new ShotKey { time = 10, x = 100, qW = 1, fov = 60 } } };
    static void Main()
    {
        var shot = Plan();
        Check(ShotPlan.Validate(shot, out _), "valid global shot rejected");
        Check(shot.Sample(5).Position == new Vector3(50, 0, 0), "linear midpoint");
        Check(shot.Sample(-1).Position == Vector3.Zero && shot.Sample(20).Position.X == 100, "clamped endpoints");
        Check(shot.Sample(5).Fov == 50, "FOV interpolation");
        shot.smooth = true;
        Check(shot.Sample(2.5f).Position.X < 25, "smooth path did not ease");
        shot.keys[1].qW = -1;
        Check(Math.Abs(shot.Sample(5).Rotation.W) > .99f, "equivalent quaternion signs");
        shot.keys[1].time = 0; Check(!ShotPlan.Validate(shot, out _), "duplicate time");
        shot = Plan(); shot.keys[0].x = float.NaN; Check(!ShotPlan.Validate(shot, out _), "NaN pose");
        shot = Plan(); shot.keys[0].qW = 0; Check(!ShotPlan.Validate(shot, out _), "zero quaternion");
        shot = Plan(); shot.keys[0].fov = 100; Check(!ShotPlan.Validate(shot, out _), "FOV bounds");
        shot = Plan(); shot.id = "../escape"; Check(!ShotPlan.Validate(shot, out _), "unsafe token");
        shot = Plan(); shot.duration = float.PositiveInfinity; Check(!ShotPlan.Validate(shot, out _), "infinite duration");
        shot = Plan(); shot.keys = new ShotKey[65]; Check(!ShotPlan.Validate(shot, out _), "key cap");
        shot = Plan(); shot.keys = new[] { shot.keys[0] }; Check(ShotPlan.Validate(shot, out _), "single fixed pose");
        Check(shot.Sample(5).Position == Vector3.Zero, "fixed shot samples");
        var take = new TakeLog("take01", Plan(), "test-build");
        Check(take.Mark("release", 2), "bookmark accepted");
        Check(!take.Mark("late", -1) && !take.Mark("bad", float.NaN), "bad bookmark clock");
        for (int i = 1; i < TakeLog.BookmarkLimit; i++) Check(take.Mark("mark", 2), "bookmark capacity too small");
        Check(!take.Mark("overflow", 2), "bookmark capacity overflow");
        take.Finish("Aborted", "focus lost", 3);
        Check(!take.Mark("after", 3) && take.bookmarks.Length == 512 && take.status == "Aborted", "closed take immutable");
        Check(take.recorder == "UNKNOWN", "recorder status fabricated");
        var folder = Path.Combine(Path.GetTempPath(), "BoscaliCinematics-" + Guid.NewGuid().ToString("N"));
        var store = new ShotStore(folder);
        try
        {
            Check(store.Save(Plan(), out _), "save valid shot");
            Check(store.Load("shot01", out var loaded, out _) && loaded.Sample(5).Position.X == 50, "round trip");
            Check(!store.Load("../escape", out _, out _), "path traversal load");
            var invalid = Plan(); invalid.duration = -5;
            Check(!store.Save(invalid, out _), "invalid save accepted");
            Check(store.Load("shot01", out loaded, out _) && loaded.duration == 10, "invalid save damaged previous file");
            File.WriteAllText(Path.Combine(folder, "shot01.json"), "{\"version\":999}");
            Check(!store.Load("shot01", out _, out _), "future version load");
            File.WriteAllText(Path.Combine(folder, "shot01.json"), "[");
            Check(!store.Load("shot01", out _, out _), "malformed JSON load");
            File.WriteAllText(Path.Combine(folder, "shot01.json"), new string('x', ShotStore.FileLimit + 1));
            Check(!store.Load("shot01", out _, out _), "oversized load");
            Check(store.SaveTake(take, out _) && File.Exists(Path.Combine(folder, "Takes", "take01.json")), "take export");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        CameraLifecycleTests.Run(Check);
        EditorTests.Run(Check);
        TimingTests.Run(Check);
        ApiTests.Run(Check);
        InboxTests.Run(Check);
        DemoTests.Run(Check);
        Console.WriteLine($"PASS: {checks} cinematography assertions");
    }
}
