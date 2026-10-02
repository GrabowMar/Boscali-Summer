using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Levels and re-projects <c>FlightHud.HUDCenter</c> and the velocity vector in third person
    /// only (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Third-person
    /// HUD"). Vanilla's own <c>FlightHud.Update()</c> already re-projects both every frame using
    /// last frame's camera pose and rolls <c>HUDCenter</c> by the cockpit/camera bank difference;
    /// subscribing to <c>Canvas.preWillRenderCanvases</c> -- which fires after every camera's
    /// <c>LateUpdate</c>, including Wingview's own postfix -- lets this run once more with this
    /// frame's final camera pose and an identity rotation, removing both the one-frame lag and
    /// the bank roll for the third-person reticle/velocity-vector/weapon-state cluster that hangs
    /// under <c>HUDCenter</c>. Nothing is written in cockpit: the gate is checked on every call.
    /// Subscription is tied to <c>HudBoard</c>'s own MonoBehaviour lifetime (Subscribe/Unsubscribe
    /// from its OnEnable/OnDestroy), never a bare static hook that outlives the feature.
    /// </summary>
    internal sealed class ThirdPersonHudCenter
    {
        private static readonly FieldInfo HudCenterField = AccessTools.Field(typeof(FlightHud), "HUDCenter");

        private Func<bool> gate;
        private Func<Aircraft> ownAircraft;
        private bool subscribed;

        public void Subscribe(Func<bool> isThirdPersonActive, Func<Aircraft> ownAircraftGetter)
        {
            gate = isThirdPersonActive;
            ownAircraft = ownAircraftGetter;
            if (subscribed) return;
            Canvas.preWillRenderCanvases += OnPreWillRenderCanvases;
            subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!subscribed) return;
            Canvas.preWillRenderCanvases -= OnPreWillRenderCanvases;
            subscribed = false;
        }

        private void OnPreWillRenderCanvases()
        {
            try
            {
                if (gate == null || !gate()) return;
                Aircraft aircraft = ownAircraft?.Invoke();
                if (aircraft == null || aircraft.cockpit == null) return;

                FlightHud hud = SceneSingleton<FlightHud>.i;
                CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
                if (hud == null || cam == null || cam.mainCamera == null) return;
                Transform hudCenter = HudCenterField?.GetValue(hud) as Transform;
                if (hudCenter == null) return;

                Transform cockpit = aircraft.cockpit.transform;
                Vector3 aimPoint = cockpit.position + cockpit.forward * 4000f;
                Vector3 screenPoint = cam.mainCamera.WorldToScreenPoint(aimPoint);
                if (Vector3.Dot(cam.transform.forward, aimPoint - cam.transform.position) > 0f)
                    screenPoint = Vector3.Scale(screenPoint, new Vector3(1f, 1f, 0f));
                hudCenter.position = screenPoint;
                hudCenter.rotation = Quaternion.identity; // Levelled: no cockpit-bank roll in third person.

                Image velocityVector = hud.velocityVector;
                if (velocityVector == null) return;
                Rigidbody rb = aircraft.CockpitRB();
                if (rb == null) return;
                bool show = rb.velocity.magnitude > 10f;
                if (velocityVector.gameObject.activeSelf != show) velocityVector.gameObject.SetActive(show);
                if (!show) return;

                Vector3 velocityPoint = cockpit.position + rb.velocity * 1000f;
                Vector3 velocityScreen = cam.mainCamera.WorldToScreenPoint(velocityPoint);
                if (Vector3.Dot(cam.transform.forward, rb.velocity) > 0f)
                    velocityScreen = Vector3.Scale(velocityScreen, new Vector3(1f, 1f, 0f));
                velocityVector.transform.position = velocityScreen;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.ThirdPersonHudCenter", e);
            }
        }
    }
}
