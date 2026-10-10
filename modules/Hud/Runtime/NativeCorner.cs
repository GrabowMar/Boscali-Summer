using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The one place this module hides native HUD objects behind its own (HUD v8 corner takeover, 2026-10-10): the three children of the native <c>TopRightPanel</c>, <c>weaponPanel</c>
    /// (<c>WeaponStatus.weaponPanel</c>), <c>countermeasuresBackground</c> (<c>CombatHUD.countermeasureBackground</c>) and <c>PowerPanel</c> (the <c>ChargeIndicator</c>'s object), whose
    /// information the board's STORES HEADER draws. Hidden with <see cref="CanvasGroup"/>s this class OWNS at alpha 0: never <c>SetActive(false)</c>, never a disabled native script (the
    /// weapon status, the countermeasure display and the charge indicator keep updating, so audio, targeting and every other hook stay live). FAIL OPEN: <see cref="Apply"/> is told, every
    /// frame, which of the three the board really draws a replacement for (<see cref="NativeTakeover.Hide"/>), and a native box goes back to alpha 1 the same frame it is not replaced;
    /// <see cref="Release"/> and <see cref="Dispose"/> (board hidden, destroyed, scene change, feature off) restore everything.
    /// <para>The panel's own dark plate (its <c>Image</c>, 87 % alpha) is the fourth thing hidden: left alone it would show as a rim around the board's 8 px margin and as an empty slab
    /// under a short board. It can only be hidden through a group on <c>TopRightPanel</c> itself, which dims every child; so each of the three keeps a group of its own that ignores parent
    /// groups (alpha 1 shows it even under a hidden plate: that is the fail-open path) and the board's root ignores them too. The plate is hidden only while the panel holds nothing but the
    /// three boxes and our board; any other child (another mod's) keeps the plate, and so itself, visible.</para>
    /// </summary>
    internal sealed class NativeCorner
    {
        private static readonly AccessTools.FieldRef<CombatHUD, WeaponStatus> WeaponStatusRef = NativeRefs.Field<CombatHUD, WeaponStatus>("weaponStatus");
        private static readonly AccessTools.FieldRef<CombatHUD, GameObject> CountermeasureRef = NativeRefs.Field<CombatHUD, GameObject>("countermeasureBackground");
        private static readonly AccessTools.FieldRef<WeaponStatus, GameObject> WeaponPanelRef = NativeRefs.Field<WeaponStatus, GameObject>("weaponPanel");

        private CanvasGroup plate, weapon, countermeasures, capacitor;
        private Transform panel;
        private int boxes;

        /// <summary>Find the three native boxes under the native <c>TopRightPanel</c> and give each, and the panel itself, an owned group at alpha 1 (nothing hidden yet). Safe to call again after the HUD was rebuilt.</summary>
        public void Find(CombatHUD hud, Transform topRightPanel, Transform board)
        {
            Dispose();
            panel = topRightPanel;
            WeaponStatus status = NativeRefs.Get(WeaponStatusRef, hud);
            weapon = Own(NativeRefs.Get(WeaponPanelRef, status), true);
            countermeasures = Own(NativeRefs.Get(CountermeasureRef, hud), true);
            ChargeIndicator charge = topRightPanel != null ? topRightPanel.GetComponentInChildren<ChargeIndicator>(true) : null;
            capacitor = Own(charge != null ? charge.gameObject : null, true);
            boxes = (weapon != null ? 1 : 0) + (countermeasures != null ? 1 : 0) + (capacitor != null ? 1 : 0);
            plate = Own(topRightPanel != null ? topRightPanel.gameObject : null, false);
            this.board = board;
        }

        private Transform board;

        /// <summary>Our group on <paramref name="target"/>, at alpha 1. Null when there is no object, or Unity would not take a second group beside one the game put there.</summary>
        private static CanvasGroup Own(GameObject target, bool ignoreParent)
        {
            if (target == null) return null;
            CanvasGroup group = target.AddComponent<CanvasGroup>();
            if (group == null) return null;
            group.alpha = 1f;
            group.ignoreParentGroups = ignoreParent;
            return group;
        }

        /// <summary>Hide exactly the boxes whose replacement is drawing; show the rest. Idempotent, cheap (a few comparisons) and meant to run every frame.</summary>
        public void Apply(bool hideWeapon, bool hideCountermeasures, bool hideCapacitor)
        {
            Set(weapon, hideWeapon);
            Set(countermeasures, hideCountermeasures);
            Set(capacitor, hideCapacitor);
            // The plate goes with the first box that goes, but only while nothing else lives under the panel.
            bool alone = panel != null && board != null && panel.childCount == boxes + 1;
            Set(plate, alone && (hideWeapon || hideCountermeasures || hideCapacitor));
        }

        private static void Set(CanvasGroup group, bool hide)
        {
            if (group == null) return;
            float alpha = hide ? 0f : 1f;
            if (group.alpha != alpha) group.alpha = alpha;
        }

        /// <summary>Show everything again (the groups stay, so hiding again needs no search).</summary>
        public void Release() => Apply(false, false, false);

        /// <summary>Restore everything and take our groups off the native objects. Called on scene reset and teardown.</summary>
        public void Dispose()
        {
            Release();
            Drop(ref weapon);
            Drop(ref countermeasures);
            Drop(ref capacitor);
            Drop(ref plate);
            panel = null;
            board = null;
            boxes = 0;
        }

        private static void Drop(ref CanvasGroup group)
        {
            if (group != null) Object.Destroy(group);
            group = null;
        }
    }
}
