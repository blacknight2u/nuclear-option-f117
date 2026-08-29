using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const float ParadeFlagSurfaceOffset = 0.012f;
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
    // The production exterior uses several very large wing polygons. Damage sections
    // must therefore be cut geometrically at these measured span stations rather than
    // assigning whole triangles by centroid. Unity's FBX conversion mirrors Blender X,
    // so the imported model's authored left side is negative X and right is positive X.
    internal const float WingRootSweep = 0.4383f;
    internal const float WingRootMetricCut = 4.1f;
    internal const float WingOuterSweep = 1.394f;
    internal const float WingOuterMetricCut = 11.82f;
    internal const float WingRootAreaFraction = 0.5487673f;
    internal const float WingInnerAreaFraction = 0.3027565f;
    internal const float WingOuterAreaFraction = 0.1484762f;
    // Keep the neutral horizontal lift centre a small, explicit distance behind
    // the dry centre of mass. The 0.28 m target is five percent of the aircraft's
    // approximately 5.6 m reference mean chord: stable enough for the native FBW,
    // without making it spend most of the elevon travel holding pitch trim.
    internal const float TargetPitchStaticMargin = 0.28f;
    internal const float NoseSuspensionTravel = 0.45f;
    internal const float GroundSpawnHeight = 2.35f;
    internal const float NoseGearContactArea = 0.06f;
    internal const float NoseSteeringSpeed = 55f;
    internal const float NoseAligningStrength = 5f;
    // The true mass-weighted CG must use every AeroPart.centerOfMass reference,
    // not the part transform origins. Keep a modest positive tricycle-gear
    // margin: 0.50 m over the 5.665 m wheelbase gives 8.8% static nose load.
    internal const float DryCenterOfMassAheadOfMainGear = 0.50f;
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
        "Aircraft", "FuelTank", "NetworkIdentity", "AircraftNetworkTransform", "TargetCam", "TargetDetector",
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

    internal static Result Assemble(GameObject sourcePrefab, GameObject modelPrefab, string materialsRoot,
        GameObject runtimeUiFallback)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "F117A_Nighthawk";

        Component aircraft = FindComponent(instance, "Aircraft");
        Component weaponManager = FindComponent(instance, "WeaponManager");
        Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
        if (aircraft == null || weaponManager == null || rigidbody == null)
            throw new InvalidOperationException("The Blueprinter reference aircraft is missing required runtime components.");

        StripReferenceArtwork(instance);
        RepairCrewMaterials(instance, materialsRoot);
        StripReferencePhysics(instance);

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
        PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        visual.name = "F117_CentralBody";
        visual.transform.SetParent(instance.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        ConvertMaterials(visual, materialsRoot);
        MeshRenderer cockpitScreenRenderer = CreateCockpitScreenRenderer(visual, materialsRoot);

        Material gearDustMaterial = CreateGearDustMaterial(materialsRoot);
        Component centralPart = ConfigurePhysics(instance, visual, aircraft, rigidbody, gearDustMaterial);
        ConfigureRuntimeSystems(instance, visual, aircraft, centralPart);
        ConfigureCrewAndSensors(instance, visual, aircraft, centralPart, runtimeUiFallback, cockpitScreenRenderer);
        ConfigureCanopy(instance, visual, aircraft, centralPart);
        ConfigureWeapons(instance, visual, weaponManager, centralPart);
        ConfigureRendererLists(visual, aircraft);
        PruneDonorScaffold(instance, visual);
        CreateParadeFlagOverlays(visual, materialsRoot);

        Transform chute = FindDeep(visual.transform, "F117_DragChute");
        Renderer[] allVisualRenderers = visual.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => chute == null || !renderer.transform.IsChildOf(chute))
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
            return transform == visual.transform || transform.IsChildOf(visual.transform);
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
                    if (string.Equals(source.name, "INT_CockpitFrame", StringComparison.OrdinalIgnoreCase))
                    {
                        // FBX does not preserve the source Multiply node. Transfer
                        // its evaluated #000000 result while retaining the maps.
                        target.SetColor("_BaseColor", Color.black);
                        target.SetColor("_Color", Color.black);
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

        Mesh screenMesh = UnityEngine.Object.Instantiate(imported);
        screenMesh.name = "F117_Tacscreen_Mesh";
        screenMesh.subMeshCount = 1;
        screenMesh.SetTriangles(screenTriangles, 0, false);
        screenMesh.RecalculateBounds();
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

    private static void CreateParadeFlagOverlays(GameObject visual, string materialsRoot)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ParadeFlagTexturePath);
        if (texture == null)
            throw new InvalidOperationException("The deterministic F-117 parade-flag texture is unavailable.");

        string[] gearDoorHingeNames =
        {
            "F117_GearDoor_Nose_CloseHinge",
            "F117_GearDoor_Left_Outer_CloseHinge", "F117_GearDoor_Left_Inner_CloseHinge",
            "F117_GearDoor_Right_Outer_CloseHinge", "F117_GearDoor_Right_Inner_CloseHinge"
        };
        Transform[] gearDoorHinges = gearDoorHingeNames
            .Select(name => FindDeep(visual.transform, name))
            .ToArray();
        if (gearDoorHinges.Any(hinge => hinge == null))
            throw new InvalidOperationException("All five F-117 gear-door close hinges are required for flag projection.");
        Quaternion[] savedRotations = gearDoorHinges.Select(hinge => hinge.localRotation).ToArray();
        try
        {
            // Select and project gear-door triangles in the aerodynamically closed pose.
            // The authored/runtime open rotations are restored even when generation fails.
            foreach (Transform hinge in gearDoorHinges)
                hinge.localRotation = Quaternion.identity;

            MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true)
                .Where(IsParadeFlagSurface)
                .ToArray();
            if (filters.Length == 0)
                throw new InvalidOperationException("No F-117 underside surfaces were found for the parade livery.");

            Bounds planform = new Bounds();
            bool initialized = false;
            foreach (MeshFilter filter in filters)
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = visual.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                if (!initialized)
                {
                    planform = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
                else
                    planform.Encapsulate(point);
            }
            if (!initialized || planform.size.x < 10f || planform.size.z < 15f)
                throw new InvalidOperationException("The F-117 parade projection has invalid planform bounds.");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Universal Render Pipeline/Lit is unavailable for the parade livery.");
            Material material = new Material(shader) { name = "F117_ParadeFlag_Underside" };
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Metallic", 0.88f);
            material.SetFloat("_Smoothness", 0.94f);
            material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = -1;
            AssetDatabase.CreateAsset(material, materialsRoot + "/F117_ParadeFlag_Underside.mat");

            int overlayCount = 0;
            for (int index = 0; index < filters.Length; index++)
                if (CreateParadeFlagOverlay(visual.transform, filters[index], material, planform,
                    materialsRoot, index))
                    overlayCount++;
            if (overlayCount < 5)
                throw new InvalidOperationException("Too few F-117 underside overlays were generated for the parade livery.");
        }
        finally
        {
            for (int index = 0; index < gearDoorHinges.Length; index++)
                if (gearDoorHinges[index] != null)
                    gearDoorHinges[index].localRotation = savedRotations[index];
        }
    }

    private static bool IsParadeFlagSurface(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
            return false;
        for (Transform current = filter.transform; current != null; current = current.parent)
            if (current.name.IndexOf("Rudder", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        string name = filter.sharedMesh.name ?? filter.name;
        if (name.IndexOf("CollisionMesh", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.StartsWith("F117_RearBody_Skin_01", StringComparison.Ordinal) ||
            name.StartsWith("F117_RearBody_Skin_02", StringComparison.Ordinal))
            return false;
        return name.IndexOf("_Skin_", StringComparison.Ordinal) >= 0 ||
            name.StartsWith("F117_Exterior_Mesh", StringComparison.Ordinal) ||
            name.StartsWith("F117_Elevon_", StringComparison.Ordinal) ||
            name.StartsWith("F117_BayDoor_", StringComparison.Ordinal) ||
            name.StartsWith("F117_GearDoor_", StringComparison.Ordinal) ||
            name.StartsWith("F117_ChuteDoor_", StringComparison.Ordinal);
    }

    private static bool CreateParadeFlagOverlay(Transform visualRoot, MeshFilter filter, Material material,
        Bounds planform, string materialsRoot, int assetIndex)
    {
        Mesh source = filter.sharedMesh;
        Vector3[] sourceVertices = source.vertices;
        Vector3[] sourceNormals = source.normals;
        bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
        // Damage skins are already parented to their owning AeroPart. Keep their flag
        // overlay on that same moving section so a detached wing does not leave an
        // intact flag-shaped ghost attached to the fuselage.
        bool anchorToVisualRoot = source.name.StartsWith("F117_Exterior_Mesh", StringComparison.Ordinal);
        Transform movingAnchor = filter.transform.parent != null ? filter.transform.parent : filter.transform;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            int[] sourceTriangles = source.GetTriangles(subMesh);
            for (int triangle = 0; triangle + 2 < sourceTriangles.Length; triangle += 3)
            {
                int a = sourceTriangles[triangle];
                int b = sourceTriangles[triangle + 1];
                int c = sourceTriangles[triangle + 2];
                Vector3 localFaceNormal = Vector3.Cross(
                    sourceVertices[b] - sourceVertices[a],
                    sourceVertices[c] - sourceVertices[a]).normalized;
                Vector3 localSelectionNormal = hasNormals
                    ? (sourceNormals[a] + sourceNormals[b] + sourceNormals[c]).normalized
                    : localFaceNormal;
                Vector3 rootNormal = visualRoot.InverseTransformDirection(
                    filter.transform.TransformDirection(localSelectionNormal)).normalized;
                if (Vector3.Dot(rootNormal, Vector3.down) < 0.35f)
                    continue;

                int first = vertices.Count;
                foreach (int sourceIndex in new[] { a, b, c })
                {
                    Vector3 localNormal = hasNormals ? sourceNormals[sourceIndex].normalized : localFaceNormal;
                    Vector3 rootPoint = visualRoot.InverseTransformPoint(
                        filter.transform.TransformPoint(sourceVertices[sourceIndex]));
                    Vector3 rootVertexNormal = visualRoot.InverseTransformDirection(
                        filter.transform.TransformDirection(localNormal)).normalized;
                    Vector3 movingPoint = movingAnchor.InverseTransformPoint(
                        filter.transform.TransformPoint(sourceVertices[sourceIndex]));
                    Vector3 movingNormal = movingAnchor.InverseTransformDirection(
                        filter.transform.TransformDirection(localNormal)).normalized;
                    Vector3 movingSurfaceNormal = movingAnchor.InverseTransformDirection(
                        filter.transform.TransformDirection(localSelectionNormal)).normalized;
                    Vector3 overlayPoint = anchorToVisualRoot
                        ? rootPoint + Vector3.down * ParadeFlagSurfaceOffset
                        : movingPoint + movingSurfaceNormal * ParadeFlagSurfaceOffset;
                    float longitudinal = Mathf.InverseLerp(planform.max.z, planform.min.z, rootPoint.z);
                    float spanwise = Mathf.InverseLerp(planform.min.x, planform.max.x, rootPoint.x);
                    vertices.Add(overlayPoint);
                    normals.Add(anchorToVisualRoot ? rootVertexNormal : movingNormal);
                    uvs.Add(new Vector2(longitudinal, spanwise));
                }
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }
        }
        if (triangles.Count == 0)
            return false;

        Mesh overlayMesh = new Mesh
        {
            name = ParadeFlagOverlayPrefix + source.name,
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        overlayMesh.SetVertices(vertices);
        overlayMesh.SetNormals(normals);
        overlayMesh.SetUVs(0, uvs);
        overlayMesh.SetTriangles(triangles, 0, true);
        string safeName = assetIndex.ToString("D2") + "_" + SafeName(source.name);
        AssetDatabase.CreateAsset(overlayMesh, materialsRoot + "/" + safeName + "_ParadeFlag.asset");

        GameObject overlay = new GameObject(ParadeFlagOverlayPrefix + safeName);
        overlay.transform.SetParent(anchorToVisualRoot ? visualRoot : movingAnchor, false);
        MeshFilter overlayFilter = overlay.AddComponent<MeshFilter>();
        overlayFilter.sharedMesh = overlayMesh;
        MeshRenderer overlayRenderer = overlay.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = material;
        overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
        overlayRenderer.receiveShadows = true;
        overlayRenderer.enabled = false;
        return true;
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
                target.SetFloat("_Metallic", 1f);
                target.SetFloat("_Smoothness", 1f);
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

        string sourcePath = AssetDatabase.GetAssetPath(source);
        TextureImporter importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("Damage texture source has no TextureImporter: " + sourcePath);
        bool wasReadable = importer.isReadable;
        if (!wasReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
            source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
        }

        Color32[] clean = source.GetPixels32();
        Color32[] damaged = new Color32[clean.Length];
        int width = source.width;
        int height = source.height;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color color = clean[y * width + x];
            float broad = Mathf.PerlinNoise(x * 0.0217f + 11.3f, y * 0.0191f + 7.9f);
            float fine = Mathf.PerlinNoise(x * 0.113f + 31.7f, y * 0.097f + 19.1f);
            float abrasion = Mathf.SmoothStep(0.38f, 0.82f, broad * 0.72f + fine * 0.28f);
            Color soot = Color.Lerp(new Color(0.018f, 0.016f, 0.014f, color.a),
                new Color(0.20f, 0.13f, 0.075f, color.a), abrasion);
            Color result = Color.Lerp(color * 0.28f, soot, 0.72f);
            result.a = color.a;
            damaged[y * width + x] = result;
        }

        // AircraftSkin blends toward one authored damage texture as HP falls. Add
        // deterministic puncture cores, exposed-metal rims, and soot blooms so gun
        // damage reads as actual pockmarks instead of only a uniform dark tint.
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
                    mark = new Color(0.004f, 0.004f, 0.003f, baseColor.a);
                    blend = 0.98f;
                }
                else if (distance < 0.72f)
                {
                    mark = new Color(0.34f, 0.25f, 0.16f, baseColor.a);
                    blend = 0.78f * (1f - (distance - 0.42f) / 0.30f);
                }
                else
                {
                    mark = new Color(0.012f, 0.010f, 0.008f, baseColor.a);
                    blend = 0.62f * Mathf.Pow(1f - (distance - 0.72f) / 1.68f, 2f);
                }
                Color marked = Color.Lerp(baseColor, mark, Mathf.Clamp01(blend));
                marked.a = baseColor.a;
                damaged[pixel] = marked;
            }
        }

        Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = "F117_" + SafeName(materialName) + "_Damage",
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel
        };
        output.SetPixels32(damaged);
        output.Apply(true, true);
        AssetDatabase.CreateAsset(output, outputPath);

        if (!wasReadable)
        {
            importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
        return output;
    }

    internal static string CanonicalMaterialName(string materialName)
    {
        if (string.IsNullOrEmpty(materialName) || TextureStems.ContainsKey(materialName))
            return materialName;

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
        bool glass = materialName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            materialName.Equals("HUD", StringComparison.OrdinalIgnoreCase);
        bool decal = materialName.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0;
        bool cutout = materialName.IndexOf("landing_gear_knob", StringComparison.OrdinalIgnoreCase) >= 0;

        // The source model uses a single detailed shell instead of separate interior and
        // exterior meshes. Render both faces so looking through the canopy cannot reveal
        // a hollow, transparent aircraft.
        if (target.HasProperty("_Cull"))
            target.SetFloat("_Cull", (float)CullMode.Off);

        if (glass || decal)
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
        Material gearDustMaterial)
    {
        // Working Blueprinter aircraft author one root AeroPart and allow Aircraft.SetComplexPhysics
        // to split its connected child-part graph into rigidbodies. The prefab Rigidbody is only a
        // seed; AircraftDefinition.mass supplies the simple-physics mass at runtime.
        rigidbody.mass = 1f;
        rigidbody.drag = 0f;
        rigidbody.angularDrag = 0.025f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rigidbody.automaticCenterOfMass = true;
        rigidbody.ResetCenterOfMass();
        rigidbody.ResetInertiaTensor();

        // Landing-gear mass is included in the central body. LandingGear.BreakWheel subtracts
        // its serialized mass from this attached part, so counting it outside the AeroPart graph
        // made both the intact and broken-aircraft masses wrong.
        // The complex-physics graph is the aircraft's dry mass. Fuel and selected weapon
        // mounts are added separately by the game when it calculates gross weight.
        // It must therefore add up to the 13,380 kg empty weight, not the 23,814 kg MTOW.
        // BalanceDryCenterOfMass below positions the central body's referenced
        // mass point so the complete graph—not merely its part origins—has the
        // required tricycle-gear margin.
        Component central = ConfigureAeroPart(visual, 6990f, CentralBodyLiftArea, 0.42f, 0, aircraft, rigidbody,
            Locator(visual, "LOC_CenterOfMass"), null, 0f);
        AddDirectBoxCollider(central, new Vector3(0f, 0.08f, 0.4f),
            new Vector3(3.4f, 1.05f, 10.2f));

        Component nose = AddPart(central.transform, "F117_Nose", new Vector3(0f, 0.02f, 5.5f),
            2250f, NoseLiftArea, 0.12f, 0, aircraft, rigidbody, central, 260000f);
        AddDirectBoxCollider(nose, Vector3.zero, new Vector3(2.2f, 0.78f, 4.1f));

        Component rear = AddPart(central.transform, "F117_RearBody", new Vector3(0f, 0.16f, -3.2f),
            1010f, RearBodyLiftArea, 0.24f, 0, aircraft, rigidbody, central, 320000f);
        // The central fuselage collider already covers most of this part. Keep only a
        // compact tail collision volume here so the rear body cannot overlap both wing
        // rigidbodies when Aircraft.SetComplexPhysics splits the graph.
        AddDirectBoxCollider(rear, new Vector3(0f, 0f, -1.35f),
            new Vector3(2.8f, 0.7f, 1.5f));

        // Match the stock aircraft pattern: a strongly attached wing root carries an
        // inner panel, which carries a replaceable outer panel/tip. The measured mass,
        // lift and drag fractions preserve the established whole-wing totals exactly.
        // Part origins use the projected planform centroids, giving detached sections
        // physically meaningful mass points instead of one point for the entire wing.
        Component leftWingRoot = AddPart(central.transform, "F117_Wing_Left_Root",
            new Vector3(-2.40313f, -0.02f, -0.33696f), 785f * WingRootAreaFraction,
            MainWingLiftArea * WingRootAreaFraction, 0.08f * WingRootAreaFraction,
            0, aircraft, rigidbody, central, 360000f);
        Component leftWingInner = AddPart(leftWingRoot.transform, "F117_Wing_Left_Inner",
            new Vector3(-1.71257f, 0f, -2.96440f), 785f * WingInnerAreaFraction,
            MainWingLiftArea * WingInnerAreaFraction, 0.08f * WingInnerAreaFraction,
            0, aircraft, rigidbody, leftWingRoot, 360000f);
        Component leftWingOuter = AddPart(leftWingInner.transform, "F117_Wing_Left_Outer",
            new Vector3(-1.61712f, 0f, -2.78229f), 785f * WingOuterAreaFraction,
            MainWingLiftArea * WingOuterAreaFraction, 0.08f * WingOuterAreaFraction,
            0, aircraft, rigidbody, leftWingInner, 360000f);

        Component rightWingRoot = AddPart(central.transform, "F117_Wing_Right_Root",
            new Vector3(2.40313f, -0.02f, -0.33696f), 785f * WingRootAreaFraction,
            MainWingLiftArea * WingRootAreaFraction, 0.08f * WingRootAreaFraction,
            0, aircraft, rigidbody, central, 360000f);
        Component rightWingInner = AddPart(rightWingRoot.transform, "F117_Wing_Right_Inner",
            new Vector3(1.71257f, 0f, -2.96440f), 785f * WingInnerAreaFraction,
            MainWingLiftArea * WingInnerAreaFraction, 0.08f * WingInnerAreaFraction,
            0, aircraft, rigidbody, rightWingRoot, 360000f);
        Component rightWingOuter = AddPart(rightWingInner.transform, "F117_Wing_Right_Outer",
            new Vector3(1.61712f, 0f, -2.78229f), 785f * WingOuterAreaFraction,
            MainWingLiftArea * WingOuterAreaFraction, 0.08f * WingOuterAreaFraction,
            0, aircraft, rigidbody, rightWingInner, 360000f);

        // The F-117 is a blended lifting body. Preserve the proven total lifting area and
        // aerodynamic centroid, but distribute it across the forebody, center body, rear
        // body and both wings instead of concentrating it in two point forces. These fixed
        // physics axes carry incidence without rotating colliders or depending on renderers.
        BindFixedLiftAxis(central, visual.transform);
        BindFixedLiftAxis(nose, visual.transform);
        BindFixedLiftAxis(rear, visual.transform);
        Component[] fixedLiftParts =
        {
            central, nose, rear,
            leftWingRoot, leftWingInner, leftWingOuter,
            rightWingRoot, rightWingInner, rightWingOuter
        };
        foreach (Component fixedLiftPart in fixedLiftParts)
            BindFixedLiftAxis(fixedLiftPart, visual.transform);

        // The source fixed exterior is one renderer. Leaving it on CentralBody makes
        // detached noses, wings and tails physically real but visually invisible.
        // Split every triangle once and parent each compact renderer to its owning
        // AeroPart, matching the working aircraft damage graph.
        PartitionExteriorForDamage(visual, central, nose, rear,
            leftWingRoot, leftWingInner, leftWingOuter,
            rightWingRoot, rightWingInner, rightWingOuter);

        // The source animation's smaller neutral-to-stop travel is +22.5323 degrees.
        // ControlSurface adds pitch and roll without a final combined clamp, so keep
        // their absolute sum at that geometric limit instead of allowing 33-40 degrees.
        AddControlSurface(visual, "F117_Elevon_L_Inner", 40f, InnerElevonArea, -ElevonPitchTravel, -ElevonRollTravel, 0f, aircraft, rigidbody, leftWingInner, false,
            InnerElevonLeftNeutralCorrection);
        AddControlSurface(visual, "F117_Elevon_R_Inner", 40f, InnerElevonArea, -ElevonPitchTravel, ElevonRollTravel, 0f, aircraft, rigidbody, rightWingInner, false,
            InnerElevonRightNeutralCorrection);
        AddControlSurface(visual, "F117_Elevon_L_Outer", 40f, OuterElevonArea, -ElevonPitchTravel, -ElevonRollTravel, 0f, aircraft, rigidbody, leftWingOuter, false, 0f);
        AddControlSurface(visual, "F117_Elevon_R_Outer", 40f, OuterElevonArea, -ElevonPitchTravel, ElevonRollTravel, 0f, aircraft, rigidbody, rightWingOuter, false, 0f);
        // Both source rudder drivers use the same signed local-Z hinge axis, so their
        // ranges must keep the same sign. Runtime telemetry also proves the global sign:
        // positive yaw rate makes ControlsFilter request negative yaw, and negative
        // travel must produce the opposing tail moment instead of reinforcing the rate.
        AddControlSurface(visual, "F117_Rudder_L", 25f, FullVerticalTailArea, 0f, 0f, RudderYawTravel,
            aircraft, rigidbody, rear, true, 0f);
        AddControlSurface(visual, "F117_Rudder_R", 25f, FullVerticalTailArea, 0f, 0f, RudderYawTravel,
            aircraft, rigidbody, rear, true, 0f);

        // The engines occupy the rear-body collision volume. They must be physical
        // children of that body so each native FixedJoint has enableCollision=false
        // against the part it intersects. Making them siblings under CentralBody lets
        // both engine proxy colliders strike RearCollider during complex-physics startup,
        // which breaks the rear-body joint and drops the complete tail/rudder assembly.
        ConfigureEngines(rear.transform, visual, aircraft, rigidbody, rear);
        ConfigureLandingGear(visual, aircraft, central, gearDustMaterial);
        float dryCenterOfMassZ = BalanceDryCenterOfMass(visual, central);
        BalancePitchStaticMargin(visual, dryCenterOfMassZ, fixedLiftParts);
        // Native bullets and explosions use collider.gameObject.GetComponent<IDamageable>();
        // they do not walk to a parent AeroPart. Every physical part must therefore own
        // its actual hitbox directly, which also gives UnitPart.Awake a finite collisionSize.
        RequireDirectAeroPartColliders(visual);
        return central;
    }

    private static float BalanceDryCenterOfMass(GameObject visual, Component centralPart)
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
        foreach (Component part in visual.GetComponentsInChildren<Component>(true)
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

    private static void BalancePitchStaticMargin(GameObject visual, float dryCenterOfMassZ,
        Component[] fixedLiftParts)
    {
        Component[] pitchControlParts = FindComponents(visual, "ControlSurface")
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
        // Exact common FastBomber1 structural profile. The F-117 uses 17 coherent
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

    private sealed class DamageVertex
    {
        internal Vector3 Position;
        internal Vector3 Normal;
        internal Vector4 Tangent;
        internal Color Color;
        internal readonly Vector4[] Uvs = new Vector4[8];

        internal static DamageVertex Lerp(DamageVertex first, DamageVertex second, float amount)
        {
            Vector3 tangent = Vector3.Lerp(
                new Vector3(first.Tangent.x, first.Tangent.y, first.Tangent.z),
                new Vector3(second.Tangent.x, second.Tangent.y, second.Tangent.z), amount).normalized;
            var result = new DamageVertex
            {
                Position = Vector3.Lerp(first.Position, second.Position, amount),
                Normal = Vector3.Lerp(first.Normal, second.Normal, amount).normalized,
                Tangent = new Vector4(tangent.x, tangent.y, tangent.z,
                    amount < 0.5f ? first.Tangent.w : second.Tangent.w),
                Color = UnityEngine.Color.Lerp(first.Color, second.Color, amount)
            };
            for (int channel = 0; channel < result.Uvs.Length; channel++)
                result.Uvs[channel] = Vector4.Lerp(first.Uvs[channel], second.Uvs[channel], amount);
            return result;
        }
    }

    private sealed class DamageMeshBuilder
    {
        private sealed class VertexComparer : IEqualityComparer<DamageVertex>
        {
            private readonly bool hasNormals;
            private readonly bool hasTangents;
            private readonly bool hasColors;
            private readonly bool[] hasUvs;

            internal VertexComparer(bool hasNormals, bool hasTangents, bool hasColors, bool[] hasUvs)
            {
                this.hasNormals = hasNormals;
                this.hasTangents = hasTangents;
                this.hasColors = hasColors;
                this.hasUvs = hasUvs;
            }

            public bool Equals(DamageVertex first, DamageVertex second)
            {
                if (ReferenceEquals(first, second))
                    return true;
                if (first == null || second == null || !first.Position.Equals(second.Position) ||
                    hasNormals && !first.Normal.Equals(second.Normal) ||
                    hasTangents && !first.Tangent.Equals(second.Tangent) ||
                    hasColors && !first.Color.Equals(second.Color))
                    return false;
                for (int channel = 0; channel < hasUvs.Length; channel++)
                    if (hasUvs[channel] && !first.Uvs[channel].Equals(second.Uvs[channel]))
                        return false;
                return true;
            }

            public int GetHashCode(DamageVertex vertex)
            {
                unchecked
                {
                    int hash = vertex.Position.GetHashCode();
                    if (hasNormals)
                        hash = hash * 397 ^ vertex.Normal.GetHashCode();
                    if (hasTangents)
                        hash = hash * 397 ^ vertex.Tangent.GetHashCode();
                    if (hasColors)
                        hash = hash * 397 ^ vertex.Color.GetHashCode();
                    for (int channel = 0; channel < hasUvs.Length; channel++)
                        if (hasUvs[channel])
                            hash = hash * 397 ^ vertex.Uvs[channel].GetHashCode();
                    return hash;
                }
            }
        }

        private readonly Transform owner;
        private readonly Transform visual;
        private readonly bool hasNormals;
        private readonly bool hasTangents;
        private readonly bool hasColors;
        private readonly bool[] hasUvs;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector4>[] uvs = Enumerable.Range(0, 8).Select(_ => new List<Vector4>()).ToArray();
        private readonly List<int> triangles = new List<int>();
        private readonly Dictionary<DamageVertex, int> vertexIndices;

        internal DamageMeshBuilder(Transform owner, Transform visual, bool hasNormals,
            bool hasTangents, bool hasColors, bool[] hasUvs)
        {
            this.owner = owner;
            this.visual = visual;
            this.hasNormals = hasNormals;
            this.hasTangents = hasTangents;
            this.hasColors = hasColors;
            this.hasUvs = hasUvs;
            vertexIndices = new Dictionary<DamageVertex, int>(
                new VertexComparer(hasNormals, hasTangents, hasColors, hasUvs));
        }

        internal bool IsEmpty => triangles.Count == 0;

        internal void AddPolygon(IReadOnlyList<DamageVertex> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return;
            for (int triangle = 1; triangle < polygon.Count - 1; triangle++)
            {
                AddVertex(polygon[0]);
                AddVertex(polygon[triangle]);
                AddVertex(polygon[triangle + 1]);
            }
        }

        private void AddVertex(DamageVertex vertex)
        {
            if (vertexIndices.TryGetValue(vertex, out int existingIndex))
            {
                triangles.Add(existingIndex);
                return;
            }
            int index = vertices.Count;
            vertexIndices.Add(vertex, index);
            Vector3 worldPoint = visual.TransformPoint(vertex.Position);
            vertices.Add(owner.InverseTransformPoint(worldPoint));
            if (hasNormals)
                normals.Add(owner.InverseTransformDirection(
                    visual.TransformDirection(vertex.Normal)).normalized);
            if (hasTangents)
            {
                Vector3 tangent = owner.InverseTransformDirection(visual.TransformDirection(
                    new Vector3(vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z))).normalized;
                tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, vertex.Tangent.w));
            }
            if (hasColors)
                colors.Add(vertex.Color);
            for (int channel = 0; channel < hasUvs.Length; channel++)
                if (hasUvs[channel])
                    uvs[channel].Add(vertex.Uvs[channel]);
            triangles.Add(index);
        }

        internal Mesh Build(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            if (hasNormals)
                mesh.SetNormals(normals);
            if (hasTangents)
                mesh.SetTangents(tangents);
            if (hasColors)
                mesh.SetColors(colors);
            for (int channel = 0; channel < hasUvs.Length; channel++)
                if (hasUvs[channel])
                    mesh.SetUVs(channel, uvs[channel]);
            mesh.SetTriangles(triangles, 0, true);
            if (!hasNormals)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private static void PartitionExteriorForDamage(GameObject visual, Component central, Component nose,
        Component rear, Component leftRoot, Component leftInner, Component leftOuter,
        Component rightRoot, Component rightInner, Component rightOuter)
    {
        Transform sourceTransform = FindDeep(visual.transform, "F117_Exterior_Mesh");
        MeshFilter sourceFilter = sourceTransform == null ? null : sourceTransform.GetComponent<MeshFilter>();
        MeshRenderer sourceRenderer = sourceTransform == null ? null : sourceTransform.GetComponent<MeshRenderer>();
        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
            throw new InvalidOperationException("The production exterior mesh is unavailable for damage partitioning.");

        Directory.CreateDirectory(DamageMeshRoot);
        Mesh sourceMesh = sourceFilter.sharedMesh;
        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector3[] sourceNormals = sourceMesh.normals;
        Vector4[] sourceTangents = sourceMesh.tangents;
        Color[] sourceColors = sourceMesh.colors;
        var sourceUvs = new List<Vector4>[8];
        bool[] hasUvs = new bool[8];
        for (int channel = 0; channel < sourceUvs.Length; channel++)
        {
            sourceUvs[channel] = new List<Vector4>();
            sourceMesh.GetUVs(channel, sourceUvs[channel]);
            hasUvs[channel] = sourceUvs[channel].Count == sourceMesh.vertexCount;
        }
        bool hasNormals = sourceNormals.Length == sourceMesh.vertexCount;
        bool hasTangents = sourceTangents.Length == sourceMesh.vertexCount;
        bool hasColors = sourceColors.Length == sourceMesh.vertexCount;
        Material[] materials = sourceRenderer.sharedMaterials;
        Component[] owners =
        {
            central, nose, rear,
            leftRoot, leftInner, leftOuter,
            rightRoot, rightInner, rightOuter
        };
        var renderersByOwner = owners.ToDictionary(owner => owner, owner => new List<Renderer>());
        int sourceTriangleCount = 0;
        float sourceArea = 0f;
        float assignedArea = 0f;

        DamageVertex SourceVertex(int index)
        {
            var vertex = new DamageVertex
            {
                Position = visual.transform.InverseTransformPoint(
                    sourceTransform.TransformPoint(sourceVertices[index])),
                Normal = hasNormals
                    ? visual.transform.InverseTransformDirection(
                        sourceTransform.TransformDirection(sourceNormals[index])).normalized
                    : Vector3.up,
                Tangent = hasTangents ? sourceTangents[index] : new Vector4(1f, 0f, 0f, 1f),
                Color = hasColors ? sourceColors[index] : UnityEngine.Color.white
            };
            if (hasTangents)
            {
                Vector3 tangent = visual.transform.InverseTransformDirection(
                    sourceTransform.TransformDirection(new Vector3(
                        sourceTangents[index].x, sourceTangents[index].y, sourceTangents[index].z))).normalized;
                vertex.Tangent = new Vector4(tangent.x, tangent.y, tangent.z, sourceTangents[index].w);
            }
            for (int channel = 0; channel < sourceUvs.Length; channel++)
                if (hasUvs[channel])
                    vertex.Uvs[channel] = sourceUvs[channel][index];
            return vertex;
        }

        for (int subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(subMesh);
            sourceTriangleCount += triangles.Length / 3;
            var builders = owners.ToDictionary(owner => owner,
                owner => new DamageMeshBuilder(owner.transform, visual.transform,
                    hasNormals, hasTangents, hasColors, hasUvs));
            for (int index = 0; index < triangles.Length; index += 3)
            {
                var triangle = new List<DamageVertex>
                {
                    SourceVertex(triangles[index]),
                    SourceVertex(triangles[index + 1]),
                    SourceVertex(triangles[index + 2])
                };
                sourceArea += DamagePolygonArea(triangle);
                assignedArea += AssignDamagePolygon(triangle, builders, central, nose, rear,
                    leftRoot, leftInner, leftOuter, rightRoot, rightInner, rightOuter);
            }

            foreach (Component owner in owners)
            {
                DamageMeshBuilder builder = builders[owner];
                if (builder.IsEmpty)
                    continue;

                Mesh mesh = builder.Build(owner.name + "_Skin_" + subMesh.ToString("D2"));
                AssetDatabase.CreateAsset(mesh, DamageMeshRoot + "/" + mesh.name + ".asset");

                GameObject piece = new GameObject(mesh.name);
                piece.layer = sourceTransform.gameObject.layer;
                piece.transform.SetParent(owner.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = subMesh < materials.Length ? materials[subMesh] : null;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                renderer.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
                renderer.enabled = true;
                renderersByOwner[owner].Add(renderer);
            }
        }

        if (sourceTriangleCount == 0 || sourceArea <= 0f ||
            Mathf.Abs(assignedArea - sourceArea) > sourceArea * 0.0001f)
            throw new InvalidOperationException("Exterior damage partition lost production mesh triangles.");
        foreach (Component owner in owners)
        {
            if (renderersByOwner[owner].Count == 0)
                throw new InvalidOperationException(owner.name + " received no exterior damage renderer.");
            ConfigureDamageRenderers(owner, renderersByOwner[owner]);
        }
        foreach (Component wingSection in new[]
                 {
                     leftRoot, leftInner, leftOuter,
                     rightRoot, rightInner, rightOuter
                 })
            AddDirectPlanformDamageCollider(wingSection, renderersByOwner[wingSection]);

        UnityEngine.Object.DestroyImmediate(sourceRenderer, true);
        UnityEngine.Object.DestroyImmediate(sourceFilter, true);
    }

    private static float AssignDamagePolygon(List<DamageVertex> polygon,
        Dictionary<Component, DamageMeshBuilder> builders, Component central, Component nose,
        Component rear, Component leftRoot, Component leftInner, Component leftOuter,
        Component rightRoot, Component rightInner, Component rightOuter)
    {
        float assignedArea = 0f;
        SplitDamagePolygon(polygon, 2, 4.2f, out List<DamageVertex> belowNose,
            out List<DamageVertex> nosePolygon);
        assignedArea += AddDamagePolygon(builders[nose], nosePolygon);

        SplitDamagePolygon(belowNose, 2, 3.6f, out List<DamageVertex> wingBand,
            out List<DamageVertex> centerBand);
        SplitDamagePolygon(wingBand, 0, 1.65f, out List<DamageVertex> nonPositive,
            out List<DamageVertex> positiveWing);
        SplitDamagePolygon(nonPositive, 0, -1.65f, out List<DamageVertex> negativeWing,
            out List<DamageVertex> centerStrip);

        SplitDamagePolygon(positiveWing, new Vector3(1f, 0f, -WingRootSweep), WingRootMetricCut,
            out List<DamageVertex> positiveRootPolygon,
            out List<DamageVertex> positiveBeyondRoot);
        SplitDamagePolygon(positiveBeyondRoot, new Vector3(1f, 0f, -WingOuterSweep), WingOuterMetricCut,
            out List<DamageVertex> positiveInnerPolygon,
            out List<DamageVertex> positiveOuterPolygon);
        // Unity mirrors the FBX X axis: positive is the authored right wing.
        assignedArea += AddDamagePolygon(builders[rightRoot], positiveRootPolygon);
        assignedArea += AddDamagePolygon(builders[rightInner], positiveInnerPolygon);
        assignedArea += AddDamagePolygon(builders[rightOuter], positiveOuterPolygon);

        SplitDamagePolygon(negativeWing, new Vector3(-1f, 0f, -WingRootSweep), WingRootMetricCut,
            out List<DamageVertex> negativeRootPolygon, out List<DamageVertex> negativeBeyondRoot);
        SplitDamagePolygon(negativeBeyondRoot, new Vector3(-1f, 0f, -WingOuterSweep), WingOuterMetricCut,
            out List<DamageVertex> negativeInnerPolygon, out List<DamageVertex> negativeOuterPolygon);
        assignedArea += AddDamagePolygon(builders[leftRoot], negativeRootPolygon);
        assignedArea += AddDamagePolygon(builders[leftInner], negativeInnerPolygon);
        assignedArea += AddDamagePolygon(builders[leftOuter], negativeOuterPolygon);

        assignedArea += AssignBodyDamagePolygon(centerBand, builders[central], builders[rear]);
        assignedArea += AssignBodyDamagePolygon(centerStrip, builders[central], builders[rear]);
        return assignedArea;
    }

    private static float AssignBodyDamagePolygon(List<DamageVertex> polygon,
        DamageMeshBuilder central, DamageMeshBuilder rear)
    {
        SplitDamagePolygon(polygon, 2, -4.35f, out List<DamageVertex> rearPolygon,
            out List<DamageVertex> centralPolygon);
        return AddDamagePolygon(rear, rearPolygon) + AddDamagePolygon(central, centralPolygon);
    }

    private static float AddDamagePolygon(DamageMeshBuilder builder, List<DamageVertex> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return 0f;
        builder.AddPolygon(polygon);
        return DamagePolygonArea(polygon);
    }

    private static float DamagePolygonArea(IReadOnlyList<DamageVertex> polygon)
    {
        if (polygon == null || polygon.Count < 3)
            return 0f;
        float area = 0f;
        for (int index = 1; index < polygon.Count - 1; index++)
            area += Vector3.Cross(polygon[index].Position - polygon[0].Position,
                polygon[index + 1].Position - polygon[0].Position).magnitude * 0.5f;
        return area;
    }

    private static void SplitDamagePolygon(List<DamageVertex> polygon, int axis, float threshold,
        out List<DamageVertex> low, out List<DamageVertex> high)
    {
        Vector3 normal = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        SplitDamagePolygon(polygon, normal, threshold, out low, out high);
    }

    private static void SplitDamagePolygon(List<DamageVertex> polygon, Vector3 normal, float threshold,
        out List<DamageVertex> low, out List<DamageVertex> high)
    {
        low = ClipDamagePolygon(polygon, normal, threshold, false);
        high = ClipDamagePolygon(polygon, normal, threshold, true);
    }

    private static List<DamageVertex> ClipDamagePolygon(List<DamageVertex> polygon, Vector3 normal,
        float threshold, bool keepHigh)
    {
        var result = new List<DamageVertex>();
        if (polygon == null || polygon.Count == 0)
            return result;
        for (int index = 0; index < polygon.Count; index++)
        {
            DamageVertex current = polygon[index];
            DamageVertex previous = polygon[(index + polygon.Count - 1) % polygon.Count];
            float currentValue = Vector3.Dot(current.Position, normal);
            float previousValue = Vector3.Dot(previous.Position, normal);
            bool currentInside = keepHigh ? currentValue >= threshold : currentValue <= threshold;
            bool previousInside = keepHigh ? previousValue >= threshold : previousValue <= threshold;
            if (currentInside != previousInside)
            {
                float amount = (threshold - previousValue) / (currentValue - previousValue);
                result.Add(DamageVertex.Lerp(previous, current, amount));
            }
            if (currentInside)
                result.Add(current);
        }
        for (int index = result.Count - 1; index >= 0; index--)
        {
            int previous = (index + result.Count - 1) % result.Count;
            if (result.Count > 1 &&
                (result[index].Position - result[previous].Position).sqrMagnitude <= 0.0000000001f)
                result.RemoveAt(index);
        }
        return result;
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

    private static void AddControlSurface(GameObject visual, string name, float mass, float area,
        float pitch, float roll, float yaw, Component aircraft, Rigidbody rigidbody,
        Component connectedPart, bool vertical, float neutralCorrection)
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
        Material gearDustMaterial)
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
            float suspensionTravel = side == "Nose" ? NoseSuspensionTravel : 0.38f;
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
            // The stock skid equation treats ordinary steering and wheel spin-up as
            // a skid on this custom wheel rig. Preserve the gear physics and rolling
            // sound, but silence this false-positive source.
            skidAudio.mute = true;
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
            Set(data, "springRate", side == "Nose" ? 580000f : 1180000f);
            Set(data, "dampingRate", side == "Nose" ? 52000f : 90000f);
            Set(data, "castPoint", castPoint);
            Set(data, "wheelRadius", wheelRadius);
            SetObjectArray(data, "wheels", new UnityEngine.Object[] { wheelProxy });
            Set(data, "axle", axle);
            Set(data, "frictionCoef", side == "Nose" ? 0.82f : 0.88f);
            Set(data, "contactArea", side == "Nose" ? NoseGearContactArea : 0.045f);
            Set(data, "rollingResistance", 0.018f);
            Set(data, "aircraft", aircraft);
            Set(data, "tireNoiseSound", rollingAudio);
            Set(data, "tireSkidSound", skidAudio);
            // The stock equation adds turn slip directly to this floor. -0.8 keeps
            // normal taxi steering silent while retaining audible high-energy skids.
            Set(data, "skidVolumeFloor", -0.8f);
            Set(data, "skidPitchMult", 1f);
            // Allow the complete authored suspension stroke; automatic low-speed
            // braking must not cross LandingGear's wheel-break threshold.
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
            Set(data, "steeringLock", steering ? 12f : 0f);
            Set(data, "steeringSpeed", steering ? NoseSteeringSpeed : 0f);
            Set(data, "aligningStrength", steering ? NoseAligningStrength : 0f);
            Set(data, "differentialBrakeFactor", 0f);
            Set(data, "dust", dust);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        AudioSource CreateTireAudio(GameObject host)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0.35f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = 180f;
            source.volume = 0f;
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
            .Select(side => (UnityEngine.Object)FindDeep(visual.transform, "F117_Engine_" + side)?.gameObject)
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
        Transform canopyGroup = FindDeep(visual.transform, "F117_Canopy");
        Transform chuteGroup = FindDeep(visual.transform, "F117_DragChute");
        Renderer[] cockpit = cockpitGroup == null
            ? Array.Empty<Renderer>()
            : cockpitGroup.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name != "F117_Cockpit_Mesh")
                .ToArray();
        Renderer[] exteriorCockpit = canopyGroup == null
            ? Array.Empty<Renderer>()
            : canopyGroup.GetComponentsInChildren<Renderer>(true);

        // Keep the detailed tub visible in both camera modes. Only auxiliary cockpit
        // widgets and the dedicated external canopy participate in camera switching.
        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            if (!(renderer is ParticleSystemRenderer))
                renderer.enabled = true;
        if (chuteGroup != null)
            chuteGroup.gameObject.SetActive(false);

        SerializedObject data = new SerializedObject(aircraft);
        SetObjectArray(data, "cockpitRenderers", cockpit.Cast<UnityEngine.Object>().ToArray());
        SetObjectArray(data, "exteriorRenderers", exteriorCockpit.Cast<UnityEngine.Object>().ToArray());
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
