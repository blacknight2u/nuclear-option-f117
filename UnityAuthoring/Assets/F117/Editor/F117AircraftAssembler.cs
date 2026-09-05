using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static F117AuthoringUtil;

internal static class F117AircraftAssembler
{
    private const string TexturesRoot = "Assets/F117/Textures/";
    private const string ParadeFlagTexturePath = TexturesRoot + "F117_ParadeFlag_Wrap.png";
    internal const string MirrorFinishTexturePath = TexturesRoot + "F117_Mirror_MS.png";
    internal const string ParadeFlagOverlayPrefix = "F117_ParadeFlagOverlay_";
    internal const string ParadeFlagMaterialName = "F117_ParadeFlag_Underside";
    internal static readonly string[] ParadeFlagFinishKeys =
    {
        // PureChrome is retained only as the untinted flag-albedo source used by
        // Matte Black. It is not exposed as a selectable livery.
        "PureChrome", "SmokedChrome"
    };
    private static readonly Color[] ParadeFlagFinishTints =
    {
        Color.white,
        new Color(0.35f, 0.39f, 0.45f, 1f)
    };
    // The nearest measured underside emblem is 0.232 mm above its airframe skin.
    // Keep the wrap below that authored decal layer while still separating it from
    // the base surface enough to avoid coincident depth values.
    internal const float ParadeFlagSurfaceOffset = 0.0001f;
    internal const float ParadeFlagWeldTolerance = 0.00001f;
    internal const float ParadeFlagVisibilityClearance = 0.00002f;
    // The flag owns the actual underside, not steep tail, side, or edge facets.
    // A 0.75 minimum downward dot excludes every measured high-tail facet near
    // the rudders while retaining the faceted lower wing, bay-door, and elevon skins.
    internal const double ParadeFlagMinimumDownwardDot = 0.75d;
    // Keep length and projected-area tolerances dimensionally separate. Comparing
    // a squared 3D cross magnitude with a linear tolerance discarded more than
    // half of the measured source triangles and produced zero-normal occluders.
    private const float ParadeFlagCoordinateEpsilon = 0.0000001f;
    // This projected-area epsilon is used only to clean already clipped polygons;
    // source projection eligibility is separately derived from the 10 um weld.
    private const float ParadeFlagAreaEpsilon = 0.00000001f;
    private const float ParadeFlagSpatialCell = 0.5f;
    internal static readonly string[] ParadeFlagSurfaceNames =
    {
        "F117_Exterior_Mesh",
        "F117_Exterior_LeftWing_Mesh", "F117_Exterior_RightWing_Mesh",
        "F117_BayDoor_Left_Mesh", "F117_BayDoor_Right_Mesh",
        "F117_Elevon_L_Inner_Mesh", "F117_Elevon_L_Outer_Mesh",
        "F117_Elevon_R_Inner_Mesh", "F117_Elevon_R_Outer_Mesh",
        "F117_GearDoor_Nose_Mesh",
        "F117_GearDoor_Left_Outer_Mesh", "F117_GearDoor_Left_Inner_Mesh",
        "F117_GearDoor_Right_Outer_Mesh", "F117_GearDoor_Right_Inner_Mesh"
    };
    // Measured from Unity's configured import of the pinned canonical FBX. Unity
    // welds coincident vertices and removes collapsed source faces before the game
    // sees the mesh; the validator separately hash-locks the raw FBX identity.
    // These counts are never copied from generated overlay meshes.
    internal static readonly int[] ParadeFlagEligibleSourceTriangleCounts =
    {
        79288, 619, 609, 516, 516, 40, 36, 42, 36,
        489, 964, 260, 956, 260
    };
    internal static readonly int[] ParadeFlagDownwardSourceTriangleCounts =
    {
        11873, 136, 131, 81, 81, 5, 6, 7, 6,
        10, 8, 5, 8, 7
    };
    internal const int ParadeFlagUniqueOccluderFaceCount = 84625;
    internal const int ParadeFlagProjectedOccluderFaceCount = 77732;
    private static readonly string[] ParadeFlagOccluderNames =
    {
        "F117_Exterior_Mesh",
        "F117_Exterior_LeftWing_Mesh", "F117_Exterior_RightWing_Mesh",
        "F117_BayDoor_Left_Mesh", "F117_BayDoor_Right_Mesh",
        "F117_Elevon_L_Inner_Mesh", "F117_Elevon_L_Outer_Mesh",
        "F117_Elevon_R_Inner_Mesh", "F117_Elevon_R_Outer_Mesh",
        "F117_GearDoor_Nose_Mesh",
        "F117_GearDoor_Left_Outer_Mesh", "F117_GearDoor_Left_Inner_Mesh",
        "F117_GearDoor_Right_Outer_Mesh", "F117_GearDoor_Right_Inner_Mesh"
    };
    private const string AircraftSkinTemplatePath =
        "Assets/blueprinter/aryx/aryx_f16m/Aryx_F16M_KingViper_Skin.mat";
    internal const float InternalStoreMountHeight = 0.45f;
    internal const float FlightControlGLimit = 6f;
    internal const float FlightControlCornerSpeed = 150f;
    internal const float FlightControlTakeoffSpeed = 72f;
    internal const float FlightControlMaxPitchRate = 0.45f;
    internal const float FlightControlMaxRollRate = 1.75f;
    internal const float FlightControlAlphaLimit = 18f;
    internal const float FlightControlPitchDamping = 2.8f;
    internal const float FlightControlYawTightness = 1.0f;
    // Keep the fly-by-wire controller active continuously, including ground roll.
    internal const float FlightControlMinimumSpeed = 0f;
    internal const float FlightControlMinimumAltitude = 0f;
    // Negative servo travel produces the opposing tail moment expected by ControlsFilter.
    internal const float RudderYawTravel = -18f;
    // The imported control animation has 22.53 degrees from neutral to its usable stop.
    internal const float ElevonPitchTravel = 15f;
    internal const float ElevonRollTravel = 7.5f;
    // Projected from the canonical mesh and averaged left/right for symmetric physics.
    internal const float InnerElevonArea = 2.991705f;
    internal const float OuterElevonArea = 2.418405f;
    internal const float CentralBodyLiftArea = 5.6667f;
    internal const float NoseLiftArea = 2f;
    internal const float RearBodyLiftArea = 6f;
    // Keep total horizontal planform at the established 73.0 m2. Increasing the
    // moving share to its measured geometry subtracts the same area from the fixed
    // wing share, so this restores control authority without inventing lift area.
    internal const float MainWingLiftArea = 24.25654f;
    // Keep the neutral horizontal lift centre a small, explicit distance behind
    // the dry centre of mass. The 0.28 m target is five percent of the aircraft's
    // approximately 5.6 m reference mean chord: stable enough for the native FBW,
    // without making it spend most of the elevon travel holding pitch trim.
    internal const float TargetPitchStaticMargin = 0.28f;
    // LandingGear uses suspensionTravel as both suspension stroke and the length
    // of its single ground line probe. Working aircraft use 0.60 m on every leg;
    // shorter probes can miss runway seams or uneven terrain between physics frames.
    internal const float NoseSuspensionTravel = 0.60f;
    internal const float MainSuspensionTravel = 0.60f;
    // All three deployed tire contacts share one authored plane. Spawn gives every
    // probe a small positive preload; unequal nose/main equilibrium deflections then
    // settle the aircraft onto its real tricycle load distribution.
    internal const float GearContactPlaneY = -2.34363496f;
    internal const float GearSpawnCompression = 0.03538486f;
    internal const float GroundSpawnHeight = 2.30825010f;
    internal const float NoseGearContactZ = 5.04024982f;
    internal const float MainGearContactZ = -0.76365989f;
    internal const float MainGearHalfTrack = 2.07423997f;
    // Balance the authored 13,380 kg dry mass about the measured 5.80390971 m
    // wheelbase and the approved 0.50 m dry-CG margin below. At the 25/52 mm
    // equilibrium compressions this puts 8.6149 percent of the static load on
    // the nose wheel and divides the remainder equally between the mains.
    internal const float NoseGearDryCompression = 0.025f;
    internal const float MainGearDryCompression = 0.052f;
    internal const float NoseGearSpringRate = 452308.21f;
    internal const float MainGearSpringRate = 1153366.30f;
    // Preserve the flight-tested v0.4.96 damping coefficients. Static support
    // balance depends only on spring force, so the CG correction does not need a
    // second, unrelated damping retune.
    internal const float NoseGearDampingRate = 83950f;
    internal const float MainGearDampingRate = 110300f;
    internal const float NoseTireResponse = 0.33f;
    internal const float MainTireResponse = 1f;
    internal const float TireRollingResistance = 0.01f;
    internal const float NoseGearContactArea = 0.06f;
    internal const float NoseSteeringLock = 45f;
    internal const float NoseSteeringSpeed = 60f;
    internal const float NoseAligningStrength = 5f;
    // Restore the last approved v0.4.92 dry CG. The later 1.06 m experiment put
    // 21.45% of the tested full-fuel/two-missile load on the nose and prevented
    // rotation at 95 m/s. The approved 0.50 m dry margin predicts 15.9% loaded nose
    // weight for that same configuration while remaining ahead of the main gear.
    internal const float DryCenterOfMassAheadOfMainGear = 0.50f;
    // The three-piece damage graph merges the former center, nose, and rear dry
    // structure into one Rigidbody. Keep its dry mass point separate from the
    // historical station where Nuclear Option applies fuel and payload mass.
    // The runtime controller combines these two first moments after every native
    // UnitPart.ModifyMass call; neither endpoint is a fixed loaded-aircraft CG.
    internal const float DryCentralMass = 10250f;
    internal const float DryCentralCenterOfMassZ = 0.657403290f;
    internal const float VariableLoadCenterOfMassZ = -0.344007939f;
    internal const float ControlSurfaceColliderInset = 0.96f;
    internal const float ControlSurfaceColliderMinSize = 0.08f;
    // The moving-rudder geometry averages 5.318495 m2 projected area while its
    // adjacent fixed-fin geometry averages 3.131796 m2. Preserve the established
    // 1.4 m2 moving-rudder calibration and include the missing fixed-fin fraction:
    // 1.4 * (5.318495 + 3.131796) / 5.318495 = 2.2245 m2 per full vertical tail.
    internal const float FullVerticalTailArea = 2.2245f;
    // Each engine lives inside the blended fuselage, so a broad box at its
    // center penetrates both CentralBody and RearBody. Represent the externally
    // hittable aft nozzle section instead: it remains joint-suppressed against
    // its RearBody parent and sits aft of CentralCollider with a safety gap.
    internal static readonly Vector3 EngineDamageColliderCenter = new Vector3(0f, 0f, -0.75f);
    internal static readonly Vector3 EngineDamageColliderSize = new Vector3(0.5f, 0.35f, 0.5f);
    internal const float InnerElevonLeftNeutralCorrection = -2.649f;
    internal const float InnerElevonRightNeutralCorrection = -2.087f;
    // Source frames 81 (deployed) and 1 (stowed) define main-strut travel.
    internal const float MainGearFoldAngle = 95.7148f;
    private const string ControlColliderRoot = "Assets/F117/Generated/Colliders";
    private const string DamageMeshRoot = "Assets/F117/Generated/DamageMeshes";
    private const string RadarChaffPrefabPath = "Assets/F117/Generated/F117_RadarChaff.prefab";
    private const string RadarChaffMaterialPath = "Assets/F117/Generated/Materials/F117_RadarChaff.mat";
    private const string RadarChaffTexturePath = "Assets/F117/Generated/Materials/F117_RadarChaff_Glint.asset";
    internal const float CockpitCameraRearwardOffset = 0.52f;
    internal const float JammerBusCapacityKj = 60f;
    internal const float JammerNominalPower = 13f;
    internal const float JammerChargePerEngineRpm = 0.0002f;
    private static readonly HashSet<string> RequiredDonorComponentTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Aircraft", "AeroPart", "FuelTank", "NetworkIdentity", "AircraftNetworkTransform", "TargetCam", "TargetDetector",
        "AutopilotPlane", "ControlsFilter", "LaserDesignator", "WeaponManager", "RadarLocator", "Radar",
        "Cockpit", "Canopy", "Pilot", "PowerSupply", "FlareEjector", "ChaffEjector"
    };
    private static readonly Dictionary<string, string> TextureStems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "F117_ext_glass", "windshield" }, { "F117_ext_glass_no_tint", "windshield" },
        { "F117_ext_weapons", "f117_ext_weapons" },
        { "F117_EXTERNAL_1", "f117_ext_1" }, { "F117_EXTERNAL_2", "f117_ext_2" },
        { "F117_EXTERNAL_3", "f117_ext_3" }, { "F117_EXTERNAL_4", "f117_ext_4" },
        { "F117_EXTERNAL_5", "f117_ext_5" }, { "F117_EXTERNAL_6", "f117_ext_6" },
        { "F117_EXTERNAL_7", "f117_ext_7" },
        { "F117_int_1", "f117_int_1" }, { "F117_int_2", "f117_int_2" },
        { "F117_int_2_landing_gear_knob", "f117_int_2" }, { "F117_int_3", "f117_int_3" },
        { "F117_int_4", "f117_int_4" }, { "F117_int_5", "f117_int_5" },
        { "F117_int_6", "f117_int_6" }, { "F117_int_7", "f117_int_7" },
        { "F117_int_8", "f117_int_8" }, { "F117_int_9", "f117_int_9" },
        { "F117_int_decal_1", "f117_int_decal_1" },
        { "F117_int_decal_1_GREEN", "f117_int_decal_1" },
        { "F117_int_decal_1_WHITE", "f117_int_decal_1" },
        { "F117_int_Gauges_1", "f117_int_gauges_1" },
        { "F117_int_Gauges_2", "f117_int_gauges_2" },
        { "F117_Tires", "f117_tires" }, { "F117A_external_decals_new", "f117_ext_decals" },
        { "gauge_glass", "gauge_glass" }, { "INT_CockpitFrame", "metal_paint02" },
        { "LIGHTS", "f117_lights" }, { "FORGOTTOTEXTURE", "f117_ext_6" }
    };

    internal static bool UsesAircraftSkin(string materialName)
    {
        string canonical = CanonicalMaterialName(materialName);
        return canonical != null && canonical.IndexOf("F117_EXTERNAL_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal sealed class Result
    {
        internal GameObject Instance;
        internal Bounds VisualBounds;
        internal Component Aircraft;
        internal Component CentralPart;
    }

    private sealed class TireAudioProfile
    {
        internal bool BypassEffects;
        internal bool BypassListenerEffects;
        internal bool BypassReverbZones;
        internal int Priority;
        internal float DopplerLevel;
        internal float Spread;
        internal float MinDistance;
        internal float MaxDistance;
        internal AudioRolloffMode RolloffMode;
        internal AnimationCurve CustomRolloff;

        internal static TireAudioProfile Capture(GameObject donor)
        {
            Component donorGear = donor.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(component => component != null && component.GetType().Name == "LandingGear");
            SerializedProperty sourceProperty = donorGear == null
                ? null
                : new SerializedObject(donorGear).FindProperty("tireNoiseSound");
            AudioSource source = sourceProperty?.objectReferenceValue as AudioSource;
            if (source == null)
                throw new InvalidOperationException("The Blueprinter reference aircraft has no stock tire audio profile.");

            AnimationCurve customRolloff = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            return new TireAudioProfile
            {
                BypassEffects = source.bypassEffects,
                BypassListenerEffects = source.bypassListenerEffects,
                BypassReverbZones = source.bypassReverbZones,
                Priority = source.priority,
                DopplerLevel = source.dopplerLevel,
                Spread = source.spread,
                MinDistance = source.minDistance,
                MaxDistance = source.maxDistance,
                RolloffMode = source.rolloffMode,
                CustomRolloff = customRolloff == null ? null : new AnimationCurve(customRolloff.keys)
            };
        }

        internal void Apply(AudioSource source)
        {
            source.bypassEffects = BypassEffects;
            source.bypassListenerEffects = BypassListenerEffects;
            source.bypassReverbZones = BypassReverbZones;
            source.priority = Priority;
            source.dopplerLevel = DopplerLevel;
            source.spread = Spread;
            source.minDistance = MinDistance;
            source.maxDistance = MaxDistance;
            source.rolloffMode = RolloffMode;
            if (RolloffMode == AudioRolloffMode.Custom && CustomRolloff != null)
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(CustomRolloff.keys));
        }
    }

    internal static Result Assemble(GameObject sourcePrefab, GameObject modelPrefab, string materialsRoot,
        GameObject runtimeUiFallback)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        // Stock AI taxi code requires the aircraft root itself to be a UnitPart.
        // Make the authoritative central AeroPart the prefab root and keep its name
        // aligned with the damage/status contract.
        instance.name = "F117_CentralBody";

        Component aircraft = FindComponent(instance, "Aircraft");
        Component weaponManager = FindComponent(instance, "WeaponManager");
        Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
        if (aircraft == null || weaponManager == null || rigidbody == null)
            throw new InvalidOperationException("The Blueprinter reference aircraft is missing required runtime components.");
        TireAudioProfile tireAudioProfile = TireAudioProfile.Capture(instance);

        StripReferenceArtwork(instance);
        RepairCrewMaterials(instance, materialsRoot);
        StripReferencePhysics(instance);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        visual.name = "F117_Visual";
        visual.transform.SetParent(instance.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        ConvertMaterials(visual, materialsRoot);
        F117CanopyPaint.Apply(visual, materialsRoot);
        MeshRenderer cockpitScreenRenderer = CreateCockpitScreenRenderer(visual, materialsRoot);

        Material gearDustMaterial = CreateGearDustMaterial(materialsRoot);
        Component centralPart = ConfigurePhysics(instance, visual, aircraft, rigidbody, gearDustMaterial,
            tireAudioProfile);
        ConfigureRuntimeSystems(instance, visual, aircraft, centralPart);
        ConfigureCrewAndSensors(instance, visual, aircraft, centralPart, runtimeUiFallback, cockpitScreenRenderer);
        ConfigureCanopy(instance, visual, aircraft, centralPart);
        ConfigureWeapons(instance, visual, weaponManager, centralPart);
        ConfigureRendererLists(instance, aircraft);
        PruneDonorScaffold(instance, visual);
        CreateParadeFlagOverlays(instance, visual, materialsRoot);
        BindParadeFlagDamageRenderers(instance, centralPart);

        Transform chute = FindDeep(instance.transform, "F117_DragChute");
        Renderer[] allVisualRenderers = instance.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => !IsPilot(renderer.transform))
            .Where(renderer => !(renderer is ParticleSystemRenderer))
            .Where(renderer => chute == null ||
                               (renderer.transform != chute && !renderer.transform.IsChildOf(chute)))
            .ToArray();
        Bounds visualBounds = LocalBounds(instance.transform, allVisualRenderers);
        if (visualBounds.size.sqrMagnitude < 100f)
            throw new InvalidOperationException("The production F-117 model imported at an invalid scale.");

        SerializedObject aircraftData = new SerializedObject(aircraft);
        Set(aircraftData, "weaponManager", weaponManager);
        Set(aircraftData, "cockpit", centralPart);
        Set(aircraftData, "fuelCapacity", 8250f);
        Set(aircraftData, "RCS", 0.0000005f);
        Size(aircraftData, "groundEquipment", 0);
        aircraftData.ApplyModifiedPropertiesWithoutUndo();

        return new Result
        {
            Instance = instance,
            VisualBounds = visualBounds,
            Aircraft = aircraft,
            CentralPart = centralPart
        };
    }

    private static void StripReferenceArtwork(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (IsPilot(renderer.transform))
            {
                renderer.enabled = true;
                continue;
            }
            renderer.enabled = false;
            if (renderer is SkinnedMeshRenderer skinned)
                skinned.sharedMesh = null;
            else
                renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (!IsPilot(filter.transform))
                filter.sharedMesh = null;
        foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true))
            lod.enabled = false;
    }

    private static void PruneDonorScaffold(GameObject root, GameObject visual)
    {
        bool IsProduction(Transform transform)
        {
            if (transform == visual.transform || transform.IsChildOf(visual.transform))
                return true;

            // The two authored whole-wing meshes and moving aerodynamic surfaces are
            // reparented to the AeroPart that physically owns them. Admit only objects
            // below a real AeroPart; broad F117 name matching also preserves renamed
            // donor cockpit widgets and defeats the cleanup audit.
            for (Transform current = transform; current != null && current != root.transform;
                 current = current.parent)
                if (current.GetComponent("AeroPart") != null)
                    return true;

            // Central parade overlays have no intermediate AeroPart transform.
            return transform.parent == root.transform &&
                   transform.name.StartsWith(ParadeFlagOverlayPrefix, StringComparison.Ordinal);
        }

        Component powerSupply = FindComponent(root, "PowerSupply");
        Transform pilotRoot = FindComponent(root, "Pilot")?.transform;
        if (powerSupply == null || pilotRoot == null)
            throw new InvalidOperationException("The donor scaffold is missing the retained power or pilot system.");

        foreach (Component component in root.GetComponentsInChildren<Component>(true).Reverse())
        {
            if (component == null || component is Transform || IsProduction(component.transform))
                continue;

            string typeName = component.GetType().Name;
            bool keep = RequiredDonorComponentTypes.Contains(typeName) ||
                        (component.transform == root.transform &&
                         (typeName == "AeroPart" || component is Collider)) ||
                        (component is Rigidbody && component.transform == root.transform) ||
                        (component is AudioSource && component.transform == powerSupply.transform) ||
                        (IsPilot(component.transform) &&
                         (component is Renderer || component is MeshFilter || component is Animator || component is Collider));
            if (!keep)
                UnityEngine.Object.DestroyImmediate(component, true);
        }

        var required = new HashSet<Transform> { root.transform };
        void KeepAncestors(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                required.Add(current);
                if (current == root.transform)
                    break;
            }
        }
        void KeepSubtree(Transform transform)
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
                required.Add(child);
            KeepAncestors(transform);
        }

        KeepSubtree(pilotRoot);
        Component[] retainedSystems = root.GetComponentsInChildren<Component>(true)
            .Where(component => component != null && RequiredDonorComponentTypes.Contains(component.GetType().Name))
            .ToArray();
        foreach (Component component in retainedSystems)
        {
            KeepAncestors(component.transform);
            SerializedObject data = new SerializedObject(component);
            SerializedProperty iterator = data.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                UnityEngine.Object referenced = iterator.objectReferenceValue;
                Transform referencedTransform = referenced as Transform;
                if (referencedTransform == null && referenced is Component referencedComponent)
                    referencedTransform = referencedComponent.transform;
                else if (referencedTransform == null && referenced is GameObject referencedObject)
                    referencedTransform = referencedObject.transform;
                if (referencedTransform != null && referencedTransform.IsChildOf(root.transform) && !IsProduction(referencedTransform))
                    KeepAncestors(referencedTransform);
            }
        }

        Transform[] donorTransforms = root.GetComponentsInChildren<Transform>(true)
            .Where(transform => transform != root.transform && !IsProduction(transform))
            .OrderByDescending(Depth)
            .ToArray();
        foreach (Transform transform in donorTransforms)
            if (transform != null && !required.Contains(transform))
                UnityEngine.Object.DestroyImmediate(transform.gameObject, true);

        Transform avionics = FindComponent(root, "WeaponManager")?.transform;
        Transform electrical = FindComponent(root, "PowerSupply")?.transform;
        Transform sensorLocator = FindComponent(root, "RadarLocator")?.transform;
        Transform canopy = FindComponent(root, "Canopy")?.transform;
        if (avionics == null || electrical == null || sensorLocator == null || canopy == null)
            throw new InvalidOperationException("A required retained system disappeared during donor pruning.");
        avionics.name = "F117_Avionics";
        electrical.name = "F117_Electrical";
        sensorLocator.name = "F117_SensorLocator";
        if (canopy.parent != null && canopy.parent != avionics)
            canopy.parent.name = "F117_CanopySystems";

        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (IsProduction(transform))
                continue;
            transform.name = transform.name.Replace("Aryx_F16M_", "F117_").Replace("F16M_", "F117_");
        }
    }

    private static int Depth(Transform transform)
    {
        int depth = 0;
        for (Transform current = transform; current != null; current = current.parent)
            depth++;
        return depth;
    }

    private static bool IsPilot(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
            if (string.Equals(current.name, "pilot", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void RepairCrewMaterials(GameObject root, string materialsRoot)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable for crew materials.");

        Material flightSuit = CreateFlatMaterial(shader, "F117_Crew", new Color(0.18f, 0.20f, 0.17f, 1f), 0.05f, 0.28f,
            materialsRoot + "/F117_Crew.mat");
        Material ejectionSeat = CreateFlatMaterial(shader, "F117_EjectionSeat", new Color(0.055f, 0.06f, 0.065f, 1f), 0.2f, 0.22f,
            materialsRoot + "/F117_EjectionSeat.mat");

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled && IsPilot(item.transform)))
        {
            Material[] slots = renderer.sharedMaterials;
            Material replacement = renderer.name.IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0
                ? ejectionSeat : flightSuit;
            for (int index = 0; index < slots.Length; index++)
                if (slots[index] == null)
                    slots[index] = replacement;
            renderer.sharedMaterials = slots;
        }
    }

    private static Material CreateFlatMaterial(Shader shader, string name, Color color, float metallic, float smoothness, string path)
    {
        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void StripReferencePhysics(GameObject root)
    {
        string[] componentTypes =
        {
            "AeroPart", "ControlSurface", "LandingGear", "GearPart", "Turbojet", "JetNozzle",
            "Airbrake", "BayDoor", "NavLights", "HighLiftDevice", "BuildingLights",
            "CockpitWarningLights", "RadarJammer", "VaporEffect", "SetGlobalParticles",
            "UniversalAdditionalLightData"
        };
        foreach (string typeName in componentTypes)
            foreach (Component component in FindComponents(root, typeName))
                UnityEngine.Object.DestroyImmediate(component, true);

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (collider is CapsuleCollider && IsPilot(collider.transform))
                continue;
            UnityEngine.Object.DestroyImmediate(collider, true);
        }
        foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            UnityEngine.Object.DestroyImmediate(source, true);
        foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            UnityEngine.Object.DestroyImmediate(particles, true);
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light, true);
    }

    private static void ConvertMaterials(GameObject visual, string materialsRoot)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable.");
        Material aircraftSkin = AssetDatabase.LoadAssetAtPath<Material>(AircraftSkinTemplatePath);
        if (aircraftSkin == null)
            throw new InvalidOperationException("The reference AircraftSkin material is unavailable.");

        Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        int index = 0;
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Material source = slots[slot];
                if (source == null)
                    continue;
                if (!converted.TryGetValue(source, out Material target))
                {
                    string canonicalMaterialName = CanonicalMaterialName(source.name);
                    bool damageSkin = UsesAircraftSkin(canonicalMaterialName);
                    target = damageSkin ? new Material(aircraftSkin) : new Material(shader);
                    target.name = "F117_" + SafeName(source.name);
                    Color color = source.HasProperty("_BaseColor")
                        ? source.GetColor("_BaseColor")
                        : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                    target.SetColor("_BaseColor", color);
                    if (!damageSkin)
                        target.SetFloat("_Metallic", source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0.15f);
                    float smoothness = source.HasProperty("_Smoothness")
                        ? source.GetFloat("_Smoothness")
                        : source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.35f;
                    target.SetFloat("_Smoothness", smoothness);
                    ApplyProductionTextures(target, canonicalMaterialName, materialsRoot);
                    ConfigureSurface(target, canonicalMaterialName);
                    if (damageSkin)
                    {
                        // Author the native AircraftSkin fields from the extracted template,
                        // then serialize the material with a valid local shader. Materials
                        // whose shader reference is null appear in an AssetBundle's name table
                        // but AssetBundle.LoadAsset<Material> returns null at runtime, so
                        // Blueprinter cannot apply the native AircraftSkin shader patch.
                        // The saved AircraftSkin texture/float properties remain on the
                        // material and become active when Blueprinter replaces this placeholder.
                        target.shader = shader;
                        ApplyProductionPreviewTextures(target, canonicalMaterialName);
                    }
                    string path = materialsRoot + "/" + index.ToString("D2") + "_" + SafeName(source.name) + ".mat";
                    AssetDatabase.CreateAsset(target, path);
                    converted.Add(source, target);
                    index++;
                }
                slots[slot] = target;
            }
            renderer.sharedMaterials = slots;
        }
        if (converted.Count == 0)
            throw new InvalidOperationException("No F-117 materials were converted.");
    }

    private static MeshRenderer CreateCockpitScreenRenderer(GameObject visual, string materialsRoot)
    {
        Transform cockpit = FindDeep(visual.transform, "F117_Cockpit_Mesh");
        MeshRenderer cockpitRenderer = cockpit == null ? null : cockpit.GetComponent<MeshRenderer>();
        MeshFilter cockpitFilter = cockpit == null ? null : cockpit.GetComponent<MeshFilter>();
        if (cockpitRenderer == null || cockpitFilter == null || cockpitFilter.sharedMesh == null)
            throw new InvalidOperationException("The F-117 cockpit mesh is unavailable for tactical-screen extraction.");

        Material[] materials = cockpitRenderer.sharedMaterials;
        int screenIndex = Array.FindIndex(materials, material => material != null &&
            material.name.IndexOf("MFD_Left", StringComparison.OrdinalIgnoreCase) >= 0);
        if (screenIndex < 0 || screenIndex >= cockpitFilter.sharedMesh.subMeshCount)
            throw new InvalidOperationException("The F-117 MFD_Left submesh is missing.");

        Mesh imported = cockpitFilter.sharedMesh;
        int[] screenTriangles = imported.GetTriangles(screenIndex);
        if (screenTriangles.Length < 3)
            throw new InvalidOperationException("The F-117 MFD_Left submesh contains no display geometry.");

        // Stock aircraft reference one dedicated renderer from both Cockpit and
        // TargetCam. Split the display out of the combined cockpit mesh so the
        // native render-texture material has one unambiguous output surface.
        Mesh bodyMesh = UnityEngine.Object.Instantiate(imported);
        bodyMesh.name = "F117_Cockpit_WithoutScreen";
        bodyMesh.SetTriangles(Array.Empty<int>(), screenIndex, false);
        bodyMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(bodyMesh, materialsRoot + "/F117_Cockpit_WithoutScreen.asset");
        cockpitFilter.sharedMesh = bodyMesh;

        Mesh screenMesh = ExtractMeshSubset(imported, screenTriangles, "F117_Tacscreen_Mesh");
        MapCompleteCockpitAtlas(screenMesh, screenMesh.triangles);
        AssetDatabase.CreateAsset(screenMesh, materialsRoot + "/F117_Tacscreen_Mesh.asset");

        GameObject screen = new GameObject("F117_Tacscreen");
        screen.layer = cockpit.gameObject.layer;
        screen.transform.SetParent(cockpit.parent, false);
        screen.transform.localPosition = cockpit.localPosition;
        screen.transform.localRotation = cockpit.localRotation;
        screen.transform.localScale = cockpit.localScale;
        MeshFilter filter = screen.AddComponent<MeshFilter>();
        filter.sharedMesh = screenMesh;
        MeshRenderer renderer = screen.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = materials[screenIndex];
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = true;
        return renderer;
    }

    private static Mesh ExtractMeshSubset(Mesh source, int[] sourceTriangles, string name)
    {
        int[] usedVertices = sourceTriangles.Distinct().OrderBy(index => index).ToArray();
        var remap = usedVertices.Select((sourceIndex, targetIndex) => new { sourceIndex, targetIndex })
            .ToDictionary(item => item.sourceIndex, item => item.targetIndex);
        Mesh mesh = new Mesh
        {
            name = name,
            indexFormat = usedVertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        Vector3[] sourceVertices = source.vertices;
        mesh.SetVertices(usedVertices.Select(index => sourceVertices[index]).ToList());
        Vector3[] sourceNormals = source.normals;
        if (sourceNormals != null && sourceNormals.Length == source.vertexCount)
            mesh.SetNormals(usedVertices.Select(index => sourceNormals[index]).ToList());
        Vector4[] sourceTangents = source.tangents;
        if (sourceTangents != null && sourceTangents.Length == source.vertexCount)
            mesh.SetTangents(usedVertices.Select(index => sourceTangents[index]).ToList());
        Color[] sourceColors = source.colors;
        if (sourceColors != null && sourceColors.Length == source.vertexCount)
            mesh.SetColors(usedVertices.Select(index => sourceColors[index]).ToList());
        for (int channel = 0; channel < 8; channel++)
        {
            var sourceUvs = new List<Vector4>();
            source.GetUVs(channel, sourceUvs);
            if (sourceUvs.Count == source.vertexCount)
                mesh.SetUVs(channel, usedVertices.Select(index => sourceUvs[index]).ToList());
        }
        mesh.SetTriangles(sourceTriangles.Select(index => remap[index]).ToArray(), 0, true);
        if (mesh.normals == null || mesh.normals.Length != mesh.vertexCount)
            mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private readonly struct CockpitAtlasRect
    {
        internal readonly float MinU;
        internal readonly float MinV;
        internal readonly float MaxU;
        internal readonly float MaxV;

        internal CockpitAtlasRect(float minU, float minV, float maxU, float maxV)
        {
            MinU = minU;
            MinV = minV;
            MaxU = maxU;
            MaxV = maxV;
        }
    }

    private static void MapCompleteCockpitAtlas(Mesh mesh, int[] triangles)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        if (uvs == null || uvs.Length != vertices.Length)
            throw new InvalidOperationException("The F-117 tactical-screen mesh has no complete UV channel.");

        var trianglesByVertex = new Dictionary<int, List<int>>();
        for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
            for (int corner = 0; corner < 3; corner++)
            {
                int vertex = triangles[triangle * 3 + corner];
                if (!trianglesByVertex.TryGetValue(vertex, out List<int> connected))
                {
                    connected = new List<int>();
                    trianglesByVertex.Add(vertex, connected);
                }
                connected.Add(triangle);
            }
        var remaining = new HashSet<int>(Enumerable.Range(0, triangles.Length / 3));
        var components = new List<HashSet<int>>();
        while (remaining.Count > 0)
        {
            int seed = remaining.First();
            remaining.Remove(seed);
            var component = new HashSet<int>();
            var pending = new Queue<int>();
            pending.Enqueue(seed);
            while (pending.Count > 0)
            {
                int triangle = pending.Dequeue();
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    component.Add(vertex);
                    foreach (int neighbor in trianglesByVertex[vertex])
                        if (remaining.Remove(neighbor))
                            pending.Enqueue(neighbor);
                }
            }
            components.Add(component);
        }
        if (components.Count != 3)
            throw new InvalidOperationException("The F-117 tactical screen requires exactly three display islands.");

        foreach (HashSet<int> component in components)
        {
            float minU = component.Min(vertex => uvs[vertex].x);
            float minV = component.Min(vertex => uvs[vertex].y);
            float maxU = component.Max(vertex => uvs[vertex].x);
            float maxV = component.Max(vertex => uvs[vertex].y);
            bool camera = minU < 0.2f;
            CockpitAtlasRect full = camera
                ? new CockpitAtlasRect(0.00110f, 0.00011f, 0.79063f, 0.99989f)
                : maxV < 0.36f
                    ? new CockpitAtlasRect(0.79230f, 0.02251f, 0.99359f, 0.34329f)
                    : new CockpitAtlasRect(0.79073f, 0.38727f, 0.99510f, 0.69994f);
            foreach (int vertex in component)
            {
                float normalizedU = Mathf.InverseLerp(minU, maxU, uvs[vertex].x);
                float normalizedV = Mathf.InverseLerp(minV, maxV, uvs[vertex].y);
                uvs[vertex] = new Vector2(
                    Mathf.Lerp(full.MinU, full.MaxU, normalizedU),
                    Mathf.Lerp(full.MinV, full.MaxV, normalizedV));
            }
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
    }

    private static Material CreateGearDustMaterial(string materialsRoot)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable for the gear dust fallback.");

        Material material = new Material(shader) { name = "F117_GearDust_Invisible" };
        Color clear = new Color(0.25f, 0.20f, 0.14f, 0f);
        material.SetColor("_BaseColor", clear);
        material.SetColor("_Color", clear);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        EnableLocalKeyword(material, "_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, materialsRoot + "/F117_GearDust_Invisible.mat");
        return material;
    }

    private static void CreateParadeFlagOverlays(GameObject root, GameObject visual, string materialsRoot)
    {
        string[] hingeNames =
        {
            "F117_GearDoor_Nose_CloseHinge",
            "F117_GearDoor_Left_Outer_CloseHinge",
            "F117_GearDoor_Left_Inner_CloseHinge",
            "F117_GearDoor_Right_Outer_CloseHinge",
            "F117_GearDoor_Right_Inner_CloseHinge"
        };
        Transform[] hinges = hingeNames
            .Select(name => FindDeep(visual.transform, name))
            .ToArray();
        if (hinges.Any(hinge => hinge == null))
            throw new InvalidOperationException(
                "Farewell Flag requires all five native landing-gear door close hinges.");

        Quaternion[] authoredRotations = hinges
            .Select(hinge => hinge.localRotation)
            .ToArray();
        try
        {
            // The imported doors are authored open. Generate against the actual
            // clean-flight lower envelope, whose native closed hinge state is identity.
            foreach (Transform hinge in hinges)
                hinge.localRotation = Quaternion.identity;
            CreateParadeFlagOverlaysClosed(root, visual, materialsRoot);
        }
        finally
        {
            for (int index = 0; index < hinges.Length; index++)
                hinges[index].localRotation = authoredRotations[index];
        }
    }

    private static void CreateParadeFlagOverlaysClosed(GameObject root, GameObject visual,
        string materialsRoot)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ParadeFlagTexturePath);
        if (texture == null)
            throw new InvalidOperationException("The deterministic F-117 parade-flag texture is unavailable.");

        MeshFilter[] allFilters = root.GetComponentsInChildren<MeshFilter>(true);
        MeshFilter[] filters = ParadeFlagSurfaceNames
            .Select(name => RequireParadeFlagFilter(allFilters, name))
            .ToArray();
        MeshFilter[] occluderFilters = ParadeFlagOccluderNames
            .Select(name => RequireParadeFlagFilter(allFilters, name))
            .ToArray();

        Bounds planform;
        List<ParadeSourceTriangle> candidates =
            GatherParadeFlagCandidates(visual.transform, filters, out planform);
        if (planform.size.x < 10f || planform.size.z < 15f)
            throw new InvalidOperationException("The F-117 parade projection has invalid exterior-skin bounds.");
        List<ParadeSourceTriangle> occluders =
            GatherParadeFlagOccluders(visual.transform, occluderFilters);
        ParadeSpatialIndex spatialIndex = new ParadeSpatialIndex(occluders);
        var builders = ParadeFlagSurfaceNames.ToDictionary(
            name => name, name => new ParadeMeshBuilder(name), StringComparer.Ordinal);

        // Lower-envelope clipping is pure geometry: every candidate reads the
        // immutable candidate/occluder lists and writes only its own result slot.
        // Run that expensive stage across all available logical processors, then
        // merge in canonical candidate order so generated meshes remain bit-for-bit
        // deterministic and all Unity object creation stays on the editor thread.
        var visibleByCandidate = new List<List<Vector2>>[candidates.Count];
        int workerCount = Math.Max(1, Environment.ProcessorCount);
        Debug.Log("F-117 Farewell Flag: clipping " + candidates.Count +
            " underside candidates across " + workerCount + " logical processors.");
        Parallel.For(0, candidates.Count,
            new ParallelOptions { MaxDegreeOfParallelism = workerCount },
            candidateIndex =>
        {
            ParadeSourceTriangle candidate = candidates[candidateIndex];
            int[] nearbyOccluderIndices = spatialIndex.Query(candidate).ToArray();
            List<List<Vector2>> visible = new List<List<Vector2>>
            {
                candidate.ProjectedTriangle()
            };
            foreach (int occluderIndex in nearbyOccluderIndices)
            {
                ParadeSourceTriangle occluder = occluders[occluderIndex];
                // Every candidate already belongs to the approved exterior-skin
                // material of its owner. Do not subtract other facets from that
                // same authored surface; self-clipping is redundant, can carve
                // legitimate door panels, and dominated build time on the dense
                // central mesh. Separate moving owners still clip one another in
                // the closed-airframe pose.
                if (string.Equals(candidate.OwnerName, occluder.OwnerName,
                        StringComparison.Ordinal))
                    continue;
                double blockingThreshold = ParadeBlockingHeightThreshold(
                    candidate, occluder);
                if (candidate.FaceKey.Equals(occluder.FaceKey) ||
                    occluder.MinY >= candidate.MaxY - blockingThreshold ||
                    !candidate.ProjectionOverlaps(occluder))
                    continue;
                List<Vector2> blocking = ParadeBlockingPolygon(candidate, occluder);
                if (blocking.Count < 3)
                    continue;

                var remainder = new List<List<Vector2>>();
                foreach (List<Vector2> polygon in visible)
                    remainder.AddRange(SubtractConvexPolygon(polygon, blocking));
                visible = remainder;
                if (visible.Count == 0)
                    break;
            }

            var resolved = new List<List<Vector2>>();
            foreach (List<Vector2> rawPolygon in visible)
            {
                // Repeated float intersections can accumulate sub-micrometre drift
                // beyond the authored source edge.  Reclip once to the exact welded
                // source triangle before emission so every generated point remains
                // on its declared exterior face and cannot form an external sliver.
                List<Vector2> polygon = IntersectConvexPolygons(
                    rawPolygon, candidate.ProjectedTriangle());
                if (!ParadePolygonAreaIsResolved(polygon))
                    continue;
                EnsureParadePolygonVisible(candidate, polygon, occluders,
                    nearbyOccluderIndices);
                resolved.Add(polygon);
            }
            visibleByCandidate[candidateIndex] = resolved;
        });

        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            ParadeSourceTriangle candidate = candidates[candidateIndex];
            foreach (List<Vector2> polygon in visibleByCandidate[candidateIndex])
                builders[candidate.OwnerName].AddPolygon(candidate, polygon,
                    occluders, spatialIndex);
        }

        Shader previewShader = Shader.Find("Universal Render Pipeline/Lit");
        if (previewShader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable for the parade livery.");
        Material aircraftSkin = AssetDatabase.LoadAssetAtPath<Material>(AircraftSkinTemplatePath);
        Texture2D mirrorFinish = AssetDatabase.LoadAssetAtPath<Texture2D>(MirrorFinishTexturePath);
        if (aircraftSkin == null || mirrorFinish == null)
            throw new InvalidOperationException("Farewell Flag requires the native AircraftSkin template and mirror finish.");

        TextureImporter flagImporter = AssetImporter.GetAtPath(ParadeFlagTexturePath) as TextureImporter;
        if (flagImporter == null)
            throw new InvalidOperationException("Farewell Flag source has no TextureImporter.");
        bool restoreReadable = !flagImporter.isReadable;
        var cleanVariants = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        var damageVariants = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        if (ParadeFlagFinishTints.Length != ParadeFlagFinishKeys.Length)
            throw new InvalidOperationException("Farewell Flag finish keys and authored tints are out of sync.");
        try
        {
            if (restoreReadable)
            {
                flagImporter.isReadable = true;
                flagImporter.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ParadeFlagTexturePath);
            }
            for (int index = 0; index < ParadeFlagFinishKeys.Length; index++)
            {
                string key = ParadeFlagFinishKeys[index];
                Texture2D clean = CreateTintedParadeFlagTexture(texture, key,
                    ParadeFlagFinishTints[index], materialsRoot);
                Texture2D damaged = CreateDamageAlbedo(clean, "ParadeFlag_" + key, materialsRoot);
                cleanVariants.Add(key, clean);
                damageVariants.Add(key, damaged);
                if (clean.isReadable)
                {
                    clean.Apply(false, true);
                    EditorUtility.SetDirty(clean);
                }
            }
        }
        finally
        {
            if (restoreReadable)
            {
                flagImporter = AssetImporter.GetAtPath(ParadeFlagTexturePath) as TextureImporter;
                if (flagImporter != null)
                {
                    flagImporter.isReadable = false;
                    flagImporter.SaveAndReimport();
                }
            }
        }

        Texture2D defaultClean = cleanVariants[ParadeFlagFinishKeys[0]];
        Texture2D defaultDamage = damageVariants[ParadeFlagFinishKeys[0]];
        Material material = new Material(aircraftSkin) { name = ParadeFlagMaterialName };
        SetSavedTexture(material, "_Basecolor", defaultClean);
        SetSavedTexture(material, "_BasecolorDmg", defaultDamage);
        SetSavedTexture(material, "_Metallic", mirrorFinish);
        // Keep the template's real native normal, damaged-normal, and AO assets.
        // Unity's Texture2D.normalTexture/whiteTexture are transient built-ins;
        // assigning them here serializes fileID 0 and silently strips the native
        // AircraftSkin contract when the generated material is reloaded.
        material.SetFloat("_HitPoints", 100f);
        material.SetFloat("_Glossiness", 0f);
        // A local valid shader keeps the bundled material loadable. Blueprinter patches
        // this exact material back to AircraftSkin before the runtime profile hydrates
        // the native clean/damaged maps and current hit-point value.
        material.shader = previewShader;
        material.SetTexture("_BaseMap", defaultClean);
        material.SetTexture("_MainTex", defaultClean);
        material.SetTexture("_MetallicGlossMap", mirrorFinish);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetFloat("_Metallic", 1f);
        material.SetFloat("_Smoothness", 1f);
        material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Back);
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = -1;
        AssetDatabase.CreateAsset(material, materialsRoot + "/" + ParadeFlagMaterialName + ".mat");

        int overlayCount = 0;
        for (int index = 0; index < filters.Length; index++)
            if (CreateParadeFlagOverlay(visual.transform, filters[index], material, planform,
                builders[filters[index].transform.name], materialsRoot, index))
                overlayCount++;
        if (overlayCount != ParadeFlagSurfaceNames.Length)
            throw new InvalidOperationException("The F-117 parade livery generated " +
                overlayCount + " of " + ParadeFlagSurfaceNames.Length +
                " required exterior underside overlays.");
    }

    private static Texture2D CreateTintedParadeFlagTexture(Texture2D source, string key,
        Color tint, string materialsRoot)
    {
        if (source == null || !source.isReadable)
            throw new InvalidOperationException("Farewell Flag source must be readable while baking " + key + ".");
        string outputPath = materialsRoot + "/F117_ParadeFlag_" + key + ".asset";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        if (existing != null)
            return existing;

        Color32[] pixels = source.GetPixels32();
        for (int index = 0; index < pixels.Length; index++)
        {
            Color32 pixel = pixels[index];
            float red = Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(pixel.r / 255f) * tint.r);
            float green = Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(pixel.g / 255f) * tint.g);
            float blue = Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(pixel.b / 255f) * tint.b);
            pixels[index] = new Color(Mathf.Clamp01(red), Mathf.Clamp01(green),
                Mathf.Clamp01(blue), pixel.a / 255f);
        }
        Texture2D output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = "F117_ParadeFlag_" + key,
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel
        };
        output.SetPixels32(pixels);
        output.Apply(true, false);
        AssetDatabase.CreateAsset(output, outputPath);
        return output;
    }

    private static MeshFilter RequireParadeFlagFilter(MeshFilter[] allFilters, string name)
    {
        MeshFilter[] matches = allFilters
            .Where(filter => filter != null && filter.sharedMesh != null &&
                filter.GetComponent<Renderer>() != null &&
                string.Equals(filter.transform.name, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Farewell Flag requires exactly one mesh named " +
                name + "; found " + matches.Length + ".");
        return matches[0];
    }

    internal static string ParadeFlagMaterialFamily(Material material)
    {
        if (material == null || string.IsNullOrEmpty(material.name))
            return null;
        string name = material.name;
        if (name.StartsWith("F117_F117_EXTERNAL_", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("F117_".Length);
        string canonical = CanonicalMaterialName(name);
        for (int family = 1; family <= 7; family++)
            if (string.Equals(canonical, "F117_EXTERNAL_" + family,
                    StringComparison.OrdinalIgnoreCase))
                return "F117_EXTERNAL_" + family;
        return null;
    }

    internal static bool IsParadeFlagMaterial(Material material, string ownerName)
    {
        string family = ParadeFlagMaterialFamily(material);
        if (family == null)
            return false;
        switch (ownerName)
        {
            case "F117_Exterior_Mesh":
                return family != "F117_EXTERNAL_7";
            case "F117_Exterior_LeftWing_Mesh":
            case "F117_Elevon_L_Inner_Mesh":
            case "F117_Elevon_L_Outer_Mesh":
                return family == "F117_EXTERNAL_3";
            case "F117_Exterior_RightWing_Mesh":
            case "F117_Elevon_R_Inner_Mesh":
            case "F117_Elevon_R_Outer_Mesh":
                return family == "F117_EXTERNAL_4";
            case "F117_BayDoor_Left_Mesh":
            case "F117_BayDoor_Right_Mesh":
                // EXTERNAL_7 and EXTERNAL_7.001 are coincident internal door
                // shells. The authored outward-visible door skin is EXTERNAL_5.
                return family == "F117_EXTERNAL_5";
            case "F117_GearDoor_Nose_Mesh":
            case "F117_GearDoor_Left_Outer_Mesh":
            case "F117_GearDoor_Left_Inner_Mesh":
            case "F117_GearDoor_Right_Outer_Mesh":
            case "F117_GearDoor_Right_Inner_Mesh":
                // EXTERNAL_2 is the door's external skin. EXTERNAL_6 on the
                // inner doors is linkage/mechanism geometry inside the well.
                return family == "F117_EXTERNAL_2";
            default:
                return false;
        }
    }

    private static List<ParadeSourceTriangle> GatherParadeFlagCandidates(Transform visualRoot,
        MeshFilter[] filters, out Bounds planform)
    {
        planform = new Bounds();
        bool initialized = false;
        var candidates = new Dictionary<ParadeOwnedFaceKey, ParadeSourceTriangle>();
        var eligibleCounts = new int[ParadeFlagSurfaceNames.Length];
        var downwardCounts = new int[ParadeFlagSurfaceNames.Length];
        for (int ownerIndex = 0; ownerIndex < filters.Length; ownerIndex++)
        {
            MeshFilter filter = filters[ownerIndex];
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = filter.GetComponent<Renderer>().sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (subMesh >= materials.Length ||
                    !IsParadeFlagMaterial(materials[subMesh], filter.transform.name))
                    continue;
                int[] triangles = mesh.GetTriangles(subMesh);
                eligibleCounts[ownerIndex] += triangles.Length / 3;
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    Vector3 a = ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle]]);
                    Vector3 b = ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle + 1]]);
                    Vector3 c = ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle + 2]]);
                    foreach (Vector3 point in new[] { a, b, c })
                    {
                        Vector3 measured = ParadePointKey.From(point).ToVector3();
                        if (!initialized)
                        {
                            planform = new Bounds(measured, Vector3.zero);
                            initialized = true;
                        }
                        else
                            planform.Encapsulate(measured);
                    }

                    ParadeSourceTriangle candidate =
                        new ParadeSourceTriangle(filter.transform.name, a, b, c);
                    Vector3 weldedCross = Vector3.Cross(
                        candidate.B - candidate.A, candidate.C - candidate.A);
                    double weldedMagnitude = ParadeMagnitude(weldedCross);
                    // The built mesh uses the intentional 10 um welded vertices.
                    // Classify direction on that exact geometry as well; raw FBX
                    // normals can select a face whose welded game-space facet is no
                    // longer downward, allowing a fan triangle to bridge across it.
                    if (weldedMagnitude == 0d ||
                        -weldedCross.y / weldedMagnitude < ParadeFlagMinimumDownwardDot)
                        continue;
                    downwardCounts[ownerIndex]++;
                    if (!candidate.HasProjection)
                        continue;
                    var ownedKey = new ParadeOwnedFaceKey(
                        filter.transform.name, candidate.FaceKey);
                    if (candidates.ContainsKey(ownedKey))
                        throw new InvalidOperationException("Farewell Flag source contains duplicate eligible face " +
                            ownedKey + ".");
                    candidates.Add(ownedKey, candidate);
                }
            }
        }
        if (!initialized)
            throw new InvalidOperationException("Farewell Flag has no eligible source planform.");
        var sourceDrift = new List<string>();
        for (int owner = 0; owner < ParadeFlagSurfaceNames.Length; owner++)
        {
            if (eligibleCounts[owner] != ParadeFlagEligibleSourceTriangleCounts[owner] ||
                downwardCounts[owner] != ParadeFlagDownwardSourceTriangleCounts[owner])
                sourceDrift.Add(
                    ParadeFlagSurfaceNames[owner] + ": expected eligible/downward " +
                    ParadeFlagEligibleSourceTriangleCounts[owner] + "/" +
                    ParadeFlagDownwardSourceTriangleCounts[owner] + ", found " +
                    eligibleCounts[owner] + "/" + downwardCounts[owner]);
        }
        if (sourceDrift.Count != 0)
            throw new InvalidOperationException("Farewell Flag source drift: " +
                string.Join("; ", sourceDrift) + ".");
        return candidates.Values
            .OrderBy(candidate => Array.IndexOf(ParadeFlagSurfaceNames, candidate.OwnerName))
            .ThenBy(candidate => candidate.FaceKey)
            .ToList();
    }

    private static List<ParadeSourceTriangle> GatherParadeFlagOccluders(Transform visualRoot,
        MeshFilter[] filters)
    {
        var occluders = new Dictionary<ParadeFaceKey, ParadeSourceTriangle>();
        var uniqueFaces = new HashSet<ParadeFaceKey>();
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = filter.GetComponent<Renderer>().sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                // Only the same authored exterior-skin families that can receive
                // the flag may block it. Internal shells, linkages, wells, and
                // decal cards are not part of the closed exterior envelope.
                if (subMesh >= materials.Length ||
                    !IsParadeFlagMaterial(materials[subMesh], filter.transform.name))
                    continue;
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    ParadeSourceTriangle occluder = new ParadeSourceTriangle(
                        filter.transform.name,
                        ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle]]),
                        ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle + 1]]),
                        ParadeRootPoint(visualRoot, filter, vertices[triangles[triangle + 2]]));
                    if (!uniqueFaces.Add(occluder.FaceKey) || !occluder.HasProjection)
                        continue;
                    occluders.Add(occluder.FaceKey, occluder);
                }
            }
        }
        if (uniqueFaces.Count != ParadeFlagUniqueOccluderFaceCount ||
            occluders.Count != ParadeFlagProjectedOccluderFaceCount)
            throw new InvalidOperationException("Farewell Flag occluder manifest drift: expected " +
                ParadeFlagUniqueOccluderFaceCount + " unique / " +
                ParadeFlagProjectedOccluderFaceCount + " projected faces, found " +
                uniqueFaces.Count + " / " + occluders.Count + ".");
        return occluders.Values.OrderBy(occluder => occluder.FaceKey).ToList();
    }

    private static Vector3 ParadeRootPoint(Transform visualRoot, MeshFilter filter,
        Vector3 localPoint)
    {
        return visualRoot.InverseTransformPoint(filter.transform.TransformPoint(localPoint));
    }

    private static List<Vector2> ParadeBlockingPolygon(ParadeSourceTriangle candidate,
        ParadeSourceTriangle occluder)
    {
        List<Vector2> intersection = IntersectConvexPolygons(
            candidate.ProjectedTriangle(), occluder.ProjectedTriangle());
        return intersection.Count < 3
            ? new List<Vector2>()
            : ClipParadeHeight(intersection, candidate, occluder);
    }

    private static List<Vector2> IntersectConvexPolygons(List<Vector2> subject,
        List<Vector2> clip)
    {
        List<Vector2> result = CleanParadePolygon(subject);
        clip = CleanParadePolygon(clip);
        for (int edge = 0; edge < clip.Count && result.Count >= 3; edge++)
            result = ClipParadeEdge(result, clip[edge], clip[(edge + 1) % clip.Count], true);
        return CleanParadePolygon(result);
    }

    private static IEnumerable<List<Vector2>> SubtractConvexPolygon(List<Vector2> subject,
        List<Vector2> clip)
    {
        var outsidePieces = new List<List<Vector2>>();
        List<Vector2> remainder = CleanParadePolygon(subject);
        clip = CleanParadePolygon(clip);
        for (int edge = 0; edge < clip.Count && remainder.Count >= 3; edge++)
        {
            Vector2 a = clip[edge];
            Vector2 b = clip[(edge + 1) % clip.Count];
            List<Vector2> outside = ClipParadeEdge(remainder, a, b, false);
            if (outside.Count >= 3)
                outsidePieces.Add(outside);
            remainder = ClipParadeEdge(remainder, a, b, true);
        }
        return outsidePieces;
    }

    private static List<Vector2> ClipParadeEdge(List<Vector2> polygon, Vector2 a,
        Vector2 b, bool keepInside)
    {
        var result = new List<Vector2>();
        if (polygon.Count == 0)
            return result;
        Vector2 previous = polygon[polygon.Count - 1];
        double previousValue = ParadeCrossDouble(b - a, previous - a);
        bool previousInside = keepInside
            ? previousValue >= 0d
            : previousValue <= 0d;
        foreach (Vector2 current in polygon)
        {
            double currentValue = ParadeCrossDouble(b - a, current - a);
            bool currentInside = keepInside
                ? currentValue >= 0d
                : currentValue <= 0d;
            if (currentInside != previousInside)
            {
                double denominator = previousValue - currentValue;
                if (denominator != 0d)
                    result.Add(ParadeInterpolate(previous, current,
                        previousValue / denominator));
            }
            if (currentInside)
                result.Add(current);
            previous = current;
            previousValue = currentValue;
            previousInside = currentInside;
        }
        return CleanParadePolygon(result);
    }

    private static List<Vector2> ClipParadeHeight(List<Vector2> polygon,
        ParadeSourceTriangle candidate, ParadeSourceTriangle occluder)
    {
        var result = new List<Vector2>();
        if (polygon.Count == 0)
            return result;
        Vector2 previous = polygon[polygon.Count - 1];
        double blockingThreshold = ParadeBlockingHeightThreshold(candidate, occluder);
        double previousValue = candidate.HeightAtPrecise(previous) -
            blockingThreshold - occluder.HeightAtPrecise(previous);
        bool previousInside = previousValue >= 0d;
        foreach (Vector2 current in polygon)
        {
            double currentValue = candidate.HeightAtPrecise(current) -
                blockingThreshold - occluder.HeightAtPrecise(current);
            bool currentInside = currentValue >= 0d;
            if (currentInside != previousInside)
            {
                double denominator = previousValue - currentValue;
                if (denominator != 0d)
                    result.Add(ParadeInterpolate(previous, current,
                        previousValue / denominator));
            }
            if (currentInside)
                result.Add(current);
            previous = current;
            previousValue = currentValue;
            previousInside = currentInside;
        }
        return CleanParadePolygon(result);
    }

    private static double ParadeBlockingHeightThreshold(ParadeSourceTriangle candidate,
        ParadeSourceTriangle occluder)
    {
        // Clipping happens before X/Z and the reconstructed candidate Y are welded
        // to the 10 um output grid.  Propagate that measured half-cell displacement
        // through the relative height plane so quantization cannot move a fragment
        // back underneath a steep exterior/door facet after it was clipped.
        double halfCell = ParadeFlagWeldTolerance * 0.5d;
        double relativeX = candidate.HeightGradientX - occluder.HeightGradientX;
        double relativeZ = candidate.HeightGradientZ - occluder.HeightGradientZ;
        double maximumWeldError = halfCell *
            (Math.Abs(relativeX) + Math.Abs(relativeZ));
        maximumWeldError += halfCell; // reconstructed candidate-Y rounding
        // Blocking begins earlier by the maximum displacement, so no point that
        // survives clipping can cross the validator's physical clearance after weld.
        return ParadeFlagVisibilityClearance - maximumWeldError;
    }

    private static Vector2 ParadeInterpolate(Vector2 from, Vector2 to, double amount)
    {
        // Vector2.Lerp performs both the ratio conversion and multiplication in
        // float, which displaced metre-scale mesh intersections by up to 0.5 um.
        // Preserve double precision until the final Vector2 storage operation.
        amount = Math.Max(0d, Math.Min(1d, amount));
        return new Vector2(
            (float)((double)from.x + ((double)to.x - from.x) * amount),
            (float)((double)from.y + ((double)to.y - from.y) * amount));
    }

    private static List<Vector2> CleanParadePolygon(IEnumerable<Vector2> points)
    {
        var result = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            if (result.Count == 0 ||
                (result[result.Count - 1] - point).sqrMagnitude >
                ParadeFlagCoordinateEpsilon * ParadeFlagCoordinateEpsilon)
                result.Add(point);
        }
        if (result.Count > 1 &&
            (result[0] - result[result.Count - 1]).sqrMagnitude <=
            ParadeFlagCoordinateEpsilon * ParadeFlagCoordinateEpsilon)
            result.RemoveAt(result.Count - 1);

        bool changed;
        do
        {
            changed = false;
            for (int index = 0; index < result.Count && result.Count >= 3; index++)
            {
                Vector2 previous = result[(index + result.Count - 1) % result.Count];
                Vector2 current = result[index];
                Vector2 next = result[(index + 1) % result.Count];
                if (Math.Abs(ParadeCrossDouble(
                        current - previous, next - current)) >
                    ParadeFlagAreaEpsilon)
                    continue;
                result.RemoveAt(index);
                changed = true;
                break;
            }
        } while (changed);

        double area = ParadeSignedArea(result);
        if (result.Count < 3 || Math.Abs(area) <= ParadeFlagAreaEpsilon)
            return new List<Vector2>();
        if (area < 0f)
            result.Reverse();
        return result;
    }

    private static double ParadeSignedArea(IReadOnlyList<Vector2> polygon)
    {
        if (polygon.Count < 3)
            return 0d;
        Vector2 origin = polygon[0];
        double twiceArea = 0d;
        for (int index = 1; index + 1 < polygon.Count; index++)
            twiceArea += ParadeCrossDouble(
                polygon[index] - origin, polygon[index + 1] - origin);
        return twiceArea * 0.5d;
    }

    internal static bool ParadePolygonAreaIsResolved(IReadOnlyList<Vector2> polygon)
    {
        if (polygon.Count < 3)
            return false;
        double perimeter = 0d;
        for (int index = 0; index < polygon.Count; index++)
            perimeter += Vector2.Distance(polygon[index],
                polygon[(index + 1) % polygon.Count]);
        double twiceArea = Math.Abs(ParadeSignedArea(polygon)) * 2d;
        // Each welded endpoint can move by sqrt(2) * tolerance in projection.
        // Propagate that coordinate uncertainty around the measured perimeter;
        // faces below this bound are indistinguishable from a line at 10 um.
        double weldAreaUncertainty =
            2d * Math.Sqrt(2d) * ParadeFlagWeldTolerance * perimeter +
            4d * polygon.Count * ParadeFlagWeldTolerance * ParadeFlagWeldTolerance;
        return twiceArea > weldAreaUncertainty;
    }

    private static double ParadeCrossDouble(Vector2 a, Vector2 b)
    {
        return (double)a.x * b.y - (double)a.y * b.x;
    }

    private static void EnsureParadePolygonVisible(ParadeSourceTriangle candidate,
        List<Vector2> polygon, List<ParadeSourceTriangle> occluders,
        IReadOnlyList<int> nearbyOccluderIndices)
    {
        if (polygon.Count < 3)
            return;
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 point in polygon)
            centroid += point;
        centroid /= polygon.Count;
        foreach (int index in nearbyOccluderIndices)
        {
            ParadeSourceTriangle occluder = occluders[index];
            if (string.Equals(candidate.OwnerName, occluder.OwnerName,
                    StringComparison.Ordinal))
                continue;
            double blockingThreshold = ParadeBlockingHeightThreshold(
                candidate, occluder);
            if (candidate.FaceKey.Equals(occluder.FaceKey) ||
                occluder.MinY >= candidate.MaxY - blockingThreshold ||
                !occluder.ContainsProjectedPoint(centroid))
                continue;
            double candidateHeight = candidate.HeightAtPrecise(centroid);
            double occluderHeight = occluder.HeightAtPrecise(centroid);
            if (occluderHeight >= candidateHeight - blockingThreshold -
                ParadeFlagCoordinateEpsilon)
                continue;
            // The inexpensive point/height test can conservatively flag overlap
            // inside its tolerance band. Only suspicious fragments pay for the
            // exact resolved candidate/occluder blocking intersection below.
            List<Vector2> blocking = ParadeBlockingPolygon(candidate, occluder);
            bool centroidInBlocking = blocking.Count >= 3 &&
                Enumerable.Range(0, blocking.Count).All(edge =>
                    ParadeCrossDouble(blocking[(edge + 1) % blocking.Count] - blocking[edge],
                        centroid - blocking[edge]) >= -ParadeFlagAreaEpsilon);
            if (!centroidInBlocking)
                continue;
            List<List<Vector2>> recut = SubtractConvexPolygon(polygon, blocking).ToList();
            throw new InvalidOperationException("Farewell Flag lower-envelope clipping left a hidden " +
                candidate.OwnerName + " fragment beneath " + occluder.OwnerName +
                "; candidate face=" + candidate.FaceKey +
                "; occluder face=" + occluder.FaceKey +
                "; centroid=" + centroid.ToString("R") +
                "; polygon area=" + ParadeSignedArea(polygon).ToString("R") +
                "; candidate Y=" + candidateHeight.ToString("R") +
                "; occluder Y=" + occluderHeight.ToString("R") +
                "; hidden depth=" + (candidateHeight - occluderHeight).ToString("R") +
                "; blocking vertices=" + blocking.Count +
                "; blocking area=" + Math.Abs(ParadeSignedArea(blocking)).ToString("R") +
                "; recut areas=" + string.Join(",", recut.Select(piece =>
                    Math.Abs(ParadeSignedArea(piece)).ToString("R"))) + ".");
        }
    }

    private static bool CreateParadeFlagOverlay(Transform visualRoot, MeshFilter filter,
        Material material, Bounds planform, ParadeMeshBuilder builder,
        string materialsRoot, int assetIndex)
    {
        if (builder.TriangleCount == 0)
            return false;

        // Only the center remains on the visual root. Every wing, bay door, and
        // elevon overlay follows the exact owner that moves or detaches it.
        bool anchorToVisualRoot = string.Equals(filter.transform.name,
            "F117_Exterior_Mesh", StringComparison.Ordinal);
        Transform movingAnchor = filter.transform.parent != null
            ? filter.transform.parent
            : filter.transform;
        Transform overlayAnchor = anchorToVisualRoot ? visualRoot : movingAnchor;
        List<Vector3> rootVertices = builder.RootVertices;
        List<Vector3> rootNormals = builder.RootNormals;
        var vertices = new List<Vector3>(rootVertices.Count);
        var normals = new List<Vector3>(rootVertices.Count);
        var uvs = new List<Vector2>(rootVertices.Count);
        for (int index = 0; index < rootVertices.Count; index++)
        {
            Vector3 rootPoint = rootVertices[index];
            Vector3 offsetPoint = rootPoint + Vector3.down * ParadeFlagSurfaceOffset;
            vertices.Add(overlayAnchor.InverseTransformPoint(
                visualRoot.TransformPoint(offsetPoint)));
            normals.Add(overlayAnchor.InverseTransformDirection(
                visualRoot.TransformDirection(rootNormals[index])).normalized);
            uvs.Add(new Vector2(
                Mathf.InverseLerp(planform.max.z, planform.min.z, rootPoint.z),
                Mathf.InverseLerp(planform.min.x, planform.max.x, rootPoint.x)));
        }

        Mesh overlayMesh = new Mesh
        {
            name = ParadeFlagOverlayPrefix + filter.transform.name,
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        overlayMesh.SetVertices(vertices);
        overlayMesh.SetNormals(normals);
        overlayMesh.SetUVs(0, uvs);
        overlayMesh.SetTriangles(builder.Triangles, 0, true);
        string safeName = assetIndex.ToString("D2") + "_" + SafeName(filter.transform.name);
        AssetDatabase.CreateAsset(overlayMesh, materialsRoot + "/" + safeName + "_ParadeFlag.asset");

        GameObject overlay = new GameObject(ParadeFlagOverlayPrefix + filter.transform.name);
        overlay.transform.SetParent(overlayAnchor, false);
        MeshFilter overlayFilter = overlay.AddComponent<MeshFilter>();
        overlayFilter.sharedMesh = overlayMesh;
        MeshRenderer overlayRenderer = overlay.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = material;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = true;
        overlayRenderer.enabled = false;
        return true;
    }

    private readonly struct ParadePointKey : IEquatable<ParadePointKey>,
        IComparable<ParadePointKey>
    {
        internal readonly long X;
        internal readonly long Y;
        internal readonly long Z;

        private ParadePointKey(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal static ParadePointKey From(Vector3 point)
        {
            return new ParadePointKey(
                ParadeQuantize(point.x),
                ParadeQuantize(point.y),
                ParadeQuantize(point.z));
        }

        internal Vector3 ToVector3()
        {
            return new Vector3(
                (float)(X * ParadeFlagWeldTolerance),
                (float)(Y * ParadeFlagWeldTolerance),
                (float)(Z * ParadeFlagWeldTolerance));
        }

        public int CompareTo(ParadePointKey other)
        {
            int comparison = X.CompareTo(other.X);
            if (comparison != 0)
                return comparison;
            comparison = Y.CompareTo(other.Y);
            return comparison != 0 ? comparison : Z.CompareTo(other.Z);
        }

        public bool Equals(ParadePointKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ParadePointKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return X + "," + Y + "," + Z;
        }
    }

    private readonly struct ParadeFaceKey : IEquatable<ParadeFaceKey>,
        IComparable<ParadeFaceKey>
    {
        internal readonly ParadePointKey A;
        internal readonly ParadePointKey B;
        internal readonly ParadePointKey C;

        internal ParadeFaceKey(ParadePointKey a, ParadePointKey b, ParadePointKey c)
        {
            var points = new[] { a, b, c };
            Array.Sort(points);
            A = points[0];
            B = points[1];
            C = points[2];
        }

        public int CompareTo(ParadeFaceKey other)
        {
            int comparison = A.CompareTo(other.A);
            if (comparison != 0)
                return comparison;
            comparison = B.CompareTo(other.B);
            return comparison != 0 ? comparison : C.CompareTo(other.C);
        }

        public bool Equals(ParadeFaceKey other)
        {
            return A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);
        }

        public override bool Equals(object obj)
        {
            return obj is ParadeFaceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = A.GetHashCode();
                hash = hash * 31 + B.GetHashCode();
                hash = hash * 31 + C.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return A + "|" + B + "|" + C;
        }
    }

    private readonly struct ParadeOwnedFaceKey : IEquatable<ParadeOwnedFaceKey>
    {
        private readonly string owner;
        private readonly ParadeFaceKey face;

        internal ParadeOwnedFaceKey(string owner, ParadeFaceKey face)
        {
            this.owner = owner;
            this.face = face;
        }

        public bool Equals(ParadeOwnedFaceKey other)
        {
            return string.Equals(owner, other.owner, StringComparison.Ordinal) &&
                face.Equals(other.face);
        }

        public override bool Equals(object obj)
        {
            return obj is ParadeOwnedFaceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (owner == null ? 0 : owner.GetHashCode()) * 397 ^ face.GetHashCode();
            }
        }

        public override string ToString()
        {
            return owner + ":" + face;
        }
    }

    private sealed class ParadeSourceTriangle
    {
        internal readonly string OwnerName;
        internal readonly Vector3 A;
        internal readonly Vector3 B;
        internal readonly Vector3 C;
        internal readonly ParadeFaceKey FaceKey;
        internal readonly Vector3 Normal;
        internal readonly bool HasProjection;
        private readonly List<Vector2> projection;
        internal readonly float MinX;
        internal readonly float MaxX;
        internal readonly float MinY;
        internal readonly float MaxY;
        internal readonly float MinZ;
        internal readonly float MaxZ;
        internal readonly double HeightGradientX;
        internal readonly double HeightGradientZ;

        internal ParadeSourceTriangle(string ownerName, Vector3 a, Vector3 b, Vector3 c)
        {
            OwnerName = ownerName;
            ParadePointKey keyA = ParadePointKey.From(a);
            ParadePointKey keyB = ParadePointKey.From(b);
            ParadePointKey keyC = ParadePointKey.From(c);
            A = keyA.ToVector3();
            B = keyB.ToVector3();
            C = keyC.ToVector3();
            FaceKey = new ParadeFaceKey(keyA, keyB, keyC);
            Vector3 cross = Vector3.Cross(B - A, C - A);
            Normal = cross.sqrMagnitude <= ParadeFlagAreaEpsilon * ParadeFlagAreaEpsilon
                ? Vector3.zero
                : cross.normalized;
            // A face is a valid lower-envelope height field only when its X/Z
            // projection is resolvable at the same 10 um precision used to weld it.
            // An absolute cross.y floor is mesh-scale dependent and admitted nearly
            // vertical slivers whose height-plane extrapolation reached kilometres.
            projection = CleanParadePolygon(new[]
            {
                new Vector2(A.x, A.z),
                new Vector2(B.x, B.z),
                new Vector2(C.x, C.z)
            });
            HasProjection = ParadePolygonAreaIsResolved(projection);
            double abx = B.x - A.x;
            double aby = B.y - A.y;
            double abz = B.z - A.z;
            double acx = C.x - A.x;
            double acy = C.y - A.y;
            double acz = C.z - A.z;
            double normalX = aby * acz - abz * acy;
            double normalY = abz * acx - abx * acz;
            double normalZ = abx * acy - aby * acx;
            HeightGradientX = HasProjection ? -normalX / normalY : 0d;
            HeightGradientZ = HasProjection ? -normalZ / normalY : 0d;
            MinX = Mathf.Min(A.x, Mathf.Min(B.x, C.x));
            MaxX = Mathf.Max(A.x, Mathf.Max(B.x, C.x));
            MinY = Mathf.Min(A.y, Mathf.Min(B.y, C.y));
            MaxY = Mathf.Max(A.y, Mathf.Max(B.y, C.y));
            MinZ = Mathf.Min(A.z, Mathf.Min(B.z, C.z));
            MaxZ = Mathf.Max(A.z, Mathf.Max(B.z, C.z));
        }

        internal List<Vector2> ProjectedTriangle()
        {
            // The welded source triangle is immutable. IntersectConvexPolygons
            // cleans into new lists before clipping, so sharing this cached source
            // projection cannot mutate it.
            return projection;
        }

        internal float HeightAt(Vector2 point)
        {
            return (float)HeightAtPrecise(point);
        }

        internal double HeightAtPrecise(Vector2 point)
        {
            return A.y + HeightGradientX * (point.x - A.x) +
                HeightGradientZ * (point.y - A.z);
        }

        internal bool ProjectionOverlaps(ParadeSourceTriangle other)
        {
            return MaxX >= other.MinX - ParadeFlagCoordinateEpsilon &&
                other.MaxX >= MinX - ParadeFlagCoordinateEpsilon &&
                MaxZ >= other.MinZ - ParadeFlagCoordinateEpsilon &&
                other.MaxZ >= MinZ - ParadeFlagCoordinateEpsilon;
        }

        internal bool ContainsProjectedPoint(Vector2 point)
        {
            for (int edge = 0; edge < projection.Count; edge++)
                if (ParadeCrossDouble(projection[(edge + 1) % projection.Count] - projection[edge],
                        point - projection[edge]) < -ParadeFlagAreaEpsilon)
                    return false;
            return projection.Count == 3;
        }
    }

    private readonly struct ParadeGridKey : IEquatable<ParadeGridKey>
    {
        internal readonly int X;
        internal readonly int Z;

        internal ParadeGridKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(ParadeGridKey other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ParadeGridKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return X * 397 ^ Z;
            }
        }
    }

    private sealed class ParadeSpatialIndex
    {
        private readonly Dictionary<ParadeGridKey, List<int>> cells =
            new Dictionary<ParadeGridKey, List<int>>();

        internal ParadeSpatialIndex(IReadOnlyList<ParadeSourceTriangle> triangles)
        {
            for (int index = 0; index < triangles.Count; index++)
            {
                ParadeSourceTriangle triangle = triangles[index];
                int minX = ParadeCell(triangle.MinX);
                int maxX = ParadeCell(triangle.MaxX);
                int minZ = ParadeCell(triangle.MinZ);
                int maxZ = ParadeCell(triangle.MaxZ);
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var key = new ParadeGridKey(x, z);
                    if (!cells.TryGetValue(key, out List<int> values))
                    {
                        values = new List<int>();
                        cells.Add(key, values);
                    }
                    values.Add(index);
                }
            }
        }

        internal IEnumerable<int> Query(ParadeSourceTriangle triangle)
        {
            var matches = new HashSet<int>();
            for (int x = ParadeCell(triangle.MinX); x <= ParadeCell(triangle.MaxX); x++)
            for (int z = ParadeCell(triangle.MinZ); z <= ParadeCell(triangle.MaxZ); z++)
                if (cells.TryGetValue(new ParadeGridKey(x, z), out List<int> values))
                    foreach (int value in values)
                        matches.Add(value);
            return matches.OrderBy(value => value);
        }

        internal IEnumerable<int> Query(Vector2 point)
        {
            return cells.TryGetValue(new ParadeGridKey(
                    ParadeCell(point.x), ParadeCell(point.y)), out List<int> values)
                ? values
                : Enumerable.Empty<int>();
        }
    }

    private sealed class ParadeMeshBuilder
    {
        private readonly string ownerName;
        private readonly Dictionary<ParadePointKey, int> vertexIndices =
            new Dictionary<ParadePointKey, int>();
        private readonly List<ParadePointKey> vertexKeys = new List<ParadePointKey>();
        private readonly List<Vector3> normalSums = new List<Vector3>();
        private readonly Dictionary<ParadeFaceKey, ParadeFaceKey> faces =
            new Dictionary<ParadeFaceKey, ParadeFaceKey>();
        internal readonly List<int> Triangles = new List<int>();

        internal ParadeMeshBuilder(string ownerName)
        {
            this.ownerName = ownerName;
        }

        internal int TriangleCount => Triangles.Count / 3;

        internal List<Vector3> RootVertices =>
            vertexKeys.Select(key => key.ToVector3()).ToList();

        internal List<Vector3> RootNormals =>
            normalSums.Select(normal => normal.sqrMagnitude <=
                    ParadeFlagAreaEpsilon * ParadeFlagAreaEpsilon
                ? Vector3.down
                : ParadeNormalize(normal, Vector3.down)).ToList();

        internal void AddPolygon(ParadeSourceTriangle source, List<Vector2> polygon,
            IReadOnlyList<ParadeSourceTriangle> occluders,
            ParadeSpatialIndex spatialIndex)
        {
            polygon = CleanParadePolygon(polygon);
            if (polygon.Count < 3)
                return;
            int first = 0;
            for (int index = 1; index < polygon.Count; index++)
                if (ParadeProjectedPoint(source, polygon[index])
                    .CompareTo(ParadeProjectedPoint(source, polygon[first])) < 0)
                    first = index;
            if (first != 0)
                polygon = polygon.Skip(first).Concat(polygon.Take(first)).ToList();

            for (int index = 1; index + 1 < polygon.Count; index++)
            {
                ParadePointKey a = ParadeProjectedPoint(source, polygon[0]);
                ParadePointKey b = ParadeProjectedPoint(source, polygon[index]);
                ParadePointKey c = ParadeProjectedPoint(source, polygon[index + 1]);
                Vector3 firstPoint = a.ToVector3();
                Vector3 secondPoint = b.ToVector3();
                Vector3 thirdPoint = c.ToVector3();
                Vector2 projectedAb = new Vector2(secondPoint.x - firstPoint.x,
                    secondPoint.z - firstPoint.z);
                Vector2 projectedAc = new Vector2(thirdPoint.x - firstPoint.x,
                    thirdPoint.z - firstPoint.z);
                double projectedTwiceArea = Math.Abs(
                    ParadeCrossDouble(projectedAb, projectedAc));
                double projectedWeldUncertainty =
                    2d * Math.Sqrt(2d) * ParadeFlagWeldTolerance *
                    (projectedAb.magnitude + projectedAc.magnitude) +
                    4d * ParadeFlagWeldTolerance * ParadeFlagWeldTolerance;
                // Adjacent source faces can independently emit a shared-edge sliver.
                // After the 10 um weld, reject it only when its projected area is
                // indistinguishable from that edge within the quantified weld error.
                if (projectedTwiceArea <= projectedWeldUncertainty)
                    continue;
                Vector3 cross = Vector3.Cross(secondPoint - firstPoint,
                    thirdPoint - firstPoint);
                if (cross.sqrMagnitude <=
                    ParadeFlagAreaEpsilon * ParadeFlagAreaEpsilon)
                    continue;
                // Unity returns Vector3.zero when Normalize sees a vector shorter
                // than 1e-5.  Clipped overlay facets are legitimately much smaller,
                // so use the exact cross-Y sign instead of normalization for winding.
                if (cross.y > 0f)
                {
                    ParadePointKey swap = b;
                    b = c;
                    c = swap;
                    secondPoint = b.ToVector3();
                    thirdPoint = c.ToVector3();
                    cross = Vector3.Cross(secondPoint - firstPoint,
                        thirdPoint - firstPoint);
                }
                if (!ParadeOutputFaceIsValid(source, firstPoint, secondPoint,
                        thirdPoint, occluders, spatialIndex))
                    continue;

                var face = new ParadeFaceKey(a, b, c);
                if (faces.TryGetValue(face, out ParadeFaceKey firstSource))
                {
                    // Polygon subtraction may partition one source face into pieces
                    // that become the same triangle after the intentional 10 um weld.
                    // Canonicalize that single-source result, but continue to reject
                    // coincident output produced by different authored source faces.
                    if (firstSource.Equals(source.FaceKey))
                        continue;
                    throw new InvalidOperationException("Farewell Flag generated duplicate face on " +
                        ownerName + ": " + face + "; first source=" + firstSource +
                        "; duplicate source=" + source.FaceKey + ".");
                }
                faces.Add(face, source.FaceKey);
                Vector3 weightedNormal = cross;
                int ia = AddVertex(a, weightedNormal);
                int ib = AddVertex(b, weightedNormal);
                int ic = AddVertex(c, weightedNormal);
                Triangles.Add(ia);
                Triangles.Add(ib);
                Triangles.Add(ic);
            }
        }

        private int AddVertex(ParadePointKey key, Vector3 normal)
        {
            if (!vertexIndices.TryGetValue(key, out int index))
            {
                index = vertexKeys.Count;
                vertexIndices.Add(key, index);
                vertexKeys.Add(key);
                normalSums.Add(Vector3.zero);
            }
            normalSums[index] += normal;
            return index;
        }
    }

    private static bool ParadeOutputFaceIsValid(ParadeSourceTriangle source,
        Vector3 a, Vector3 b, Vector3 c,
        IReadOnlyList<ParadeSourceTriangle> occluders,
        ParadeSpatialIndex spatialIndex)
    {
        const float sourceTolerance = 0.00003f;
        Vector3 centroid = (a + b + c) / 3f;
        foreach (Vector3 point in new[] { a, b, c, centroid })
            if (ParadePointTriangleDistance(point, source.A, source.B, source.C) >
                sourceTolerance)
                return false;

        Vector3[] visibilitySamples =
        {
            centroid,
            a * 0.6f + b * 0.2f + c * 0.2f,
            a * 0.2f + b * 0.6f + c * 0.2f,
            a * 0.2f + b * 0.2f + c * 0.6f
        };
        foreach (Vector3 point in visibilitySamples)
        {
            Vector2 projected = new Vector2(point.x, point.z);
            foreach (int index in spatialIndex.Query(projected))
            {
                ParadeSourceTriangle occluder = occluders[index];
                if (string.Equals(source.OwnerName, occluder.OwnerName,
                        StringComparison.Ordinal))
                    continue;
                if (point.x < occluder.MinX || point.x > occluder.MaxX ||
                    point.z < occluder.MinZ || point.z > occluder.MaxZ ||
                    !occluder.ContainsProjectedPoint(projected))
                    continue;
                if (occluder.HeightAtPrecise(projected) <
                    point.y - ParadeFlagVisibilityClearance -
                    ParadeFlagCoordinateEpsilon)
                    return false;
            }
        }
        return true;
    }

    private static double ParadePointTriangleDistance(Vector3 point,
        Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;
        double d1 = ParadeDot(ab, ap);
        double d2 = ParadeDot(ac, ap);
        if (d1 <= 0d && d2 <= 0d)
            return ParadeMagnitude(ap);

        Vector3 bp = point - b;
        double d3 = ParadeDot(ab, bp);
        double d4 = ParadeDot(ac, bp);
        if (d3 >= 0d && d4 <= d3)
            return ParadeMagnitude(bp);

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0d && d1 >= 0d && d3 <= 0d)
        {
            double denominator = d1 - d3;
            return denominator == 0d ? ParadeMagnitude(ap) :
                ParadeMagnitude(point - (a + ab * (float)(d1 / denominator)));
        }

        Vector3 cp = point - c;
        double d5 = ParadeDot(ab, cp);
        double d6 = ParadeDot(ac, cp);
        if (d6 >= 0d && d5 <= d6)
            return ParadeMagnitude(cp);

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0d && d2 >= 0d && d6 <= 0d)
        {
            double denominator = d2 - d6;
            return denominator == 0d ? ParadeMagnitude(ap) :
                ParadeMagnitude(point - (a + ac * (float)(d2 / denominator)));
        }

        double va = d3 * d6 - d5 * d4;
        if (va <= 0d && d4 - d3 >= 0d && d5 - d6 >= 0d)
        {
            Vector3 bc = c - b;
            double numerator = d4 - d3;
            double denominator = numerator + d5 - d6;
            return denominator == 0d ? ParadeMagnitude(bp) :
                ParadeMagnitude(point - (b + bc * (float)(numerator / denominator)));
        }

        Vector3 cross = Vector3.Cross(ab, ac);
        double crossMagnitude = ParadeMagnitude(cross);
        return crossMagnitude == 0d
            ? Math.Min(ParadePointSegmentDistance(point, a, b),
                Math.Min(ParadePointSegmentDistance(point, b, c),
                    ParadePointSegmentDistance(point, c, a)))
            : Math.Abs(ParadeDot(ap, cross)) / crossMagnitude;
    }

    private static double ParadePointSegmentDistance(Vector3 point,
        Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        double lengthSquared = ParadeDot(edge, edge);
        if (lengthSquared == 0d)
            return ParadeMagnitude(point - a);
        double amount = Math.Max(0d, Math.Min(1d,
            ParadeDot(point - a, edge) / lengthSquared));
        return ParadeMagnitude(point - (a + edge * (float)amount));
    }

    private static double ParadeDot(Vector3 a, Vector3 b)
    {
        return (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
    }

    private static double ParadeMagnitude(Vector3 value)
    {
        return Math.Sqrt(ParadeDot(value, value));
    }

    private static ParadePointKey ParadeProjectedPoint(ParadeSourceTriangle source,
        Vector2 point)
    {
        return ParadePointKey.From(new Vector3(point.x, source.HeightAt(point), point.y));
    }

    private static Vector3 ParadeNormalize(Vector3 value, Vector3 fallback)
    {
        double magnitude = Math.Sqrt((double)value.x * value.x +
            (double)value.y * value.y + (double)value.z * value.z);
        return magnitude == 0d
            ? fallback
            : new Vector3((float)(value.x / magnitude),
                (float)(value.y / magnitude), (float)(value.z / magnitude));
    }

    private static long ParadeQuantize(float value)
    {
        return (long)Math.Round(value / ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
    }

    private static int ParadeCell(float value)
    {
        return Mathf.FloorToInt(value / ParadeFlagSpatialCell);
    }

    private static void ApplyProductionTextures(Material target, string materialName, string materialsRoot)
    {
        if (!TextureStems.TryGetValue(materialName, out string stem))
            return;
        bool damageSkin = UsesAircraftSkin(materialName);

        string albedoName = materialName.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0
            ? stem : stem + "_albedo";
        Texture2D albedo = LoadTexture(albedoName);
        Texture2D normal = LoadTexture(stem + "_normal") ?? LoadTexture(stem + "_norm");
        Texture2D mask = LoadTexture(stem + "_mask");
        Texture2D matteFinish = damageSkin ? LoadTexture(stem + "_ms") : null;
        Texture2D occlusion = LoadTexture(stem + "_occlusion");

        string emissionStem = stem;
        if (materialName.EndsWith("_GREEN", StringComparison.OrdinalIgnoreCase))
            emissionStem += "_green";
        else if (materialName.EndsWith("_WHITE", StringComparison.OrdinalIgnoreCase))
            emissionStem += "_white";
        Texture2D emission = LoadTexture(emissionStem + "_emissive") ?? LoadTexture(emissionStem + "_emis");

        if (albedo != null)
        {
            Texture2D damageAlbedo = damageSkin
                ? CreateDamageAlbedo(albedo, materialName, materialsRoot)
                : null;
            albedo = LoadTexture(albedoName);
            if (target.HasProperty("_BaseMap"))
                target.SetTexture("_BaseMap", albedo);
            // The extracted editor shader is a deliberately minimal stand-in. It previews
            // _MainTex, while Nuclear Option's runtime URP shader consumes _BaseMap.
            // Bind both so the material is correct in the builder and after runtime remap.
            if (target.HasProperty("_MainTex"))
                target.SetTexture("_MainTex", albedo);
            if (damageSkin)
            {
                SetSavedTexture(target, "_Basecolor", albedo);
                SetSavedTexture(target, "_BasecolorDmg", damageAlbedo);
            }
            target.SetColor("_BaseColor", Color.white);
            target.SetColor("_Color", Color.white);
        }
        if (normal != null)
        {
            if (target.HasProperty("_BumpMap"))
            {
                target.SetTexture("_BumpMap", normal);
                target.SetFloat("_BumpScale", 1f);
                EnableLocalKeyword(target, "_NORMALMAP");
            }
            if (damageSkin)
            {
                SetSavedTexture(target, "_Normal", normal);
                SetSavedTexture(target, "_NormalDmg", normal);
            }
        }
        if (mask != null)
        {
            bool tireRubber = string.Equals(materialName, "F117_Tires", StringComparison.OrdinalIgnoreCase);
            bool nonMetallicCockpit = materialName.StartsWith("F117_int_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(materialName, "INT_CockpitFrame", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(materialName, "LIGHTS", StringComparison.OrdinalIgnoreCase);
            if (tireRubber)
            {
                if (target.HasProperty("_MetallicGlossMap"))
                    target.SetTexture("_MetallicGlossMap", null);
                target.DisableKeyword("_METALLICSPECGLOSSMAP");
                if (target.HasProperty("_Metallic"))
                    target.SetFloat("_Metallic", 0f);
                if (target.HasProperty("_Smoothness"))
                    target.SetFloat("_Smoothness", 0.12f);
                if (target.HasProperty("_EnvironmentReflections"))
                    target.SetFloat("_EnvironmentReflections", 0f);
            }
            else if (target.HasProperty("_MetallicGlossMap"))
            {
                target.SetTexture("_MetallicGlossMap", mask);
                // Packed cockpit maps provide detail, but the authored FBX materials are
                // non-metallic with 0.5 roughness. Treating their mask like exterior chrome
                // made the tub and canopy frame react sharply to every lighting/reflection
                // change instead of retaining the source matte finish.
                target.SetFloat("_Metallic", nonMetallicCockpit ? 0f : 1f);
                target.SetFloat("_Smoothness", nonMetallicCockpit ? 0.5f : 1f);
                target.SetFloat("_SmoothnessTextureChannel", 0f);
                EnableLocalKeyword(target, "_METALLICSPECGLOSSMAP");
            }
            if (damageSkin)
            {
                if (matteFinish == null)
                    throw new InvalidOperationException(materialName + " has no native AircraftSkin matte MS texture.");
                SetSavedTexture(target, "_Metallic", matteFinish);
            }
        }
        if (occlusion != null)
        {
            if (target.HasProperty("_OcclusionMap"))
            {
                target.SetTexture("_OcclusionMap", occlusion);
                target.SetFloat("_OcclusionStrength", 1f);
                EnableLocalKeyword(target, "_OCCLUSIONMAP");
            }
            if (damageSkin)
                SetSavedTexture(target, "_AO", occlusion);
        }
        if (emission != null)
        {
            target.SetTexture("_EmissionMap", emission);
            target.SetColor("_EmissionColor", Color.white);
            EnableLocalKeyword(target, "_EMISSION");
            target.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (target.HasProperty("_HitPoints"))
            target.SetFloat("_HitPoints", 100f);
    }

    private static void ApplyProductionPreviewTextures(Material target, string materialName)
    {
        if (!TextureStems.TryGetValue(materialName, out string stem))
            return;
        Texture2D albedo = LoadTexture(stem + "_albedo");
        Texture2D normal = LoadTexture(stem + "_normal") ?? LoadTexture(stem + "_norm");
        Texture2D mask = LoadTexture(stem + "_mask");
        Texture2D occlusion = LoadTexture(stem + "_occlusion");
        if (albedo != null)
        {
            target.SetTexture("_BaseMap", albedo);
            target.SetTexture("_MainTex", albedo);
        }
        if (normal != null)
            target.SetTexture("_BumpMap", normal);
        if (mask != null)
            target.SetTexture("_MetallicGlossMap", mask);
        if (occlusion != null)
            target.SetTexture("_OcclusionMap", occlusion);
    }

    private static void SetSavedTexture(Material material, string propertyName, Texture texture)
    {
        SerializedObject serialized = new SerializedObject(material);
        SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
        if (textures == null || !textures.isArray)
            throw new InvalidOperationException(material.name + " has no serialized texture property table.");
        SerializedProperty entry = null;
        for (int index = 0; index < textures.arraySize; index++)
        {
            SerializedProperty candidate = textures.GetArrayElementAtIndex(index);
            SerializedProperty key = candidate.FindPropertyRelative("first");
            if (key != null && string.Equals(key.stringValue, propertyName, StringComparison.Ordinal))
            {
                entry = candidate;
                break;
            }
        }
        if (entry == null)
        {
            int index = textures.arraySize;
            textures.InsertArrayElementAtIndex(index);
            entry = textures.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("first").stringValue = propertyName;
            SerializedProperty scale = entry.FindPropertyRelative("second.m_Scale");
            SerializedProperty offset = entry.FindPropertyRelative("second.m_Offset");
            if (scale != null)
                scale.vector2Value = Vector2.one;
            if (offset != null)
                offset.vector2Value = Vector2.zero;
        }
        SerializedProperty value = entry.FindPropertyRelative("second.m_Texture");
        if (value == null)
            throw new InvalidOperationException(material.name + "." + propertyName + " has no serialized texture value.");
        value.objectReferenceValue = texture;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Texture2D CreateDamageAlbedo(Texture2D source, string materialName, string materialsRoot)
    {
        string outputPath = materialsRoot + "/F117_" + SafeName(materialName) + "_Damage.asset";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
        if (existing != null)
            return existing;

        int width = source.width;
        int height = source.height;
        Color32[] damaged = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float broad = Mathf.PerlinNoise(x * 0.0217f + 11.3f, y * 0.0191f + 7.9f);
            float fine = Mathf.PerlinNoise(x * 0.113f + 31.7f, y * 0.097f + 19.1f);
            float abrasion = Mathf.SmoothStep(0.38f, 0.82f, broad * 0.72f + fine * 0.28f);
            float multiplier = Mathf.Lerp(0.72f, 0.98f, abrasion);
            Color result = new Color(multiplier, multiplier, multiplier, 1f);
            damaged[y * width + x] = result;
        }

        // Native AircraftSkin multiplies clean color by this mask as HP falls;
        // it does not replace clean color with a second painted albedo. Alpha is
        // the native damage-cutout mask, independent of the clean artwork alpha.
        uint state = 2166136261u;
        foreach (char character in materialName)
            state = (state ^ character) * 16777619u;
        int impactCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(width * height) / 42f), 18, 48);
        int minDimension = Mathf.Min(width, height);
        for (int impact = 0; impact < impactCount; impact++)
        {
            state = state * 1664525u + 1013904223u;
            int centerX = (int)(state % (uint)width);
            state = state * 1664525u + 1013904223u;
            int centerY = (int)(state % (uint)height);
            state = state * 1664525u + 1013904223u;
            float radius = Mathf.Max(3f, minDimension * Mathf.Lerp(0.0045f, 0.011f,
                (state & 0xffffu) / 65535f));
            int extent = Mathf.CeilToInt(radius * 2.4f);
            for (int offsetY = -extent; offsetY <= extent; offsetY++)
            for (int offsetX = -extent; offsetX <= extent; offsetX++)
            {
                int x = centerX + offsetX;
                int y = centerY + offsetY;
                if (x < 0 || x >= width || y < 0 || y >= height)
                    continue;
                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY) / radius;
                if (distance > 2.4f)
                    continue;
                int pixel = y * width + x;
                Color baseColor = damaged[pixel];
                Color mark;
                float blend;
                if (distance < 0.42f)
                {
                    mark = new Color(0.02f, 0.02f, 0.02f, 0.48f);
                    blend = 0.98f;
                }
                else if (distance < 0.72f)
                {
                    mark = Color.white;
                    blend = 0.78f * (1f - (distance - 0.42f) / 0.30f);
                }
                else
                {
                    mark = new Color(0.28f, 0.28f, 0.28f, 1f);
                    blend = 0.62f * Mathf.Pow(1f - (distance - 0.72f) / 1.68f, 2f);
                }
                Color marked = Color.Lerp(baseColor, mark, Mathf.Clamp01(blend));
                damaged[pixel] = marked;
            }
        }

        Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = "F117_" + SafeName(materialName) + "_Damage",
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel
        };
        output.SetPixels32(damaged);
        output.Apply(true, true);
        AssetDatabase.CreateAsset(output, outputPath);

        return output;
    }

    internal static string CanonicalMaterialName(string materialName)
    {
        if (string.IsNullOrEmpty(materialName) || TextureStems.ContainsKey(materialName))
            return materialName;

        // AssetDatabase.CreateAsset adopts the deterministic NN_<source> filename as
        // the material object's name. Strip that build-order prefix only when the
        // remainder is an exact production material. This keeps the underside and
        // damage whitelists strict while allowing them to survive a real Unity build.
        if (materialName.Length > 3 && char.IsDigit(materialName[0]) &&
            char.IsDigit(materialName[1]) && materialName[2] == '_')
        {
            string unnumbered = materialName.Substring(3);
            if (TextureStems.ContainsKey(unnumbered))
                return unnumbered;
            materialName = unnumbered;
        }

        // Blender appends .001, .002, ... when distinct source slots share a name.
        // Unity's generated asset name uses underscores, so accept that spelling too.
        // Only remove a suffix when doing so resolves to a known production material;
        // legitimate authored names that happen to end in digits remain untouched.
        string candidate = materialName;
        while (candidate.Length > 4)
        {
            int suffix = candidate.Length - 4;
            char separator = candidate[suffix];
            if ((separator != '.' && separator != '_') ||
                !char.IsDigit(candidate[suffix + 1]) ||
                !char.IsDigit(candidate[suffix + 2]) ||
                !char.IsDigit(candidate[suffix + 3]))
                break;
            candidate = candidate.Substring(0, suffix);
            if (TextureStems.ContainsKey(candidate))
                return candidate;
        }
        return materialName;
    }

    private static Texture2D LoadTexture(string stem)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesRoot + stem + ".png");
    }

    private static void EnableLocalKeyword(Material material, string keywordName)
    {
        // The project ships an AssetRipper placeholder for URP/Lit, so its editor-side
        // keyword space is empty. Preserve the intended runtime keyword by name; the
        // real game shader resolves it when Blueprinter loads the bundle.
        material.EnableKeyword(keywordName);
    }

    private static void ConfigureSurface(Material target, string materialName)
    {
        bool structure = materialName.IndexOf("AircraftStructure",
            StringComparison.OrdinalIgnoreCase) >= 0;
        bool glass = materialName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            materialName.Equals("HUD", StringComparison.OrdinalIgnoreCase);
        bool decal = materialName.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0;
        bool exteriorDecal = string.Equals(materialName, "F117A_external_decals_new",
            StringComparison.OrdinalIgnoreCase);
        bool cutout = materialName.IndexOf("landing_gear_knob", StringComparison.OrdinalIgnoreCase) >= 0;

        if (structure)
        {
            // Each real root interface has two coincident, oppositely wound owner faces.
            // Back-face culling renders the correct mate from either detached side and
            // prevents intact co-planar faces from double-rendering or bleeding through.
            if (target.HasProperty("_Cull"))
                target.SetFloat("_Cull", (float)CullMode.Back);
            target.SetFloat("_Surface", 0f);
            target.SetFloat("_AlphaClip", 0f);
            target.SetFloat("_SrcBlend", (float)BlendMode.One);
            target.SetFloat("_DstBlend", (float)BlendMode.Zero);
            target.SetFloat("_ZWrite", 1f);
            target.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            target.DisableKeyword("_ALPHATEST_ON");
            target.SetOverrideTag("RenderType", "Opaque");
            target.renderQueue = -1;
            return;
        }

        // The source model uses a single detailed shell instead of separate interior and
        // exterior meshes. Render both faces so looking through the canopy cannot reveal
        // a hollow, transparent aircraft.
        if (target.HasProperty("_Cull"))
            target.SetFloat("_Cull", (float)CullMode.Off);

        if (exteriorDecal)
        {
            // These are opaque painted insignia and stencil cards with a transparent
            // atlas background. A blended, non-depth-writing pass is sorted only at
            // renderer granularity; after the airframe was divided into real damage
            // renderers it fought the supporting wing/fuselage depth and exposed the
            // cards as clipped, reflective rectangles. Keep the authored RGBA atlas,
            // but render its visible texels as matte alpha-tested paint.
            target.SetColor("_BaseColor", Color.white);
            target.SetColor("_Color", Color.white);
            if (target.HasProperty("_MetallicGlossMap"))
                target.SetTexture("_MetallicGlossMap", null);
            target.SetFloat("_Metallic", 0f);
            target.SetFloat("_Smoothness", 0.25f);
            if (target.HasProperty("_EnvironmentReflections"))
                target.SetFloat("_EnvironmentReflections", 0f);
            target.SetFloat("_Surface", 0f);
            target.SetFloat("_Blend", 0f);
            target.SetFloat("_AlphaClip", 1f);
            target.SetFloat("_Cutoff", 0.1f);
            target.SetFloat("_SrcBlend", (float)BlendMode.One);
            target.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (target.HasProperty("_SrcBlendAlpha"))
                target.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (target.HasProperty("_DstBlendAlpha"))
                target.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
            target.SetFloat("_ZWrite", 1f);
            target.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            target.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            target.DisableKeyword("_ALPHAMODULATE_ON");
            target.DisableKeyword("_METALLICSPECGLOSSMAP");
            EnableLocalKeyword(target, "_ALPHATEST_ON");
            target.SetOverrideTag("RenderType", "TransparentCutout");
            target.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else if (glass || decal)
        {
            bool exteriorGlass = materialName.IndexOf("ext_glass", StringComparison.OrdinalIgnoreCase) >= 0;
            Color tint = decal
                ? Color.white
                : exteriorGlass
                ? new Color(0.35f, 0.35f, 0.35f, 0.08f)
                : target.GetColor("_BaseColor");
            if (!exteriorGlass && !decal)
            {
                // The physical HUD combiner glass ("...hud_front") and the emissive
                // HUD display ("HUD") must read as nearly clear, not a bright
                // semi-opaque pane that blocks the forward view.
                bool hudGlass = materialName.IndexOf("HUD", StringComparison.OrdinalIgnoreCase) >= 0;
                tint.a = hudGlass ? 0.02f : 0.28f;
                if (hudGlass)
                {
                    tint.r = 0.45f;
                    tint.g = 0.45f;
                    tint.b = 0.45f;
                }
            }
            target.SetColor("_BaseColor", tint);
            target.SetColor("_Color", tint);
            if (exteriorGlass)
            {
                // The source canopy was authored as fully metallic glass, which
                // mirrors the blue sky and makes the whole canopy read as a blue
                // solid. Keep the windows optically neutral and nearly clear.
                target.SetTexture("_BaseMap", null);
                target.SetTexture("_MainTex", null);
                target.SetTexture("_BumpMap", null);
                target.SetTexture("_MetallicGlossMap", null);
                target.SetTexture("_OcclusionMap", null);
                target.SetTexture("_EmissionMap", null);
                target.DisableKeyword("_NORMALMAP");
                target.DisableKeyword("_METALLICSPECGLOSSMAP");
                target.DisableKeyword("_OCCLUSIONMAP");
                target.DisableKeyword("_EMISSION");
                target.SetFloat("_Metallic", 0f);
                target.SetFloat("_Smoothness", 0.35f);
                target.SetFloat("_EnvironmentReflections", 0f);
                target.SetColor("_EmissionColor", Color.black);
            }
            target.SetFloat("_Surface", 1f);
            target.SetFloat("_Blend", 0f);
            target.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            target.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            target.SetFloat("_ZWrite", 0f);
            target.SetOverrideTag("RenderType", "Transparent");
            EnableLocalKeyword(target, "_SURFACE_TYPE_TRANSPARENT");
            target.renderQueue = (int)RenderQueue.Transparent;
        }
        else if (cutout)
        {
            target.SetFloat("_AlphaClip", 1f);
            target.SetFloat("_Cutoff", 0.2f);
            target.SetOverrideTag("RenderType", "TransparentCutout");
            EnableLocalKeyword(target, "_ALPHATEST_ON");
            target.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }

    private static string SafeName(string value)
    {
        string result = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_').ToArray()).Trim('_');
        return string.IsNullOrEmpty(result) ? "Material" : result;
    }
    private static Component ConfigurePhysics(GameObject root, GameObject visual, Component aircraft, Rigidbody rigidbody,
        Material gearDustMaterial, TireAudioProfile tireAudioProfile)
    {
        rigidbody.mass = 1f;
        rigidbody.drag = 0f;
        rigidbody.angularDrag = 0.025f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.automaticCenterOfMass = true;
        rigidbody.ResetCenterOfMass();
        rigidbody.ResetInertiaTensor();

        // The model contains one continuous center body and two authored whole-wing
        // islands. Preserve every established mass/lift/drag total and let the existing
        // CG/static-margin solvers position their result on that real three-part graph.
        Component central = ConfigureAeroPart(root, DryCentralMass,
            CentralBodyLiftArea + NoseLiftArea + RearBodyLiftArea, 0.78f,
            0, aircraft, rigidbody, Locator(visual, "LOC_CenterOfMass"), null, 0f);
        AddDirectBoxCollider(central, new Vector3(0f, 0.08f, 0.4f),
            new Vector3(3.4f, 1.05f, 10.2f));
        AddDirectBoxCollider(central, new Vector3(0f, 0.02f, 5.5f),
            new Vector3(2.2f, 0.78f, 4.1f));
        AddDirectBoxCollider(central, new Vector3(0f, 0.16f, -4.55f),
            new Vector3(2.8f, 0.7f, 1.5f));

        // This is the area/mass-weighted centroid of the prior three wing nodes.
        // Consolidation therefore preserves each whole wing's first moment exactly.
        Component leftWing = AddPart(central.transform, "F117_Wing_Left",
            new Vector3(-3.4160013f, -0.02f, -2.087698f), 785f,
            MainWingLiftArea, 0.08f, 0, aircraft, rigidbody, central, 360000f);
        Component rightWing = AddPart(central.transform, "F117_Wing_Right",
            new Vector3(3.4160013f, -0.02f, -2.087698f), 785f,
            MainWingLiftArea, 0.08f, 0, aircraft, rigidbody, central, 360000f);

        BindFixedLiftAxis(central, visual.transform);
        BindFixedLiftAxis(leftWing, visual.transform);
        BindFixedLiftAxis(rightWing, visual.transform);
        Component[] fixedLiftParts = { central, leftWing, rightWing };

        BindAuthoredDamageSections(visual, central, leftWing, rightWing);

        Vector3[] elevonHingeAxes = CalculateMirroredElevonHingeAxes(visual);
        AddControlSurface(visual, "F117_Elevon_L_Inner", 40f, InnerElevonArea,
            -ElevonPitchTravel, -ElevonRollTravel, 0f, aircraft, rigidbody, leftWing, false,
            InnerElevonLeftNeutralCorrection, elevonHingeAxes[0]);
        AddControlSurface(visual, "F117_Elevon_R_Inner", 40f, InnerElevonArea,
            -ElevonPitchTravel, ElevonRollTravel, 0f, aircraft, rigidbody, rightWing, false,
            InnerElevonRightNeutralCorrection, elevonHingeAxes[1]);
        AddControlSurface(visual, "F117_Elevon_L_Outer", 40f, OuterElevonArea,
            -ElevonPitchTravel, -ElevonRollTravel, 0f, aircraft, rigidbody, leftWing, false, 0f,
            elevonHingeAxes[0]);
        AddControlSurface(visual, "F117_Elevon_R_Outer", 40f, OuterElevonArea,
            -ElevonPitchTravel, ElevonRollTravel, 0f, aircraft, rigidbody, rightWing, false, 0f,
            elevonHingeAxes[1]);
        SymmetrizeElevonForcePoints(root, visual);

        AddControlSurface(visual, "F117_Rudder_L", 25f, FullVerticalTailArea,
            0f, 0f, RudderYawTravel, aircraft, rigidbody, central, true, 0f, null);
        AddControlSurface(visual, "F117_Rudder_R", 25f, FullVerticalTailArea,
            0f, 0f, RudderYawTravel, aircraft, rigidbody, central, true, 0f, null);

        ConfigureEngines(central.transform, visual, aircraft, rigidbody, central);
        ConfigureLandingGear(visual, aircraft, central, gearDustMaterial, tireAudioProfile);
        float dryCenterOfMassZ = BalanceDryCenterOfMass(root, visual, central);
        BalancePitchStaticMargin(root, visual, dryCenterOfMassZ, fixedLiftParts);
        RequireDirectAeroPartColliders(root);
        return central;
    }

    private static float BalanceDryCenterOfMass(GameObject root, GameObject visual, Component centralPart)
    {
        Transform leftContact = Locator(visual, "LOC_Gear_Left_Contact");
        Transform rightContact = Locator(visual, "LOC_Gear_Right_Contact");
        float mainContactZ = (visual.transform.InverseTransformPoint(leftContact.position).z +
                              visual.transform.InverseTransformPoint(rightContact.position).z) * 0.5f;
        float targetCenterZ = mainContactZ + DryCenterOfMassAheadOfMainGear;

        float totalMass = 0f;
        float otherMomentZ = 0f;
        float centralMass = 0f;
        Transform centralMassPoint = null;
        foreach (Component part in root.GetComponentsInChildren<Component>(true)
                     .Where(component => component != null && component.GetType().Name == "AeroPart"))
        {
            SerializedObject data = new SerializedObject(part);
            float partMass = data.FindProperty("mass").floatValue;
            Transform massPoint = data.FindProperty("centerOfMass").objectReferenceValue as Transform;
            if (massPoint == null)
                throw new InvalidOperationException(part.name + " has no centerOfMass transform.");
            totalMass += partMass;
            if (part == centralPart)
            {
                centralMass = partMass;
                centralMassPoint = massPoint;
            }
            else
            {
                otherMomentZ += visual.transform.InverseTransformPoint(massPoint.position).z * partMass;
            }
        }

        if (centralMassPoint == null || centralMass <= 0f || totalMass <= centralMass)
            throw new InvalidOperationException("Cannot balance the F-117 dry center of mass.");

        float centralPointZ = (targetCenterZ * totalMass - otherMomentZ) / centralMass;
        Vector3 centralPointLocal = visual.transform.InverseTransformPoint(centralMassPoint.position);
        centralPointLocal.z = centralPointZ;
        centralMassPoint.position = visual.transform.TransformPoint(centralPointLocal);
        return targetCenterZ;
    }

    private static void BalancePitchStaticMargin(GameObject root, GameObject visual, float dryCenterOfMassZ,
        Component[] fixedLiftParts)
    {
        Component[] pitchControlParts = FindComponents(root, "ControlSurface")
            .Where(control =>
            {
                SerializedObject controlData = new SerializedObject(control);
                return Mathf.Abs(controlData.FindProperty("pitchRange").floatValue) > 0.001f;
            })
            .Select(control => control.GetComponent("AeroPart"))
            .Where(part => part != null)
            .ToArray();

        float fixedArea = 0f;
        float totalArea = 0f;
        float liftMomentZ = 0f;
        foreach (Component part in fixedLiftParts.Concat(pitchControlParts))
        {
            SerializedObject data = new SerializedObject(part);
            float wingArea = data.FindProperty("wingArea").floatValue;
            Transform liftNormal = data.FindProperty("liftNormal").objectReferenceValue as Transform;
            SerializedProperty centerProperty = data.FindProperty("centerOfLift");
            if (wingArea <= 0f || liftNormal == null || centerProperty == null)
                throw new InvalidOperationException(part.name + " cannot contribute to pitch-balance calculation.");
            Vector3 forcePoint = liftNormal.TransformPoint(centerProperty.vector3Value);
            float forcePointZ = visual.transform.InverseTransformPoint(forcePoint).z;
            totalArea += wingArea;
            liftMomentZ += forcePointZ * wingArea;
            if (fixedLiftParts.Contains(part))
                fixedArea += wingArea;
        }

        if (fixedArea <= 0f || totalArea <= fixedArea)
            throw new InvalidOperationException("Cannot balance the F-117 horizontal lift distribution.");

        float targetLiftCenterZ = dryCenterOfMassZ - TargetPitchStaticMargin;
        float forwardCorrection = (targetLiftCenterZ * totalArea - liftMomentZ) / fixedArea;
        foreach (Component part in fixedLiftParts)
        {
            SerializedObject data = new SerializedObject(part);
            Transform liftNormal = data.FindProperty("liftNormal").objectReferenceValue as Transform;
            SerializedProperty centerProperty = data.FindProperty("centerOfLift");
            Vector3 worldPoint = liftNormal.TransformPoint(centerProperty.vector3Value) +
                                 visual.transform.forward * forwardCorrection;
            centerProperty.vector3Value = liftNormal.InverseTransformPoint(worldPoint);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log("F-117 neutral horizontal lift centre balanced " +
                  TargetPitchStaticMargin.ToString("0.00") + " m behind dry CG; fixed lift shifted " +
                  forwardCorrection.ToString("0.000") + " m forward from its authored part origins.");
    }

    private static void RequireDirectAeroPartColliders(GameObject visual)
    {
        foreach (Component part in visual.GetComponentsInChildren<Component>(true))
        {
            if (part == null || part.GetType().Name != "AeroPart")
                continue;
            Collider collider = part.GetComponent<Collider>();
            if (collider == null || collider.gameObject != part.gameObject)
                throw new InvalidOperationException(part.name +
                    " has no directly owned collider for native projectile/explosion damage routing.");
        }
    }

    private static Bounds EncapsulateLocalBounds(Transform root, IEnumerable<Collider> colliders)
    {
        bool started = false;
        Bounds local = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Collider collider in colliders)
        {
            Bounds world = collider.bounds;
            Vector3[] corners =
            {
                new Vector3(world.min.x, world.min.y, world.min.z),
                new Vector3(world.min.x, world.min.y, world.max.z),
                new Vector3(world.min.x, world.max.y, world.min.z),
                new Vector3(world.min.x, world.max.y, world.max.z),
                new Vector3(world.max.x, world.min.y, world.min.z),
                new Vector3(world.max.x, world.min.y, world.max.z),
                new Vector3(world.max.x, world.max.y, world.min.z),
                new Vector3(world.max.x, world.max.y, world.max.z)
            };
            foreach (Vector3 corner in corners)
            {
                Vector3 point = root.InverseTransformPoint(corner);
                if (!started)
                {
                    local = new Bounds(point, Vector3.zero);
                    started = true;
                }
                else
                {
                    local.Encapsulate(point);
                }
            }
        }
        return started ? local : new Bounds(Vector3.zero, new Vector3(1f, 0.5f, 1f));
    }

    private static Component AddPart(Transform parent, string name, Vector3 localPosition, float mass,
        float wingArea, float dragArea, int airfoil, Component aircraft, Rigidbody rigidbody,
        Component connectedPart, float breakStrength)
    {
        GameObject gameObject = Child(parent, name, localPosition);
        return ConfigureAeroPart(gameObject, mass, wingArea, dragArea, airfoil, aircraft, rigidbody,
            gameObject.transform, connectedPart, breakStrength);
    }

    private static Component ConfigureAeroPart(GameObject gameObject, float mass, float wingArea,
        float dragArea, int airfoil, Component aircraft, Rigidbody rigidbody,
        Transform centerOfMass, Component connectedPart, float breakStrength)
    {
         // Native aircraft leave fixed liftNormal unset; AeroPart.Awake then binds
         // it to the part transform. Keep explicit lift pivots only for controls.
         Transform liftNormal = null;
        Component part = AddRuntimeComponent(gameObject, "AeroPart");
        SerializedObject data = new SerializedObject(part);
        // Aircraft AeroParts are not instant-kill components in the stock damage graph.
        // Engine failure and pilot/system damage already provide the normal kill paths.
        Set(data, "criticalPart", false);
        Set(data, "parentUnit", aircraft);
        Set(data, "mass", mass);
        Set(data, "rb", rigidbody);
        Set(data, "centerOfMass", centerOfMass);
        // UnitPart.Awake resets every part to 100 HP. Durability is therefore authored
        // through ArmorProperties, as on stock aircraft, rather than ineffective HP values.
        Set(data, "hitPoints", 100f);
        Set(data, "structuralThreshold", -25f);
        Set(data, "integrityThreshold", float.MinValue);
        SerializedProperty armor = Require(data, "armorProperties");
        // Exact common FastBomber1 structural profile. The F-117 uses 11 coherent
        // AeroParts versus the donor's 35, so this restores stock-scale survivability without
        // inventing extra hit points or making the aircraft unusually armored.
        Set(armor, "pierceArmor", 20f);
        Set(armor, "blastArmor", 60f);
        Set(armor, "fireArmor", 0f);
        Set(armor, "pierceTolerance", 5f);
        Set(armor, "blastTolerance", 6f);
        Set(armor, "fireTolerance", 1f);
        Set(armor, "overpressureLimit", 5f);
        Set(data, "hitSound", null);
        Size(data, "damageEffects", 0);
        Size(data, "disintegrationEffects", 0);
        Size(data, "disintegrateObjects", 0);
        SerializedProperty joints = Require(data, "joints");
        joints.arraySize = connectedPart == null ? 0 : 1;
        if (connectedPart != null)
        {
            SerializedProperty joint = joints.GetArrayElementAtIndex(0);
            Set(joint, "connectedPart", connectedPart);
            Set(joint, "tensor", null);
            Set(joint, "solverIterations", 8);
            Set(joint, "breakForce", breakStrength);
            Set(joint, "breakTorque", breakStrength);
            Set(joint, "anchor", null);
            Set(joint, "breakSound", null);
            Set(joint, "joint", null);
        }
        Set(data, "wingArea", wingArea);
        Set(data, "dragArea", dragArea);
         Set(data, "liftNormal", liftNormal);
        Set(data, "connectedAnchor", null);
        Set(data, "centerOfLift", Vector3.zero);
        Set(data, "buoyancy", 2f);
         // Use the actual lift-transform orientation rather than artificially rotating
         // the airflow toward the fuselage.
         Set(data, "airflowChanneling", 0f);
        Set(data, "airfoil", airfoil);
        data.ApplyModifiedPropertiesWithoutUndo();
        return part;
    }

    private static void BindFixedLiftAxis(Component part, Transform aircraftRoot)
    {
        Transform liftAxis = Child(part.transform, "F117_FixedLiftAxis", Vector3.zero).transform;
        // Working Shrike and FS-41 fixed lifting surfaces are aircraft-aligned.
        // The removed -9 degree constant actually pitched this axis 9 degrees up,
        // allowing the F-117 to generate lift while visibly flying nose-down.
        liftAxis.rotation = aircraftRoot.rotation;
        SerializedObject partData = new SerializedObject(part);
        Set(partData, "liftNormal", liftAxis);
        Set(partData, "centerOfLift", Vector3.zero);
        partData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static BoxCollider AddDirectBoxCollider(Component part, Vector3 center, Vector3 size)
    {
        BoxCollider collider = part.gameObject.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
        return collider;
    }

    private static MeshCollider AddDirectCompoundBoxCollider(Component part, string meshName,
        Vector3[] centers, Vector3[] sizes, float[] yaws)
    {
        if (centers == null || sizes == null || yaws == null ||
            centers.Length == 0 || centers.Length != sizes.Length || centers.Length != yaws.Length)
            throw new ArgumentException("Compound damage collider arrays must be non-empty and equal length.");

        var vertices = new List<Vector3>(centers.Length * 8);
        var triangles = new List<int>(centers.Length * 36);
        int[] cubeTriangles =
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
        };
        for (int boxIndex = 0; boxIndex < centers.Length; boxIndex++)
        {
            Vector3 half = sizes[boxIndex] * 0.5f;
            Quaternion rotation = Quaternion.Euler(0f, yaws[boxIndex], 0f);
            int first = vertices.Count;
            foreach (Vector3 corner in new[]
            {
                new Vector3(-half.x, -half.y, -half.z),
                new Vector3( half.x, -half.y, -half.z),
                new Vector3( half.x, -half.y,  half.z),
                new Vector3(-half.x, -half.y,  half.z),
                new Vector3(-half.x,  half.y, -half.z),
                new Vector3( half.x,  half.y, -half.z),
                new Vector3( half.x,  half.y,  half.z),
                new Vector3(-half.x,  half.y,  half.z)
            })
                vertices.Add(centers[boxIndex] + rotation * corner);
            triangles.AddRange(cubeTriangles.Select(index => first + index));
        }

        Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        Directory.CreateDirectory(DamageMeshRoot);
        AssetDatabase.CreateAsset(mesh, DamageMeshRoot + "/" + meshName + ".asset");

        MeshCollider collider = part.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
        collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
        return collider;
    }

    private static void BindAuthoredDamageSections(GameObject visual, Component central,
        Component leftWing, Component rightWing)
    {
        Renderer centralRenderer = RequireAuthoredDamageRenderer(visual,
            "F117_Exterior_Mesh", 15);
        Renderer leftRenderer = RequireAuthoredDamageRenderer(visual,
            "F117_Exterior_LeftWing_Mesh", 7);
        Renderer rightRenderer = RequireAuthoredDamageRenderer(visual,
            "F117_Exterior_RightWing_Mesh", 8);

        // The source model owns these three real geometric islands. Reparent only
        // the two wings, preserving their authored world pose, so detachment follows
        // the measured root contours instead of a generated plane cut.
        leftRenderer.transform.SetParent(leftWing.transform, true);
        rightRenderer.transform.SetParent(rightWing.transform, true);

        ConfigureDamageRenderers(central, new[] { centralRenderer });
        ConfigureDamageRenderers(leftWing, new[] { leftRenderer });
        ConfigureDamageRenderers(rightWing, new[] { rightRenderer });
        AddDirectPlanformDamageCollider(leftWing, new[] { leftRenderer });
        AddDirectPlanformDamageCollider(rightWing, new[] { rightRenderer });
    }

    private static void BindParadeFlagDamageRenderers(GameObject root, Component central)
    {
        if (root == null || central == null)
            throw new InvalidOperationException("Farewell Flag damage ownership requires the F-117 root and central AeroPart.");

        Renderer BaseRenderer(string name)
        {
            Renderer renderer = RequireUniqueNamedComponent<Renderer>(root, name);
            if (!renderer.sharedMaterials.Any(material => material != null &&
                    UsesAircraftSkin(material.name)))
                throw new InvalidOperationException(name +
                    " must retain an AircraftSkin exterior material for native damage blending.");
            return renderer;
        }

        Renderer OverlayRenderer(string sourceName)
        {
            Renderer renderer = RequireUniqueNamedComponent<Renderer>(root,
                ParadeFlagOverlayPrefix + sourceName);
            if (renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial == null ||
                !string.Equals(renderer.sharedMaterial.name, ParadeFlagMaterialName,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(renderer.name +
                    " must retain the single native Farewell Flag material.");
            return renderer;
        }

        Component Owner(string name)
        {
            Transform ownerTransform = RequireUniqueNamedComponent<Transform>(root, name);
            Component owner = ownerTransform.GetComponent("AeroPart");
            if (owner == null)
                throw new InvalidOperationException(name + " has no AeroPart damage owner.");
            return owner;
        }

        AppendDamageRenderers(central, new[]
        {
            BaseRenderer("F117_BayDoor_Left_Mesh"),
            BaseRenderer("F117_BayDoor_Right_Mesh"),
            BaseRenderer("F117_GearDoor_Nose_Mesh"),
            BaseRenderer("F117_GearDoor_Left_Outer_Mesh"),
            BaseRenderer("F117_GearDoor_Left_Inner_Mesh"),
            BaseRenderer("F117_GearDoor_Right_Outer_Mesh"),
            BaseRenderer("F117_GearDoor_Right_Inner_Mesh"),
            OverlayRenderer("F117_Exterior_Mesh"),
            OverlayRenderer("F117_BayDoor_Left_Mesh"),
            OverlayRenderer("F117_BayDoor_Right_Mesh"),
            OverlayRenderer("F117_GearDoor_Nose_Mesh"),
            OverlayRenderer("F117_GearDoor_Left_Outer_Mesh"),
            OverlayRenderer("F117_GearDoor_Left_Inner_Mesh"),
            OverlayRenderer("F117_GearDoor_Right_Outer_Mesh"),
            OverlayRenderer("F117_GearDoor_Right_Inner_Mesh")
        });
        AppendDamageRenderers(Owner("F117_Wing_Left"), new[]
        {
            OverlayRenderer("F117_Exterior_LeftWing_Mesh")
        });
        AppendDamageRenderers(Owner("F117_Wing_Right"), new[]
        {
            OverlayRenderer("F117_Exterior_RightWing_Mesh")
        });

        string[] controlOwners =
        {
            "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
            "F117_Elevon_R_Inner", "F117_Elevon_R_Outer"
        };
        foreach (string ownerName in controlOwners)
            AppendDamageRenderers(Owner(ownerName), new[]
            {
                OverlayRenderer(ownerName + "_Mesh")
            });
    }

    private static T RequireUniqueNamedComponent<T>(GameObject root, string name)
        where T : Component
    {
        T[] matches = root.GetComponentsInChildren<T>(true)
            .Where(component => component != null &&
                string.Equals(component.transform.name, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Expected exactly one " + typeof(T).Name +
                " named " + name + "; found " + matches.Length + ".");
        return matches[0];
    }

    private static void AppendDamageRenderers(Component part, IEnumerable<Renderer> additions)
    {
        if (part == null)
            throw new InvalidOperationException("Cannot append damage renderers to a missing UnitPart.");
        Renderer[] appended = additions.Where(renderer => renderer != null).Distinct().ToArray();
        foreach (Renderer renderer in appended)
            if (renderer.transform != part.transform && !renderer.transform.IsChildOf(part.transform))
                throw new InvalidOperationException(renderer.name + " is not owned by " + part.name + ".");

        SerializedObject data = new SerializedObject(part);
        SerializedProperty damageMaterial = Require(data, "damageMaterial");
        SerializedProperty rendererArray = Require(damageMaterial, "renderers");
        var combined = new List<Renderer>();
        for (int index = 0; index < rendererArray.arraySize; index++)
        {
            Renderer renderer = rendererArray.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
            if (renderer != null && !combined.Contains(renderer))
                combined.Add(renderer);
        }
        foreach (Renderer renderer in appended)
            if (!combined.Contains(renderer))
                combined.Add(renderer);

        rendererArray.arraySize = combined.Count;
        for (int index = 0; index < combined.Count; index++)
            rendererArray.GetArrayElementAtIndex(index).objectReferenceValue = combined[index];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Renderer RequireAuthoredDamageRenderer(GameObject visual, string objectName,
        int expectedStructuralTriangles)
    {
        Transform transform = FindDeep(visual.transform, objectName);
        MeshFilter filter = transform == null ? null : transform.GetComponent<MeshFilter>();
        Renderer renderer = transform == null ? null : transform.GetComponent<Renderer>();
        Mesh mesh = filter == null ? null : filter.sharedMesh;
        if (transform == null || filter == null || renderer == null || mesh == null)
            throw new InvalidOperationException(objectName +
                " is missing from the authored three-piece exterior.");

        Material[] materials = renderer.sharedMaterials;
        if (materials.Length != mesh.subMeshCount)
            throw new InvalidOperationException(objectName +
                " has a renderer/material count that does not match its authored submeshes.");
        int[] structureSlots = Enumerable.Range(0, materials.Length)
            .Where(index => materials[index] != null &&
                materials[index].name.IndexOf("AircraftStructure",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        if (structureSlots.Length != 1)
            throw new InvalidOperationException(objectName +
                " must contain exactly one authored aircraft-structure material slot.");
        Material structureMaterial = materials[structureSlots[0]];
        if (!structureMaterial.HasProperty("_Cull") ||
            !structureMaterial.HasProperty("_Surface") ||
            !structureMaterial.HasProperty("_ZWrite") ||
            Mathf.Abs(structureMaterial.GetFloat("_Cull") - (float)CullMode.Back) > 0.001f ||
            Mathf.Abs(structureMaterial.GetFloat("_Surface")) > 0.001f ||
            Mathf.Abs(structureMaterial.GetFloat("_ZWrite") - 1f) > 0.001f)
            throw new InvalidOperationException(objectName +
                " aircraft structure must be opaque, depth-writing and back-face culled.");
        int structureTriangles = mesh.GetTriangles(structureSlots[0]).Length / 3;
        if (structureTriangles != expectedStructuralTriangles)
            throw new InvalidOperationException(objectName + " has " + structureTriangles +
                " structural root triangles; expected " + expectedStructuralTriangles + ".");
        return renderer;
    }

    private static void AddDirectPlanformDamageCollider(Component part, IEnumerable<Renderer> renderers)
    {
        var points = new List<Vector2>();
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        foreach (Renderer renderer in renderers.Where(value => value != null))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;
            Matrix4x4 toPart = part.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            foreach (Vector3 sourceVertex in filter.sharedMesh.vertices)
            {
                Vector3 vertex = toPart.MultiplyPoint3x4(sourceVertex);
                points.Add(new Vector2(vertex.x, vertex.z));
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }
        }
        List<Vector2> hull = ConvexHull(points);
        if (hull.Count < 3 || float.IsNaN(minY) || float.IsInfinity(minY) ||
            float.IsNaN(maxY) || float.IsInfinity(maxY))
            throw new InvalidOperationException(part.name + " cannot produce a planform damage collider.");

        Vector2 center = hull.Aggregate(Vector2.zero, (sum, point) => sum + point) / hull.Count;
        for (int index = 0; index < hull.Count; index++)
            hull[index] = center + (hull[index] - center) * 0.985f;
        float middleY = (minY + maxY) * 0.5f;
        float halfHeight = Mathf.Max((maxY - minY) * 0.485f, 0.09f);
        var vertices = new List<Vector3>(hull.Count * 2);
        foreach (Vector2 point in hull)
            vertices.Add(new Vector3(point.x, middleY - halfHeight, point.y));
        foreach (Vector2 point in hull)
            vertices.Add(new Vector3(point.x, middleY + halfHeight, point.y));
        var triangles = new List<int>();
        for (int index = 1; index < hull.Count - 1; index++)
        {
            triangles.Add(0);
            triangles.Add(index + 1);
            triangles.Add(index);
            triangles.Add(hull.Count);
            triangles.Add(hull.Count + index);
            triangles.Add(hull.Count + index + 1);
        }
        for (int index = 0; index < hull.Count; index++)
        {
            int next = (index + 1) % hull.Count;
            triangles.Add(index);
            triangles.Add(next);
            triangles.Add(hull.Count + next);
            triangles.Add(index);
            triangles.Add(hull.Count + next);
            triangles.Add(hull.Count + index);
        }

        string meshName = part.name + "_DamageCollider";
        var mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        Directory.CreateDirectory(DamageMeshRoot);
        string assetPath = DamageMeshRoot + "/" + meshName + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(mesh, assetPath);

        MeshCollider collider = part.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.convex = true;
        collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
    }

    private static List<Vector2> ConvexHull(IEnumerable<Vector2> source)
    {
        List<Vector2> sorted = source
            .OrderBy(point => point.x)
            .ThenBy(point => point.y)
            .ToList();
        var unique = new List<Vector2>();
        foreach (Vector2 point in sorted)
            if (unique.Count == 0 || (point - unique[unique.Count - 1]).sqrMagnitude > 0.00000001f)
                unique.Add(point);
        if (unique.Count <= 3)
            return unique;

        float Cross(Vector2 origin, Vector2 first, Vector2 second)
        {
            Vector2 a = first - origin;
            Vector2 b = second - origin;
            return a.x * b.y - a.y * b.x;
        }

        var lower = new List<Vector2>();
        foreach (Vector2 point in unique)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], point) <= 0.000001f)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<Vector2>();
        for (int index = unique.Count - 1; index >= 0; index--)
        {
            Vector2 point = unique[index];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], point) <= 0.000001f)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        if (lower.Count > 64)
            lower = lower.Where((point, index) => index % Mathf.CeilToInt(lower.Count / 64f) == 0).ToList();
        return lower;
    }

    private static void ConfigureDamageRenderers(Component part, IEnumerable<Renderer> renderers)
    {
        Renderer[] owned = renderers.Where(renderer => renderer != null).Distinct().ToArray();
        if (owned.Length == 0)
            throw new InvalidOperationException(part.name + " has no owned damage renderers.");
        SerializedObject data = new SerializedObject(part);
        SerializedProperty damageMaterial = Require(data, "damageMaterial");
        Set(damageMaterial, "threshold", 50f);
        SerializedProperty rendererArray = Require(damageMaterial, "renderers");
        rendererArray.arraySize = owned.Length;
        for (int index = 0; index < owned.Length; index++)
            rendererArray.GetArrayElementAtIndex(index).objectReferenceValue = owned[index];
        Size(damageMaterial, "indices", 0);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3[] CalculateMirroredElevonHingeAxes(GameObject visual)
    {
        string[] leftNames = { "F117_Elevon_L_Inner", "F117_Elevon_L_Outer" };
        string[] rightNames = { "F117_Elevon_R_Inner", "F117_Elevon_R_Outer" };
        Vector3 sum = Vector3.zero;
        foreach (string name in leftNames)
        {
            Transform source = FindDeep(visual.transform, name);
            if (source == null)
                throw new InvalidOperationException("Missing production control surface " + name + ".");
            sum += visual.transform.InverseTransformDirection(source.right).normalized;
        }
        foreach (string name in rightNames)
        {
            Transform source = FindDeep(visual.transform, name);
            if (source == null)
                throw new InvalidOperationException("Missing production control surface " + name + ".");
            Vector3 axis = visual.transform.InverseTransformDirection(source.right).normalized;
            sum += new Vector3(axis.x, -axis.y, -axis.z);
        }
        Vector3 left = sum.normalized;
        if (left.sqrMagnitude < 0.99f)
            throw new InvalidOperationException("Cannot derive the F-117 canonical elevon hinge axis.");
        Vector3 right = new Vector3(left.x, -left.y, -left.z);
        return new[] { left, right };
    }

    private static void SymmetrizeElevonForcePoints(GameObject root, GameObject visual)
    {
        foreach (string section in new[] { "Inner", "Outer" })
        {
            Component left = FindDeep(root.transform, "F117_Elevon_L_" + section)?.GetComponent("AeroPart");
            Component right = FindDeep(root.transform, "F117_Elevon_R_" + section)?.GetComponent("AeroPart");
            if (left == null || right == null)
                throw new InvalidOperationException("Cannot symmetrize the F-117 " + section + " elevon force points.");

            SerializedObject leftData = new SerializedObject(left);
            SerializedObject rightData = new SerializedObject(right);
            Transform leftLift = leftData.FindProperty("liftNormal").objectReferenceValue as Transform;
            Transform rightLift = rightData.FindProperty("liftNormal").objectReferenceValue as Transform;
            Vector3 leftCenter = leftData.FindProperty("centerOfLift").vector3Value;
            Vector3 rightCenter = rightData.FindProperty("centerOfLift").vector3Value;
            Vector3 leftPoint = visual.transform.InverseTransformPoint(leftLift.TransformPoint(leftCenter));
            Vector3 rightPoint = visual.transform.InverseTransformPoint(rightLift.TransformPoint(rightCenter));
            float halfSpan = (Mathf.Abs(leftPoint.x) + Mathf.Abs(rightPoint.x)) * 0.5f;
            float height = (leftPoint.y + rightPoint.y) * 0.5f;
            float longitudinal = (leftPoint.z + rightPoint.z) * 0.5f;
            Vector3 leftTarget = visual.transform.TransformPoint(new Vector3(-halfSpan, height, longitudinal));
            Vector3 rightTarget = visual.transform.TransformPoint(new Vector3(halfSpan, height, longitudinal));
            leftData.FindProperty("centerOfLift").vector3Value = leftLift.InverseTransformPoint(leftTarget);
            rightData.FindProperty("centerOfLift").vector3Value = rightLift.InverseTransformPoint(rightTarget);
            leftData.ApplyModifiedPropertiesWithoutUndo();
            rightData.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AddControlSurface(GameObject visual, string name, float mass, float area,
        float pitch, float roll, float yaw, Component aircraft, Rigidbody rigidbody,
        Component connectedPart, bool vertical, float neutralCorrection, Vector3? hingeAxisAircraft)
    {
        Transform transform = FindDeep(visual.transform, name);
        if (transform == null)
            throw new InvalidOperationException("Missing production control surface " + name + ".");

        // UnitPart.Awake only discovers an attachment through its parent or grandparent.
        // Keep every detachable aerodynamic child directly under the root AeroPart so
        // complex-physics startup cannot treat a control surface as another root body.
        // Match the native Shrike/FS-41 graph: the transform parent and serialized
        // FixedJoint target must be the same physical part. This also makes Unity's
        // enableCollision=false joint suppress contact at the control-surface seam.
        transform.SetParent(connectedPart.transform, true);

        // ControlSurface rotates visibleMesh every physics frame. Keep that visual
        // pivot below the AeroPart so only rendered geometry is animated.
        Transform visualPivot = Child(transform, name + "_VisualPivot", Vector3.zero).transform;
        // The game always animates visibleMesh around its local X axis. The source
        // animation audit proves the elevons use local X but both canted rudders use
        // local Z. Rotate only the rudder visual pivot's resting basis so its animated
        // X maps exactly onto the model's Z hinge, then preserve the mesh world pose.
        if (vertical)
            visualPivot.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
        else if (hingeAxisAircraft.HasValue)
        {
            Vector3 desiredWorldAxis = visual.transform.TransformDirection(hingeAxisAircraft.Value).normalized;
            // Quaternion.FromToRotation intentionally snaps extremely small rotations to
            // identity. The imported outer-right hinge differs by only hundredths of a
            // degree, which is small enough to be snapped but large enough to leave a
            // speed-amplified asymmetric pitch moment. Construct the orthonormal frame
            // directly so the driven local-X axis is the canonical mirrored axis exactly.
            Vector3 preservedUp = Vector3.ProjectOnPlane(visualPivot.up, desiredWorldAxis).normalized;
            if (preservedUp.sqrMagnitude < 0.99f)
                preservedUp = Vector3.ProjectOnPlane(visual.transform.up, desiredWorldAxis).normalized;
            Vector3 forward = Vector3.Cross(desiredWorldAxis, preservedUp).normalized;
            preservedUp = Vector3.Cross(forward, desiredWorldAxis).normalized;
            visualPivot.rotation = Quaternion.LookRotation(forward, preservedUp);
        }
        Transform renderHost = visualPivot;
        if (!vertical && Mathf.Abs(neutralCorrection) > 0.001f)
            renderHost = Child(visualPivot, name + "_MeshCorrection", Vector3.zero).transform;
        Transform[] renderRoots = transform.Cast<Transform>()
            .Where(child => child != visualPivot && child.GetComponentInChildren<Renderer>(true) != null)
            .ToArray();
        foreach (Transform renderRoot in renderRoots)
            renderRoot.SetParent(renderHost, true);

        // The approved production mesh carries a small up-elevon bias on only the two
        // inner panels. Measurements from the matching Blender master show the exact
        // signed hinge corrections needed to match each already-correct outer panel.
        // Keep this cosmetic correction below the clean native servo pivot. Parenting
        // F117_LiftAxis under the corrected pivot made the 0.562-degree left/right
        // visual difference into a real aerodynamic bias and seeded an uncommanded
        // roll during rotation even with rollInput=0 and yawInput=0.
        if (!vertical && Mathf.Abs(neutralCorrection) > 0.001f)
            renderHost.localRotation = Quaternion.AngleAxis(neutralCorrection, Vector3.right);

        Renderer[] surfaceRenderers = visualPivot.GetComponentsInChildren<Renderer>(true);
        if (surfaceRenderers.Length == 0)
            throw new InvalidOperationException("Control surface " + name + " has no rendered geometry.");
        // Build the hitbox in the control surface's own hinge space. Using Renderer.bounds
        // here produced a world-axis AABB and then applied that already-expanded size in
        // the canted rudder frame a second time. The two tail boxes overlapped, violently
        // separated the sibling rigidbodies at spawn and pulled the rear body off with them.
        Bounds surfaceLocalBounds = CalculateRendererGeometryBounds(transform, surfaceRenderers);
        AddControlSurfaceCollider(transform, surfaceRenderers, surfaceLocalBounds, name);
        // AeroJob applies force at the rigidbody centre unless centerOfLift is non-zero.
        // Use the actual mesh centroid relative to the authored hinge. This gives every
        // elevon and rudder a physical aerodynamic moment arm instead of a magic tuning
        // torque, and remains correct if the approved mesh is rebuilt at the same pivots.
        // Visible servo stays on the imported hinge. Aero uses a separate aircraft-aligned
        // lift axis so Cross(vel, Right) is up (elevon) or sideways (rudder).
        // Make the aerodynamic axis a child of the native servo pivot. The control job
        // now moves the visible geometry and physical lift axis together at servoSpeed.
        Transform liftAxis = Child(visualPivot, "F117_LiftAxis", Vector3.zero).transform;
        Quaternion liftWorld = visual.transform.rotation;
        if (vertical)
            liftWorld *= Quaternion.FromToRotation(Vector3.right, Vector3.up);
        liftAxis.rotation = liftWorld;
        Vector3 surfaceWorldCenter = transform.TransformPoint(surfaceLocalBounds.center);
        Vector3 centerOfLift = Quaternion.Inverse(liftAxis.rotation) *
            (surfaceWorldCenter - liftAxis.position);

        Component part = ConfigureAeroPart(transform.gameObject, mass, area, 0.025f, 1, aircraft,
            rigidbody, transform, connectedPart, 120000f);
        ConfigureDamageRenderers(part, surfaceRenderers);
        Transform generatedLiftNormal = FindDeep(transform, name + "_LiftNormal");
        SerializedObject partData = new SerializedObject(part);
        // Match the native control-surface durability used by Shrike and FS-41.
        Set(partData, "hitPoints", 100f);
        Set(partData, "liftNormal", liftAxis);
        Set(partData, "centerOfLift", centerOfLift);
        partData.ApplyModifiedPropertiesWithoutUndo();
        if (generatedLiftNormal != null)
            UnityEngine.Object.DestroyImmediate(generatedLiftNormal.gameObject, true);

        Component control = AddRuntimeComponent(transform.gameObject, "ControlSurface");
        SerializedObject controlData = new SerializedObject(control);
        Set(controlData, "pitchRange", pitch);
        Set(controlData, "rollRange", roll);
        Set(controlData, "yawRange", yaw);
        Set(controlData, "brakeRange", 0f);
        Set(controlData, "attachedSurface", part);
        Set(controlData, "visibleMesh", visualPivot.gameObject);
        Set(controlData, "flap", false);
        Set(controlData, "servoSpeed", 55f);
        Set(controlData, "splitDrag", 0f);
        Set(controlData, "splitUpper", null);
        Set(controlData, "splitLower", null);
        Set(controlData, "maxSplit", 0f);
        Set(controlData, "yawSplitFactor", 0f);
        Set(controlData, "splitSound", null);
        Set(controlData, "splitVolumeMultiplier", 0f);
        Set(controlData, "maxVolumeSpeed", 315f);
        Set(controlData, "splitPitchMin", 0.5f);
        Set(controlData, "splitPitchMax", 2f);
        controlData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddControlSurfaceCollider(Transform root, Renderer[] renderers, Bounds geometryBounds, string name)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
            Mesh source = filter == null ? null : filter.sharedMesh;
            if (source == null)
                throw new InvalidOperationException("Control surface " + name + " has a renderer without a readable mesh.");

            int vertexOffset = vertices.Count;
            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            foreach (Vector3 vertex in source.vertices)
            {
                Vector3 local = toRoot.MultiplyPoint3x4(vertex);
                vertices.Add(geometryBounds.center +
                    (local - geometryBounds.center) * ControlSurfaceColliderInset);
            }

            int[] sourceTriangles = source.triangles;
            bool mirrored = toRoot.determinant < 0f;
            for (int index = 0; index < sourceTriangles.Length; index += 3)
            {
                int first = sourceTriangles[index] + vertexOffset;
                int second = sourceTriangles[index + 1] + vertexOffset;
                int third = sourceTriangles[index + 2] + vertexOffset;
                triangles.Add(first);
                triangles.Add(mirrored ? third : second);
                triangles.Add(mirrored ? second : third);
            }
        }

        if (vertices.Count == 0 || triangles.Count == 0)
            throw new InvalidOperationException("Control surface " + name + " produced an empty collision mesh.");
        if (vertices.Count > 255)
            throw new InvalidOperationException("Control surface " + name + " exceeds Unity's convex-collider vertex limit.");

        var collisionMesh = new Mesh
        {
            name = name + "_CollisionMesh",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        collisionMesh.SetVertices(vertices);
        collisionMesh.SetTriangles(triangles, 0, true);
        collisionMesh.RecalculateBounds();
        collisionMesh.RecalculateNormals();

        Directory.CreateDirectory(ControlColliderRoot);
        string assetPath = ControlColliderRoot + "/" + name + "_CollisionMesh.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            AssetDatabase.DeleteAsset(assetPath);
        AssetDatabase.CreateAsset(collisionMesh, assetPath);

        MeshCollider collider = root.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = collisionMesh;
        collider.convex = true;
        collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
    }

    internal static Bounds CalculateRendererGeometryBounds(Transform root, IEnumerable<Renderer> renderers)
    {
        bool started = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            void EncapsulatePoint(Vector3 rendererLocalPoint)
            {
                Vector3 localPoint = root.InverseTransformPoint(renderer.transform.TransformPoint(rendererLocalPoint));
                if (!started)
                {
                    result = new Bounds(localPoint, Vector3.zero);
                    started = true;
                }
                else
                {
                    result.Encapsulate(localPoint);
                }
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    EncapsulatePoint(vertex);
                continue;
            }

            if (renderer is SkinnedMeshRenderer skinned)
            {
                Bounds geometryBounds = skinned.localBounds;
                Vector3 min = geometryBounds.min;
                Vector3 max = geometryBounds.max;
                EncapsulatePoint(new Vector3(min.x, min.y, min.z));
                EncapsulatePoint(new Vector3(min.x, min.y, max.z));
                EncapsulatePoint(new Vector3(min.x, max.y, min.z));
                EncapsulatePoint(new Vector3(min.x, max.y, max.z));
                EncapsulatePoint(new Vector3(max.x, min.y, min.z));
                EncapsulatePoint(new Vector3(max.x, min.y, max.z));
                EncapsulatePoint(new Vector3(max.x, max.y, min.z));
                EncapsulatePoint(new Vector3(max.x, max.y, max.z));
                continue;
            }

            throw new InvalidOperationException(
                "Cannot author a local collider for renderer " + renderer.name + " without mesh geometry.");
        }

        if (!started)
            throw new InvalidOperationException("Cannot author a control-surface collider without rendered geometry.");
        return result;
    }
    private static void ConfigureEngines(Transform physicsRoot, GameObject visual, Component aircraft,
        Rigidbody rigidbody, Component connectedPart)
    {
        ConfigureEngine("Left", Locator(visual, "LOC_Engine_Left"));
        ConfigureEngine("Right", Locator(visual, "LOC_Engine_Right"));

        void ConfigureEngine(string side, Transform locator)
        {
            Vector3 localPosition = physicsRoot.InverseTransformPoint(locator.position);
            Component part = AddPart(physicsRoot, "F117_Engine_" + side, localPosition,
                675f, 0f, 0.05f, -1, aircraft, rigidbody, connectedPart, 300000f);
            BoxCollider engineDamageCollider = part.gameObject.AddComponent<BoxCollider>();
            engineDamageCollider.center = EngineDamageColliderCenter;
            engineDamageCollider.size = EngineDamageColliderSize;
            Component engine = AddRuntimeComponent(part.gameObject, "Turbojet");
            AudioSource turbineAudio = CreateEngineAudio(part.gameObject, 0.72f, 900f);
            AudioSource thrustAudio = CreateEngineAudio(part.gameObject, 0.58f, 1400f);
            Component nozzle = AddRuntimeComponent(part.gameObject, "JetNozzle");

            SerializedObject nozzleData = new SerializedObject(nozzle);
            Set(nozzleData, "part", part);
            Set(nozzleData, "turbojet", engine);
            Set(nozzleData, "engine", null);
            Set(nozzleData, "failureEffect", null);
            Set(nozzleData, "thrustTransform", part.transform);
            Set(nozzleData, "thrustProportion", 1f);
            Set(nozzleData, "pitchThrust", 0f);
            Set(nozzleData, "rollThrust", 0f);
            Set(nozzleData, "thrustMaxVolume", 0.58f);
            Set(nozzleData, "IRMin", 0.5f);
            Set(nozzleData, "IRMax", 2.2f);
            Set(nozzleData, "glow", null);
            Size(nozzleData, "vectorTransforms", 0);
            Size(nozzleData, "afterburners", 0);
            Set(nozzleData, "thrustAudio", thrustAudio);
            nozzleData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject data = new SerializedObject(engine);
            Set(data, "maxThrust", 47150f);
            Set(data, "damageFactor", 1f);
            Set(data, "engineFire", false);
            Set(data, "turbineAudio", turbineAudio);
            Set(data, "thrustVectoring", Vector3.zero);
            Set(data, "thrustVectoringGain", Vector3.zero);
            Set(data, "throttleRemap", new Vector2(0f, 1f));
            Set(data, "thrustVectoringMaxAirspeed", 0f);
            Set(data, "minDensity", 0.15f);
            Set(data, "splitThrustFactor", 0f);
            SetCurve(data, "altitudeThrust", new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(13716f, 0.56f), new Keyframe(18000f, 0.32f)));
            SetObjectArray(data, "nozzles", new UnityEngine.Object[] { nozzle });
            Size(data, "vectoringTransforms", 0);
            Set(data, "turbineMaxPitch", 1.35f);
            Set(data, "minRPM", 900f);
            Set(data, "maxRPM", 2900f);
            Set(data, "maxSpeed", 315f);
            Set(data, "spoolRate", 1150f);
            Set(data, "startupRate", 180f);
            Set(data, "fuelConsumptionMin", 0.12f);
            Set(data, "fuelConsumptionMax", 0.68f);
            Set(data, "damageThreshold", 55f);
            SetObjectArray(data, "criticalParts", new UnityEngine.Object[] { part });
            SetString(data, "failureMessage", side.ToUpperInvariant() + " ENGINE FIRE");
            Set(data, "failureMessageAudio", null);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        AudioSource CreateEngineAudio(GameObject host, float volume, float maxDistance)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 12f;
            source.maxDistance = maxDistance;
            source.volume = volume;
            return source;
        }
    }
    private static void ConfigureLandingGear(GameObject visual, Component aircraft, Component attachedPart,
        Material gearDustMaterial, TireAudioProfile tireAudioProfile)
    {
        ConfigureGear("Nose", "F117_Gear_Nose", "LOC_Gear_Nose_Stowed", "LOC_Gear_Nose_Contact",
            0.293f, 100f, true, false, new[] { new DoorSpec("F117_GearDoor_Nose", "LOC_GearDoor_Nose_Closed") });
        ConfigureGear("Left", "F117_Gear_Left", "LOC_Gear_Left_Stowed", "LOC_Gear_Left_Contact",
            0.427f, 150f, false, true, new[]
            {
                new DoorSpec("F117_GearDoor_Left_Outer", "LOC_GearDoor_Left_Outer_Closed"),
                new DoorSpec("F117_GearDoor_Left_Inner", "LOC_GearDoor_Left_Inner_Closed")
            });
        ConfigureGear("Right", "F117_Gear_Right", "LOC_Gear_Right_Stowed", "LOC_Gear_Right_Contact",
            0.427f, 150f, false, true, new[]
            {
                new DoorSpec("F117_GearDoor_Right_Outer", "LOC_GearDoor_Right_Outer_Closed"),
                new DoorSpec("F117_GearDoor_Right_Inner", "LOC_GearDoor_Right_Inner_Closed")
            });

        void ConfigureGear(string side, string visualName, string targetName, string contactName,
            float wheelRadius, float gearMass, bool steering, bool braked, DoorSpec[] doorSpecs)
        {
            Transform gearVisual = FindDeep(visual.transform, visualName);
            if (gearVisual == null)
                throw new InvalidOperationException("Missing production landing gear " + visualName + ".");
            HingeResult hinge = CreateAxisHinge(gearVisual, Locator(visual, targetName), "F117_Gear_" + side + "_Hinge");
            GameObject sprung = Child(hinge.Transform, "F117_Gear_" + side + "_Sprung", Vector3.zero);
            // LandingGear derives signed ground speed from transform.forward. Keep
            // the visual fold hinge, but align the physics frame aircraft-forward.
            sprung.transform.SetPositionAndRotation(hinge.Transform.position, visual.transform.rotation);

            Transform contactLocator = Locator(visual, contactName);
            Vector3 canonicalContact = side == "Nose"
                ? new Vector3(0f, GearContactPlaneY, NoseGearContactZ)
                : new Vector3(side == "Left" ? -MainGearHalfTrack : MainGearHalfTrack,
                    GearContactPlaneY, MainGearContactZ);
            contactLocator.SetPositionAndRotation(
                visual.transform.TransformPoint(canonicalContact), visual.transform.rotation);
            float suspensionTravel = side == "Nose" ? NoseSuspensionTravel : MainSuspensionTravel;
            GameObject unsprung = Child(sprung.transform, "F117_Gear_" + side + "_Unsprung", Vector3.zero);
            GameObject bumpStop = Child(sprung.transform, "BumpStop", Vector3.zero);
            bumpStop.transform.SetPositionAndRotation(
                contactLocator.position + visual.transform.up * suspensionTravel,
                visual.transform.rotation);
            unsprung.transform.SetPositionAndRotation(
                contactLocator.position + visual.transform.up * wheelRadius,
                visual.transform.rotation);
            gearVisual.SetParent(unsprung.transform, true);
            Transform castPoint = Child(bumpStop.transform, "CastPoint", Vector3.zero).transform;
            Transform axle = Child(unsprung.transform, "Axle", Vector3.zero).transform;
            axle.SetPositionAndRotation(contactLocator.position + visual.transform.up * wheelRadius, visual.transform.rotation);
            Transform wheelProxy = Child(axle, "WheelProxy", Vector3.zero).transform;
            AudioSource rollingAudio = CreateTireAudio(wheelProxy.gameObject);
            AudioSource skidAudio = CreateTireAudio(wheelProxy.gameObject);
            ParticleSystem dust = wheelProxy.gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule dustMain = dust.main;
            dustMain.playOnAwake = false;
            dustMain.loop = true;
            dustMain.maxParticles = 0;
            dustMain.startLifetime = 0f;
            dustMain.startSpeed = 0f;
            dustMain.startSize = 0f;
            ParticleSystem.EmissionModule dustEmission = dust.emission;
            dustEmission.enabled = false;
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
            if (dustRenderer != null)
            {
                dustRenderer.sharedMaterial = gearDustMaterial;
                dustRenderer.enabled = false;
                dustRenderer.forceRenderingOff = true;
            }
            BoxCollider gearCollider = sprung.AddComponent<BoxCollider>();
            gearCollider.center = sprung.transform.InverseTransformPoint(contactLocator.position + visual.transform.up * wheelRadius);
            gearCollider.size = new Vector3(wheelRadius * 1.2f, wheelRadius * 2f, wheelRadius * 1.2f);
            gearCollider.enabled = false;

            Component gear = AddRuntimeComponent(sprung, "LandingGear");
            SerializedObject data = new SerializedObject(gear);
            Set(data, "attachedPart", attachedPart);
            Set(data, "extendedDrag", side == "Nose" ? 0.75f : 1.1f);
            Set(data, "bumpStop", bumpStop);
            Set(data, "unsprung", unsprung);
            Set(data, "suspensionTravel", suspensionTravel);
            Set(data, "springRate", side == "Nose" ? NoseGearSpringRate : MainGearSpringRate);
            Set(data, "dampingRate", side == "Nose" ? NoseGearDampingRate : MainGearDampingRate);
            Set(data, "castPoint", castPoint);
            Set(data, "wheelRadius", wheelRadius);
            SetObjectArray(data, "wheels", new UnityEngine.Object[] { wheelProxy });
            Set(data, "axle", axle);
            Set(data, "frictionCoef", side == "Nose" ? 0.82f : 0.88f);
            Set(data, "contactArea", side == "Nose" ? NoseGearContactArea : 0.045f);
            Set(data, "response", side == "Nose" ? NoseTireResponse : MainTireResponse);
            Set(data, "rollingResistance", TireRollingResistance);
            Set(data, "aircraft", aircraft);
            Set(data, "tireNoiseSound", rollingAudio);
            Set(data, "tireSkidSound", skidAudio);
            // The wheel and axle frames are aircraft-forward, so the native slip
            // calculation is valid and can retain its stock audio response.
            Set(data, "skidVolumeFloor", -0.4f);
            Set(data, "skidPitchMult", 1f);
            // Preserve the complete stock-sized probe envelope. Moving the bump stop
            // upward with the longer travel leaves the rendered tire/contact plane
            // unchanged while preventing one-frame misses over runway seams.
            Set(data, "maxCompression", suspensionTravel);
            Set(data, "mass", gearMass);
            Set(data, "gearCollider", gearCollider);
            Set(data, "breakSound", null);
            Set(data, "foldSound", null);
            Set(data, "foldVolume", 0.3f);
            Set(data, "latchSound", null);
            Set(data, "latchVolume", 0.3f);
            Set(data, "gearHinge", hinge.Transform);
            Set(data, "hingeFoldMotion", hinge.Motion);
            Set(data, "foldDegrees", hinge.Angle);
            Set(data, "foldSpeed", 28f);
            Set(data, "strutRotationTransform", unsprung.transform);
            Set(data, "strutRotation", 0f);
            // The F-117 source linkage includes relative translations that the
            // stock rotation-only GearPart structure cannot represent. The model
            // therefore carries source-derived residual-link pose locators; the
            // aircraft plugin evaluates them from this gear's normalized native
            // fold state while LandingGear continues to own physics and doors.
            Size(data, "movingParts", 0);
            Size(data, "joints", 0);

            SerializedProperty doors = Require(data, "gearDoors");
            doors.arraySize = doorSpecs.Length;
            for (int index = 0; index < doorSpecs.Length; index++)
            {
                Transform door = FindDeep(visual.transform, doorSpecs[index].Visual);
                Transform closed = Locator(visual, doorSpecs[index].ClosedTarget);
                if (door == null)
                    throw new InvalidOperationException("Missing landing-gear door " + doorSpecs[index].Visual + ".");
                // LandingGear lerps eulers and slams retracted doors to (0,0,0).
                // Closed must be identity on an X-hinge or the close axis is garbage.
                HingeResult doorHinge = CreateClosedXHinge(door, closed, doorSpecs[index].Visual + "_CloseHinge");
                SerializedProperty entry = doors.GetArrayElementAtIndex(index);
                Set(entry, "transform", doorHinge.Transform);
                Set(entry, "openAngle", new Vector3(doorHinge.Angle, 0f, 0f));
                Set(entry, "closedAngle", Vector3.zero);
            }

            Set(data, "steering", steering);
            Set(data, "braked", braked);
            Set(data, "steeringLock", steering ? NoseSteeringLock : 0f);
            Set(data, "steeringSpeed", steering ? NoseSteeringSpeed : 0f);
            Set(data, "aligningStrength", steering ? NoseAligningStrength : 0f);
            Set(data, "differentialBrakeFactor",
                side == "Left" ? -1f : side == "Right" ? 1f : 0f);
            Set(data, "dust", dust);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        AudioSource CreateTireAudio(GameObject host)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.volume = 0f;
            tireAudioProfile.Apply(source);
            return source;
        }
    }

    private static void ConfigureRuntimeSystems(GameObject root, GameObject visual, Component aircraft, Component centralPart)
    {
        SerializedObject aircraftNetworkData = new SerializedObject(aircraft);
        SetEnum(aircraftNetworkData, "SyncSettings.From", "Server");
        SetEnum(aircraftNetworkData, "SyncSettings.To", "Owner And Observers");
        SetEnum(aircraftNetworkData, "SyncSettings.Timing", "Variable");
        Set(aircraftNetworkData, "SyncSettings.Interval", 0.1f);
        SetString(aircraftNetworkData, "MapUniqueName", string.Empty);
        Set(aircraftNetworkData, "MapHQ", null);
        Set(aircraftNetworkData, "MapAirbase", null);
        Size(aircraftNetworkData, "weaponStations", 0);
        Set(aircraftNetworkData, "obstacleTopTransform", null);
        Set(aircraftNetworkData, "HasRequestedRearm", false);
        Size(aircraftNetworkData, "dopplerSounds", 0);
        // Retain the donor aircraft's valid scrape clip. Aircraft.ThrowSparks
        // dereferences it before starting the scrape source.
        Set(aircraftNetworkData, "relaxedStabilityController", null);
        Set(aircraftNetworkData, "flightAssist", false);
        GameObject sparksObject = Child(visual.transform, "F117_SparksEmitter", Vector3.zero);
        ParticleSystem sparks = sparksObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule sparksMain = sparks.main;
        sparksMain.playOnAwake = false;
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer sparksRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        if (sparksRenderer != null)
        {
            sparksRenderer.enabled = false;
            sparksRenderer.forceRenderingOff = true;
        }
        Set(aircraftNetworkData, "sparksEmitter", sparks);
        Size(aircraftNetworkData, "groundEquipment", 0);
        Set(aircraftNetworkData, "ecmIntensity", 0f);
        SerializedProperty countermeasures = Require(aircraftNetworkData, "countermeasureManager");
        Size(countermeasures, "countermeasureStations", 0);
        Set(countermeasures, "activeIndex", 0);
        Set(countermeasures, "aircraft", aircraft);
        aircraftNetworkData.ApplyModifiedPropertiesWithoutUndo();

        Component networkTransform = FindComponent(root, "AircraftNetworkTransform");
        Component networkIdentity = FindComponent(root, "NetworkIdentity");
        if (networkTransform == null || networkIdentity == null)
            throw new InvalidOperationException("The reference aircraft is missing required multiplayer networking behavior.");
        SerializedObject transformNetworkData = new SerializedObject(networkTransform);
        SetEnum(transformNetworkData, "SyncSettings.From", "Server");
        SetEnum(transformNetworkData, "SyncSettings.To", "Owner And Observers");
        SetEnum(transformNetworkData, "SyncSettings.Timing", "Variable");
        Set(transformNetworkData, "SyncSettings.Interval", 0.1f);
        Set(transformNetworkData, "extrapolationFactor", 1f);
        Set(transformNetworkData, "Aircraft", aircraft);
        Set(transformNetworkData, "SendInputsInterval", 4);
        transformNetworkData.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject identityData = new SerializedObject(networkIdentity);
        Set(identityData, "SpawnSettings.SendPosition", true);
        Set(identityData, "SpawnSettings.SendRotation", true);
        Set(identityData, "SpawnSettings.SendScale", false);
        Set(identityData, "SpawnSettings.SendName", false);
        SetEnum(identityData, "SpawnSettings.SendActive", "Force Enable");
        identityData.ApplyModifiedPropertiesWithoutUndo();

        Component[] tanks = FindComponents(root, "FuelTank");
        Component fuelTank = tanks.FirstOrDefault(tank => tank.transform == root.transform) ?? tanks.FirstOrDefault();
        if (fuelTank == null)
            throw new InvalidOperationException("The reference aircraft has no reusable fuel-tank behavior.");
        foreach (Component tank in tanks)
            if (tank != fuelTank)
                UnityEngine.Object.DestroyImmediate(tank, true);
        SerializedObject tankData = new SerializedObject(fuelTank);
        Set(tankData, "fuelCapacity", 8250f);
        Set(tankData, "fuelMass", 0f);
        Size(tankData, "connectedTanks", 0);
        // One aggregate, protected internal tank.  These are explicit gameplay
        // damage values, not inherited donor-airframe defaults.
        Set(tankData, "leakThreshold", 50f);
        Set(tankData, "leakPerHP", 0.2f);
        Set(tankData, "maxLeakRate", 50f);
        Set(tankData, "ruptureGMin", 50f);
        Set(tankData, "ruptureGMax", 500f);
        Set(tankData, "ignitionGMin", 50f);
        Set(tankData, "ignitionGMax", 500f);
        Set(tankData, "ignitionPierceMin", 2f);
        Set(tankData, "ignitionPierceMax", 6f);
        Set(tankData, "ignitionBlastMin", 2f);
        Set(tankData, "ignitionBlastMax", 6f);
        Set(tankData, "fireIntensity", 3f);
        Set(tankData, "leakEffect", null);
        Set(tankData, "fireEffect", null);
        Set(tankData, "fireball", null);
        Set(tankData, "part", centralPart);
        tankData.ApplyModifiedPropertiesWithoutUndo();

        Component[] engines = FindComponents(root, "Turbojet");
        if (engines.Length != 2)
            throw new InvalidOperationException("The assembled F-117 must have exactly two engines before system wiring.");
        UnityEngine.Object[] engineHosts = engines.Select(engine => (UnityEngine.Object)engine.gameObject).ToArray();

        Component power = FindComponent(root, "PowerSupply");
        if (power == null)
            throw new InvalidOperationException("The reference aircraft has no reusable power-supply behavior.");
        AudioSource powerAudio = power.gameObject.AddComponent<AudioSource>();
        powerAudio.playOnAwake = false;
        powerAudio.loop = true;
        powerAudio.spatialBlend = 1f;
        powerAudio.minDistance = 4f;
        powerAudio.maxDistance = 100f;
        powerAudio.volume = 0f;
        SerializedObject powerData = new SerializedObject(power);
        SetObjectArray(powerData, "powerSources", engineHosts);
        Set(powerData, "maxCharge", JammerBusCapacityKj);
        Set(powerData, "maxPower", JammerNominalPower);
        // JammingPod1 itself remains completely native at a 13-unit draw. This
        // dedicated 60 kJ bus supplies about five seconds of strong jamming from
        // full charge. Two engines at 2,900 RPM replenish about 1.16 kJ/s, making
        // a full recovery take roughly 52 seconds at maximum RPM and much longer
        // at idle. The jammer is therefore a critical-moment burst tool.
        Set(powerData, "chargePerRPM", JammerChargePerEngineRpm);
        Set(powerData, "pitchMin", 0.8f);
        Set(powerData, "pitchMax", 1.25f);
        Set(powerData, "volumeMultiplier", 0f);
        SetCurve(powerData, "supplyAtCharge", new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 0.65f), new Keyframe(1f, 1f)));
        Set(powerData, "source", powerAudio);
        Set(powerData, "aircraft", aircraft);
        powerData.ApplyModifiedPropertiesWithoutUndo();

        ConfigureCountermeasures(root, centralPart, aircraft);

        Component noseGear = FindComponents(root, "LandingGear")
            .FirstOrDefault(gear => gear.name.IndexOf("Nose", StringComparison.OrdinalIgnoreCase) >= 0);
        Component controls = FindComponent(root, "ControlsFilter");
        if (controls == null || noseGear == null)
            throw new InvalidOperationException("The F-117 controls or nose gear are missing during system wiring.");
        SerializedObject controlsData = new SerializedObject(controls);
        Set(controlsData, "minSpeed", FlightControlMinimumSpeed);
        Set(controlsData, "minAlt", FlightControlMinimumAltitude);
        SetString(controlsData, "flightAssistName", "F-117 Stability Assist");
        SerializedProperty flyByWire = Require(controlsData, "flyByWire");
        Set(flyByWire, "Enabled", true);
        Set(flyByWire, "gLimitPositive", FlightControlGLimit);
        Set(flyByWire, "opacity", 1f);
        Set(flyByWire, "cornerSpeed", FlightControlCornerSpeed);
        Set(flyByWire, "postStallManeuverSpeed", 90f);
        Set(flyByWire, "maxRollSpeed", 180f);
        Set(flyByWire, "takeoffSpeed", FlightControlTakeoffSpeed);
        Set(flyByWire, "pidTransitionSpeed", 220f);
        Set(flyByWire, "maxPitchAngularVel", FlightControlMaxPitchRate);
        Set(flyByWire, "maxRollAngularVel", FlightControlMaxRollRate);
        Set(flyByWire, "alphaLimiter", FlightControlAlphaLimit);
        Set(flyByWire, "alphaLimiterStrength", 0.1f);
        Set(flyByWire, "pFactorFast", 10f);
        Set(flyByWire, "iFactor", 0.01f);
        Set(flyByWire, "dFactorFast", FlightControlPitchDamping);
        Set(flyByWire, "rollTrimRate", 0.04f);
        Set(flyByWire, "rollTrimLimit", 0.3f);
        Set(flyByWire, "yawTightness", FlightControlYawTightness);
        Set(flyByWire, "yawWeathervaning", 0f);
        Set(flyByWire, "rollTightness", 0.65f);
        Set(flyByWire, "inputSmoothing", Vector3.zero);
        Set(flyByWire, "noseGear", noseGear);
        SerializedProperty autoHover = Require(controlsData, "autoHover");
        Set(autoHover, "Enabled", false);
        Set(autoHover, "Active", false);
        Set(autoHover, "setFlightAssistOff", false);
        Set(autoHover, "customAxis1Position", 0f);
        Set(autoHover, "errorGain", 0f);
        Set(autoHover, "sensitivity", 0f);
        Set(autoHover, "hoverBaseThrottle", 0f);
        Set(autoHover, "climbSensitivity", 0f);
        Set(autoHover, "customAxisSlowDown", 0f);
        Set(autoHover, "correctionStrength", 0f);
        Set(autoHover, "maxSpeed", 0f);
        Set(Require(autoHover, "attitudePIDFactors"), "PID", Vector3.zero);
        Set(Require(autoHover, "altitudePIDFactors"), "PID", Vector3.zero);
        Set(autoHover, "lastShipCheck", 0f);
        Set(autoHover, "storedFlightAssistState", false);
        SerializedProperty aimAssist = Require(controlsData, "aimAssist");
        Set(aimAssist, "Enabled", false);
        Set(Require(aimAssist, "pitchPID"), "PID", Vector3.zero);
        Set(Require(aimAssist, "yawPID"), "PID", Vector3.zero);
        Set(Require(aimAssist, "rollPID"), "PID", Vector3.zero);
        Set(controlsData, "flightAssistDefault", true);
        Set(controlsData, "ReverseThrust", false);
        controlsData.ApplyModifiedPropertiesWithoutUndo();

        Component autopilot = FindComponent(root, "AutopilotPlane");
        if (autopilot == null)
            throw new InvalidOperationException("The F-117 autopilot behavior is missing during system wiring.");
        SerializedObject autopilotData = new SerializedObject(autopilot);
        Set(autopilotData, "aircraft", aircraft);
        SerializedProperty forward = Require(autopilotData, "forwardFlightController");
        Set(forward, "Enabled", true);
        Set(forward, "referenceAirspeed", FlightControlCornerSpeed);
        Set(Require(forward, "pitchFlightPID"), "PID", new Vector3(0.06f, 0.002f, 0.04f));
        Set(Require(forward, "yawFlightPID"), "PID", new Vector3(0.045f, 0f, 0.03f));
        Set(Require(forward, "rollFlightPID"), "PID", new Vector3(0.012f, 0.0005f, 0.06f));
        SerializedProperty hover = Require(autopilotData, "hoverController");
        Set(hover, "Enabled", false);
        Set(Require(hover, "pitchHoverPID"), "PID", Vector3.zero);
        Set(Require(hover, "yawHoverPID"), "PID", Vector3.zero);
        Set(Require(hover, "rollHoverPID"), "PID", Vector3.zero);
        Set(hover, "hoverThrottle", 0f);
        Set(autopilotData, "preventInvertedFlight", true);
        SerializedProperty aoaLimiter = Require(autopilotData, "aoaLimiter");
        Set(aoaLimiter, "threshold", 12f);
        Set(aoaLimiter, "limit", FlightControlAlphaLimit);
        autopilotData.ApplyModifiedPropertiesWithoutUndo();

        Component locator = FindComponent(root, "RadarLocator");
        if (locator != null)
        {
            SerializedObject locatorData = new SerializedObject(locator);
            Set(locatorData, "aircraft", aircraft);
            SetObjectArray(locatorData, "essentialParts", new UnityEngine.Object[] { centralPart });
            Set(locatorData, "onlySurface", false);
            locatorData.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject aircraftData = new SerializedObject(aircraft);
        Set(aircraftData, "powerSupply", power);
        Set(aircraftData, "controlsFilter", controls);
        aircraftData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureCountermeasures(GameObject root, Component centralPart, Component aircraft)
    {
        Component flare = FindComponent(root, "FlareEjector");
        if (flare == null)
            flare = root.AddComponent(FindType("FlareEjector"));
        Component chaff = root.AddComponent(FindType("ChaffEjector"));

        Transform stationRoot = Child(centralPart.transform, "F117_Countermeasures", new Vector3(0f, 0.05f, -4.8f)).transform;
        Transform left = Child(stationRoot, "Countermeasure_Left", new Vector3(-0.55f, 0f, 0f)).transform;
        Transform right = Child(stationRoot, "Countermeasure_Right", new Vector3(0.55f, 0f, 0f)).transform;
        left.localRotation = Quaternion.Euler(15f, 180f, -8f);
        right.localRotation = Quaternion.Euler(15f, 180f, 8f);

        ConfigureCountermeasureEjector(flare, aircraft, centralPart, left, right,
            "IR Flares", "flarePrefab", null, 32, 20f, 0.12f);
        ConfigureCountermeasureEjector(chaff, aircraft, centralPart, left, right,
            "Radar Chaff", "chaffPrefab", CreateRadarChaffPrefab(), 64, 18f, 0.12f);
    }

    private static void ConfigureCountermeasureEjector(Component ejector, Component aircraft, Component part,
        Transform left, Transform right, string displayName, string prefabField, GameObject payloadPrefab,
        int ammo, float ejectionVelocity, float ejectionInterval)
    {
        SerializedObject data = new SerializedObject(ejector);
        SetString(data, "displayName", displayName);
        Set(data, "displayImage", null);
        Set(data, "chargeable", false);
        Set(data, "ammo", ammo);
        Set(data, "aircraft", aircraft);
        Set(data, prefabField, payloadPrefab);
        Set(data, "ejectionVelocity", ejectionVelocity);
        Set(data, "ejectionVelocityVariance", 0.15f);
        SerializedProperty points = Require(data, "ejectionPoints");
        points.arraySize = 2;
        foreach (var entry in new[] { new { Index = 0, Transform = left }, new { Index = 1, Transform = right } })
        {
            SerializedProperty point = points.GetArrayElementAtIndex(entry.Index);
            Set(point, "part", part);
            Set(point, "transform", entry.Transform);
        }
        Size(data, "flareDoors", 0);
        Set(data, "ejectionInterval", ejectionInterval);
        Set(data, "ejectionGrouping", 2);
        Set(data, "ejectionSound", null);
        Set(data, "ejectionVolume", 0.8f);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject CreateRadarChaffPrefab()
    {
        AssetDatabase.DeleteAsset(RadarChaffPrefabPath);
        GameObject root = new GameObject("F117_RadarChaff");
        Component chaff = root.AddComponent(FindType("RadarChaff"));
        GameObject particleObject = Child(root.transform, "ChaffParticles", Vector3.zero);
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 8f;
        main.startLifetime = 1.4f;
        main.startSpeed = 0.35f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.62f, 0.68f, 0.72f, 0.7f),
            new Color(1f, 1f, 1f, 0.95f));
        main.maxParticles = 128;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 45f;
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.72f, 0.78f, 0.82f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.75f, 0.72f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            AssetDatabase.DeleteAsset(RadarChaffTexturePath);
            Texture2D glint = new Texture2D(16, 4, TextureFormat.RGBA32, false, true)
            {
                name = "F117_RadarChaff_Glint",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < glint.height; y++)
            for (int x = 0; x < glint.width; x++)
            {
                float along = 1f - Mathf.Abs((x + 0.5f) / glint.width * 2f - 1f);
                float across = 1f - Mathf.Abs((y + 0.5f) / glint.height * 2f - 1f);
                float alpha = Mathf.Pow(Mathf.Clamp01(along), 0.45f) * Mathf.Pow(Mathf.Clamp01(across), 1.8f);
                glint.SetPixel(x, y, new Color(0.92f, 0.97f, 1f, alpha));
            }
            glint.Apply(false, true);
            AssetDatabase.CreateAsset(glint, RadarChaffTexturePath);

            // The AssetRipper authoring project exposes the verified game-compatible
            // URP/Lit placeholder used by every production F-117 material, but not the
            // package's particle-specific shaders. ParticleSystemRenderer still supplies
            // ordinary billboard geometry, so the known Lit shader is the safe runtime path.
            Shader particleShader = Shader.Find("Universal Render Pipeline/Lit");
            if (particleShader == null)
                throw new InvalidOperationException("No runtime-compatible URP shader is available for radar chaff.");
            AssetDatabase.DeleteAsset(RadarChaffMaterialPath);
            Material material = new Material(particleShader) { name = "F117_RadarChaff" };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", glint);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", glint);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, RadarChaffMaterialPath);

            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 0.7f;
            renderer.cameraVelocityScale = 0f;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
        }
        SerializedObject data = new SerializedObject(chaff);
        Set(data, "chaffTime", 8f);
        Set(data, "drag", 0.0025f);
        Set(data, "chaffParticles", particles);
        Set(data, "emitFrequency", 45f);
        Set(data, "minSpeed", 20f);
        data.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RadarChaffPrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null)
            throw new InvalidOperationException("Unity failed to create the F-117 RadarChaff prefab.");
        return prefab;
    }

    private static void ConfigureCrewAndSensors(GameObject root, GameObject visual, Component aircraft,
        Component cockpitPart, GameObject runtimeUiFallback, MeshRenderer cockpitScreenRenderer)
    {
        Component cockpitController = FindComponent(root, "Cockpit");
        Transform cockpit = FindDeep(root.transform, "Cockpit");
        Transform pilot = cockpit == null ? null : FindDeep(cockpit, "pilot");
        if (pilot == null)
            throw new InvalidOperationException("The reference pilot is missing.");
        pilot.position = Locator(visual, "LOC_PilotSeat").position;
        pilot.rotation = root.transform.rotation;
        Transform helmetCamera = FindDeep(pilot, "helmetCamPoint");
        if (helmetCamera == null)
            throw new InvalidOperationException("The retained pilot rig is missing helmetCamPoint.");
        Transform authoredCamera = Locator(visual, "LOC_CockpitCamera");
        Transform cameraPoint = Child(root.transform, "F117_CockpitViewPoint", Vector3.zero).transform;
        // The model locator sits 0.52 m ahead of the seat origin, against the glare
        // shield. Retain its authored eye height and move it back onto the seat line.
        cameraPoint.position = authoredCamera.position - root.transform.forward * CockpitCameraRearwardOffset;
        cameraPoint.rotation = root.transform.rotation;

        Component radar = FindComponent(root, "Radar");
        Component radarLocator = FindComponent(root, "RadarLocator");
        if (radarLocator == null)
            throw new InvalidOperationException("The reference passive radar-warning receiver is missing.");
        Component eots = root.GetComponentsInChildren<Component>(true)
            .FirstOrDefault(component => component != null && component.GetType().Name == "TargetDetector");

        if (eots != null)
        {
            SerializedObject eotsData = new SerializedObject(eots);
            Set(eotsData, "attachedUnit", aircraft);
            Set(eotsData, "scanner", cameraPoint);
            Set(eotsData, "part", cockpitPart);
            Set(eotsData, "checkInterval", 0.2f);
            Set(eotsData, "alertCheckInterval", 0.1f);
            Set(eotsData, "visualRange", 15000f);
            Set(eotsData, "magnification", 3f);
            Set(eotsData, "maxSpeed", 1000f);
            Size(eotsData, "rotators", 0);
            Set(eotsData, "shared", false);
            Size(eotsData, "detectedTargets", 0);
            eotsData.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            throw new InvalidOperationException("The passive F-117 EOTS detector is missing.");
        }

        // The native TacScreen has an explicit radar-null optical path. Remove the inherited
        // emitting search radar and let the retained TargetDetector, RWR and shared contacts do
        // their normal jobs without advertising this aircraft through Radar.HasRadarEmission().
        if (radar != null)
            UnityEngine.Object.DestroyImmediate(radar, true);

        Component targetCam = FindComponent(root, "TargetCam");
        if (targetCam != null)
        {
            Transform forwardMount = FindDeep(root.transform, "TargetCamFront");
            Transform rearMount = FindDeep(root.transform, "TargetCamRear");
            Transform landingMount = FindDeep(root.transform, "LandingCam");
            if (forwardMount == null || rearMount == null || landingMount == null)
                throw new InvalidOperationException("The reference target-camera mounts are missing.");
            forwardMount.name = "F117_TargetCamera_Forward";
            rearMount.name = "F117_TargetCamera_Rear";
            landingMount.name = "F117_TargetCamera_Landing";
            forwardMount.SetParent(root.transform, false);
            rearMount.SetParent(root.transform, false);
            landingMount.SetParent(root.transform, false);
            forwardMount.localPosition = new Vector3(0f, 0.2f, 9.8f);
            forwardMount.localRotation = Quaternion.identity;
            rearMount.localPosition = new Vector3(0f, 0.65f, -9.2f);
            rearMount.localRotation = Quaternion.identity;
            landingMount.localPosition = new Vector3(0f, -1.65f, -3.2f);
            landingMount.localRotation = Quaternion.Euler(7f, 0f, 0f);
            SerializedObject targetCamData = new SerializedObject(targetCam);
            Set(targetCamData, "currentMount", forwardMount);
            Set(targetCamData, "camMountForward", forwardMount);
            Set(targetCamData, "camMountRear", rearMount);
            Set(targetCamData, "camMountLanding", landingMount);
            Set(targetCamData, "attachedPart", cockpitPart);
            if (cockpitScreenRenderer == null || !cockpitScreenRenderer.enabled)
                throw new InvalidOperationException("The target camera requires the visible F-117 tactical-screen renderer.");
            Set(targetCamData, "targetScreenRenderer", cockpitScreenRenderer);
            Set(targetCamData, "landingCamFoV", 90f);
            Set(targetCamData, "vtolLandingCam", false);
            targetCamData.ApplyModifiedPropertiesWithoutUndo();
        }

        Component laser = FindComponent(root, "LaserDesignator");
        if (laser != null)
        {
            SerializedObject laserData = new SerializedObject(laser);
            Set(laserData, "unitPart", cockpitPart);
            Set(laserData, "aircraft", aircraft);
            Set(laserData, "maxTargets", 1);
            Set(laserData, "range", 20000f);
            laserData.ApplyModifiedPropertiesWithoutUndo();
        }

        Component pilotComponent = FindComponent(root, "Pilot");
        if (pilotComponent == null)
            throw new InvalidOperationException("The reference pilot behavior is missing.");
        SerializedObject pilotData = new SerializedObject(pilotComponent);
        CapsuleCollider pilotCollider = pilotComponent.GetComponent<CapsuleCollider>();
        SkinnedMeshRenderer pilotRenderer = pilotComponent.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Animator pilotAnimator = pilotComponent.GetComponentInChildren<Animator>(true);
        if (pilotCollider == null || pilotRenderer == null || pilotAnimator == null)
            throw new InvalidOperationException("The retained game-native pilot rig is incomplete.");
        SetEnum(pilotData, "pilotType", "Plane");
        SetEnum(pilotData, "exitDirection", "Left");
        Set(pilotData, "playerControlled", false);
        Set(pilotData, "dead", false);
        Set(pilotData, "ejected", false);
        Set(pilotData, "aircraft", aircraft);
        Set(pilotData, "player", null);
        Set(pilotData, "pilotCollider", pilotCollider);
        Set(pilotData, "skinnedMeshRenderer", pilotRenderer);
        Set(pilotData, "animator", pilotAnimator);
        Set(pilotData, "unitPart", cockpitPart);
        Set(pilotData, "ejectionSeat", true);
        Set(pilotData, "accel", Vector3.zero);
        Set(pilotData, "velocityPrev", Vector3.zero);
        Set(pilotData, "gForce", 0f);
        SerializedProperty armor = Require(pilotData, "armorProperties");
        Set(armor, "pierceArmor", 0f);
        Set(armor, "blastArmor", 100f);
        Set(armor, "fireArmor", 0f);
        Set(armor, "pierceTolerance", 0.5f);
        Set(armor, "blastTolerance", 1f);
        Set(armor, "fireTolerance", 1f);
        Set(armor, "overpressureLimit", 10f);
        Set(pilotData, "relaxedStabilityController", null);
        Set(pilotData, "autoTrimmer", FindComponent(root, "ControlsFilter"));
        pilotData.ApplyModifiedPropertiesWithoutUndo();

        if (cockpitController == null || cockpitScreenRenderer == null)
            throw new InvalidOperationException("The native cockpit controller or dedicated F-117 tactical screen is missing.");
        SerializedObject cockpitData = new SerializedObject(cockpitController);
        if (runtimeUiFallback == null)
            throw new InvalidOperationException("The runtime cockpit UI fallback is missing.");
        Set(cockpitData, "tacScreenUIPrefab", runtimeUiFallback);
        Set(cockpitData, "tacScreenRender", cockpitScreenRenderer);
        Set(cockpitData, "aircraft", aircraft);
        UnityEngine.Object[] engineSources = new[] { "Left", "Right" }
            .Select(side => (UnityEngine.Object)FindDeep(root.transform, "F117_Engine_" + side)?.gameObject)
            .ToArray();
        if (engineSources.Any(source => source == null))
            throw new InvalidOperationException("The cockpit controller could not find both F-117 engines.");
        SetObjectArray(cockpitData, "engineSources", engineSources);
        Size(cockpitData, "joysticks", 0);
        Size(cockpitData, "throttles", 0);
        cockpitData.ApplyModifiedPropertiesWithoutUndo();
        cockpitController.transform.name = "F117_CockpitController";

        SerializedObject data = new SerializedObject(aircraft);
        Set(data, "cockpitViewPoint", cameraPoint);
        Set(data, "cockpit", cockpitPart);
        Set(data, "radar", null);
        if (eots != null)
            Set(data, "EOTS", eots);
        if (targetCam != null)
            Set(data, "targetCam", targetCam);
        SetObjectArray(data, "pilots", new UnityEngine.Object[] { pilotComponent });
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureCanopy(GameObject root, GameObject visual, Component aircraft, Component attachedPart)
    {
        Component[] canopies = FindComponents(root, "Canopy");
        Component canopy = canopies.FirstOrDefault();
        Transform canopyVisual = FindDeep(visual.transform, "F117_Canopy");
        if (canopy == null || canopyVisual == null)
            throw new InvalidOperationException("The canopy behavior or production canopy is missing.");
        foreach (Component extra in canopies.Skip(1))
            UnityEngine.Object.DestroyImmediate(extra, true);

        HingeResult hinge = CreateAxisHinge(canopyVisual, Locator(visual, "LOC_Canopy_Open"), "F117_Canopy_Hinge");
        BoxCollider ejectionCollider = hinge.Transform.gameObject.AddComponent<BoxCollider>();
        ejectionCollider.size = new Vector3(1.4f, 0.35f, 2.2f);
        ejectionCollider.enabled = false;
        SerializedObject canopyData = new SerializedObject(canopy);
        Set(canopyData, "ejectionTransform", hinge.Transform);
        Set(canopyData, "attachedPart", attachedPart);
        Set(canopyData, "mass", 90f);
        Set(canopyData, "ejectionCollider", ejectionCollider);
        Set(canopyData, "ejectionForce", new Vector3(0f, 5500f, 900f));
        Set(canopyData, "forcePosition", Vector3.zero);
        SerializedProperty hinges = Require(canopyData, "canopyHinges");
        hinges.arraySize = 1;
        SerializedProperty entry = hinges.GetArrayElementAtIndex(0);
        Set(entry, "transform", hinge.Transform);
        // CanopyHinge uses the signed local-X rotation returned by CreateAxisHinge.
        Set(entry, "hingeAngle", hinge.Angle);
        Set(canopyData, "openSpeed", 0.5f);
        Set(canopyData, "fireTime", 0.25f);
        Set(canopyData, "ejectSound", null);
        Set(canopyData, "glassDamageThreshold", 80f);
        Set(canopyData, "glassDamageLimit", 10f);
        SetObjectArray(canopyData, "glassRenderers", canopyVisual.GetComponentsInChildren<Renderer>(true));
        canopyData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject aircraftData = new SerializedObject(aircraft);
        SetObjectArray(aircraftData, "canopies", new UnityEngine.Object[] { canopy });
        aircraftData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureWeapons(GameObject root, GameObject visual, Component manager, Component centralPart)
    {
        Component leftDoor = ConfigureBayDoor(visual, "F117_BayDoor_Left", "LOC_BayDoor_Left_Open");
        Component rightDoor = ConfigureBayDoor(visual, "F117_BayDoor_Right", "LOC_BayDoor_Right_Open");
        Transform leftSocket = Locator(visual, "LOC_Weapon_Left");
        Transform rightSocket = Locator(visual, "LOC_Weapon_Right");
        if (Mathf.Abs(leftSocket.localPosition.y - InternalStoreMountHeight) > 0.01f ||
            Mathf.Abs(rightSocket.localPosition.y - InternalStoreMountHeight) > 0.01f)
            throw new InvalidOperationException("The production internal-store locators are not on the audited bay mount plane.");

        SerializedObject data = new SerializedObject(manager);
        SerializedProperty sets = Require(data, "hardpointSets");
        sets.arraySize = 3;
        ConfigureWeaponSet(sets.GetArrayElementAtIndex(0), "Left Weapon Bay",
            F117Builder.WeaponOptionCount, leftSocket, centralPart, Array.Empty<int>(), leftDoor);
        ConfigureWeaponSet(sets.GetArrayElementAtIndex(1), "Right Weapon Bay",
            F117Builder.WeaponOptionCount, rightSocket, centralPart, Array.Empty<int>(), rightDoor);

        // A third, completely internal station hosts the game's native JammingPod1
        // weapon unchanged. It has no bay-door or pylon geometry and is hidden/locked
        // by the runtime plugin, so the stock weapon remains installed on every loadout.
        Transform ecmSocket = Child(visual.transform, "F117_FixedJammerSocket", Vector3.zero).transform;
        ecmSocket.SetPositionAndRotation(root.transform.position, root.transform.rotation);
        ConfigureWeaponSet(sets.GetArrayElementAtIndex(2), "JammingPod1", 1, ecmSocket,
            centralPart, Array.Empty<int>());
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureWeaponSet(SerializedProperty set, string name, int optionCount,
        Transform socket, Component centralPart, int[] precludedSets, params Component[] bayDoors)
    {
        SetString(set, "name", name);
        SetString(set, "SymmetryName", string.Empty);
        Set(set, "SymmetryWithPrev", false);
        SerializedProperty preclusions = Require(set, "precludingHardpointSets");
        preclusions.arraySize = precludedSets.Length;
        for (int index = 0; index < precludedSets.Length; index++)
            preclusions.GetArrayElementAtIndex(index).intValue = precludedSets[index];
        SerializedProperty options = Require(set, "weaponOptions");
        options.arraySize = optionCount;
        for (int index = 0; index < options.arraySize; index++)
            options.GetArrayElementAtIndex(index).objectReferenceValue = null;
        Set(set, "weaponMount", null);

        SerializedProperty hardpoints = Require(set, "hardpoints");
        hardpoints.arraySize = 1;
        ConfigureHardpoint(hardpoints.GetArrayElementAtIndex(0), socket, centralPart, bayDoors);
    }

    private static Component ConfigureBayDoor(GameObject visual, string visualName, string targetName)
    {
        Transform doorVisual = FindDeep(visual.transform, visualName);
        if (doorVisual == null)
            throw new InvalidOperationException("Missing production weapon-bay door " + visualName + ".");
        HingeResult hinge = CreateZAxisHinge(doorVisual, Locator(visual, targetName), visualName + "_Hinge");
        string side = visualName.EndsWith("_Left", StringComparison.Ordinal) ? "Left" : "Right";
        Transform bayPanel = FindDeep(visual.transform, "F117_Bay_" + side);
        if (bayPanel == null)
            throw new InvalidOperationException("Missing production weapon-bay panel F117_Bay_" + side + ".");
        // F117_Bay_Left/Right are the fixed five-sided cavity meshes created by
        // build_production_model.py, not door liners. They remain on the fuselage;
        // moving them with the door produces the huge rectangular slabs seen in game.
        Component door = AddRuntimeComponent(hinge.Transform.gameObject, "BayDoor");
        SerializedObject data = new SerializedObject(door);
        Set(data, "hingeAngle", hinge.Angle);
        Set(data, "openSpeed", 2f);
        Set(data, "closeSpeed", 1.5f);
        Set(data, "doorAudioSource", null);
        Set(data, "openStartSound", null);
        Set(data, "closeStartSound", null);
        data.ApplyModifiedPropertiesWithoutUndo();
        return door;
    }

    private static void ConfigureHardpoint(SerializedProperty hardpoint, Transform socket, Component part, params Component[] bayDoorComponents)
    {
        Set(hardpoint, "transform", socket);
        Set(hardpoint, "part", part);
        SerializedProperty doors = Require(hardpoint, "bayDoors");
        doors.arraySize = bayDoorComponents.Length;
        for (int index = 0; index < bayDoorComponents.Length; index++)
            doors.GetArrayElementAtIndex(index).objectReferenceValue = bayDoorComponents[index];
        Set(hardpoint, "doorOpenDuration", 1.2f);
        Size(hardpoint, "pylonOptions", 0);
        Set(hardpoint, "Pylon", null);
        Set(hardpoint, "Plug", null);
        Size(hardpoint, "BuiltInWeapons", 0);
        Size(hardpoint, "BuiltInTurrets", 0);
    }

    private static void ConfigureRendererLists(GameObject visual, Component aircraft)
    {
        Transform cockpitGroup = FindDeep(visual.transform, "F117_Cockpit");
        Transform chuteGroup = FindDeep(visual.transform, "F117_DragChute");
        Renderer[] cockpit = cockpitGroup == null
            ? Array.Empty<Renderer>()
            : cockpitGroup.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name != "F117_Cockpit_Mesh")
                .ToArray();
        // The tub and canopy are shared by both views. Exterior-only renderers are
        // disabled by Aircraft.SetCockpitRenderers when entering the cockpit.
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            if (!(renderer is ParticleSystemRenderer))
                renderer.enabled = true;
        if (chuteGroup != null)
            chuteGroup.gameObject.SetActive(false);

        SerializedObject data = new SerializedObject(aircraft);
        SetObjectArray(data, "cockpitRenderers", cockpit.Cast<UnityEngine.Object>().ToArray());
        SetObjectArray(data, "exteriorRenderers", Array.Empty<UnityEngine.Object>());
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private readonly struct DoorSpec
    {
        internal readonly string Visual;
        internal readonly string ClosedTarget;

        internal DoorSpec(string visual, string closedTarget)
        {
            Visual = visual;
            ClosedTarget = closedTarget;
        }
    }
}
