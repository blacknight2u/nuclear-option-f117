using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class F117ContractValidator
{
    private const string PrefabPath = "Assets/F117/Generated/F117A_Nighthawk.prefab";
    private const string ModelPath = "Assets/F117/Models/F117_Production.fbx";
    private const string DefinitionPath = "Assets/F117/Generated/F117A_Nighthawk_Definition.asset";
    private const string LiveryPath = "Assets/F117/Generated/F117A_Nighthawk_Livery.asset";
    private const string ParadeLiveryPath = "Assets/F117/Generated/F117A_ParadeFlag_Livery.asset";
    private static readonly string[] ParadeLiveryPaths =
    {
        ParadeLiveryPath,
        "Assets/F117/Generated/F117A_ParadeFlag_SilverBlue_Livery.asset",
        "Assets/F117/Generated/F117A_ParadeFlag_CoolTitanium_Livery.asset",
        "Assets/F117/Generated/F117A_ParadeFlag_SmokedChrome_Livery.asset",
        "Assets/F117/Generated/F117A_ParadeFlag_WarmTitanium_Livery.asset"
    };
    private static readonly string[] ParadeLiveryDisplayNames =
    {
        "Farewell Flag - Pure Chrome",
        "Farewell Flag - Silver Blue",
        "Farewell Flag - Cool Titanium",
        "Farewell Flag - Smoked Chrome",
        "Farewell Flag - Warm Titanium"
    };
    private const string ParadeFlagTexturePath = "Assets/F117/Textures/F117_ParadeFlag.png";
    private const string ParadeFlagWrapTexturePath = "Assets/F117/Textures/F117_ParadeFlag_Wrap.png";
    private const string MirrorFinishTexturePath = "Assets/F117/Textures/F117_Mirror_MS.png";
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
            { "Aircraft", 1 }, { "AutopilotPlane", 1 }, { "AeroPart", 17 }, { "BayDoor", 2 },
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

        Require(prefab.name == "F117A_Nighthawk", "Aircraft root has unique F-117 name", failures);
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
            .FirstOrDefault(transform => transform.name == "F117_CentralBody");
        Component pilot = components.FirstOrDefault(component => component.GetType().Name == "Pilot");
        string[] donorArtwork = prefab.GetComponentsInChildren<Component>(true)
            .Where(component => component is Renderer || component is MeshFilter || component is LODGroup || component is ParticleSystemRenderer)
            .Where(component => productionVisual == null ||
                                (component.transform != productionVisual && !component.transform.IsChildOf(productionVisual)))
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
        for (int panel = 1; panel <= 7; panel++)
            ValidateMatteFinishTexture(panel, MatteFinishTextureGuids[panel - 1], failures);
        ValidateMirrorFinishTexture(failures);
        Renderer[] paradeOverlays = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                StringComparison.Ordinal))
            .ToArray();
        Require(paradeOverlays.Length >= 5,
            "Farewell-flag livery has underside overlays for the shell and moving surfaces", failures);
        Require(paradeOverlays.All(renderer => !renderer.enabled),
            "Farewell-flag overlays are disabled for the default black livery", failures);
        Require(paradeOverlays.All(renderer => renderer.sharedMaterial != null &&
                AssetDatabase.GetAssetPath(Texture(renderer.sharedMaterial, "_BaseMap")) == ParadeFlagWrapTexturePath),
            "Every farewell-flag overlay uses the deterministic opaque full-belly wrap", failures);
        Renderer[] damageSkinOverlays = paradeOverlays
            .Where(renderer => renderer.name.IndexOf("_Skin_", StringComparison.Ordinal) >= 0)
            .ToArray();
        Require(damageSkinOverlays.Length >= 9 && damageSkinOverlays.All(renderer =>
            renderer.transform.parent != null && renderer.transform.parent.GetComponent("AeroPart") != null),
            "Every fixed-skin flag overlay is parented directly to its detachable AeroPart", failures);
        string[] invalidParadeOverlays = paradeOverlays
            // The overlay generator classifies faces in the production model root's
            // coordinate system. Validate in that identical space: the donor aircraft
            // root can retain an import basis that is irrelevant to the F-117 mesh.
            // Gear-door overlays are authored in the closed pose and correctly rotate
            // away from downward while the doors are open; they are validated below
            // after temporarily restoring all five close hinges to identity.
            .Where(renderer => renderer.name.IndexOf("GearDoor", StringComparison.Ordinal) < 0)
            .Where(renderer => productionVisual == null || !OverlayFacesDown(renderer, productionVisual))
            .Select(renderer => renderer.name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Require(invalidParadeOverlays.Length == 0,
            "Farewell-flag overlays contain only lower-facing aircraft surfaces" +
            (invalidParadeOverlays.Length == 0 ? string.Empty :
                " (invalid: " + string.Join(", ", invalidParadeOverlays) + ")"), failures);
        ValidateGearDoorOverlays(prefab.transform, productionVisual, failures);
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
        Require(authoredMeshColliders.Length == 12 &&
                controlMeshColliders.Length == 6 && wingMeshColliders.Length == 6 &&
                authoredMeshColliders.All(collider => collider.convex &&
                    collider.sharedMesh != null && collider.sharedMesh.vertexCount <= 255),
            "Six controls and six wing sections use low-poly convex, directly damage-routable MeshColliders", failures);

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
                cockpitFrame.GetColor("_BaseColor").b <= 0.05f,
            "Canopy/cockpit frame uses the authored black material instead of the white fallback", failures);
        RequireTexturePath(cockpitFrame, "_MetallicGlossMap",
            "Assets/F117/Textures/metal_paint02_mask.png", failures);
        Require(cockpitFrame != null &&
                cockpitFrame.shaderKeywords.Contains("_METALLICSPECGLOSSMAP"),
            "Canopy frame keeps its authored URP packed-mask keyword for both finish profiles", failures);
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
            .Where(material => material != null && F117AircraftAssembler.UsesAircraftSkin(material.name))
            .Distinct()
            .ToArray();
        Require(damageSkinMaterials.Length >= 7,
            "All seven opaque exterior material families use the native AircraftSkin damage contract", failures);
        foreach (Material material in damageSkinMaterials)
            Require(material.shader != null &&
                    HasSavedMaterialProperty(material, "m_Floats", "_HitPoints") &&
                    Texture(material, "_Basecolor") != null && Texture(material, "_BasecolorDmg") != null,
                material.name + " is loadable and has clean/damaged skin textures driven by _HitPoints", failures);
        ValidateProfileSlotClassification(prefab, failures);

        Require(aeroParts.Length == 17, "Exactly 17 aerodynamic/mass parts", failures);
        Component centralPart = productionVisual == null
            ? null
            : aeroParts.FirstOrDefault(part => part.transform == productionVisual);
        Require(centralPart != null, "F117_CentralBody owns the root AeroPart", failures);
        float mass = 0f;
        float area = 0f;
        float horizontalLiftArea = 0f;
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
                horizontalLiftArea += partWingArea;
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
                Renderer[] ownedRenderers = damageRenderers == null || !damageRenderers.isArray
                    ? Array.Empty<Renderer>()
                    : Enumerable.Range(0, damageRenderers.arraySize)
                        .Select(index => damageRenderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer)
                        .Where(renderer => renderer != null)
                        .ToArray();
                Bounds geometry = F117AircraftAssembler.CalculateRendererGeometryBounds(
                    prefab.transform, ownedRenderers);
                float sideSign = part.name.IndexOf("_Left_", StringComparison.Ordinal) >= 0 ? -1f : 1f;
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
                float[] rootMetrics = geometryVertices.Select(vertex =>
                    sideSign * vertex.x - F117AircraftAssembler.WingRootSweep * vertex.z).ToArray();
                float[] outerMetrics = geometryVertices.Select(vertex =>
                    sideSign * vertex.x - F117AircraftAssembler.WingOuterSweep * vertex.z).ToArray();
                Vector3 partCenter = prefab.transform.InverseTransformPoint(part.transform.position);
                Vector3 colliderCenter = prefab.transform.InverseTransformPoint(
                    part.transform.TransformPoint(rootMesh.sharedMesh.bounds.center));
                Require(ownedRenderers.Length > 0 && geometry.center.x * sideSign > 0f &&
                        partCenter.x * sideSign > 0f && colliderCenter.x * sideSign > 0f,
                    part.name + " physical body, collider and visible damage skin are on the same authored side", failures);
                bool correctBoundary = geometryVertices.Length > 0 &&
                    (part.name.EndsWith("_Root", StringComparison.Ordinal)
                        ? Near(rootMetrics.Max(), F117AircraftAssembler.WingRootMetricCut, 0.025f)
                        : part.name.EndsWith("_Inner", StringComparison.Ordinal)
                            ? Near(rootMetrics.Min(), F117AircraftAssembler.WingRootMetricCut, 0.025f) &&
                              Near(outerMetrics.Max(), F117AircraftAssembler.WingOuterMetricCut, 0.025f)
                            : Near(outerMetrics.Min(), F117AircraftAssembler.WingOuterMetricCut, 0.025f));
                Require(correctBoundary,
                    part.name + " visible skin is geometrically clipped to its measured swept seam", failures);
                float expectedFraction = part.name.EndsWith("_Root", StringComparison.Ordinal)
                    ? F117AircraftAssembler.WingRootAreaFraction
                    : part.name.EndsWith("_Inner", StringComparison.Ordinal)
                        ? F117AircraftAssembler.WingInnerAreaFraction
                        : F117AircraftAssembler.WingOuterAreaFraction;
                Require(Near(Float(data, "mass", part.name, failures), 785f * expectedFraction, 0.1f) &&
                        Near(Float(data, "wingArea", part.name, failures),
                            F117AircraftAssembler.MainWingLiftArea * expectedFraction, 0.001f),
                    part.name + " preserves its measured share of whole-wing mass and lift", failures);
            }
            else if (part.name.StartsWith("F117_Engine_", StringComparison.Ordinal))
                Require(rootBox != null &&
                        (rootBox.center - F117AircraftAssembler.EngineDamageColliderCenter).sqrMagnitude <= 0.0001f &&
                        (rootBox.size - F117AircraftAssembler.EngineDamageColliderSize).sqrMagnitude <= 0.0001f,
                    part.name + " uses the authored aft nozzle damage collider outside CentralCollider", failures);
            else if (part == centralPart)
                Require(rootBox != null &&
                        (rootBox.center - new Vector3(0f, 0.08f, 0.4f)).sqrMagnitude <= 0.0001f &&
                        (rootBox.size - new Vector3(3.4f, 1.05f, 10.2f)).sqrMagnitude <= 0.0001f,
                    "Central body owns its full direct damage collider", failures);
            else if (part.name == "F117_Nose")
                Require(rootBox != null && rootBox.center.sqrMagnitude <= 0.0001f &&
                        (rootBox.size - new Vector3(2.2f, 0.78f, 4.1f)).sqrMagnitude <= 0.0001f,
                    "Nose owns its full direct damage collider", failures);
            else if (part.name == "F117_RearBody")
                Require(rootBox != null &&
                        (rootBox.center - new Vector3(0f, 0f, -1.35f)).sqrMagnitude <= 0.0001f &&
                        (rootBox.size - new Vector3(2.8f, 0.7f, 1.5f)).sqrMagnitude <= 0.0001f,
                    "Rear body owns its full direct damage collider", failures);
            SerializedProperty joints = Property(data, "joints", part.name, failures);
            if (part == centralPart)
            {
                Require(joints != null && joints.isArray && joints.arraySize == 0,
                    "Root AeroPart has no parent joint", failures);
                Require(Near(Float(data, "mass", part.name, failures), 6990f, 0.1f),
                    "Root AeroPart carries its audited share of dry mass", failures);
            }
            else
            {
                Component physicalParent = part.transform.parent == null
                    ? null
                    : part.transform.parent.GetComponent("AeroPart");
                Require(physicalParent != null,
                    part.name + " is directly parented to another AeroPart", failures);
                if (part.name.StartsWith("F117_Wing_", StringComparison.Ordinal))
                {
                    string side = part.name.IndexOf("_Left_", StringComparison.Ordinal) >= 0
                        ? "Left"
                        : "Right";
                    string expectedParent = part.name.EndsWith("_Root", StringComparison.Ordinal)
                        ? "F117_CentralBody"
                        : part.name.EndsWith("_Inner", StringComparison.Ordinal)
                            ? "F117_Wing_" + side + "_Root"
                            : "F117_Wing_" + side + "_Inner";
                    Require(physicalParent != null && physicalParent.name == expectedParent,
                        part.name + " follows the stock root -> inner -> outer structural chain", failures);
                }
                Require(joints != null && joints.isArray && joints.arraySize >= 1,
                    part.name + " has a serialized attachment joint", failures);
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
        float neutralLiftCenterZ = horizontalLiftArea > 0f
            ? horizontalLiftMomentZ / horizontalLiftArea
            : float.NaN;
        Require(Near(horizontalLiftArea, 73f, 0.02f),
            "Horizontal lifting area totals the established 73.0 m2", failures);
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
            "True mass-weighted centre is 0.50 m ahead of the main-wheel plane", failures);
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
            if (!rudder && attachedSurface != null)
            {
                Component structuralParent = attachedSurface.transform.parent == null
                    ? null
                    : attachedSurface.transform.parent.GetComponent("AeroPart");
                string side = control.name.IndexOf("_L_", StringComparison.Ordinal) >= 0 ? "Left" : "Right";
                string section = control.name.IndexOf("_Inner", StringComparison.Ordinal) >= 0 ? "Inner" : "Outer";
                Require(structuralParent != null &&
                        structuralParent.name == "F117_Wing_" + side + "_" + section,
                    control.name + " is attached to its matching structural wing section", failures);
            }
            Vector3 animatedAxis = visibleMesh == null
                ? Vector3.zero
                : visibleMesh.transform.localRotation * Vector3.right;
            Vector3 expectedAxis = rudder ? Vector3.forward : Vector3.right;
            Require(animatedAxis.sqrMagnitude > 0f && Vector3.Dot(animatedAxis.normalized, expectedAxis) > 0.999f,
                control.name + " maps the game's local-X animation onto its audited source hinge axis", failures);
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
                Require(Near(Mathf.Abs(pitchRange), F117AircraftAssembler.ElevonPitchTravel, 0.01f) &&
                        Near(Mathf.Abs(rollRange), F117AircraftAssembler.ElevonRollTravel, 0.01f) &&
                        Near(yawRange, 0f, 0.001f),
                    control.name + " uses the full 22.5 degree actuator budget as 15 pitch + 7.5 roll", failures);
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
                float actualServoNeutral = visibleMesh == null
                    ? float.NaN
                    : Mathf.DeltaAngle(0f, visibleMesh.transform.localEulerAngles.x);
                Require(Near(actualServoNeutral, 0f, 0.01f),
                    control.name + " keeps an unbiased native servo/aerodynamic neutral", failures);
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
        foreach (Component gear in gears)
        {
            SerializedObject data = new SerializedObject(gear);
            foreach (string field in new[] { "attachedPart", "bumpStop", "unsprung", "castPoint", "axle", "aircraft", "tireNoiseSound", "tireSkidSound", "gearCollider", "gearHinge", "strutRotationTransform", "dust" })
                RequireRef(data, field, gear.name, failures);
            RequireArray(data, "wheels", 1, gear.name, failures);
            ParticleSystem dust = Ref(data, "dust") as ParticleSystem;
            AudioSource skidSource = Ref(data, "tireSkidSound") as AudioSource;
            ParticleSystemRenderer dustRenderer = dust == null ? null : dust.GetComponent<ParticleSystemRenderer>();
            Require(dust != null && !dust.emission.enabled && dust.main.maxParticles == 0,
                gear.name + " placeholder dust system cannot emit particles", failures);
            Require(dustRenderer != null && dustRenderer.sharedMaterial != null && !dustRenderer.enabled,
                gear.name + " dust renderer serializes disabled for the runtime safety lock", failures);
            Require(skidSource != null && skidSource.mute,
                gear.name + " suppresses the custom-rig false-positive skid squeal", failures);
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
                float castEndY = prefab.transform.InverseTransformPoint(castEnd).y;
                if (noseGear)
                {
                    float noseInitialClearance = castEndY + groundSpawnHeight;
                    Require(noseInitialClearance >= 0.20f && noseInitialClearance <= 0.27f,
                        gear.name + " retains the source-authored nose-up ground attitude before settling " +
                        "(initial clearance=" + noseInitialClearance.ToString("0.000") + ")", failures);
                }
                else
                {
                    Require(Mathf.Abs(castEndY + groundSpawnHeight) <= 0.03f,
                        gear.name + " main tire reaches the runway at ground spawn (local end Y=" +
                        castEndY.ToString("0.000") + ")", failures);
                }
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
                    Require(Mathf.Abs(renderedBottomY - castEndY) <= 0.05f,
                        gear.name + " physics contact matches its rendered tire bottom (rendered=" +
                        renderedBottomY.ToString("0.000") + ", contact=" + castEndY.ToString("0.000") + ")", failures);
                }
            }
        }

        Component fuelTank = Single(components, "FuelTank", failures);
        if (fuelTank != null)
        {
            SerializedObject data = new SerializedObject(fuelTank);
            RequireRef(data, "part", fuelTank.name, failures);
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
            Renderer originalShell = prefab.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer => renderer.name == "F117_Exterior_Mesh");
            Renderer[] damageShells = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.IndexOf("_Skin_", StringComparison.Ordinal) >= 0)
                .ToArray();
            Require(originalShell == null && damageShells.Length >= 5,
                "Monolithic exterior is replaced by AeroPart-owned damage renderers", failures);
            Renderer[] seamCaps = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.EndsWith("_SeamCaps", StringComparison.Ordinal))
                .ToArray();
            Require(seamCaps.Length == 9,
                "All nine fixed-airframe damage sections have closed cross-section caps", failures);
            foreach (Renderer seamCap in seamCaps)
            {
                Mesh capMesh = seamCap.GetComponent<MeshFilter>()?.sharedMesh;
                Material capMaterial = seamCap.sharedMaterial;
                Component owningPart = null;
                for (Transform current = seamCap.transform; current != null && owningPart == null;
                     current = current.parent)
                    owningPart = current.GetComponent("AeroPart");
                SerializedProperty ownedRenderers = owningPart == null
                    ? null
                    : new SerializedObject(owningPart).FindProperty("damageMaterial")
                        ?.FindPropertyRelative("renderers");
                bool registered = ownedRenderers != null && ownedRenderers.isArray &&
                    Enumerable.Range(0, ownedRenderers.arraySize).Any(index =>
                        ownedRenderers.GetArrayElementAtIndex(index).objectReferenceValue == seamCap);
                Require(seamCap.enabled && capMesh != null && capMesh.triangles.Length >= 6 &&
                        capMaterial != null && capMaterial.name == "F117_DamageSeamCap" &&
                        capMaterial.shader != null &&
                        capMaterial.shader.name == "Universal Render Pipeline/Lit" &&
                        Near(capMaterial.GetFloat("_Surface"), 0f, 0.001f) &&
                        Near(capMaterial.GetFloat("_ZWrite"), 1f, 0.001f) &&
                        owningPart != null && registered,
                    seamCap.name +
                    " is an opaque, physical-part-owned cross-section registered for native damage",
                    failures);
            }
            var bulkheadPlanes =
                new Dictionary<string, Dictionary<Renderer, List<Vector2>>>(StringComparer.Ordinal);
            foreach (Renderer seamCap in seamCaps)
            {
                Mesh capMesh = seamCap.GetComponent<MeshFilter>()?.sharedMesh;
                if (capMesh == null)
                    continue;
                Vector3[] capVertices = capMesh.vertices;
                int[] capTriangles = capMesh.triangles;
                for (int triangle = 0; triangle < capTriangles.Length; triangle += 3)
                {
                    Vector3 first = prefab.transform.InverseTransformPoint(
                        seamCap.transform.TransformPoint(capVertices[capTriangles[triangle]]));
                    Vector3 second = prefab.transform.InverseTransformPoint(
                        seamCap.transform.TransformPoint(capVertices[capTriangles[triangle + 1]]));
                    Vector3 third = prefab.transform.InverseTransformPoint(
                        seamCap.transform.TransformPoint(capVertices[capTriangles[triangle + 2]]));
                    Vector3 normal = Vector3.Cross(second - first, third - first).normalized;
                    if (normal.sqrMagnitude < 0.5f)
                        continue;
                    if (normal.x < -0.0001f ||
                        (Mathf.Abs(normal.x) <= 0.0001f && normal.y < -0.0001f) ||
                        (Mathf.Abs(normal.x) <= 0.0001f && Mathf.Abs(normal.y) <= 0.0001f && normal.z < 0f))
                        normal = -normal;
                    float distance = Vector3.Dot(normal, first);
                    string planeKey = Mathf.RoundToInt(normal.x * 1000f) + ":" +
                        Mathf.RoundToInt(normal.y * 1000f) + ":" +
                        Mathf.RoundToInt(normal.z * 1000f) + ":" +
                        Mathf.RoundToInt(distance * 1000f);
                    if (!bulkheadPlanes.TryGetValue(planeKey,
                            out Dictionary<Renderer, List<Vector2>> footprints))
                    {
                        footprints = new Dictionary<Renderer, List<Vector2>>();
                        bulkheadPlanes.Add(planeKey, footprints);
                    }
                    if (!footprints.TryGetValue(seamCap, out List<Vector2> footprint))
                    {
                        footprint = new List<Vector2>();
                        footprints.Add(seamCap, footprint);
                    }
                    Vector3 tangentReference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f
                        ? Vector3.right
                        : Vector3.up;
                    Vector3 tangent = Vector3.Cross(tangentReference, normal).normalized;
                    Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
                    foreach (Vector3 point in new[] { first, second, third })
                        footprint.Add(new Vector2(Vector3.Dot(point, tangent),
                            Vector3.Dot(point, bitangent)));
                }
            }
            var duplicateBulkheadPlanes = new List<string>();
            foreach (KeyValuePair<string, Dictionary<Renderer, List<Vector2>>> plane in bulkheadPlanes)
            {
                KeyValuePair<Renderer, List<Vector2>>[] footprints = plane.Value.ToArray();
                for (int firstIndex = 0; firstIndex < footprints.Length; firstIndex++)
                    for (int secondIndex = firstIndex + 1; secondIndex < footprints.Length; secondIndex++)
                    {
                        List<Vector2> firstPoints = footprints[firstIndex].Value;
                        List<Vector2> secondPoints = footprints[secondIndex].Value;
                        float overlapX = Mathf.Min(firstPoints.Max(point => point.x),
                                secondPoints.Max(point => point.x)) -
                            Mathf.Max(firstPoints.Min(point => point.x),
                                secondPoints.Min(point => point.x));
                        float overlapY = Mathf.Min(firstPoints.Max(point => point.y),
                                secondPoints.Max(point => point.y)) -
                            Mathf.Max(firstPoints.Min(point => point.y),
                                secondPoints.Min(point => point.y));
                        if (overlapX > 0.001f && overlapY > 0.001f)
                            duplicateBulkheadPlanes.Add(plane.Key + " [" +
                                (footprints[firstIndex].Key.transform.parent?.name ??
                                 footprints[firstIndex].Key.name) + " overlaps " +
                                (footprints[secondIndex].Key.transform.parent?.name ??
                                 footprints[secondIndex].Key.name) + "]");
                    }
            }
            Require(duplicateBulkheadPlanes.Count == 0,
                "Damage-section bulkheads are recessed onto distinct planes and cannot z-fight into exterior triangles" +
                (duplicateBulkheadPlanes.Count == 0 ? string.Empty : ": " +
                    string.Join("; ", duplicateBulkheadPlanes)),
                failures);
            Require(exterior == null || !exterior.isArray || damageShells.All(shell =>
                    !Enumerable.Range(0, exterior.arraySize).Any(index =>
                        exterior.GetArrayElementAtIndex(index).objectReferenceValue == shell)),
                "Damage-part airframe renderers are never disabled by cockpit camera switching", failures);
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
            foreach (Renderer damageShell in damageShells)
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
                    // below the airframe. True geometric section clipping creates one
                    // legitimate nose-skin triangle at its old X/Z threshold, so also
                    // require the protruding below-belly Y position that distinguished
                    // the ladder from the aircraft shell.
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
            Transform statusPart = statusPrefab.transform.Find("F117_CentralBody");
            RectTransform statusRootRect = statusPrefab.GetComponent<RectTransform>();
            Require(statusRootRect != null &&
                    Vector2.Distance(statusRootRect.anchorMin, new Vector2(1f, 0f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.anchorMax, new Vector2(1f, 0f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.pivot, new Vector2(1f, -0.05f)) < 0.001f &&
                    Vector2.Distance(statusRootRect.anchoredPosition, Vector2.zero) < 0.001f &&
                    Vector2.Distance(statusRootRect.sizeDelta, new Vector2(260f, 260f)) < 0.001f,
                "Status HUD uses the retail bottom-right 260 px anchor and pivot contract", failures);
            Require(statusPart != null,
                "Status damage image is named for F117_CentralBody", failures);
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
        Require(manifestJson.IndexOf("F117_Physics", StringComparison.Ordinal) < 0 &&
                manifestJson.IndexOf("F117_Visual", StringComparison.Ordinal) < 0,
            "Manifest contains no obsolete F-117 wrapper paths", failures);
        Require(manifestJson.IndexOf("\"componentType\": \"RadarJammer, Assembly-CSharp\"", StringComparison.Ordinal) < 0,
            "Manifest contains no defensive RadarJammer countermeasure patch", failures);
        Require(manifestJson.Contains("\"locator\": \"JammingPod1\"") &&
                manifestJson.Contains("hardpointSets[2].weaponOptions[0]") &&
                manifestJson.Contains("loadouts[0].weapons[2]") &&
                manifestJson.Contains("StandardLoadouts[0].loadout.weapons[2]"),
            "Manifest installs native JammingPod1 on the fixed active-jammer station and every loadout", failures);
        foreach (string side in new[] { "Left", "Right" })
            Require(manifestJson.Contains("F117_CentralBody/F117_RearBody/F117_Engine_" + side),
                "Manifest patches the " + side.ToLowerInvariant() +
                " engine under F117_RearBody so the joint suppresses their overlapping collision", failures);
        foreach (string weapon in F117Builder.WeaponAssetNames)
            Require(manifestJson.Contains("\"locator\": \"" + weapon + "\""), "Manifest patches weapon " + weapon, failures);
        foreach (string asset in new[] { "IRFlare", "flare1", "weaponicon_flares", "weaponicon_radarJammer" })
            Require(manifestJson.Contains("\"locator\": \"" + asset + "\""),
                "Manifest patches countermeasure asset " + asset, failures);
        Require(manifestJson.Contains("\"name\": \"Shader Graphs/AircraftSkin\"") &&
                damageSkinMaterials.All(material => manifestJson.Contains(material.name + "/shader")),
            "Manifest resolves every F-117 damage skin material to the native AircraftSkin shader", failures);

        notes.Add("Validated root: " + prefab.name);
        notes.Add("Components: AeroPart=17, ControlSurface=6, LandingGear=3, Turbojet=2, JetNozzle=2, BayDoor=2, FlareEjector=1, ChaffEjector=1, RadarJammer=0, Radar=0");
        notes.Add("Countermeasures: 32 native flares, 64 native chaff, two central-body ejection points each, visible material-backed RadarChaff payload");
        notes.Add("Active jammer: native JammingPod1 weapon, permanently installed and target-fired; no defensive RadarJammer countermeasure");
        notes.Add("Electrical: dedicated 60 kJ jammer bus; native 13-unit draw gives about 5 s full-charge burst; two engines recharge at up to 1.16 kJ/s (about 52 s empty-to-full)");
        notes.Add("Physics graph: one root AeroPart plus 16 parent-matched, jointed descendants; each wing uses stock-style root -> inner -> outer structure, elevons attach to matching panels, and rudders attach to rear body");
        notes.Add("Hitboxes: all 17 AeroParts directly own their real colliders; six wing colliders are generated from their clipped planforms; native bullets/blast fragments cannot be swallowed by non-damageable child objects; full unrelated-part penetration audit passed");
        notes.Add("Damage model: 17 non-critical, standard 100 HP AeroParts; fixed airframe triangles are geometrically clipped and backed by inset opaque bulkheads at all nine rigidbody boundaries; controls own their render geometry, AircraftSkin pockmark textures, status reporting, native fuel fire/leak effects, and physical detachment");
        notes.Add("Elevon neutral: unbiased native servo/aero pivots; measured inner-panel visual corrections isolated below them");
        notes.Add("Mass: dry graph=13380 kg; full internal fuel=21630 kg; MTOW=23814 kg; payload margin=2184 kg; runtime CoM Z=" +
            runtimeCenterOfMass.z.ToString("0.00") + " m");
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
        notes.Add("Liveries: Nighthawk Black plus five Farewell Flag metal-finish choices; exact 50-star/13-stripe " +
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
        notes.Add("Landing gear: aircraft-forward tire-physics frames, stock-sized 0.60 m ground probes on all three legs, full probe travel before BreakWheel, false-positive skid audio muted");
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
            bool excluded = ProfileHierarchyExcluded(renderer.transform) ||
                renderer.name.StartsWith(F117AircraftAssembler.ParadeFlagOverlayPrefix,
                    StringComparison.Ordinal);
            bool staticAccessory = ProfileStaticAccessory(renderer.transform);
            foreach (Material material in renderer.sharedMaterials)
            {
                string canonical = CanonicalProfileMaterialName(material == null ? null : material.name);
                bool frame = renderer.name == "F117_Canopy_Mesh" && canonical == "INT_CockpitFrame";
                bool tire = canonical == "F117_Tires";
                bool exterior = canonical != null && canonical.StartsWith("F117_EXTERNAL_", StringComparison.Ordinal) &&
                    canonical.Length == "F117_EXTERNAL_1".Length &&
                    canonical[canonical.Length - 1] >= '1' && canonical[canonical.Length - 1] <= '7';
                bool body = exterior && !excluded && !staticAccessory;
                if (body)
                {
                    bodySlots++;
                    bodyFamilies.Add(canonical);
                    if (ProfileHierarchyExcluded(renderer.transform) || staticAccessory)
                        invalidTargets.Add(GetPath(prefab.transform, renderer.transform));
                }
                if (exterior && staticAccessory)
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
            "Profile classification isolates gear, gear doors, bay linkages, drag chute, and all three tire renderers from body tint",
            failures);
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
        Require(importer != null && importer.textureType == TextureImporterType.Sprite &&
                importer.sRGBTexture && importer.alphaIsTransparency && importer.mipmapEnabled &&
                importer.npotScale == TextureImporterNPOTScale.None && importer.filterMode == FilterMode.Bilinear &&
                importer.anisoLevel == 1 && importer.wrapModeU == TextureWrapMode.Repeat &&
                importer.wrapModeV == TextureWrapMode.Repeat && importer.maxTextureSize == 2048,
            "Panel " + panel + " matte MS matches Aryx_F16M_Skin_MS color-space and sampling", failures);

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
        Texture2D raw = LoadRawPng(MirrorFinishTexturePath);
        try
        {
            Color32 pixel = raw == null ? default : raw.GetPixel(0, 0);
            Require(imported != null && raw != null && raw.width == 1 && raw.height == 1 &&
                    pixel.r == 255 && pixel.g == 255 && pixel.b == 255 && pixel.a == 240 &&
                    AssetDatabase.AssetPathToGUID(MirrorFinishTexturePath) == MirrorFinishTextureGuid,
                "Bundled mirror MS is exact RGBA=(1,1,1,0.94) at its fixed path and GUID", failures);
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

    private static void ValidateGearDoorOverlays(Transform prefabRoot, Transform visualRoot,
        List<string> failures)
    {
        if (visualRoot == null)
            return;
        string[] hingeNames =
        {
            "F117_GearDoor_Nose_CloseHinge",
            "F117_GearDoor_Left_Outer_CloseHinge", "F117_GearDoor_Left_Inner_CloseHinge",
            "F117_GearDoor_Right_Outer_CloseHinge", "F117_GearDoor_Right_Inner_CloseHinge"
        };
        Transform[] hinges = hingeNames.Select(name => F117AuthoringUtil.FindDeep(prefabRoot, name)).ToArray();
        Require(hinges.All(hinge => hinge != null),
            "All five gear-door hinges are available for closed-pose flag validation", failures);
        if (hinges.Any(hinge => hinge == null))
            return;
        Quaternion[] rotations = hinges.Select(hinge => hinge.localRotation).ToArray();
        try
        {
            foreach (Transform hinge in hinges)
                hinge.localRotation = Quaternion.identity;
            foreach (Transform hinge in hinges)
            {
                MeshFilter source = hinge.GetComponentsInChildren<MeshFilter>(true)
                    .FirstOrDefault(filter => !filter.name.StartsWith(
                        F117AircraftAssembler.ParadeFlagOverlayPrefix, StringComparison.Ordinal) &&
                        filter.sharedMesh != null && filter.sharedMesh.name.StartsWith(
                            "F117_GearDoor_", StringComparison.Ordinal));
                MeshFilter[] overlays = hinge.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.name.StartsWith(
                        F117AircraftAssembler.ParadeFlagOverlayPrefix, StringComparison.Ordinal))
                    .ToArray();
                Require(source != null && overlays.Length == 1 && overlays[0].transform.IsChildOf(hinge),
                    hinge.name + " owns exactly one moving closed-pose flag overlay", failures);
                if (source == null || overlays.Length != 1)
                    continue;
                SurfaceStats expected = SelectedDownwardStats(source, visualRoot);
                SurfaceStats actual = AllTriangleStats(overlays[0], visualRoot);
                float areaError = expected.Area <= 0f
                    ? float.PositiveInfinity
                    : Mathf.Abs(actual.Area - expected.Area) / expected.Area;
                Require(expected.Triangles > 0 && actual.Triangles == expected.Triangles && areaError <= 0.01f,
                    hinge.name + " overlay reproduces every eligible closed-pose downward triangle within 1% area" +
                    " (expected " + expected.Triangles + "/" + expected.Area.ToString("0.000") +
                    ", actual " + actual.Triangles + "/" + actual.Area.ToString("0.000") + ")", failures);
            }
        }
        finally
        {
            for (int index = 0; index < hinges.Length; index++)
                hinges[index].localRotation = rotations[index];
        }
    }

    private readonly struct SurfaceStats
    {
        internal readonly int Triangles;
        internal readonly float Area;

        internal SurfaceStats(int triangles, float area)
        {
            Triangles = triangles;
            Area = area;
        }
    }

    private static SurfaceStats SelectedDownwardStats(MeshFilter filter, Transform visualRoot)
    {
        Mesh mesh = filter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        int count = 0;
        float area = 0f;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            int[] triangles = mesh.GetTriangles(subMesh);
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                Vector3 selection = hasNormals ? (normals[a] + normals[b] + normals[c]).normalized : face;
                Vector3 rootNormal = visualRoot.InverseTransformDirection(
                    filter.transform.TransformDirection(selection)).normalized;
                if (Vector3.Dot(rootNormal, Vector3.down) < 0.35f)
                    continue;
                area += RootTriangleArea(filter.transform, visualRoot, vertices[a], vertices[b], vertices[c]);
                count++;
            }
        }
        return new SurfaceStats(count, area);
    }

    private static SurfaceStats AllTriangleStats(MeshFilter filter, Transform visualRoot)
    {
        Mesh mesh = filter.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        float area = 0f;
        for (int index = 0; index + 2 < triangles.Length; index += 3)
            area += RootTriangleArea(filter.transform, visualRoot,
                vertices[triangles[index]], vertices[triangles[index + 1]], vertices[triangles[index + 2]]);
        return new SurfaceStats(triangles.Length / 3, area);
    }

    private static float RootTriangleArea(Transform source, Transform root,
        Vector3 a, Vector3 b, Vector3 c)
    {
        a = root.InverseTransformPoint(source.TransformPoint(a));
        b = root.InverseTransformPoint(source.TransformPoint(b));
        c = root.InverseTransformPoint(source.TransformPoint(c));
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static bool OverlayFacesDown(Renderer renderer, Transform aircraftRoot)
    {
        MeshFilter filter = renderer == null ? null : renderer.GetComponent<MeshFilter>();
        Mesh mesh = filter == null ? null : filter.sharedMesh;
        if (mesh == null || mesh.normals == null || mesh.normals.Length != mesh.vertexCount)
            return false;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        if (triangles.Length == 0)
            return false;
        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Vector3 normal = (normals[triangles[index]] + normals[triangles[index + 1]] +
                normals[triangles[index + 2]]).normalized;
            Vector3 rootNormal = aircraftRoot.InverseTransformDirection(
                renderer.transform.TransformDirection(normal)).normalized;
            if (Vector3.Dot(rootNormal, Vector3.down) < 0.34f)
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
            if (material.HasProperty(property))
            {
                Texture texture = material.GetTexture(property);
                if (texture != null)
                    return texture;
            }
            SerializedProperty entries = new SerializedObject(material)
                .FindProperty("m_SavedProperties.m_TexEnvs");
            if (entries == null || !entries.isArray)
                continue;
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
        return null;
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

    private static int RegexCount(string value, string needle)
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
