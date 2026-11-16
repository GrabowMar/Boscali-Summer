using BoscaliSummer.Modules.Gamemodes.Domain;
using BoscaliSummer.Core.Game;

int assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}

MissionProfile.SetDisabled(Array.Empty<string>());
MissionProfile.SetRequiredDisabled(new[] { "support", "gamemodes" });
Check(!MissionProfile.AllowsModule("support"), "Required mode restriction was ignored.");
Check(MissionProfile.AllowsModule("gamemodes"), "A mode disabled its own rules.");
MissionProfile.SetDisabled(Array.Empty<string>());
Check(!MissionProfile.AllowsModule("support"), "Pilot toggle bypassed required mode restriction.");
MissionProfile.SetRequiredDisabled(null);
Check(MissionProfile.AllowsModule("support"), "Mode restrictions leaked into next ordinary mission.");
Check(GamemodeRules.DetectMode("Fuzzball") == GameModeKind.None, "A title must not opt an unrelated mission into a mode.");
Check(GamemodeRules.DetectMode("Briefing\r\n[BoscaliGamemode:fuzzball]\r\n") == GameModeKind.Fuzzball, "Replicated marker not recognized.");
Check(GamemodeRules.DetectMode("prefix [BoscaliGamemode:fuzzball]") == GameModeKind.None, "Inline marker must not match.");
Check(GamemodeRules.DetectMode("[BoscaliGamemode:fuzzball]\n[BoscaliGamemode:heavyweight]") == GameModeKind.None, "Ambiguous markers must fail closed.");

var dogfight = new GamemodeMatch(GameModeKind.Fuzzball, 10);
Check(!dogfight.TryCreditKill(11, "ground", 0, 1, false), "Ground kills scored.");
Check(!dogfight.TryCreditKill(11, "friendly", 0, 0, true), "Friendly kill scored.");
Check(!dogfight.TryCreditKill(11, "neutral", 0, -1, true), "Neutral kill scored.");
Check(dogfight.TryCreditKill(11, "enemy", 0, 1, true), "Enemy kill failed.");
Check(!dogfight.TryCreditKill(11, "enemy", 0, 1, true), "Duplicate kill scored.");
Check(!dogfight.TryCreditKill(10, "stale", 0, 1, true), "Backwards event time accepted.");
Check(!dogfight.TryCreditKill(double.NaN, "nan", 0, 1, true), "Nonfinite time accepted.");
Check(!dogfight.TryCreditKill(1210, "late", 1, 0, true), "Kill at deadline scored.");
Check(dogfight.Phase == GamemodePhase.Finished && dogfight.Winner == 0 && dogfight.Score(0) == 1, "Timed result incorrect.");

var target = new GamemodeMatch(GameModeKind.Fuzzball, 0);
for (int i = 0; i < GamemodeRules.FuzzballKillTarget; i++) Check(target.TryCreditKill(i, "enemy-" + i, 1, 0, true), "Target kill rejected.");
Check(target.Phase == GamemodePhase.Finished && target.Winner == 1, "Kill target did not finish.");
Check(!target.TryCreditKill(100, "after", 0, 1, true), "Scoring continued after victory.");

var lift = new GamemodeMatch(GameModeKind.Heavyweight, 100);
Check(!lift.TryLoadCargo(101, "bad-side", 1, "A", "B", 10), "Interceptors loaded scoring cargo.");
Check(!lift.TryLoadCargo(101, "same", 0, "A", "A", 10), "Source equals destination accepted.");
Check(!lift.TryLoadCargo(101, "zero", 0, "A", "B", 0), "Empty cargo accepted.");
Check(lift.TryLoadCargo(101, "load", 0, "A", "B", 10), "Source-issued cargo rejected.");
Check(!lift.TryLoadCargo(101, "load", 0, "A", "B", 10), "Duplicate source load accepted.");
Check(!lift.TryDeliverCargo(102, "load", "receipt", 1, "B", 4), "Wrong-side delivery accepted.");
Check(!lift.TryDeliverCargo(102, "load", "receipt", 0, "C", 4), "Wrong destination accepted.");
Check(!lift.TryDeliverCargo(102, "load", "receipt", 0, "B", 11), "Over-delivery accepted.");
Check(lift.TryDeliverCargo(102, "load", "receipt", 0, "B", 4), "Partial delivery rejected.");
Check(!lift.TryDeliverCargo(102, "load", "receipt", 0, "B", 4), "Duplicate receipt accepted.");
Check(lift.Score(0) == 4 && lift.ActiveManifests == 1, "Partial delivery did not preserve remainder.");
Check(lift.TryDeliverCargo(103, "load", "remainder", 0, "B", 6), "Remaining cargo rejected.");
Check(lift.ActiveManifests == 0 && lift.Score(0) == 10, "Completed cargo stayed eligible.");
Check(!lift.TryLoadCargo(104, "load", 0, "A", "B", 10), "Spent manifest replayed.");
Check(lift.TryLoadCargo(104, "lost", 0, "A", "B", 10) && lift.DiscardCargo(104, "lost"), "Cargo loss not recorded.");
Check(!lift.TryDeliverCargo(105, "lost", "lost-receipt", 0, "B", 10), "Lost cargo scored.");
Check(!lift.TryLoadCargo(105, "lost", 0, "A", "B", 10), "Discarded manifest replayed.");
Check(lift.TryLoadCargo(106, "carryover", 0, "A", "B", 10), "Carryover fixture failed.");
Check(!lift.TryDeliverCargo(1300, "carryover", "late", 0, "B", 10), "Deadline delivery scored.");
Check(lift.Phase == GamemodePhase.Intermission && lift.Leg == 1 && lift.TransportingTeam == -1 && lift.ActiveManifests == 0, "Intermission incorrect.");
Check(!lift.TryLoadCargo(1359, "early", 1, "A", "B", 20), "Cargo accepted during intermission.");
Check(lift.Tick(1360) && lift.Leg == 2 && lift.TransportingTeam == 1, "Role swap incorrect.");
Check(!lift.TryDeliverCargo(1361, "carryover", "carryover-receipt", 1, "B", 10), "Previous leg cargo survived.");
Check(!lift.TryLoadCargo(1361, "wrong-second", 0, "A", "B", 10), "Previous transport team retained role.");
Check(lift.TryLoadCargo(1361, "second", 1, "A", "B", 11), "Second-leg load rejected.");
Check(lift.TryDeliverCargo(1362, "second", "second-receipt", 1, "B", 11), "Second-leg delivery rejected.");
Check(lift.Score(0) == 10 && lift.Score(1) == 11 && lift.Winner == -1, "Scores followed roles rather than teams or declared winner early.");
Check(lift.Tick(2560) && lift.Phase == GamemodePhase.Finished && lift.Winner == 1, "Final Heavyweight result incorrect.");

var skipped = new GamemodeMatch(GameModeKind.Heavyweight, 0);
Check(skipped.Tick(3000) && skipped.Phase == GamemodePhase.Finished && skipped.Leg == 2 && skipped.Deadline == 2460, "Late tick lengthened match or skipped state incorrectly.");
Check(skipped.Winner == -1 && skipped.RemainingSeconds == 0, "Tied match was not a draw.");
var war = new GamemodeMatch(GameModeKind.WarBox, 0);
Check(war.Tick(100000) && war.Phase == GamemodePhase.Running, "WarBox imposed an arcade timer.");
Check(!war.TryCreditKill(100000, "enemy", 0, 1, true), "WarBox awarded arcade points.");

Console.WriteLine($"PASS: {assertions} gamemode assertions.");
