namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>What a fitted store is, for the summary and the role.</summary>
    internal enum StoreKind : byte { None, AirToAir, AirToGround, Bomb, Ecm, Cargo, MissileDefence, Other }

    /// <summary>One set's store as the page resolved it: one this build knows, and what it is.</summary>
    internal struct StoreFacts
    {
        public bool Known;
        /// <summary>A known store this mission refuses (restricted, nuclear not yet, …): it launches empty here.</summary>
        public bool Refused;
        public StoreKind Kind;
        /// <summary>Per pylon (WeaponMount.mass is per hardpoint).</summary>
        public float Mass;
        public int Ammo;
    }

    /// <summary>A fit in numbers: stations (rows), how many carry a store that flies, how many hold one the launch empties
    /// (blocked) or the mission refuses, the mass of what flies (every pylon) and its rounds by kind.</summary>
    internal struct FitSummary
    {
        public int Stations, Fitted, Blocked, Refused;
        public float Mass;
        public int Aam, Agm, Bombs, Ecm, Cargo, MslDef;
    }

    internal static class LoadoutSummary
    {
        /// <summary>What a store is for the summary and ROLE: cargo, ECM, then a gun (review R5: an internal cannon, gun pod or turret is
        /// rated like a missile, but its hundreds of rounds must never count as AAM or AGM), missile defence, bombs, then its better role.</summary>
        public static StoreKind KindOf(bool cargo, bool jammer, bool gun, bool bomb, float antiAir, float antiSurface, float antiMissile)
        {
            if (cargo) return StoreKind.Cargo;
            if (jammer) return StoreKind.Ecm;
            if (gun) return StoreKind.Other;
            if (antiMissile > System.Math.Max(antiAir, antiSurface)) return StoreKind.MissileDefence;
            if (bomb) return StoreKind.Bomb;
            if (antiAir > 0f && antiAir >= antiSurface) return StoreKind.AirToAir;
            if (antiSurface > 0f) return StoreKind.AirToGround;
            return StoreKind.Other;
        }

        /// <summary>The fit of <paramref name="facts"/> (one per set) on <paramref name="layout"/>, as it would launch.</summary>
        public static FitSummary Of(StationLayout layout, StoreFacts[] facts)
        {
            int sets = layout.Sets;
            var fitted = new bool[sets];
            var cleared = new bool[sets];
            for (int s = 0; s < sets && s < facts.Length; s++) fitted[s] = facts[s].Known && !facts[s].Refused;
            layout.WillClear(fitted, cleared);
            var sum = new FitSummary { Stations = layout.Stations };
            for (int st = 0; st < layout.Stations; st++)
            {
                if (st >= facts.Length || !facts[st].Known) continue;
                StoreFacts f = facts[st];
                if (f.Refused) sum.Refused++;
                else if (cleared[st]) sum.Blocked++;
                else
                {
                    int pylons = layout.PylonsAt(st);
                    sum.Fitted++;
                    sum.Mass += f.Mass * pylons;
                    Add(ref sum, f.Kind, f.Ammo * pylons);
                }
            }
            return sum;
        }

        private static void Add(ref FitSummary s, StoreKind kind, int rounds)
        {
            switch (kind)
            {
                case StoreKind.AirToAir: s.Aam += rounds; break;
                case StoreKind.AirToGround: s.Agm += rounds; break;
                case StoreKind.Bomb: s.Bombs += rounds; break;
                case StoreKind.Ecm: s.Ecm += rounds; break;
                case StoreKind.Cargo: s.Cargo += rounds; break;
                case StoreKind.MissileDefence: s.MslDef += rounds; break;
            }
        }
    }
}
