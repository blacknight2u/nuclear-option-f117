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
    private const string StatusPath = "Assets/F117/Generated/F117A_Nighthawk_StatusDisplay.prefab";
    private const string RadarChaffPrefabPath = "Assets/F117/Generated/F117_RadarChaff.prefab";
    private const string ManifestPath = "Assets/F117/Generated/patch_manifest.json";
    private const string ReportPath = @"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Contract_Validation.txt";

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
            { "Aircraft", 1 }, { "AutopilotPlane", 1 }, { "AeroPart", 13 }, { "BayDoor", 2 },
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
            Require(Near(Float(definitionData, "radarSize", definition.name, failures), 0.0001f, 0.000001f),
                "Closed clean-aircraft definition has the nonzero 0.0001 radar return", failures);
            Require(Near(Float(definitionData, "visibleRange", definition.name, failures), 2500f, 0.01f),
                "Aircraft optical visibility is 2.5 km", failures);
            Require(Near(Float(definitionData, "mass", definition.name, failures), 13380f, 0.1f),
                "Simple-physics mass is the 13,380 kg dry weight", failures);
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
                RequireRef(parameterData, "HUDExtras", parameters.name, failures);
                RequireArray(parameterData, "loadouts", F117Builder.WeaponLoadoutCount, parameters.name, failures);
                SerializedProperty loadouts = Property(parameterData, "loadouts", parameters.name, failures);
                if (loadouts != null && loadouts.isArray)
                    for (int index = 0; index < loadouts.arraySize; index++)
                    {
                        SerializedProperty weapons = loadouts.GetArrayElementAtIndex(index).FindPropertyRelative("weapons");
                        Require(weapons != null && weapons.arraySize == 2,
                            "Loadout " + index + " contains payload plus fixed active jammer", failures);
                    }
                SerializedProperty standards = Property(parameterData, "StandardLoadouts", parameters.name, failures);
                Require(standards != null && standards.isArray && standards.arraySize == F117Builder.WeaponLoadoutCount,
                    "Aircraft has one named loadout for every supported internal rack", failures);
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
                        Require(standardWeapons != null && standardWeapons.arraySize == 2,
                            "Standard loadout " + index + " contains payload plus fixed active jammer", failures);
                    }
                }
                SerializedProperty liveries = Property(parameterData, "liveries", parameters.name, failures);
                Require(liveries != null && liveries.isArray && liveries.arraySize == 1,
                    "Aircraft has exactly one clean F-117 livery", failures);
                if (liveries != null && liveries.isArray && liveries.arraySize == 1)
                {
                    SerializedProperty entry = liveries.GetArrayElementAtIndex(0);
                    Require(entry.FindPropertyRelative("name").stringValue == "Nighthawk Black",
                        "F-117 livery has its own display name", failures);
                    SerializedProperty reference = entry.FindPropertyRelative("assetReference");
                    string expectedGuid = AssetDatabase.AssetPathToGUID(LiveryPath);
                    Require(reference != null && !string.IsNullOrEmpty(expectedGuid) &&
                            reference.FindPropertyRelative("m_AssetGUID").stringValue == expectedGuid,
                        "F-117 livery addressable key matches its generated asset", failures);
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
        UnityEngine.Object liveryAsset = AssetDatabase.LoadMainAssetAtPath(LiveryPath);
        Require(liveryAsset != null && liveryAsset.GetType().Name == "LiveryData", "Clean F-117 livery asset loads", failures);
        if (liveryAsset != null)
        {
            SerializedObject liveryData = new SerializedObject(liveryAsset);
            SerializedProperty texture = Property(liveryData, "Texture", liveryAsset.name, failures);
            SerializedProperty colors = Property(liveryData, "Colors", liveryAsset.name, failures);
            Require(texture != null && texture.objectReferenceValue == null,
                "F-117 livery cannot replace production textures with donor artwork", failures);
            Require(colors != null && colors.isArray && colors.arraySize == 0,
                "F-117 livery contains no donor color table", failures);
        }
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
            int expectedAmmo = ejectorType == "FlareEjector" ? 16 : 64;
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
            Require(Near(Float(powerData, "maxCharge", "PowerSupply", failures), 300f, 0.001f),
                "PowerSupply base capacity retains the stock 300 kJ bus", failures);
            Require(Near(Float(powerData, "maxPower", "PowerSupply", failures), 60f, 0.001f),
                "PowerSupply retains the stock 60-unit power rating", failures);
            Require(Near(Float(powerData, "chargePerRPM", "PowerSupply", failures), 0.0015f, 0.000001f),
                "PowerSupply splits the native generator coefficient across two engines", failures);
        }
        MeshCollider[] authoredMeshColliders = prefab.GetComponentsInChildren<MeshCollider>(true);
        Require(authoredMeshColliders.Length == 6 && authoredMeshColliders.All(collider =>
                collider.convex && collider.sharedMesh != null && collider.sharedMesh.vertexCount <= 255 &&
                collider.GetComponent("ControlSurface") != null),
            "Only the six low-poly convex control-surface MeshColliders are present", failures);

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
        int albedoCount = productionMaterials.Count(material => Texture(material, "_BaseMap") != null);
        int compatibilityAlbedoCount = productionMaterials.Count(material => Texture(material, "_MainTex") != null);
        int normalCount = productionMaterials.Count(material => Texture(material, "_BumpMap") != null);
        int maskCount = productionMaterials.Count(material => Texture(material, "_MetallicGlossMap") != null);
        int emissionCount = productionMaterials.Count(material => Texture(material, "_EmissionMap") != null);
        int bakedLadderTriangleCount = -1;
        Require(albedoCount >= 24, "At least 24 production materials retain albedo textures", failures);
        Require(compatibilityAlbedoCount == albedoCount,
            "Every production albedo is bound to the extracted-shader compatibility slot", failures);
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
            Require(material != null && Texture(material, "_BaseMap") != null,
                name + " has a bound production albedo texture", failures);
        }
        Material[] exteriorPanelMaterials = productionMaterials
            .Where(material => material.name.IndexOf("F117_EXTERNAL_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        Require(exteriorPanelMaterials.Length >= 7,
            "All exterior panel material families are present", failures);
        foreach (Material material in exteriorPanelMaterials)
        {
            Require(Texture(material, "_BaseMap") != null && Texture(material, "_MainTex") != null &&
                    Texture(material, "_BumpMap") != null && Texture(material, "_MetallicGlossMap") != null,
                material.name + " resolves Blender duplicate suffixes and retains every production texture binding",
                failures);
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

        Require(aeroParts.Length == 13, "Exactly 13 aerodynamic/mass parts", failures);
        Component centralPart = productionVisual == null
            ? null
            : aeroParts.FirstOrDefault(part => part.transform == productionVisual);
        Require(centralPart != null, "F117_CentralBody owns the root AeroPart", failures);
        float mass = 0f;
        float area = 0f;
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
            Require(Near(Float(data, "airflowChanneling", part.name, failures), 0f, 0.0001f),
                part.name + " uses its actual lift-transform angle without artificial airflow alignment", failures);
            Require(part.GetComponent<Collider>() != null,
                part.name + " owns a collider on the AeroPart GameObject so UnitPart.Awake can fill collisionSize", failures);
            Collider rootCollider = part.GetComponent<Collider>();
            BoxCollider rootBox = part.GetComponent<BoxCollider>();
            if (isControlSurfacePart)
            {
                MeshCollider meshCollider = part.GetComponent<MeshCollider>();
                Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
                Bounds geometryBounds = F117AircraftAssembler.CalculateRendererGeometryBounds(part.transform, renderers);
                Vector3 expectedSize = Vector3.Max(
                    geometryBounds.size * F117AircraftAssembler.ControlSurfaceColliderInset,
                    Vector3.one * F117AircraftAssembler.ControlSurfaceColliderMinSize);
                Require(rootBox == null && meshCollider != null && meshCollider.convex &&
                        meshCollider.sharedMesh != null && meshCollider.sharedMesh.vertexCount <= 255 &&
                        (meshCollider.sharedMesh.bounds.center - geometryBounds.center).sqrMagnitude <= 0.0001f &&
                        (meshCollider.sharedMesh.bounds.size - expectedSize).sqrMagnitude <= 0.0001f,
                    part.name + " uses an inset convex root-space mesh collider like the working Aryx aircraft", failures);
            }
            else
                Require(rootBox != null && rootBox.size.x <= 1.01f && rootBox.size.y <= 1.01f && rootBox.size.z <= 1.01f,
                    part.name + " uses a contained Awake proxy instead of a broad collider envelope", failures);
            if (part.name.StartsWith("F117_Engine_", StringComparison.Ordinal))
                Require(rootBox != null &&
                        (rootBox.center - F117AircraftAssembler.EngineDamageColliderCenter).sqrMagnitude <= 0.0001f &&
                        (rootBox.size - F117AircraftAssembler.EngineDamageColliderSize).sqrMagnitude <= 0.0001f,
                    part.name + " uses the authored aft nozzle damage collider outside CentralCollider", failures);
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
        Require(Near(mass, 13380f, 0.1f), "Connected AeroPart graph totals the 13,380 kg dry mass", failures);
        Vector3 runtimeCenterOfMass = mass > 0f ? massMoment / mass : Vector3.zero;
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
                    rightOwner.name + "/" + right.name + " at spawn", failures);
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
                    gear.name + " has enough cast travel for uneven terrain", failures);
                Require(Near(Float(data, "contactArea", gear.name, failures),
                        F117AircraftAssembler.NoseGearContactArea, 0.001f),
                    gear.name + " has sufficient soft-ground contact area", failures);
                Require(Near(Float(data, "steeringSpeed", gear.name, failures),
                        F117AircraftAssembler.NoseSteeringSpeed, 0.01f) &&
                        Near(Float(data, "aligningStrength", gear.name, failures),
                        F117AircraftAssembler.NoseAligningStrength, 0.01f),
                    gear.name + " uses the stable low-bias steering response", failures);
            }
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
                Require(link.GetComponentsInChildren<Renderer>(true).Length == 1,
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
            Require(sets != null && sets.isArray && sets.arraySize == 2,
                "One internal store rack plus one fixed active-jammer station", failures);
            if (sets != null && sets.isArray && sets.arraySize == 2)
            {
                SerializedProperty set = sets.GetArrayElementAtIndex(0);
                SerializedProperty options = set.FindPropertyRelative("weaponOptions");
                SerializedProperty hardpoints = set.FindPropertyRelative("hardpoints");
                Require(options != null && options.arraySize == F117Builder.WeaponOptionCount,
                    "Internal rack exposes empty plus every supported multi-store option", failures);
                Require(hardpoints != null && hardpoints.arraySize == 1, "Internal bay has one center rack socket", failures);
                if (hardpoints != null)
                {
                    for (int index = 0; index < hardpoints.arraySize; index++)
                    {
                        SerializedProperty hardpoint = hardpoints.GetArrayElementAtIndex(index);
                        RequireRelativeRef(hardpoint, "transform", "Hardpoint " + index, failures);
                        Transform socket = hardpoint.FindPropertyRelative("transform")?.objectReferenceValue as Transform;
                        Require(socket != null &&
                                Near(prefab.transform.InverseTransformPoint(socket.position).y,
                                    F117AircraftAssembler.InternalStoreMountHeight, 0.01f),
                            "Hardpoint " + index + " is on the audited internal bay mount plane", failures);
                        Require(socket != null && socket.name == "F117_InternalRackSocket" &&
                                Near(prefab.transform.InverseTransformPoint(socket.position).x, 0f, 0.01f),
                            "Hardpoint " + index + " uses the centered F-117 internal rack socket", failures);
                        RequireRelativeRef(hardpoint, "part", "Hardpoint " + index, failures);
                        SerializedProperty bayDoors = hardpoint.FindPropertyRelative("bayDoors");
                        Require(bayDoors != null && bayDoors.arraySize == 2 &&
                                bayDoors.GetArrayElementAtIndex(0).objectReferenceValue != null &&
                                bayDoors.GetArrayElementAtIndex(1).objectReferenceValue != null,
                            "Hardpoint " + index + " opens both internal bay doors", failures);
                        SerializedProperty doorOpenDuration = hardpoint.FindPropertyRelative("doorOpenDuration");
                        Require(doorOpenDuration != null && Near(doorOpenDuration.floatValue, 1.2f, 0.001f),
                            "Hardpoint " + index + " keeps both bays open for 1.2 seconds after the final release", failures);
                        RequireRelativeArray(hardpoint, "BuiltInWeapons", 0, "Hardpoint " + index, failures);
                        RequireRelativeArray(hardpoint, "BuiltInTurrets", 0, "Hardpoint " + index, failures);
                    }
                }

                SerializedProperty jammerSet = sets.GetArrayElementAtIndex(1);
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
            Require(cockpit != null && cockpit.isArray && cockpit.arraySize >= 1 && detailedCockpit != null &&
                    Enumerable.Range(0, cockpit.arraySize).Any(index =>
                        cockpit.GetArrayElementAtIndex(index).objectReferenceValue == detailedCockpit),
                "Cockpit renderer group contains the dedicated F-117 interior", failures);
            Require(exterior != null && exterior.isArray && exterior.arraySize >= 1 && exteriorCanopy != null &&
                    Enumerable.Range(0, exterior.arraySize).Any(index =>
                        exterior.GetArrayElementAtIndex(index).objectReferenceValue == exteriorCanopy),
                "Exterior renderer group contains the dedicated F-117 canopy", failures);
            Renderer mainShell = prefab.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer => renderer.name == "F117_Exterior_Mesh");
            Require(mainShell != null && (exterior == null || !exterior.isArray ||
                    !Enumerable.Range(0, exterior.arraySize).Any(index =>
                        exterior.GetArrayElementAtIndex(index).objectReferenceValue == mainShell)),
                "Main airframe shell is never disabled by cockpit camera switching", failures);
            Mesh exteriorMesh = mainShell == null ? null : mainShell.GetComponent<MeshFilter>()?.sharedMesh;
            if (exteriorMesh != null)
            {
                bakedLadderTriangleCount = 0;
                Vector3[] vertices = exteriorMesh.vertices;
                int[] triangles = exteriorMesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 localCenter = (vertices[triangles[index]] + vertices[triangles[index + 1]] +
                        vertices[triangles[index + 2]]) / 3f;
                    Vector3 rootCenter = prefab.transform.InverseTransformPoint(
                        mainShell.transform.TransformPoint(localCenter));
                    if (rootCenter.x > 1.75f && rootCenter.z > 5.2f)
                        bakedLadderTriangleCount++;
                }
            }
            Require(bakedLadderTriangleCount == 0,
                "Production exterior contains no baked boarding-ladder geometry", failures);
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
            Require(Near(Float(data, "RCS", aircraft.name, failures), 0.0001f, 0.000001f),
                "Prefab fallback RCS is the clean 0.0001 baseline before the runtime bay/gear controller attaches", failures);
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
                manifestJson.Contains("hardpointSets[1].weaponOptions[0]") &&
                manifestJson.Contains("loadouts[0].weapons[1]") &&
                manifestJson.Contains("StandardLoadouts[0].loadout.weapons[1]"),
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

        notes.Add("Validated root: " + prefab.name);
        notes.Add("Components: AeroPart=13, ControlSurface=6, LandingGear=3, Turbojet=2, JetNozzle=2, BayDoor=2, FlareEjector=1, ChaffEjector=1, RadarJammer=0, Radar=0");
        notes.Add("Countermeasures: 16 native flares, 64 native chaff, two central-body ejection points each, visible material-backed RadarChaff payload");
        notes.Add("Active jammer: native JammingPod1 weapon, permanently installed and target-fired; no defensive RadarJammer countermeasure");
        notes.Add("Electrical: stock 300 kJ bus and two engines share the native 0.003 charge/RPM rate; JammingPod1 retains its native draw");
        notes.Add("Physics graph: one root AeroPart plus 12 parent-matched, jointed descendants; elevons attach to wings and rudders to rear body");
        notes.Add("Control collisions: six inset convex mesh colliders matching the working Aryx pattern; full unrelated-part penetration audit passed");
        notes.Add("Damage model: 13 non-critical, standard 100 HP AeroParts; common FastBomber 20/60 armor, 5x/6x tolerances, -25 structural margin; controls retain 120 kN parent-matched attachments");
        notes.Add("Elevon neutral: unbiased native servo/aero pivots; measured inner-panel visual corrections isolated below them");
        notes.Add("Mass: dry graph=13380 kg; full internal fuel=21630 kg; MTOW=23814 kg; payload margin=2184 kg; runtime CoM Z=" +
            runtimeCenterOfMass.z.ToString("0.00") + " m");
        notes.Add("Aerodynamic area: 77.449 m^2 total (62.180 m^2 fixed + 10.820 m^2 measured elevons + 4.449 m^2 full vertical tails)");
        notes.Add("Engine thrust: 2 x 47150 N");
        notes.Add("Weapons: 1 centered native multi-store rack at Y=" + F117AircraftAssembler.InternalStoreMountHeight.ToString("0.00") +
            " m, " + F117Builder.WeaponOptionCount + " option slots (empty + " + F117Builder.WeaponLoadoutCount +
            " racks), both bay doors linked; store counts span 6/4/2/1; a second hidden station permanently mounts JammingPod1");
        notes.Add("Bomb-bay mechanism: two source-derived strut tracks per door, nine door-angle poses each; struts remain independent of rigid panels");
        notes.Add("Materials: albedo=" + albedoCount + ", compatibility albedo=" + compatibilityAlbedoCount +
            ", normal=" + normalCount + ", mask=" + maskCount + ", emission=" + emissionCount);
        notes.Add("Stealth: clean RCS=0.0001; each bay adds up to 0.04 independently; gear adds up to 0.05 progressively; internal stores remain shielded");
        notes.Add("Sensors: no emitting search Radar; passive EOTS=15 km/3x, passive RadarLocator retained; optical visibility=2.5 km");
        notes.Add("Infrared: two forward-aligned sources, 0.5 idle to 2.2 full dry thrust; no afterburner, vapor, or global contrail components");
        notes.Add("Status HUD: retail bottom-right 260 px layout; plugin wires embedded F-117 damage Images before initialization");
        notes.Add("Exterior: baked boarding-ladder triangles=" + bakedLadderTriangleCount);
        notes.Add("Cockpit: stock Cricket display atlas mapped without stretching across " + cockpitDisplayCount +
            " physical displays (upright center camera/radar; 90-degree-corrected left flight and right engine instruments); " +
            "root-aligned viewpoint at Y=1.39 m on the seat line");
        notes.Add("Canopy: upward 40 degree ejection opening");
        notes.Add("Landing gear: aircraft-forward tire-physics frames, full authored suspension travel before BreakWheel, false-positive skid audio muted");
        notes.Add("Rear controls: model-measured elevon area, 15 pitch + 7.5 roll travel; both rudders use coordinated -18 yaw on local-Z visual hinges");
        notes.Add("Fixed lift: aircraft-aligned native axes; removed the erroneous 9-degree nose-up aerodynamic incidence proven by v0.4.47 telemetry");
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
        File.WriteAllLines(@"C:\Users\JEDENSMORE\NuclearOption-F117\F117_Full_Component_Inventory.txt", lines);
        Debug.Log("F-117 full component inventory written: " + lines.Length + " entries.");
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

    private static Texture Texture(Material material, string property)
    {
        return material != null && material.HasProperty(property) ? material.GetTexture(property) : null;
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
        const float atlasPixelAspect = 2f; // Native TacScreenRenderer is 1024 x 512.
        for (int index = 0; index < components.Count; index++)
        {
            float minimumU = float.PositiveInfinity;
            float minimumV = float.PositiveInfinity;
            float maximumU = float.NegativeInfinity;
            float maximumV = float.NegativeInfinity;
            float minimumX = float.PositiveInfinity;
            float minimumY = float.PositiveInfinity;
            float minimumZ = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumY = float.NegativeInfinity;
            float maximumZ = float.NegativeInfinity;
            foreach (int vertex in components[index])
            {
                Vector2 uv = uvs[vertex];
                Vector3 point = vertices[vertex];
                minimumU = Mathf.Min(minimumU, uv.x);
                minimumV = Mathf.Min(minimumV, uv.y);
                maximumU = Mathf.Max(maximumU, uv.x);
                maximumV = Mathf.Max(maximumV, uv.y);
                minimumX = Mathf.Min(minimumX, point.x);
                minimumY = Mathf.Min(minimumY, point.y);
                minimumZ = Mathf.Min(minimumZ, point.z);
                maximumX = Mathf.Max(maximumX, point.x);
                maximumY = Mathf.Max(maximumY, point.y);
                maximumZ = Mathf.Max(maximumZ, point.z);
            }

            Require(minimumU >= -0.001f && minimumV >= -0.001f &&
                    maximumU <= 1.001f && maximumV <= 1.001f,
                "F-117 cockpit display " + (index + 1) + " remains inside the native screen atlas",
                failures);

            float physicalWidth = maximumX - minimumX;
            float physicalHeight = Mathf.Sqrt(
                Mathf.Pow(maximumY - minimumY, 2f) + Mathf.Pow(maximumZ - minimumZ, 2f));
            float physicalAspect = physicalHeight > 0.0001f ? physicalWidth / physicalHeight : 0f;
            bool camera = minimumU < 0.2f && minimumV < 0.01f && maximumV > 0.99f;
            bool basicFlight = minimumU > 0.75f && maximumV < 0.36f;
            bool engine = minimumU > 0.75f && minimumV > 0.35f && maximumV < 0.72f;
            bool rotatedInstrument = basicFlight || engine;
            float renderedAspect = maximumV - minimumV > 0.0001f && maximumU - minimumU > 0.0001f
                ? rotatedInstrument
                    ? (maximumV - minimumV) / ((maximumU - minimumU) * atlasPixelAspect)
                    : (maximumU - minimumU) * atlasPixelAspect / (maximumV - minimumV)
                : 0f;
            Require(physicalAspect > 0f && Near(renderedAspect, physicalAspect, 0.02f),
                "F-117 cockpit display " + (index + 1) +
                " preserves the 1024x512 source aspect without stretching",
                failures);

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
                Require(Near(minimumU, 0.09402f, 0.005f) && Near(maximumU, 0.69771f, 0.005f) &&
                        Near(minimumV, 0.00011f, 0.005f) && Near(maximumV, 0.99989f, 0.005f),
                    "Center display maps only the native Cricket camera/radar atlas region", failures);
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
                Require(Near(minimumU, 0.80791f, 0.005f) && Near(maximumU, 0.97798f, 0.005f) &&
                        Near(minimumV, 0.02251f, 0.005f) && Near(maximumV, 0.34329f, 0.005f),
                    "Left display maps the native Cricket basic-flight instrument region", failures);
                Require(correlationYU > 0.95f && correlationXV > 0.95f,
                    "Left instrument display counter-rotates the clockwise atlas content by 90 degrees " +
                    "(y/u=" + correlationYU.ToString("0.000") + ", x/v=" + correlationXV.ToString("0.000") +
                    ", x/u=" + correlationXU.ToString("0.000") + ", y/v=" + correlationYV.ToString("0.000") + ")", failures);
            }
            else if (engine)
            {
                engineFound = true;
                Require(Near(minimumU, 0.81003f, 0.005f) && Near(maximumU, 0.97580f, 0.005f) &&
                        Near(minimumV, 0.38727f, 0.005f) && Near(maximumV, 0.69994f, 0.005f),
                    "Right display maps the native Cricket engine-instrument region", failures);
                Require(correlationYU > 0.95f && correlationXV > 0.95f,
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
        File.WriteAllLines(ReportPath, report);
        if (failures.Count > 0)
            throw new InvalidOperationException("F-117 runtime contract validation failed:\n" + string.Join("\n", failures));
        Debug.Log("F-117 runtime contract validation PASS. Report: " + ReportPath);
    }
}
