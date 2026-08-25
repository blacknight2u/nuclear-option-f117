using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.Rendering;

namespace Blacknight2u.F117Nighthawk
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("NuclearOption.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "blacknight2u.f117a.nighthawk";
        public const string PluginName = "F-117A Nighthawk";
        public const string PluginVersion = "0.4.65";
        internal const string AircraftKey = "blacknight2u_F117A_Nighthawk";
        internal const string FixedJammerHardpointName = "JammingPod1";
        internal const string LightweightAgmMountKey = "AGM1_quad_internal";

        private static readonly FieldInfo CockpitAircraft =
            AccessTools.Field(typeof(Cockpit), "aircraft");
        private static readonly FieldInfo CockpitTacScreenPrefab =
            AccessTools.Field(typeof(Cockpit), "tacScreenUIPrefab");
        private static readonly FieldInfo MissileRailDirection =
            AccessTools.Field(typeof(MountedMissile), "railDirection");
        private static readonly FieldInfo MissileRailLength =
            AccessTools.Field(typeof(MountedMissile), "railLength");
        private static readonly FieldInfo MissileRailSpeed =
            AccessTools.Field(typeof(MountedMissile), "railSpeed");
        private static readonly FieldInfo MissileRailDelay =
            AccessTools.Field(typeof(MountedMissile), "railDelay");
        private static GameObject nativeTacScreenPrefab;
        private static GameObject nativeHudExtrasPrefab;
        private static bool loggedTacScreenSelection;
        private static bool loggedHudSelection;
        private static bool warnedTacScreenUnavailable;
        private static bool warnedHudUnavailable;
        private static bool loggedHudTelemetryRemoval;
        private static bool loggedFixedJammer;
        private static bool warnedFixedJammerUnavailable;
        private static bool loggedLightweightAgmAdapter;

        private Harmony harmony;
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo(PluginName + " " + PluginVersion + " runtime systems loaded.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        internal static bool IsF117(Aircraft aircraft)
        {
            if (aircraft == null)
                return false;
            if (aircraft.definition != null && aircraft.definition.jsonKey == AircraftKey)
                return true;
            // Definition assignment can lag Awake by one frame on network spawns.
            return aircraft.name.StartsWith("F117A_Nighthawk", StringComparison.Ordinal);
        }

        internal static void AttachRuntime(Aircraft aircraft)
        {
            if (!IsF117(aircraft) || aircraft.GetComponent<F117RuntimeController>() != null)
                return;
            aircraft.gameObject.AddComponent<F117RuntimeController>().Initialize(aircraft);
        }

        internal static bool IsFixedJammerSet(HardpointSet set)
        {
            return set != null && string.Equals(set.name, FixedJammerHardpointName, StringComparison.Ordinal);
        }

        internal static bool IsFixedJammerHardpoint(Hardpoint hardpoint)
        {
            Aircraft aircraft = hardpoint != null && hardpoint.part != null
                ? hardpoint.part.parentUnit as Aircraft
                : null;
            return IsF117(aircraft) && hardpoint.transform != null &&
                string.Equals(hardpoint.transform.name, "F117_FixedJammerSocket", StringComparison.Ordinal);
        }

        internal static WeaponMount FindFixedJammerMount(Aircraft aircraft)
        {
            if (!IsF117(aircraft) || aircraft.weaponManager == null ||
                aircraft.weaponManager.hardpointSets == null)
                return null;
            HardpointSet set = aircraft.weaponManager.hardpointSets.FirstOrDefault(IsFixedJammerSet);
            if (set?.weaponOptions == null)
                return null;
            return set.weaponOptions.FirstOrDefault(mount => mount != null &&
                (string.Equals(mount.jsonKey, "JammingPod1", StringComparison.Ordinal) ||
                 string.Equals(mount.name, "JammingPod1", StringComparison.Ordinal)));
        }

        internal static void EnforceFixedJammer(Aircraft aircraft, Loadout loadout)
        {
            if (!IsF117(aircraft) || loadout == null)
                return;
            WeaponMount jammer = FindFixedJammerMount(aircraft);
            if (jammer == null)
            {
                if (!warnedFixedJammerUnavailable)
                {
                    warnedFixedJammerUnavailable = true;
                    Log.LogError("F-117 native JammingPod1 mount is unavailable; loadout was not modified.");
                }
                return;
            }
            while (loadout.weapons.Count < 2)
                loadout.weapons.Add(null);
            loadout.weapons[1] = jammer;
        }

        internal static void ConcealInstalledJammer(Aircraft aircraft)
        {
            if (!IsF117(aircraft))
                return;
            JammingPod[] jammers = aircraft.GetComponentsInChildren<JammingPod>(true);
            foreach (JammingPod jammer in jammers)
            {
                foreach (Renderer renderer in jammer.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
                foreach (Collider collider in jammer.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
            }
            if (jammers.Length > 0 && !loggedFixedJammer)
            {
                loggedFixedJammer = true;
                Log.LogInfo("F-117 installed the unchanged native JammingPod1 weapon on its concealed fixed station.");
            }
        }

        internal static void ConfigureLightweightAgmBayRelease(
            MountedMissile missile, Aircraft aircraft, WeaponMount mount)
        {
            if (missile == null || !IsF117(aircraft))
                return;

            if (mount == null ||
                (!string.Equals(mount.jsonKey, LightweightAgmMountKey, StringComparison.Ordinal) &&
                 !string.Equals(mount.name, LightweightAgmMountKey, StringComparison.Ordinal)))
                return;

            // AGM1_quad_internal is the only compatible stock bay rack that performs a
            // zero-distance forward launch. That is valid for its donor installation but
            // spawns the missile inside the F-117's fuselage. Use the unchanged native
            // AGM_heavy_internalx2 release motion on this aircraft's cloned rack: while
            // the doors open at 2 normalized units/second, lower the store 2 m at 4 m/s.
            // The live missile therefore spawns below the aircraft after the same 0.5 s.
            MissileRailDirection.SetValue(missile, MountedMissile.RailDirection.Down);
            MissileRailLength.SetValue(missile, 2f);
            MissileRailSpeed.SetValue(missile, 4f);
            MissileRailDelay.SetValue(missile, 0f);

            if (!loggedLightweightAgmAdapter)
            {
                loggedLightweightAgmAdapter = true;
                Log.LogInfo("F-117 adapted AGM1_quad_internal to the native 2 m downward bay-release path.");
            }
        }


        internal static bool PrepareNativeTacScreen(Cockpit cockpit)
        {
            Aircraft aircraft = CockpitAircraft?.GetValue(cockpit) as Aircraft ??
                cockpit.GetComponentInParent<Aircraft>();
            if (!IsF117(aircraft))
                return true;

            if (nativeTacScreenPrefab == null || nativeTacScreenPrefab.GetComponent<TacScreen>() == null)
                nativeTacScreenPrefab = FindNativeTacScreenPrefab();
            if (nativeTacScreenPrefab == null)
            {
                // Skipping the stock initializer is intentional here. Instantiating the
                // engine-only fallback would return no TacScreen and leave a broken UI
                // object behind before throwing a NullReferenceException.
                if (!warnedTacScreenUnavailable)
                {
                    warnedTacScreenUnavailable = true;
                    Log.LogError("F-117 native tactical-screen prefab was unavailable; cockpit screen disabled safely.");
                }
                return false;
            }

            CockpitTacScreenPrefab.SetValue(cockpit, nativeTacScreenPrefab);
            if (!loggedTacScreenSelection)
            {
                loggedTacScreenSelection = true;
                Log.LogInfo("F-117 cockpit is using native tactical screen '" + nativeTacScreenPrefab.name + "'.");
            }
            return true;
        }

        internal static void PrepareNativeHud(Aircraft aircraft)
        {
            if (!IsF117(aircraft))
                return;
            AircraftParameters parameters = aircraft.GetAircraftParameters();
            if (parameters == null)
                return;

            if (nativeHudExtrasPrefab == null || !HasRuntimeHudController(nativeHudExtrasPrefab))
                nativeHudExtrasPrefab = FindNativeHudExtrasPrefab();

            // Null is a safe fallback: FlightHud's standard compass, pitch ladder,
            // velocity vector, and targeting symbology still operate. It is preferable
            // to instantiating a scriptless custom prefab that can cover the viewport.
            parameters.HUDExtras = nativeHudExtrasPrefab;
            if (nativeHudExtrasPrefab != null)
            {
                if (!loggedHudSelection)
                {
                    loggedHudSelection = true;
                    Log.LogInfo("F-117 HUD is using deterministic native extras '" + nativeHudExtrasPrefab.name + "'.");
                }
            }
            else if (!warnedHudUnavailable)
            {
                warnedHudUnavailable = true;
                Log.LogWarning("F-117 native HUD extras were unavailable; retaining the standard FlightHud only.");
            }
        }


















































        private static GameObject FindNativeTacScreenPrefab()
        {
            TacScreen[] candidates = Resources.FindObjectsOfTypeAll<TacScreen>();
            var ranked = new List<Tuple<int, string, GameObject>>();
            foreach (TacScreen candidate in candidates)
            {
                if (candidate == null)
                    continue;
                GameObject prefab = candidate.gameObject;
                string name = prefab.name ?? string.Empty;
                if (name.IndexOf("F117", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Aryx", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                int score = prefab.scene.IsValid() ? 0 : 100;
                score += NativeAircraftPriority(name);
                string identity = name + " " + prefab.GetInstanceID().ToString("D10");
                ranked.Add(Tuple.Create(score, identity, prefab));
            }
            return ranked
                .OrderByDescending(item => item.Item1)
                .ThenBy(item => item.Item2, StringComparer.Ordinal)
                .Select(item => item.Item3)
                .FirstOrDefault();
        }

        private static GameObject FindNativeHudExtrasPrefab()
        {
            AircraftParameters[] candidates = Resources.FindObjectsOfTypeAll<AircraftParameters>();
            var ranked = new List<Tuple<int, string, GameObject>>();
            foreach (AircraftParameters candidate in candidates)
            {
                if (candidate == null || candidate.HUDExtras == null ||
                    !HasRuntimeHudController(candidate.HUDExtras))
                    continue;
                string identity = (candidate.aircraftName ?? string.Empty) + " " +
                    (candidate.name ?? string.Empty) + " " + candidate.HUDExtras.name;
                if (identity.IndexOf("F117", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    identity.IndexOf("Aryx", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                int score = candidate.HUDExtras.scene.IsValid() ? 0 : 100;
                score += NativeAircraftPriority(identity);
                ranked.Add(Tuple.Create(score, identity, candidate.HUDExtras));
            }
            return ranked
                .OrderByDescending(item => item.Item1)
                .ThenBy(item => item.Item2, StringComparer.Ordinal)
                .Select(item => item.Item3)
                .FirstOrDefault();
        }

        internal static void RemoveDonorEngineTelemetry(Aircraft aircraft)
        {
            if (!IsF117(aircraft) || nativeHudExtrasPrefab == null || SceneSingleton<FlightHud>.i == null)
                return;
            Transform center = SceneSingleton<FlightHud>.i.GetHUDCenter();
            if (center == null)
                return;
            string cloneName = nativeHudExtrasPrefab.name + "(Clone)";
            Transform instance = center.GetComponentsInChildren<Transform>(true)
                .LastOrDefault(item => item != center && item.name == cloneName);
            if (instance == null)
                return;
            EngineTelemetry[] telemetry = instance.GetComponentsInChildren<EngineTelemetry>(true);
            foreach (EngineTelemetry component in telemetry)
                if (component != null)
                    UnityEngine.Object.Destroy(component);
            if (telemetry.Length > 0 && !loggedHudTelemetryRemoval)
            {
                loggedHudTelemetryRemoval = true;
                Log.LogInfo("F-117 removed " + telemetry.Length +
                    " donor engine-telemetry widgets from its private HUD instance.");
            }
        }

        private static bool HasRuntimeHudController(GameObject prefab)
        {
            if (prefab == null)
                return false;
            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null)
                    return true;
            }
            return false;
        }

        private static int NativeAircraftPriority(string identity)
        {
            string[] preferred = { "Revoker", "Medusa", "Compass", "Chicane", "Cricket", "Darkreach" };
            for (int index = 0; index < preferred.Length; index++)
                if (identity.IndexOf(preferred[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    return preferred.Length - index;
            return 0;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AircraftAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            Plugin.AttachRuntime(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SpawnedInPosition))]
    internal static class AircraftSpawnedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            Plugin.AttachRuntime(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "set_Networkloadout")]
    internal static class F117FixedJammerLoadoutPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft __instance, ref Loadout __0)
        {
            Plugin.EnforceFixedJammer(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SpawnWeapons))]
    internal static class F117FixedJammerSpawnPatch
    {
        [HarmonyPrefix]
        private static void Prefix(WeaponManager __instance)
        {
            Aircraft aircraft = __instance == null ? null : __instance.GetComponentInParent<Aircraft>();
            if (aircraft != null)
                Plugin.EnforceFixedJammer(aircraft, aircraft.loadout);
        }

        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            Aircraft aircraft = __instance == null ? null : __instance.GetComponentInParent<Aircraft>();
            Plugin.ConcealInstalledJammer(aircraft);
        }
    }

    [HarmonyPatch(typeof(MountedMissile), nameof(MountedMissile.AttachToHardpoint))]
    internal static class F117LightweightAgmBayReleasePatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            MountedMissile __instance, Aircraft aircraft, WeaponMount weaponMount)
        {
            // AttachToHardpoint derives its cached rail vector inside the original method,
            // so the direction must be set before that calculation runs.
            Plugin.ConfigureLightweightAgmBayRelease(__instance, aircraft, weaponMount);
        }
    }

    [HarmonyPatch(typeof(LoadoutSelector), nameof(LoadoutSelector.GenerateLoadoutFromDropdowns))]
    internal static class F117HangarLoadoutJammerPatch
    {
        private static readonly FieldInfo AircraftField = AccessTools.Field(typeof(LoadoutSelector), "aircraft");

        [HarmonyPostfix]
        private static void Postfix(LoadoutSelector __instance, ref Loadout __result)
        {
            Plugin.EnforceFixedJammer(AircraftField?.GetValue(__instance) as Aircraft, __result);
        }
    }

    [HarmonyPatch]
    internal static class F117FixedJammerSelectorPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(WeaponSelector).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name == nameof(WeaponSelector.Initialize) &&
                    method.GetParameters().Any(parameter => parameter.ParameterType == typeof(HardpointSet)));
        }

        [HarmonyPostfix]
        private static void Postfix(WeaponSelector __instance, object[] __args)
        {
            HardpointSet set = __args?.OfType<HardpointSet>().FirstOrDefault();
            if (Plugin.IsFixedJammerSet(set))
                __instance.SetHidden(true);
        }
    }

    [HarmonyPatch]
    internal static class F117CockpitInitializePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Cockpit), "Cockpit_OnAircraftInitialize");
        }

        [HarmonyPrefix]
        private static bool Prefix(Cockpit __instance)
        {
            return Plugin.PrepareNativeTacScreen(__instance);
        }
    }

    [HarmonyPatch]
    internal static class F117HudInitializePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Aircraft), "SetupLocalPlayerAndUI");
        }

        [HarmonyPrefix]
        private static void Prefix(Aircraft __instance)
        {
            Plugin.PrepareNativeHud(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            if (!Plugin.IsF117(__instance))
                return;
            Plugin.RemoveDonorEngineTelemetry(__instance);
            F117RuntimeController controller = __instance.GetComponent<F117RuntimeController>();
            controller?.ScheduleInitialCockpitCameraRefresh();
        }
    }

    [HarmonyPatch(typeof(ThrottleGauge), nameof(ThrottleGauge.Initialize))]
    internal static class F117ThrottleGaugePatch
    {
        private static readonly FieldInfo Airbrake =
            AccessTools.Field(typeof(ThrottleGauge), "airbrake");
        private static readonly FieldInfo Afterburner =
            AccessTools.Field(typeof(ThrottleGauge), "afterburner");
        private static readonly FieldInfo ThrottleRegions =
            AccessTools.Field(typeof(ThrottleGauge), "throttleRegions");
        private static readonly FieldInfo CurrentRegion =
            AccessTools.Field(typeof(ThrottleGauge), "currentRegion");
        private static readonly FieldInfo ThrottleBoundaryPivot =
            AccessTools.Field(typeof(ThrottleGauge), "throttleBoundaryPivot");
        private static bool logged;

        [HarmonyPrefix]
        private static void Prefix(ThrottleGauge __instance, Aircraft aircraft)
        {
            if (!Plugin.IsF117(aircraft) || Afterburner == null)
                return;

            // Match the stock non-afterburning fixed-wing HUD contract used by
            // SFB_HUDExtras, Trainer_HUDExtras, and VTOLTrainer1_HUDExtras:
            // afterburner=false, zero throttle regions, and no boundary pivot.
            // The borrowed Fighter1 regions cannot be retained because its MIL
            // region can remain latched after the AB region is removed. These
            // fields are display-only and do not affect engine thrust.
            Airbrake?.SetValue(__instance, false);
            Afterburner.SetValue(__instance, false);
            if (ThrottleRegions?.GetValue(__instance) is Array regions &&
                regions.GetType().GetElementType() is Type elementType)
            {
                ThrottleRegions.SetValue(__instance, Array.CreateInstance(elementType, 0));
            }
            CurrentRegion?.SetValue(__instance, null);
            Transform boundary = ThrottleBoundaryPivot?.GetValue(__instance) as Transform;
            if (boundary != null)
                boundary.gameObject.SetActive(false);
            ThrottleBoundaryPivot?.SetValue(__instance, null);
            if (!logged)
            {
                logged = true;
                Plugin.Log.LogInfo("F-117 throttle gauge is configured for its actual dry-thrust controls " +
                    "(no AIRBRAKE or AFTERBURNER label, no throttle regions, no AB boundary).");
            }
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), nameof(CameraCockpitState.EnterState))]
    internal static class CockpitCameraEnterPatch
    {
        private static readonly FieldInfo RelativePosition =
            AccessTools.Field(typeof(CameraCockpitState), "camRelativePos");
        private static readonly FieldInfo RelativeVelocity =
            AccessTools.Field(typeof(CameraCockpitState), "camRelativeVel");

        [HarmonyPrefix]
        private static void Prefix(CameraCockpitState __instance, CameraStateManager cam)
        {
            Aircraft aircraft = cam == null ? null : cam.followingUnit as Aircraft;
            if (!Plugin.IsF117(aircraft))
                return;

            // CameraCockpitState is reused when the player receives a replacement
            // aircraft. The stock EnterState method resets pan and tilt but leaves
            // these crash/inertia offsets behind, so a second F-117 can inherit the
            // previous airframe's displaced eye position until the game is restarted.
            RelativePosition?.SetValue(__instance, Vector3.zero);
            RelativeVelocity?.SetValue(__instance, Vector3.zero);
        }
    }

    [HarmonyPatch]
    internal static class F117LandingGearArticulationPatch
    {
        private static readonly FieldInfo FoldAmount =
            AccessTools.Field(typeof(LandingGear), "foldAmount");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(LandingGear), "UpdateMovingParts");
        }

        [HarmonyPostfix]
        private static void Postfix(LandingGear __instance)
        {
            Aircraft aircraft = __instance == null ? null : __instance.GetComponentInParent<Aircraft>();
            if (!Plugin.IsF117(aircraft) || FoldAmount == null)
                return;

            object value = FoldAmount.GetValue(__instance);
            if (!(value is float amount) || float.IsNaN(amount))
                return;
            F117GearArticulation articulation =
                __instance.GetComponent<F117GearArticulation>() ??
                __instance.gameObject.AddComponent<F117GearArticulation>();
            // LandingGear deliberately lets foldAmount exceed one while the
            // outer doors close. Preserve that staging signal here; the
            // articulation component clamps only the strut-driven tracks.
            articulation.Apply(amount, __instance);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class F117GearArticulation : MonoBehaviour
    {
        private const int GearPoseCount = 9;
        private const int DoorPoseCount = 17;
        private static readonly FieldInfo GearDoors =
            AccessTools.Field(typeof(LandingGear), "gearDoors");

        private sealed class Link
        {
            internal Transform Transform;
            internal Transform[] Poses;
        }

        private sealed class DoorLink
        {
            internal Transform Transform;
            internal Transform[] GearPoses;
            internal Transform[] ClosePoses;
        }

        private Link[] links;
        private DoorLink[] doorLinks;
        private LandingGear.GearDoor outerDoor;
        private LandingGear.GearDoor innerDoor;
        private bool initialized;

        internal void Apply(float rawAmount, LandingGear gear)
        {
            if (!initialized)
                Initialize(gear);

            float gearAmount = Mathf.Clamp01(rawAmount);
            foreach (Link link in links ?? Array.Empty<Link>())
                ApplyPose(link.Transform, link.Poses, gearAmount);

            // The original inner main doors are mechanically linked to the
            // struts: open with deployed gear and closed with stowed gear. The
            // stock game otherwise holds every door open for the complete fold
            // and closes all of them afterward, which is only correct for the
            // outer panels.
            if (innerDoor != null && innerDoor.transform != null)
            {
                innerDoor.transform.localRotation = Quaternion.Slerp(
                    Quaternion.Euler(innerDoor.openAngle),
                    Quaternion.Euler(innerDoor.closedAngle),
                    gearAmount);
            }

            // The restored outer-door linkages have two source-derived tracks:
            // one during strut travel and one during the native outer-door stage.
            float outerOpen = OuterDoorOpenFraction();
            bool doorStage = outerDoor != null && outerOpen < 0.999f;
            float linkageAmount = doorStage ? 1f - outerOpen : gearAmount;
            foreach (DoorLink link in doorLinks ?? Array.Empty<DoorLink>())
                ApplyPose(link.Transform,
                    doorStage ? link.ClosePoses : link.GearPoses,
                    linkageAmount);
        }

        private static void ApplyPose(Transform target, Transform[] poses, float amount)
        {
            if (target == null || poses == null || poses.Length < 2)
                return;
            float scaled = Mathf.Clamp01(amount) * (poses.Length - 1);
            int lower = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, poses.Length - 1);
            int upper = Mathf.Min(lower + 1, poses.Length - 1);
            if (poses[lower] == null || poses[upper] == null)
                return;
            float blend = scaled - lower;
            target.localPosition = Vector3.LerpUnclamped(
                poses[lower].localPosition, poses[upper].localPosition, blend);
            target.localRotation = Quaternion.SlerpUnclamped(
                poses[lower].localRotation, poses[upper].localRotation, blend);
            target.localScale = Vector3.LerpUnclamped(
                poses[lower].localScale, poses[upper].localScale, blend);
        }

        private float OuterDoorOpenFraction()
        {
            if (outerDoor == null || outerDoor.transform == null)
                return 1f;
            Quaternion closed = Quaternion.Euler(outerDoor.closedAngle);
            Quaternion open = Quaternion.Euler(outerDoor.openAngle);
            float fullTravel = Quaternion.Angle(closed, open);
            if (fullTravel < 0.01f)
                return 1f;
            return Mathf.Clamp01(
                Quaternion.Angle(closed, outerDoor.transform.localRotation) / fullTravel);
        }

        private void Initialize(LandingGear gear)
        {
            initialized = true;
            Transform[] descendants = GetComponentsInChildren<Transform>(true);
            var byName = descendants.ToDictionary(transform => transform.name, transform => transform,
                StringComparer.Ordinal);
            var result = new List<Link>();
            foreach (Transform transform in descendants
                .Where(item => item.name.IndexOf("_Link_", StringComparison.Ordinal) >= 0)
                .OrderBy(item => item.name, StringComparer.Ordinal))
            {
                int marker = transform.name.LastIndexOf("_Link_", StringComparison.Ordinal);
                string prefix = transform.name.Substring(0, marker);
                string index = transform.name.Substring(marker + "_Link_".Length);
                var poses = new Transform[GearPoseCount];
                bool complete = true;
                for (int poseIndex = 0; poseIndex < GearPoseCount; poseIndex++)
                {
                    string poseName = prefix + "_Pose_" + index + "_" + poseIndex.ToString("D2");
                    complete &= byName.TryGetValue(poseName, out poses[poseIndex]);
                }
                if (!complete)
                {
                    Plugin.Log.LogError("F-117 articulated gear link '" + transform.name +
                        "' is missing one or more source pose locators.");
                    continue;
                }
                result.Add(new Link { Transform = transform, Poses = poses });
            }
            links = result.ToArray();

            var staged = new List<DoorLink>();
            foreach (Transform transform in descendants
                .Where(item => item.name.IndexOf("_DoorTrack_", StringComparison.Ordinal) >= 0)
                .OrderBy(item => item.name, StringComparer.Ordinal))
            {
                int marker = transform.name.LastIndexOf("_DoorTrack_", StringComparison.Ordinal);
                string prefix = transform.name.Substring(0, marker);
                string index = transform.name.Substring(marker + "_DoorTrack_".Length);
                var gearPoses = new Transform[DoorPoseCount];
                var closePoses = new Transform[DoorPoseCount];
                bool complete = true;
                for (int poseIndex = 0; poseIndex < DoorPoseCount; poseIndex++)
                {
                    string suffix = index + "_" + poseIndex.ToString("D2");
                    complete &= byName.TryGetValue(
                        prefix + "_DoorGearPose_" + suffix, out gearPoses[poseIndex]);
                    complete &= byName.TryGetValue(
                        prefix + "_DoorClosePose_" + suffix, out closePoses[poseIndex]);
                }
                if (!complete)
                {
                    Plugin.Log.LogError("F-117 staged door linkage '" + transform.name +
                        "' is missing one or more source pose locators.");
                    continue;
                }
                staged.Add(new DoorLink
                {
                    Transform = transform,
                    GearPoses = gearPoses,
                    ClosePoses = closePoses
                });
            }
            doorLinks = staged.ToArray();

            if (GearDoors != null && gear != null &&
                GearDoors.GetValue(gear) is IEnumerable<LandingGear.GearDoor> doors)
            {
                foreach (LandingGear.GearDoor door in doors)
                {
                    if (door == null || door.transform == null)
                        continue;
                    if (door.transform.name.IndexOf("_Outer_", StringComparison.OrdinalIgnoreCase) >= 0)
                        outerDoor = door;
                    else if (door.transform.name.IndexOf("_Inner_", StringComparison.OrdinalIgnoreCase) >= 0)
                        innerDoor = door;
                }
            }

            Plugin.Log.LogInfo(name + " initialized " + links.Length +
                " strut linkage tracks and " + doorLinks.Length +
                " staged outer-door linkage tracks" +
                (innerDoor == null ? "." : "; inner door follows the source strut phase."));
        }
    }

    [HarmonyPatch]
    internal static class F117BayDoorArticulationPatch
    {
        private static readonly FieldInfo OpenAmount =
            AccessTools.Field(typeof(BayDoor), "openAmount");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(BayDoor), "Update");
        }

        [HarmonyPostfix]
        private static void Postfix(BayDoor __instance)
        {
            Aircraft aircraft = __instance == null ? null : __instance.GetComponentInParent<Aircraft>();
            if (!Plugin.IsF117(aircraft) || OpenAmount == null)
                return;
            object value = OpenAmount.GetValue(__instance);
            if (!(value is float amount) || float.IsNaN(amount))
                return;
            F117BayDoorArticulation articulation =
                __instance.GetComponent<F117BayDoorArticulation>() ??
                __instance.gameObject.AddComponent<F117BayDoorArticulation>();
            articulation.Apply(amount);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class F117BayDoorArticulation : MonoBehaviour
    {
        private const int PoseCount = 9;

        private sealed class Link
        {
            internal Transform Transform;
            internal Transform[] Poses;
        }

        private Link[] links;
        private bool initialized;

        internal void Apply(float amount)
        {
            if (!initialized)
                Initialize();
            foreach (Link link in links ?? Array.Empty<Link>())
                ApplyPose(link.Transform, link.Poses, amount);
        }

        private static void ApplyPose(Transform target, Transform[] poses, float amount)
        {
            if (target == null || poses == null || poses.Length < 2)
                return;
            float scaled = Mathf.Clamp01(amount) * (poses.Length - 1);
            int lower = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, poses.Length - 1);
            int upper = Mathf.Min(lower + 1, poses.Length - 1);
            if (poses[lower] == null || poses[upper] == null)
                return;
            float blend = scaled - lower;
            target.localPosition = Vector3.LerpUnclamped(
                poses[lower].localPosition, poses[upper].localPosition, blend);
            target.localRotation = Quaternion.SlerpUnclamped(
                poses[lower].localRotation, poses[upper].localRotation, blend);
            target.localScale = Vector3.LerpUnclamped(
                poses[lower].localScale, poses[upper].localScale, blend);
        }

        private void Initialize()
        {
            initialized = true;
            Transform[] descendants = GetComponentsInChildren<Transform>(true);
            var byName = descendants.ToDictionary(transform => transform.name, transform => transform,
                StringComparer.Ordinal);
            var result = new List<Link>();
            foreach (Transform transform in descendants
                .Where(item => item.name.IndexOf("_BayLink_", StringComparison.Ordinal) >= 0)
                .OrderBy(item => item.name, StringComparer.Ordinal))
            {
                int marker = transform.name.LastIndexOf("_BayLink_", StringComparison.Ordinal);
                string prefix = transform.name.Substring(0, marker);
                string index = transform.name.Substring(marker + "_BayLink_".Length);
                var poses = new Transform[PoseCount];
                bool complete = true;
                for (int poseIndex = 0; poseIndex < PoseCount; poseIndex++)
                {
                    string poseName = prefix + "_BayPose_" + index + "_" + poseIndex.ToString("D2");
                    complete &= byName.TryGetValue(poseName, out poses[poseIndex]);
                }
                if (!complete)
                {
                    Plugin.Log.LogError("F-117 bomb-bay linkage '" + transform.name +
                        "' is missing one or more source pose locators.");
                    continue;
                }
                result.Add(new Link { Transform = transform, Poses = poses });
            }
            links = result.ToArray();
            Plugin.Log.LogInfo(name + " initialized " + links.Length +
                " source-derived bomb-bay linkage tracks.");
        }
    }

    [DisallowMultipleComponent]
    public sealed class F117RuntimeController : MonoBehaviour
    {
        // T.O. 1F-117A-1 limits drag-chute deployment to 215 KCAS, requires the
        // nose gear on the runway and the aircraft aligned, and calls for
        // jettison at approximately 20 knots ground speed. These are maximum
        // deployment and low-speed jettison thresholds, not the inverse.
        private const float MaximumChuteDeploymentSpeed = 110.61f; // 215 knots
        private const float ChuteJettisonSpeed = 10.29f;            // 20 knots
        private const float MinimumLandingTouchdownSpeed = 25f;     // excludes taxi/bump events
        private const float AirborneEvidenceDuration = 0.75f;
        private const float WheelLoadThreshold = 0f;                // any positive suspension compression
        private const float MaximumDeploymentSinkRate = 6f;
        private const float MaximumRunwayAlignmentAngle = 12f;
        private const float ChuteDragArea = 24f;              // effective CdA in square metres
        private const float MaximumChuteForce = 120000f;
        // Keep the clean aircraft exceptionally difficult, but not mathematically impossible, to
        // detect. Each bay and the landing gear contribute independently and continuously so the
        // signature follows the actual native animation instead of switching between magic states.
        private const float CleanRcs = 0.0001f;
        private const float FullyOpenBayRcsPerDoor = 0.04f;
        private const float FullyDeployedGearRcs = 0.05f;
        private static readonly FieldInfo BayOpenAmount = AccessTools.Field(typeof(BayDoor), "openAmount");
        private static readonly FieldInfo GearFoldAmount = AccessTools.Field(typeof(LandingGear), "foldAmount");
        private static readonly FieldInfo TacScreenMaterial = AccessTools.Field(typeof(TacScreen), "screenMaterial");
        private static readonly FieldInfo TacScreenRenderTexture = AccessTools.Field(typeof(TacScreen), "renderTexture");
        private static readonly FieldInfo TacScreenCamera = AccessTools.Field(typeof(TacScreen), "cam");
        private static readonly FieldInfo CockpitTacScreenRender = AccessTools.Field(typeof(Cockpit), "tacScreenRender");
        private Aircraft aircraft;
        private Rigidbody body;
        private ControlInputs inputs;
        private Transform chute;
        private Transform leftDoor;
        private Transform rightDoor;
        private Transform leftOpenTarget;
        private Transform rightOpenTarget;
        private BayDoor[] weaponBayDoors;
        private LandingGear[] landingGears;
        private LandingGear noseGear;
        private LandingGear[] mainGears;
        private Quaternion leftClosed;
        private Quaternion rightClosed;
        private Vector3 chuteFullScale = Vector3.one;
        private bool initialized;
        private bool chuteAvailable;
        private bool stealthAvailable;
        private bool deployed;
        private bool spent;
        private bool wasAirborne;
        private bool landingRollArmed;
        private bool previousMainGearContact;
        private bool cockpitDisplayBound;
        private Coroutine initialCameraRefresh;
        private float openAmount;
        private float airborneEvidenceTime;

        public void Initialize(Aircraft owner)
        {
            if (initialized)
                return;
            aircraft = owner;
            body = owner.GetComponent<Rigidbody>();
            inputs = owner.GetInputs();
            chute = FindDeep(owner.transform, "F117_DragChute");
            leftDoor = FindDeep(owner.transform, "F117_ChuteDoor_Left");
            rightDoor = FindDeep(owner.transform, "F117_ChuteDoor_Right");
            leftOpenTarget = FindDeep(owner.transform, "LOC_ChuteDoor_Left_Open");
            rightOpenTarget = FindDeep(owner.transform, "LOC_ChuteDoor_Right_Open");
            weaponBayDoors = owner.GetComponentsInChildren<BayDoor>(true);
            landingGears = owner.GetComponentsInChildren<LandingGear>(true);
            noseGear = landingGears.FirstOrDefault(gear =>
                gear != null && gear.name.IndexOf("Nose", StringComparison.OrdinalIgnoreCase) >= 0);
            mainGears = landingGears.Where(gear => gear != null && gear != noseGear).ToArray();
            RestoreCockpitTransparentSurfaces(owner);

            if (owner.cockpitViewPoint != null)
            {
                Vector3 eye = owner.transform.InverseTransformPoint(owner.cockpitViewPoint.position);
                Plugin.Log.LogInfo("F-117 cockpit eye point initialized at local " + eye.ToString("F3") + ".");
            }

            chuteAvailable = chute != null && leftDoor != null && rightDoor != null &&
                leftOpenTarget != null && rightOpenTarget != null && body != null &&
                noseGear != null && mainGears.Length == 2;
            stealthAvailable = weaponBayDoors != null && weaponBayDoors.Length == 2 && BayOpenAmount != null;

            if (!chuteAvailable && !stealthAvailable)
            {
                Plugin.Log.LogError("F-117 runtime geometry is incomplete; runtime systems disabled on " + owner.name + ".");
                enabled = false;
                return;
            }

            if (chuteAvailable)
            {
                leftClosed = leftDoor.localRotation;
                rightClosed = rightDoor.localRotation;
                chuteFullScale = chute.localScale;
                chute.gameObject.SetActive(false);
                previousMainGearContact = MainGearCarryWeight();
            }
            else
            {
                Plugin.Log.LogWarning("F-117 drag-chute geometry is incomplete; stealth remains active on " + owner.name + ".");
            }

            initialized = true;
            if (stealthAvailable)
            {
                UpdateStealthSignature();
                Plugin.Log.LogInfo("F-117 low-observable controller active: clean RCS " + CleanRcs.ToString("0.0000") +
                    ", each fully open bay +" + FullyOpenBayRcsPerDoor.ToString("0.00") +
                    ", fully deployed gear +" + FullyDeployedGearRcs.ToString("0.00") + ".");
            }
            else
            {
                Plugin.Log.LogError("F-117 weapon-bay state is unavailable; dynamic stealth disabled on " + owner.name + ".");
            }

        }

        private static void RestoreCockpitTransparentSurfaces(Aircraft owner)
        {
            var restored = new HashSet<Material>();
            int restoredCount = 0;
            foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !restored.Add(material))
                        continue;
                    string name = material.name ?? string.Empty;
                    bool hudGlass = name.IndexOf("F117_int_glass_hud_front", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hudProjection = name.EndsWith("_HUD", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "HUD", StringComparison.OrdinalIgnoreCase);
                    if (!hudGlass && !hudProjection)
                        continue;

                    // Blueprinter replaces the bundle's placeholder URP shader with the
                    // game's real URP/Lit shader. Re-assert its transparent render state
                    // after that replacement; serialized placeholder keywords are not a
                    // valid runtime transparency contract and made the HUD combiner's
                    // thin side faces appear as tall opaque black panels.
                    Color tint = new Color(0.45f, 0.45f, 0.45f, hudProjection ? 0.005f : 0.02f);
                    material.SetColor("_BaseColor", tint);
                    material.SetColor("_Color", tint);
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.1f);
                    material.SetFloat("_EnvironmentReflections", 0f);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetShaderPassEnabled("ShadowCaster", false);
                    material.renderQueue = (int)RenderQueue.Transparent;
                    restoredCount++;
                }
            }
            if (restoredCount > 0)
                Plugin.Log.LogInfo("F-117 restored runtime transparency on " + restoredCount +
                    " HUD combiner material(s).");
        }

        public void ScheduleInitialCockpitCameraRefresh()
        {
            if (initialCameraRefresh != null)
                StopCoroutine(initialCameraRefresh);
            initialCameraRefresh = StartCoroutine(RefreshInitialCockpitCamera());
        }

        private IEnumerator RefreshInitialCockpitCamera()
        {
            // SetupLocalPlayerAndUI first enters orbit through SetFollowingUnit and
            // immediately enters cockpit in the same frame. On this aircraft that
            // first transition consistently retains the old camera pose; manually
            // cycling views fixes it because CockpitState is entered again. Repeat
            // that clean state entry once, after the spawn frame has settled.
            yield return null;
            yield return new WaitForEndOfFrame();
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera != null && camera.followingUnit == aircraft && camera.currentState == camera.cockpitState)
            {
                camera.SwitchState(camera.cockpitState);
                Plugin.Log.LogInfo("F-117 initial cockpit camera state refreshed after spawn.");
            }
            initialCameraRefresh = null;
        }

        private void Update()
        {
            if (!initialized || aircraft == null)
                return;

            if (!cockpitDisplayBound)
                TryBindCockpitDisplay();

            if (!chuteAvailable || body == null)
                return;

            if (inputs == null)
                inputs = aircraft.GetInputs();
            float speed = body.velocity.magnitude;
            bool gearLocked = aircraft.gearState == LandingGear.GearState.LockedExtended;
            bool mainGearOnGround = MainGearCarryWeight();
            bool noseOnGround = noseGear.WeightOnWheel(WheelLoadThreshold);
            bool allGearOnGround = mainGearOnGround && noseOnGround;
            bool requested = inputs != null && inputs.brake >= 0.65f;
            bool noGearWeight = !mainGearOnGround && !noseOnGround;
            if (noGearWeight && speed >= MinimumLandingTouchdownSpeed)
                airborneEvidenceTime += Time.deltaTime;
            else
                airborneEvidenceTime = 0f;
            if (aircraft.radarAlt > 10f ||
                aircraft.gearState == LandingGear.GearState.LockedRetracted ||
                airborneEvidenceTime >= AirborneEvidenceDuration)
            {
                wasAirborne = true;
                landingRollArmed = false;
            }

            // A landing roll begins on the main-gear contact edge after genuine
            // airborne evidence. Spawning, ordinary taxiing, braking while
            // turning, and momentary suspension movement cannot arm the chute.
            if (wasAirborne && !previousMainGearContact && mainGearOnGround &&
                gearLocked && speed >= MinimumLandingTouchdownSpeed)
            {
                wasAirborne = false;
                landingRollArmed = true;
                Plugin.Log.LogInfo("F-117 landing roll armed drag chute at " +
                    (speed * 3.6f).ToString("0") + " km/h; waiting for nose-wheel contact, alignment, and brake command.");
            }
            previousMainGearContact = mainGearOnGround;

            Vector3 groundVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
            Vector3 groundForward = Vector3.ProjectOnPlane(aircraft.transform.forward, Vector3.up);
            float alignmentAngle = groundVelocity.sqrMagnitude > 1f && groundForward.sqrMagnitude > 0.01f
                ? Vector3.Angle(groundForward, groundVelocity)
                : 180f;
            bool validLandingRoll = landingRollArmed && gearLocked && allGearOnGround &&
                aircraft.radarAlt <= 4f && Mathf.Abs(body.velocity.y) <= MaximumDeploymentSinkRate &&
                alignmentAngle <= MaximumRunwayAlignmentAngle;

            if (!deployed && !spent && requested && validLandingRoll &&
                speed <= MaximumChuteDeploymentSpeed && speed > ChuteJettisonSpeed)
            {
                deployed = true;
                landingRollArmed = false;
                Plugin.Log.LogInfo("F-117 drag chute deployed at " + (speed * 3.6f).ToString("0") +
                    " km/h with all gear loaded and runway alignment " + alignmentAngle.ToString("0.0") + " degrees.");
            }

            if (deployed && speed <= ChuteJettisonSpeed)
            {
                deployed = false;
                spent = true;
                Plugin.Log.LogInfo("F-117 drag chute jettisoned at " + (speed * 3.6f).ToString("0") + " km/h.");
            }
            else if (!deployed && landingRollArmed && speed <= ChuteJettisonSpeed)
            {
                // Once the aircraft has slowed to taxi speed, that landing's
                // deployment window is over. A new airborne/touchdown cycle is
                // required, so later taxi braking cannot release the chute.
                landingRollArmed = false;
                Plugin.Log.LogInfo("F-117 drag-chute landing window closed at taxi speed without deployment.");
            }

            float target = deployed ? 1f : 0f;
            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime * (deployed ? 2.5f : 4f));
            leftDoor.localRotation = Quaternion.Slerp(leftClosed, leftOpenTarget.localRotation, openAmount);
            rightDoor.localRotation = Quaternion.Slerp(rightClosed, rightOpenTarget.localRotation, openAmount);

            bool showChute = deployed && openAmount > 0.18f;
            if (chute.gameObject.activeSelf != showChute)
                chute.gameObject.SetActive(showChute);
            if (showChute)
            {
                float scale = Mathf.SmoothStep(0.08f, 1f, openAmount);
                chute.localScale = chuteFullScale * scale;
            }
        }

        private void TryBindCockpitDisplay()
        {
            if (TacScreenMaterial == null)
                return;
            TacScreen tacScreen = aircraft.GetComponentInChildren<TacScreen>(true);
            if (tacScreen == null)
                return;
            Material screenMaterial = TacScreenMaterial.GetValue(tacScreen) as Material;
            RenderTexture renderTexture = TacScreenRenderTexture != null
                ? TacScreenRenderTexture.GetValue(tacScreen) as RenderTexture
                : null;
            Camera screenCamera = TacScreenCamera != null
                ? TacScreenCamera.GetValue(tacScreen) as Camera
                : null;
            if (screenCamera != null)
            {
                screenCamera.enabled = true;
                if (renderTexture != null)
                    screenCamera.targetTexture = renderTexture;
            }
            if (screenMaterial == null)
                return;
            Texture feed = renderTexture != null
                ? (Texture)renderTexture
                : screenMaterial.mainTexture
                    ?? screenMaterial.GetTexture("_BaseMap")
                    ?? screenMaterial.GetTexture("_EmissionMap");
            if (feed != null)
            {
                screenMaterial.SetTexture("_BaseMap", feed);
                screenMaterial.SetTexture("_MainTex", feed);
                screenMaterial.SetTexture("_EmissionMap", feed);
                screenMaterial.EnableKeyword("_EMISSION");
                screenMaterial.SetColor("_EmissionColor", Color.white * 2f);
                screenMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            int bound = 0;
            foreach (Renderer renderer in EnumerateCockpitScreens())
            {
                renderer.enabled = true;
                renderer.sharedMaterial = screenMaterial;
                bound++;
            }
            Cockpit cockpit = aircraft.GetComponentInChildren<Cockpit>(true);
            if (cockpit != null && CockpitTacScreenRender != null)
            {
                Renderer assigned = CockpitTacScreenRender.GetValue(cockpit) as Renderer;
                if (assigned != null)
                {
                    assigned.enabled = true;
                    assigned.sharedMaterial = screenMaterial;
                }
            }
            if (bound == 0)
                return;
            cockpitDisplayBound = feed != null;
            if (cockpitDisplayBound)
                Plugin.Log.LogInfo("F-117 cockpit screens use stock TacScreen material '" +
                    screenMaterial.name + "' feed '" + feed.name + "' on " + bound + " displays.");
        }

        private IEnumerable<Renderer> EnumerateCockpitScreens()
        {
            foreach (Renderer renderer in aircraft.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                string name = renderer.gameObject.name ?? "";
                string mat = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
                if (name.IndexOf("Tacscreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("MFD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mat.IndexOf("MFD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    mat.IndexOf("Tacscreen", StringComparison.OrdinalIgnoreCase) >= 0)
                    yield return renderer;
            }
        }

        private void LateUpdate()
        {
            if (!initialized || aircraft == null)
                return;
            if (!stealthAvailable)
                return;

            // BayDoor updates in Update(). Applying the signature in LateUpdate observes the final
            // animated position for this frame and also wins over any other ordinary per-frame writes.
            UpdateStealthSignature();
        }

        private void UpdateStealthSignature()
        {
            float bayPenalty = 0f;
            foreach (BayDoor door in weaponBayDoors)
            {
                if (door == null)
                    continue;
                object value = BayOpenAmount.GetValue(door);
                if (value is float amount)
                    bayPenalty += FullyOpenBayRcsPerDoor * Mathf.Clamp01(amount);
            }

            float gearDeployment = aircraft.gearState == LandingGear.GearState.LockedRetracted ? 0f : 1f;
            if (GearFoldAmount != null && landingGears != null && landingGears.Length > 0)
            {
                float deploymentSum = 0f;
                int validGearCount = 0;
                foreach (LandingGear gear in landingGears)
                {
                    if (gear == null)
                        continue;
                    object value = GearFoldAmount.GetValue(gear);
                    if (value is float foldAmount)
                    {
                        deploymentSum += 1f - Mathf.Clamp01(foldAmount);
                        validGearCount++;
                    }
                }
                if (validGearCount > 0)
                    gearDeployment = deploymentSum / validGearCount;
            }
            float gearPenalty = FullyDeployedGearRcs * gearDeployment;
            // This is the complete signature, not an additive modifier. Internal stores stay shielded
            // regardless of which compatible stock mount is loaded.
            aircraft.RCS = CleanRcs + bayPenalty + gearPenalty;
        }

        private void FixedUpdate()
        {
            if (!initialized || !chuteAvailable || !deployed || aircraft == null || body == null || aircraft.remoteSim)
                return;

            Vector3 velocity = body.velocity;
            float speedSquared = velocity.sqrMagnitude;
            if (speedSquared < 1f)
                return;
            float density = Mathf.Max(0.2f, aircraft.GetAirDensity());
            float force = Mathf.Min(MaximumChuteForce, 0.5f * density * ChuteDragArea * speedSquared);
            body.AddForce(-velocity.normalized * force, ForceMode.Force);
        }

        private bool MainGearCarryWeight()
        {
            return mainGears != null && mainGears.Length == 2 &&
                mainGears.All(gear => gear != null && gear.WeightOnWheel(WheelLoadThreshold));
        }

        private static Transform FindDeep(Transform root, string objectName)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == objectName)
                    return item;
            return null;
        }
    }


    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.ModifyRCS))]
    internal static class InternalStoreRcsPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Hardpoint __instance)
        {
            // Suppress mount RCS only for this aircraft's bay-door-equipped internal stations.
            // External stations, other units, and unrelated Unit.ModifyRCS callers remain untouched.
            Aircraft aircraft = __instance != null && __instance.part != null
                ? __instance.part.parentUnit as Aircraft
                : null;
            bool internalF117Station = Plugin.IsF117(aircraft) &&
                ((__instance.bayDoors != null && __instance.bayDoors.Length > 0) ||
                 Plugin.IsFixedJammerHardpoint(__instance));
            return !internalF117Station;
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.ModifyDrag))]
    internal static class FixedJammerDragPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Hardpoint __instance)
        {
            // JammingPod1 itself remains untouched. Only its external-pod mounting
            // penalty is suppressed because this installation is wholly internal.
            return !Plugin.IsFixedJammerHardpoint(__instance);
        }
    }

    [HarmonyPatch(typeof(Hardpoint), nameof(Hardpoint.ModifyMass))]
    internal static class FixedJammerMassPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Hardpoint __instance)
        {
            // The internal jammer is already included in the aircraft's authored
            // empty mass; do not add the stock external pod structure a second time.
            return !Plugin.IsFixedJammerHardpoint(__instance);
        }
    }

}
