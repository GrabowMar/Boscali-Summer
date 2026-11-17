using System;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>The shock front opens a spherical hole through every ambient cloud layer; displaced weather returns slowly.</summary>
    internal sealed class Sa74CloudClearance : MonoBehaviour, ISceneService
    {
        private const int Maximum = 4;
        private static readonly int CountId = Shader.PropertyToID("_CloudBlastCount");
        private static readonly int CentersId = Shader.PropertyToID("_CloudBlastA");
        private static readonly int ShapesId = Shader.PropertyToID("_CloudBlastB");
        private static readonly int ChangeId = Shader.PropertyToID("_CloudBlastChange");
        private readonly Vector4[] centers = new Vector4[Maximum], shapes = new Vector4[Maximum];
        private int previousCount;
        private int previousStamp = int.MinValue, observedEvents = -1;
        private uint observedEpoch;
        private float nextPublish, newestStarted = float.NaN;
        internal static int Publications { get; private set; }

        public void ResetForScene() => Clear();
        private void Update()
        {
            float started = Sa74Fallout.Sites.Count > 0 ? Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1].Started : 0f;
            bool scrubbed = started != newestStarted || observedEvents != Sa74Fallout.Events || observedEpoch != Sa74Fallout.Epoch;
            if (!scrubbed && Time.time < nextPublish)
            {
                if (!Application.isBatchMode) Shader.SetGlobalFloat(ChangeId, 0f);
                return;
            }
            newestStarted = started; observedEvents = Sa74Fallout.Events; observedEpoch = Sa74Fallout.Epoch;
            nextPublish = Time.time + .1f; // Immutable worker snapshots at 10 Hz; the soft edge is at least 600 metres.
            int count = 0, stamp = 17, shadowStamp = 17;
            bool moving = false;
            float maximumRadius = Sa74Fallout.ShockRadius;
            float refill = Sa74BlastShape.ArrivalSeconds(maximumRadius) + 25f;
            for (int i = Sa74Fallout.Sites.Count - 1; i >= 0 && count < Maximum; i--)
            {
                Sa74Fallout.Site site = Sa74Fallout.Sites[i];
                float age = Mathf.Max(0f, Time.time - site.Started);
                float strength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(refill, refill + 60f, age));
                if (strength <= .001f) continue;
                float radius = Sa74Fallout.FrontRadius(age);
                float edge = Mathf.Max(600f, radius * .045f);
                Vector3 global = site.Position.AsVector3();
                centers[count] = new Vector4(global.x, global.y, global.z, radius);
                shapes[count] = new Vector4(strength, edge, 0f, 0f);
                unchecked { stamp = stamp * 31 + (int)site.Id; stamp = stamp * 31 + (int)(radius / 10f); stamp = stamp * 31 + (int)(strength * 100f); }
                // Shadow cookies blend over seconds: invalidate their CPU worker only per kilometre / 5% refill.
                unchecked { shadowStamp = shadowStamp * 31 + (int)site.Id; shadowStamp = shadowStamp * 31 + (int)(radius / 1000f); shadowStamp = shadowStamp * 31 + (int)(strength * 20f); }
                moving |= radius < maximumRadius || strength < .999f;
                count++;
            }
            if (count == 0) stamp = 0;
            if (count == previousCount && stamp == previousStamp)
            {
                if (!Application.isBatchMode) Shader.SetGlobalFloat(ChangeId, 0f);
                return;
            }
            var snapshot = count == 0 ? Array.Empty<CloudClearanceSphere>() : new CloudClearanceSphere[count];
            for (int i = 0; i < count; i++) snapshot[i] = new CloudClearanceSphere(centers[i].x, centers[i].y, centers[i].z, centers[i].w, shapes[i].x, shapes[i].y);
            CloudClearance.Publish(snapshot, count == 0 ? 0 : shadowStamp);
            Publications++;
            if (!Application.isBatchMode)
            {
                Shader.SetGlobalFloat(CountId, count);
                Shader.SetGlobalVectorArray(CentersId, centers);
                Shader.SetGlobalVectorArray(ShapesId, shapes);
                Shader.SetGlobalFloat(ChangeId, scrubbed || moving && Time.deltaTime > 0f || count != previousCount ? 1f : 0f);
            }
            previousCount = count;
            previousStamp = stamp;
        }
        private void Clear()
        {
            previousCount = 0;
            previousStamp = int.MinValue; observedEvents = -1; newestStarted = float.NaN; nextPublish = 0f;
            Publications = 0;
            CloudClearance.Publish(Array.Empty<CloudClearanceSphere>(), 0);
            if (!Application.isBatchMode) { Shader.SetGlobalFloat(CountId, 0f); Shader.SetGlobalFloat(ChangeId, 0f); }
        }
        private void OnDestroy() => Clear();
    }
}
