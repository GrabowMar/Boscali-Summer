using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal sealed partial class FrontService
    {
        private const float CollectionTickSeconds = 0.25f;
        private const double CollectionSliceMilliseconds = 1.5;

        private sealed class CollectionOrder
        {
            public FactionHQ Owner;
            public OrbitCollection View;
            public int Satellite, Revision;
            public float LossAt;
            public SpaceObservations.OpenWalk Walk;
        }

        private CollectionOrder runningCollection;
        private float nextCollectionTick;

        internal void FillCollections(FactionHQ owner, OrbitCollection[] into)
        {
            if (owner == null || !factions.TryGetValue(owner, out FactionFront f)) return;
            for (int i = 0; i < into.Length; i++) into[i] = f.Collections[i]?.View ?? default;
        }

        internal bool CollectionImminent(FactionHQ owner, BirdKind bird, float now)
        {
            int index = (int)bird;
            if (index >= OrbitCollection.SensorCount || owner == null || !factions.TryGetValue(owner, out FactionFront f)) return false;
            CollectionOrder order = f.Collections[index];
            // Reserve the next opportunity for the operator if a watch scan's cooldown would overlap it.
            return order != null && order.View.Status == OrbitCollection.Queued &&
                order.View.At <= now + SpaceRules.Cooldown(CollectionTask(index)) * 1.5f;
        }

        private FrontResult BookCollection(FactionFront f, int[] ids, float now)
        {
            if (ids == null || ids.Length != 2 || (uint)ids[0] >= OrbitCollection.SensorCount ||
                !GeoSpace.TryUnpack(ids[1], out float u, out float v)) return new FrontResult(FrontOutcome.BadAmount);

            int index = ids[0];
            if (f.Collections[index]?.Walk != null) return new FrontResult(FrontOutcome.Unavailable);
            if (space == null || !manager.ActionEnabled(CollectionAction(index)) || !space.TryGetStateCoarse(f.Owner, out SpaceState state))
                return new FrontResult(FrontOutcome.Unavailable);
            var kind = (BirdKind)index;
            int instance = state.BirdInstance(kind);
            if (instance == 0) return new FrontResult(FrontOutcome.BirdLost);
            GeoBird bird = state.Geo(kind);
            var booking = new CollectionOrder { Owner = f.Owner, Satellite = instance, View = new OrbitCollection { U = u, V = v, Status = OrbitCollection.Queued } };
            if (!Forecast(booking, bird, now + state.ReadyIn(CollectionTask(index), now))) return new FrontResult(FrontOutcome.NoPass);
            f.Collections[index] = booking;
            return new FrontResult(FrontOutcome.PassBooked, Mathf.CeilToInt(booking.View.At - now));
        }

        private static FrontResult CancelCollection(FactionFront f, int index)
        {
            if ((uint)index >= OrbitCollection.SensorCount) return new FrontResult(FrontOutcome.BadAmount);
            CollectionOrder booking = f.Collections[index];
            if (booking == null || booking.Walk != null || booking.View.Status != OrbitCollection.Queued)
                return new FrontResult(FrontOutcome.NoPass);
            f.Collections[index] = null;
            return new FrontResult(FrontOutcome.PassCancelled);
        }

        private static bool Forecast(CollectionOrder booking, in GeoBird bird, float soonest)
        {
            if (!bird.NextPass(soonest, booking.View.U, booking.View.V, GeoSpace.Footprint(bird.Payload, bird.Bus), out OrbitPass pass)) return false;
            booking.View.At = Math.Max(soonest, pass.PeakAt);
            booking.LossAt = pass.EndAt;
            booking.Revision = bird.OrbitRevision;
            return true;
        }

        private void TickCollections(float now)
        {
            if (runningCollection != null)
            {
                CollectionOrder booking = runningCollection;
                try
                {
                    if (booking.Walk.Step(CollectionSliceMilliseconds)) return;
                    RodGuard.Tell(booking.Owner, "ORBIT WATCH · COLLECTION COMPLETE · " + booking.Walk.Admitted + " CONTACTS RETURNED · REVIEW OPERATE");
                    CompleteCollection(booking, OrbitCollection.Collected, now);
                }
                catch (Exception e)
                {
                    CompleteCollection(booking, OrbitCollection.Missed, now);
                    PatchGuard.Report("Support orbital collection", e);
                }
                finally
                {
                    if (booking.Walk == null) runningCollection = null;
                }
            }

            if (space == null || now < nextCollectionTick) return;
            nextCollectionTick = now + CollectionTickSeconds;
            foreach (FactionFront f in order)
            for (int i = 0; i < f.Collections.Length; i++)
            {
                CollectionOrder booking = f.Collections[i];
                if (booking == null) continue;
                if (booking.View.Status != OrbitCollection.Queued)
                {
                    if (now - booking.View.At >= OrbitCollection.RetainSeconds) f.Collections[i] = null;
                    continue;
                }
                if (TryStartCollection(f.Owner, booking, i, now)) return;
            }
        }

        private bool TryStartCollection(FactionHQ owner, CollectionOrder booking, int index, float now)
        {
            var kind = (BirdKind)index;
            BirdTask task = CollectionTask(index);
            SupportActionId action = CollectionAction(index);
            if (!manager.ActionEnabled(action) || !space.TryGetStateCoarse(owner, out SpaceState state) || state.BirdInstance(kind) != booking.Satellite)
            {
                CompleteCollection(booking, OrbitCollection.Missed, now);
                return false;
            }

            GeoBird bird = state.Geo(kind);
            if ((booking.Revision != bird.OrbitRevision && !Forecast(booking, bird, now)) || now > booking.LossAt)
            {
                CompleteCollection(booking, OrbitCollection.Missed, now);
                return false;
            }
            if (now < booking.View.At || !state.Covers(kind, now, booking.View.U, booking.View.V) ||
                !state.TryReserve(task, now, out SpaceTaskReservation receipt)) return false;

            var transaction = new SpaceActionTransaction(space, owner, state, receipt, manager.TaskSecondsOf(action));
            Vector2 span = TheaterFrame.Resolve();
            var point = new GlobalPosition((booking.View.U - 0.5f) * span.x, 0f, (0.5f - booking.View.V) * span.y);
            bool radar = kind == BirdKind.Radar;
            float radius = radar ? manager.Settings.SarSceneRadius.Value : manager.Settings.OpticalSceneRadius.Value;
            try { booking.Walk = space.BeginScan(owner, point, radius, radar, transaction.Commit, booked: true); }
            finally { transaction.Cancel(); }
            if (booking.Walk == null)
            {
                CompleteCollection(booking, OrbitCollection.Missed, now);
                return false;
            }
            runningCollection = booking;
            return true;
        }

        private static void CompleteCollection(CollectionOrder booking, byte status, float now)
        {
            booking.View.Status = status;
            booking.View.At = now;
            booking.Walk = null;
        }

        private static BirdTask CollectionTask(int index) => index == (int)BirdKind.Radar ? BirdTask.Scan : BirdTask.Camera;
        private static SupportActionId CollectionAction(int index) => index == (int)BirdKind.Radar ? SupportActionId.Recon : SupportActionId.SatCamera;
    }
}
