namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The line-art family of a store (one silhouette per class, see <c>StoreShapes</c>): a gun, an air-to-air or an air-to-ground missile, a bomb, a rocket pod, a rail (a powered energy weapon) and a kit (cargo, troops, a jammer or any other pod).</summary>
    internal enum StoreClass : byte { Gun, Aam, Agm, Bomb, Pod, Rail, Kit }

    /// <summary>The <c>WeaponInfo</c> flags (and the weapon's own kind) <see cref="StoresMath.ClassOf"/> reads, so the table is pure.</summary>
    internal struct WeaponFlags
    {
        public bool Gun, Energy, Bomb, Missile, Jammer, Cargo, Launcher;
        /// <summary>The seeker role, as the game rates it: anti-air against anti-surface (the tie goes to air).</summary>
        public float AntiAir, AntiSurface;
    }

    /// <summary>One weapon station of the player's aircraft as the STORES HEADER draws it: a pip on the station rail and, when it is the selected one, the weapon cell.</summary>
    internal struct StoreStation
    {
        public StoreClass Class;
        /// <summary>The weapon's name in capitals (<c>"20MM ROTARY CANNON"</c>); stations with the same name pool their rounds.</summary>
        public string Name;
        /// <summary>Rounds left and the full load of this station.</summary>
        public int Ammo, Max;
        /// <summary>0 for a gun (the rail calls it G), else 1..n in station order among the others: the digit under the pip and in "STA 3 AGM".</summary>
        public int Slot;
        public bool Reloading;
        /// <summary>How far through the reload it is, 0..1; NaN when the weapon does not say.</summary>
        public float ReloadElapsed;
        /// <summary>Seconds of the reload still to run; NaN when the weapon does not say.</summary>
        public float ReloadLeftS;
        /// <summary>Seconds since the station last fired (large when it has not).</summary>
        public float FiredAgeS;
        /// <summary>The weapon's own picture as a handle (0: none; the presentation's <c>StoreIcons</c> table says which sprite): the type stays free of Unity.</summary>
        public int Icon;
    }

    /// <summary>
    /// The STORES HEADER's feed (Hud's own provider over the aircraft's weapon stations, the countermeasure stations of the board frame and
    /// its power supply): everything the weapon cell, the station rail, the countermeasure cell and the capacitor row draw. A part with no
    /// data is simply absent (<see cref="StationCount"/> 0, <see cref="CmCount"/> 0, <see cref="HasCap"/> false) and its element is not
    /// drawn. The arrays are the provider's own, filled in place; client-local (every peer reads its own aircraft).
    /// </summary>
    internal struct StoresScope
    {
        /// <summary>Enough stations for every aircraft in the game; a longer list is cut (the rail has no room for more).</summary>
        public const int MaxStations = 12;

        public StoreStation[] Stations;
        public int StationCount;
        /// <summary>Index into <see cref="Stations"/> of the weapon the pilot has selected; -1 when none is.</summary>
        public int Selected;
        /// <summary>The selected station's master-arm lock (gear, ground safety) is on.</summary>
        public bool Safe;
        /// <summary>Guns are linked (more than one gun station and the pilot linked them), and the turrets fire at will: tags on the gun's cell.</summary>
        public bool Link, TurretAuto;

        public CmStation[] Cm;
        public int CmCount;
        /// <summary>The seeker classes (<see cref="HudCm.IrMask"/> | <see cref="HudCm.RadarMask"/>) the THREAT page calls countermeasures for right now: their dispenser pulses DISPENSE.</summary>
        public byte Call;

        /// <summary>The aircraft has a power supply with a user (the native capacitor bar's own condition).</summary>
        public bool HasCap;
        public float CapKj, CapMaxKj;
        /// <summary>The charge's measured rate, kJ per second: negative while something drains it.</summary>
        public float CapRate;
    }
}
