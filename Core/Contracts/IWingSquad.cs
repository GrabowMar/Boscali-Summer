using UnityEngine;

namespace BoscaliSummer.Core.Contracts
{
    /// <summary>One saved pilot as the roster studio edits it: identity, service record and look.</summary>
    internal struct CustomPilotView
    {
        public string Name;
        public string Callsign;
        public string DialogueTag;
        public string Background;
        public int Persona;
        public int Xp;
        public int Kills;
        public int Sorties;
        public bool HasPortrait;
        public int Body;
        public int Face;
        public int Hair;
        public int Uniform;
        public int Accessory;
        public int Backdrop;
    }

    /// <summary>
    /// The wing's squadron face for Boscali consumers (Squad ace hunts, the SQD studio,
    /// staff portraits). Wing owns personnel, portraits, adversary flights and survivor
    /// tracking; callers only read and request. Spawns and survivor recovery are
    /// host-authoritative and fail closed off-host. Borrowed portraits must never be
    /// destroyed by the consumer.
    /// </summary>
    internal interface IWingSquad
    {
        /// <returns>Name, callsign, background, persona (int). No roster recruitment.</returns>
        bool TryCreatePilot(int seed, out string name, out string callsign,
            out string background, out int persona);

        Sprite PilotPortrait(string name, string callsign);

        /// <summary>Spawn an intercept flight through the native server spawner at a
        /// host-selected global ingress. Index zero is the ace. Empty when refused.</summary>
        Aircraft[] SpawnWingAt(Aircraft target, FactionHQ enemyHq, int seed, int tier,
            int count, string callsign, float ingressX, float ingressZ);

        bool SetWingTarget(Aircraft[] wing, Aircraft target);

        /// <summary>Release hunt preference to native mission AI, or remove spawned
        /// aircraft without kill rewards.</summary>
        void ReleaseWing(Aircraft[] wing, bool destroy);

        void Chatter(string callsign, string context, string message);

        /// <summary>0 unknown, 1 living dismounted pilot, 2 returned, 3 dead, 4 captured.</summary>
        int SurvivorStatus(PersistentID aircraftId);

        bool RecoverSurvivor(PersistentID aircraftId);

        /// <summary>Active ace perks: bit 0 toughness, 1 countermeasures, 2 notch expert, 3 ghost.</summary>
        int AbilityMask(Aircraft aircraft);

        int PortraitBodyCount { get; }
        int PortraitFaceCount { get; }
        int PortraitHairCount { get; }
        int PortraitUniformCount { get; }
        int PortraitBackdropCount { get; }
        string PortraitBodyLabel(int body);
        string PortraitUniformLabel(int uniform);
        string PersonaLabel(int persona);
        string RankNameForXp(int xp);
        Sprite PortraitForSelection(int body, int face, int hair, int uniform,
            int accessory, int backdrop);
        bool TryGetCustomPilot(string callsign, out CustomPilotView record);
        CustomPilotView[] ListCustomPilots();
        bool SaveCustomPilot(CustomPilotView record);
        bool DeleteCustomPilot(string callsign);
        bool IsPilotRecruited(string callsign);
        bool RecruitCustomPilot(string callsign);
        bool DischargeCustomPilot(string callsign);
        int ImportAllCustomPilots();
    }
}
