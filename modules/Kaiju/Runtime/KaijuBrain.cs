using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Kaiju.Configuration;
using BoscaliSummer.Modules.Kaiju.Domain;
using BoscaliSummer.Modules.Kaiju.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Kaiju.Runtime
{
    /// <summary>
    /// Host-only fight logic for one kaiju: rises out of the ground or sea, marches on the nearest airbase and turns on
    /// any aircraft that comes close. Spikes are IR missiles fired from the spine sacs, the beam is a sphere cast that
    /// aims at where its target was <see cref="KaijuRules.LaserLag"/> seconds ago, the claws kill what the forearm
    /// sweeps through, and every footfall crushes what it lands on. All damage goes through the game's own
    /// <c>TakeDamage</c>, so it replicates. Losing weak spots takes attacks away (<see cref="KaijuRules"/>); the heart
    /// only exists as a target once <see cref="KaijuRules.HeartGuard"/> of them are gone, and killing it ends the fight.
    /// </summary>
    internal sealed class KaijuBrain
    {
        private const float RiseDepth = 700f, RiseSpeed = 55f, GoalRadius = 800f, SinkAfter = 90f, SinkSeconds = 40f;
        private static readonly Collider[] Overlap = new Collider[256];
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static MissileDefinition spikeMissile;
        private static bool spikeLooked;

        public readonly KaijuBody Body;
        public bool Finished { get; private set; }

        private readonly KaijuRules rules = new KaijuRules();
        private readonly KaijuSettings settings;
        private readonly FactionHQ hq;
        private readonly byte gen;
        private readonly Vector3 goal; // global
        private readonly Queue<(float time, Vector3 global)> trail = new Queue<(float, Vector3)>();
        private float ground, nextGround, nextThink, actionEnd, nextBeamTick, nextReveal, deathAt = -1f;
        private bool fired, heartOpen, throatSeen = true;
        private int lastBroken;
        private Aircraft target;
        private Vector3 beamDir;

        public KaijuBrain(byte gen, Vector3 at, float yaw, FactionHQ hq, KaijuSettings settings)
        {
            this.gen = gen;
            this.hq = hq;
            this.settings = settings;
            ground = Ground(at);
            Body = KaijuBody.Create(gen, new GlobalPosition(at.x, ground - RiseDepth, at.z), yaw);
            if (Body == null) return;
            Body.OnStomp = foot => Burn(foot, 180f, 200f, Dealer(Weakspot.Knee));
            goal = NearestAirbase(at);
            for (int i = 0; i < KaijuRig.Spots.Length; i++)
                if (i != KaijuRig.HeartIndex) SpawnNode(i);
            Announce("WARNING: KAIJU EMERGING. HIT THE GLOWING WEAK SPOTS.");
        }

        public void Tick(float dt)
        {
            if (Body == null)
            {
                Finished = true;
                return;
            }
            float now = Time.time;
            if (deathAt >= 0f)
            {
                Sink(now, dt);
                return;
            }
            KaijuDamage d = Damage();
            if (!heartOpen && d.HeartExposed)
            {
                heartOpen = true;
                SpawnNode(KaijuRig.HeartIndex);
                Announce("KAIJU HEART EXPOSED. HIT THE CHEST.");
            }
            if (heartOpen && Body.Dead(KaijuRig.HeartIndex))
            {
                Die(now, d);
                return;
            }
            if (d.Broken > lastBroken)
            {
                if (throatSeen && !d.Throat) Announce("KAIJU THROAT DESTROYED. THE BEAM IS DOWN.");
                if (Body.Action == KaijuAction.Walk || Body.Action == KaijuAction.Idle) Start(KaijuAction.Flinch, now, d);
            }
            lastBroken = d.Broken;
            throatSeen = d.Throat;

            if (now >= nextReveal)
            {
                nextReveal = now + 4f;
                Reveal();
            }
            target = Nearest(Body.Chest, out KaijuSense sense);
            Track(now);
            Body.HasAim = target != null || Body.Action == KaijuAction.LaserFire;
            if (target != null && Body.Action != KaijuAction.LaserFire) Body.Aim = target.transform.position.ToGlobalPosition();

            switch (Body.Action)
            {
                case KaijuAction.Rise:
                    if (Body.Position.AsVector3().y >= ground - 5f) Start(KaijuAction.Roar, now, d);
                    break;
                case KaijuAction.Walk:
                case KaijuAction.Idle:
                    if (now < nextThink) break;
                    nextThink = now + 0.3f;
                    KaijuAction next = rules.Choose(now, sense, d);
                    if (next == KaijuAction.Walk && AtGoal()) next = KaijuAction.Idle;
                    if (next != Body.Action) Start(next, now, d);
                    break;
                case KaijuAction.LaserFire:
                    Beam(now, dt, d);
                    if (now >= actionEnd || !d.Throat) Start(KaijuAction.Walk, now, d);
                    break;
                default:
                    float clip = (now - Body.ActionStart) * KaijuRig.Playback(Body.Action);
                    if (Body.Action == KaijuAction.Spike && !fired && clip >= KaijuRig.SpikeRelease)
                    {
                        fired = true;
                        LaunchSpikes(d);
                    }
                    if ((Body.Action == KaijuAction.SwipeL || Body.Action == KaijuAction.SwipeR) &&
                        clip >= KaijuRig.SwipeHitStart && clip <= KaijuRig.SwipeHitEnd)
                        Swipe(Body.Action == KaijuAction.SwipeL ? -1 : 1);
                    if (now >= actionEnd)
                        Start(Body.Action == KaijuAction.LaserCharge && d.Throat ? KaijuAction.LaserFire : KaijuAction.Walk, now, d);
                    break;
            }
            Move(now, dt, d, sense);
        }

        /// <summary>Remove the hardpoints from the game (the body is the director's to destroy).</summary>
        public void Despawn()
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (Body == null || spawner == null) return;
            foreach (Building node in Body.Nodes)
                if (node != null) spawner.ServerObjectManager.Destroy(node.gameObject);
        }

        /// <summary>Test seam: start an action now, skipping cooldowns.</summary>
        public void Force(KaijuAction action) => Start(action, Time.time, Damage());

        private void Start(KaijuAction action, float now, KaijuDamage d)
        {
            Body.Action = action;
            Body.ActionStart = now;
            float length = KaijuRig.Duration(action);
            actionEnd = length > 0f ? now + length : float.MaxValue;
            fired = false;
            rules.Commit(action, now, d);
            if (action == KaijuAction.LaserFire)
            {
                beamDir = (Body.Aim.ToLocalPosition() - Body.Mouth).normalized;
                nextBeamTick = now;
            }
        }

        private void Move(float now, float dt, KaijuDamage d, KaijuSense sense)
        {
            Vector3 pos = Body.Position.AsVector3();
            if (now >= nextGround)
            {
                nextGround = now + 0.5f;
                ground = Ground(pos);
            }
            bool walking = Body.Action == KaijuAction.Walk, rising = Body.Action == KaijuAction.Rise;
            Vector3 toward = target != null && sense.Distance <= KaijuRules.LaserRange ? target.transform.position.ToGlobalPosition().AsVector3() : goal;
            Vector3 flat = toward - pos;
            flat.y = 0f;
            if (flat.sqrMagnitude > 1f)
            {
                float rate = KaijuRules.TurnRate * (walking || rising ? 1f : Body.Action == KaijuAction.LaserFire ? 0.5f : 0.25f);
                Body.Yaw = Mathf.MoveTowardsAngle(Body.Yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, rate * dt);
            }
            float speed = walking ? KaijuRules.Speed(d) : rising ? 12f : 0f;
            Body.WalkPlayback = rising ? 0.6f : KaijuRules.Speed(d) / KaijuRules.WalkSpeed;
            pos += Quaternion.Euler(0f, Body.Yaw, 0f) * Vector3.forward * (speed * dt);
            pos.y = Mathf.MoveTowards(pos.y, ground, (rising ? RiseSpeed : 25f) * dt);
            Body.Position = new GlobalPosition(pos);
        }

        private bool AtGoal()
        {
            Vector3 flat = goal - Body.Position.AsVector3();
            flat.y = 0f;
            return flat.magnitude < GoalRadius;
        }

        private KaijuDamage Damage()
        {
            var d = new KaijuDamage { Total = KaijuRig.Spots.Length - 1, Broken = Body.Broken };
            for (int i = 0; i < KaijuRig.Spots.Length; i++)
            {
                if (!Body.Alive(i)) continue;
                KaijuRig.Spot s = KaijuRig.Spots[i];
                switch (s.Kind)
                {
                    case Weakspot.Throat: d.Throat = true; break;
                    case Weakspot.Eye: d.Eyes++; break;
                    case Weakspot.Sac: d.Sacs++; break;
                    case Weakspot.Wrist:
                        if (s.Side < 0) d.WristL = true;
                        else d.WristR = true;
                        break;
                    case Weakspot.Knee: d.Knees++; break;
                }
            }
            return d;
        }

        private void SpawnNode(int i)
        {
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            KaijuRig.Spot spot = KaijuRig.Spots[i];
            if (spawner == null) return;
            // Spawner.SpawnBuilding by hand: the kill feed copies the unit name at spawn, so name it first.
            GlobalPosition at = Body.SpotWorld(i).ToGlobalPosition();
            GameObject go = UnityEngine.Object.Instantiate(KaijuAssets.Node);
            go.transform.SetPositionAndRotation(at.ToLocalPosition(), Quaternion.identity);
            Building node = go.GetComponent<Building>();
            node.NetworkHQ = hq;
            node.NetworkUniqueName = KaijuBody.NodePrefix + gen + ":" + i;
            node.NetworkstartPosition = at;
            node.NetworkstartRotation = Quaternion.identity;
            node.NetworkunitName = "KAIJU " + spot.Name;
            spawner.ServerObjectManager.Spawn(go.GetComponent<Mirage.NetworkIdentity>());
            // One knob on top of the donor building's armour, measured with the game's own weapons in tests/ingame/kaiju-tune.json.
            float k = settings.Toughness.Value * (spot.Kind == Weakspot.Heart ? 3f : 1f);
            foreach (UnitPart part in node.GetComponentsInChildren<UnitPart>(true))
            {
                ArmorProperties armor = part.GetArmorProperties();
                if (armor == null) continue;
                armor.pierceTolerance *= k;
                armor.blastTolerance *= k;
                armor.fireTolerance *= k;
            }
            Body.Attach(i, node);
        }

        private Aircraft Nearest(Vector3 chest, out KaijuSense sense)
        {
            sense = new KaijuSense { Distance = float.PositiveInfinity };
            Aircraft best = null;
            float bestScore = float.PositiveInfinity;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || a.disabled) continue;
                float distance = Vector3.Distance(a.transform.position, chest);
                float score = a.radarAlt < 20f ? distance * 3f : distance; // airborne first; parked jets are an afterthought
                if (score >= bestScore) continue;
                bestScore = score;
                best = a;
                sense.Distance = distance;
            }
            if (best != null)
            {
                Vector3 local = Body.transform.InverseTransformDirection(best.transform.position - chest);
                sense.Bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }
            return best;
        }

        /// <summary>Where the target has been over the last few seconds, for the beam's lag.</summary>
        private void Track(float now)
        {
            if (target == null)
            {
                trail.Clear();
                return;
            }
            trail.Enqueue((now, target.transform.position.ToGlobalPosition().AsVector3()));
            while (trail.Count > 0 && trail.Peek().time < now - 3f) trail.Dequeue();
        }

        private void Beam(float now, float dt, KaijuDamage d)
        {
            Vector3 mouth = Body.Mouth;
            float lagged = now - KaijuRules.LaserLag(d);
            Vector3? aim = null;
            foreach ((float time, Vector3 global) in trail)
                if (time >= lagged) { aim = new GlobalPosition(global).ToLocalPosition(); break; }
            if (aim.HasValue)
                beamDir = Vector3.RotateTowards(beamDir, (aim.Value - mouth).normalized, KaijuRules.LaserSweepRate * Mathf.Deg2Rad * dt, 0f);
            Body.Aim = (mouth + beamDir * KaijuRules.LaserRange).ToGlobalPosition();
            if (now < nextBeamTick) return;
            nextBeamTick = now + 0.1f;

            PersistentID dealer = Dealer(Weakspot.Throat);
            int n = Physics.SphereCastNonAlloc(mouth, 35f, beamDir, Hits, KaijuRules.LaserRange, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < n; i++)
                if (Hits[i].collider != null && !Body.Owns(Hits[i].collider) && Hits[i].distance > 0f &&
                    (best < 0 || Hits[i].distance < Hits[best].distance)) best = i;
            if (best < 0) return;
            Unit unit = Hits[best].collider.GetComponentInParent<IDamageable>()?.GetUnit();
            if (unit is Aircraft && !unit.disabled)
                foreach (UnitPart part in unit.GetComponentsInChildren<UnitPart>()) part.TakeDamage(0f, 0f, 1f, 0f, 20f, dealer);
            else Burn(Hits[best].point, 90f, 30f, dealer);
        }

        private void LaunchSpikes(KaijuDamage d)
        {
            MissileDefinition def = SpikeMissile();
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (def == null || spawner == null) return;
            Vector3 chest = Body.Chest;
            var targets = new List<Aircraft>();
            foreach (Aircraft a in UnitRegistry.allAircraft)
                if (a != null && !a.disabled && a.radarAlt >= 20f && Vector3.Distance(a.transform.position, chest) <= KaijuRules.SpikeRange)
                    targets.Add(a);
            targets.Sort((a, b) => Vector3.Distance(a.transform.position, chest).CompareTo(Vector3.Distance(b.transform.position, chest)));
            if (targets.Count > 3) targets.RemoveRange(3, targets.Count - 3);
            var sacs = new List<int>();
            for (int i = 0; i < KaijuRig.Spots.Length; i++)
                if (KaijuRig.Spots[i].Kind == Weakspot.Sac && Body.Alive(i)) sacs.Add(i);
            if (targets.Count == 0 || sacs.Count == 0) return;

            // An IR seeker drops its target at launch unless the launching side tracks it: the kaiju sees everything.
            foreach (Aircraft a in targets) hq?.RpcUpdateTrackingInfo(a.persistentID);
            var ids = new List<uint>();
            int count = KaijuRules.SpikesPerVolley(d);
            for (int i = 0; i < count; i++)
            {
                Building sac = Body.Nodes[sacs[i % sacs.Count]];
                Aircraft aim = targets[i % targets.Count];
                Vector3 from = sac.transform.position + Body.transform.up * 160f + UnityEngine.Random.insideUnitSphere * 40f;
                Vector3 dir = (Vector3.up * 1.5f + (aim.transform.position - from).normalized).normalized;
                Missile missile = spawner.SpawnMissile(def, from, Quaternion.LookRotation(dir), dir * 140f, aim, sac);
                if (missile == null) continue;
                KaijuBody.Restyle(missile);
                ids.Add(missile.persistentID.Id);
            }
            KaijuNet.SendSpikes(ids.ToArray());
        }

        private void Swipe(int side)
        {
            string s = side < 0 ? "_L" : "_R";
            Vector3 elbow = Body.Bone("Elbow" + s).position, wrist = Body.Bone("Wrist" + s).position, claw = Body.Bone("MiddleFinger4" + s).position;
            PersistentID dealer = Dealer(Weakspot.Wrist);
            foreach (Aircraft a in new List<Aircraft>(UnitRegistry.allAircraft))
            {
                if (a == null || a.disabled) continue;
                Vector3 p = a.transform.position;
                if (Mathf.Min(Segment(p, elbow, wrist), Segment(p, wrist, claw)) > 160f) continue;
                foreach (UnitPart part in a.GetComponentsInChildren<UnitPart>()) part.TakeDamage(0f, 0f, 1f, 0f, 400f, dealer);
            }
        }

        /// <summary>Area damage to everything but the kaiju (beam splash, footfalls).</summary>
        private void Burn(Vector3 at, float radius, float impact, PersistentID dealer)
        {
            int n = Physics.OverlapSphereNonAlloc(at, radius, Overlap, ~0, QueryTriggerInteraction.Ignore);
            var seen = new HashSet<IDamageable>();
            for (int i = 0; i < n; i++)
            {
                if (Body.Owns(Overlap[i])) continue;
                IDamageable damageable = Overlap[i].GetComponentInParent<IDamageable>();
                Unit unit = damageable?.GetUnit();
                if (unit == null || unit.disabled || Body.IsKaiju(unit) || !seen.Add(damageable)) continue;
                damageable.TakeDamage(0f, 0f, 1f, 0f, impact, dealer);
            }
            Array.Clear(Overlap, 0, n);
        }

        /// <summary>A mountain needs no spotting: every other side tracks the live weak spots, so HUDs can designate them.</summary>
        private void Reveal()
        {
            foreach (FactionHQ other in FactionRegistry.GetAllHQs())
            {
                if (other == null || other == hq) continue;
                foreach (Building node in Body.Nodes)
                    if (node != null && !node.disabled) other.RpcUpdateTrackingInfo(node.persistentID);
            }
        }

        private void Die(float now, KaijuDamage d)
        {
            Start(KaijuAction.Death, now, d);
            deathAt = now;
            Body.HasAim = false;
            Despawn();
            Announce("KAIJU DOWN.");
            try { settings.Defeated?.Invoke(); } catch (Exception e) { BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Kaiju.Defeated", e); }
        }

        private void Sink(float now, float dt)
        {
            float t = now - deathAt;
            if (t < SinkAfter) return;
            Vector3 pos = Body.Position.AsVector3();
            pos.y -= 30f * dt;
            Body.Position = new GlobalPosition(pos);
            if (t > SinkAfter + SinkSeconds) Finished = true;
        }

        private PersistentID Dealer(Weakspot kind)
        {
            PersistentID any = default;
            for (int i = 0; i < KaijuRig.Spots.Length; i++)
            {
                if (!Body.Alive(i)) continue;
                if (KaijuRig.Spots[i].Kind == kind) return Body.Nodes[i].persistentID;
                if (any.Id == 0) any = Body.Nodes[i].persistentID;
            }
            return any;
        }

        private static float Segment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Global ground height under a point: terrain, or a wading depth below the sea.</summary>
        private static float Ground(Vector3 global)
        {
            Vector3 top = new GlobalPosition(global.x, 8000f, global.z).ToLocalPosition();
            int n = Physics.RaycastNonAlloc(top, Vector3.down, Hits, 12000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            float y = -150f;
            for (int i = 0; i < n; i++)
                if (Hits[i].collider != null && GameAssets.i != null && Hits[i].collider.sharedMaterial == GameAssets.i.terrainMaterial)
                    y = Mathf.Max(y, Hits[i].point.ToGlobalPosition().AsVector3().y);
            return y;
        }

        private static Vector3 NearestAirbase(Vector3 global)
        {
            Vector3 best = global;
            float bestDistance = float.PositiveInfinity;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.center == null) continue;
                Vector3 p = airbase.center.position.ToGlobalPosition().AsVector3();
                float distance = (p - global).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = p;
            }
            return best;
        }

        private static MissileDefinition SpikeMissile()
        {
            if (spikeLooked) return spikeMissile;
            spikeLooked = true;
            foreach (MissileDefinition def in Encyclopedia.i.missiles)
            {
                if (def == null || def.unitPrefab == null || def.unitPrefab.GetComponentInChildren<IRSeeker>(true) == null) continue;
                bool aam = def.jsonKey != null && def.jsonKey.IndexOf("AAM", StringComparison.OrdinalIgnoreCase) >= 0;
                if (spikeMissile == null || aam && spikeMissile.jsonKey.IndexOf("AAM", StringComparison.OrdinalIgnoreCase) < 0) spikeMissile = def;
            }
            Debug.Log("[Kaiju] spike missile: " + (spikeMissile != null ? spikeMissile.jsonKey : "none"));
            return spikeMissile;
        }

        private static void Announce(string text) => NetworkSceneSingleton<MessageManager>.i?.RpcAllHQMessage(text);
    }
}
