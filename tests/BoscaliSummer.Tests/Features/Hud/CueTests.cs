using System;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The CUE stack (the game's grey message boxes redrawn at the left edge): the class and severity of the common messages, the fitting of long ones, the effect timeline, the decode, the ring and the layout.</summary>
    internal static class CueTests
    {
        public static void Run()
        {
            Plain();
            Classes();
            Fitting();
            Life();
            Brackets();
            Locking();
            Underline();
            Fade();
            Decode();
            Ring();
            Filtering();
            Health();
            Signature();
            Layout();
            Tones();
        }

        private static (CueClass, CueSeverity) Of(string rich, CueSource source = CueSource.Action, CueSeverity severity = CueSeverity.Normal)
        {
            CueClass c = CueText.Classify(CueText.Plain(rich), source, ref severity);
            return (c, severity);
        }

        private static void Plain()
        {
            TestAssert.Eq(CueText.Plain("Weapon safety lock <b>Disabled</b>"), "WEAPON SAFETY LOCK DISABLED", "Tags go, capitals stay");
            TestAssert.Eq(CueText.Plain("<color=#FF3030FF>Engine\nfire</color>  now"), "ENGINE FIRE NOW", "A line break and a run of spaces are one space");
            TestAssert.Eq(CueText.Plain("<b></b>"), "", "Tags only is nothing");
            TestAssert.Eq(CueText.Plain(null), "", "No text is nothing");
            TestAssert.Eq(CueText.Plain("  Gear down "), "GEAR DOWN", "Trimmed");
            TestAssert.That(CueText.Coloured("<color=#FF0000>Engine fire</color>") && !CueText.Coloured("Gear <b>down</b>"), "The alert colour is a colour tag");
            TestAssert.Eq(CueText.Seed("ARMED"), CueText.Seed("ARMED"), "The seed is a function of the words");
            TestAssert.That(CueText.Seed("ARMED") != CueText.Seed("SAFE"), "...and different words have different seeds");
        }

        private static void Classes()
        {
            TestAssert.Eq(Of("Weapon safety lock <b>Disabled</b>"), (CueClass.Wpn, CueSeverity.Caution), "Safety lock disabled: WPN, a caution (armed)");
            TestAssert.Eq(Of("Weapon safety lock <b>Enabled</b>"), (CueClass.Wpn, CueSeverity.Normal), "Safety lock enabled: WPN, normal (safe)");
            TestAssert.Eq(Of("Weapons ARMED"), (CueClass.Wpn, CueSeverity.Caution), "Weapons armed: WPN, a caution");
            TestAssert.Eq(Of("Weapons SAFE"), (CueClass.Wpn, CueSeverity.Normal), "Weapons safe: WPN");
            TestAssert.Eq(Of("Master arm disarmed"), (CueClass.Wpn, CueSeverity.Normal), "Disarmed is not armed");
            TestAssert.Eq(Of("Rearmed FS-20 by Tender"), (CueClass.Wpn, CueSeverity.Normal), "Rearmed is not armed either");
            TestAssert.Eq(Of("Guns <b>Split</b>"), (CueClass.Wpn, CueSeverity.Normal), "Guns: WPN");
            TestAssert.Eq(Of("Turrets set to hold fire"), (CueClass.Wpn, CueSeverity.Normal), "Hold fire is the turrets' safe state, not a fire");
            TestAssert.Eq(Of("Turrets set to engage at will"), (CueClass.Wpn, CueSeverity.Caution), "Engage at will: weapons free, a caution");
            TestAssert.Eq(Of("Landing gear down"), (CueClass.Sys, CueSeverity.Normal), "Gear: SYS");
            TestAssert.Eq(Of("Stability Assist <b>Enabled</b>"), (CueClass.Sys, CueSeverity.Normal), "Flight assist: SYS");
            TestAssert.Eq(Of("Radar Armed"), (CueClass.Sys, CueSeverity.Normal), "Radar armed is the radar's word, not the weapons'");
            TestAssert.Eq(Of("Radar Disarmed"), (CueClass.Sys, CueSeverity.Normal), "Radar disarmed: SYS");
            TestAssert.Eq(Of("Jammer ON"), (CueClass.Sys, CueSeverity.Normal), "Jammer: SYS");
            TestAssert.Eq(Of("Nav Lights <b>On</b>"), (CueClass.Sys, CueSeverity.Normal), "Lights: SYS");
            TestAssert.Eq(Of("<color=#FF3030FF>Engine fire</color>", CueSource.Action, CueSeverity.Alert), (CueClass.Dmg, CueSeverity.Alert), "An alert-coloured failure: DMG, an alert");
            TestAssert.Eq(Of("Hydraulic failure"), (CueClass.Dmg, CueSeverity.Caution), "A failure word without the colour: DMG, a caution");
            TestAssert.Eq(Of("<b>Fuel leak detected; unable to refuel</b>"), (CueClass.Dmg, CueSeverity.Caution), "A leak: DMG");
            TestAssert.Eq(Of("Rearmed FS-20 - cost: $120k"), (CueClass.Funds, CueSeverity.Normal), "Money in an action report: FUNDS");
            TestAssert.Eq(Of("Rearm insufficient funds"), (CueClass.Funds, CueSeverity.Caution), "Insufficient funds: FUNDS, a caution");
            TestAssert.Eq(Of("Rearm insufficient ammo"), (CueClass.Wpn, CueSeverity.Caution), "Insufficient ammo: WPN, a caution");
            TestAssert.Eq(Of("Detected units +1,0"), (CueClass.Funds, CueSeverity.Normal), "A score line: FUNDS");
            TestAssert.Eq(Of("Successful Sortie + 3.5"), (CueClass.Funds, CueSeverity.Normal), "A score line with a space after the plus: FUNDS");
            TestAssert.Eq(Of("Cleared to taxi to Runway 21"), (CueClass.Sys, CueSeverity.Normal), "Unknown text is SYS");
            TestAssert.Eq(CueText.Plain("Cleared to taxi to Runway 21"), "CLEARED TO TAXI TO RUNWAY 21", "...with its text kept");
            TestAssert.Eq(Of("+$803,7k", CueSource.Allocation), (CueClass.Funds, CueSeverity.Normal), "The funds line is FUNDS whatever it says");
            TestAssert.Eq(Of("Engine fire", CueSource.Allocation), (CueClass.Funds, CueSeverity.Normal), "...the box decides");
            TestAssert.Eq(Of("Detected units +1,0", CueSource.Score), (CueClass.Funds, CueSeverity.Normal), "The score line is FUNDS");
            TestAssert.Eq(Of("FS-20 RESERVE GRANTED", CueSource.Reserve), (CueClass.Rsv, CueSeverity.Normal), "A reserve notice is RSV");
            TestAssert.Eq(Of("FS-20 RESERVE CANCELLED", CueSource.Reserve, CueSeverity.Alert), (CueClass.Rsv, CueSeverity.Alert), "...and keeps the severity it came with");
            TestAssert.Eq(CueWords.Of(CueClass.Sys) + CueWords.Of(CueClass.Wpn) + CueWords.Of(CueClass.Dmg) + CueWords.Of(CueClass.Funds) + CueWords.Of(CueClass.Rsv), "SYSWPNDMGFUNDSRSV", "The class words");
        }

        private static void Fitting()
        {
            TestAssert.Eq(CueFit.BigChars, 32, "32 characters at 20");
            TestAssert.Eq(CueFit.SmallChars, 40, "40 characters at 16");
            string t32 = new string('A', 32), t33 = new string('A', 33), t40 = new string('B', 40);
            TestAssert.That(CueFit.Fit(t32, out float s) == t32 && s == 20f, "32 characters stay at 20");
            TestAssert.That(CueFit.Fit(t33, out s) == t33 && s == 16f, "33 drop to 16");
            TestAssert.That(CueFit.Fit(t40, out s) == t40 && s == 16f, "40 stay at 16");
            string longLine = "REINFORCEMENTS ARE INBOUND FROM THE NORTHERN STAGING AREA";
            string cut = CueFit.Fit(longLine, out s);
            TestAssert.Eq(cut, "REINFORCEMENTS ARE INBOUND FROM THE", "Longer than 40: whole words off the end (the next word would not fit)");
            TestAssert.That(s == 16f && cut.Length <= 40 && !cut.Contains(".") && !cut.EndsWith(" "), "...at 16, no dots");
            TestAssert.Eq(CueFit.Fit("ONE TWO THREE FOUR FIVE SIX SEVEN - EIGHT NINE", out s), "ONE TWO THREE FOUR FIVE SIX SEVEN", "A dash left at the end of a cut goes too");
            TestAssert.Eq(CueFit.Fit(new string('X', 60), out s), new string('X', 40), "One word as long as the line is cut");
            // A message that fits at 20 draws inside the stack's 420 units.
            TestAssert.That(32 * CueFit.Advance * CueFit.BigSize <= CueFit.MaxWidth && 40 * CueFit.Advance * CueFit.SmallSize <= CueFit.MaxWidth, "Both limits are inside 420");
        }

        private static void Life()
        {
            TestAssert.Near(CueTimeline.LifeOf(5f), 5f, "The game's own time");
            TestAssert.Near(CueTimeline.LifeOf(0.5f), CueLog.MinLife, "A very short line is held to 1.2 s so it can be read");
            TestAssert.Near(CueTimeline.LifeOf(float.NaN), 5f, "Not a time: five seconds");
            TestAssert.Near(CueTimeline.LifeOf(-2f), 5f, "No time: five seconds");
            TestAssert.Near(CueTimeline.LifeOf(1e9f), 60f, "An absurd time is a minute");
            TestAssert.Eq(CueTimeline.PhaseOf(0.1f, 5f), CuePhase.Show, "Show");
            TestAssert.Eq(CueTimeline.PhaseOf(0.5f, 5f), CuePhase.Hold, "Hold");
            TestAssert.Eq(CueTimeline.PhaseOf(4.7f, 5f), CuePhase.Fade, "Fade: the last 0.35 s");
            TestAssert.Eq(CueTimeline.PhaseOf(5f, 5f), CuePhase.Gone, "Gone");
            TestAssert.Eq(CueTimeline.PhaseOf(0.5f, CueLog.MinLife), CuePhase.Hold, "The shortest life still has a hold");
            TestAssert.That(CueTimeline.Flash(0.05f) && CueTimeline.Flash(0.119f) && !CueTimeline.Flash(0.121f) && !CueTimeline.Flash(-0.1f), "One tone brighter for the first 0.12 s");
        }

        private static void Brackets()
        {
            TestAssert.Near(CueTimeline.BracketOffset(0f, 5f), 46f, "The halves start 46 units outside");
            TestAssert.Near(CueTimeline.BracketOffset(0.07f, 5f), 46f * 0.125f, "Half way through the snap (ease-out) they are an eighth of the way back");
            TestAssert.Near(CueTimeline.BracketOffset(0.14f, 5f), 0f, "In at 0.14 s");
            TestAssert.Near(CueTimeline.BracketOffset(1f, 5f), 0f, "They hug through the hold");
            TestAssert.Near(CueTimeline.BracketOffset(4.65f, 5f), 0f, "...until the fade starts");
            TestAssert.Near(CueTimeline.BracketOffset(4.825f, 5f), 46f * 0.25f, "Half way through the fade (ease-in) a quarter of the way out");
            TestAssert.Near(CueTimeline.BracketOffset(5f, 5f), 46f, "46 outside again when it goes");
            float last = 100f;
            for (float a = 0f; a <= 0.14f; a += 0.01f)
            {
                float o = CueTimeline.BracketOffset(a, 5f);
                TestAssert.That(o <= last + 0.001f, "The snap only moves inward");
                last = o;
            }
        }

        private static void Locking()
        {
            TestAssert.Eq(CueTimeline.Locked(0f, 10), 0, "Nothing has locked at the first instant");
            TestAssert.Eq(CueTimeline.Locked(0.06f, 10), 2, "About 0.025 s a character");
            TestAssert.Eq(CueTimeline.Locked(0.21f, 10), 8, "...left to right");
            TestAssert.Eq(CueTimeline.Locked(0.3f, 10), 10, "A ten character message has locked by 0.3 s");
            TestAssert.Eq(CueTimeline.Locked(5f, 10), 10, "...and stays");
            foreach (int n in new[] { 1, 5, 12, 27, 32, 40 })
            {
                TestAssert.Eq(CueTimeline.Locked(0.3f, n), n, "A message of " + n + " has locked by 0.3 s, however long");
                TestAssert.That(CueTimeline.Locked(0.29f, n) < n || n == 1 || n == 5 || n == 12, "...and not before it, when it is long enough to be slowed (" + n + ")");
            }
            TestAssert.That(CueTimeline.Locked(0.1f, 40) > CueTimeline.Locked(0.1f, 10), "A long message locks faster per character so the whole takes 0.3 s");
            TestAssert.Eq(CueTimeline.Locked(1f, 0), 0, "No message, nothing to lock");
        }

        private static void Underline(float age, float life, float from, float to, string what)
        {
            CueTimeline.Underline(age, life, out float f, out float t);
            TestAssert.Near(f, from, what + " (from)");
            TestAssert.Near(t, to, what + " (to)");
        }

        private static void Underline()
        {
            Underline(0f, 5f, 0f, 0f, "Nothing drawn at the first instant");
            Underline(0.16f, 5f, 0f, 0.875f, "Drawing from the left, ease-out");
            Underline(0.32f, 5f, 0f, 1f, "The full width at the end of the show");
            Underline(2f, 5f, 0f, 1f, "Whole through the hold");
            Underline(4.825f, 5f, 0.25f, 1f, "Retracting to the right: the left end runs on");
            Underline(5f, 5f, 1f, 1f, "Gone to the right");
        }

        private static void Fade()
        {
            TestAssert.Near(CueTimeline.Alpha(0f, 5f), 1f, "Full at the start");
            TestAssert.Near(CueTimeline.Alpha(2f, 5f), 1f, "Full through the hold");
            float[] steps = { 1f, 0.55f, 0.85f, 0.3f, 0f };
            for (int i = 0; i < 5; i++) TestAssert.Near(CueTimeline.Alpha(4.65f + 0.07f * i + 0.035f, 5f), steps[i], "Fade step " + i + " is " + steps[i]);
            TestAssert.Near(CueTimeline.Alpha(5f, 5f), 0f, "Nothing once the game's time is up");
            TestAssert.Near(CueTimeline.Alpha(9f, 5f), 0f, "...and after");
            TestAssert.That(CueTimeline.Alpha(4.755f, 5f) < CueTimeline.Alpha(4.825f, 5f), "A flicker, not a slope: the third step is brighter than the second");
            TestAssert.Eq(CueTimeline.Stage(1f, 5f), 0, "A cue standing still has stage 0");
            TestAssert.That(CueTimeline.Stage(0.02f, 5f) > 0 && CueTimeline.Stage(0.2f, 5f) > CueTimeline.Stage(0.02f, 5f), "...and one that moves steps every sixtieth of a second");
            TestAssert.That(CueTimeline.Stage(4.8f, 5f) > 0, "The fade moves");
            TestAssert.Eq(CueTimeline.Stage(6f, 5f), -1, "A cue that is gone is -1");
            TestAssert.Near(CueTimeline.Slide(0f), 0f, "A slot has not slid at the instant a newer cue comes");
            TestAssert.Near(CueTimeline.Slide(0.06f), 0.875f, "...is 7/8 down half way (ease-out)");
            TestAssert.Near(CueTimeline.Slide(0.12f), 1f, "...and all the way after 0.12 s");
        }

        private static void Decode()
        {
            const string Text = "WEAPON SAFETY LOCK DISABLED";
            int seed = CueText.Seed(Text);
            var a = new char[96];
            var b = new char[96];
            int n = CueDecode.Fill(a, Text, 0.05f, seed);
            CueDecode.Fill(b, Text, 0.05f, seed);
            TestAssert.Eq(n, Text.Length, "The decoded message is as long as the real one");
            TestAssert.That(a.Take(n).SequenceEqual(b.Take(n)), "The same age and the same cue always decode the same way");
            for (int i = 0; i < n; i++)
            {
                if (Text[i] == ' ') TestAssert.That(a[i] == ' ', "A space never scrambles (" + i + ")");
                else if (i >= CueTimeline.Locked(0.05f, n)) TestAssert.That(CueDecode.Glyphs.IndexOf(a[i]) >= 0, "An unlocked character is a glyph from A-Z 0-9 (" + i + ": " + a[i] + ")");
                else TestAssert.That(a[i] == Text[i], "A locked character is the real one (" + i + ")");
            }
            TestAssert.That(CueTimeline.Locked(0.05f, n) > 0 && CueTimeline.Locked(0.05f, n) < n, "Part of it has locked at 0.05 s");
            CueDecode.Fill(a, Text, 0f, seed);
            string first = new string(a, 0, n);
            CueDecode.Fill(a, Text, 0.15f, seed);
            string mid = new string(a, 0, n);
            TestAssert.That(first != Text && mid != Text && first != mid, "At 0 and 0.15 s the message is mid-scramble and different");
            CueDecode.Fill(a, Text, 0.3001f, seed);
            TestAssert.Eq(new string(a, 0, n), Text, "It ends on the real text by 0.3 s");
            CueDecode.Fill(a, Text, 0f, seed + 1);
            TestAssert.That(new string(a, 0, n) != first, "A different cue scrambles differently");
            // Every glyph the decode shows, over many ages and places, is one of the 36.
            for (int tick = 0; tick < 40; tick++)
            {
                CueDecode.Fill(a, "ABCDEFGHIJKLMNOP QRSTUVW", tick / 30f * 0.99f, 7);
                for (int i = 0; i < 24; i++) TestAssert.That(a[i] == ' ' ? i == 16 : CueDecode.Glyphs.IndexOf(a[i]) >= 0, "Only the alphabet and the space: " + a[i]);
            }
            var small = new char[4];
            TestAssert.Eq(CueDecode.Fill(small, Text, 0f, seed), 4, "A buffer that is too small cuts the message");
        }

        private static CueLog Fresh() => new CueLog();

        private static void Ring()
        {
            CueLog log = Fresh();
            TestAssert.That(log.Add(CueSource.Action, "<b></b>", CueSeverity.Normal, 100f, 5f) == null && log.Count == 0, "A message with nothing in it is not kept");
            Cue first = log.Add(CueSource.Action, "Weapon safety lock <b>Disabled</b>", CueSeverity.Normal, 100f, 5f);
            TestAssert.Eq(first.Text, "WEAPON SAFETY LOCK DISABLED", "The words are capitals without tags");
            TestAssert.That(first.Class == CueClass.Wpn && first.Severity == CueSeverity.Caution && first.Size == 20f && first.Life == 5f, "Class, severity, size and life are worked out");
            TestAssert.Eq(first.Serial, 1, "The first cue is number 1");
            log.Add(CueSource.Action, "Gear down", CueSeverity.Normal, 101f, 5f);
            log.Add(CueSource.Reserve, "FS-20 RESERVE GRANTED", CueSeverity.Normal, 102f, 5f);
            log.Add(CueSource.Score, "Detected units +1,0", CueSeverity.Normal, 103f, 5f);
            TestAssert.Eq(log.Count, 4, "Four fit");
            log.Add(CueSource.Allocation, "$12M", CueSeverity.Normal, 104f, 5f);
            TestAssert.Eq(log.Count, 4, "The ring holds four");
            TestAssert.Eq(log[0].Text, "GEAR DOWN", "...the oldest left");
            TestAssert.Eq(log[3].Text, "$12M", "...the newest last");
            var shown = new Cue[CueLog.MaxShown];
            int n = log.Select(104.5f, BoardDeclutter.Full, 15, shown);
            TestAssert.Eq(n, 3, "At most three are shown");
            TestAssert.That(shown[0].Text == "$12M" && shown[1].Text == "DETECTED UNITS +1,0" && shown[2].Text == "FS-20 RESERVE GRANTED", "...newest first");
            TestAssert.Eq(log.Select(104.5f, BoardDeclutter.Full, 0, shown), 0, "No source in charge, none shown");
            TestAssert.Eq(log.Select(104.5f, BoardDeclutter.Full, 1 << (int)CueSource.Reserve, shown), 1, "Only the sources in charge are shown");
            // A cue goes when the game's time is up, and a short one is held to 1.2 s.
            log = Fresh();
            Cue shortOne = log.Add(CueSource.Action, "Tail Hook Deployed", CueSeverity.Normal, 10f, 0.4f);
            TestAssert.That(shortOne.Alive(11.1f) && !shortOne.Alive(11.3f), "A line the game keeps for 0.4 s is shown for 1.2");
            log.Add(CueSource.Action, "Radar Armed", CueSeverity.Normal, 10f, 5f);
            TestAssert.Eq(log.Select(11.3f, BoardDeclutter.Full, 15, shown), 1, "...and then it is gone");
            TestAssert.Eq(log.LiveCount(CueSource.Action, 11.3f), 1, "One live cue of that source");
            // The scene started again: the clock ran back.
            log.Tick(9.8f);
            TestAssert.Eq(log.Count, 2, "A clock half a second behind is not a new scene");
            log.Tick(2f);
            TestAssert.Eq(log.Count, 0, "A clock that ran backwards empties the ring");
            log.Add(CueSource.Action, "Gear down", CueSeverity.Normal, 300f, 5f);
            log.Add(CueSource.Action, "Gear up", CueSeverity.Normal, 5f, 5f);
            TestAssert.Eq(log.Count, 1, "A cue from an older clock empties the ring before it goes in");
        }

        private static void Filtering()
        {
            CueLog log = Fresh();
            log.Add(CueSource.Action, "Landing gear down", CueSeverity.Normal, 10f, 5f);
            log.Add(CueSource.Action, "Weapon safety lock <b>Disabled</b>", CueSeverity.Normal, 10.5f, 5f);
            log.Add(CueSource.Action, "<color=#F00>Engine fire</color>", CueSeverity.Alert, 11f, 5f);
            var shown = new Cue[CueLog.MaxShown];
            TestAssert.Eq(log.Select(11.5f, BoardDeclutter.Full, 15, shown), 3, "Full shows all");
            TestAssert.Eq(log.Select(11.5f, BoardDeclutter.Lean, 15, shown), 3, "Lean shows all (it only drops the shelf)");
            TestAssert.Eq(log.Select(11.5f, BoardDeclutter.Minimal, 15, shown), 2, "Minimal shows only the AMBER and RED ones");
            TestAssert.That(shown[0].Severity == CueSeverity.Alert && shown[1].Severity == CueSeverity.Caution, "...the alert on top");
        }

        private static void Health()
        {
            CueLog log = Fresh();
            TestAssert.That(log.Healthy(CueSource.Action, 0f, false), "A box showing nothing is healthy");
            TestAssert.That(log.Healthy(CueSource.Action, 0f, true), "A box showing text with no cue is healthy for the moment");
            TestAssert.That(log.Healthy(CueSource.Action, 0.5f, true), "...for 0.6 s");
            TestAssert.That(!log.Healthy(CueSource.Action, 0.7f, true), "...and then the capture is blind: the game's box is left whole");
            TestAssert.That(log.Healthy(CueSource.Reserve, 0.7f, true), "Another box has its own account");
            log.Add(CueSource.Action, "Gear down", CueSeverity.Normal, 0.8f, 5f);
            TestAssert.That(log.Healthy(CueSource.Action, 0.8f, true), "A live cue of its source and it trusts itself again");
            TestAssert.That(log.Healthy(CueSource.Action, 4f, true), "...for as long as one is live");
            TestAssert.That(log.Healthy(CueSource.Action, 7f, true) && log.Healthy(CueSource.Action, 7.5f, true) && !log.Healthy(CueSource.Action, 7.7f, true), "Once it has gone the 0.6 s starts again");
            TestAssert.That(log.Healthy(CueSource.Action, 9f, false) && log.Healthy(CueSource.Action, 9.1f, true), "A box that cleared resets the account");
        }

        private static void Signature()
        {
            CueLog log = Fresh();
            var shown = new Cue[CueLog.MaxShown];
            Cue cue = log.Add(CueSource.Action, "Weapon safety lock <b>Disabled</b>", CueSeverity.Normal, 10f, 5f);
            int Sig(float now, BoardDeclutter level = BoardDeclutter.Full)
            {
                int n = log.Select(now, level, 15, shown);
                return CueLog.Signature(now, level, 15, shown, n);
            }
            TestAssert.That(Sig(10.01f) != Sig(10.1f) && Sig(10.1f) != Sig(10.2f), "The show steps the signature");
            TestAssert.Eq(Sig(11f), Sig(12f), "A cue that holds does not");
            TestAssert.That(Sig(14.8f) != Sig(14.9f), "The fade steps it");
            TestAssert.That(Sig(11f) != Sig(11f, BoardDeclutter.Lean), "The level is part of it");
            int one = Sig(11f);
            log.Add(CueSource.Action, "Gear down", CueSeverity.Normal, 11.5f, 5f);
            TestAssert.That(Sig(12f) != one, "A new cue changes it");
            TestAssert.That(cue.Serial != 0, "Cues are numbered");
        }

        private static void Layout()
        {
            TestAssert.Near(CueLayout.Top(1080f), 324f, "Top at 30 % of 1080");
            TestAssert.Near(CueLayout.Top(1200f), 360f, "...of 1200");
            TestAssert.That(CueLayout.Left == 16f && CueLayout.Pitch == 34f, "16 in, 34 apart");
            TestAssert.Near(CueLayout.ShelfWidth(3), 80f, "A shelf for SYS: 12 in, the word, 4 clear, a short hairline");
            TestAssert.Near(CueLayout.ShelfWidth(5), 95f, "...for FUNDS");
            var shown = new Cue[3];
            var log = Fresh();
            shown[0] = log.Add(CueSource.Action, "B", CueSeverity.Normal, 10f, 5f);
            shown[1] = log.Add(CueSource.Action, "A", CueSeverity.Normal, 5f, 5f);
            shown[2] = log.Add(CueSource.Action, "C", CueSeverity.Normal, 1f, 5f);
            TestAssert.Near(CueLayout.Offset(shown, 0, 10f), 0f, "The newest is in the first slot");
            TestAssert.Near(CueLayout.Offset(shown, 1, 10f), 0f, "An older cue has not moved at the instant a newer one comes");
            TestAssert.Near(CueLayout.Offset(shown, 1, 10.06f), 34f * 0.875f, "...is 7/8 of a slot down half way through the slide");
            TestAssert.Near(CueLayout.Offset(shown, 1, 10.12f), 34f, "...and a slot down after 0.12 s");
            TestAssert.Near(CueLayout.Offset(shown, 2, 20f), 68f, "The third is two slots down once everything has settled");
            TestAssert.Near(CueLayout.Offset(shown, 2, 10.06f), 34f + 34f * 0.875f, "...and slides from the second slot while only the newest slides");
        }

        private static void Tones()
        {
            TestAssert.Eq(CueTimeline.ToneOf(CueSeverity.Normal), ScopeTone.Hi, "Normal is BRIGHT");
            TestAssert.Eq(CueTimeline.ToneOf(CueSeverity.Caution), ScopeTone.Amber, "A caution is AMBER");
            TestAssert.Eq(CueTimeline.ToneOf(CueSeverity.Alert), ScopeTone.Red, "An alert is RED");
            TestAssert.Eq(CueTimeline.Raise(ScopeTone.Dim), ScopeTone.Ink, "DIM, a tone brighter, is INK");
            TestAssert.Eq(CueTimeline.Raise(ScopeTone.Ink), ScopeTone.Hi, "INK is BRIGHT");
            TestAssert.Eq(CueTimeline.Raise(ScopeTone.Hi), ScopeTone.White, "BRIGHT is white");
            TestAssert.Eq(CueTimeline.Raise(ScopeTone.Amber), ScopeTone.White, "AMBER flashes white");
            var pal = new ScopePalette(new Rgba(0.5f, 0.87f, 0.6f), new Rgba(1f, 0.85f, 0.3f), new Rgba(1f, 0.3f, 0.3f));
            TestAssert.That(pal.Of(ScopeTone.White).R > 0.99f && pal.Of(ScopeTone.White).G > 0.99f && pal.Of(ScopeTone.White).B > 0.99f && pal.Of(ScopeTone.White).A > 0.99f, "White is white");
            TestAssert.That(pal.Of(ScopeTone.White).R > pal.Of(ScopeTone.Hi).R, "...above BRIGHT");
        }
    }
}
