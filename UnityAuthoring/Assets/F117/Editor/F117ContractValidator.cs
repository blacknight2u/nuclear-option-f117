using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class F117ContractValidator
{
    private const string PrefabPath = "Assets/F117/Generated/F117A_Nighthawk.prefab";
    private const string ModelPath = "Assets/F117/Models/F117_Production.fbx";
    private const string DefinitionPath = "Assets/F117/Generated/F117A_Nighthawk_Definition.asset";
    private const string LiveryPath = "Assets/F117/Generated/F117A_Nighthawk_Livery.asset";
    private static readonly string[] ParadeLiveryPaths =
    {
        "Assets/F117/Generated/F117A_ParadeFlag_SmokedChrome_Livery.asset",
        "Assets/F117/Generated/F117A_ParadeFlag_MatteBlack_Livery.asset"
    };
    private static readonly string[] ParadeLiveryDisplayNames =
    {
        "Farewell Flag - Smoked Chrome",
        "Farewell Flag - Matte Black"
    };
    private const string ParadeFlagTexturePath = "Assets/F117/Textures/F117_ParadeFlag.png";
    private const string ParadeFlagWrapTexturePath = "Assets/F117/Textures/F117_ParadeFlag_Wrap.png";
    private const string GeneratedMaterialsRoot = "Assets/F117/Generated/Materials";
    // Independently hashed from the approved source files, not emitted by the
    // overlay generator or read back from generated assets.
    private const string ParadeFlagTextureSha256 =
        "94e494c455362a6a11e5858ed4f1d5225245ef87829acdd5620797a08c97cd51";
    private const string ParadeFlagWrapTextureSha256 =
        "2f3cbd242f28a04e73c3c34b6bf9aa88ecebf99efecc3319e2f2270925fea172";
    private const string ParadeFlagFbxEligibleMultisetSha256 =
        "09cb0dc46cd397855cc1bb86ce274a18c3126aecdbcec9f02bfa3f7027a8f240";
    private const string ParadeFlagFbxEligibleSetSha256 =
        "cc3ab67a2c70d75c65ed160dd9aa087b1ae3b93dca26bd362d896c9476f1412a";
    private const string ParadeFlagFbxDownwardSha256 =
        "13965313b313bc3761ba7aa933d09cf64908be072d940e405bdf26b20f0fc834";
    // Clipping and 10 um welding can rotate tiny emitted facets by up to five
    // percentage points while all of their points remain on a >=0.75 source face.
    private const double ParadeFlagOutputMinimumDownwardDot = 0.70d;
    private const string MirrorFinishTexturePath = "Assets/F117/Textures/F117_Mirror_MS.png";
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
    private static readonly string[] MatteFinishTextureGuids =
    {
        "d7f117a0f1a94f40b5b4cdb16ac5e601", "d7f117a0f1a94f40b5b4cdb16ac5e602",
        "d7f117a0f1a94f40b5b4cdb16ac5e603", "d7f117a0f1a94f40b5b4cdb16ac5e604",
        "d7f117a0f1a94f40b5b4cdb16ac5e605", "d7f117a0f1a94f40b5b4cdb16ac5e606",
        "d7f117a0f1a94f40b5b4cdb16ac5e607"
    };

    private const string MirrorFinishTextureGuid = "d7f117a0f1a94f40b5b4cdb16ac5e608";
    private const string StatusPath = "Assets/F117/Generated/F117A_Nighthawk_StatusDisplay.prefab";
    private const string RadarChaffPrefabPath = "Assets/F117/Generated/F117_RadarChaff.prefab";
    private const string ManifestPath = "Assets/F117/Generated/patch_manifest.json";
    private static string ReportPath => Path.Combine(
        Application.dataPath, "F117", "Generated", "Reports", "F117_Contract_Validation.txt");

    [MenuItem("F-117A Nighthawk/Validate Runtime Contract")]
    public static void Validate()
    {
        var failures = new List<string>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Require(prefab != null, "Generated aircraft prefab loads", failures);
        if (prefab == null)
            Finish(failures, new List<string>());

        Component[] rawComponents = prefab.GetComponentsInChildren<Component>(true);
        Require(rawComponents.All(component => component != null), "No missing-script components", failures);
        Component[] components = rawComponents.Where(component => component != null).ToArray();
        var notes = new List<string>();

        Component aircraft = Single(components, "Aircraft", failures);
        Component manager = Single(components, "WeaponManager", failures);
        Component[] aeroParts = OfType(components, "AeroPart");
        Component[] controls = OfType(components, "ControlSurface");
        Component[] gears = OfType(components, "LandingGear");
        Component[] turbojets = OfType(components, "Turbojet");
        Component[] nozzles = OfType(components, "JetNozzle");
        float groundSpawnHeight = 0f;
        var expectedRuntimeCounts = new Dictionary<string, int>
        {
            { "Aircraft", 1 }, { "AutopilotPlane", 1 }, { "AeroPart", 11 }, { "BayDoor", 2 },
            { "Canopy", 1 }, { "Cockpit", 1 }, { "ControlsFilter", 1 }, { "ControlSurface", 6 },
            { "ChaffEjector", 1 }, { "FlareEjector", 1 }, { "FuelTank", 1 }, { "JetNozzle", 2 },
            { "LandingGear", 3 }, { "LaserDesignator", 1 },
            { "Mirage.NetworkIdentity", 1 },
            { "NuclearOption.NetworkTransforms.AircraftNetworkTransform", 1 },
            { "Pilot", 1 }, { "PowerSupply", 1 }, { "RadarLocator", 1 }, { "TargetCam", 1 },
            { "TargetDetector", 1 }, { "Turbojet", 2 }, { "WeaponManager", 1 }
        };
        foreach (KeyValuePair<string, int> expected in expectedRuntimeCounts)
            Require(components.Count(component => component.GetType().FullName == expected.Key || component.GetType().Name == expected.Key) == expected.Value,
                "Exactly " + expected.Value + " " + expected.Key, failures);
        var allowedRuntimeTypes = new HashSet<string>(expectedRuntimeCounts.Keys, StringComparer.Ordinal);
        foreach (Component component in components)
        {
            Type type = component.GetType();
            string fullName = type.FullName ?? type.Name;
            string ns = type.Namespace ?? string.Empty;
            if (!ns.StartsWith("UnityEngine", StringComparison.Ordinal) && !allowedRuntimeTypes.Contains(fullName) && !allowedRuntimeTypes.Contains(type.Name))
                failures.Add("Unexpected inherited runtime component: " + fullName + " on " + GetPath(prefab.transform, component.transform));
        }

        Require(prefab.name == "F117A_Nighthawk", "Aircraft root retains the production prefab identity", failures);
        string[] donorNames = prefab.GetComponentsInChildren<Transform>(true)
            .Where(transform => transform.name.IndexOf("Aryx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                transform.name.IndexOf("F16", StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(transform => GetPath(prefab.transform, transform))
            .ToArray();
        Require(donorNames.Length == 0, "No donor F-16 object names remain" +
            (donorNames.Length == 0 ? string.Empty : ": " + string.Join(", ", donorNames)), failures);
        var donorAssetReferences = new List<string>();
        foreach (Component component in components)
        {
            SerializedObject serialized = new SerializedObject(component);
            SerializedProperty iterator = serialized.GetIterator();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference || iterator.objectReferenceValue == null)
                    continue;
                string assetPath = AssetDatabase.GetAssetPath(iterator.objectReferenceValue);
                bool approvedNativeUi = assetPath.EndsWith("/Aryx_F16M_TacScreen.prefab", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(assetPath) &&
                    !approvedNativeUi &&
                    assetPath.IndexOf("Assets/blueprinter/aryx/", StringComparison.OrdinalIgnoreCase) >= 0)
                    donorAssetReferences.Add(GetPath(prefab.transform, component.transform) + " | " +
                        component.GetType().Name + "." + iterator.propertyPath + " -> " + assetPath);
            }
        }
        Require(donorAssetReferences.Count == 0, "No serialized reference points back into the donor F-16 assets" +
            (donorAssetReferences.Count == 0 ? string.Empty : ": " + string.Join(", ", donorAssetReferences)), failures);
        Transform productionVisual = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "F117_Visual");
        Component pilot = components.FirstOrDefault(component => component.GetType().Name == "Pilot");
        bool IsProductionGeometry(Transform transform)
        {
            if (productionVisual != null &&
                (transform == productionVisual || transform.IsChildOf(productionVisual)))
                return true;
            for (Transform current = transform; current != null && current != prefab.transform;
                 current = current.parent)
                if (current.GetComponent("AeroPart") != null)
                    return true;
            return transform.parent == prefab.transform &&
                   transform.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                       StringComparison.Ordinal);
        }
        string[] donorArtwork = prefab.GetComponentsInChildren<Component>(true)
            .Where(component => component is Renderer || component is MeshFilter || component is LODGroup || component is ParticleSystemRenderer)
            .Where(component => !IsProductionGeometry(component.transform))
            .Where(component => pilot == null ||
                                (component.transform != pilot.transform && !component.transform.IsChildOf(pilot.transform)))
            .Select(component => GetPath(prefab.transform, component.transform) + " | " + component.GetType().Name)
            .ToArray();
        Require(donorArtwork.Length == 0, "No hidden donor artwork components remain" +
            (donorArtwork.Length == 0 ? string.Empty : ": " + string.Join(", ", donorArtwork)), failures);
        UnityEngine.Object definition = AssetDatabase.LoadMainAssetAtPath(DefinitionPath);
        Require(definition != null, "Generated aircraft definition loads", failures);
        if (definition != null)
        {
            SerializedObject definitionData = new SerializedObject(definition);
            SerializedProperty spawnOffset = Property(definitionData, "spawnOffset", definition.name, failures);
            if (spawnOffset != null)
                groundSpawnHeight = spawnOffset.vector3Value.y;
            Require(Near(groundSpawnHeight, F117AircraftAssembler.GroundSpawnHeight, 0.001f),
                "Ground spawn height clears the frame-81 deployed main-tire plane", failures);
            Require(Near(Float(definitionData, "radarSize", definition.name, failures), 0.0000005f, 0.00000001f),
                "Closed clean-aircraft definition has the nonzero 0.0000005 radar return", failures);
            Require(Near(Float(definitionData, "visibleRange", definition.name, failures), 2500f, 0.01f),
                "Aircraft optical visibility is 2.5 km", failures);
            Require(Near(Float(definitionData, "mapIconSize", definition.name, failures), 1.30f, 0.001f),
                "F-117 tactical-map silhouette compensates for its transparent canvas padding", failures);
            Require(Near(Float(definitionData, "mass", definition.name, failures), 13380f, 0.1f),
                "Simple-physics mass is the 13,380 kg dry weight", failures);
            Require(Near(Float(definitionData, "value", definition.name, failures), 120f, 0.001f),
                "Aircraft purchase price is $120 million", failures);
            SerializedProperty aircraftInfo = Property(definitionData, "aircraftInfo", definition.name, failures);
            if (aircraftInfo != null)
            {
                SerializedProperty emptyWeight = aircraftInfo.FindPropertyRelative("emptyWeight");
                SerializedProperty maxWeight = aircraftInfo.FindPropertyRelative("maxWeight");
                Require(emptyWeight != null && Near(emptyWeight.floatValue, 13380f, 0.1f),
                    "Aircraft information reports the 13,380 kg empty weight", failures);
                Require(maxWeight != null && Near(maxWeight.floatValue, 23814f, 0.1f),
                    "Aircraft information reports the 23,814 kg maximum takeoff weight", failures);
            }
            foreach (string field in new[] { "friendlyIcon", "hostileIcon", "mapIcon", "aircraftParameters" })
                RequireRef(definitionData, field, definition.name, failures);

            UnityEngine.Object parameters = Ref(definitionData, "aircraftParameters");
            if (parameters != null)
            {
                SerializedObject parameterData = new SerializedObject(parameters);
                SerializedProperty rankRequired = Property(parameterData, "rankRequired", parameters.name, failures);
                Require(rankRequired != null && rankRequired.intValue == 4,
                    "Aircraft requires rank 4", failures);
                RequireRef(parameterData, "HUDExtras", parameters.name, failures);
                RequireArray(parameterData, "loadouts", F117Builder.WeaponLoadoutCount, parameters.name, failures);
                SerializedProperty loadouts = Property(parameterData, "loadouts", parameters.name, failures);
                if (loadouts != null && loadouts.isArray)
                    for (int index = 0; index < loadouts.arraySize; index++)
                    {
                        SerializedProperty weapons = loadouts.GetArrayElementAtIndex(index).FindPropertyRelative("weapons");
                        Require(weapons != null && weapons.arraySize == 3,
                            "Loadout " + index + " covers independent left/right bays and the fixed jammer", failures);
                    }
                SerializedProperty standards = Property(parameterData, "StandardLoadouts", parameters.name, failures);
                Require(standards != null && standards.isArray && standards.arraySize == F117Builder.WeaponLoadoutCount,
                    "Aircraft has one named loadout for every supported internal payload", failures);
                if (standards != null && standards.isArray)
                {
                    for (int index = 0; index < standards.arraySize; index++)
                    {
                        string label = standards.GetArrayElementAtIndex(index).FindPropertyRelative("Name").stringValue;
                        Require(!string.IsNullOrWhiteSpace(label), "Standard loadout " + index + " has a display name", failures);
                        Require(label.IndexOf("operational", StringComparison.OrdinalIgnoreCase) < 0 &&
                            label.IndexOf("proposed", StringComparison.OrdinalIgnoreCase) < 0 &&
                            label.IndexOf("concept", StringComparison.OrdinalIgnoreCase) < 0,
                            "Standard loadout " + index + " has no historical grouping label", failures);
                        SerializedProperty standardWeapons = standards.GetArrayElementAtIndex(index)
                            .FindPropertyRelative("loadout")?.FindPropertyRelative("weapons");
                        Require(standardWeapons != null && standardWeapons.arraySize == 3,
                            "Standard loadout " + index +
                            " covers independent left/right bays and the fixed jammer", failures);
                    }
                }
                SerializedProperty liveries = Property(parameterData, "liveries", parameters.name, failures);
                int expectedLiveryCount = 1 + ParadeLiveryPaths.Length;
                Require(liveries != null && liveries.isArray && liveries.arraySize == expectedLiveryCount,
                    "Aircraft has black plus five photograph-matched farewell-flag finish choices", failures);
                if (liveries != null && liveries.isArray && liveries.arraySize == expectedLiveryCount)
                {
                    string[] names = new[] { "Nighthawk Black" }
                        .Concat(ParadeLiveryDisplayNames).ToArray();
                    string[] paths = new[] { LiveryPath }.Concat(ParadeLiveryPaths).ToArray();
                    for (int index = 0; index < names.Length; index++)
                    {
                        SerializedProperty entry = liveries.GetArrayElementAtIndex(index);
                        Require(entry.FindPropertyRelative("name").stringValue == names[index],
                            "F-117 livery " + index + " has the expected display name", failures);
                        SerializedProperty reference = entry.FindPropertyRelative("assetReference");
                        string expectedGuid = AssetDatabase.AssetPathToGUID(paths[index]);
                        Require(reference != null && !string.IsNullOrEmpty(expectedGuid) &&
                                reference.FindPropertyRelative("m_AssetGUID").stringValue == expectedGuid,
                            "F-117 livery " + index + " addressable key matches its generated asset", failures);
                    }
                }
                SerializedProperty airfoils = Property(parameterData, "airfoils", parameters.name, failures);
                Require(airfoils != null && airfoils.isArray && airfoils.arraySize == 2,
                    "Aircraft has two explicit F-117 airfoil definitions", failures);
                if (airfoils != null && airfoils.isArray && airfoils.arraySize == 2)
                {
                    Require(airfoils.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue == "F117_DeltaWing",
                        "Main airfoil is F-117-specific", failures);
                    Require(airfoils.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue == "F117_ControlSurface",
                        "Control-surface airfoil is F-117-specific", failures);
                }
            }
        }
        foreach (string liveryPath in new[] { LiveryPath }.Concat(ParadeLiveryPaths))
        {
            UnityEngine.Object liveryAsset = AssetDatabase.LoadMainAssetAtPath(liveryPath);
            Require(liveryAsset != null && liveryAsset.GetType().Name == "LiveryData",
                Path.GetFileNameWithoutExtension(liveryPath) + " livery asset loads", failures);
            if (liveryAsset == null)
                continue;
            SerializedObject liveryData = new SerializedObject(liveryAsset);
            SerializedProperty texture = Property(liveryData, "Texture", liveryAsset.name, failures);
            SerializedProperty colors = Property(liveryData, "Colors", liveryAsset.name, failures);
            Require(texture != null && texture.objectReferenceValue == null,
                liveryAsset.name + " cannot replace production textures through the incompatible stock UV path",
                failures);
            Require(colors != null && colors.isArray && colors.arraySize == 0,
                liveryAsset.name + " contains no donor color table", failures);
        }
        Texture2D paradeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ParadeFlagTexturePath);
        Require(paradeTexture != null && paradeTexture.width == 4032 && paradeTexture.height == 2688,
            "Farewell-flag projection texture has the aircraft''s audited 1.500 planform aspect", failures);
        Texture2D paradeWrapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ParadeFlagWrapTexturePath);
        Require(paradeWrapTexture != null && paradeWrapTexture.width == 4032 &&
                paradeWrapTexture.height == 2688,
            "Farewell-flag runtime wrap preserves the 4K master resolution and 1.500 aspect", failures);
        ValidateParadeFlagTextureAssets(failures);
        for (int panel = 1; panel <= 7; panel++)
            ValidateMatteFinishTexture(panel, MatteFinishTextureGuids[panel - 1], failures);
        ValidateMirrorFinishTexture(failures);
        Renderer[] paradeOverlays = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                StringComparison.Ordinal))
            .ToArray();
        string[] expectedParadeOverlayNames = F117AircraftAssembler.ParadeFlagSurfaceNames
            .Select(name => F117AircraftAssembler.ParadeFlagOverlayPrefix + name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] actualParadeOverlayNames = paradeOverlays
            .Select(renderer => renderer.name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Require(paradeOverlays.Length == F117AircraftAssembler.ParadeFlagSurfaceNames.Length,
            "Farewell-flag livery has exactly fourteen bottom-only overlays: three fixed skins, " +
            "two exterior bay doors, four elevons, and five landing-gear doors", failures);
        Require(actualParadeOverlayNames.SequenceEqual(
                expectedParadeOverlayNames, StringComparer.Ordinal),
            "Farewell-flag overlay identities are exactly the fourteen approved owners" +
            (actualParadeOverlayNames.SequenceEqual(expectedParadeOverlayNames,
                StringComparer.Ordinal) ? string.Empty :
                " (actual: " + string.Join(", ", actualParadeOverlayNames) + ")"), failures);
        Require(paradeOverlays.All(renderer => !renderer.enabled),
            "Farewell-flag overlays are disabled for the default black livery", failures);
        Material[] paradeMaterials = paradeOverlays.Select(renderer => renderer.sharedMaterial)
            .Where(material => material != null)
            .Distinct()
            .ToArray();
        Material paradeMaterial = paradeMaterials.Length == 1 ? paradeMaterials[0] : null;
        string defaultFlagPath = GeneratedMaterialsRoot + "/F117_ParadeFlag_PureChrome.asset";
        string defaultFlagDamagePath = GeneratedMaterialsRoot +
            "/F117_ParadeFlag_PureChrome_Damage.asset";
        Require(paradeMaterial != null &&
                string.Equals(paradeMaterial.name, F117AircraftAssembler.ParadeFlagMaterialName,
                    StringComparison.Ordinal) &&
                paradeOverlays.All(renderer => renderer.sharedMaterials.Length == 1 &&
                    renderer.sharedMaterial == paradeMaterial) &&
                AssetDatabase.GetAssetPath(Texture(paradeMaterial, "_BaseMap")) == defaultFlagPath &&
                AssetDatabase.GetAssetPath(Texture(paradeMaterial, "_Basecolor")) == defaultFlagPath &&
                AssetDatabase.GetAssetPath(Texture(paradeMaterial, "_BasecolorDmg")) == defaultFlagDamagePath &&
                AssetDatabase.GetAssetPath(Texture(paradeMaterial, "_Metallic")) == MirrorFinishTexturePath &&
                Texture(paradeMaterial, "_Normal") != null &&
                Texture(paradeMaterial, "_NormalDmg") != null &&
                Texture(paradeMaterial, "_AO") != null &&
                HasSavedMaterialProperty(paradeMaterial, "m_Floats", "_HitPoints") &&
                HasSavedMaterialProperty(paradeMaterial, "m_Floats", "_Glossiness"),
            "Every farewell-flag overlay shares the complete native clean/damaged AircraftSkin contract",
            failures);
        foreach (string key in F117AircraftAssembler.ParadeFlagFinishKeys)
        {
            Texture2D clean = AssetDatabase.LoadAssetAtPath<Texture2D>(
                GeneratedMaterialsRoot + "/F117_ParadeFlag_" + key + ".asset");
            Texture2D damaged = AssetDatabase.LoadAssetAtPath<Texture2D>(
                GeneratedMaterialsRoot + "/F117_ParadeFlag_" + key + "_Damage.asset");
            Require(clean != null && damaged != null && clean.width == 4032 &&
                    clean.height == 2688 && damaged.width == clean.width &&
                    damaged.height == clean.height,
                "Farewell Flag " + key + " has deterministic full-resolution clean and damaged textures",
                failures);
        }
        Require(paradeOverlays.All(renderer => renderer.sharedMaterial != null &&
                renderer.sharedMaterial.HasProperty("_Cull") &&
                Near(renderer.sharedMaterial.GetFloat("_Cull"), (float)CullMode.Back, 0.001f)),
            "Farewell-flag overlays cull their back faces so bay interiors and upper faces stay unpainted",
            failures);
        ValidateParadeFlagOverlays(prefab.transform, productionVisual, paradeOverlays, failures);
        Require(prefab.GetComponent<Rigidbody>() != null, "Aircraft has Rigidbody", failures);
        if (prefab.GetComponent<Rigidbody>() != null)
            Require(Near(prefab.GetComponent<Rigidbody>().mass, 1f, 0.001f),
                "Prefab Rigidbody uses the standard 1 kg seed mass before Aircraft applies its definition", failures);
        Require(OfType(components, "Airbrake").Length == 0,
            "No inherited F-16 Airbrake component or donor speedbrake panels", failures);
        foreach (string chuteNode in new[]
        {
            "F117_DragChute", "F117_ChuteDoor_Left", "F117_ChuteDoor_Right",
            "LOC_ChuteDoor_Left_Open", "LOC_ChuteDoor_Right_Open"
        })
            Require(prefab.GetComponentsInChildren<Transform>(true).Any(transform => transform.name == chuteNode),
                "Drag-chute runtime geometry includes " + chuteNode, failures);
        Require(OfType(components, "NavLights").Length == 0, "No inherited donor navigation-light controller", failures);
        Require(OfType(components, "Radar").Length == 0, "No emitting search radar", failures);
        foreach (string forbidden in new[] { "HighLiftDevice", "BuildingLights", "CockpitWarningLights", "VaporEffect", "SetGlobalParticles" })
            Require(OfType(components, forbidden).Length == 0, "No inherited donor " + forbidden, failures);
        Require(OfType(components, "RadarJammer").Length == 0,
            "No defensive RadarJammer countermeasure remains; active jamming is supplied by JammingPod1", failures);
        foreach (string ejectorType in new[] { "FlareEjector", "ChaffEjector" })
        {
            Component ejector = Single(components, ejectorType, failures);
            if (ejector == null)
                continue;
            SerializedObject data = new SerializedObject(ejector);
            SerializedProperty ammo = Property(data, "ammo", ejectorType, failures);
            int expectedAmmo = ejectorType == "FlareEjector" ? 32 : 64;
            Require(ammo != null && ammo.intValue == expectedAmmo,
                ejectorType + " carries " + expectedAmmo + " rounds", failures);
            Require(Ref(data, "aircraft") == aircraft, ejectorType + " is owned by the F-117 Aircraft", failures);
            SerializedProperty points = Property(data, "ejectionPoints", ejectorType, failures);
            Require(points != null && points.isArray && points.arraySize == 2,
                ejectorType + " has two authored ejection points", failures);
            if (points != null && points.isArray)
                for (int index = 0; index < points.arraySize; index++)
                {
                    SerializedProperty point = points.GetArrayElementAtIndex(index);
                    Require(point.FindPropertyRelative("part").objectReferenceValue != null,
                        ejectorType + " point " + index + " has an owning UnitPart", failures);
                    Require(point.FindPropertyRelative("transform").objectReferenceValue != null,
                        ejectorType + " point " + index + " has an ejection transform", failures);
                }
        }
        GameObject chaffPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RadarChaffPrefabPath);
        Require(chaffPrefab != null && chaffPrefab.GetComponent("RadarChaff") != null,
            "Bundled RadarChaff payload prefab has the native RadarChaff component", failures);
        ParticleSystem chaffParticles = chaffPrefab == null ? null : chaffPrefab.GetComponentInChildren<ParticleSystem>(true);
        ParticleSystemRenderer chaffRenderer = chaffParticles == null ? null : chaffParticles.GetComponent<ParticleSystemRenderer>();
        Require(chaffParticles != null && chaffParticles.main.maxParticles == 128,
            "RadarChaff uses a bounded native particle cloud", failures);
        Require(chaffRenderer != null && chaffRenderer.enabled && !chaffRenderer.forceRenderingOff &&
                chaffRenderer.sharedMaterial != null && chaffRenderer.renderMode == ParticleSystemRenderMode.Stretch,
            "RadarChaff has a visible, material-backed metallic glint effect", failures);
        Component chaffEjector = OfType(components, "ChaffEjector").SingleOrDefault();
        if (chaffEjector != null)
            Require(Ref(new SerializedObject(chaffEjector), "chaffPrefab") == chaffPrefab,
                "ChaffEjector references the bundled RadarChaff payload", failures);
        Component powerSupply = Single(components, "PowerSupply", failures);
        if (powerSupply != null)
        {
            SerializedObject powerData = new SerializedObject(powerSupply);
            SerializedProperty sources = Property(powerData, "powerSources", "PowerSupply", failures);
            Require(sources != null && sources.isArray && sources.arraySize == 2,
                "PowerSupply uses both F-117 engines", failures);
            Require(Near(Float(powerData, "maxCharge", "PowerSupply", failures),
                    F117AircraftAssembler.JammerBusCapacityKj, 0.001f),
                "PowerSupply has the dedicated 60 kJ jammer-burst capacity", failures);
            Require(Near(Float(powerData, "maxPower", "PowerSupply", failures),
                    F117AircraftAssembler.JammerNominalPower, 0.001f),
                "PowerSupply rating matches the native jammer's 13-unit demand", failures);
            Require(Near(Float(powerData, "chargePerRPM", "PowerSupply", failures),
                    F117AircraftAssembler.JammerChargePerEngineRpm, 0.000001f),
                "PowerSupply uses the slow dedicated jammer recharge coefficient", failures);
        }
        MeshCollider[] authoredMeshColliders = prefab.GetComponentsInChildren<MeshCollider>(true);
        MeshCollider[] controlMeshColliders = authoredMeshColliders
            .Where(collider => collider.GetComponent("ControlSurface") != null)
            .ToArray();
        MeshCollider[] wingMeshColliders = authoredMeshColliders
            .Where(collider => collider.GetComponent("AeroPart") != null &&
                collider.name.StartsWith("F117_Wing_", StringComparison.Ordinal))
            .ToArray();
        Require(authoredMeshColliders.Length == 8 &&
                controlMeshColliders.Length == 6 && wingMeshColliders.Length == 2 &&
                authoredMeshColliders.All(collider => collider.convex &&
                    collider.sharedMesh != null && collider.sharedMesh.vertexCount <= 255),
            "Six controls and two whole wings use low-poly convex, directly damage-routable MeshColliders", failures);

        Material[] productionMaterials = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null && AssetDatabase.GetAssetPath(material).StartsWith("Assets/F117/Generated/Materials/", StringComparison.Ordinal))
            .Distinct()
            .ToArray();
        string[] renderersWithMissingMaterials = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            .Where(renderer => renderer.enabled)
            .Where(renderer => renderer.sharedMaterials.Any(material => material == null))
            .Select(renderer => GetPath(prefab.transform, renderer.transform))
            .ToArray();
        Require(renderersWithMissingMaterials.Length == 0,
            "No mesh renderer has a missing material" +
            (renderersWithMissingMaterials.Length == 0 ? string.Empty : ": " + string.Join(", ", renderersWithMissingMaterials)), failures);
        int albedoCount = productionMaterials.Count(material =>
            Texture(material, "_BaseMap", "_Basecolor") != null);
        int compatibilityAlbedoCount = productionMaterials.Count(material =>
            F117AircraftAssembler.UsesAircraftSkin(material.name)
                ? Texture(material, "_Basecolor") != null
                : Texture(material, "_BaseMap") == null || Texture(material, "_MainTex") != null);
        int normalCount = productionMaterials.Count(material =>
            Texture(material, "_BumpMap", "_Normal") != null);
        int maskCount = productionMaterials.Count(material =>
            Texture(material, "_MetallicGlossMap", "_Metallic") != null);
        int emissionCount = productionMaterials.Count(material => Texture(material, "_EmissionMap") != null);
        int bakedLadderTriangleCount = -1;
        Require(albedoCount >= 24, "At least 24 production materials retain albedo textures", failures);
        Require(compatibilityAlbedoCount == productionMaterials.Length,
            "Every production material uses the texture slot required by its runtime shader", failures);
        Require(normalCount >= 22, "At least 22 production materials retain normal maps", failures);
        Require(maskCount >= 22, "At least 22 production materials retain metallic/smoothness maps", failures);
        Require(emissionCount >= 8, "At least 8 cockpit/light materials retain emission maps", failures);
        foreach (string name in new[]
        {
            "F117_EXTERNAL_1", "F117_EXTERNAL_2", "F117_EXTERNAL_3", "F117_EXTERNAL_4",
            "F117_EXTERNAL_5", "F117_EXTERNAL_6", "F117_EXTERNAL_7", "F117_Tires",
            "F117A_external_decals_new"
        })
        {
            Material material = productionMaterials.FirstOrDefault(item => item.name.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            Require(material != null && Texture(material, "_BaseMap", "_Basecolor") != null,
                name + " has a bound production albedo texture", failures);
        }
        Material tireMaterial = productionMaterials.FirstOrDefault(material =>
            material.name.EndsWith("F117_Tires", StringComparison.OrdinalIgnoreCase));
        Require(tireMaterial != null && Texture(tireMaterial, "_MetallicGlossMap") == null &&
                tireMaterial.GetFloat("_Metallic") <= 0.001f &&
                tireMaterial.GetFloat("_Smoothness") <= 0.15f &&
                tireMaterial.GetFloat("_EnvironmentReflections") <= 0.001f &&
                !tireMaterial.shaderKeywords.Contains("_METALLICSPECGLOSSMAP"),
            "Tire rubber is explicitly nonmetallic and cannot inherit a chrome fallback", failures);
        Material exteriorDecalMaterial = productionMaterials.FirstOrDefault(material =>
            material.name.EndsWith("F117A_external_decals_new",
                StringComparison.OrdinalIgnoreCase));
        Renderer[] exteriorDecalRenderers = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.sharedMaterials.Contains(exteriorDecalMaterial))
            .ToArray();
        Require(exteriorDecalMaterial != null &&
                Texture(exteriorDecalMaterial, "_BaseMap") != null &&
                Near(exteriorDecalMaterial.GetFloat("_Surface"), 0f, 0.001f) &&
                Near(exteriorDecalMaterial.GetFloat("_AlphaClip"), 1f, 0.001f) &&
                Near(exteriorDecalMaterial.GetFloat("_Cutoff"), 0.1f, 0.001f) &&
                Near(exteriorDecalMaterial.GetFloat("_ZWrite"), 1f, 0.001f) &&
                Near(exteriorDecalMaterial.GetFloat("_Metallic"), 0f, 0.001f) &&
                Near(exteriorDecalMaterial.GetFloat("_Smoothness"), 0.25f, 0.001f) &&
                Texture(exteriorDecalMaterial, "_MetallicGlossMap") == null &&
                exteriorDecalMaterial.shaderKeywords.Contains("_ALPHATEST_ON") &&
                !exteriorDecalMaterial.shaderKeywords.Contains("_SURFACE_TYPE_TRANSPARENT") &&
                !exteriorDecalMaterial.shaderKeywords.Contains("_ALPHAPREMULTIPLY_ON") &&
                exteriorDecalMaterial.renderQueue == (int)RenderQueue.AlphaTest,
            "External insignia use one matte, depth-writing alpha-cutout material instead " +
            "of a reflective transparent pass", failures);
        Require(exteriorDecalRenderers.Length == 9 &&
                exteriorDecalRenderers.Select(renderer => renderer.name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(new[]
                    {
                        "F117_BayDoor_Left_Mesh",
                        "F117_BayDoor_Right_Mesh",
                        "F117_Exterior_LeftWing_Mesh",
                        "F117_Exterior_Mesh",
                        "F117_Exterior_RightWing_Mesh",
                        "F117_GearDoor_Nose_Mesh",
                        "F117_Gear_Nose_Part_013",
                        "F117_Rudder_L_Mesh",
                        "F117_Rudder_R_Mesh"
                    }, StringComparer.Ordinal),
            "All nine authored airframe, door, gear, and rudder renderers retain their " +
            "external insignia slot",
            failures);
        Material[] exteriorPanelMaterials = productionMaterials
            .Where(material => material.name.IndexOf("F117_EXTERNAL_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        Require(exteriorPanelMaterials.Length >= 7,
            "All exterior panel material families are present", failures);
        foreach (Material material in exteriorPanelMaterials)
        {
            Require(Texture(material, "_Basecolor") != null &&
                    Texture(material, "_BasecolorDmg") != null &&
                    Texture(material, "_Normal") != null && Texture(material, "_Metallic") != null,
                material.name + " resolves Blender duplicate suffixes and retains every production texture binding",
                failures);
        }
        for (int panel = 1; panel <= 7; panel++)
        {
            string family = "F117_EXTERNAL_" + panel;
            string stem = "f117_ext_" + panel;
            Material material = exteriorPanelMaterials.FirstOrDefault(item =>
                item.name.EndsWith(family, StringComparison.OrdinalIgnoreCase));
            Require(material != null, family + " exact texture contract material exists", failures);
            if (material == null)
                continue;
            RequireTexturePath(material, "_Basecolor", "Assets/F117/Textures/" + stem + "_albedo.png", failures);
            RequireTexturePath(material, "_BasecolorDmg",
                "Assets/F117/Generated/Materials/F117_" + family + "_Damage.asset", failures);
            RequireTexturePath(material, "_Normal", "Assets/F117/Textures/" + stem + "_normal.png", failures);
            RequireTexturePath(material, "_NormalDmg", "Assets/F117/Textures/" + stem + "_normal.png", failures);
            RequireTexturePath(material, "_Metallic", "Assets/F117/Textures/" + stem + "_ms.png", failures);
            RequireTexturePath(material, "_AO", "Assets/F117/Textures/" + stem + "_occlusion.png", failures);
            RequireTexturePath(material, "_BaseMap", "Assets/F117/Textures/" + stem + "_albedo.png", failures);
            RequireTexturePath(material, "_MainTex", "Assets/F117/Textures/" + stem + "_albedo.png", failures);
        }
        Require(productionMaterials.All(item =>
                item.name.IndexOf("FORGOTTOTEXTURE", StringComparison.OrdinalIgnoreCase) < 0),
            "No production renderer retains the source placeholder gear material", failures);
        Material cockpitFrame = productionMaterials.FirstOrDefault(material =>
            material.name.EndsWith("INT_CockpitFrame", StringComparison.OrdinalIgnoreCase));
        Require(cockpitFrame != null && cockpitFrame.GetColor("_BaseColor").r <= 0.05f &&
                cockpitFrame.GetColor("_BaseColor").g <= 0.05f &&
                cockpitFrame.GetColor("_BaseColor").b <= 0.05f &&
                Near(cockpitFrame.GetFloat("_Metallic"), 0f, 0.001f) &&
                Near(cockpitFrame.GetFloat("_Smoothness"), 0.5f, 0.001f),
            "Canopy/cockpit frame uses the authored black material instead of the white fallback", failures);
        RequireTexturePath(cockpitFrame, "_MetallicGlossMap",
            "Assets/F117/Textures/metal_paint02_mask.png", failures);
        Require(cockpitFrame != null &&
                cockpitFrame.shaderKeywords.Contains("_METALLICSPECGLOSSMAP"),
            "Canopy frame keeps its authored URP packed-mask keyword for both finish profiles", failures);
        Material[] cockpitStructureMaterials = productionMaterials.Where(material =>
            (material.name.IndexOf("F117_int_", StringComparison.OrdinalIgnoreCase) >= 0 &&
             material.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) < 0) ||
            material.name.EndsWith("INT_CockpitFrame", StringComparison.OrdinalIgnoreCase) ||
            material.name.EndsWith("LIGHTS", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(cockpitStructureMaterials.Length >= 11 &&
                cockpitStructureMaterials.All(material =>
                    Near(material.GetFloat("_Metallic"), 0f, 0.001f) &&
                    Near(material.GetFloat("_Smoothness"), 0.5f, 0.001f)),
            "Cockpit tub and frame preserve the source non-metallic medium-rough finish", failures);
        Renderer canopyRenderer = prefab.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.name == "F117_Canopy_Mesh");
        Material[] canopyMaterials = canopyRenderer == null ? Array.Empty<Material>() : canopyRenderer.sharedMaterials;
        Material[] canopyGlass = canopyMaterials
            .Where(material => material != null && material.name.IndexOf("ext_glass", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        Material[] canopyOpaque = canopyMaterials
            .Where(material => material == null || material.name.IndexOf("ext_glass", StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();
        MeshFilter canopyFilter = canopyRenderer == null ? null : canopyRenderer.GetComponent<MeshFilter>();
        Require(canopyRenderer != null && canopyFilter != null && canopyFilter.sharedMesh != null &&
                canopyFilter.sharedMesh.subMeshCount == 3 && canopyMaterials.Length == 3,
            "Production canopy resolves to one frame and two window material groups after FBX import", failures);
        Require(cockpitFrame != null && canopyOpaque.Length == 1 && canopyOpaque[0] == cockpitFrame,
            "Every opaque canopy face resolves to the exact authored black cockpit-frame material", failures);
        Require(canopyGlass.Length == 2, "Canopy has exactly two clear window slots", failures);
        foreach (Material glass in canopyGlass)
        {
            Color tint = glass.GetColor("_BaseColor");
            Require(Mathf.Abs(tint.r - tint.g) <= 0.01f && Mathf.Abs(tint.g - tint.b) <= 0.01f &&
                    tint.a >= 0.04f && tint.a <= 0.12f,
                glass.name + " is neutral clear glazing rather than blue/tinted glass", failures);
            Require(glass.GetFloat("_Surface") == 1f && glass.GetFloat("_ZWrite") == 0f &&
                    glass.GetFloat("_Metallic") <= 0.01f && glass.GetFloat("_EnvironmentReflections") <= 0.01f,
                glass.name + " uses non-metallic transparent clear-glass rendering", failures);
            Require(Texture(glass, "_BaseMap") == null && Texture(glass, "_MainTex") == null &&
                    Texture(glass, "_BumpMap") == null && Texture(glass, "_MetallicGlossMap") == null &&
                    Texture(glass, "_EmissionMap") == null,
                glass.name + " has no painted or emissive texture capable of recoloring the window", failures);
        }
        Material hudProjector = productionMaterials.FirstOrDefault(material =>
            material.name.EndsWith("HUD", StringComparison.OrdinalIgnoreCase));
        Require(hudProjector != null && hudProjector.GetColor("_BaseColor").a <= 0.08f,
            "HUD projector glass is clear enough not to block the forward view", failures);
        Material hudFrontGlass = productionMaterials.FirstOrDefault(material =>
            material.name.IndexOf("hud_front", StringComparison.OrdinalIgnoreCase) >= 0);
        Require(hudFrontGlass != null && hudFrontGlass.GetColor("_BaseColor").a <= 0.08f,
            "Physical HUD combiner glass is clear enough not to block the forward view", failures);
        Material[] damageSkinMaterials = productionMaterials
            .Where(material => material != null &&
                (F117AircraftAssembler.UsesAircraftSkin(material.name) ||
                 string.Equals(material.name, F117AircraftAssembler.ParadeFlagMaterialName,
                     StringComparison.Ordinal)))
            .Distinct()
            .ToArray();
        Require(damageSkinMaterials.Length >= 8,
            "All seven opaque exterior families and the Farewell Flag use the native AircraftSkin damage contract",
            failures);
        foreach (Material material in damageSkinMaterials)
            Require(material.shader != null &&
                    HasSavedMaterialProperty(material, "m_Floats", "_HitPoints") &&
                    HasSavedMaterialProperty(material, "m_Floats", "_Glossiness") &&
                    Texture(material, "_Basecolor") != null &&
                    Texture(material, "_BasecolorDmg") != null &&
                    Texture(material, "_Normal") != null &&
                    Texture(material, "_NormalDmg") != null &&
                    Texture(material, "_Metallic") != null &&
                    Texture(material, "_AO") != null,
                material.name + " is loadable and has the complete native texture/HP contract", failures);
        ValidateProfileSlotClassification(prefab, failures);

        Require(aeroParts.Length == 11, "Exactly 11 aerodynamic/mass parts", failures);
        Component centralPart = aeroParts.FirstOrDefault(part => part.transform == prefab.transform);
        Require(centralPart != null && prefab.GetComponent("AeroPart") == centralPart,
            "Aircraft root owns the authoritative F117_CentralBody AeroPart required by stock AI taxi states", failures);
        Component leftWingPart = aeroParts.FirstOrDefault(part => part.name == "F117_Wing_Left");
        Component rightWingPart = aeroParts.FirstOrDefault(part => part.name == "F117_Wing_Right");
        Component[] fixedAirframeParts = aeroParts.Where(part =>
                part.GetComponent("ControlSurface") == null &&
                !part.name.StartsWith("F117_Engine_", StringComparison.Ordinal))
            .ToArray();
        Require(fixedAirframeParts.Length == 3 && centralPart != null && leftWingPart != null &&
                rightWingPart != null && fixedAirframeParts.Contains(centralPart) &&
                fixedAirframeParts.Contains(leftWingPart) && fixedAirframeParts.Contains(rightWingPart),
            "Fixed airframe contains only the central root and the two authored whole-wing AeroParts", failures);
        Renderer[] fixedExteriorRenderers = ValidateAuthoredFixedAirframe(
            prefab, centralPart, leftWingPart, rightWingPart, failures);
        ValidateExactDamageRendererOwners(aeroParts, centralPart, leftWingPart,
            rightWingPart, failures);
        string[] forbiddenDamageArtifacts = FindForbiddenDamageArtifacts(prefab);
        Require(forbiddenDamageArtifacts.Length == 0,
            "Generated aircraft contains no _Skin_, _SeamCap, _SeamCaps, or DamageInterior artifacts" +
            (forbiddenDamageArtifacts.Length == 0 ? string.Empty : ": " +
                string.Join(", ", forbiddenDamageArtifacts)), failures);
        float mass = 0f;
        float area = 0f;
        float horizontalLiftArea = 0f;
        float horizontalLiftMomentX = 0f;
        float horizontalLiftMomentZ = 0f;
        Vector3 massMoment = Vector3.zero;
        foreach (Component part in aeroParts)
        {
            SerializedObject data = new SerializedObject(part);
            RequireRef(data, "parentUnit", part.name, failures);
            RequireRef(data, "rb", part.name, failures);
            RequireRef(data, "centerOfMass", part.name, failures);
            Require(!Bool(data, "criticalPart", part.name, failures),
                part.name + " is not an instant-kill AeroPart", failures);
            Require(Near(Float(data, "hitPoints", part.name, failures), 100f, 0.001f),
                part.name + " uses the game-standard 100 HP initialized by UnitPart.Awake", failures);
            Require(Near(Float(data, "structuralThreshold", part.name, failures), -25f, 0.001f),
                part.name + " retains the stock FastBomber structural margin below zero HP", failures);
            SerializedProperty armor = Property(data, "armorProperties", part.name, failures);
            if (armor != null)
            {
                Require(Near(RelativeFloat(armor, "pierceArmor", part.name, failures), 20f, 0.001f) &&
                        Near(RelativeFloat(armor, "blastArmor", part.name, failures), 60f, 0.001f) &&
                        Near(RelativeFloat(armor, "fireArmor", part.name, failures), 0f, 0.001f) &&
                        Near(RelativeFloat(armor, "pierceTolerance", part.name, failures), 5f, 0.001f) &&
                        Near(RelativeFloat(armor, "blastTolerance", part.name, failures), 6f, 0.001f) &&
                        Near(RelativeFloat(armor, "fireTolerance", part.name, failures), 1f, 0.001f) &&
                        Near(RelativeFloat(armor, "overpressureLimit", part.name, failures), 5f, 0.001f),
                    part.name + " uses the audited common FastBomber damage profile", failures);
            }
            bool enginePart = part.name.StartsWith("F117_Engine_", StringComparison.Ordinal);
            SerializedProperty damageMaterial = Property(data, "damageMaterial", part.name, failures);
            SerializedProperty damageRenderers = damageMaterial?.FindPropertyRelative("renderers");
            if (!enginePart)
            {
                Require(damageRenderers != null && damageRenderers.isArray && damageRenderers.arraySize > 0,
                    part.name + " owns at least one visible renderer for native damage and detachment", failures);
                if (damageRenderers != null && damageRenderers.isArray)
                    for (int index = 0; index < damageRenderers.arraySize; index++)
                    {
                        Renderer renderer = damageRenderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
                        Component nearestAeroPart = null;
                        for (Transform current = renderer == null ? null : renderer.transform;
                             current != null && nearestAeroPart == null; current = current.parent)
                            nearestAeroPart = current.GetComponent("AeroPart");
                        Require(renderer != null && nearestAeroPart == part,
                            part.name + " damage renderer " + index + " belongs to that physical AeroPart", failures);
                    }
            }
            float partWingArea = Float(data, "wingArea", part.name, failures);
            Transform serializedLiftNormal = Ref(data, "liftNormal") as Transform;
            bool isControlSurfacePart = part.GetComponent("ControlSurface") != null;
            bool servoLift = serializedLiftNormal != null && serializedLiftNormal.name == "F117_LiftAxis";
            bool fixedLift = serializedLiftNormal != null && serializedLiftNormal.name == "F117_FixedLiftAxis";
            Require(isControlSurfacePart ? servoLift : partWingArea > 0f ? fixedLift : serializedLiftNormal == null,
                part.name + " uses the authored native liftNormal binding contract", failures);
            if (!isControlSurfacePart && partWingArea > 0f && serializedLiftNormal != null)
                Require(Vector3.Dot(serializedLiftNormal.forward, prefab.transform.forward) > 0.99999f &&
                        Vector3.Dot(serializedLiftNormal.right, prefab.transform.right) > 0.99999f,
                    part.name + " uses the aircraft-aligned fixed lift axis proven by working aircraft", failures);
            Component partControl = part.GetComponent("ControlSurface");
            bool pitchSurface = partControl == null;
            if (partControl != null)
            {
                SerializedObject controlData = new SerializedObject(partControl);
                pitchSurface = Mathf.Abs(Float(controlData, "pitchRange", part.name, failures)) > 0.001f;
            }
            if (partWingArea > 0f && serializedLiftNormal != null && pitchSurface)
            {
                Vector3 centerOfLift = Property(data, "centerOfLift", part.name, failures)?.vector3Value ??
                                       Vector3.zero;
                Vector3 forcePoint = serializedLiftNormal.TransformPoint(centerOfLift);
                float forcePointZ = prefab.transform.InverseTransformPoint(forcePoint).z;
                float forcePointX = prefab.transform.InverseTransformPoint(forcePoint).x;
                horizontalLiftArea += partWingArea;
                horizontalLiftMomentX += forcePointX * partWingArea;
                horizontalLiftMomentZ += forcePointZ * partWingArea;
            }
            Require(Near(Float(data, "airflowChanneling", part.name, failures), 0f, 0.0001f),
                part.name + " uses its actual lift-transform angle without artificial airflow alignment", failures);
            Require(part.GetComponent<Collider>() != null &&
                    part.GetType().GetInterfaces().Any(type => type.Name == "IDamageable"),
                part.name + " owns a native-damage-routable collider directly on its AeroPart GameObject", failures);
            Collider rootCollider = part.GetComponent<Collider>();
            BoxCollider rootBox = part.GetComponent<BoxCollider>();
            MeshCollider rootMesh = part.GetComponent<MeshCollider>();
            if (isControlSurfacePart)
            {
                Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => !renderer.name.StartsWith(
                        F117AircraftAssembler.ParadeFlagOverlayPrefix, StringComparison.Ordinal))
                    .ToArray();
                Bounds geometryBounds = F117AircraftAssembler.CalculateRendererGeometryBounds(part.transform, renderers);
                Vector3 expectedSize = Vector3.Max(
                    geometryBounds.size * F117AircraftAssembler.ControlSurfaceColliderInset,
                    Vector3.one * F117AircraftAssembler.ControlSurfaceColliderMinSize);
                Require(rootBox == null && rootMesh != null && rootMesh.convex &&
                        rootMesh.sharedMesh != null && rootMesh.sharedMesh.vertexCount <= 255 &&
                        (rootMesh.sharedMesh.bounds.center - geometryBounds.center).sqrMagnitude <= 0.0001f &&
                        (rootMesh.sharedMesh.bounds.size - expectedSize).sqrMagnitude <= 0.0001f,
                    part.name + " uses an inset convex root-space mesh collider like the working Aryx aircraft", failures);
            }
            else if (part.name.StartsWith("F117_Wing_", StringComparison.Ordinal))
            {
                Require(rootBox == null && rootMesh != null && rootMesh.convex &&
                        rootMesh.sharedMesh != null && rootMesh.sharedMesh.vertexCount >= 6 &&
                        rootMesh.sharedMesh.vertexCount <= 128 && rootMesh.sharedMesh.vertexCount % 2 == 0,
                    part.name + " owns a mesh-derived inset planform damage collider", failures);
                Renderer[] damageOwnedRenderers = damageRenderers == null || !damageRenderers.isArray
                    ? Array.Empty<Renderer>()
                    : Enumerable.Range(0, damageRenderers.arraySize)
                        .Select(index => damageRenderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer)
                        .Where(renderer => renderer != null)
                        .ToArray();
                Renderer[] ownedRenderers = damageOwnedRenderers
                    .Where(renderer => !renderer.name.StartsWith(
                        F117AircraftAssembler.ParadeFlagOverlayPrefix, StringComparison.Ordinal))
                    .ToArray();
                Bounds geometry = F117AircraftAssembler.CalculateRendererGeometryBounds(
                    prefab.transform, ownedRenderers);
                float sideSign = part.name == "F117_Wing_Left" ? -1f : 1f;
                Vector3[] geometryVertices = ownedRenderers
                    .SelectMany(renderer =>
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        return filter == null || filter.sharedMesh == null
                            ? Array.Empty<Vector3>()
                            : filter.sharedMesh.vertices.Select(vertex =>
                                prefab.transform.InverseTransformPoint(
                                    renderer.transform.TransformPoint(vertex)));
                    })
                    .ToArray();
                Vector3 partCenter = prefab.transform.InverseTransformPoint(part.transform.position);
                Vector3 colliderCenter = prefab.transform.InverseTransformPoint(
                    part.transform.TransformPoint(rootMesh.sharedMesh.bounds.center));
                Require((part == leftWingPart || part == rightWingPart) &&
                        damageOwnedRenderers.Length == 2 && ownedRenderers.Length == 1 &&
                        geometryVertices.Length > 0 && geometry.center.x * sideSign > 0f &&
                        partCenter.x * sideSign > 0f && colliderCenter.x * sideSign > 0f,
                    part.name + " physical body, planform collider, and base whole-wing renderer share the same side while its overlay is damage-bound",
                    failures);
                Require(Near(Float(data, "mass", part.name, failures), 785f, 0.1f) &&
                        Near(Float(data, "wingArea", part.name, failures),
                            F117AircraftAssembler.MainWingLiftArea, 0.001f) &&
                        Near(Float(data, "dragArea", part.name, failures), 0.08f, 0.001f),
                    part.name + " carries the exact whole-wing mass, lift, and drag contract", failures);
            }
            else if (part.name.StartsWith("F117_Engine_", StringComparison.Ordinal))
                Require(rootBox != null &&
                        (rootBox.center - F117AircraftAssembler.EngineDamageColliderCenter).sqrMagnitude <= 0.0001f &&
                        (rootBox.size - F117AircraftAssembler.EngineDamageColliderSize).sqrMagnitude <= 0.0001f,
                    part.name + " uses the authored aft nozzle damage collider outside CentralCollider", failures);
            else if (part == centralPart)
            {
                BoxCollider[] centralBoxes = part.GetComponents<BoxCollider>();
                Collider[] centralColliders = part.GetComponents<Collider>();
                Vector3[] expectedCenters =
                {
                    new Vector3(0f, 0.08f, 0.4f),
                    new Vector3(0f, 0.02f, 5.5f),
                    new Vector3(0f, 0.16f, -4.55f)
                };
                Vector3[] expectedSizes =
                {
                    new Vector3(3.4f, 1.05f, 10.2f),
                    new Vector3(2.2f, 0.78f, 4.1f),
                    new Vector3(2.8f, 0.7f, 1.5f)
                };
                bool exactCentralBoxes = centralBoxes.Length == 3 && centralColliders.Length == 3 &&
                    Enumerable.Range(0, expectedCenters.Length).All(index => centralBoxes.Count(box =>
                        (box.center - expectedCenters[index]).sqrMagnitude <= 0.00000001f &&
                        (box.size - expectedSizes[index]).sqrMagnitude <= 0.00000001f) == 1);
                Require(exactCentralBoxes,
                    "Central root owns exactly the three measured direct fuselage damage boxes", failures);
            }
            SerializedProperty joints = Property(data, "joints", part.name, failures);
            if (part == centralPart)
            {
                Require(joints != null && joints.isArray && joints.arraySize == 0,
                    "Root AeroPart has no parent joint", failures);
                Require(Near(Float(data, "mass", part.name, failures),
                            F117AircraftAssembler.DryCentralMass, 0.1f) &&
                        Near(Float(data, "wingArea", part.name, failures), 13.6667f, 0.0001f) &&
                        Near(Float(data, "dragArea", part.name, failures), 0.78f, 0.001f),
                    "Central root carries the exact consolidated mass, lift, and drag contract", failures);
            }
            else
            {
                Component physicalParent = part.transform.parent == null
                    ? null
                    : part.transform.parent.GetComponent("AeroPart");
                Require(physicalParent != null,
                    part.name + " is directly parented to another AeroPart", failures);
                if (part.name.StartsWith("F117_Wing_", StringComparison.Ordinal))
                    Require((part == leftWingPart || part == rightWingPart) &&
                            part.transform.parent == prefab.transform && physicalParent == centralPart,
                        part.name + " is a direct child of the central root at its only authored break seam",
                        failures);
                bool exactJointCount = joints != null && joints.isArray &&
                    (part.name.StartsWith("F117_Wing_", StringComparison.Ordinal)
                        ? joints.arraySize == 1
                        : joints.arraySize >= 1);
                Require(exactJointCount,
                    part.name + (part.name.StartsWith("F117_Wing_", StringComparison.Ordinal)
                        ? " has exactly one central-root attachment joint"
                        : " has a serialized attachment joint"), failures);
                bool linkedToPhysicalParent = false;
                if (joints != null && joints.isArray)
                {
                    for (int index = 0; index < joints.arraySize; index++)
                    {
                        SerializedProperty joint = joints.GetArrayElementAtIndex(index);
                        SerializedProperty connected = joint.FindPropertyRelative("connectedPart");
                        SerializedProperty breakForce = joint.FindPropertyRelative("breakForce");
                        SerializedProperty breakTorque = joint.FindPropertyRelative("breakTorque");
                        linkedToPhysicalParent |= connected != null && connected.objectReferenceValue == physicalParent;
                        Require(connected != null && connected.objectReferenceValue != null,
                            part.name + ".joints[" + index + "] has a connected part", failures);
                        Require(breakForce != null && breakForce.floatValue > 0f &&
                                breakTorque != null && breakTorque.floatValue > 0f,
                            part.name + ".joints[" + index + "] has positive break force and torque", failures);
                        if (isControlSurfacePart)
                            Require(breakForce != null && Near(breakForce.floatValue, 120000f, 0.1f) &&
                                    breakTorque != null && Near(breakTorque.floatValue, 120000f, 0.1f),
                                part.name + ".joints[" + index + "] retains its audited 120 kN attachment", failures);
                    }
                }
                Require(linkedToPhysicalParent,
                    part.name + " joint target matches its transform parent", failures);
            }
            float partMass = Float(data, "mass", part.name, failures);
            mass += partMass;
            Transform partCenterOfMass = Ref(data, "centerOfMass") as Transform;
            if (partCenterOfMass != null)
                massMoment += prefab.transform.InverseTransformPoint(partCenterOfMass.position) * partMass;
            area += partWingArea;
        }
        string[] obsoleteChildHitboxes =
        {
            "CentralCollider", "NoseCollider", "RearCollider", "InnerCollider", "OuterCollider"
        };
        Require(prefab.GetComponentsInChildren<Transform>(true)
                .All(transform => !obsoleteChildHitboxes.Contains(transform.name)),
            "No child-only hitbox can absorb bullets or fragments without routing damage to an AeroPart", failures);
        Require(Near(mass, 13380f, 0.1f), "Connected AeroPart graph totals the 13,380 kg dry mass", failures);
        Vector3 runtimeCenterOfMass = mass > 0f ? massMoment / mass : Vector3.zero;
        float neutralLiftCenterX = horizontalLiftArea > 0f
            ? horizontalLiftMomentX / horizontalLiftArea
            : float.NaN;
        float neutralLiftCenterZ = horizontalLiftArea > 0f
            ? horizontalLiftMomentZ / horizontalLiftArea
            : float.NaN;
        Require(Near(horizontalLiftArea, 73f, 0.02f),
            "Horizontal lifting area totals the established 73.0 m2", failures);
        Require(Near(runtimeCenterOfMass.x, 0f, 0.001f),
            "Dry center of mass is centered laterally", failures);
        const float matchedVariableLoadMass = 9250f; // full 8,250 kg fuel plus matched 1,000 kg stores
        SerializedObject centralPartData = centralPart == null ? null : new SerializedObject(centralPart);
        float centralDryMass = centralPartData == null
            ? float.NaN
            : Float(centralPartData, "mass", centralPart.name, failures);
        Transform centralDryMassPoint = centralPartData == null
            ? null
            : Ref(centralPartData, "centerOfMass") as Transform;
        Vector3 centralDryCenter = centralDryMassPoint == null
            ? new Vector3(float.NaN, float.NaN, float.NaN)
            : prefab.transform.InverseTransformPoint(centralDryMassPoint.position);
        Require(centralDryMassPoint != null &&
                Near(centralDryMass, F117AircraftAssembler.DryCentralMass, 0.001f) &&
                Near(centralDryCenter.x, 0f, 0.0001f) &&
                Near(centralDryCenter.z,
                    F117AircraftAssembler.DryCentralCenterOfMassZ, 0.0001f),
            "Central dry structure retains the mass and station used by the runtime mixture", failures);
        Vector3 variableLoadCenter = new Vector3(
            centralDryCenter.x, centralDryCenter.y,
            F117AircraftAssembler.VariableLoadCenterOfMassZ);
        float matchedCentralMass = centralDryMass + matchedVariableLoadMass;
        Vector3 matchedCentralCenterOfMass =
            (centralDryCenter * centralDryMass + variableLoadCenter * matchedVariableLoadMass) /
            matchedCentralMass;
        Vector3 matchedLoadedMoment = massMoment - centralDryCenter * centralDryMass +
                                      matchedCentralCenterOfMass * matchedCentralMass;
        Vector3 matchedLoadedCenterOfMass = matchedLoadedMoment /
                                            (mass + matchedVariableLoadMass);
        float approvedDryCenterZ = F117AircraftAssembler.MainGearContactZ +
                                   F117AircraftAssembler.DryCenterOfMassAheadOfMainGear;
        float approvedMatchedLoadedCenterZ =
            (mass * approvedDryCenterZ + matchedVariableLoadMass *
                F117AircraftAssembler.VariableLoadCenterOfMassZ) /
            (mass + matchedVariableLoadMass);
        Require(Near(matchedLoadedCenterOfMass.x, 0f, 0.0001f) &&
                Near(matchedLoadedCenterOfMass.z, approvedMatchedLoadedCenterZ, 0.001f),
            "Full fuel plus the matched payload restores the approved loaded CG from authored first moments",
            failures);
        Require(Near(neutralLiftCenterX, 0f, 0.001f),
            "Neutral horizontal lift center is centered laterally", failures);
        float approvedNeutralLiftCenterZ = F117AircraftAssembler.MainGearContactZ +
            F117AircraftAssembler.DryCenterOfMassAheadOfMainGear -
            F117AircraftAssembler.TargetPitchStaticMargin;
        Require(Near(neutralLiftCenterZ, approvedNeutralLiftCenterZ, 0.0001f),
            "Horizontal lift centroid retains its exact approved root-space station", failures);
        Require(Near(runtimeCenterOfMass.z - neutralLiftCenterZ,
                F117AircraftAssembler.TargetPitchStaticMargin, 0.015f),
            "Neutral horizontal lift centre is 0.28 m behind dry CG, preserving pitch authority instead of consuming it as trim",
            failures);
        Transform leftContact = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "LOC_Gear_Left_Contact");
        Transform rightContact = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "LOC_Gear_Right_Contact");
        float mainContactZ = leftContact != null && rightContact != null
            ? (prefab.transform.InverseTransformPoint(leftContact.position).z +
               prefab.transform.InverseTransformPoint(rightContact.position).z) * 0.5f
            : float.NaN;
        Require(leftContact != null && rightContact != null &&
                Near(runtimeCenterOfMass.z - mainContactZ,
                    F117AircraftAssembler.DryCenterOfMassAheadOfMainGear, 0.02f),
            "True mass-weighted centre matches the calibrated main-wheel margin", failures);
        Require(Near(area, 77.449f, 0.02f),
            "Aerodynamic surface area totals 77.449 m2 (62.180 fixed lifting body + 10.820 measured elevon + 4.449 full vertical tails)",
            failures);
        if (aircraft != null)
        {
            SerializedObject aircraftData = new SerializedObject(aircraft);
            RequireRef(aircraftData, "sparksEmitter", aircraft.name, failures);
        }

        Require(controls.Length == 6, "Four elevons plus two rudders", failures);
        foreach (Component control in controls)
        {
            SerializedObject data = new SerializedObject(control);
            RequireRef(data, "attachedSurface", control.name, failures);
            RequireRef(data, "visibleMesh", control.name, failures);
            Component attachedSurface = Ref(data, "attachedSurface") as Component;
            GameObject visibleMesh = Ref(data, "visibleMesh") as GameObject;
            SerializedObject attachedSurfaceData = attachedSurface == null ? null : new SerializedObject(attachedSurface);
            Transform aerodynamicLiftNormal = attachedSurfaceData == null
                ? null
                : Ref(attachedSurfaceData, "liftNormal") as Transform;
            Vector3 centerOfLift = attachedSurfaceData == null
                ? Vector3.zero
                : Property(attachedSurfaceData, "centerOfLift", control.name, failures)?.vector3Value ?? Vector3.zero;
            Vector3 forcePoint = aerodynamicLiftNormal == null
                ? new Vector3(float.NaN, float.NaN, float.NaN)
                : prefab.transform.InverseTransformPoint(
                    aerodynamicLiftNormal.TransformPoint(centerOfLift));
            Vector3 expectedForcePoint;
            switch (control.name)
            {
                case "F117_Elevon_L_Inner":
                    expectedForcePoint = new Vector3(-3.410287f, -0.095724f, -4.367178f);
                    break;
                case "F117_Elevon_R_Inner":
                    expectedForcePoint = new Vector3(3.410287f, -0.095724f, -4.367178f);
                    break;
                case "F117_Elevon_L_Outer":
                    expectedForcePoint = new Vector3(-4.967001f, -0.133974f, -6.458450f);
                    break;
                case "F117_Elevon_R_Outer":
                    expectedForcePoint = new Vector3(4.967001f, -0.133974f, -6.458450f);
                    break;
                case "F117_Rudder_L":
                    expectedForcePoint = new Vector3(-1.042116f, 1.387793f, -8.727183f);
                    break;
                case "F117_Rudder_R":
                    expectedForcePoint = new Vector3(1.044548f, 1.385473f, -8.727262f);
                    break;
                default:
                    expectedForcePoint = new Vector3(float.NaN, float.NaN, float.NaN);
                    break;
            }
            Require(aerodynamicLiftNormal != null &&
                    (forcePoint - expectedForcePoint).sqrMagnitude <= 0.00000001f,
                control.name + " retains its audited root-space aerodynamic force point", failures);
            Require(attachedSurface != null && visibleMesh != null &&
                    visibleMesh != attachedSurface.gameObject &&
                    visibleMesh.transform.parent == attachedSurface.transform &&
                    visibleMesh.GetComponent<Rigidbody>() == null &&
                    visibleMesh.GetComponents<Component>().All(component =>
                        component == null || component.GetType().Name != "AeroPart"),
                control.name + " rotates a child visual pivot, never its AeroPart rigidbody", failures);
            Require(visibleMesh != null && aerodynamicLiftNormal != null &&
                    aerodynamicLiftNormal.name == "F117_LiftAxis" &&
                    aerodynamicLiftNormal.parent == visibleMesh.transform,
                control.name + " carries its aerodynamic lift axis beneath the native servo pivot", failures);
            Require(centerOfLift.sqrMagnitude >= 0.01f,
                control.name + " has a non-zero, model-derived aerodynamic moment arm", failures);
            bool rudder = control.name.IndexOf("Rudder", StringComparison.Ordinal) >= 0;
            if (attachedSurface != null)
            {
                Component structuralParent = attachedSurface.transform.parent == null
                    ? null
                    : attachedSurface.transform.parent.GetComponent("AeroPart");
                if (rudder)
                    Require(structuralParent == centralPart,
                        control.name + " attaches directly to the non-detaching central structure", failures);
                else
                {
                    Component expectedWing = control.name.IndexOf("_L_", StringComparison.Ordinal) >= 0
                        ? leftWingPart
                        : rightWingPart;
                    Require(structuralParent != null && structuralParent == expectedWing,
                        control.name + " attaches directly to its matching whole-wing structure", failures);
                }
            }
            if (rudder)
            {
                Vector3 animatedAxis = visibleMesh == null
                    ? Vector3.zero
                    : visibleMesh.transform.localRotation * Vector3.right;
                Require(animatedAxis.sqrMagnitude > 0f &&
                        Vector3.Dot(animatedAxis.normalized, Vector3.forward) > 0.999f,
                    control.name + " maps the game's local-X animation onto its audited source hinge axis", failures);
                Require(aerodynamicLiftNormal != null &&
                        Vector3.Dot(aerodynamicLiftNormal.right, prefab.transform.up) > 0.99999f,
                    control.name + " keeps its aerodynamic lift axis on aircraft up for lateral tail force",
                    failures);
            }
            float pitchRange = Float(data, "pitchRange", control.name, failures);
            float rollRange = Float(data, "rollRange", control.name, failures);
            float yawRange = Float(data, "yawRange", control.name, failures);
            if (rudder)
            {
                Require(Near(pitchRange, 0f, 0.001f) && Near(rollRange, 0f, 0.001f) &&
                        Near(yawRange, F117AircraftAssembler.RudderYawTravel, 0.01f),
                    control.name + " uses coordinated, feedback-opposing travel on the source model's shared signed hinge axis",
                    failures);
            }
            else
            {
                float expectedRollRange = control.name.IndexOf("_L_", StringComparison.Ordinal) >= 0
                    ? -F117AircraftAssembler.ElevonRollTravel
                    : F117AircraftAssembler.ElevonRollTravel;
                Require(Near(pitchRange, -F117AircraftAssembler.ElevonPitchTravel, 0.01f) &&
                        Near(rollRange, expectedRollRange, 0.01f) &&
                        Near(yawRange, 0f, 0.001f),
                    control.name + " retains the signed 15 pitch and mirrored 7.5 roll authority", failures);
                float expectedArea = control.name.IndexOf("Inner", StringComparison.Ordinal) >= 0
                    ? F117AircraftAssembler.InnerElevonArea
                    : F117AircraftAssembler.OuterElevonArea;
                Require(attachedSurfaceData != null &&
                        Near(Float(attachedSurfaceData, "wingArea", control.name, failures), expectedArea, 0.001f),
                    control.name + " uses its measured production-mesh planform area", failures);
                float expectedNeutral = control.name.IndexOf("Inner", StringComparison.Ordinal) >= 0
                    ? control.name.IndexOf("_L_", StringComparison.Ordinal) >= 0
                        ? F117AircraftAssembler.InnerElevonLeftNeutralCorrection
                        : F117AircraftAssembler.InnerElevonRightNeutralCorrection
                    : 0f;
                Require(aerodynamicLiftNormal != null &&
                        Vector3.Dot(aerodynamicLiftNormal.right, prefab.transform.right) > 0.99999f &&
                        Vector3.Dot(aerodynamicLiftNormal.up, prefab.transform.up) > 0.99999f &&
                        Vector3.Dot(aerodynamicLiftNormal.forward, prefab.transform.forward) > 0.99999f,
                    control.name + " keeps an aircraft-aligned aerodynamic neutral", failures);
                Transform correction = visibleMesh == null
                    ? null
                    : visibleMesh.transform.Find(control.name + "_MeshCorrection");
                float actualVisualCorrection = correction == null
                    ? 0f
                    : Mathf.DeltaAngle(0f, correction.localEulerAngles.x);
                Require(Near(actualVisualCorrection, expectedNeutral, 0.01f) &&
                        ((Mathf.Abs(expectedNeutral) > 0.001f) == (correction != null)),
                    control.name + " keeps its measured cosmetic correction below the servo/aero pivot", failures);
            }
            Require(Near(Float(data, "maxSplit", control.name, failures), 0f, 0.001f), control.name + " has no unconfigured split mode", failures);
        }

        foreach (string section in new[] { "Inner", "Outer" })
        {
            Component leftControl = controls.FirstOrDefault(control =>
                control.name == "F117_Elevon_L_" + section);
            Component rightControl = controls.FirstOrDefault(control =>
                control.name == "F117_Elevon_R_" + section);
            SerializedObject leftControlData = leftControl == null ? null : new SerializedObject(leftControl);
            SerializedObject rightControlData = rightControl == null ? null : new SerializedObject(rightControl);
            GameObject leftVisible = leftControlData == null ? null :
                Ref(leftControlData, "visibleMesh") as GameObject;
            GameObject rightVisible = rightControlData == null ? null :
                Ref(rightControlData, "visibleMesh") as GameObject;
            Component leftPart = leftControlData == null ? null :
                Ref(leftControlData, "attachedSurface") as Component;
            Component rightPart = rightControlData == null ? null :
                Ref(rightControlData, "attachedSurface") as Component;
            SerializedObject leftPartData = leftPart == null ? null : new SerializedObject(leftPart);
            SerializedObject rightPartData = rightPart == null ? null : new SerializedObject(rightPart);
            Transform leftLift = leftPartData == null ? null : Ref(leftPartData, "liftNormal") as Transform;
            Transform rightLift = rightPartData == null ? null : Ref(rightPartData, "liftNormal") as Transform;
            Vector3 leftCenter = leftPartData == null ? Vector3.zero :
                Property(leftPartData, "centerOfLift", leftPart.name, failures)?.vector3Value ?? Vector3.zero;
            Vector3 rightCenter = rightPartData == null ? Vector3.zero :
                Property(rightPartData, "centerOfLift", rightPart.name, failures)?.vector3Value ?? Vector3.zero;
            Vector3 leftAxis = leftVisible == null ? Vector3.zero :
                prefab.transform.InverseTransformDirection(leftVisible.transform.right).normalized;
            Vector3 rightAxis = rightVisible == null ? Vector3.zero :
                prefab.transform.InverseTransformDirection(rightVisible.transform.right).normalized;
            Vector3 rightAxisMirroredToLeft = new Vector3(rightAxis.x, -rightAxis.y, -rightAxis.z);
            Require(leftAxis.sqrMagnitude > 0.99f && rightAxis.sqrMagnitude > 0.99f &&
                    (leftAxis - rightAxisMirroredToLeft).sqrMagnitude <= 0.00000001f,
                section + " elevons use exactly mirrored driven hinge axes", failures);
            Vector3 leftPoint = leftLift == null ? Vector3.zero :
                prefab.transform.InverseTransformPoint(leftLift.TransformPoint(leftCenter));
            Vector3 rightPoint = rightLift == null ? Vector3.zero :
                prefab.transform.InverseTransformPoint(rightLift.TransformPoint(rightCenter));
            Require(leftLift != null && rightLift != null &&
                    Near(leftPoint.x, -rightPoint.x, 0.0001f) &&
                    Near(leftPoint.y, rightPoint.y, 0.0001f) &&
                    Near(leftPoint.z, rightPoint.z, 0.0001f),
                section + " elevons use exactly mirrored aerodynamic force points", failures);
        }

        Collider[] controlHitboxes = controls
            .Select(control => control.GetComponent<Collider>())
            .Where(collider => collider != null)
            .ToArray();
        for (int first = 0; first < controlHitboxes.Length; first++)
        {
            for (int second = first + 1; second < controlHitboxes.Length; second++)
            {
                Collider left = controlHitboxes[first];
                Collider right = controlHitboxes[second];
                if (left.transform.parent != right.transform.parent)
                    continue;
                bool penetrates = Physics.ComputePenetration(
                    left, left.transform.position, left.transform.rotation,
                    right, right.transform.position, right.transform.rotation,
                    out Vector3 _, out float distance);
                Require(!penetrates || distance <= 0.0001f,
                    left.name + " and " + right.name + " sibling hitboxes do not penetrate at neutral", failures);
            }
        }

        Component FindAeroOwner(Collider collider)
        {
            for (Transform current = collider == null ? null : collider.transform; current != null; current = current.parent)
            {
                Component owner = current.GetComponent("AeroPart");
                if (owner != null)
                    return owner;
            }
            return null;
        }
        bool DirectlyConnected(Component firstPart, Component secondPart)
        {
            bool HasConnection(Component sourcePart, Component targetPart)
            {
                SerializedProperty joints = new SerializedObject(sourcePart).FindProperty("joints");
                if (joints == null || !joints.isArray)
                    return false;
                for (int index = 0; index < joints.arraySize; index++)
                    if (joints.GetArrayElementAtIndex(index).FindPropertyRelative("connectedPart").objectReferenceValue == targetPart)
                        return true;
                return false;
            }
            return HasConnection(firstPart, secondPart) || HasConnection(secondPart, firstPart);
        }
        // Physics.ComputePenetration does not reliably evaluate colliders that only
        // exist on an unloaded prefab asset. Audit a temporary live scene instance so
        // the same engine/body penetrations that can tear apart complex physics at
        // spawn are observable before the bundle is built.
        GameObject collisionAuditInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        Require(collisionAuditInstance != null, "Live collider-audit instance can be created", failures);
        Collider[] physicalColliders = Array.Empty<Collider>();
        if (collisionAuditInstance != null)
        {
            collisionAuditInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();
            physicalColliders = collisionAuditInstance.GetComponentsInChildren<Collider>(true)
                .Where(collider => collider != null && collider.enabled && !collider.isTrigger &&
                    collider.gameObject.activeInHierarchy && FindAeroOwner(collider) != null)
                .ToArray();
        }
        for (int first = 0; first < physicalColliders.Length; first++)
        {
            for (int second = first + 1; second < physicalColliders.Length; second++)
            {
                Collider left = physicalColliders[first];
                Collider right = physicalColliders[second];
                Component leftOwner = FindAeroOwner(left);
                Component rightOwner = FindAeroOwner(right);
                if (leftOwner == rightOwner || DirectlyConnected(leftOwner, rightOwner))
                    continue;
                bool penetrates = Physics.ComputePenetration(
                    left, left.transform.position, left.transform.rotation,
                    right, right.transform.position, right.transform.rotation,
                    out Vector3 _, out float distance);
                Require(!penetrates || distance <= 0.0001f,
                    leftOwner.name + "/" + left.name + " does not penetrate unrelated " +
                    rightOwner.name + "/" + right.name + " at spawn (depth=" +
                    distance.ToString("0.0000") + " m)", failures);
            }
        }
        if (collisionAuditInstance != null)
            UnityEngine.Object.DestroyImmediate(collisionAuditInstance);

        Require(gears.Length == 3, "Tricycle landing gear has three assemblies", failures);

        GameObject productionModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Require(productionModel != null, "Production FBX loads for authored gear-contract checks", failures);
        if (productionModel != null)
        {
            string[] gearDoors =
            {
                "F117_GearDoor_Nose", "F117_GearDoor_Left_Outer", "F117_GearDoor_Left_Inner",
                "F117_GearDoor_Right_Outer", "F117_GearDoor_Right_Inner"
            };
            foreach (string doorName in gearDoors)
            {
                Transform door = productionModel.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name == doorName);
                Transform closed = productionModel.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name == "LOC_" + doorName.Substring("F117_".Length) + "_Closed");
                Require(door != null && closed != null && Vector3.Distance(door.position, closed.position) <= 0.001f,
                    doorName + " has a rotation-only closed target at its authored pivot", failures);
            }

            foreach (KeyValuePair<string, int> expected in new Dictionary<string, int>
            {
                { "Nose", 18 }, { "Left", 11 }, { "Right", 10 }
            })
            {
                Transform gearRoot = productionModel.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name == "F117_Gear_" + expected.Key);
                Transform[] descendants = gearRoot == null
                    ? Array.Empty<Transform>()
                    : gearRoot.GetComponentsInChildren<Transform>(true);
                int linkCount = descendants.Count(transform =>
                    transform.name.StartsWith("F117_Gear_" + expected.Key + "_Link_", StringComparison.Ordinal));
                int poseCount = descendants.Count(transform =>
                    transform.name.StartsWith("F117_Gear_" + expected.Key + "_Pose_", StringComparison.Ordinal));
                Require(gearRoot != null && linkCount == expected.Value && poseCount == expected.Value * 9,
                    expected.Key + " gear preserves every source mesh with nine audited linkage poses", failures);

                if (expected.Key != "Nose")
                {
                    int doorTrackCount = descendants.Count(transform =>
                        transform.name.StartsWith(
                            "F117_Gear_" + expected.Key + "_DoorTrack_", StringComparison.Ordinal));
                    int doorGearPoseCount = descendants.Count(transform =>
                        transform.name.StartsWith(
                            "F117_Gear_" + expected.Key + "_DoorGearPose_", StringComparison.Ordinal));
                    int doorClosePoseCount = descendants.Count(transform =>
                        transform.name.StartsWith(
                            "F117_Gear_" + expected.Key + "_DoorClosePose_", StringComparison.Ordinal));
                    Require(doorTrackCount == 2 && doorGearPoseCount == 34 && doorClosePoseCount == 34,
                        expected.Key + " outer-door linkages preserve both source-derived animation stages",
                        failures);
                }
            }

            Transform leftStowed = productionModel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "LOC_Gear_Left_Stowed");
            Transform rightStowed = productionModel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "LOC_Gear_Right_Stowed");
            Transform leftGear = productionModel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "F117_Gear_Left");
            Transform rightGear = productionModel.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "F117_Gear_Right");
            Require(leftStowed != null && rightStowed != null && leftGear != null && rightGear != null &&
                    Near(Quaternion.Angle(leftGear.rotation, leftStowed.rotation),
                        F117AircraftAssembler.MainGearFoldAngle, 0.1f) &&
                    Near(Quaternion.Angle(rightGear.rotation, rightStowed.rotation),
                        F117AircraftAssembler.MainGearFoldAngle, 0.1f),
                "Both main gears preserve the source frame-81 to frame-1 fold magnitude", failures);
        }

        // Magnitude alone cannot distinguish a correct fold from its inverse.
        // Simulate the exact native LandingGear hinge endpoint on a disposable
        // prefab instance and require the visible gear root to land on the
        // signed source target exported from Blender.
        GameObject signedGearInstance = UnityEngine.Object.Instantiate(prefab);
        foreach (string side in new[] { "Nose", "Left", "Right" })
        {
            Component instanceGear = signedGearInstance.GetComponentsInChildren<Component>(true)
                .SingleOrDefault(component => component != null &&
                    component.GetType().Name == "LandingGear" &&
                    component.name.IndexOf(side, StringComparison.OrdinalIgnoreCase) >= 0);
            SerializedObject gearData = instanceGear == null ? null : new SerializedObject(instanceGear);
            Transform hinge = gearData == null ? null : Ref(gearData, "gearHinge") as Transform;
            Transform visualGear = signedGearInstance.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(transform => transform.name == "F117_Gear_" + side);
            Transform signedTarget = signedGearInstance.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(transform => transform.name == "LOC_Gear_" + side + "_Stowed");
            float foldDegrees = gearData == null ? 0f : Float(gearData, "foldDegrees", side + " gear", failures);
            SerializedProperty foldMotion = gearData == null
                ? null
                : Property(gearData, "hingeFoldMotion", side + " gear", failures);
            if (hinge != null && foldMotion != null)
            {
                hinge.localPosition += foldMotion.vector3Value;
                hinge.localRotation = Quaternion.Euler(foldDegrees, 0f, 0f);
            }
            Require(instanceGear != null && hinge != null && visualGear != null && signedTarget != null &&
                    Vector3.Distance(visualGear.position, signedTarget.position) <= 0.001f &&
                    Quaternion.Angle(visualGear.rotation, signedTarget.rotation) <= 0.1f,
                side + " gear native fold reaches the signed source stow transform", failures);
        }
        UnityEngine.Object.DestroyImmediate(signedGearInstance);
        Dictionary<string, Vector3> gearCastEnds = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        Dictionary<string, float> gearSpringRates = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (Component gear in gears)
        {
            SerializedObject data = new SerializedObject(gear);
            foreach (string field in new[] { "attachedPart", "bumpStop", "unsprung", "castPoint", "axle", "aircraft", "tireNoiseSound", "tireSkidSound", "gearCollider", "gearHinge", "strutRotationTransform", "dust" })
                RequireRef(data, field, gear.name, failures);
            RequireArray(data, "wheels", 1, gear.name, failures);
            ParticleSystem dust = Ref(data, "dust") as ParticleSystem;
            AudioSource rollingSource = Ref(data, "tireNoiseSound") as AudioSource;
            AudioSource skidSource = Ref(data, "tireSkidSound") as AudioSource;
            ParticleSystemRenderer dustRenderer = dust == null ? null : dust.GetComponent<ParticleSystemRenderer>();
            Require(dust != null && !dust.emission.enabled && dust.main.maxParticles == 0,
                gear.name + " placeholder dust system cannot emit particles", failures);
            Require(dustRenderer != null && dustRenderer.sharedMaterial != null && !dustRenderer.enabled,
                gear.name + " dust renderer serializes disabled for the runtime safety lock", failures);
            Require(skidSource != null && !skidSource.mute &&
                    Near(Float(data, "skidVolumeFloor", gear.name, failures), -0.4f, 0.001f) &&
                    Near(Float(data, "skidPitchMult", gear.name, failures), 1f, 0.001f),
                gear.name + " uses the unmuted stock skid-audio response", failures);
            foreach (AudioSource source in new[] { rollingSource, skidSource })
            {
                Require(source != null && Near(source.dopplerLevel, 0f, 0.001f) &&
                        Near(source.spread, 24f, 0.001f) &&
                        Near(source.minDistance, 20f, 0.001f) && Near(source.maxDistance, 200f, 0.001f) &&
                        source.rolloffMode == AudioRolloffMode.Custom,
                    gear.name + " tire sources use the stock aircraft spatial-audio profile", failures);
                AnimationCurve rolloff = source == null
                    ? null
                    : source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                Require(rolloff != null && rolloff.length == 5 &&
                        Near(rolloff.keys[0].time, 0f, 0.001f) && Near(rolloff.keys[0].value, 1.00618f, 0.001f) &&
                        Near(rolloff.keys[1].time, 0.2f, 0.001f) && Near(rolloff.keys[1].value, 0.5f, 0.001f) &&
                        Near(rolloff.keys[2].time, 0.4f, 0.001f) && Near(rolloff.keys[2].value, 0.25f, 0.001f) &&
                        Near(rolloff.keys[3].time, 0.8f, 0.001f) && Near(rolloff.keys[3].value, 0.125f, 0.001f) &&
                        Near(rolloff.keys[4].time, 1f, 0.001f) && Near(rolloff.keys[4].value, 0f, 0.001f),
                    gear.name + " tire sources preserve the stock distance rolloff curve", failures);
            }
            Require(Ref(data, "foldSound") == null && Ref(data, "latchSound") == null,
                gear.name + " keeps native gear-motion clips as runtime-patched placeholders", failures);
            Renderer[] gearRenderers = gear.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                .ToArray();
            Require(gear.gameObject.activeSelf && gearRenderers.Length >= 1 &&
                    gearRenderers.All(renderer => renderer.gameObject.activeSelf && renderer.enabled),
                gear.name + " and its visible mesh are active in the deployed prefab state", failures);
            SerializedProperty doors = Property(data, "gearDoors", gear.name, failures);
            Require(doors != null && doors.isArray && doors.arraySize >= 1, gear.name + " has linked door animation", failures);
            SerializedProperty movingParts = Property(data, "movingParts", gear.name, failures);
            Require(movingParts != null && movingParts.isArray && movingParts.arraySize == 0,
                gear.name + " does not misuse the native rotation-only movingParts contract", failures);
            SerializedProperty gearJoints = Property(data, "joints", gear.name, failures);
            Require(gearJoints != null && gearJoints.isArray && gearJoints.arraySize == 0,
                gear.name + " does not invent suspension IK absent from the source model", failures);
            Transform bumpStop = Ref(data, "bumpStop") as GameObject == null ? (Ref(data, "bumpStop") as Transform) : ((GameObject)Ref(data, "bumpStop")).transform;
            Transform unsprung = Ref(data, "unsprung") as GameObject == null ? (Ref(data, "unsprung") as Transform) : ((GameObject)Ref(data, "unsprung")).transform;
            Transform castPoint = Ref(data, "castPoint") as Transform;
            Transform axle = Ref(data, "axle") as Transform;
            Transform gearHinge = Ref(data, "gearHinge") as Transform;
            float suspensionTravel = Float(data, "suspensionTravel", gear.name, failures);
            float maxCompression = Float(data, "maxCompression", gear.name, failures);
            bool noseGear = gear.name.IndexOf("Nose", StringComparison.OrdinalIgnoreCase) >= 0;
            string gearSide = noseGear ? "Nose" :
                (gear.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0 ? "Left" : "Right");
            float expectedSpringRate = noseGear
                ? F117AircraftAssembler.NoseGearSpringRate
                : F117AircraftAssembler.MainGearSpringRate;
            float expectedDampingRate = noseGear
                ? F117AircraftAssembler.NoseGearDampingRate
                : F117AircraftAssembler.MainGearDampingRate;
            Require(Near(Float(data, "springRate", gear.name, failures), expectedSpringRate, 0.01f) &&
                    Near(Float(data, "dampingRate", gear.name, failures), expectedDampingRate, 0.01f),
                gear.name + " uses the mass-distribution-derived spring and damping rates", failures);
            float expectedTireResponse = noseGear
                ? F117AircraftAssembler.NoseTireResponse
                : F117AircraftAssembler.MainTireResponse;
            Require(Near(Float(data, "response", gear.name, failures),
                        expectedTireResponse, 0.0001f) &&
                    Near(Float(data, "rollingResistance", gear.name, failures),
                        F117AircraftAssembler.TireRollingResistance, 0.0001f),
                gear.name + " uses the native tire response and rolling resistance", failures);
            float expectedDifferentialBrake = gearSide == "Left" ? -1f : gearSide == "Right" ? 1f : 0f;
            Require(Near(Float(data, "differentialBrakeFactor", gear.name, failures),
                        expectedDifferentialBrake, 0.0001f),
                gear.name + " uses the stock left/right differential-brake sign", failures);
            gearSpringRates[gearSide] = expectedSpringRate;
            if (noseGear)
            {
                Require(Near(suspensionTravel, F117AircraftAssembler.NoseSuspensionTravel, 0.001f),
                    gear.name + " uses the stock-sized ground-probe travel", failures);
                Require(Near(Float(data, "contactArea", gear.name, failures),
                        F117AircraftAssembler.NoseGearContactArea, 0.001f),
                    gear.name + " has sufficient soft-ground contact area", failures);
                Require(Near(Float(data, "steeringSpeed", gear.name, failures),
                        F117AircraftAssembler.NoseSteeringSpeed, 0.01f) &&
                        Near(Float(data, "aligningStrength", gear.name, failures),
                        F117AircraftAssembler.NoseAligningStrength, 0.01f),
                    gear.name + " uses the stable low-bias steering response", failures);
                Require(Near(Float(data, "steeringLock", gear.name, failures),
                        F117AircraftAssembler.NoseSteeringLock, 0.01f),
                    gear.name + " retains full low-speed taxi steering authority", failures);
            }
            else
                Require(Near(suspensionTravel, F117AircraftAssembler.MainSuspensionTravel, 0.001f),
                    gear.name + " uses the stock-sized ground-probe travel", failures);
            Require(Near(maxCompression, suspensionTravel, 0.001f),
                gear.name + " permits its full suspension stroke before the game's break test", failures);
            Require(gearHinge != null && gear.transform.parent == gearHinge,
                gear.name + " sprung assembly is directly under its fold hinge", failures);
            Require(Vector3.Dot(gear.transform.forward, prefab.transform.forward) > 0.99f,
                gear.name + " LandingGear frame reports forward runway motion as positive ground speed", failures);
            Require(bumpStop != null && unsprung != null &&
                    bumpStop.parent == gear.transform && unsprung.parent == gear.transform,
                gear.name + " bump stop and unsprung assembly are children of LandingGear", failures);
            Require(castPoint != null && Vector3.Dot(castPoint.up, prefab.transform.up) > 0.99f,
                gear.name + " suspension cast axis points down in aircraft space", failures);
            Require(axle != null && Vector3.Dot(axle.forward, prefab.transform.forward) > 0.99f,
                gear.name + " wheel longitudinal axis matches aircraft forward", failures);
            Require(gearHinge != null && Mathf.Abs(Mathf.DeltaAngle(gearHinge.localEulerAngles.x, 0f)) < 0.1f &&
                Mathf.Abs(Mathf.DeltaAngle(gearHinge.localEulerAngles.y, 0f)) < 0.1f &&
                Mathf.Abs(Mathf.DeltaAngle(gearHinge.localEulerAngles.z, 0f)) < 0.1f,
                gear.name + " deployed hinge is zeroed and cannot trigger the game's >10 degree break test", failures);
            if (castPoint != null)
            {
                Vector3 castEnd = castPoint.position - castPoint.up * suspensionTravel;
                Vector3 localCastEnd = prefab.transform.InverseTransformPoint(castEnd);
                gearCastEnds[gearSide] = localCastEnd;
                float spawnCompression = -(localCastEnd.y + groundSpawnHeight);
                Require(Near(localCastEnd.y, F117AircraftAssembler.GearContactPlaneY, 0.0001f) &&
                        Near(spawnCompression, F117AircraftAssembler.GearSpawnCompression, 0.0001f),
                    gear.name + " shares the canonical contact plane with 35.38486 mm spawn preload",
                    failures);
                float expectedX = gearSide == "Nose" ? 0f :
                    (gearSide == "Left" ? -F117AircraftAssembler.MainGearHalfTrack :
                        F117AircraftAssembler.MainGearHalfTrack);
                float expectedZ = gearSide == "Nose"
                    ? F117AircraftAssembler.NoseGearContactZ
                    : F117AircraftAssembler.MainGearContactZ;
                Require(Near(localCastEnd.x, expectedX, 0.0001f) &&
                        Near(localCastEnd.z, expectedZ, 0.0001f),
                    gear.name + " uses the canonical lateral and longitudinal contact coordinates", failures);
                if (gearRenderers.Length > 0)
                {
                    MeshFilter[] gearMeshes = gear.GetComponentsInChildren<MeshFilter>(true)
                        .Where(filter => filter.sharedMesh != null)
                        .ToArray();
                    Require(gearMeshes.Length > 0,
                        gear.name + " visible assembly contains mesh geometry", failures);
                    float renderedBottomY = gearMeshes
                        // A rotated mesh's bounding-box corners can extend below every
                        // real vertex. Measure the serialized geometry itself so this
                        // check describes the tire plane the player will actually see.
                        .SelectMany(filter => filter.sharedMesh.vertices
                            .Select(filter.transform.TransformPoint))
                        .Min(point => prefab.transform.InverseTransformPoint(point).y);
                    Require(Mathf.Abs(renderedBottomY - localCastEnd.y) <= 0.05f,
                        gear.name + " physics contact matches its rendered tire bottom (rendered=" +
                        renderedBottomY.ToString("0.000") + ", contact=" + localCastEnd.y.ToString("0.000") + ")", failures);
                }
            }
        }
        if (gearCastEnds.Count == 3 && gearSpringRates.Count == 3)
        {
            Vector3 noseContact = gearCastEnds["Nose"];
            Vector3 mainLeftContact = gearCastEnds["Left"];
            Vector3 mainRightContact = gearCastEnds["Right"];
            Require(Near(mainLeftContact.x, -mainRightContact.x, 0.0001f) &&
                    Near(mainLeftContact.y, mainRightContact.y, 0.0001f) &&
                    Near(mainLeftContact.z, mainRightContact.z, 0.0001f),
                "Left/right main contacts are exact mirrors about the aircraft centreline", failures);
            float noseForce = gearSpringRates["Nose"] * F117AircraftAssembler.NoseGearDryCompression;
            float leftForce = gearSpringRates["Left"] * F117AircraftAssembler.MainGearDryCompression;
            float rightForce = gearSpringRates["Right"] * F117AircraftAssembler.MainGearDryCompression;
            float dryWeight = 13380f * Mathf.Abs(Physics.gravity.y);
            float totalSupport = noseForce + leftForce + rightForce;
            Require(Mathf.Abs(totalSupport - dryWeight) <= dryWeight * 0.002f,
                "Three-point static spring force balances the 13,380 kg dry weight", failures);
            Require(Near(leftForce, rightForce, 0.01f),
                "Left/right main springs carry exactly equal static load", failures);
            float wheelbase = noseContact.z - (mainLeftContact.z + mainRightContact.z) * 0.5f;
            float noseLoadFraction = noseForce / totalSupport;
            float expectedNoseLoadFraction =
                F117AircraftAssembler.DryCenterOfMassAheadOfMainGear / wheelbase;
            Require(Near(noseLoadFraction, expectedNoseLoadFraction, 0.0001f),
                "Dry nose-wheel load follows the approved CG and measured wheelbase", failures);
            float supportMoment = noseForce * wheelbase;
            float weightMoment = dryWeight * F117AircraftAssembler.DryCenterOfMassAheadOfMainGear;
            Require(Mathf.Abs(supportMoment - weightMoment) <= weightMoment * 0.003f,
                "Static gear forces balance pitch moment about the main-wheel plane", failures);
        }

        Component fuelTank = Single(components, "FuelTank", failures);
        if (fuelTank != null)
        {
            SerializedObject data = new SerializedObject(fuelTank);
            RequireRef(data, "part", fuelTank.name, failures);
            Require(Ref(data, "part") == centralPart,
                "The aggregate fuel tank applies all live fuel mass through the corrected central mixture",
                failures);
            RequireArray(data, "connectedTanks", 0, fuelTank.name, failures);
            Require(Near(Float(data, "fuelCapacity", fuelTank.name, failures), 8250f, 0.1f), "Fuel capacity is 8,250 kg", failures);
            foreach (KeyValuePair<string, float> expected in new Dictionary<string, float>
            {
                { "leakThreshold", 50f }, { "leakPerHP", 0.2f }, { "maxLeakRate", 50f },
                { "ruptureGMin", 50f }, { "ruptureGMax", 500f }, { "ignitionGMin", 50f },
                { "ignitionGMax", 500f }, { "ignitionPierceMin", 2f }, { "ignitionPierceMax", 6f },
                { "ignitionBlastMin", 2f }, { "ignitionBlastMax", 6f }, { "fireIntensity", 3f }
            })
                Require(Near(Float(data, expected.Key, fuelTank.name, failures), expected.Value, 0.0001f),
                    "FuelTank." + expected.Key + " is explicitly configured", failures);
        }

        Component power = Single(components, "PowerSupply", failures);
        if (power != null)
        {
            SerializedObject data = new SerializedObject(power);
            RequireArray(data, "powerSources", 2, power.name, failures);
            RequireRef(data, "source", power.name, failures);
            RequireRef(data, "aircraft", power.name, failures);
            SerializedProperty curve = Property(data, "supplyAtCharge", power.name, failures);
            Require(curve != null && curve.animationCurveValue != null && curve.animationCurveValue.length >= 2,
                "Power supply has a valid output curve", failures);
        }

        Component controlsFilter = Single(components, "ControlsFilter", failures);
        if (controlsFilter != null)
        {
            SerializedObject data = new SerializedObject(controlsFilter);
            foreach (KeyValuePair<string, float> expected in new Dictionary<string, float>
            {
                { "minSpeed", F117AircraftAssembler.FlightControlMinimumSpeed },
                { "minAlt", F117AircraftAssembler.FlightControlMinimumAltitude },
                { "flyByWire.gLimitPositive", F117AircraftAssembler.FlightControlGLimit },
                { "flyByWire.cornerSpeed", F117AircraftAssembler.FlightControlCornerSpeed },
                { "flyByWire.takeoffSpeed", F117AircraftAssembler.FlightControlTakeoffSpeed },
                { "flyByWire.maxPitchAngularVel", F117AircraftAssembler.FlightControlMaxPitchRate },
                { "flyByWire.maxRollAngularVel", F117AircraftAssembler.FlightControlMaxRollRate },
                { "flyByWire.alphaLimiter", F117AircraftAssembler.FlightControlAlphaLimit },
                { "flyByWire.alphaLimiterStrength", 0.1f }, { "flyByWire.pFactorFast", 10f },
                { "flyByWire.iFactor", 0.01f },
                { "flyByWire.dFactorFast", F117AircraftAssembler.FlightControlPitchDamping },
                { "flyByWire.rollTrimRate", 0.04f }, { "flyByWire.rollTrimLimit", 0.3f },
                { "flyByWire.yawTightness", F117AircraftAssembler.FlightControlYawTightness },
                { "flyByWire.yawWeathervaning", 0f },
                { "flyByWire.rollTightness", 0.65f }
            })
                Require(Near(Float(data, expected.Key, controlsFilter.name, failures), expected.Value, 0.0001f),
                    "ControlsFilter." + expected.Key + " uses the F-117 control law", failures);
            Require(Bool(data, "flyByWire.Enabled", controlsFilter.name, failures), "F-117 fly-by-wire is enabled", failures);
            Require(!Bool(data, "autoHover.Enabled", controlsFilter.name, failures), "F-117 has no hover controller", failures);
            Require(!Bool(data, "aimAssist.Enabled", controlsFilter.name, failures), "F-117 has no gun aim assist", failures);
            Require(Bool(data, "flightAssistDefault", controlsFilter.name, failures), "F-117 stability assist defaults on", failures);
            SerializedProperty assistName = Property(data, "flightAssistName", controlsFilter.name, failures);
            Require(assistName != null && assistName.stringValue == "F-117 Stability Assist", "Flight-assist label is F-117-specific", failures);
            SerializedProperty iterator = data.GetIterator();
            int noseGearLinks = 0;
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.name == "noseGear")
                {
                    noseGearLinks++;
                    Require(iterator.objectReferenceValue != null && iterator.objectReferenceValue.name.Contains("Nose"),
                        "ControlsFilter." + iterator.propertyPath + " links the F-117 nose gear", failures);
                }
            }
            Require(noseGearLinks > 0, "ControlsFilter exposes at least one nose-gear link", failures);
        }

        Component autopilot = Single(components, "AutopilotPlane", failures);
        if (autopilot != null)
        {
            SerializedObject data = new SerializedObject(autopilot);
            RequireRef(data, "aircraft", autopilot.name, failures);
            Require(Bool(data, "forwardFlightController.Enabled", autopilot.name, failures), "Forward-flight autopilot is enabled", failures);
            Require(!Bool(data, "hoverController.Enabled", autopilot.name, failures), "Autopilot hover mode is disabled", failures);
            Require(Bool(data, "preventInvertedFlight", autopilot.name, failures), "F-117 AI avoids inverted flight", failures);
            Require(Near(Float(data, "forwardFlightController.referenceAirspeed", autopilot.name, failures),
                F117AircraftAssembler.FlightControlCornerSpeed, 0.001f), "Autopilot uses the F-117 reference speed", failures);
            Require(Near(Float(data, "aoaLimiter.threshold", autopilot.name, failures), 12f, 0.001f), "Autopilot AoA warning is 12 degrees", failures);
            Require(Near(Float(data, "aoaLimiter.limit", autopilot.name, failures),
                F117AircraftAssembler.FlightControlAlphaLimit, 0.001f), "Autopilot AoA limit matches the F-117 control law", failures);
        }

        foreach (string typeName in new[] { "Pilot", "TargetCam", "TargetDetector", "Cockpit", "LaserDesignator", "RadarLocator", "Canopy" })
        {
            Component system = Single(components, typeName, failures);
            if (system == null)
                continue;
            SerializedObject data = new SerializedObject(system);
            if (typeName == "Pilot")
                foreach (string field in new[] { "aircraft", "unitPart", "autoTrimmer", "pilotCollider", "skinnedMeshRenderer", "animator" }) RequireRef(data, field, typeName, failures);
            if (typeName == "TargetCam")
                foreach (string field in new[] { "attachedPart", "camMountForward", "camMountRear", "camMountLanding" }) RequireRef(data, field, typeName, failures);
            if (typeName == "TargetDetector")
            {
                foreach (string field in new[] { "attachedUnit", "scanner", "part" }) RequireRef(data, field, typeName, failures);
                Require(Near(Float(data, "visualRange", typeName, failures), 15000f, 0.01f),
                    "Passive EOTS detection range is 15 km", failures);
                Require(Near(Float(data, "magnification", typeName, failures), 3f, 0.001f),
                    "Passive EOTS magnification is 3x", failures);
            }
            if (typeName == "Cockpit")
            {
                foreach (string field in new[] { "tacScreenRender", "tacScreenUIPrefab", "aircraft" }) RequireRef(data, field, typeName, failures);
                RequireArray(data, "engineSources", 2, typeName, failures);
                RequireArray(data, "joysticks", 0, typeName, failures);
                RequireArray(data, "throttles", 0, typeName, failures);
            }
            if (typeName == "LaserDesignator")
                foreach (string field in new[] { "aircraft", "unitPart" }) RequireRef(data, field, typeName, failures);
            if (typeName == "RadarLocator")
            {
                RequireRef(data, "aircraft", typeName, failures);
                RequireArray(data, "essentialParts", 1, typeName, failures);
            }
            if (typeName == "Canopy")
            {
                foreach (string field in new[] { "attachedPart", "ejectionTransform", "ejectionCollider" }) RequireRef(data, field, typeName, failures);
                SerializedProperty canopyHinges = Property(data, "canopyHinges", typeName, failures);
                SerializedProperty hingeAngle = canopyHinges != null && canopyHinges.isArray && canopyHinges.arraySize == 1
                    ? canopyHinges.GetArrayElementAtIndex(0).FindPropertyRelative("hingeAngle")
                    : null;
                Require(hingeAngle != null && Near(hingeAngle.floatValue, 40f, 0.01f),
                    "Canopy opens upward 40 degrees instead of folding into the cockpit", failures);
            }
        }

        Component cockpitSystem = components.FirstOrDefault(component => component != null && component.GetType().Name == "Cockpit");
        Component targetCamSystem = components.FirstOrDefault(component => component != null && component.GetType().Name == "TargetCam");
        Renderer cockpitScreen = cockpitSystem == null ? null :
            new SerializedObject(cockpitSystem).FindProperty("tacScreenRender")?.objectReferenceValue as Renderer;
        Renderer targetScreen = targetCamSystem == null ? null :
            new SerializedObject(targetCamSystem).FindProperty("targetScreenRenderer")?.objectReferenceValue as Renderer;
        Require(cockpitScreen != null && cockpitScreen == targetScreen && cockpitScreen.enabled &&
                cockpitScreen.name == "F117_Tacscreen" && cockpitScreen.GetComponent<MeshFilter>()?.sharedMesh != null,
            "Cockpit and TargetCam share one dedicated visible F117_Tacscreen renderer", failures);
        Renderer cockpitScreenBackground = prefab.GetComponentsInChildren<Renderer>(true)
            .FirstOrDefault(renderer => renderer.name == "F117_Tacscreen_Background");
        Require(cockpitScreenBackground == null,
            "Cockpit displays use one full-size surface without a visible inset/background duplicate", failures);
        Mesh cockpitScreenMesh = cockpitScreen == null ? null : cockpitScreen.GetComponent<MeshFilter>()?.sharedMesh;
        int cockpitDisplayCount = ValidateCockpitDisplayMesh(cockpitScreenMesh, failures);

        Require(turbojets.Length == 2, "Two GE F404-F1D2 engines", failures);
        foreach (Component engine in turbojets)
        {
            SerializedObject data = new SerializedObject(engine);
            RequireRef(data, "turbineAudio", engine.name, failures);
            RequireArray(data, "nozzles", 1, engine.name, failures);
            Require(Near(Float(data, "maxThrust", engine.name, failures), 47150f, 0.1f), engine.name + " static thrust is 47,150 N", failures);
            SerializedProperty throttleRemap = Property(data, "throttleRemap", engine.name, failures);
            Require(throttleRemap != null &&
                    Near(throttleRemap.vector2Value.x, 0f, 0.001f) &&
                    Near(throttleRemap.vector2Value.y, 1f, 0.001f),
                engine.name + " retains the complete dry-thrust throttle range", failures);
        }

        Require(nozzles.Length == 2, "Two functional engine nozzles", failures);
        foreach (Component nozzle in nozzles)
        {
            SerializedObject data = new SerializedObject(nozzle);
            foreach (string field in new[] { "part", "turbojet", "thrustTransform", "thrustAudio" })
                RequireRef(data, field, nozzle.name, failures);
            Require(Near(Float(data, "thrustProportion", nozzle.name, failures), 1f, 0.001f), nozzle.name + " applies full engine thrust", failures);
            Require(Near(Float(data, "IRMin", nozzle.name, failures), 0.5f, 0.001f),
                nozzle.name + " idle infrared strength is 0.5", failures);
            Require(Near(Float(data, "IRMax", nozzle.name, failures), 2.2f, 0.001f),
                nozzle.name + " full-power infrared strength is 2.2", failures);
            Transform thrustTransform = Ref(data, "thrustTransform") as Transform;
            Require(thrustTransform != null && Vector3.Dot(thrustTransform.forward, prefab.transform.forward) > 0.999f,
                nozzle.name + " infrared/thrust transform points aircraft-forward", failures);
            RequireArray(data, "afterburners", 0, nozzle.name, failures);
        }

        Require(OfType(components, "BayDoor").Length == 2, "Two functional internal weapon-bay doors", failures);
        foreach (string side in new[] { "Left", "Right" })
        {
            Component bayDoor = OfType(components, "BayDoor")
                .SingleOrDefault(component => component.name.IndexOf(side, StringComparison.OrdinalIgnoreCase) >= 0);
            Transform panel = prefab.GetComponentsInChildren<Transform>(true)
                .SingleOrDefault(transform => transform.name == "F117_Bay_" + side);
            Require(bayDoor != null && panel != null && !panel.IsChildOf(bayDoor.transform) &&
                    panel.parent == productionVisual,
                side + " bay cavity remains fixed to the fuselage outside the native BayDoor hinge", failures);
            Require(panel != null && panel.GetComponentsInChildren<Collider>(true).Length == 0,
                side + " bay panel has no collision shape that can shove the airframe", failures);
            Transform[] bayLinks = prefab.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name.StartsWith(
                    "F117_BayDoor_" + side + "_BayLink_", StringComparison.Ordinal))
                .OrderBy(transform => transform.name, StringComparer.Ordinal)
                .ToArray();
            Require(bayLinks.Length == 2,
                side + " bay door has two independently animated mechanical linkages", failures);
            foreach (Transform link in bayLinks)
            {
                string index = link.name.Substring(link.name.LastIndexOf("_BayLink_", StringComparison.Ordinal) +
                    "_BayLink_".Length);
                Transform[] poses = Enumerable.Range(0, 9)
                    .Select(poseIndex => prefab.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(transform => transform.name ==
                            "F117_BayDoor_" + side + "_BayPose_" + index + "_" + poseIndex.ToString("D2")))
                    .ToArray();
                Require(poses.All(pose => pose != null),
                    link.name + " preserves all nine source-derived door-angle poses", failures);
                Require(link.GetComponentsInChildren<Renderer>(true).Count(renderer =>
                        !renderer.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                            StringComparison.Ordinal)) == 1,
                    link.name + " owns exactly one separate linkage mesh", failures);
                if (poses.All(pose => pose != null))
                    Require(Quaternion.Angle(poses[0].localRotation, poses[8].localRotation) > 80f,
                        link.name + " retains its greater-than-80-degree motion relative to the door", failures);
            }
        }
        if (manager != null)
        {
            SerializedObject data = new SerializedObject(manager);
            RequireRef(data, "aircraft", manager.name, failures);
            SerializedProperty sets = Property(data, "hardpointSets", manager.name, failures);
            Require(sets != null && sets.isArray && sets.arraySize == 3,
                "Independent left/right payload bays and one fixed jammer", failures);
            if (sets != null && sets.isArray && sets.arraySize == 3)
            {
                for (int setIndex = 0; setIndex < 2; setIndex++)
                {
                    string side = setIndex == 0 ? "Left" : "Right";
                    SerializedProperty baySet = sets.GetArrayElementAtIndex(setIndex);
                    Require(baySet.FindPropertyRelative("name")?.stringValue == side + " Weapon Bay",
                        side + " bay has a clear independent station name", failures);
                    SerializedProperty options = baySet.FindPropertyRelative("weaponOptions");
                    Require(options != null && options.arraySize == F117Builder.WeaponOptionCount,
                        side + " bay exposes empty plus every supported payload option", failures);
                    SerializedProperty preclusions = baySet.FindPropertyRelative("precludingHardpointSets");
                    Require(preclusions != null && preclusions.arraySize == 0,
                        side + " bay remains independently selectable", failures);
                    SerializedProperty hardpoints = baySet.FindPropertyRelative("hardpoints");
                    Require(hardpoints != null && hardpoints.arraySize == 1,
                        side + " bay has exactly one physical socket", failures);
                    if (hardpoints != null && hardpoints.arraySize == 1)
                    {
                        SerializedProperty hardpoint = hardpoints.GetArrayElementAtIndex(0);
                        RequireRelativeRef(hardpoint, "transform", side + " hardpoint", failures);
                        Transform socket = hardpoint.FindPropertyRelative("transform")?.objectReferenceValue as Transform;
                        Require(socket != null &&
                                Near(prefab.transform.InverseTransformPoint(socket.position).y,
                                    F117AircraftAssembler.InternalStoreMountHeight, 0.01f),
                            side + " hardpoint is on the audited internal bay mount plane", failures);
                        Require(socket != null && socket.name == "LOC_Weapon_" + side,
                            side + " hardpoint uses its authored physical bay locator", failures);
                        RequireRelativeRef(hardpoint, "part", side + " hardpoint", failures);
                        Require(hardpoint.FindPropertyRelative("part")?.objectReferenceValue == centralPart,
                            side + " hardpoint applies payload mass through the corrected central mixture",
                            failures);
                        SerializedProperty bayDoors = hardpoint.FindPropertyRelative("bayDoors");
                        Component linkedDoor = bayDoors != null && bayDoors.arraySize == 1
                            ? bayDoors.GetArrayElementAtIndex(0).objectReferenceValue as Component
                            : null;
                        Require(linkedDoor != null &&
                                linkedDoor.gameObject.name == "F117_BayDoor_" + side + "_Hinge",
                            side + " hardpoint opens only its matching bay door", failures);
                        SerializedProperty doorOpenDuration = hardpoint.FindPropertyRelative("doorOpenDuration");
                        Require(doorOpenDuration != null && Near(doorOpenDuration.floatValue, 1.2f, 0.001f),
                            side + " bay stays open for 1.2 seconds after its final release", failures);
                        RequireRelativeArray(hardpoint, "BuiltInWeapons", 0, side + " hardpoint", failures);
                        RequireRelativeArray(hardpoint, "BuiltInTurrets", 0, side + " hardpoint", failures);
                    }
                }

                SerializedProperty jammerSet = sets.GetArrayElementAtIndex(2);
                Require(jammerSet.FindPropertyRelative("name")?.stringValue == "JammingPod1",
                    "Fixed station retains the native JammingPod1 name", failures);
                SerializedProperty jammerOptions = jammerSet.FindPropertyRelative("weaponOptions");
                SerializedProperty jammerHardpoints = jammerSet.FindPropertyRelative("hardpoints");
                Require(jammerOptions != null && jammerOptions.arraySize == 1,
                    "Fixed jammer station accepts only JammingPod1", failures);
                Require(jammerHardpoints != null && jammerHardpoints.arraySize == 1,
                    "Fixed jammer station has exactly one internal socket", failures);
                if (jammerHardpoints != null && jammerHardpoints.arraySize == 1)
                {
                    SerializedProperty jammerHardpoint = jammerHardpoints.GetArrayElementAtIndex(0);
                    Transform jammerSocket = jammerHardpoint.FindPropertyRelative("transform")?.objectReferenceValue as Transform;
                    Require(jammerSocket != null && jammerSocket.name == "F117_FixedJammerSocket" &&
                            Vector3.Distance(prefab.transform.InverseTransformPoint(jammerSocket.position), Vector3.zero) < 0.01f,
                        "Active jammer is mounted invisibly at the aircraft origin", failures);
                    RequireRelativeRef(jammerHardpoint, "part", "Fixed jammer hardpoint", failures);
                    Require(jammerHardpoint.FindPropertyRelative("part")?.objectReferenceValue == centralPart,
                        "Fixed jammer hardpoint remains owned by the central mass part", failures);
                    RequireRelativeArray(jammerHardpoint, "bayDoors", 0, "Fixed jammer hardpoint", failures);
                    RequireRelativeArray(jammerHardpoint, "BuiltInWeapons", 0, "Fixed jammer hardpoint", failures);
                    RequireRelativeArray(jammerHardpoint, "BuiltInTurrets", 0, "Fixed jammer hardpoint", failures);
                }
            }
        }

        if (aircraft != null)
        {
            SerializedObject data = new SerializedObject(aircraft);
            foreach (string field in new[]
            {
                "definition", "EOTS", "weaponManager", "cockpit", "cockpitViewPoint", "targetCam",
                "powerSupply", "controlsFilter"
            })
                RequireRef(data, field, aircraft.name, failures);
            Require(Ref(data, "radar") == null,
                "Aircraft has no emitting search-radar reference and uses the native optical TacScreen path", failures);
            RequireArray(data, "canopies", 1, aircraft.name, failures);
            RequireArray(data, "pilots", 1, aircraft.name, failures);
            SerializedProperty exterior = Property(data, "exteriorRenderers", aircraft.name, failures);
            SerializedProperty cockpit = Property(data, "cockpitRenderers", aircraft.name, failures);
            Renderer detailedCockpit = prefab.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer => renderer.name == "F117_Cockpit_Mesh");
            Renderer exteriorCanopy = prefab.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer => renderer.name == "F117_Canopy_Mesh");
            Require(detailedCockpit != null && detailedCockpit.enabled &&
                    (cockpit == null || !cockpit.isArray ||
                     !Enumerable.Range(0, cockpit.arraySize).Any(index =>
                         cockpit.GetArrayElementAtIndex(index).objectReferenceValue == detailedCockpit)),
                "Detailed F-117 cockpit remains enabled and is never camera-switched off", failures);
            Require(exterior != null && exterior.isArray && exterior.arraySize >= 1 && exteriorCanopy != null &&
                    Enumerable.Range(0, exterior.arraySize).Any(index =>
                        exterior.GetArrayElementAtIndex(index).objectReferenceValue == exteriorCanopy),
                "Exterior renderer group contains the dedicated F-117 canopy", failures);
            Require(fixedExteriorRenderers.Length == 3 &&
                    (exterior == null || !exterior.isArray || fixedExteriorRenderers.All(shell =>
                    !Enumerable.Range(0, exterior.arraySize).Any(index =>
                        exterior.GetArrayElementAtIndex(index).objectReferenceValue == shell))),
                "The three part-owned fixed-airframe renderers are never disabled by cockpit camera switching",
                failures);
            Transform ejectionSeat = prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "EjectionSeat");
            MeshFilter seatFilter = ejectionSeat == null ? null : ejectionSeat.GetComponent<MeshFilter>();
            Renderer seatRenderer = ejectionSeat == null ? null : ejectionSeat.GetComponent<Renderer>();
            string seatManifest = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : string.Empty;
            bool nativeSeatMeshPatch = seatManifest.Contains("\"locator\": \"ejectionSeat\"") &&
                seatManifest.Contains("F117_Avionics/Cockpit/pilot/EjectionSeat") &&
                seatManifest.Contains("UnityEngine.MeshFilter, UnityEngine.CoreModule") &&
                seatManifest.Contains("\"memberPath\": \"sharedMesh\"");
            Require(ejectionSeat != null && seatFilter != null &&
                    (seatFilter.sharedMesh != null || nativeSeatMeshPatch) &&
                    seatRenderer != null && seatRenderer.enabled && ejectionSeat.gameObject.activeSelf,
                "Retained pilot hierarchy includes a visible ejection seat with an exact native mesh patch", failures);
            bakedLadderTriangleCount = 0;
            var bakedLadderCandidates = new List<string>();
            foreach (Renderer damageShell in fixedExteriorRenderers)
            {
                Mesh exteriorMesh = damageShell.GetComponent<MeshFilter>()?.sharedMesh;
                if (exteriorMesh == null)
                    continue;
                Vector3[] vertices = exteriorMesh.vertices;
                int[] triangles = exteriorMesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 localCenter = (vertices[triangles[index]] + vertices[triangles[index + 1]] +
                        vertices[triangles[index + 2]]) / 3f;
                    Vector3 rootCenter = prefab.transform.InverseTransformPoint(
                        damageShell.transform.TransformPoint(localCenter));
                    // The removed boarding ladder occupied this forward-side region
                    // below the airframe. Require the protruding below-belly Y position
                    // that distinguished it from the authored center-body shell.
                    if (rootCenter.x > 1.75f && rootCenter.z > 5.2f && rootCenter.y < -0.25f)
                    {
                        bakedLadderTriangleCount++;
                        bakedLadderCandidates.Add(damageShell.name + "@" + rootCenter.ToString("F3"));
                    }
                }
            }
            Require(bakedLadderTriangleCount == 0,
                "Production exterior contains no baked boarding-ladder geometry " +
                "(candidate triangles=" + bakedLadderTriangleCount +
                (bakedLadderCandidates.Count == 0 ? string.Empty : ": " +
                    string.Join(", ", bakedLadderCandidates)) + ")", failures);
            Transform cockpitViewPoint = Ref(data, "cockpitViewPoint") as Transform;
            Transform authoredCockpitPoint = prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "LOC_CockpitCamera");
            Require(cockpitViewPoint != null && cockpitViewPoint.parent == prefab.transform &&
                    authoredCockpitPoint != null &&
                    Vector3.Distance(cockpitViewPoint.position,
                        authoredCockpitPoint.position - prefab.transform.forward *
                        F117AircraftAssembler.CockpitCameraRearwardOffset) < 0.01f &&
                    Near(prefab.transform.InverseTransformPoint(cockpitViewPoint.position).y, 1.39f, 0.01f) &&
                    Vector3.Dot(cockpitViewPoint.forward, prefab.transform.forward) > 0.999f &&
                    Vector3.Dot(cockpitViewPoint.up, prefab.transform.up) > 0.999f,
                "Cockpit camera is root-aligned, at the 1.39 m eye line, and aligned to the pilot seat", failures);
            Require(Near(Float(data, "RCS", aircraft.name, failures), 0.0000005f, 0.00000001f),
                "Prefab fallback RCS is the clean 0.0000005 baseline before the runtime bay/gear controller attaches", failures);
        }

        GameObject statusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StatusPath);
        Require(statusPrefab != null, "Clean F-117 status prefab loads", failures);
        if (statusPrefab != null)
        {
            Component[] statusComponents = statusPrefab.GetComponentsInChildren<Component>(true);
            Component status = statusComponents.FirstOrDefault(component => component != null && component.GetType().Name == "StatusDisplay");
            Require(status != null, "Status prefab contains StatusDisplay", failures);
            string statusYaml = File.ReadAllText(StatusPath);
            Require(!statusYaml.Contains("m_Script: {fileID: 0}"),
                "Status prefab serializes no unresolved scripts", failures);
            Require(statusComponents.All(component => component != null),
                "Status prefab contains no missing components", failures);
            Require(statusComponents.All(component => component == null ||
                    component.GetType().FullName != "UnityEngine.UI.Image"),
                "Status prefab contains no editor-authored UGUI Images", failures);
            Transform statusPart = statusPrefab.transform.Find("F117A_Nighthawk");
            RectTransform statusRootRect = statusPrefab.GetComponent<RectTransform>();
            Require(statusRootRect != null &&
                    Vector2.Distance(statusRootRect.anchorMin, new Vector2(1f, 0f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.anchorMax, new Vector2(1f, 0f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.pivot, new Vector2(1f, -0.05f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.anchoredPosition, Vector2.zero) < 0.001f &&
                    Vector2.Distance(statusRootRect.sizeDelta, new Vector2(260f, 260f)) < 0.001f,
                "Status HUD uses the retail bottom-right 260 px anchor and pivot contract", failures);
            Require(statusPart != null,
                "Status damage image matches the persisted F117A_Nighthawk root part", failures);
            Require(statusPart != null && statusPart.GetComponent<RectTransform>() != null &&
                    statusPart.GetComponent<CanvasRenderer>() != null,
                "Status damage part contains runtime-safe layout components", failures);
            RectTransform statusPartRect = statusPart == null ? null : statusPart.GetComponent<RectTransform>();
            Require(statusPartRect != null &&
                    Vector2.Distance(statusPartRect.anchorMin, Vector2.zero) < 0.001f &&
                    Vector2.Distance(statusPartRect.anchorMax, Vector2.one) < 0.001f &&
                    Vector2.Distance(statusPartRect.anchoredPosition, Vector2.zero) < 0.001f &&
                    Vector2.Distance(statusPartRect.sizeDelta, Vector2.zero) < 0.001f,
                "Status silhouette stays fully contained inside its bottom-right HUD holder", failures);
            if (status != null)
            {
                SerializedObject statusData = new SerializedObject(status);
                SerializedProperty displays = Property(statusData, "statusDisplays", status.name, failures);
                Require(displays != null && displays.isArray && displays.arraySize == 1,
                    "Status prefab maps exactly one F-117 damage part", failures);
                if (displays != null && displays.isArray && displays.arraySize == 1)
                {
                    SerializedProperty partImage = displays.GetArrayElementAtIndex(0).FindPropertyRelative("partImage");
                    Require(partImage != null && partImage.objectReferenceValue == null,
                        "Status part image is reserved for runtime wiring", failures);
                }
                SerializedProperty background = Property(statusData, "aircraftBackground", status.name, failures);
                Require(background != null && background.objectReferenceValue == null,
                    "Status background image is reserved for runtime wiring", failures);
            }
        }

        string manifestJson = File.ReadAllText(ManifestPath);
        Require(manifestJson.IndexOf("Aryx", StringComparison.OrdinalIgnoreCase) < 0 &&
                manifestJson.IndexOf("F16", StringComparison.OrdinalIgnoreCase) < 0,
            "Manifest contains no donor F-16 asset or hierarchy names", failures);
        string[] obsoleteFixedPartPaths =
        {
            "F117_Physics", "F117_CentralBody/", "F117_Nose", "F117_RearBody",
            "F117_Wing_Left_Root", "F117_Wing_Left_Inner", "F117_Wing_Left_Outer",
            "F117_Wing_Right_Root", "F117_Wing_Right_Inner", "F117_Wing_Right_Outer"
        };
        Require(obsoleteFixedPartPaths.All(path =>
                manifestJson.IndexOf(path, StringComparison.Ordinal) < 0),
            "Manifest contains no obsolete wrapper, nose/rear, or segmented-wing hierarchy paths", failures);
        Require(manifestJson.IndexOf("\"componentType\": \"RadarJammer, Assembly-CSharp\"", StringComparison.Ordinal) < 0,
            "Manifest contains no defensive RadarJammer countermeasure patch", failures);
        Require(manifestJson.Contains("\"locator\": \"JammingPod1\"") &&
                manifestJson.Contains("hardpointSets[2].weaponOptions[0]") &&
                manifestJson.Contains("loadouts[0].weapons[2]") &&
                manifestJson.Contains("StandardLoadouts[0].loadout.weapons[2]"),
            "Manifest installs native JammingPod1 on the fixed active-jammer station and every loadout", failures);
        foreach (string side in new[] { "Left", "Right" })
            Require(manifestJson.Contains("\"id\": \"F117A_Nighthawk/F117_Engine_" + side + "\"") &&
                    manifestJson.Contains("\"hierarchyPath\": \"F117_Engine_" + side + "\""),
                "Manifest patches the " + side.ToLowerInvariant() +
                " engine at its exact aircraft-root hierarchy path", failures);
        foreach (string weapon in F117Builder.WeaponAssetNames)
            Require(manifestJson.Contains("\"locator\": \"" + weapon + "\""), "Manifest patches weapon " + weapon, failures);
        foreach (string asset in new[] { "IRFlare", "flare1", "weaponicon_flares", "weaponicon_radarJammer" })
            Require(manifestJson.Contains("\"locator\": \"" + asset + "\""),
                "Manifest patches countermeasure asset " + asset, failures);
        Require(manifestJson.Contains("\"locator\": \"gearfold\"") &&
                LiteralCount(manifestJson, "\"memberPath\": \"foldSound\"") == 3,
            "Manifest patches the native gear-fold clip onto all three landing gears", failures);
        Require(manifestJson.Contains("\"locator\": \"latch1\"") &&
                LiteralCount(manifestJson, "\"memberPath\": \"latchSound\"") == 3,
            "Manifest patches the native gear-latch clip onto all three landing gears", failures);
        foreach (string side in new[] { "Nose", "Left", "Right" })
        {
            string wheelPath = "F117_Visual/F117_Gear_" + side + "_Hinge_Axis/F117_Gear_" + side +
                               "_Hinge/F117_Gear_" + side + "_Sprung/F117_Gear_" + side +
                               "_Unsprung/Axle/WheelProxy/UnityEngine.AudioSource, UnityEngine.AudioModule#";
            Require(manifestJson.Contains(wheelPath + "0") && manifestJson.Contains(wheelPath + "1"),
                "Manifest routes both " + side.ToLowerInvariant() +
                " tire sources through the native Effects mixer", failures);
        }
        Require(manifestJson.Contains("\"locator\": \"hudIcon_aircraft\"") &&
                manifestJson.Contains("\"memberPath\": \"friendlyIcon\"") &&
                manifestJson.Contains("\"memberPath\": \"hostileIcon\"") &&
                !manifestJson.Contains("\"memberPath\": \"mapIcon\""),
            "Friendly and hostile HUD markers use the native aircraft icon while the map keeps the F-117 silhouette",
            failures);
        Require(manifestJson.Contains("\"name\": \"Shader Graphs/AircraftSkin\"") &&
                damageSkinMaterials.All(material => manifestJson.Contains(material.name + "/shader")),
            "Manifest resolves every F-117 damage skin material to the native AircraftSkin shader", failures);

        notes.Add("Validated root: " + prefab.name);
        notes.Add("Components: AeroPart=11, ControlSurface=6, LandingGear=3, Turbojet=2, JetNozzle=2, BayDoor=2, FlareEjector=1, ChaffEjector=1, RadarJammer=0, Radar=0");
        notes.Add("Countermeasures: 32 native flares, 64 native chaff, two central-body ejection points each, visible material-backed RadarChaff payload");
        notes.Add("Active jammer: native JammingPod1 weapon, permanently installed and target-fired; no defensive RadarJammer countermeasure");
        notes.Add("Electrical: dedicated 60 kJ jammer bus; native 13-unit draw gives about 5 s full-charge burst; two engines recharge at up to 1.16 kJ/s (about 52 s empty-to-full)");
        notes.Add("Physics graph: one central root AeroPart plus 10 parent-matched, jointed descendants; the two whole-wing parts detach only at their authored root rings, all elevons follow their matching wing, and both rudders remain on the central structure");
        notes.Add("Hitboxes: all 11 AeroParts directly own their real colliders; the central root owns three measured boxes and the two whole wings use mesh-derived planform colliders; native bullets/blast fragments cannot be swallowed by non-damageable child objects; full unrelated-part penetration audit passed");
        notes.Add("Damage model: 11 non-critical, standard 100 HP AeroParts; the fixed airframe uses exactly three always-enabled authored renderers with integrated structural slots (central/left/right triangles=15/7/8) at two real shared root rings; no structural triangle overlaps exterior skin, and no generated skins, cap renderers, cap objects, duplicate interior shells, or cockpit cut exists; controls retain AircraftSkin pockmarks, status reporting, native fuel fire/leak effects, and physical detachment");
        notes.Add("Elevon neutral: unbiased native servo/aero pivots; measured inner-panel visual corrections isolated below them");
        notes.Add("Mass: dry graph=13380 kg at CoM Z=" + runtimeCenterOfMass.z.ToString("0.000") +
            " m; full fuel plus the matched 1000 kg payload=22630 kg at CoM Z=" +
            matchedLoadedCenterOfMass.z.ToString("0.000") + " m; the central Rigidbody uses the dry/load " +
            "first-moment mixture at Z=" + matchedCentralCenterOfMass.z.ToString("0.000") + " m; " +
            "MTOW=23814 kg; payload margin=2184 kg");
        notes.Add("Aerodynamic area: 77.449 m^2 total (62.180 m^2 fixed + 10.820 m^2 measured elevons + 4.449 m^2 full vertical tails)");
        notes.Add("Engine thrust: 2 x 47150 N");
        notes.Add("Weapons: independent left/right native racks at Y=" +
            F117AircraftAssembler.InternalStoreMountHeight.ToString("0.00") + " m, " +
            F117Builder.WeaponOptionCount + " options per bay; " + F117Builder.WeaponLoadoutCount +
            " standard maximum-capacity loadouts spanning 8/4/2 stores; either bay may be cleared or mixed independently; " +
            "a third hidden station permanently mounts JammingPod1");
        notes.Add("Bomb-bay mechanism: two source-derived strut tracks per door, nine door-angle poses each; struts remain independent of rigid panels");
        notes.Add("Materials: albedo=" + albedoCount + ", compatibility albedo=" + compatibilityAlbedoCount +
            ", normal=" + normalCount + ", mask=" + maskCount + ", emission=" + emissionCount);
        notes.Add("Liveries: Nighthawk Black plus Smoked Chrome and Matte Black Farewell Flag finishes; exact 50-star/13-stripe " +
            "projection across " + paradeOverlays.Length + " lower-facing render meshes");
        notes.Add("Stealth: clean RCS=0.0000005; each bay adds up to 0.04 independently; gear adds up to 0.05 progressively; internal stores remain shielded");
        notes.Add("Sensors: no emitting search Radar; passive EOTS=15 km/3x, passive RadarLocator retained; optical visibility=2.5 km");
        notes.Add("Infrared: two forward-aligned sources, 0.5 idle to 2.2 full dry thrust; no afterburner, vapor, or global contrail components");
        notes.Add("Status HUD: retail bottom-right 260 px layout; plugin wires embedded F-117 damage Images before initialization");
        notes.Add("Exterior: baked boarding-ladder triangles=" + bakedLadderTriangleCount);
        notes.Add("Cockpit: complete stock Cricket atlas regions mapped without cropping across " + cockpitDisplayCount +
            " full-size physical displays (upright center camera/radar; 90-degree-corrected left flight and right engine instruments); " +
            "root-aligned viewpoint at Y=1.39 m on the seat line");
        notes.Add("Canopy: upward 40 degree ejection opening");
        notes.Add("HUD markers: native hudIcon_aircraft for friendly and hostile contacts; custom F-117 silhouette retained for the map and damage display");
        notes.Add("Landing gear: aircraft-forward tire-physics frames, stock-sized 0.60 m ground probes on all three legs, full probe travel before BreakWheel, unmuted stock rolling/skid audio, native fold/latch sounds");
        notes.Add("Rear controls: model-measured elevon area, 15 pitch + 7.5 roll travel; both rudders use coordinated -18 yaw on local-Z visual hinges");
        notes.Add("Pitch balance: neutral horizontal lift centre is " +
            (runtimeCenterOfMass.z - neutralLiftCenterZ).ToString("0.00") +
            " m behind dry CG; controller retains elevon travel for pilot pitch authority");
        notes.Add("Fixed lift: aircraft-aligned native axes with zero artificial aerodynamic incidence");
        notes.Add("Stability: constant aero areas in every gear state; pitch damping 2.8; yaw tightness 1.0; no synthetic weathervaning");
        notes.Add("Flight controls: +6 g, 18 deg alpha, 0.45 rad/s pitch, 1.75 rad/s roll, 72 m/s takeoff schedule");
        Finish(failures, notes);
    }

    [MenuItem("F-117A Nighthawk/Dump Full Component Inventory")]
    public static void DumpFullComponentInventory()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Generated F-117 prefab is missing.");
        string[] lines = prefab.GetComponentsInChildren<Component>(true)
            .Select(component => component == null
                ? "<MISSING SCRIPT>"
                : GetPath(prefab.transform, component.transform) + " | " + component.GetType().FullName)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
        string path = Path.Combine(
            Application.dataPath, "F117", "Generated", "Reports", "F117_Full_Component_Inventory.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllLines(path, lines);
        Debug.Log("F-117 full component inventory written to " + path + ": " + lines.Length + " entries.");
    }

    private static string GetPath(Transform root, Transform item)
    {
        var names = new List<string>();
        for (Transform current = item; current != null; current = current.parent)
        {
            names.Add(current.name);
            if (current == root)
                break;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private const float StructuralPointTolerance = 0.0001f;

    private readonly struct StructuralFace
    {
        internal readonly Vector3 A;
        internal readonly Vector3 B;
        internal readonly Vector3 C;

        internal StructuralFace(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }

        internal Vector3 Center => (A + B + C) / 3f;
        internal Vector3 AreaNormal => Vector3.Cross(B - A, C - A);
    }

    private sealed class AuthoredStructureSection
    {
        internal Renderer Renderer;
        internal readonly List<StructuralFace> StructureFaces = new List<StructuralFace>();
        internal readonly List<StructuralFace> ExteriorFaces = new List<StructuralFace>();
    }

    private static Component NearestAeroPart(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            Component part = current.GetComponent("AeroPart");
            if (part != null)
                return part;
        }
        return null;
    }

    private static Renderer[] ValidateAuthoredFixedAirframe(GameObject prefab, Component central,
        Component leftWing, Component rightWing, List<string> failures)
    {
        string[] objectNames =
        {
            "F117_Exterior_Mesh",
            "F117_Exterior_LeftWing_Mesh",
            "F117_Exterior_RightWing_Mesh"
        };
        Component[] expectedOwners = { central, leftWing, rightWing };
        int[] expectedStructureTriangles = { 15, 7, 8 };
        Renderer[] exteriorCandidates = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.StartsWith("F117_Exterior", StringComparison.Ordinal))
            .ToArray();
        Require(exteriorCandidates.Length == 3 &&
                exteriorCandidates.Select(renderer => renderer.name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(objectNames.OrderBy(name => name, StringComparer.Ordinal)),
            "Fixed airframe has exactly the three authored center/left-wing/right-wing exterior renderers",
            failures);

        var renderers = new Renderer[objectNames.Length];
        var sections = new AuthoredStructureSection[objectNames.Length];
        for (int index = 0; index < objectNames.Length; index++)
        {
            Renderer[] matches = exteriorCandidates
                .Where(renderer => renderer.name == objectNames[index])
                .ToArray();
            Require(matches.Length == 1,
                objectNames[index] + " exists exactly once in the fixed-airframe renderer set", failures);
            if (matches.Length != 1)
                continue;
            Renderer renderer = matches[0];
            renderers[index] = renderer;
            Component owner = NearestAeroPart(renderer.transform);
            SerializedProperty damageRenderers = expectedOwners[index] == null
                ? null
                : new SerializedObject(expectedOwners[index]).FindProperty("damageMaterial")
                    ?.FindPropertyRelative("renderers");
            bool isDamageRenderer = damageRenderers != null && damageRenderers.isArray &&
                Enumerable.Range(0, damageRenderers.arraySize).Any(damageIndex =>
                    damageRenderers.GetArrayElementAtIndex(damageIndex).objectReferenceValue == renderer);
            Require(renderer.enabled && owner == expectedOwners[index] && isDamageRenderer,
                objectNames[index] +
                " is the owning AeroPart's always-enabled authoritative base damage renderer", failures);
            sections[index] = ReadAuthoredStructureSection(prefab.transform, renderer,
                objectNames[index], expectedStructureTriangles[index], failures);
        }

        if (sections.All(section => section != null))
        {
            List<StructuralFace> allStructure = sections
                .SelectMany(section => section.StructureFaces).ToList();
            List<StructuralFace> allExterior = sections
                .SelectMany(section => section.ExteriorFaces).ToList();
            Require(!allStructure.Any(structure =>
                    allExterior.Any(exterior => StructuralTrianglesCoincide(structure, exterior))),
                "No integrated structural root triangle overlaps any exterior-skin triangle",
                failures);
            List<StructuralFace> centralLeft = sections[0].StructureFaces
                .Where(face => face.Center.x < 0f).ToList();
            List<StructuralFace> centralRight = sections[0].StructureFaces
                .Where(face => face.Center.x > 0f).ToList();
            Require(centralLeft.Count == 7 && centralRight.Count == 8 &&
                    centralLeft.Count + centralRight.Count == sections[0].StructureFaces.Count,
                "Central structure contains only the 7-triangle left and 8-triangle right root faces",
                failures);
            List<Vector3> leftRing = ValidateSharedStructuralInterface(
                "left wing root", sections[0], centralLeft, sections[1], 7, true, failures);
            List<Vector3> rightRing = ValidateSharedStructuralInterface(
                "right wing root", sections[0], centralRight, sections[2], 8, false, failures);
            Require(!leftRing.Any(left => rightRing.Any(right => StructuralPointsNear(left, right))),
                "Left and right structural rings have no unintended sibling adjacency", failures);
        }
        return renderers.Where(renderer => renderer != null).ToArray();
    }

    private static void ValidateExactDamageRendererOwners(Component[] aeroParts,
        Component central, Component leftWing, Component rightWing, List<string> failures)
    {
        string Overlay(string sourceName)
        {
            return F117AircraftAssembler.ParadeFlagOverlayPrefix + sourceName;
        }

        string[] controlNames =
        {
            "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
            "F117_Elevon_R_Inner", "F117_Elevon_R_Outer"
        };
        Component[] owners = new[] { central, leftWing, rightWing }
            .Concat(controlNames.Select(name => aeroParts.FirstOrDefault(part => part.name == name)))
            .ToArray();
        string[][] expectedNames =
        {
            new[]
            {
                "F117_Exterior_Mesh", "F117_BayDoor_Left_Mesh", "F117_BayDoor_Right_Mesh",
                "F117_GearDoor_Nose_Mesh", "F117_GearDoor_Left_Outer_Mesh",
                "F117_GearDoor_Left_Inner_Mesh", "F117_GearDoor_Right_Outer_Mesh",
                "F117_GearDoor_Right_Inner_Mesh",
                Overlay("F117_Exterior_Mesh"), Overlay("F117_BayDoor_Left_Mesh"),
                Overlay("F117_BayDoor_Right_Mesh"), Overlay("F117_GearDoor_Nose_Mesh"),
                Overlay("F117_GearDoor_Left_Outer_Mesh"),
                Overlay("F117_GearDoor_Left_Inner_Mesh"),
                Overlay("F117_GearDoor_Right_Outer_Mesh"),
                Overlay("F117_GearDoor_Right_Inner_Mesh")
            },
            new[] { "F117_Exterior_LeftWing_Mesh", Overlay("F117_Exterior_LeftWing_Mesh") },
            new[] { "F117_Exterior_RightWing_Mesh", Overlay("F117_Exterior_RightWing_Mesh") },
            new[] { "F117_Elevon_L_Inner_Mesh", Overlay("F117_Elevon_L_Inner_Mesh") },
            new[] { "F117_Elevon_L_Outer_Mesh", Overlay("F117_Elevon_L_Outer_Mesh") },
            new[] { "F117_Elevon_R_Inner_Mesh", Overlay("F117_Elevon_R_Inner_Mesh") },
            new[] { "F117_Elevon_R_Outer_Mesh", Overlay("F117_Elevon_R_Outer_Mesh") }
        };

        var allBoundRenderers = new List<Renderer>();
        for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
        {
            Component part = owners[ownerIndex];
            if (part == null)
            {
                Require(false, "Native damage owner " + ownerIndex + " exists", failures);
                continue;
            }
            SerializedProperty renderers = new SerializedObject(part).FindProperty("damageMaterial")
                ?.FindPropertyRelative("renderers");
            Renderer[] actual = renderers == null || !renderers.isArray
                ? Array.Empty<Renderer>()
                : Enumerable.Range(0, renderers.arraySize)
                    .Select(index => renderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer)
                    .ToArray();
            string[] actualNames = actual.Select(renderer => renderer == null ? "<null>" : renderer.name)
                .ToArray();
            Require(actualNames.SequenceEqual(expectedNames[ownerIndex], StringComparer.Ordinal),
                part.name + " has the exact authoritative base + Farewell Flag damage renderer order" +
                (actualNames.SequenceEqual(expectedNames[ownerIndex], StringComparer.Ordinal)
                    ? string.Empty
                    : " (actual: " + string.Join(", ", actualNames) + ")"), failures);
            foreach (Renderer renderer in actual.Where(renderer => renderer != null))
            {
                Require(NearestAeroPart(renderer.transform) == part,
                    renderer.name + " resolves to its declared native damage owner " + part.name, failures);
                allBoundRenderers.Add(renderer);
            }
        }
        Require(allBoundRenderers.Count == allBoundRenderers.Distinct().Count(),
            "Every authoritative base/flag damage renderer is assigned to exactly one UnitPart", failures);
    }

    private static AuthoredStructureSection ReadAuthoredStructureSection(Transform aircraftRoot,
        Renderer renderer, string objectName, int expectedStructureTriangles, List<string> failures)
    {
        MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
        Mesh mesh = filter == null ? null : filter.sharedMesh;
        Require(filter != null && mesh != null && mesh.name == objectName,
            objectName + " uses its exact authored mesh asset", failures);
        if (mesh == null)
            return null;

        Material[] materials = renderer.sharedMaterials;
        Require(materials.Length == mesh.subMeshCount,
            objectName + " has one renderer material for every authored submesh", failures);
        int[] structureSlots = Enumerable.Range(0, Mathf.Min(materials.Length, mesh.subMeshCount))
            .Where(index => materials[index] != null &&
                materials[index].name.IndexOf("AircraftStructure", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        Require(structureSlots.Length == 1,
            objectName + " has exactly one integrated aircraft-structure material slot", failures);
        if (structureSlots.Length != 1)
            return null;
        Material structureMaterial = materials[structureSlots[0]];
        SerializedProperty customRenderQueue =
            new SerializedObject(structureMaterial).FindProperty("m_CustomRenderQueue");
        Require(structureMaterial.HasProperty("_Surface") &&
                structureMaterial.HasProperty("_ZWrite") &&
                structureMaterial.HasProperty("_Cull") &&
                Near(structureMaterial.GetFloat("_Surface"), 0f, 0.001f) &&
                Near(structureMaterial.GetFloat("_ZWrite"), 1f, 0.001f) &&
                Near(structureMaterial.GetFloat("_Cull"),
                    (float)UnityEngine.Rendering.CullMode.Back, 0.001f) &&
                structureMaterial.GetTag("RenderType", false, string.Empty) == "Opaque" &&
                // Material.renderQueue resolves a raw -1 (shader default) to the
                // shader's effective queue. Check both the serialized inheritance
                // marker and its effective shader queue so a custom 2000 override
                // cannot masquerade as the intended default opaque state.
                customRenderQueue != null && customRenderQueue.intValue == -1 &&
                structureMaterial.shader != null &&
                structureMaterial.renderQueue == structureMaterial.shader.renderQueue,
            objectName +
            " integrated structure slot is opaque, depth-writing, and back-face culled", failures);

        var section = new AuthoredStructureSection { Renderer = renderer };
        Vector3[] vertices = mesh.vertices;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            List<StructuralFace> destination = subMesh == structureSlots[0]
                ? section.StructureFaces
                : section.ExteriorFaces;
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                Vector3 a = aircraftRoot.InverseTransformPoint(
                    renderer.transform.TransformPoint(vertices[triangles[triangle]]));
                Vector3 b = aircraftRoot.InverseTransformPoint(
                    renderer.transform.TransformPoint(vertices[triangles[triangle + 1]]));
                Vector3 c = aircraftRoot.InverseTransformPoint(
                    renderer.transform.TransformPoint(vertices[triangles[triangle + 2]]));
                destination.Add(new StructuralFace(a, b, c));
            }
        }
        Require(section.StructureFaces.Count == expectedStructureTriangles &&
                section.StructureFaces.All(face => face.AreaNormal.sqrMagnitude > 0.0000000001f),
            objectName + " has exactly " + expectedStructureTriangles +
            " non-degenerate authored structural triangles", failures);
        return section;
    }

    private static List<Vector3> ValidateSharedStructuralInterface(string label,
        AuthoredStructureSection central, List<StructuralFace> centralFaces,
        AuthoredStructureSection wing, int expectedTriangles, bool left,
        List<string> failures)
    {
        List<StructuralFace> wingFaces = wing.StructureFaces;
        List<Vector3> centralRing = UniqueStructuralPoints(FacePoints(centralFaces));
        List<Vector3> wingRing = UniqueStructuralPoints(FacePoints(wingFaces));
        int expectedRingVertices = expectedTriangles + 2;
        Require(centralFaces.Count == expectedTriangles && wingFaces.Count == expectedTriangles &&
                centralRing.Count == expectedRingVertices && wingRing.Count == expectedRingVertices &&
                StructuralPointSetsEqual(centralRing, wingRing),
            label + " uses one exact shared " + expectedRingVertices +
            "-vertex ring and no extra structural geometry", failures);
        Require(StructuralFacesFormSingleDisk(centralFaces) &&
                StructuralFacesFormSingleDisk(wingFaces),
            label + " structural faces are closed, manifold triangulated disks with no invented holes",
            failures);

        List<Vector3> centralExteriorBoundary = ExteriorBoundaryPoints(central.ExteriorFaces);
        List<Vector3> wingExteriorBoundary = ExteriorBoundaryPoints(wing.ExteriorFaces);
        Require(centralRing.All(point => centralExteriorBoundary.Any(boundary =>
                    StructuralPointsNear(point, boundary))) &&
                wingRing.All(point => wingExteriorBoundary.Any(boundary =>
                    StructuralPointsNear(point, boundary))),
            label + " is made only from the real open exterior boundary on both mating sections",
            failures);

        bool lateralRing = wingRing.Count > 0 &&
            wingRing.All(point => (left ? -point.x : point.x) > 2.0f) &&
            wingRing.Max(point => point.x) - wingRing.Min(point => point.x) < 1f &&
            wingRing.Max(point => point.z) - wingRing.Min(point => point.z) > 7.5f;
        Require(lateralRing,
            label + " follows the long, narrow authored root contour fully outside the central cockpit body",
            failures);

        bool exactlyPaired = centralFaces.Count == wingFaces.Count && centralFaces.All(centerFace =>
            wingFaces.Count(wingFace => StructuralTrianglesMatchOpposite(centerFace, wingFace)) == 1);
        float centralArea = centralFaces.Sum(face => face.AreaNormal.magnitude * 0.5f);
        float wingArea = wingFaces.Sum(face => face.AreaNormal.magnitude * 0.5f);
        Require(exactlyPaired && centralArea > 0f &&
                Mathf.Abs(centralArea - wingArea) <= centralArea * 0.0001f,
            label + " has coincident paired triangles with equal area and opposite winding", failures);
        return wingRing;
    }

    private static IEnumerable<Vector3> FacePoints(IEnumerable<StructuralFace> faces)
    {
        foreach (StructuralFace face in faces)
        {
            yield return face.A;
            yield return face.B;
            yield return face.C;
        }
    }

    private static List<Vector3> UniqueStructuralPoints(IEnumerable<Vector3> points)
    {
        var unique = new List<Vector3>();
        foreach (Vector3 point in points)
            if (!unique.Any(existing => StructuralPointsNear(existing, point)))
                unique.Add(point);
        return unique;
    }

    private static bool StructuralPointsNear(Vector3 first, Vector3 second) =>
        (first - second).sqrMagnitude <= StructuralPointTolerance * StructuralPointTolerance;

    private static bool StructuralPointSetsEqual(List<Vector3> first, List<Vector3> second) =>
        first.Count == second.Count &&
        first.All(point => second.Count(candidate => StructuralPointsNear(point, candidate)) == 1) &&
        second.All(point => first.Count(candidate => StructuralPointsNear(point, candidate)) == 1);

    private static bool StructuralTrianglesMatchOpposite(StructuralFace first, StructuralFace second)
    {
        if (!StructuralTrianglesCoincide(first, second))
            return false;
        Vector3 firstNormal = first.AreaNormal.normalized;
        Vector3 secondNormal = second.AreaNormal.normalized;
        return firstNormal.sqrMagnitude > 0.5f && secondNormal.sqrMagnitude > 0.5f &&
            Vector3.Dot(firstNormal, secondNormal) < -0.999f;
    }

    private static bool StructuralTrianglesCoincide(StructuralFace first, StructuralFace second)
    {
        Vector3[] firstPoints = { first.A, first.B, first.C };
        Vector3[] secondPoints = { second.A, second.B, second.C };
        return firstPoints.All(point =>
                   secondPoints.Count(candidate => StructuralPointsNear(point, candidate)) == 1) &&
               secondPoints.All(point =>
                   firstPoints.Count(candidate => StructuralPointsNear(point, candidate)) == 1);
    }

    private static bool StructuralFacesFormSingleDisk(List<StructuralFace> faces)
    {
        List<Vector3> vertices = UniqueStructuralPoints(FacePoints(faces));
        if (faces.Count == 0 || vertices.Count != faces.Count + 2)
            return false;
        var edgeCounts = new Dictionary<long, int>();
        foreach (StructuralFace face in faces)
        {
            int[] indices =
            {
                vertices.FindIndex(point => StructuralPointsNear(point, face.A)),
                vertices.FindIndex(point => StructuralPointsNear(point, face.B)),
                vertices.FindIndex(point => StructuralPointsNear(point, face.C))
            };
            if (indices.Any(index => index < 0) || indices.Distinct().Count() != 3)
                return false;
            for (int edge = 0; edge < 3; edge++)
            {
                int first = Mathf.Min(indices[edge], indices[(edge + 1) % 3]);
                int second = Mathf.Max(indices[edge], indices[(edge + 1) % 3]);
                long key = ((long)first << 32) | (uint)second;
                edgeCounts[key] = edgeCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }
        if (edgeCounts.Values.Any(count => count < 1 || count > 2) ||
            vertices.Count - edgeCounts.Count + faces.Count != 1)
            return false;
        long[] boundaryEdges = edgeCounts.Where(pair => pair.Value == 1).Select(pair => pair.Key).ToArray();
        if (boundaryEdges.Length != vertices.Count)
            return false;
        int[] degrees = new int[vertices.Count];
        var adjacency = Enumerable.Range(0, vertices.Count).Select(_ => new List<int>()).ToArray();
        foreach (long edge in boundaryEdges)
        {
            int first = (int)(edge >> 32);
            int second = (int)(uint)edge;
            degrees[first]++;
            degrees[second]++;
            adjacency[first].Add(second);
            adjacency[second].Add(first);
        }
        if (degrees.Any(degree => degree != 2))
            return false;
        var visited = new HashSet<int> { 0 };
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.Count > 0)
            foreach (int neighbor in adjacency[pending.Dequeue()])
                if (visited.Add(neighbor))
                    pending.Enqueue(neighbor);
        return visited.Count == vertices.Count;
    }

    private static List<Vector3> ExteriorBoundaryPoints(List<StructuralFace> faces)
    {
        var vertices = new List<Vector3>();
        var vertexIndices = new Dictionary<Vector3Int, int>();
        var edgeCounts = new Dictionary<long, int>();
        foreach (StructuralFace face in faces)
        {
            Vector3[] points = { face.A, face.B, face.C };
            var indices = new int[3];
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                Vector3 point = points[pointIndex];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(point.x / StructuralPointTolerance),
                    Mathf.RoundToInt(point.y / StructuralPointTolerance),
                    Mathf.RoundToInt(point.z / StructuralPointTolerance));
                if (!vertexIndices.TryGetValue(key, out int vertexIndex))
                {
                    vertexIndex = vertices.Count;
                    vertexIndices.Add(key, vertexIndex);
                    vertices.Add(point);
                }
                indices[pointIndex] = vertexIndex;
            }
            for (int edge = 0; edge < 3; edge++)
            {
                int first = Mathf.Min(indices[edge], indices[(edge + 1) % 3]);
                int second = Mathf.Max(indices[edge], indices[(edge + 1) % 3]);
                long key = ((long)first << 32) | (uint)second;
                edgeCounts[key] = edgeCounts.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }
        var result = new List<Vector3>();
        foreach (long edge in edgeCounts.Where(pair => pair.Value == 1).Select(pair => pair.Key))
        {
            int first = (int)(edge >> 32);
            int second = (int)(uint)edge;
            if (!result.Any(point => StructuralPointsNear(point, vertices[first])))
                result.Add(vertices[first]);
            if (!result.Any(point => StructuralPointsNear(point, vertices[second])))
                result.Add(vertices[second]);
        }
        return result;
    }

    private static string[] FindForbiddenDamageArtifacts(GameObject prefab)
    {
        string[] tokens = { "_Skin_", "_SeamCap", "_SeamCaps", "DamageInterior" };
        var findings = new List<string>();
        void AddIfForbidden(string label, string value)
        {
            if (!string.IsNullOrEmpty(value) && tokens.Any(token =>
                    value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                findings.Add(label + "=" + value);
        }

        foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
            AddIfForbidden("object", GetPath(prefab.transform, transform));
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            AddIfForbidden("mesh", filter.sharedMesh.name);
            AddIfForbidden("meshAsset", AssetDatabase.GetAssetPath(filter.sharedMesh));
        }
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials.Where(material => material != null))
            {
                AddIfForbidden("material", material.name);
                AddIfForbidden("materialAsset", AssetDatabase.GetAssetPath(material));
            }
        foreach (string guid in AssetDatabase.FindAssets(string.Empty,
                     new[] { "Assets/F117/Generated" }))
            AddIfForbidden("generatedAsset", AssetDatabase.GUIDToAssetPath(guid));
        return findings.Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateProfileSlotClassification(GameObject prefab, List<string> failures)
    {
        var bodyFamilies = new HashSet<string>(StringComparer.Ordinal);
        int bodySlots = 0;
        int frameSlots = 0;
        int staticAccessorySlots = 0;
        int tireSlots = 0;
        var invalidTargets = new List<string>();
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            bool overlay = renderer.name.StartsWith(
                F117AircraftAssembler.ParadeFlagOverlayPrefix,
                StringComparison.Ordinal);
            bool excluded = ProfileHierarchyExcluded(renderer.transform) || overlay;
            bool staticAccessory = ProfileStaticAccessory(renderer.transform);
            foreach (Material material in renderer.sharedMaterials)
            {
                string canonical = CanonicalProfileMaterialName(material == null ? null : material.name);
                bool frame = renderer.name == "F117_Canopy_Mesh" && canonical == "INT_CockpitFrame";
                bool tire = canonical == "F117_Tires";
                bool exterior = canonical != null && canonical.StartsWith("F117_EXTERNAL_", StringComparison.Ordinal) &&
                    canonical.Length == "F117_EXTERNAL_1".Length &&
                    canonical[canonical.Length - 1] >= '1' && canonical[canonical.Length - 1] <= '7';
                bool gearDoorExterior = exterior && !overlay &&
                    canonical == "F117_EXTERNAL_2" &&
                    ProfileGearDoorExteriorSkin(renderer.transform);
                bool body = exterior && ((!excluded && !staticAccessory) || gearDoorExterior);
                if (body)
                {
                    bodySlots++;
                    bodyFamilies.Add(canonical);
                    if (!gearDoorExterior &&
                        (ProfileHierarchyExcluded(renderer.transform) || staticAccessory))
                        invalidTargets.Add(GetPath(prefab.transform, renderer.transform));
                }
                if (exterior && staticAccessory && !gearDoorExterior)
                    staticAccessorySlots++;
                if (tire)
                    tireSlots++;
                if (frame)
                    frameSlots++;
            }
        }
        Require(bodySlots >= 7 && bodyFamilies.Count == 7,
            "Profile classification includes all seven AircraftSkin exterior families", failures);
        Require(frameSlots == 1,
            "Profile classification includes exactly the canopy-frame INT_CockpitFrame slot", failures);
        Require(staticAccessorySlots >= 40 && tireSlots == 3 && invalidTargets.Count == 0,
            "Profile classification includes only landing-gear-door EXTERNAL_2 skins in body tint " +
            "and isolates mechanisms, bay linkages, drag chute, and all three tires",
            failures);
    }

    private static bool ProfileGearDoorExteriorSkin(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
            if ((current.name ?? string.Empty).StartsWith("F117_GearDoor_",
                    StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool ProfileStaticAccessory(Transform transform)
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

    private static bool ProfileHierarchyExcluded(Transform transform)
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

    private static string CanonicalProfileMaterialName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        string result = name;
        if (result.Length > 3 && char.IsDigit(result[0]) && char.IsDigit(result[1]) && result[2] == '_')
            result = result.Substring(3);
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

    private static void ValidateMatteFinishTexture(int panel, string expectedGuid, List<string> failures)
    {
        string sourcePath = "Assets/F117/Textures/f117_ext_" + panel + "_comp.png";
        string finishPath = "Assets/F117/Textures/f117_ext_" + panel + "_ms.png";
        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(finishPath);
        TextureImporter importer = AssetImporter.GetAtPath(finishPath) as TextureImporter;
        Require(imported != null && importer != null &&
                AssetDatabase.AssetPathToGUID(finishPath) == expectedGuid,
            "Panel " + panel + " matte MS loads from its exact path and GUID", failures);
        Require(importer != null && importer.textureType == TextureImporterType.Default &&
                !importer.sRGBTexture && !importer.alphaIsTransparency && importer.mipmapEnabled &&
                importer.npotScale == TextureImporterNPOTScale.None && importer.filterMode == FilterMode.Bilinear &&
                importer.anisoLevel == 1 && importer.wrapModeU == TextureWrapMode.Repeat &&
                importer.wrapModeV == TextureWrapMode.Repeat && importer.maxTextureSize == 2048,
            "Panel " + panel + " matte MS imports as linear packed material data with audited sampling", failures);

        Texture2D source = LoadRawPng(sourcePath);
        Texture2D finish = LoadRawPng(finishPath);
        try
        {
            Require(source != null && finish != null && source.width == finish.width &&
                    source.height == finish.height && imported != null &&
                    imported.width == source.width && imported.height == source.height,
                "Panel " + panel + " matte MS preserves source resolution", failures);
            if (source == null || finish == null || source.width != finish.width || source.height != finish.height)
                return;
            Color32[] sourcePixels = source.GetPixels32();
            Color32[] finishPixels = finish.GetPixels32();
            int invalid = 0;
            for (int index = 0; index < sourcePixels.Length; index++)
            {
                byte metallic = sourcePixels[index].b;
                byte smoothness = (byte)(255 - sourcePixels[index].g);
                Color32 actual = finishPixels[index];
                if (actual.r != metallic || actual.g != metallic || actual.b != metallic ||
                    actual.a != smoothness)
                    invalid++;
            }
            Require(invalid == 0,
                "Panel " + panel + " matte MS packs RGB=source metallic B and A=1-source roughness G" +
                (invalid == 0 ? string.Empty : " (invalid pixels: " + invalid + ")"), failures);
        }
        finally
        {
            if (source != null)
                UnityEngine.Object.DestroyImmediate(source);
            if (finish != null)
                UnityEngine.Object.DestroyImmediate(finish);
        }
    }

    private static void ValidateMirrorFinishTexture(List<string> failures)
    {
        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(MirrorFinishTexturePath);
        TextureImporter importer = AssetImporter.GetAtPath(MirrorFinishTexturePath) as TextureImporter;
        Texture2D raw = LoadRawPng(MirrorFinishTexturePath);
        try
        {
            Color32 pixel = raw == null ? default : raw.GetPixel(0, 0);
            Require(imported != null && raw != null && raw.width == 1 && raw.height == 1 &&
                    pixel.r == 255 && pixel.g == 255 && pixel.b == 255 && pixel.a == 240 &&
                    AssetDatabase.AssetPathToGUID(MirrorFinishTexturePath) == MirrorFinishTextureGuid,
                "Bundled mirror MS is exact RGBA=(1,1,1,0.94) at its fixed path and GUID", failures);
            Require(importer != null && importer.textureType == TextureImporterType.Default &&
                    !importer.sRGBTexture && !importer.alphaIsTransparency,
                "Bundled mirror MS imports as linear material data, not a color/sprite texture",
                failures);
        }
        finally
        {
            if (raw != null)
                UnityEngine.Object.DestroyImmediate(raw);
        }
    }

    private static Texture2D LoadRawPng(string assetPath)
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        if (!File.Exists(path))
            return null;
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (texture.LoadImage(File.ReadAllBytes(path), false))
            return texture;
        UnityEngine.Object.DestroyImmediate(texture);
        return null;
    }

    private static void ValidateParadeFlagTextureAssets(List<string> failures)
    {
        string masterPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", ParadeFlagTexturePath));
        string wrapPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", ParadeFlagWrapTexturePath));
        Require(FileSha256(masterPath) == ParadeFlagTextureSha256,
            "Farewell-flag photographic master matches its independently pinned SHA-256",
            failures);
        Require(FileSha256(wrapPath) == ParadeFlagWrapTextureSha256,
            "Farewell-flag runtime wrap matches its independently pinned SHA-256",
            failures);

        TextureImporter importer =
            AssetImporter.GetAtPath(ParadeFlagWrapTexturePath) as TextureImporter;
        Require(importer != null && importer.textureType == TextureImporterType.Default &&
                importer.sRGBTexture && importer.mipmapEnabled && !importer.isReadable &&
                importer.wrapMode == TextureWrapMode.Clamp &&
                importer.filterMode == FilterMode.Trilinear &&
                importer.anisoLevel == 16 && importer.maxTextureSize >= 4096 &&
                importer.textureCompression == TextureImporterCompression.Uncompressed,
            "Farewell-flag wrap imports as sRGB, clamped, trilinear/aniso16, mipmapped, " +
            "uncompressed 4K content", failures);
    }

    private static string FileSha256(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return string.Concat(sha.ComputeHash(stream)
                .Select(value => value.ToString("x2")));
    }

    private static string ParadeSourceDigest(IEnumerable<MeshFilter> sources,
        Transform visualRoot, bool downwardOnly, bool distinct)
    {
        var records = new List<string>();
        foreach (MeshFilter source in sources)
        {
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = source.GetComponent<Renderer>().sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (subMesh >= materials.Length ||
                    !F117AircraftAssembler.IsParadeFlagMaterial(
                        materials[subMesh], source.transform.name))
                    continue;
                string family = F117AircraftAssembler.ParadeFlagMaterialFamily(
                    materials[subMesh]);
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    Vector3[] rootPoints =
                    {
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle]])),
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle + 1]])),
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle + 2]]))
                    };
                    long[][] points = rootPoints.Select(point => new[]
                    {
                        ParadeQuantizedInteger(point.x),
                        ParadeQuantizedInteger(point.y),
                        ParadeQuantizedInteger(point.z)
                    }).ToArray();
                    Vector3[] weldedPoints = rootPoints.Select(point => new Vector3(
                        ParadeQuantized(point.x), ParadeQuantized(point.y),
                        ParadeQuantized(point.z))).ToArray();
                    if (downwardOnly && !ParadeFaceIsDownward(
                            weldedPoints[0], weldedPoints[1], weldedPoints[2]))
                        continue;
                    Array.Sort(points, CompareParadeIntegerPoint);
                    var record = new StringBuilder("face");
                    record.Append('\t').Append(source.transform.name);
                    record.Append('\t').Append(family);
                    foreach (long[] point in points)
                    foreach (long value in point)
                        record.Append('\t').Append(
                            value.ToString(CultureInfo.InvariantCulture));
                    records.Add(record.ToString());
                }
            }
        }
        if (distinct)
            records = records.Distinct(StringComparer.Ordinal).ToList();
        records.Sort(StringComparer.Ordinal);

        string[] owners = F117AircraftAssembler.ParadeFlagSurfaceNames
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] pairs =
        {
            "F117_BayDoor_Left_Mesh\tF117_EXTERNAL_5",
            "F117_BayDoor_Right_Mesh\tF117_EXTERNAL_5",
            "F117_Elevon_L_Inner_Mesh\tF117_EXTERNAL_3",
            "F117_Elevon_L_Outer_Mesh\tF117_EXTERNAL_3",
            "F117_Elevon_R_Inner_Mesh\tF117_EXTERNAL_4",
            "F117_Elevon_R_Outer_Mesh\tF117_EXTERNAL_4",
            "F117_Exterior_LeftWing_Mesh\tF117_EXTERNAL_3",
            "F117_Exterior_Mesh\tF117_EXTERNAL_1",
            "F117_Exterior_Mesh\tF117_EXTERNAL_2",
            "F117_Exterior_Mesh\tF117_EXTERNAL_3",
            "F117_Exterior_Mesh\tF117_EXTERNAL_4",
            "F117_Exterior_Mesh\tF117_EXTERNAL_5",
            "F117_Exterior_Mesh\tF117_EXTERNAL_6",
            "F117_Exterior_RightWing_Mesh\tF117_EXTERNAL_4",
            "F117_GearDoor_Left_Inner_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Left_Outer_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Nose_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Right_Inner_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Right_Outer_Mesh\tF117_EXTERNAL_2"
        };
        var payload = new StringBuilder();
        payload.Append("schema\tf117-parade-source-v2\n");
        payload.Append("profile\tapproved-fourteen-owner-material\n");
        payload.Append("stage\t")
            .Append(downwardOnly ? "downward-selected" : "eligible-all-normal")
            .Append('\n');
        payload.Append("quantum_nm\t10000\n");
        payload.Append("selection_dot_down_min\t")
            .Append(F117AircraftAssembler.ParadeFlagMinimumDownwardDot
                .ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        foreach (string owner in owners)
            payload.Append("owner\t").Append(owner).Append('\n');
        foreach (string pair in pairs)
            payload.Append("eligible_pair\t").Append(pair).Append('\n');
        payload.Append("record_count\t")
            .Append(records.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (string record in records)
            payload.Append(record).Append('\n');

        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.ASCII.GetBytes(payload.ToString()));
            return string.Concat(hash.Select(value => value.ToString("x2")));
        }
    }

    private static long ParadeQuantizedInteger(float value)
    {
        return (long)Math.Round(
            value / F117AircraftAssembler.ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
    }

    private static int CompareParadeIntegerPoint(long[] first, long[] second)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            int comparison = first[axis].CompareTo(second[axis]);
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }

    private readonly struct ParadeTriangle
    {
        internal readonly Vector3 A;
        internal readonly Vector3 B;
        internal readonly Vector3 C;

        internal ParadeTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private static void ValidateParadeFlagOverlays(Transform prefabRoot, Transform visualRoot,
        Renderer[] overlays, List<string> failures)
    {
        string[] hingeNames =
        {
            "F117_GearDoor_Nose_CloseHinge",
            "F117_GearDoor_Left_Outer_CloseHinge",
            "F117_GearDoor_Left_Inner_CloseHinge",
            "F117_GearDoor_Right_Outer_CloseHinge",
            "F117_GearDoor_Right_Inner_CloseHinge"
        };
        Transform[] allTransforms = prefabRoot == null
            ? Array.Empty<Transform>()
            : prefabRoot.GetComponentsInChildren<Transform>(true);
        Transform[] hinges = hingeNames.Select(name => allTransforms
            .SingleOrDefault(transform => transform.name == name)).ToArray();
        Require(hinges.All(hinge => hinge != null),
            "Farewell-flag validation found all five native landing-gear close hinges",
            failures);
        if (hinges.Any(hinge => hinge == null))
            return;

        Quaternion[] authoredRotations = hinges
            .Select(hinge => hinge.localRotation).ToArray();
        try
        {
            foreach (Transform hinge in hinges)
                hinge.localRotation = Quaternion.identity;
            ValidateParadeFlagOverlaysClosed(prefabRoot, visualRoot, overlays, failures);
        }
        finally
        {
            for (int index = 0; index < hinges.Length; index++)
                hinges[index].localRotation = authoredRotations[index];
        }
    }

    private static void ValidateParadeFlagOverlaysClosed(Transform prefabRoot,
        Transform visualRoot, Renderer[] overlays, List<string> failures)
    {
        if (visualRoot == null)
            return;

        MeshFilter[] allFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
        var sources = new List<MeshFilter>();
        foreach (string sourceName in F117AircraftAssembler.ParadeFlagSurfaceNames)
        {
            MeshFilter[] matches = allFilters
                .Where(filter => filter.transform.name == sourceName && filter.sharedMesh != null)
                .ToArray();
            Require(matches.Length == 1,
                sourceName + " is the one authoritative farewell-flag source surface", failures);
            if (matches.Length == 1)
                sources.Add(matches[0]);
        }
        if (sources.Count != F117AircraftAssembler.ParadeFlagSurfaceNames.Length)
            return;

        Bounds planform = new Bounds();
        bool initialized = false;
        var eligibleCounts = new int[sources.Count];
        var downwardCounts = new int[sources.Count];
        var downwardFaces = new HashSet<string>(StringComparer.Ordinal);
        var duplicateDownwardFaces = new List<string>();
        var eligiblePairs = new HashSet<string>(StringComparer.Ordinal);
        for (int ownerIndex = 0; ownerIndex < sources.Count; ownerIndex++)
        {
            MeshFilter source = sources[ownerIndex];
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = source.GetComponent<Renderer>().sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (subMesh >= materials.Length ||
                    !F117AircraftAssembler.IsParadeFlagMaterial(
                        materials[subMesh], source.transform.name))
                    continue;
                string family = F117AircraftAssembler.ParadeFlagMaterialFamily(
                    materials[subMesh]);
                eligiblePairs.Add(source.transform.name + "\t" + family);
                int[] triangles = mesh.GetTriangles(subMesh);
                eligibleCounts[ownerIndex] += triangles.Length / 3;
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    var rootPoints = new[]
                    {
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle]])),
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle + 1]])),
                        visualRoot.InverseTransformPoint(source.transform.TransformPoint(
                            vertices[triangles[triangle + 2]]))
                    };
                    foreach (Vector3 point in rootPoints)
                    {
                        if (!initialized)
                        {
                            planform = new Bounds(point, Vector3.zero);
                            initialized = true;
                        }
                        else
                            planform.Encapsulate(point);
                    }
                    Vector3[] weldedPoints = rootPoints.Select(point => new Vector3(
                        ParadeQuantized(point.x), ParadeQuantized(point.y),
                        ParadeQuantized(point.z))).ToArray();
                    if (!ParadeFaceIsDownward(
                            weldedPoints[0], weldedPoints[1], weldedPoints[2]))
                        continue;
                    downwardCounts[ownerIndex]++;
                    string face = ParadeFaceKey(rootPoints[0], rootPoints[1], rootPoints[2]);
                    string ownedFace = source.transform.name + "\t" + face;
                    if (!downwardFaces.Add(ownedFace))
                        duplicateDownwardFaces.Add(ownedFace);
                }
            }
        }
        Require(initialized && planform.size.x >= 10f && planform.size.z >= 15f,
            "Farewell-flag UV projection uses the full eligible exterior-skin planform", failures);
        if (!initialized)
            return;
        Require(eligibleCounts.SequenceEqual(
                F117AircraftAssembler.ParadeFlagEligibleSourceTriangleCounts),
            "Farewell-flag exact owner/material source counts match the pinned " +
            F117AircraftAssembler.ParadeFlagEligibleSourceTriangleCounts.Sum() +
            "-triangle Unity import (actual: " + string.Join(",", eligibleCounts) + ")",
            failures);
        Require(downwardCounts.SequenceEqual(
                F117AircraftAssembler.ParadeFlagDownwardSourceTriangleCounts),
            "Farewell-flag downward source counts match the pinned " +
            F117AircraftAssembler.ParadeFlagDownwardSourceTriangleCounts.Sum() +
            "-triangle welded Unity " +
            "manifest (actual: " + string.Join(",", downwardCounts) + ")", failures);
        Require(duplicateDownwardFaces.Count == 0,
            "Farewell-flag approved downward source has zero duplicate stable face identities" +
            (duplicateDownwardFaces.Count == 0 ? string.Empty :
                " (duplicates: " + duplicateDownwardFaces.Count + ")"), failures);
        string[] expectedEligiblePairs =
        {
            "F117_Exterior_Mesh\tF117_EXTERNAL_1",
            "F117_Exterior_Mesh\tF117_EXTERNAL_2",
            "F117_Exterior_Mesh\tF117_EXTERNAL_3",
            "F117_Exterior_Mesh\tF117_EXTERNAL_4",
            "F117_Exterior_Mesh\tF117_EXTERNAL_5",
            "F117_Exterior_Mesh\tF117_EXTERNAL_6",
            "F117_Exterior_LeftWing_Mesh\tF117_EXTERNAL_3",
            "F117_Exterior_RightWing_Mesh\tF117_EXTERNAL_4",
            "F117_BayDoor_Left_Mesh\tF117_EXTERNAL_5",
            "F117_BayDoor_Right_Mesh\tF117_EXTERNAL_5",
            "F117_Elevon_L_Inner_Mesh\tF117_EXTERNAL_3",
            "F117_Elevon_L_Outer_Mesh\tF117_EXTERNAL_3",
            "F117_Elevon_R_Inner_Mesh\tF117_EXTERNAL_4",
            "F117_Elevon_R_Outer_Mesh\tF117_EXTERNAL_4",
            "F117_GearDoor_Nose_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Left_Outer_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Left_Inner_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Right_Outer_Mesh\tF117_EXTERNAL_2",
            "F117_GearDoor_Right_Inner_Mesh\tF117_EXTERNAL_2"
        };
        Require(eligiblePairs.SetEquals(expectedEligiblePairs),
            "Farewell-flag material eligibility is exactly the independently approved " +
            "fourteen-owner/nineteen-pair manifest; inner mechanisms and bay-door " +
            "EXTERNAL_7/.001 are excluded",
            failures);
        string eligibleMultisetDigest =
            ParadeSourceDigest(sources, visualRoot, false, false);
        Require(eligibleMultisetDigest == ParadeFlagFbxEligibleMultisetSha256,
            "Farewell-flag eligible FBX source multiset matches its independent " +
            "Unity-import digest (actual " + eligibleMultisetDigest + ")", failures);
        string eligibleSetDigest =
            ParadeSourceDigest(sources, visualRoot, false, true);
        Require(eligibleSetDigest == ParadeFlagFbxEligibleSetSha256,
            "Farewell-flag eligible FBX source set matches its independent " +
            "Unity-import digest (actual " + eligibleSetDigest + ")", failures);
        string downwardDigest =
            ParadeSourceDigest(sources, visualRoot, true, false);
        Require(downwardDigest == ParadeFlagFbxDownwardSha256,
            "Farewell-flag downward FBX source matches its independent " +
            "Unity-import digest (actual " + downwardDigest + ")", failures);

        foreach (MeshFilter source in sources)
        {
            string suffix = "_" + source.transform.name;
            Renderer[] matches = overlays
                .Where(renderer => renderer.name.EndsWith(suffix, StringComparison.Ordinal))
                .ToArray();
            Require(matches.Length == 1,
                source.transform.name + " owns exactly one farewell-flag overlay", failures);
            if (matches.Length != 1)
                continue;

            Renderer overlay = matches[0];
            Transform expectedParent = source.transform.name == "F117_Exterior_Mesh"
                ? visualRoot
                : source.transform.parent;
            Require(overlay.transform.parent == expectedParent,
                source.transform.name + " overlay follows the same central/wing/moving-surface owner", failures);
            Require(OverlayFacesDown(overlay, visualRoot),
                source.transform.name + " overlay contains only geometric downward faces", failures);
            ValidateParadeOverlayGeometry(source, overlay, visualRoot, planform, failures);
        }

        ValidateParadeOwnerSeams(sources, overlays, visualRoot, failures);
        ValidateParadeBottomVisibility(prefabRoot, overlays, visualRoot, failures);
        ValidateParadeDecalClearance(sources.Take(3).ToArray(), visualRoot, failures);
    }

    private static void ValidateParadeOverlayGeometry(MeshFilter source, Renderer overlay,
        Transform visualRoot, Bounds planform, List<string> failures)
    {
        MeshFilter overlayFilter = overlay.GetComponent<MeshFilter>();
        Mesh mesh = overlayFilter == null ? null : overlayFilter.sharedMesh;
        Require(mesh != null && mesh.vertexCount > 0,
            source.transform.name + " has non-empty generated underside geometry", failures);
        if (mesh == null || mesh.vertexCount == 0)
            return;
        Vector3[] actualVertices = mesh.vertices;
        Vector2[] actualUvs = mesh.uv;
        int[] actualTriangles = mesh.triangles;
        bool validIndices = actualTriangles.Length >= 3 &&
            actualTriangles.Length % 3 == 0 &&
            actualTriangles.All(index => index >= 0 && index < actualVertices.Length);
        Require(validIndices,
            source.transform.name + " overlay has valid indexed triangle topology", failures);
        if (!validIndices)
            return;

        Require(actualUvs.Length == actualVertices.Length &&
                mesh.normals != null && mesh.normals.Length == actualVertices.Length,
            source.transform.name + " overlay has one UV and normal per welded vertex", failures);
        Require(actualVertices.Length < actualTriangles.Length &&
                actualTriangles.Distinct().Count() == actualVertices.Length,
            source.transform.name + " overlay is indexed/welded rather than triangle soup" +
            " (vertices " + actualVertices.Length + ", indices " + actualTriangles.Length + ")",
            failures);

        List<ParadeTriangle> sourceTriangles =
            DownwardParadeTriangles(source, visualRoot);
        var sourceTriangleIndex = new ParadeTriangleSpatialIndex(sourceTriangles, 0.5f,
            0.00003f);
        var rootPoints = new Vector3[actualVertices.Length];
        var vertexKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        int duplicateVertices = 0;
        float maximumUvError = 0f;
        for (int index = 0; index < actualVertices.Length; index++)
        {
            Vector3 actualRoot = visualRoot.InverseTransformPoint(
                overlay.transform.TransformPoint(actualVertices[index]));
            Vector3 sourceRoot = actualRoot +
                Vector3.up * F117AircraftAssembler.ParadeFlagSurfaceOffset;
            rootPoints[index] = sourceRoot;
            string pointKey = ParadePointKey(sourceRoot);
            if (vertexKeys.ContainsKey(pointKey))
                duplicateVertices++;
            else
                vertexKeys.Add(pointKey, index);
            Vector2 expectedUv = new Vector2(
                Mathf.InverseLerp(planform.max.z, planform.min.z, sourceRoot.z),
                Mathf.InverseLerp(planform.min.x, planform.max.x, sourceRoot.x));
            maximumUvError = Mathf.Max(maximumUvError,
                index < actualUvs.Length
                    ? Vector2.Distance(actualUvs[index], expectedUv)
                    : float.PositiveInfinity);
        }
        Require(duplicateVertices == 0,
            source.transform.name + " overlay has exactly one indexed vertex per 10 um " +
            "root-space position (duplicates " + duplicateVertices + ")", failures);
        Require(maximumUvError <= 0.00001f,
            source.transform.name + " overlay preserves the shared root-space X/Z flag projection" +
            " (maximum UV error " + maximumUvError.ToString("0.000000") + ")", failures);

        var faces = new HashSet<string>(StringComparer.Ordinal);
        var edges = new Dictionary<string, int>(StringComparer.Ordinal);
        int unsupported = 0;
        int duplicateFaces = 0;
        int wrongFacing = 0;
        var unsupportedDetails = new List<string>();
        var wrongFacingDetails = new List<string>();
        for (int triangle = 0; triangle + 2 < actualTriangles.Length; triangle += 3)
        {
            Vector3 a = rootPoints[actualTriangles[triangle]];
            Vector3 b = rootPoints[actualTriangles[triangle + 1]];
            Vector3 c = rootPoints[actualTriangles[triangle + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            double magnitude = Math.Sqrt((double)normal.x * normal.x +
                (double)normal.y * normal.y + (double)normal.z * normal.z);
            double downwardDot = magnitude == 0d
                ? double.NegativeInfinity
                : -normal.y / magnitude;
            if (downwardDot < ParadeFlagOutputMinimumDownwardDot)
            {
                wrongFacing++;
                if (wrongFacingDetails.Count < 8)
                    wrongFacingDetails.Add(ParadeFaceKey(a, b, c) + " dot=" +
                        downwardDot.ToString("R"));
            }
            string face = ParadeFaceKey(a, b, c);
            if (!faces.Add(face))
                duplicateFaces++;
            AddParadeEdge(edges, a, b);
            AddParadeEdge(edges, b, c);
            AddParadeEdge(edges, c, a);

            Vector3 centroid = (a + b + c) / 3f;
            if (!ParadePointSupported(a, sourceTriangleIndex.Query(a)) ||
                !ParadePointSupported(b, sourceTriangleIndex.Query(b)) ||
                !ParadePointSupported(c, sourceTriangleIndex.Query(c)) ||
                !ParadePointSupported(centroid, sourceTriangleIndex.Query(centroid)))
            {
                unsupported++;
                if (unsupportedDetails.Count < 8)
                    unsupportedDetails.Add(ParadeFaceKey(a, b, c));
            }
        }
        Require(wrongFacing == 0,
            source.transform.name + " overlay contains only outward-downward faces" +
            " (wrong " + wrongFacing +
            (wrongFacingDetails.Count == 0 ? ")" : ": " +
                string.Join("; ", wrongFacingDetails) + ")"), failures);
        Require(duplicateFaces == 0,
            source.transform.name + " overlay has zero duplicate stable face identities", failures);
        Require(edges.Values.All(count => count == 1 || count == 2),
            source.transform.name + " overlay has manifold welded edge incidence", failures);
        Require(unsupported == 0,
            source.transform.name + " overlay is uniformly root-down-offset from its own " +
            "approved exterior source facets (unsupported triangles " + unsupported +
            (unsupportedDetails.Count == 0 ? ")" : ": " +
                string.Join("; ", unsupportedDetails) + ")"), failures);
    }

    private static List<ParadeTriangle> DownwardParadeTriangles(MeshFilter source,
        Transform visualRoot)
    {
        var result = new List<ParadeTriangle>();
        Mesh mesh = source.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Material[] materials = source.GetComponent<Renderer>().sharedMaterials;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            if (subMesh >= materials.Length ||
                !F117AircraftAssembler.IsParadeFlagMaterial(
                    materials[subMesh], source.transform.name))
                continue;
            int[] triangles = mesh.GetTriangles(subMesh);
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                Vector3 a = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle]]);
                Vector3 b = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle + 1]]);
                Vector3 c = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle + 2]]);
                if (!ParadeFaceIsDownward(a, b, c) ||
                    !F117AircraftAssembler.ParadePolygonAreaIsResolved(new[]
                    {
                        new Vector2(a.x, a.z),
                        new Vector2(b.x, b.z),
                        new Vector2(c.x, c.z)
                    }))
                    continue;
                result.Add(new ParadeTriangle(a, b, c));
            }
        }
        return result;
    }

    private static bool ParadeFaceIsDownward(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 cross = Vector3.Cross(b - a, c - a);
        double magnitude = ParadeMagnitude(cross);
        return magnitude != 0d && -cross.y / magnitude >=
            F117AircraftAssembler.ParadeFlagMinimumDownwardDot;
    }

    private static Vector3 ParadeQuantizedRootPoint(Transform visualRoot,
        MeshFilter source, Vector3 localPoint)
    {
        Vector3 rootPoint = visualRoot.InverseTransformPoint(
            source.transform.TransformPoint(localPoint));
        return new Vector3(
            ParadeQuantized(rootPoint.x),
            ParadeQuantized(rootPoint.y),
            ParadeQuantized(rootPoint.z));
    }

    private static float ParadeQuantized(float value)
    {
        long key = (long)Math.Round(
            value / F117AircraftAssembler.ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
        return (float)(key * F117AircraftAssembler.ParadeFlagWeldTolerance);
    }

    private static string ParadePointKey(Vector3 point)
    {
        long x = (long)Math.Round(point.x / F117AircraftAssembler.ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
        long y = (long)Math.Round(point.y / F117AircraftAssembler.ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
        long z = (long)Math.Round(point.z / F117AircraftAssembler.ParadeFlagWeldTolerance,
            MidpointRounding.AwayFromZero);
        return x + "," + y + "," + z;
    }

    private static string ParadeFaceKey(Vector3 a, Vector3 b, Vector3 c)
    {
        string[] points = { ParadePointKey(a), ParadePointKey(b), ParadePointKey(c) };
        Array.Sort(points, StringComparer.Ordinal);
        return string.Join("|", points);
    }

    private static string ParadeEdgeKey(Vector3 a, Vector3 b)
    {
        string first = ParadePointKey(a);
        string second = ParadePointKey(b);
        return string.CompareOrdinal(first, second) <= 0
            ? first + "|" + second
            : second + "|" + first;
    }

    private static void AddParadeEdge(Dictionary<string, int> edges,
        Vector3 a, Vector3 b)
    {
        string edge = ParadeEdgeKey(a, b);
        edges[edge] = edges.TryGetValue(edge, out int count) ? count + 1 : 1;
    }

    private static bool ParadePointSupported(Vector3 point,
        IEnumerable<ParadeTriangle> sourceTriangles)
    {
        const float tolerance = 0.00003f;
        foreach (ParadeTriangle triangle in sourceTriangles)
        {
            float minX = Mathf.Min(triangle.A.x, Mathf.Min(triangle.B.x, triangle.C.x));
            float maxX = Mathf.Max(triangle.A.x, Mathf.Max(triangle.B.x, triangle.C.x));
            float minZ = Mathf.Min(triangle.A.z, Mathf.Min(triangle.B.z, triangle.C.z));
            float maxZ = Mathf.Max(triangle.A.z, Mathf.Max(triangle.B.z, triangle.C.z));
            if (point.x < minX - tolerance || point.x > maxX + tolerance ||
                point.z < minZ - tolerance || point.z > maxZ + tolerance)
                continue;
            if (ParadePointTriangleDistancePrecise(point,
                    triangle.A, triangle.B, triangle.C) <= tolerance)
                return true;
        }
        return false;
    }

    private static double ParadePointTriangleDistancePrecise(Vector3 point,
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

    private sealed class ParadeTriangleSpatialIndex
    {
        private readonly float cellSize;
        private readonly Dictionary<long, List<ParadeTriangle>> cells =
            new Dictionary<long, List<ParadeTriangle>>();

        internal ParadeTriangleSpatialIndex(IEnumerable<ParadeTriangle> triangles,
            float cellSize, float padding = 0f)
        {
            this.cellSize = cellSize;
            foreach (ParadeTriangle triangle in triangles)
            {
                float minX = Mathf.Min(triangle.A.x, Mathf.Min(triangle.B.x, triangle.C.x)) - padding;
                float maxX = Mathf.Max(triangle.A.x, Mathf.Max(triangle.B.x, triangle.C.x)) + padding;
                float minZ = Mathf.Min(triangle.A.z, Mathf.Min(triangle.B.z, triangle.C.z)) - padding;
                float maxZ = Mathf.Max(triangle.A.z, Mathf.Max(triangle.B.z, triangle.C.z)) + padding;
                for (int x = Mathf.FloorToInt(minX / cellSize);
                     x <= Mathf.FloorToInt(maxX / cellSize); x++)
                for (int z = Mathf.FloorToInt(minZ / cellSize);
                     z <= Mathf.FloorToInt(maxZ / cellSize); z++)
                {
                    long key = ParadeCellKey(x, z);
                    if (!cells.TryGetValue(key, out List<ParadeTriangle> values))
                    {
                        values = new List<ParadeTriangle>();
                        cells.Add(key, values);
                    }
                    values.Add(triangle);
                }
            }
        }

        internal IEnumerable<ParadeTriangle> Query(Vector3 point)
        {
            return cells.TryGetValue(ParadeCellKey(
                    Mathf.FloorToInt(point.x / cellSize),
                    Mathf.FloorToInt(point.z / cellSize)),
                out List<ParadeTriangle> values)
                ? values
                : Enumerable.Empty<ParadeTriangle>();
        }
    }

    private sealed class ParadeEdgeAccumulator
    {
        internal int Count;
        internal Vector3 A;
        internal Vector3 B;
    }

    private sealed class ParadeTopology
    {
        internal readonly Dictionary<string, ParadeEdgeAccumulator> BoundaryEdges =
            new Dictionary<string, ParadeEdgeAccumulator>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Vector2> Uvs =
            new Dictionary<string, Vector2>(StringComparer.Ordinal);
    }

    private static ParadeTopology ParadeSourceTopology(IEnumerable<ParadeTriangle> triangles)
    {
        var allEdges = new Dictionary<string, ParadeEdgeAccumulator>(StringComparer.Ordinal);
        foreach (ParadeTriangle triangle in triangles)
        {
            AccumulateParadeEdge(allEdges, triangle.A, triangle.B);
            AccumulateParadeEdge(allEdges, triangle.B, triangle.C);
            AccumulateParadeEdge(allEdges, triangle.C, triangle.A);
        }
        var result = new ParadeTopology();
        foreach (KeyValuePair<string, ParadeEdgeAccumulator> edge in allEdges)
            if (edge.Value.Count == 1)
                result.BoundaryEdges.Add(edge.Key, edge.Value);
        return result;
    }

    private static ParadeTopology ParadeOverlayTopology(Renderer overlay, Transform visualRoot)
    {
        var result = new ParadeTopology();
        MeshFilter filter = overlay == null ? null : overlay.GetComponent<MeshFilter>();
        Mesh mesh = filter == null ? null : filter.sharedMesh;
        if (mesh == null)
            return result;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        var rootPoints = new Vector3[vertices.Length];
        for (int index = 0; index < vertices.Length; index++)
        {
            Vector3 rootPoint = visualRoot.InverseTransformPoint(
                overlay.transform.TransformPoint(vertices[index])) +
                Vector3.up * F117AircraftAssembler.ParadeFlagSurfaceOffset;
            rootPoints[index] = new Vector3(
                ParadeQuantized(rootPoint.x),
                ParadeQuantized(rootPoint.y),
                ParadeQuantized(rootPoint.z));
            if (index < uvs.Length)
                result.Uvs[ParadePointKey(rootPoints[index])] = uvs[index];
        }
        var allEdges = new Dictionary<string, ParadeEdgeAccumulator>(StringComparer.Ordinal);
        for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
        {
            AccumulateParadeEdge(allEdges,
                rootPoints[triangles[triangle]], rootPoints[triangles[triangle + 1]]);
            AccumulateParadeEdge(allEdges,
                rootPoints[triangles[triangle + 1]], rootPoints[triangles[triangle + 2]]);
            AccumulateParadeEdge(allEdges,
                rootPoints[triangles[triangle + 2]], rootPoints[triangles[triangle]]);
        }
        foreach (KeyValuePair<string, ParadeEdgeAccumulator> edge in allEdges)
            if (edge.Value.Count == 1)
                result.BoundaryEdges.Add(edge.Key, edge.Value);
        return result;
    }

    private static void AccumulateParadeEdge(
        Dictionary<string, ParadeEdgeAccumulator> edges, Vector3 a, Vector3 b)
    {
        string key = ParadeEdgeKey(a, b);
        if (!edges.TryGetValue(key, out ParadeEdgeAccumulator edge))
        {
            edge = new ParadeEdgeAccumulator { A = a, B = b };
            edges.Add(key, edge);
        }
        edge.Count++;
    }

    private static List<ParadeTriangle> AllEligibleParadeTriangles(MeshFilter source,
        Transform visualRoot)
    {
        var result = new List<ParadeTriangle>();
        var faces = new HashSet<string>(StringComparer.Ordinal);
        Mesh mesh = source.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Material[] materials = source.GetComponent<Renderer>().sharedMaterials;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            if (subMesh >= materials.Length ||
                !F117AircraftAssembler.IsParadeFlagMaterial(
                    materials[subMesh], source.transform.name))
                continue;
            int[] triangles = mesh.GetTriangles(subMesh);
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                Vector3 a = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle]]);
                Vector3 b = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle + 1]]);
                Vector3 c = ParadeQuantizedRootPoint(visualRoot, source,
                    vertices[triangles[triangle + 2]]);
                if (Vector3.Cross(b - a, c - a).sqrMagnitude > 0f &&
                    faces.Add(ParadeFaceKey(a, b, c)))
                    result.Add(new ParadeTriangle(a, b, c));
            }
        }
        return result;
    }

    private static void ValidateParadeOwnerSeams(List<MeshFilter> sources,
        Renderer[] overlays, Transform visualRoot, List<string> failures)
    {
        if (sources == null ||
            sources.Count != F117AircraftAssembler.ParadeFlagSurfaceNames.Length ||
            overlays == null ||
            overlays.Length != F117AircraftAssembler.ParadeFlagSurfaceNames.Length ||
            overlays.Any(overlay => overlay == null ||
                !overlay.name.StartsWith(
                    F117AircraftAssembler.ParadeFlagOverlayPrefix,
                    StringComparison.Ordinal)) ||
            overlays.Select(overlay => overlay.name).Distinct(StringComparer.Ordinal).Count() !=
                F117AircraftAssembler.ParadeFlagSurfaceNames.Length ||
            !new HashSet<string>(overlays.Select(overlay => overlay.name.Substring(
                    F117AircraftAssembler.ParadeFlagOverlayPrefix.Length)),
                    StringComparer.Ordinal)
                .SetEquals(F117AircraftAssembler.ParadeFlagSurfaceNames))
            return;
        var fullSource = sources.ToDictionary(
            source => source.transform.name,
            source => ParadeSourceTopology(AllEligibleParadeTriangles(source, visualRoot)),
            StringComparer.Ordinal);
        var downwardSource = sources.ToDictionary(
            source => source.transform.name,
            source => ParadeSourceTopology(DownwardParadeTriangles(source, visualRoot)),
            StringComparer.Ordinal);
        var output = overlays.ToDictionary(
            overlay => overlay.name.Substring(
                F117AircraftAssembler.ParadeFlagOverlayPrefix.Length),
            overlay => ParadeOverlayTopology(overlay, visualRoot),
            StringComparer.Ordinal);

        const string center = "F117_Exterior_Mesh";
        const string left = "F117_Exterior_LeftWing_Mesh";
        const string right = "F117_Exterior_RightWing_Mesh";
        ValidateParadeInterface("full center/left authored root cycle",
            fullSource[center], fullSource[left], 7, 8, 12.185200f,
            false, failures);
        ValidateParadeInterface("full center/right authored root cycle",
            fullSource[center], fullSource[right], 8, 9, 13.821610f,
            false, failures);
        ValidateParadeInterface("downward center/left source chain",
            downwardSource[center], downwardSource[left], 4, 5, 6.877789548f,
            false, failures);
        ValidateParadeInterface("downward center/right source chain",
            downwardSource[center], downwardSource[right], 4, 5, 6.878904879f,
            false, failures);
        ValidateParadeInterface("output center/left welded chain",
            output[center], output[left], 4, 5, 6.877789548f,
            true, failures);
        ValidateParadeInterface("output center/right welded chain",
            output[center], output[right], 4, 5, 6.878904879f,
            true, failures);

        string[] owners = F117AircraftAssembler.ParadeFlagSurfaceNames;
        for (int first = 0; first < owners.Length; first++)
        for (int second = first + 1; second < owners.Length; second++)
        {
            bool approved = owners[first] == center &&
                (owners[second] == left || owners[second] == right);
            int fullShared = SharedParadeEdges(
                fullSource[owners[first]], fullSource[owners[second]]).Count;
            int outputShared = SharedParadeEdges(
                output[owners[first]], output[owners[second]]).Count;
            if (!approved)
            {
                Require(fullShared == 0,
                    "No unintended authored cross-owner interface exists between " +
                    owners[first] + " and " + owners[second], failures);
                Require(outputShared == 0,
                    "No unintended output cross-owner interface exists between " +
                    owners[first] + " and " + owners[second], failures);
            }
        }
    }

    private static HashSet<string> SharedParadeEdges(ParadeTopology first,
        ParadeTopology second)
    {
        return new HashSet<string>(
            first.BoundaryEdges.Keys.Intersect(
                second.BoundaryEdges.Keys, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static void ValidateParadeInterface(string label, ParadeTopology first,
        ParadeTopology second, int expectedEdges, int expectedVertices,
        float expectedLength, bool requireUv, List<string> failures)
    {
        HashSet<string> shared = SharedParadeEdges(first, second);
        var vertices = new HashSet<string>(StringComparer.Ordinal);
        float length = 0f;
        float maximumUvError = 0f;
        int missingUvs = 0;
        foreach (string key in shared)
        {
            ParadeEdgeAccumulator edge = first.BoundaryEdges[key];
            length += Vector3.Distance(edge.A, edge.B);
            foreach (Vector3 point in new[] { edge.A, edge.B })
            {
                string pointKey = ParadePointKey(point);
                vertices.Add(pointKey);
                if (!requireUv)
                    continue;
                if (!first.Uvs.TryGetValue(pointKey, out Vector2 firstUv) ||
                    !second.Uvs.TryGetValue(pointKey, out Vector2 secondUv))
                    missingUvs++;
                else
                    maximumUvError = Mathf.Max(maximumUvError,
                        Vector2.Distance(firstUv, secondUv));
            }
        }
        Require(shared.Count == expectedEdges && vertices.Count == expectedVertices &&
                Mathf.Abs(length - expectedLength) <= 0.002f,
            label + " has its exact measured edge/vertex/length contract" +
            " (actual " + shared.Count + "/" + vertices.Count + "/" +
            length.ToString("0.000000") + " m)", failures);
        if (requireUv)
            Require(missingUvs == 0 && maximumUvError <= 0.00001f,
                label + " preserves continuous shared root-space UVs" +
                " (missing " + missingUvs + ", max error " +
                maximumUvError.ToString("0.000000") + ")", failures);
    }

    private sealed class ParadePlaneTriangle
    {
        internal readonly string OwnerName;
        internal readonly Vector3 A;
        internal readonly Vector3 B;
        internal readonly Vector3 C;
        internal readonly float MinX;
        internal readonly float MaxX;
        internal readonly float MinZ;
        internal readonly float MaxZ;

        internal ParadePlaneTriangle(string ownerName, Vector3 a, Vector3 b, Vector3 c)
        {
            OwnerName = ownerName;
            A = a;
            B = b;
            C = c;
            MinX = Mathf.Min(A.x, Mathf.Min(B.x, C.x));
            MaxX = Mathf.Max(A.x, Mathf.Max(B.x, C.x));
            MinZ = Mathf.Min(A.z, Mathf.Min(B.z, C.z));
            MaxZ = Mathf.Max(A.z, Mathf.Max(B.z, C.z));
        }

        internal double HeightAt(Vector2 point)
        {
            double abx = B.x - A.x;
            double aby = B.y - A.y;
            double abz = B.z - A.z;
            double acx = C.x - A.x;
            double acy = C.y - A.y;
            double acz = C.z - A.z;
            double normalX = aby * acz - abz * acy;
            double normalY = abz * acx - abx * acz;
            double normalZ = abx * acy - aby * acx;
            return A.y - (normalX * (point.x - A.x) +
                normalZ * (point.y - A.z)) / normalY;
        }

        internal bool Contains(Vector2 point)
        {
            Vector2 a = new Vector2(A.x, A.z);
            Vector2 b = new Vector2(B.x, B.z);
            Vector2 c = new Vector2(C.x, C.z);
            float first = ParadeCross2(b - a, point - a);
            float second = ParadeCross2(c - b, point - b);
            float third = ParadeCross2(a - c, point - c);
            const float epsilon = 0.00000001f;
            return (first >= -epsilon && second >= -epsilon && third >= -epsilon) ||
                (first <= epsilon && second <= epsilon && third <= epsilon);
        }
    }

    private static void ValidateParadeBottomVisibility(Transform prefabRoot,
        Renderer[] overlays, Transform visualRoot, List<string> failures)
    {
        MeshFilter[] allFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
        var filters = new List<MeshFilter>();
        foreach (string name in ParadeFlagOccluderNames)
        {
            MeshFilter[] matches = allFilters.Where(filter =>
                filter != null && filter.sharedMesh != null &&
                filter.transform.name == name).ToArray();
            Require(matches.Length == 1,
                "Bottom-envelope occluder " + name + " resolves exactly once", failures);
            if (matches.Length == 1)
                filters.Add(matches[0]);
        }
        if (filters.Count != ParadeFlagOccluderNames.Length)
            return;

        var occluders = new List<ParadePlaneTriangle>();
        var uniqueFaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = filter.GetComponent<Renderer>().sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (subMesh >= materials.Length ||
                    !F117AircraftAssembler.IsParadeFlagMaterial(
                        materials[subMesh], filter.transform.name))
                    continue;
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    Vector3 a = ParadeQuantizedRootPoint(visualRoot, filter,
                        vertices[triangles[triangle]]);
                    Vector3 b = ParadeQuantizedRootPoint(visualRoot, filter,
                        vertices[triangles[triangle + 1]]);
                    Vector3 c = ParadeQuantizedRootPoint(visualRoot, filter,
                        vertices[triangles[triangle + 2]]);
                    string face = ParadeFaceKey(a, b, c);
                    if (!uniqueFaces.Add(face))
                        continue;
                    if (!F117AircraftAssembler.ParadePolygonAreaIsResolved(new[]
                        {
                            new Vector2(a.x, a.z),
                            new Vector2(b.x, b.z),
                            new Vector2(c.x, c.z)
                        }))
                        continue;
                    occluders.Add(new ParadePlaneTriangle(filter.transform.name, a, b, c));
                }
            }
        }
        Require(uniqueFaces.Count == F117AircraftAssembler.ParadeFlagUniqueOccluderFaceCount &&
                occluders.Count == F117AircraftAssembler.ParadeFlagProjectedOccluderFaceCount,
            "Bottom-envelope occluders match the pinned " +
            F117AircraftAssembler.ParadeFlagUniqueOccluderFaceCount + " unique / " +
            F117AircraftAssembler.ParadeFlagProjectedOccluderFaceCount +
            " projectable exterior-face manifest (actual " + uniqueFaces.Count + "/" +
            occluders.Count + ")", failures);

        const float cellSize = 0.5f;
        var cells = new Dictionary<long, List<int>>();
        for (int index = 0; index < occluders.Count; index++)
        {
            ParadePlaneTriangle triangle = occluders[index];
            for (int x = Mathf.FloorToInt(triangle.MinX / cellSize);
                 x <= Mathf.FloorToInt(triangle.MaxX / cellSize); x++)
            for (int z = Mathf.FloorToInt(triangle.MinZ / cellSize);
                 z <= Mathf.FloorToInt(triangle.MaxZ / cellSize); z++)
            {
                long key = ParadeCellKey(x, z);
                if (!cells.TryGetValue(key, out List<int> values))
                {
                    values = new List<int>();
                    cells.Add(key, values);
                }
                values.Add(index);
            }
        }

        int samples = 0;
        int hidden = 0;
        var hiddenDetails = new List<string>();
        var outputByOwner = new Dictionary<string, List<ParadeTriangle>>(
            StringComparer.Ordinal);
        foreach (Renderer overlay in overlays)
        {
            MeshFilter filter = overlay == null ? null : overlay.GetComponent<MeshFilter>();
            Mesh mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null)
                continue;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var rootPoints = vertices.Select(vertex =>
            {
                Vector3 root = visualRoot.InverseTransformPoint(
                    overlay.transform.TransformPoint(vertex)) +
                    Vector3.up * F117AircraftAssembler.ParadeFlagSurfaceOffset;
                return new Vector3(ParadeQuantized(root.x),
                    ParadeQuantized(root.y), ParadeQuantized(root.z));
            }).ToArray();
            string ownerName = overlay.name.StartsWith(
                    F117AircraftAssembler.ParadeFlagOverlayPrefix,
                    StringComparison.Ordinal)
                ? overlay.name.Substring(
                    F117AircraftAssembler.ParadeFlagOverlayPrefix.Length)
                : overlay.name;
            if (!outputByOwner.TryGetValue(ownerName,
                    out List<ParadeTriangle> outputTriangles))
            {
                outputTriangles = new List<ParadeTriangle>();
                outputByOwner.Add(ownerName, outputTriangles);
            }
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                Vector3 a = rootPoints[triangles[triangle]];
                Vector3 b = rootPoints[triangles[triangle + 1]];
                Vector3 c = rootPoints[triangles[triangle + 2]];
                outputTriangles.Add(new ParadeTriangle(a, b, c));
                Vector3[] interiorSamples =
                {
                    (a + b + c) / 3f,
                    a * 0.6f + b * 0.2f + c * 0.2f,
                    a * 0.2f + b * 0.6f + c * 0.2f,
                    a * 0.2f + b * 0.2f + c * 0.6f
                };
                foreach (Vector3 point in interiorSamples)
                {
                    samples++;
                    int cellX = Mathf.FloorToInt(point.x / cellSize);
                    int cellZ = Mathf.FloorToInt(point.z / cellSize);
                    if (!cells.TryGetValue(ParadeCellKey(cellX, cellZ),
                            out List<int> candidates))
                        continue;
                    Vector2 projected = new Vector2(point.x, point.z);
                    foreach (int candidate in candidates)
                    {
                        ParadePlaneTriangle occluder = occluders[candidate];
                        // The generator deliberately does not self-clip an
                        // approved exterior owner. Validate against the same
                        // immutable ownership contract instead of treating the
                        // owner's adjacent/overlapping source triangles as a
                        // foreign obstruction.
                        if (string.Equals(ownerName, occluder.OwnerName,
                                StringComparison.Ordinal))
                            continue;
                        if (point.x < occluder.MinX || point.x > occluder.MaxX ||
                            point.z < occluder.MinZ || point.z > occluder.MaxZ ||
                            !occluder.Contains(projected))
                            continue;
                        if (occluder.HeightAt(projected) <
                            point.y - F117AircraftAssembler.ParadeFlagVisibilityClearance -
                            0.0000001f)
                        {
                            hidden++;
                            if (hiddenDetails.Count < 12)
                                hiddenDetails.Add(overlay.name + " output=" +
                                    ParadeFaceKey(a, b, c) + " sample=" +
                                    ParadePointKey(point) + " occluder=" +
                                    ParadeFaceKey(occluder.A, occluder.B, occluder.C) +
                                    " sampleY=" + point.y.ToString("R") +
                                    " occluderY=" +
                                    occluder.HeightAt(projected).ToString("R"));
                            break;
                        }
                    }
                }
            }
        }
        Require(samples > 0 && hidden == 0,
            "Every farewell-flag output facet lies on the true outward-visible bottom " +
            "envelope across all " + ParadeFlagOccluderNames.Length +
            " exterior-skin owners" +
            " (samples " + samples + ", hidden " + hidden +
            (hiddenDetails.Count == 0 ? ")" : ": " +
                string.Join("; ", hiddenDetails) + ")"), failures);

        Dictionary<string, ParadeTriangleSpatialIndex> outputIndices = outputByOwner
            .ToDictionary(pair => pair.Key,
                pair => new ParadeTriangleSpatialIndex(pair.Value, cellSize),
                StringComparer.Ordinal);
        int visibleSourceCentroids = 0;
        int uncoveredSourceCentroids = 0;
        var uncoveredDetails = new List<string>();
        foreach (string ownerName in F117AircraftAssembler.ParadeFlagSurfaceNames)
        {
            MeshFilter source = allFilters.SingleOrDefault(filter =>
                filter != null && filter.sharedMesh != null &&
                filter.transform.name == ownerName);
            if (source == null ||
                !outputIndices.TryGetValue(ownerName,
                    out ParadeTriangleSpatialIndex ownerOutput))
                continue;
            foreach (ParadeTriangle candidate in DownwardParadeTriangles(
                source, visualRoot))
            {
                Vector3 centroid = (candidate.A + candidate.B + candidate.C) / 3f;
                if (!ParadeBottomVisible(ownerName, centroid, occluders, cells, cellSize))
                    continue;
                visibleSourceCentroids++;
                if (!ParadePointSupported(centroid, ownerOutput.Query(centroid)))
                {
                    uncoveredSourceCentroids++;
                    if (uncoveredDetails.Count < 12)
                        uncoveredDetails.Add(ownerName + " face=" +
                            ParadeFaceKey(candidate.A, candidate.B, candidate.C) +
                            " centroid=" + ParadePointKey(centroid));
                }
            }
        }
        Require(visibleSourceCentroids > 0 && uncoveredSourceCentroids == 0,
            "Every independently bottom-visible source-facet centroid is covered by " +
            "its exact owner overlay (visible " + visibleSourceCentroids +
            ", uncovered " + uncoveredSourceCentroids +
            (uncoveredDetails.Count == 0 ? ")" : ": " +
                string.Join("; ", uncoveredDetails) + ")"), failures);
    }

    private static bool ParadeBottomVisible(string ownerName, Vector3 point,
        IReadOnlyList<ParadePlaneTriangle> occluders,
        Dictionary<long, List<int>> cells, float cellSize)
    {
        int cellX = Mathf.FloorToInt(point.x / cellSize);
        int cellZ = Mathf.FloorToInt(point.z / cellSize);
        if (!cells.TryGetValue(ParadeCellKey(cellX, cellZ),
                out List<int> candidates))
            return true;
        Vector2 projected = new Vector2(point.x, point.z);
        foreach (int candidate in candidates)
        {
            ParadePlaneTriangle occluder = occluders[candidate];
            if (string.Equals(ownerName, occluder.OwnerName,
                    StringComparison.Ordinal))
                continue;
            if (point.x < occluder.MinX || point.x > occluder.MaxX ||
                point.z < occluder.MinZ || point.z > occluder.MaxZ ||
                !occluder.Contains(projected))
                continue;
            if (occluder.HeightAt(projected) <
                point.y - F117AircraftAssembler.ParadeFlagVisibilityClearance -
                0.0000001f)
                return false;
        }
        return true;
    }

    private static long ParadeCellKey(int x, int z)
    {
        return ((long)x << 32) | (uint)z;
    }

    private static float ParadeCross2(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static void ValidateParadeDecalClearance(MeshFilter[] fixedSources, Transform visualRoot,
        List<string> failures)
    {
        int downwardDecalTriangles = 0;
        float minimumClearance = float.PositiveInfinity;
        foreach (MeshFilter source in fixedSources)
        {
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Material[] materials = source.GetComponent<Renderer>().sharedMaterials;
            var skin = new List<ParadeTriangle>();
            var decalPoints = new List<Vector3>();
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                bool eligibleSkin = subMesh < materials.Length &&
                    F117AircraftAssembler.IsParadeFlagMaterial(
                        materials[subMesh], source.transform.name);
                bool decal = subMesh < materials.Length && materials[subMesh] != null &&
                    materials[subMesh].name.IndexOf("external_decals",
                        StringComparison.OrdinalIgnoreCase) >= 0;
                if (!eligibleSkin && !decal)
                    continue;
                int[] triangles = mesh.GetTriangles(subMesh);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    int a = triangles[index];
                    int b = triangles[index + 1];
                    int c = triangles[index + 2];
                    Vector3 localFace = Vector3.Cross(
                        vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                    Vector3 rootFace = visualRoot.InverseTransformDirection(
                        source.transform.TransformDirection(localFace)).normalized;
                    if (Vector3.Dot(rootFace, Vector3.down) <
                        F117AircraftAssembler.ParadeFlagMinimumDownwardDot)
                        continue;
                    Vector3 first = visualRoot.InverseTransformPoint(
                        source.transform.TransformPoint(vertices[a]));
                    Vector3 second = visualRoot.InverseTransformPoint(
                        source.transform.TransformPoint(vertices[b]));
                    Vector3 third = visualRoot.InverseTransformPoint(
                        source.transform.TransformPoint(vertices[c]));
                    if (eligibleSkin)
                        skin.Add(new ParadeTriangle(first, second, third));
                    else
                    {
                        downwardDecalTriangles++;
                        decalPoints.Add(first);
                        decalPoints.Add(second);
                        decalPoints.Add(third);
                    }
                }
            }
            foreach (Vector3 point in decalPoints)
            foreach (ParadeTriangle triangle in skin)
                minimumClearance = Mathf.Min(minimumClearance,
                    PointTriangleDistance(point, triangle.A, triangle.B, triangle.C));
        }

        Require(downwardDecalTriangles == 26,
            "All 26 authored underside emblem triangles remain outside the flag-overlay submeshes", failures);
        Require(!float.IsInfinity(minimumClearance) &&
                minimumClearance > F117AircraftAssembler.ParadeFlagSurfaceOffset * 1.5f,
            "Farewell-flag offset remains beneath every authored underside emblem" +
            (float.IsInfinity(minimumClearance) ? string.Empty :
                " (minimum clearance " + (minimumClearance * 1000f).ToString("0.000") + " mm)"),
            failures);
    }

    private static float PointTriangleDistance(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
            return ap.magnitude;

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
            return bp.magnitude;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return (point - (a + ab * (d1 / (d1 - d3)))).magnitude;

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
            return cp.magnitude;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return (point - (a + ac * (d2 / (d2 - d6)))).magnitude;

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            Vector3 bc = c - b;
            return (point - (b + bc * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).magnitude;
        }

        Vector3 normal = Vector3.Cross(ab, ac).normalized;
        return Mathf.Abs(Vector3.Dot(point - a, normal));
    }

    private static bool OverlayFacesDown(Renderer renderer, Transform aircraftRoot)
    {
        MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
        Mesh mesh = filter == null ? null : filter.sharedMesh;
        if (mesh == null)
            return false;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        if (triangles.Length == 0)
            return false;
        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Vector3 normal = Vector3.Cross(
                vertices[triangles[index + 1]] - vertices[triangles[index]],
                vertices[triangles[index + 2]] - vertices[triangles[index]]);
            Vector3 rootNormal = aircraftRoot.InverseTransformDirection(
                renderer.transform.TransformDirection(normal));
            double magnitude = Math.Sqrt((double)rootNormal.x * rootNormal.x +
                (double)rootNormal.y * rootNormal.y +
                (double)rootNormal.z * rootNormal.z);
            if (magnitude == 0d || -rootNormal.y / magnitude <
                    ParadeFlagOutputMinimumDownwardDot)
                return false;
        }
        return true;
    }

    private static Texture Texture(Material material, params string[] properties)
    {
        if (material == null)
            return null;
        foreach (string property in properties)
        {
            SerializedProperty entries = new SerializedObject(material)
                .FindProperty("m_SavedProperties.m_TexEnvs");
            if (entries != null && entries.isArray)
            {
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    SerializedProperty key = entry.FindPropertyRelative("first");
                    SerializedProperty value = entry.FindPropertyRelative("second.m_Texture");
                    if (key != null && value != null && key.stringValue == property &&
                        value.objectReferenceValue is Texture savedTexture)
                        return savedTexture;
                }
            }
            if (MaterialPropertyIsTexture(material, property))
            {
                Texture texture = material.GetTexture(property);
                if (texture != null)
                    return texture;
            }
        }
        return null;
    }

    private static bool MaterialPropertyIsTexture(Material material, string property)
    {
        if (material == null || material.shader == null)
            return false;
        int count = ShaderUtil.GetPropertyCount(material.shader);
        for (int index = 0; index < count; index++)
            if (ShaderUtil.GetPropertyName(material.shader, index) == property)
                return ShaderUtil.GetPropertyType(material.shader, index) ==
                    ShaderUtil.ShaderPropertyType.TexEnv;
        return false;
    }

    private static bool HasSavedMaterialProperty(Material material, string collection, string property)
    {
        if (material == null)
            return false;
        if (material.HasProperty(property))
            return true;
        SerializedProperty entries = new SerializedObject(material)
            .FindProperty("m_SavedProperties." + collection);
        if (entries == null || !entries.isArray)
            return false;
        for (int index = 0; index < entries.arraySize; index++)
        {
            SerializedProperty key = entries.GetArrayElementAtIndex(index).FindPropertyRelative("first");
            if (key != null && key.stringValue == property)
                return true;
        }
        return false;
    }

    private static void RequireTexturePath(Material material, string property, string expectedPath,
        List<string> failures)
    {
        Texture texture = Texture(material, property);
        string actualPath = texture == null ? string.Empty : AssetDatabase.GetAssetPath(texture);
        Require(string.Equals(actualPath, expectedPath, StringComparison.Ordinal),
            (material == null ? "<missing material>" : material.name) + "." + property +
            " resolves exactly to " + expectedPath +
            (string.IsNullOrEmpty(actualPath) ? " (actual: null)" : " (actual: " + actualPath + ")"),
            failures);
    }

    private static Component[] OfType(Component[] components, string typeName) =>
        components.Where(component => component.GetType().Name == typeName).ToArray();

    private static Component Single(Component[] components, string typeName, List<string> failures)
    {
        Component[] matches = OfType(components, typeName);
        Require(matches.Length == 1, "Exactly one " + typeName, failures);
        return matches.Length == 1 ? matches[0] : null;
    }

    private static SerializedProperty Property(SerializedObject data, string field, string owner, List<string> failures)
    {
        SerializedProperty property = data.FindProperty(field);
        Require(property != null, owner + " serializes " + field, failures);
        return property;
    }

    private static void RequireRef(SerializedObject data, string field, string owner, List<string> failures)
    {
        SerializedProperty property = Property(data, field, owner, failures);
        Require(property != null && property.objectReferenceValue != null, owner + "." + field + " is linked", failures);
    }

    private static void RequireRelativeRef(SerializedProperty owner, string field, string label, List<string> failures)
    {
        SerializedProperty property = owner.FindPropertyRelative(field);
        Require(property != null && property.objectReferenceValue != null, label + "." + field + " is linked", failures);
    }

    private static void RequireArray(SerializedObject data, string field, int size, string owner, List<string> failures)
    {
        SerializedProperty property = Property(data, field, owner, failures);
        Require(property != null && property.isArray && property.arraySize == size, owner + "." + field + " has size " + size, failures);
        if (property != null && property.isArray)
            for (int index = 0; index < property.arraySize; index++)
                Require(property.GetArrayElementAtIndex(index).propertyType != SerializedPropertyType.ObjectReference ||
                        property.GetArrayElementAtIndex(index).objectReferenceValue != null,
                    owner + "." + field + "[" + index + "] is linked", failures);
    }

    private static void RequireRelativeArray(SerializedProperty owner, string field, int size, string label, List<string> failures)
    {
        SerializedProperty property = owner.FindPropertyRelative(field);
        Require(property != null && property.isArray && property.arraySize == size, label + "." + field + " has size " + size, failures);
    }

    private static float Float(SerializedObject data, string field, string owner, List<string> failures)
    {
        SerializedProperty property = Property(data, field, owner, failures);
        return property == null ? float.NaN : property.floatValue;
    }

    private static float RelativeFloat(SerializedProperty data, string field, string owner, List<string> failures)
    {
        SerializedProperty property = data.FindPropertyRelative(field);
        Require(property != null, owner + ".armorProperties serializes " + field, failures);
        return property == null ? float.NaN : property.floatValue;
    }

    private static bool Bool(SerializedObject data, string field, string owner, List<string> failures)
    {
        SerializedProperty property = Property(data, field, owner, failures);
        return property != null && property.boolValue;
    }

    private static UnityEngine.Object Ref(SerializedObject data, string field)
    {
        SerializedProperty property = data.FindProperty(field);
        return property == null ? null : property.objectReferenceValue;
    }

    private static int LiteralCount(string value, string needle)
    {
        int count = 0;
        for (int index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length)
            count++;
        return count;
    }

    private static bool Near(float actual, float expected, float tolerance) => Math.Abs(actual - expected) <= tolerance;

    private static int ValidateCockpitDisplayMesh(Mesh mesh, List<string> failures)
    {
        if (mesh == null)
        {
            Require(false, "F-117 tactical-screen mesh is linked", failures);
            return 0;
        }

        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Require(triangles.Length >= 9, "F-117 tactical-screen mesh contains rendered triangles", failures);
        Require(uvs.Length == vertices.Length,
            "Every F-117 tactical-screen vertex has a native texture coordinate", failures);
        if (triangles.Length < 3 || vertices.Length == 0 || uvs.Length != vertices.Length)
            return 0;

        int triangleCount = triangles.Length / 3;
        var pointTriangles = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        string PointKey(Vector3 point) =>
            Mathf.RoundToInt(point.x * 10000f) + ":" +
            Mathf.RoundToInt(point.y * 10000f) + ":" +
            Mathf.RoundToInt(point.z * 10000f);
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                string key = PointKey(vertices[triangles[triangle * 3 + corner]]);
                if (!pointTriangles.TryGetValue(key, out List<int> connected))
                {
                    connected = new List<int>();
                    pointTriangles.Add(key, connected);
                }
                connected.Add(triangle);
            }
        }

        var components = new List<HashSet<int>>();
        var visited = new bool[triangleCount];
        for (int seed = 0; seed < triangleCount; seed++)
        {
            if (visited[seed])
                continue;
            var componentVertices = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            visited[seed] = true;
            while (queue.Count > 0)
            {
                int triangle = queue.Dequeue();
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = triangles[triangle * 3 + corner];
                    componentVertices.Add(vertex);
                    foreach (int neighbor in pointTriangles[PointKey(vertices[vertex])])
                    {
                        if (visited[neighbor])
                            continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }
            components.Add(componentVertices);
        }

        Require(components.Count == 3,
            "F-117 tactical-screen mesh contains exactly three physical display islands (found " + components.Count + ")",
            failures);
        bool cameraFound = false;
        bool basicFlightFound = false;
        bool engineFound = false;
        for (int index = 0; index < components.Count; index++)
        {
            float minimumU = float.PositiveInfinity;
            float minimumV = float.PositiveInfinity;
            float maximumU = float.NegativeInfinity;
            float maximumV = float.NegativeInfinity;
            foreach (int vertex in components[index])
            {
                Vector2 uv = uvs[vertex];
                minimumU = Mathf.Min(minimumU, uv.x);
                minimumV = Mathf.Min(minimumV, uv.y);
                maximumU = Mathf.Max(maximumU, uv.x);
                maximumV = Mathf.Max(maximumV, uv.y);
            }

            Require(minimumU >= -0.001f && minimumV >= -0.001f &&
                    maximumU <= 1.001f && maximumV <= 1.001f,
                "F-117 cockpit display " + (index + 1) + " remains inside the native screen atlas",
                failures);

            bool camera = minimumU < 0.2f && minimumV < 0.01f && maximumV > 0.99f;
            bool basicFlight = minimumU > 0.75f && maximumV < 0.36f;
            bool engine = minimumU > 0.75f && minimumV > 0.35f && maximumV < 0.72f;

            Require(camera || basicFlight || engine,
                "F-117 cockpit display " + (index + 1) + " maps one approved stock Cricket atlas region",
                failures);

            float meanX = components[index].Average(vertex => vertices[vertex].x);
            float meanY = components[index].Average(vertex => vertices[vertex].y);
            float meanU = components[index].Average(vertex => uvs[vertex].x);
            float meanV = components[index].Average(vertex => uvs[vertex].y);
            float varianceX = 0f, varianceY = 0f, varianceU = 0f, varianceV = 0f;
            float covarianceXU = 0f, covarianceXV = 0f, covarianceYU = 0f, covarianceYV = 0f;
            foreach (int vertex in components[index])
            {
                float x = vertices[vertex].x - meanX;
                float y = vertices[vertex].y - meanY;
                float u = uvs[vertex].x - meanU;
                float v = uvs[vertex].y - meanV;
                varianceX += x * x;
                varianceY += y * y;
                varianceU += u * u;
                varianceV += v * v;
                covarianceXU += x * u;
                covarianceXV += x * v;
                covarianceYU += y * u;
                covarianceYV += y * v;
            }
            float Correlation(float covariance, float firstVariance, float secondVariance) =>
                firstVariance > 0.0000001f && secondVariance > 0.0000001f
                    ? covariance / Mathf.Sqrt(firstVariance * secondVariance)
                    : 0f;
            float correlationXU = Correlation(covarianceXU, varianceX, varianceU);
            float correlationXV = Correlation(covarianceXV, varianceX, varianceV);
            float correlationYU = Correlation(covarianceYU, varianceY, varianceU);
            float correlationYV = Correlation(covarianceYV, varianceY, varianceV);
            if (camera)
            {
                cameraFound = true;
                Require(Near(minimumU, 0.00110f, 0.005f) && Near(maximumU, 0.79063f, 0.005f) &&
                        Near(minimumV, 0.00011f, 0.005f) && Near(maximumV, 0.99989f, 0.005f),
                    "Center display preserves the complete native Cricket camera/radar atlas region", failures);
                // FBX conversion mirrors the model's horizontal X axis. The known-good
                // center screen therefore imports as X/U=-1 while physical up remains Y/V=+1.
                Require(correlationXU < -0.95f && correlationYV > 0.95f,
                    "Center camera/radar display remains upright and unrotated " +
                    "(x/u=" + correlationXU.ToString("0.000") + ", y/v=" + correlationYV.ToString("0.000") +
                    ", x/v=" + correlationXV.ToString("0.000") + ", y/u=" + correlationYU.ToString("0.000") + ")", failures);
            }
            else if (basicFlight)
            {
                basicFlightFound = true;
                Require(Near(minimumU, 0.79230f, 0.005f) && Near(maximumU, 0.99359f, 0.005f) &&
                        Near(minimumV, 0.02251f, 0.005f) && Near(maximumV, 0.34329f, 0.005f),
                    "Left display preserves the complete native Cricket basic-flight instrument region", failures);
                Require(correlationYU < -0.95f && correlationXV < -0.95f,
                    "Left instrument display counter-rotates the clockwise atlas content by 90 degrees " +
                    "(y/u=" + correlationYU.ToString("0.000") + ", x/v=" + correlationXV.ToString("0.000") +
                    ", x/u=" + correlationXU.ToString("0.000") + ", y/v=" + correlationYV.ToString("0.000") + ")", failures);
            }
            else if (engine)
            {
                engineFound = true;
                Require(Near(minimumU, 0.79073f, 0.005f) && Near(maximumU, 0.99510f, 0.005f) &&
                        Near(minimumV, 0.38727f, 0.005f) && Near(maximumV, 0.69994f, 0.005f),
                    "Right display preserves the complete native Cricket engine-instrument region", failures);
                Require(correlationYU < -0.95f && correlationXV < -0.95f,
                    "Right instrument display counter-rotates the clockwise atlas content by 90 degrees " +
                    "(y/u=" + correlationYU.ToString("0.000") + ", x/v=" + correlationXV.ToString("0.000") +
                    ", x/u=" + correlationXU.ToString("0.000") + ", y/v=" + correlationYV.ToString("0.000") + ")", failures);
            }
        }
        Require(cameraFound && basicFlightFound && engineFound,
            "Cockpit has one center camera/radar display plus distinct left flight and right engine displays",
            failures);
        return components.Count;
    }

    private static void Require(bool condition, string message, List<string> failures)
    {
        if (!condition)
            failures.Add(message);
    }

    private static void Finish(List<string> failures, List<string> notes)
    {
        var report = new List<string>
        {
            "F-117A NIGHTHAWK RUNTIME CONTRACT VALIDATION",
            "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "Result: " + (failures.Count == 0 ? "PASS" : "FAIL"),
            ""
        };
        report.AddRange(notes);
        if (failures.Count > 0)
        {
            report.Add("");
            report.Add("Failures:");
            report.AddRange(failures.Select(failure => "- " + failure));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllLines(ReportPath, report);
        if (failures.Count > 0)
            throw new InvalidOperationException("F-117 runtime contract validation failed:\n" + string.Join("\n", failures));
        Debug.Log("F-117 runtime contract validation PASS. Report: " + ReportPath);
    }
}
