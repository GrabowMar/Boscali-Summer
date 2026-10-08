using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>The geostationary bird: parked until a burn, slews at a fixed speed, spends fuel by distance, refuses at zero fuel.</summary>
    internal static class GeoBirdTests
    {
        public static void Run()
        {
            var b = new GeoBird(0.2f, 0.5f, 100f);
            TestAssert.That(!b.Moving(50f) && b.U(50f) == 0.2f, "a parked bird does not move");
            GeoBird m = b.Relocate(10f, 0.8f, 0.5f);
            float cost = GeoBird.Cost(0.6f);
            TestAssert.That(m.Moving(11f) && System.Math.Abs(m.Fuel - (100f - cost)) < 0.01f, "the burn starts and spends fuel up front");
            TestAssert.That(System.Math.Abs(m.Duration - 0.6f / GeoBird.Slew) < 0.01f && m.Duration > 40f && m.Duration < 120f, "a 60 % hop takes under two minutes");
            TestAssert.That(System.Math.Abs(m.U(10f + m.Duration * 0.5f) - 0.5f) < 0.01f, "half way at half the time");
            TestAssert.That(!m.Moving(10f + m.Duration + 1f) && m.U(1000f) == 0.8f, "it parks at the target");
            TestAssert.That(m.Relocate(12f, 0.1f, 0.1f).ToU == 0.8f, "a second burn is refused mid-transfer");
            var dry = new GeoBird(0.2f, 0.5f, 3f);
            TestAssert.That(dry.Relocate(0f, 0.9f, 0.5f).ToU == 0.2f, "not enough fuel: no move");
            TestAssert.That(b.Relocate(0f, 0.2f, 0.5f).DepartAt == 0f && b.Relocate(0f, 0.2f, 0.5f).Fuel == 100f, "a zero move costs nothing");
            TestAssert.That(b.Covers(0f, 0.3f, 0.5f, 0.14f) && !b.Covers(0f, 0.6f, 0.5f, 0.14f), "footprint gate");
            HostRules();
        }

        /// <summary>The host side: SpaceState owns a GeoBird per bird, judges a relocation and gates a perk by footprint.</summary>
        private static void HostRules()
        {
            var st = new SpaceState(1);
            var hu = new[] { 0.40f, 0.30f, 0.50f }; var hv = new[] { 0.45f, 0.55f, 0.50f };
            st.SetHomes(hu, hv);
            TestAssert.That(st.Geo(BirdKind.Radar).U(5f) == 0.30f && st.Geo(BirdKind.Radar).Fuel == 100f, "a bird starts parked at its default point with a full tank");
            TestAssert.That(st.Covers(BirdKind.Radar, 5f, 0.35f, 0.55f) && !st.Covers(BirdKind.Radar, 5f, 0.80f, 0.55f), "the footprint gate reads the bird's own radius");
            TestAssert.That(st.Covers(BirdKind.Radar, 5f, 0.30f - GeoSpace.Reach[1] * 0.9f, 0.55f) && !st.Covers(BirdKind.Optical, 5f, 0.30f - GeoSpace.Reach[1] * 0.9f, 0.55f), "radar swath is wider than the optical one");

            TestAssert.Eq(st.Relocate(BirdKind.Radar, 10f, 0.30f, 0.55f), GeoSpace.Refusal.NoMove, "zero move is refused");
            TestAssert.Eq(st.Relocate(BirdKind.Radar, 10f, 0.80f, 0.55f), GeoSpace.Refusal.None, "a burn starts");
            GeoBird g = st.Geo(BirdKind.Radar);
            TestAssert.That(g.Moving(11f) && System.Math.Abs(g.Fuel - (100f - GeoBird.Cost(0.5f))) < 0.01f, "fuel is spent at the start of the burn");
            TestAssert.Eq(st.Relocate(BirdKind.Radar, 20f, 0.10f, 0.10f), GeoSpace.Refusal.Moving, "mid-burn is refused");
            TestAssert.That(!st.Covers(BirdKind.Radar, 11f, 0.80f, 0.55f) && st.Covers(BirdKind.Radar, 10f + g.Duration + 1f, 0.80f, 0.55f), "the footprint follows the slide: covered once parked");
            TestAssert.Eq(st.Relocate(BirdKind.Optical, 10f, 0.40f, 0.45f), GeoSpace.Refusal.NoMove, "another bird is judged on its own");

            // Fuel runs out: no regeneration, refused with NoFuel, state untouched.
            float t = 10f + g.Duration + 1f; int burns = 0; bool toggle = false;
            while (burns < 40 && st.Relocate(BirdKind.Radar, t, toggle ? 0.05f : 0.95f, 0.5f) == GeoSpace.Refusal.None) { burns++; toggle = !toggle; t += st.Geo(BirdKind.Radar).Duration + 1f; }
            TestAssert.That(burns > 0 && burns < 40 && st.Geo(BirdKind.Radar).Fuel < GeoBird.Cost(0.9f), "fuel is finite and does not come back");
            float left = st.Geo(BirdKind.Radar).Fuel;
            TestAssert.Eq(st.Relocate(BirdKind.Radar, t + 5000f, toggle ? 0.05f : 0.95f, 0.5f), GeoSpace.Refusal.NoFuel, "too little fuel is refused");
            TestAssert.That(st.Geo(BirdKind.Radar).Fuel == left, "a refusal changes nothing");

            // A dead bird cannot burn or cover; a relaunched one is fresh at its default point.
            st.KillBird(BirdKind.Radar, t);
            TestAssert.Eq(st.Relocate(BirdKind.Radar, t + 1f, 0.5f, 0.5f), GeoSpace.Refusal.Dead, "a dead bird is refused");
            TestAssert.That(!st.Covers(BirdKind.Radar, t, st.Geo(BirdKind.Radar).U(t), st.Geo(BirdKind.Radar).V(t)), "a dead bird covers nothing");
            st.RestoreBird();
            TestAssert.That(st.Geo(BirdKind.Radar).U(t) == 0.30f && st.Geo(BirdKind.Radar).V(t) == 0.55f && st.Geo(BirdKind.Radar).Fuel == 100f && st.Covers(BirdKind.Radar, t, 0.30f, 0.55f), "a relaunch restores the default point and a full tank");

            // The director: the idle, fuelled bird that misses the point, nearest first, honouring its rest time and the 30 % floor.
            var rest = new float[3];
            TestAssert.Eq(GeoSpace.PickBird(st, 5000f, rest, 0.40f, 0.50f), -1, "every footprint already covers a point in the middle");
            int pick = GeoSpace.PickBird(st, 5000f, rest, 0.95f, 0.10f);
            TestAssert.That(pick >= 0, "a far point sends a bird");
            rest[pick] = 6000f;
            TestAssert.That(GeoSpace.PickBird(st, 5000f, rest, 0.95f, 0.10f) != pick, "a resting bird is left alone");
            st.SetHomes(hu, hv);
            // Defaults sit between the two sides, on the faction's side, spread apart.
            var du = new float[3]; var dv = new float[3];
            GeoSpace.Defaults(0.2f, 0.5f, 0.8f, 0.5f, du, dv);
            TestAssert.That(du[0] > 0.2f && du[0] < 0.5f && du[2] > du[1] && System.Math.Abs(dv[0] - dv[1]) > 0.05f, "default points lie toward the front, apart from each other");
            TestAssert.That(GeoSpace.TryUnpack(GeoSpace.Pack(1.4f, -3f), out float pu, out float pv) && pu == 1f && pv == 0f && !GeoSpace.TryUnpack(-1, out _, out _), "map points clamp and pack");
        }
    }
}
