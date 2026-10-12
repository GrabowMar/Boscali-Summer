namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The host edits that change the tape identity (spec 9.3 "Cut"): a new story, a re-roll, a pin, a scenario. Each is one key edit that rebases first (so the clock reference is now) and then
    /// places the new tape like a capture, because the new tape has a different front geometry and the old offset means nothing on it. Pure, so the order is tested.
    /// Slice 4 holds D, so the placement is the held case of <see cref="TapeOpening.Solve"/> (class-only, pinned story or sky pin); slice 5 passes the key's Dynamic flag.
    /// </summary>
    internal static class TapeCuts
    {
        /// <summary>The mission-led opening (spec 5.5.3) of a key whose tape just changed: the same tape, the offset <see cref="TapeOpening.Solve"/> gives.</summary>
        public static WeatherKey Place(WeatherKey key) =>
            key.WithOffset(TapeOpening.Solve(WeatherTape.For(key), key, false).Offset);

        /// <summary>STORY: repeats one story for the whole tape, or hands the choice back to the mission class with <see cref="WeatherKey.None"/>. A sky pin still overrides it.</summary>
        public static WeatherKey Story(WeatherKey key, float now, byte story) =>
            Place(key.Rebased(now).WithStory(story));

        /// <summary>RE-ROLL: a new layout salt (the salt is part of the tape identity, so this is a cut): new cloud, cell and set-piece sites and sky variants.</summary>
        public static WeatherKey Reroll(WeatherKey key, float now) =>
            Place(key.Rebased(now).WithLayoutSalt(unchecked((byte)(key.LayoutSalt + 1))));
    }
}
