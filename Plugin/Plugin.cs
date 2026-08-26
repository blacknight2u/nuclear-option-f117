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
        public const string PluginVersion = "0.4.84";
        internal const string AircraftKey = "blacknight2u_F117A_Nighthawk";
        internal const string FixedJammerHardpointName = "JammingPod1";
        internal const string LightweightAgmMountKey = "AGM1_quad_internal";
        internal const string ParadeFlagLiveryName = "F117A_ParadeFlag_Livery";
        internal const string ParadeFlagSilverBlueLiveryName =
            "F117A_ParadeFlag_SilverBlue_Livery";
        internal const string ParadeFlagCoolTitaniumLiveryName =
            "F117A_ParadeFlag_CoolTitanium_Livery";
        internal const string ParadeFlagSmokedChromeLiveryName =
            "F117A_ParadeFlag_SmokedChrome_Livery";
        internal const string ParadeFlagWarmTitaniumLiveryName =
            "F117A_ParadeFlag_WarmTitanium_Livery";
        internal const string ParadeFlagOverlayPrefix = "F117_ParadeFlagOverlay_";
        internal const string MirrorFinishAssetPath =
            "assets/f117/textures/f117_mirror_ms.png";
        internal const string MatteFinishAssetPrefix =
            "assets/f117/textures/f117_ext_";
        internal const string MatteAlbedoAssetPrefix =
            "assets/f117/textures/f117_ext_";
        internal const string MatteNormalAssetPrefix =
            "assets/f117/textures/f117_ext_";
        internal const string MatteOcclusionAssetPrefix =
            "assets/f117/textures/f117_ext_";
        internal const string DamageAlbedoAssetPrefix =
            "assets/f117/generated/materials/f117_f117_external_";

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
        private static readonly FieldInfo LiveryAircraft =
            AccessTools.Field(typeof(LiveryBehaviour), "aircraft");
        private static Texture2D mirrorMetallicTexture;
        private static readonly Dictionary<int, Texture2D> MatteMetallicTextures =
            new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> MatteAlbedoTextures =
            new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> MatteNormalTextures =
            new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> MatteOcclusionTextures =
            new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Texture2D> DamageAlbedoTextures =
            new Dictionary<int, Texture2D>();
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
            mirrorMetallicTexture = null;
            MatteMetallicTextures.Clear();
            MatteAlbedoTextures.Clear();
            MatteNormalTextures.Clear();
            MatteOcclusionTextures.Clear();
            DamageAlbedoTextures.Clear();
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

        internal static void ApplyParadeFlagLivery(LiveryBehaviour behaviour, LiveryData livery)
        {
            Aircraft aircraft = behaviour == null || LiveryAircraft == null
                ? null
                : LiveryAircraft.GetValue(behaviour) as Aircraft;
            if (!IsF117(aircraft))
                return;

            Color tint;
            string finishName;
            bool enabled = TryGetParadeFlagFinish(livery, out tint, out finishName);
            Renderer[] overlays = GetAircraftRenderers(aircraft)
                .Where(renderer => renderer.name.StartsWith(ParadeFlagOverlayPrefix,
                    StringComparison.Ordinal))
                .ToArray();
            if (enabled && overlays.Length == 0)
            {
                Log.LogError("F-117 farewell-flag livery selected, but its underside overlays are missing.");
            }

            int changed = 0;
            foreach (Renderer overlay in overlays)
            {
                if (overlay.enabled == enabled)
                    continue;
                overlay.enabled = enabled;
                changed++;
            }
            if (changed > 0)
            {
                Log.LogDebug("F-117 " + (enabled ? "enabled" : "disabled") +
                    " the photograph-matched farewell-flag underside on " + changed + " renderers.");
            }
            F117LiveryMaterialProfiles profiles =
                aircraft.GetComponent<F117LiveryMaterialProfiles>() ??
                aircraft.gameObject.AddComponent<F117LiveryMaterialProfiles>();
            profiles.Initialize(aircraft, behaviour);
            profiles.Apply(enabled, tint, finishName, behaviour);
        }

        private static bool TryGetParadeFlagFinish(LiveryData livery, out Color tint,
            out string finishName)
        {
            tint = Color.white;
            finishName = "Nighthawk Black";
            if (livery == null)
                return false;

            switch (livery.name)
            {
                case ParadeFlagLiveryName:
                    finishName = "Pure Chrome";
                    return true;
                case ParadeFlagSilverBlueLiveryName:
                    tint = new Color(0.72f, 0.82f, 0.95f, 1f);
                    finishName = "Silver Blue";
                    return true;
                case ParadeFlagCoolTitaniumLiveryName:
                    tint = new Color(0.57f, 0.61f, 0.67f, 1f);
                    finishName = "Cool Titanium";
                    return true;
                case ParadeFlagSmokedChromeLiveryName:
                    tint = new Color(0.35f, 0.39f, 0.45f, 1f);
                    finishName = "Smoked Chrome";
                    return true;
                case ParadeFlagWarmTitaniumLiveryName:
                    tint = new Color(0.65f, 0.59f, 0.51f, 1f);
                    finishName = "Warm Titanium";
                    return true;
                default:
                    return false;
            }
        }

        internal static Renderer[] GetAircraftRenderers(Aircraft aircraft)
        {
            var renderers = new HashSet<Renderer>();
            if (aircraft == null)
                return Array.Empty<Renderer>();
            foreach (Renderer renderer in aircraft.GetComponentsInChildren<Renderer>(true))
                if (renderer != null)
                    renderers.Add(renderer);
            if (aircraft.partLookup != null)
            {
                foreach (UnitPart part in aircraft.partLookup)
                {
                    if (part == null)
                        continue;
                    foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                        if (renderer != null)
                            renderers.Add(renderer);
                }
            }
            return renderers.ToArray();
        }

        internal static Texture MirrorMetallicTexture
        {
            get
            {
                if (mirrorMetallicTexture != null)
                    return mirrorMetallicTexture;
                mirrorMetallicTexture = LoadBundledTexture(MirrorFinishAssetPath);
                if (mirrorMetallicTexture == null)
                    Log.LogError("F-117 bundled mirror finish is unavailable at " +
                        MirrorFinishAssetPath + ".");
                return mirrorMetallicTexture;
            }
        }

        internal static Texture MatteMetallicTexture(string canonicalMaterialName)
        {
            return PanelTexture(canonicalMaterialName, MatteFinishAssetPrefix, "_ms.png",
                MatteMetallicTextures, "matte finish");
        }

        internal static Texture MatteAlbedoTexture(string canonicalMaterialName)
        {
            return PanelTexture(canonicalMaterialName, MatteAlbedoAssetPrefix, "_albedo.png",
                MatteAlbedoTextures, "authored albedo");
        }

        internal static Texture MatteNormalTexture(string canonicalMaterialName)
        {
            return PanelTexture(canonicalMaterialName, MatteNormalAssetPrefix, "_normal.png",
                MatteNormalTextures, "authored normal");
        }

        internal static Texture MatteOcclusionTexture(string canonicalMaterialName)
        {
            return PanelTexture(canonicalMaterialName, MatteOcclusionAssetPrefix, "_occlusion.png",
                MatteOcclusionTextures, "authored occlusion");
        }

        internal static Texture DamageAlbedoTexture(string canonicalMaterialName)
        {
            return PanelTexture(canonicalMaterialName, DamageAlbedoAssetPrefix, "_damage.asset",
                DamageAlbedoTextures, "damage albedo");
        }

        private static Texture2D PanelTexture(string canonicalMaterialName, string assetPrefix,
            string assetSuffix, Dictionary<int, Texture2D> cache, string description)
        {
            const string materialPrefix = "F117_EXTERNAL_";
            if (string.IsNullOrEmpty(canonicalMaterialName) ||
                !canonicalMaterialName.StartsWith(materialPrefix, StringComparison.Ordinal) ||
                canonicalMaterialName.Length != materialPrefix.Length + 1)
                return null;
            int panel = canonicalMaterialName[materialPrefix.Length] - '0';
            if (panel < 1 || panel > 7)
                return null;
            if (cache.TryGetValue(panel, out Texture2D cached) && cached != null)
                return cached;
            string path = assetPrefix + panel + assetSuffix;
            Texture2D texture = LoadBundledTexture(path);
            if (texture == null)
            {
                Log.LogError("F-117 bundled " + description + " is unavailable at " + path + ".");
                return null;
            }
            cache[panel] = texture;
            return texture;
        }

        private static Texture2D LoadBundledTexture(string assetPath)
        {
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null)
                    continue;
                Texture2D texture = bundle.LoadAsset<Texture2D>(assetPath);
                if (texture != null)
                    return texture;
            }
            return null;
        }

        internal static bool IsTextureProperty(Material material, string property)
        {
            if (material == null || material.shader == null || !material.HasProperty(property))
                return false;
            int index = material.shader.FindPropertyIndex(property);
            return index >= 0 && material.shader.GetPropertyType(index) == ShaderPropertyType.Texture;
        }

        internal static void SetFloatProperty(Material material, string property, float value)
        {
            if (material == null || material.shader == null || !material.HasProperty(property))
                return;
            int index = material.shader.FindPropertyIndex(property);
            if (index < 0 || material.shader.GetPropertyType(index) == ShaderPropertyType.Texture)
                return;
            material.SetFloat(property, value);
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
            if (jammers.Length > 0)
            {
                F117JammerPowerHudRegistration registration =
                    aircraft.GetComponent<F117JammerPowerHudRegistration>() ??
                    aircraft.gameObject.AddComponent<F117JammerPowerHudRegistration>();
                registration.Register(aircraft.GetPowerSupply());
            }
            if (jammers.Length > 0 && !loggedFixedJammer)
            {
                loggedFixedJammer = true;
                Log.LogDebug("F-117 installed the unchanged native JammingPod1 weapon on its concealed fixed station.");
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
                Log.LogDebug("F-117 adapted AGM1_quad_internal to the native 2 m downward bay-release path.");
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
                Log.LogDebug("F-117 cockpit is using native tactical screen '" + nativeTacScreenPrefab.name + "'.");
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
                    Log.LogDebug("F-117 HUD is using deterministic native extras '" + nativeHudExtrasPrefab.name + "'.");
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
                Log.LogDebug("F-117 removed " + telemetry.Length +
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

    internal sealed class F117LiveryMaterialProfiles : MonoBehaviour
    {
        private enum ProfileKind
        {
            AircraftSkin,
            StaticAccessory,
            TireRubber,
            CockpitFrame
        }

        private sealed class Entry
        {
            internal Renderer Renderer;
            internal int Slot;
            internal Material Material;
            internal ProfileKind Kind;
            internal Texture BaseColor;
            internal Texture DamageColor;
            internal Texture Normal;
            internal Texture Occlusion;
            internal Texture MatteFinish;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<Material> ownedMaterials = new HashSet<Material>();
        private bool initialized;

        internal void Initialize(Aircraft aircraft, LiveryBehaviour liveryBehaviour)
        {
            if (initialized || !Plugin.IsF117(aircraft))
                return;
            initialized = true;
            foreach (Renderer renderer in Plugin.GetAircraftRenderers(aircraft))
                CloneTargetSlots(renderer, liveryBehaviour, true);
            Plugin.Log.LogDebug("F-117 created " + entries.Count +
                " renderer-slot livery profiles; shared bundle materials and exterior glass are untouched.");
        }

        private void CloneTargetSlots(Renderer renderer, LiveryBehaviour liveryBehaviour, bool discover)
        {
            if (renderer == null || renderer.name.StartsWith(Plugin.ParadeFlagOverlayPrefix,
                    StringComparison.Ordinal))
                return;

            // sharedMaterials is inspected before any material/materials getter so Unity
            // cannot silently instantiate unrelated slots such as tires or landing gear.
            Material[] slots = renderer.sharedMaterials;
            bool changed = false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Material source = slots[slot];
                Entry entry = entries.FirstOrDefault(item => item.Renderer == renderer && item.Slot == slot);
                if (entry != null && source == entry.Material)
                    continue;

                // SetLivery may replace a renderer's material instance after the
                // profile was discovered. Renderer/slot identity remains authoritative:
                // rebind that known slot even when the replacement already uses URP/Lit.
                // Reclassifying it would reject the live replacement and leave Apply
                // updating a detached, invisible material clone.
                if (entry == null && !discover)
                    continue;
                ProfileKind? kind = entry == null ? Classify(renderer, source) : entry.Kind;
                if (!kind.HasValue)
                    continue;

                string canonical = CanonicalImportedName(source.name);
                bool exterior = kind.Value == ProfileKind.AircraftSkin ||
                    kind.Value == ProfileKind.StaticAccessory;
                bool tire = kind.Value == ProfileKind.TireRubber;
                Texture baseColor = entry == null
                    ? exterior ? Plugin.MatteAlbedoTexture(canonical)
                        : tire ? TextureProperty(source, "_BaseMap") : null
                    : entry.BaseColor;
                Texture damageColor = entry == null && exterior
                    ? Plugin.DamageAlbedoTexture(canonical)
                    : entry?.DamageColor;
                Texture normal = entry == null
                    ? exterior ? Plugin.MatteNormalTexture(canonical)
                        : tire ? TextureProperty(source, "_BumpMap") : null
                    : entry.Normal;
                Texture occlusion = entry == null
                    ? exterior ? Plugin.MatteOcclusionTexture(canonical)
                        : tire ? TextureProperty(source, "_OcclusionMap") : null
                    : entry.Occlusion;
                Texture matteFinish = entry == null
                    ? exterior ? Plugin.MatteMetallicTexture(canonical)
                        : tire ? null : FinishTexture(source, kind.Value)
                    : entry.MatteFinish;
                if ((!tire && matteFinish == null) || (exterior &&
                    (baseColor == null || damageColor == null || normal == null || occlusion == null)))
                {
                    Plugin.Log.LogError("F-117 refused to profile " + renderer.name + " slot " + slot +
                        " because its complete authored material texture set is missing.");
                    continue;
                }

                Material clone = new Material(source)
                {
                    name = MaterialIdentity(source.name) + " [F117 Profile]"
                };
                liveryBehaviour?.RemoveFromMaterialCleanup(clone);
                ownedMaterials.Add(clone);
                slots[slot] = clone;
                changed = true;

                if (entry == null)
                {
                    entries.Add(new Entry
                    {
                        Renderer = renderer,
                        Slot = slot,
                        Material = clone,
                        Kind = kind.Value,
                        BaseColor = baseColor,
                        DamageColor = damageColor,
                        Normal = normal,
                        Occlusion = occlusion,
                        MatteFinish = matteFinish
                    });
                }
                else
                {
                    Material displaced = entry.Material;
                    entry.Material = clone;
                    if (displaced != null && ownedMaterials.Remove(displaced))
                        Destroy(displaced);
                }
            }
            if (changed)
                renderer.sharedMaterials = slots;
        }

        internal void Apply(bool paradeFlag, Color tint, string finishName,
            LiveryBehaviour liveryBehaviour)
        {
            foreach (Renderer renderer in entries.Select(entry => entry.Renderer).Distinct().ToArray())
                CloneTargetSlots(renderer, liveryBehaviour, false);

            Texture mirrorFinish = paradeFlag ? Plugin.MirrorMetallicTexture : null;
            if (paradeFlag && mirrorFinish == null)
                return;
            int skinCount = 0;
            int accessoryCount = 0;
            int tireCount = 0;
            int frameCount = 0;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Plugin.Log.LogError("F-117 cannot restore its authored materials because URP/Lit is unavailable.");
                return;
            }
            foreach (Entry entry in entries)
            {
                Material material = entry.Material;
                if (material == null)
                    continue;
                if (entry.Kind == ProfileKind.AircraftSkin)
                {
                    // The pre-0.4.76 aircraft rendered correctly through URP/Lit. The
                    // AircraftSkin shader migration produced a white/chrome fallback at
                    // runtime even when its serialized custom properties were populated.
                    // Restore the known-good renderer contract while retaining the
                    // current physical damage graph and livery overlays.
                    Texture profileAlbedo = paradeFlag ? Texture2D.whiteTexture : entry.BaseColor;
                    Color profileTint = paradeFlag ? tint : Color.white;
                    ApplyLitExterior(material, lit, profileAlbedo, entry.Normal, entry.Occlusion,
                        paradeFlag ? mirrorFinish : entry.MatteFinish, profileTint);
                    skinCount++;
                }
                else if (entry.Kind == ProfileKind.StaticAccessory)
                {
                    // Gear, gear doors, bay linkages, and the drag chute reuse exterior
                    // source materials but are not livery-painted airframe skin. Keep
                    // their authored matte textures regardless of the selected finish.
                    ApplyLitExterior(material, lit, entry.BaseColor, entry.Normal, entry.Occlusion,
                        entry.MatteFinish, Color.white);
                    accessoryCount++;
                }
                else if (entry.Kind == ProfileKind.TireRubber)
                {
                    material.shader = lit;
                    SetTexture(material, "_BaseMap", entry.BaseColor);
                    SetTexture(material, "_MainTex", entry.BaseColor);
                    SetTexture(material, "_BumpMap", entry.Normal);
                    SetTexture(material, "_OcclusionMap", entry.Occlusion);
                    if (Plugin.IsTextureProperty(material, "_MetallicGlossMap"))
                        material.SetTexture("_MetallicGlossMap", null);
                    material.SetColor("_BaseColor", Color.white);
                    material.SetColor("_Color", Color.white);
                    material.SetFloat("_Metallic", 0f);
                    material.SetFloat("_Smoothness", 0.12f);
                    if (material.HasProperty("_EnvironmentReflections"))
                        material.SetFloat("_EnvironmentReflections", 0f);
                    material.DisableKeyword("_METALLICSPECGLOSSMAP");
                    if (entry.Normal != null)
                        material.EnableKeyword("_NORMALMAP");
                    if (entry.Occlusion != null)
                        material.EnableKeyword("_OCCLUSIONMAP");
                    tireCount++;
                }
                else if (entry.Kind == ProfileKind.CockpitFrame)
                {
                    // The frame keeps its black albedo, UV layout, multipliers, and
                    // keyword state. Only its URP packed finish texture changes.
                    material.SetTexture("_MetallicGlossMap",
                        paradeFlag ? mirrorFinish : entry.MatteFinish);
                    frameCount++;
                }
            }
            Plugin.Log.LogInfo("F-117 applied independent " +
                (paradeFlag ? "Farewell Flag / " + finishName : "Nighthawk Black matte") +
                " profile to " + skinCount + " skin material(s); preserved " +
                accessoryCount + " static accessory, " + tireCount + " tire, and " +
                frameCount + " frame material(s).");
            LogMaterialState(paradeFlag, finishName);
        }

        private static void ApplyLitExterior(Material material, Shader lit, Texture albedo,
            Texture normal, Texture occlusion, Texture finish, Color tint)
        {
            material.shader = lit;
            SetTexture(material, "_BaseMap", albedo);
            SetTexture(material, "_MainTex", albedo);
            SetTexture(material, "_BumpMap", normal);
            SetTexture(material, "_OcclusionMap", occlusion);
            SetTexture(material, "_MetallicGlossMap", finish);
            material.SetColor("_BaseColor", tint);
            material.SetColor("_Color", tint);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            if (material.HasProperty("_EnvironmentReflections"))
                material.SetFloat("_EnvironmentReflections", 1f);
            if (material.HasProperty("_SpecularHighlights"))
                material.SetFloat("_SpecularHighlights", 1f);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            if (occlusion != null)
                material.EnableKeyword("_OCCLUSIONMAP");
            if (finish != null)
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (texture != null && Plugin.IsTextureProperty(material, property))
                material.SetTexture(property, texture);
        }

        private void LogMaterialState(bool paradeFlag, string finishName)
        {
            int boundCount = 0;
            int skinCount = 0;
            int accessoryCount = 0;
            int tireCount = 0;
            var failures = new List<string>();
            foreach (Entry entry in entries)
            {
                Material material = entry.Material;
                if (material == null || entry.Renderer == null)
                {
                    failures.Add("missing material/renderer");
                    continue;
                }
                Material[] liveSlots = entry.Renderer.sharedMaterials;
                if (entry.Slot < 0 || entry.Slot >= liveSlots.Length ||
                    liveSlots[entry.Slot] != material)
                {
                    failures.Add(entry.Renderer.name + "[" + entry.Slot + "] detached");
                    continue;
                }
                boundCount++;

                Texture expectedFinish;
                if (entry.Kind == ProfileKind.AircraftSkin)
                {
                    expectedFinish = paradeFlag ? Plugin.MirrorMetallicTexture : entry.MatteFinish;
                    skinCount++;
                }
                else if (entry.Kind == ProfileKind.StaticAccessory)
                {
                    expectedFinish = entry.MatteFinish;
                    accessoryCount++;
                }
                else if (entry.Kind == ProfileKind.CockpitFrame)
                {
                    expectedFinish = paradeFlag ? Plugin.MirrorMetallicTexture : entry.MatteFinish;
                }
                else
                {
                    expectedFinish = null;
                    tireCount++;
                }

                Texture actualFinish = material.HasProperty("_MetallicGlossMap")
                    ? material.GetTexture("_MetallicGlossMap")
                    : null;
                if (actualFinish != expectedFinish)
                    failures.Add(entry.Renderer.name + "[" + entry.Slot + "] wrong finish");
                if (entry.Kind == ProfileKind.TireRubber &&
                    (material.GetFloat("_Metallic") > 0.001f ||
                     material.GetFloat("_Smoothness") > 0.121f))
                    failures.Add(entry.Renderer.name + "[" + entry.Slot + "] reflective tire");
                if (entry.Kind == ProfileKind.AircraftSkin && paradeFlag &&
                    (!material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ||
                     material.GetFloat("_Metallic") < 0.999f ||
                     material.GetFloat("_Smoothness") < 0.999f))
                    failures.Add(entry.Renderer.name + "[" + entry.Slot + "] chrome disabled");
            }

            if (failures.Count > 0)
            {
                Plugin.Log.LogError("F-117 livery material verification failed for " +
                    failures.Count + " slot(s): " + string.Join("; ", failures.Take(12)) +
                    (failures.Count > 12 ? "; ..." : "."));
                return;
            }
            Plugin.Log.LogDebug("F-117 verified " + boundCount + " live " +
                (paradeFlag ? finishName + " livery" : "Nighthawk Black") +
                " material slots: " + skinCount + " skin, " + accessoryCount +
                " matte accessory, and " + tireCount + " non-metallic tire.");
        }

        private static Texture FinishTexture(Material material, ProfileKind kind)
        {
            string property = kind == ProfileKind.AircraftSkin
                ? "_Metallic"
                : "_MetallicGlossMap";
            return material != null && material.HasProperty(property)
                ? material.GetTexture(property)
                : null;
        }

        private static Texture TextureProperty(Material material, string property)
        {
            return Plugin.IsTextureProperty(material, property)
                ? material.GetTexture(property)
                : null;
        }

        private static ProfileKind? Classify(Renderer renderer, Material material)
        {
            if (renderer == null || material == null || material.shader == null)
                return null;
            string canonical = CanonicalImportedName(material.name);
            if (canonical == "F117_Tires" &&
                material.shader.name == "Universal Render Pipeline/Lit")
                return ProfileKind.TireRubber;
            if (renderer.name == "F117_Canopy_Mesh" && canonical == "INT_CockpitFrame" &&
                material.shader.name == "Universal Render Pipeline/Lit")
                return ProfileKind.CockpitFrame;
            if (IsStaticAccessoryHierarchy(renderer.transform) &&
                material.shader.name == "Shader Graphs/AircraftSkin" &&
                IsExteriorFamily(canonical))
                return ProfileKind.StaticAccessory;
            if (IsExcludedHierarchy(renderer.transform) ||
                material.shader.name != "Shader Graphs/AircraftSkin" ||
                !IsExteriorFamily(canonical))
                return null;
            return ProfileKind.AircraftSkin;
        }

        private static bool IsStaticAccessoryHierarchy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.name ?? string.Empty;
                if (name.StartsWith("F117_Gear", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("_BayLink_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("_BayPart_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.StartsWith("F117_DragChute", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsExcludedHierarchy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.name ?? string.Empty;
                if (name.StartsWith("F117_Gear", StringComparison.OrdinalIgnoreCase) ||
                    name.IndexOf("Cockpit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Canopy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("pilot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Hardpoint", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsExteriorFamily(string canonical)
        {
            if (canonical == null || !canonical.StartsWith("F117_EXTERNAL_", StringComparison.Ordinal))
                return false;
            string suffix = canonical.Substring("F117_EXTERNAL_".Length);
            return suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '7';
        }

        private static string CanonicalImportedName(string name)
        {
            string result = MaterialIdentity(name);
            const string profile = " [F117 Profile]";
            if (result.EndsWith(profile, StringComparison.Ordinal))
                result = result.Substring(0, result.Length - profile.Length);
            // Generated materials are serialized as NN_<imported name>. Strip only
            // that exact two-digit asset ordinal before matching canonical families.
            if (result.Length > 3 && char.IsDigit(result[0]) && char.IsDigit(result[1]) &&
                result[2] == '_')
                result = result.Substring(3);
            if (result.StartsWith("F117_", StringComparison.Ordinal) &&
                (result.Substring(5).StartsWith("F117_EXTERNAL_", StringComparison.Ordinal) ||
                 result.Substring(5) == "INT_CockpitFrame" ||
                 result.Substring(5) == "F117_Tires"))
                result = result.Substring(5);
            if (result.Length > 4)
            {
                int suffix = result.Length - 4;
                if ((result[suffix] == '.' || result[suffix] == '_') &&
                    char.IsDigit(result[suffix + 1]) && char.IsDigit(result[suffix + 2]) &&
                    char.IsDigit(result[suffix + 3]))
                    result = result.Substring(0, suffix);
            }
            return result;
        }

        private static string MaterialIdentity(string name)
        {
            string result = name ?? string.Empty;
            const string instance = " (Instance)";
            while (result.EndsWith(instance, StringComparison.Ordinal))
                result = result.Substring(0, result.Length - instance.Length);
            return result;
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
                if (material != null)
                    Destroy(material);
            entries.Clear();
            ownedMaterials.Clear();
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

    [HarmonyPatch]
    internal static class F117LiveryPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(LiveryBehaviour), "SetLivery");
        }

        [HarmonyPostfix]
        private static void Postfix(LiveryBehaviour __instance, LiveryData livery)
        {
            Plugin.ApplyParadeFlagLivery(__instance, livery);
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
                Plugin.Log.LogDebug("F-117 throttle gauge is configured for its actual dry-thrust controls " +
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

            // CameraCockpitState is reused for replacement aircraft, so clear
            // crash/inertia offsets that EnterState does not reset.
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

            // Inner main doors follow strut travel; outer panels use the
            // separate native door-closing stage.
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

            Plugin.Log.LogDebug(name + " initialized " + links.Length +
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
            Plugin.Log.LogDebug(name + " initialized " + links.Length +
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
                Plugin.Log.LogDebug("F-117 cockpit eye point initialized at local " + eye.ToString("F3") + ".");
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
                Plugin.Log.LogDebug("F-117 low-observable controller active: clean RCS " + CleanRcs.ToString("0.0000") +
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

                    // Blueprinter replaces the editor placeholder shader at load time,
                    // so assert the HUD's transparent state on the runtime material.
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
                Plugin.Log.LogDebug("F-117 restored runtime transparency on " + restoredCount +
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
            // Initial player setup enters orbit and cockpit in one frame. Re-enter
            // the cockpit state after spawn so it consumes the settled eye transform.
            yield return null;
            yield return new WaitForEndOfFrame();
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera != null && camera.followingUnit == aircraft && camera.currentState == camera.cockpitState)
            {
                camera.SwitchState(camera.cockpitState);
                Plugin.Log.LogDebug("F-117 initial cockpit camera state refreshed after spawn.");
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
                Plugin.Log.LogDebug("F-117 landing roll armed drag chute at " +
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
                Plugin.Log.LogDebug("F-117 drag chute deployed at " + (speed * 3.6f).ToString("0") +
                    " km/h with all gear loaded and runway alignment " + alignmentAngle.ToString("0.0") + " degrees.");
            }

            if (deployed && speed <= ChuteJettisonSpeed)
            {
                deployed = false;
                spent = true;
                Plugin.Log.LogDebug("F-117 drag chute jettisoned at " + (speed * 3.6f).ToString("0") + " km/h.");
            }
            else if (!deployed && landingRollArmed && speed <= ChuteJettisonSpeed)
            {
                // Once the aircraft has slowed to taxi speed, that landing's
                // deployment window is over. A new airborne/touchdown cycle is
                // required, so later taxi braking cannot release the chute.
                landingRollArmed = false;
                Plugin.Log.LogDebug("F-117 drag-chute landing window closed at taxi speed without deployment.");
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
                Plugin.Log.LogDebug("F-117 cockpit screens use stock TacScreen material '" +
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

    [DisallowMultipleComponent]
    internal sealed class F117JammerPowerHudRegistration : MonoBehaviour
    {
        private PowerSupply powerSupply;

        internal void Register(PowerSupply supply)
        {
            if (powerSupply == supply)
                return;
            if (powerSupply != null)
                powerSupply.RemoveUser();
            powerSupply = supply;
            if (powerSupply != null)
            {
                // JammingPod1 draws from PowerSupply but, unlike the other stock
                // powered systems, does not call AddUser. Users only controls the
                // stock ChargeIndicator's visibility; it does not alter capacity,
                // generation, power draw, or jamming strength.
                powerSupply.AddUser();
                Plugin.Log.LogDebug("F-117 registered its native JammingPod1 with the stock capacitor HUD.");
            }
        }

        private void OnDestroy()
        {
            if (powerSupply != null)
                powerSupply.RemoveUser();
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
