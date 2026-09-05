using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static F117AuthoringUtil;

public static class F117Builder
{
    private const string SourceRoot = "Assets/blueprinter/aryx/aryx_f16m";
    private const string SourcePrefabPath = SourceRoot + "/Aryx_F16M_KingViper.prefab";
    private const string SourceManifestPath = "Assets/blueprinter/generated/patch_manifest.json";
    private const string ModelPath = "Assets/F117/Models/F117_Production.fbx";
    private const string IconPath = "Assets/F117/UI/F117_Icon.png";
    private const string DamagePath = "Assets/F117/UI/F117_Damage.png";
    private const string TexturesRoot = "Assets/F117/Textures";
    private static readonly string[] FinishTexturePaths = Enumerable.Range(1, 7)
        .Select(index => TexturesRoot + "/f117_ext_" + index + "_ms.png")
        .Concat(new[] { TexturesRoot + "/F117_Mirror_MS.png" })
        .ToArray();
    private static readonly string[] ExteriorSourceTexturePaths = Enumerable.Range(1, 7)
        .SelectMany(index => new[]
        {
            TexturesRoot + "/f117_ext_" + index + "_albedo.png",
            TexturesRoot + "/f117_ext_" + index + "_normal.png",
            TexturesRoot + "/f117_ext_" + index + "_occlusion.png"
        })
        .ToArray();
    private const string GeneratedRoot = "Assets/F117/Generated";
    private const string MaterialsRoot = GeneratedRoot + "/Materials";
    private static readonly string[] RuntimeProfileTexturePaths = ExteriorSourceTexturePaths
        .Concat(FinishTexturePaths)
        .Concat(Enumerable.Range(1, 7).Select(index => MaterialsRoot +
            "/F117_F117_EXTERNAL_" + index + "_Damage.asset"))
        .Concat(F117AircraftAssembler.ParadeFlagFinishKeys.SelectMany(key => new[]
        {
            MaterialsRoot + "/F117_ParadeFlag_" + key + ".asset",
            MaterialsRoot + "/F117_ParadeFlag_" + key + "_Damage.asset"
        }))
        .ToArray();
    private const string PrefabPath = GeneratedRoot + "/F117A_Nighthawk.prefab";
    private const string DefinitionPath = GeneratedRoot + "/F117A_Nighthawk_Definition.asset";
    private const string ParametersPath = GeneratedRoot + "/F117A_Nighthawk_Parameters.asset";
    private const string LiveryPath = GeneratedRoot + "/F117A_Nighthawk_Livery.asset";
    private static readonly string[] ParadeLiveryPaths =
    {
        GeneratedRoot + "/F117A_ParadeFlag_SmokedChrome_Livery.asset",
        GeneratedRoot + "/F117A_ParadeFlag_MatteBlack_Livery.asset"
    };
    private static readonly string[] ParadeLiveryAssetNames =
    {
        "F117A_ParadeFlag_SmokedChrome_Livery",
        "F117A_ParadeFlag_MatteBlack_Livery"
    };
    private static readonly string[] ParadeLiveryDisplayNames =
    {
        "Farewell Flag - Smoked Chrome",
        "Farewell Flag - Matte Black"
    };
    private const string StatusPath = GeneratedRoot + "/F117A_Nighthawk_StatusDisplay.prefab";
    private const string RuntimeUiFallbackPath = GeneratedRoot + "/F117_RuntimeUI_Fallback.prefab";
    private const string ManifestPath = GeneratedRoot + "/patch_manifest.json";

    private const string AircraftKey = "blacknight2u_F117A_Nighthawk";
    private const string AircraftName = "F-117A Nighthawk";
    private const string BundleName = "blacknight2u.f117a.nighthawk.nobp";
    private const string Version = "0.4.100";
    private const string FixedJammerAsset = "JammingPod1";

    private sealed class WeaponLoadoutSpec
    {
        internal readonly string AssetName;
        internal readonly string DisplayName;
        internal readonly float FuelRatio;

        internal WeaponLoadoutSpec(string assetName, string displayName, float fuelRatio = 1f)
        {
            AssetName = assetName;
            DisplayName = displayName;
            FuelRatio = fuelRatio;
        }
    }

    private static readonly WeaponLoadoutSpec[] WeaponLoadouts =
    {
        new WeaponLoadoutSpec("bomb_125_internalx4", "8× PAB-125"),
        new WeaponLoadoutSpec("bomb_glide1_quad_internal", "8× PAB-80LR"),
        new WeaponLoadoutSpec("AGM1_quad_internal", "8× AGM-48"),
        new WeaponLoadoutSpec("bomb_250_internalx2", "4× PAB-250"),
        new WeaponLoadoutSpec("bomb_500_internal", "2× GPO-500"),
        new WeaponLoadoutSpec("bomb_penetrator1", "2× GPO-2P Auger", 0.96f),
        new WeaponLoadoutSpec("AGM_heavy_internal", "2× AGM-68"),
        new WeaponLoadoutSpec("ARM1_single", "2× ARAD-116"),
        new WeaponLoadoutSpec("AAM1_double_internal", "4× MMR-S3"),
        new WeaponLoadoutSpec("AAM2_double_internal", "4× AAM-29 Scythe"),
        new WeaponLoadoutSpec("CruiseMissile1_internal", "2× ALM-C450", fuelRatio: 0.98f),
        new WeaponLoadoutSpec("nuclearBomb1_internal", "2× GPO-N (1.5 kt)"),
        new WeaponLoadoutSpec("bomb_250_glide_internalx2", "4× PAB-250LR"),
        new WeaponLoadoutSpec("bomb_500_glide_internalx1", "2× GBM-500LR"),
        new WeaponLoadoutSpec("bomb_cluster1_single_internal", "2× CBO-400"),
        new WeaponLoadoutSpec("ARM2_single_internal", "2× ARAD-45"),
        new WeaponLoadoutSpec("AShM2_internal_single", "2× AGM-99"),
        new WeaponLoadoutSpec("AShM3_single", "2× Tusko-B"),
        new WeaponLoadoutSpec("nuclearBomb1_strategic_internal", "2× GPO-N (250 kt)")
    };

    internal static int WeaponOptionCount => WeaponLoadouts.Length + 1;
    internal static int WeaponLoadoutCount => WeaponLoadouts.Length;
    internal static IReadOnlyList<string> WeaponAssetNames =>
        WeaponLoadouts.Select(spec => spec.AssetName).ToArray();

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("F-117A Nighthawk/Build Blueprinter Bundle")]
    public static void Build()
    {
        EnsureAssets();
        ConfigureModel();
        ConfigureSprite(IconPath, 512);
        ConfigureSprite(DamagePath, 1024);
        ConfigureTextures();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        if (AssetDatabase.IsValidFolder(GeneratedRoot))
            AssetDatabase.DeleteAsset(GeneratedRoot);
        Directory.CreateDirectory(MaterialsRoot);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        GameObject runtimeUiFallback = CreateRuntimeUiFallback();
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        F117AircraftAssembler.Result assembled = F117AircraftAssembler.Assemble(
            source, model, MaterialsRoot, runtimeUiFallback);
        F117DamageAvatarGenerator.Generate(assembled.Instance);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(assembled.Instance, PrefabPath);
        UnityEngine.Object.DestroyImmediate(assembled.Instance);
        if (prefab == null)
            throw new InvalidOperationException("Unity failed to save the F-117 aircraft prefab.");

        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        UnityEngine.Object livery = CreateLivery(LiveryPath, "F117A_Nighthawk_Livery");
        UnityEngine.Object[] paradeLiveries = Enumerable.Range(0, ParadeLiveryPaths.Length)
            .Select(index => CreateLivery(ParadeLiveryPaths[index], ParadeLiveryAssetNames[index]))
            .ToArray();
        GameObject status = CreateStatusDisplay();
        UnityEngine.Object parameters = CreateParameters(status, livery, paradeLiveries, runtimeUiFallback);
        UnityEngine.Object definition = CreateDefinition(prefab, parameters, icon, assembled.VisualBounds);
        FinalizePrefab(definition);
        CreateManifest();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        F117ContractValidator.Validate();
        BuildBundle();
        Debug.Log("F-117A Nighthawk build complete. Bounds: " + assembled.VisualBounds);
    }

    private static void EnsureAssets()
    {
        string[] required = new[]
        {
            SourcePrefabPath, SourceManifestPath, ModelPath, IconPath, DamagePath, TexturesRoot
        }.Concat(ExteriorSourceTexturePaths).Concat(FinishTexturePaths).ToArray();
        string[] missing = required.Where(path => !File.Exists(path) && !Directory.Exists(path)).ToArray();
        if (missing.Length > 0)
            throw new FileNotFoundException("F-117 authoring assets are missing: " + string.Join(", ", missing));
    }

    private static void ConfigureModel()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new InvalidOperationException("The production F-117 FBX has no ModelImporter.");
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.isReadable = false;
        importer.SaveAndReimport();
    }

    private static void ConfigureSprite(string path, int maxSize)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException(path + " has no TextureImporter.");
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = maxSize;
        importer.SaveAndReimport();
    }

    private static void ConfigureTextures()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesRoot });
        if (textureGuids.Length < 80)
            throw new InvalidOperationException("The F-117 packed texture export is incomplete: " + textureGuids.Length + " textures found.");

        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException(path + " has no TextureImporter.");

            string stem = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool normal = stem.EndsWith("_normal", StringComparison.Ordinal) || stem.EndsWith("_norm", StringComparison.Ordinal);
            bool decal = stem.IndexOf("decal", StringComparison.Ordinal) >= 0;
            bool paradeFlag = stem.StartsWith("f117_paradeflag", StringComparison.Ordinal);
            bool aircraftFinish = stem.EndsWith("_ms", StringComparison.Ordinal);
            bool data = normal || aircraftFinish || stem.EndsWith("_comp", StringComparison.Ordinal) ||
                stem.EndsWith("_mask", StringComparison.Ordinal) || stem.EndsWith("_occlusion", StringComparison.Ordinal);
            importer.textureType = normal
                ? TextureImporterType.NormalMap
                : TextureImporterType.Default;
            importer.sRGBTexture = !data;
            importer.alphaIsTransparency = !data;
            if (paradeFlag || aircraftFinish)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.filterMode = aircraftFinish ? FilterMode.Bilinear : FilterMode.Trilinear;
                importer.anisoLevel = aircraftFinish ? 1 : 16;
            }
            // Tiny stencil lettering was being blurred/censored by alpha mipmaps and
            // block compression even though the source texture and UVs are correct.
            importer.mipmapEnabled = !decal;
            importer.maxTextureSize = paradeFlag ? 4096 : aircraftFinish ? 2048 : 1024;
            importer.npotScale = paradeFlag || aircraftFinish
                ? TextureImporterNPOTScale.None
                : TextureImporterNPOTScale.ToNearest;
            importer.textureCompression = decal || paradeFlag
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.CompressedHQ;
            if (aircraftFinish)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Repeat;
                importer.wrapModeW = TextureWrapMode.Repeat;
            }
            importer.SaveAndReimport();
        }
    }

    private static GameObject CreateStatusDisplay()
    {
        // UGUI MonoBehaviours authored in this stripped editor project lose their script
        // identity when loaded by the retail game. Keep only engine-native layout objects
        // in the bundle; the version-matched runtime plugin adds and wires the two Images
        // before StatusDisplay.Initialize executes.
        GameObject root = new GameObject("F117A_Nighthawk_StatusDisplay", typeof(RectTransform), typeof(CanvasRenderer));
        int uiLayer = LayerMask.NameToLayer("UI");
        root.layer = uiLayer >= 0 ? uiLayer : 5;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        // Match the retail HUD''s bottom-right StatusDisplay contract. A centered
        // pivot leaves half of a custom silhouette outside the screen edge.
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(1f, -0.05f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = new Vector2(260f, 260f);

        // PrefabUtility names the saved aircraft root after F117A_Nighthawk.prefab.
        // StatusDisplay matches damage parts by GameObject name, so the central entry
        // must use the persisted runtime root name rather than its authoring alias.
        GameObject part = new GameObject("F117A_Nighthawk", typeof(RectTransform), typeof(CanvasRenderer));
        part.layer = root.layer;
        part.transform.SetParent(root.transform, false);
        RectTransform partRect = part.GetComponent<RectTransform>();
        partRect.anchorMin = Vector2.zero;
        partRect.anchorMax = Vector2.one;
        partRect.pivot = new Vector2(0.5f, 0.5f);
        partRect.anchoredPosition = Vector2.zero;
        partRect.sizeDelta = Vector2.zero;

        Type statusType = FindType("StatusDisplay");
        if (statusType == null)
            throw new InvalidOperationException("The game StatusDisplay type is unavailable.");
        Component status = root.AddComponent(statusType);
        SerializedObject statusData = new SerializedObject(status);
        SerializedProperty displays = Require(statusData, "statusDisplays");
        displays.arraySize = 1;
        SerializedProperty display = displays.GetArrayElementAtIndex(0);
        Set(display, "partImage", null);
        Set(display, "redStatusThreshold", 35f);
        Set(statusData, "aircraftBackground", null);
        Size(statusData, "failureIndicators", 0);
        statusData.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StatusPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (saved == null)
            throw new InvalidOperationException("Unity did not save the clean F-117 status-display prefab.");
        AssetDatabase.ImportAsset(StatusPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StatusPath);
        if (prefab == null)
            throw new InvalidOperationException("Unity did not persist the F-117 status-display prefab.");
        return prefab;
    }

    private static UnityEngine.Object CreateLivery(string path, string assetName)
    {
        Type liveryType = FindType("LiveryData");
        if (!typeof(ScriptableObject).IsAssignableFrom(liveryType))
            throw new InvalidOperationException("LiveryData is not a ScriptableObject type.");
        ScriptableObject livery = ScriptableObject.CreateInstance(liveryType);
        livery.name = assetName;
        SerializedObject data = new SerializedObject(livery);
        Set(data, "Texture", null);
        Set(data, "Glossiness", 0f);
        Size(data, "Colors", 0);
        data.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(livery, path);
        return livery;
    }

    private static GameObject CreateRuntimeUiFallback()
    {
        // Custom UI prefabs copied out of another Blueprinter bundle lose their retail
        // MonoBehaviour identities when repacked. Keep a harmless engine-only object in
        // the serialized contract; the version-matched runtime plugin replaces it with
        // a verified native-game HUD and TacScreen before either can be instantiated.
        GameObject root = new GameObject("F117_RuntimeUI_Fallback", typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        root.layer = uiLayer >= 0 ? uiLayer : 5;
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        root.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, RuntimeUiFallbackPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null)
            throw new InvalidOperationException("Unity failed to create the runtime UI fallback prefab.");
        return prefab;
    }


    private static UnityEngine.Object CreateParameters(GameObject status, UnityEngine.Object livery,
        UnityEngine.Object[] paradeLiveries, GameObject runtimeUiFallback)
    {
        Type parametersType = FindType("AircraftParameters");
        ScriptableObject parameters = ScriptableObject.CreateInstance(parametersType);
        parameters.name = "F117A_Nighthawk_Parameters";
        SerializedObject data = new SerializedObject(parameters);
        SetString(data, "aircraftName", AircraftName);
        Set(data, "rankRequired", 4);
        ConfigureAirfoils(data);
        Set(data, "DefaultFuelLevel", 1f);
        Set(data, "StatusDisplay", status);
        Set(data, "HUDExtras", runtimeUiFallback);
        Set(data, "takeoffMusic", null);
        SerializedProperty aoa = Require(data, "AoAEffects");
        Set(aoa, "AudioClip", null);
        Set(aoa, "OnsetSpeed", 60f);
        Set(aoa, "FullVolumeSpeed", 190f);
        Set(aoa, "OnsetAlpha", 7f);
        Set(aoa, "FullVolumeAlpha", 28f);
        Set(aoa, "ShakeFactor", 0.08f);
        Set(data, "aircraftGLimit", 6f);
        Set(data, "PIDReferenceAirspeed", 145f);
        Set(data, "maxSpeed", 306f);
        Set(data, "takeoffSpeed", 78f);
        Set(data, "takeoffDistance", 950f);
        Set(data, "turningRadius", 1200f);
        Set(data, "cornerSpeed", 150f);
        Set(data, "approachSpeed", 76f);
        Set(data, "landingSpeed", 72f);
        Set(data, "shortLandingSpeed", 68f);
        Set(data, "cruiseThrottle", 0.8f);
        Set(data, "minimumRadarAlt", 60f);
        Set(data, "levelBias", 0f);
        Set(data, "verticalLanding", false);
        Set(data, "hoverTiltFactor", 0f);
        Set(data, "collectivePID", Vector3.zero);
        Set(data, "hoverPID", Vector3.zero);
        Set(data, "tiltPID", Vector3.zero);
        Set(data, "groundTurningRadius", 12f);

        SerializedProperty liveries = Require(data, "liveries");
        liveries.arraySize = 1 + paradeLiveries.Length;
        ConfigureLiveryEntry(liveries.GetArrayElementAtIndex(0), livery, LiveryPath, "Nighthawk Black");
        for (int index = 0; index < paradeLiveries.Length; index++)
            ConfigureLiveryEntry(liveries.GetArrayElementAtIndex(index + 1), paradeLiveries[index],
                ParadeLiveryPaths[index], ParadeLiveryDisplayNames[index]);

        SerializedProperty loadouts = Require(data, "loadouts");
        loadouts.arraySize = WeaponLoadouts.Length;
        for (int index = 0; index < loadouts.arraySize; index++)
            ConfigureLoadout(loadouts.GetArrayElementAtIndex(index));

        SerializedProperty standards = Require(data, "StandardLoadouts");
        standards.arraySize = WeaponLoadouts.Length;
        for (int index = 0; index < WeaponLoadouts.Length; index++)
        {
            SerializedProperty standard = standards.GetArrayElementAtIndex(index);
            Set(standard, "disabled", false);
            SetString(standard, "Name", WeaponLoadouts[index].DisplayName);
            Set(standard, "FuelRatio", WeaponLoadouts[index].FuelRatio);
            ConfigureLoadout(Require(standard, "loadout"));
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(parameters, ParametersPath);
        return parameters;
    }

    private static void ConfigureLiveryEntry(SerializedProperty liveryEntry, UnityEngine.Object livery,
        string expectedPath, string displayName)
    {
        SetString(liveryEntry, "name", displayName);
        Set(liveryEntry, "faction", null);
        SerializedProperty assetReference = Require(liveryEntry, "assetReference");
        string liveryPath = AssetDatabase.GetAssetPath(livery);
        if (!string.Equals(liveryPath, expectedPath, StringComparison.Ordinal))
            throw new InvalidOperationException("The generated livery asset is not at the audited bundle path.");
        string liveryGuid = AssetDatabase.AssetPathToGUID(liveryPath);
        if (string.IsNullOrEmpty(liveryGuid))
            throw new InvalidOperationException("The generated F-117 livery has no asset GUID.");
        Require(assetReference, "m_AssetGUID").stringValue = liveryGuid;
        Require(assetReference, "m_SubObjectName").stringValue = string.Empty;
        Require(assetReference, "m_SubObjectType").stringValue = string.Empty;
    }

    private static void ConfigureAirfoils(SerializedObject data)
    {
        SerializedProperty airfoils = Require(data, "airfoils");
        airfoils.arraySize = 2;
        ConfigureAirfoil(airfoils.GetArrayElementAtIndex(0), "F117_DeltaWing", 1f, 1f);
        ConfigureAirfoil(airfoils.GetArrayElementAtIndex(1), "F117_ControlSurface", 1.08f, 1.12f);
    }

    private static void ConfigureAirfoil(SerializedProperty airfoil, string name, float liftScale, float dragScale)
    {
        SetString(airfoil, "name", name);
        AnimationCurve lift = SmoothPeriodicCurve(new[]
        {
            new Keyframe(-Mathf.PI, 0f), new Keyframe(-2.62f, 0.38f * liftScale),
            new Keyframe(-Mathf.PI / 2f, 0f), new Keyframe(-0.785f, -0.82f * liftScale),
            new Keyframe(-0.35f, -1.02f * liftScale), new Keyframe(-0.1745f, -0.72f * liftScale),
            new Keyframe(0f, 0f), new Keyframe(0.1745f, 0.72f * liftScale),
            new Keyframe(0.35f, 1.02f * liftScale), new Keyframe(0.785f, 0.82f * liftScale),
            new Keyframe(Mathf.PI / 2f, 0f), new Keyframe(2.62f, -0.38f * liftScale),
            new Keyframe(Mathf.PI, 0f)
        });
        AnimationCurve drag = SmoothPeriodicCurve(new[]
        {
            new Keyframe(-Mathf.PI, 0.06f * dragScale), new Keyframe(-2.62f, 0.48f * dragScale),
            new Keyframe(-Mathf.PI / 2f, 1.25f * dragScale), new Keyframe(-0.785f, 0.72f * dragScale),
            new Keyframe(-0.35f, 0.18f * dragScale), new Keyframe(-0.1745f, 0.055f * dragScale),
            new Keyframe(0f, 0.028f * dragScale), new Keyframe(0.1745f, 0.055f * dragScale),
            new Keyframe(0.35f, 0.18f * dragScale), new Keyframe(0.785f, 0.72f * dragScale),
            new Keyframe(Mathf.PI / 2f, 1.25f * dragScale), new Keyframe(2.62f, 0.48f * dragScale),
            new Keyframe(Mathf.PI, 0.06f * dragScale)
        });
        SetCurve(airfoil, "liftCoef", lift);
        SetCurve(airfoil, "dragCoef", drag);
    }

    private static AnimationCurve SmoothPeriodicCurve(Keyframe[] keys)
    {
        AnimationCurve curve = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
        for (int index = 0; index < curve.length; index++)
            curve.SmoothTangents(index, 0f);
        return curve;
    }

    private static void ConfigureLoadout(SerializedProperty loadout)
    {
        SerializedProperty weapons = Require(loadout, "weapons");
        weapons.arraySize = 3;
        for (int index = 0; index < weapons.arraySize; index++)
            weapons.GetArrayElementAtIndex(index).objectReferenceValue = null;
    }
    private static UnityEngine.Object CreateDefinition(GameObject prefab, UnityEngine.Object parameters, Sprite icon, Bounds bounds)
    {
        Type definitionType = FindType("AircraftDefinition");
        ScriptableObject definition = ScriptableObject.CreateInstance(definitionType);
        definition.name = "F117A_Nighthawk_Definition";
        SerializedObject data = new SerializedObject(definition);
        SerializedProperty typeIdentity = Require(data, "typeIdentity");
        Set(typeIdentity, "surface", 0f);
        Set(typeIdentity, "air", 1f);
        Set(typeIdentity, "missile", 0f);
        Set(typeIdentity, "radar", 0f);
        Set(typeIdentity, "strategic", 0.2f);
        SerializedProperty roleIdentity = Require(data, "roleIdentity");
        Set(roleIdentity, "antiSurface", 1f);
        Set(roleIdentity, "antiAir", 0.35f);
        Set(roleIdentity, "antiMissile", 0f);
        Set(roleIdentity, "antiRadar", 1f);
        SetString(data, "jsonKey", AircraftKey);
        SetString(data, "unitName", AircraftName);
        SetString(data, "bogeyName", "Nighthawk");
        SetString(data, "code", "F-117A");
        SetString(data, "description", Description());
        Set(data, "visibleRange", 2500f);
        Set(data, "iconRange", 1f);
        Set(data, "radarSize", 0.0000005f);
        Set(data, "mapOrient", true);
        Set(data, "IsObstacle", true);
        Set(data, "iconSize", 1f);
        Set(data, "mapIconSize", 1.30f);
        Set(data, "captureCapacity", 0);
        Set(data, "captureStrength", 0f);
        Set(data, "captureDefense", 0f);
        Set(data, "length", 20.09f);
        Set(data, "width", 13.21f);
        Set(data, "height", 3.78f);
        Set(data, "value", 120f);
        Set(data, "mass", 13380f);
        Set(data, "manpower", 1f);
        Set(data, "armorTier", 0f);
        Set(data, "damageTolerance", 1f);
        Set(data, "CanSlingLoad", false);
        // The deployed tire contacts sit at Y=-1.89 m (nose) and -1.99 m (mains).
        // A 1.90 m spawn height gives the main suspension a small static preload
        // while keeping the fuselage more than 0.8 m above the runway.
        // Frame 81 is the source gear animation's real fully deployed endpoint.
        // Spawn with the two main tire planes just above the runway; the nose then
        // settles through the source-authored 2.3-degree ground attitude.
        Set(data, "spawnOffset", new Vector3(0f, F117AircraftAssembler.GroundSpawnHeight, 0f));
        Set(data, "disabled", false);
        Set(data, "isEventContent", false);
        Set(data, "dontAutomaticallyAddToEncyclopedia", false);
        Set(data, "minEditorHeight", 0f);
        Set(data, "maxEditorHeight", 10000f);
        Set(data, "unitPrefab", prefab);
        Set(data, "aircraftParameters", parameters);
        Set(data, "friendlyIcon", icon);
        Set(data, "hostileIcon", icon);
        Set(data, "mapIcon", icon);

        SerializedProperty info = Require(data, "aircraftInfo");
        Set(info, "emptyWeight", 13380f);
        Set(info, "maxSpeed", 1100f);
        Set(info, "stallSpeed", 285f);
        Set(info, "maneuverability", 5f);
        Set(info, "maxWeight", 23814f);
        Set(data, "restRotation", Vector3.zero);
        data.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(definition, DefinitionPath);
        return definition;
    }

    private static void FinalizePrefab(UnityEngine.Object definition)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Component aircraft = FindComponent(root, "Aircraft");
            if (aircraft == null)
                throw new InvalidOperationException("The generated F-117 prefab has no Aircraft component.");
            SerializedObject data = new SerializedObject(aircraft);
            Set(data, "definition", definition);
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static string Description()
    {
        return "A high-subsonic, extreme-low-observable strike aircraft with two non-afterburning F404 engines, " +
               "digital fly-by-wire control, electro-optical targeting, and two internal weapon bays. A broad set of compatible " +
               "bomb and missile loadouts is available for varied play. Closed internal stores remain radar-shielded; opening a " +
               "weapon bay or lowering the landing gear increases radar signature. A fixed active radar jammer is installed on every loadout: " +
               "select it as a weapon, designate a tracked radar target, and hold fire to jam. " +
               "Holding wheel brake during a valid landing " +
               "roll deploys the one-use drag chute after all three wheels settle and the aircraft is aligned with its travel.";
    }
    private static void CreateManifest()
    {
        PatchManifest source = JsonUtility.FromJson<PatchManifest>(File.ReadAllText(SourceManifestPath));
        PatchManifest output = new PatchManifest
        {
            modName = AircraftName,
            schemaVersion = 3,
            modVersion = Version,
            Patches = new List<AssetPatch>(),
            Ops = new List<Op>(),
            Addressables = new List<AddressableOverride>()
        };

        output.Addressables.Add(new AddressableOverride
        {
            guid = AssetDatabase.AssetPathToGUID(LiveryPath),
            subObjectName = string.Empty,
            subObjectType = string.Empty,
            BundleAsset = BundleAsset(LiveryPath, "F117A_Nighthawk_Livery", "LiveryData, Assembly-CSharp")
        });
        for (int index = 0; index < ParadeLiveryPaths.Length; index++)
        {
            output.Addressables.Add(new AddressableOverride
            {
                guid = AssetDatabase.AssetPathToGUID(ParadeLiveryPaths[index]),
                subObjectName = string.Empty,
                subObjectType = string.Empty,
                BundleAsset = BundleAsset(ParadeLiveryPaths[index], ParadeLiveryAssetNames[index],
                    "LiveryData, Assembly-CSharp")
            });
        }

        AddAuditedReferencePatches(output, source);
        AddHudIconAssetPatches(output, source);
        AddAircraftSkinShaderPatches(output, source);
        AddCountermeasureAssetPatches(output, source);
        AddLandingGearAudioAssetPatches(output, source);

        AddEngineAssetPatch(output, source, "revoker_turbine", "UnityEngine.AudioClip, UnityEngine.AudioModule",
            "UnityEngine.AudioSource, UnityEngine.AudioModule", 0, "clip");
        AddEngineAssetPatch(output, source, "jet_thrust", "UnityEngine.AudioClip, UnityEngine.AudioModule",
            "UnityEngine.AudioSource, UnityEngine.AudioModule", 1, "clip");
        AddEngineAssetPatch(output, source, "MasterAudioMixer", "UnityEngine.Audio.AudioMixer, UnityEngine.AudioModule",
            "UnityEngine.AudioSource, UnityEngine.AudioModule", 0, "outputAudioMixerGroup::Effects_General");
        AddEngineAssetPatch(output, source, "MasterAudioMixer", "UnityEngine.Audio.AudioMixer, UnityEngine.AudioModule",
            "UnityEngine.AudioSource, UnityEngine.AudioModule", 1, "outputAudioMixerGroup::Effects_General");
        AddEngineAssetPatch(output, source, "turbineFIre", "UnityEngine.GameObject, UnityEngine.CoreModule",
            "JetNozzle, Assembly-CSharp", 0, "failureEffect");

        for (int index = 0; index < WeaponLoadouts.Length; index++)
            AddWeaponPatch(output, WeaponLoadouts[index], index + 1, index);
        AddFixedJammerPatch(output);

        AssetRef definition = BundleAsset(DefinitionPath, "F117A_Nighthawk_Definition", "AircraftDefinition, Assembly-CSharp");
        output.Ops.Add(new Op
        {
            opId = "OpAddToHangar",
            payloadJson = JsonUtility.ToJson(new OpAddToHangarPayload
            {
                BundleAsset = definition,
                Hangars = new[]
                {
                    "revetment1__revetment1", "shelter1__shelter1", "hangar_med__hangar_med",
                    "fleetCarrier1__hangar_R1", "fleetCarrier1__hangar_R2", "fleetCarrier1__hangar_R3",
                    "AssaultCarrier1__hangar_R1", "AssaultCarrier1__hangar_R2", "AssaultCarrier1__hangar_R3"
                }
            })
        });
        output.Ops.Add(new Op
        {
            opId = "OpAddToEncyclopedia",
            payloadJson = JsonUtility.ToJson(new OpAddToEncyclopediaPayload { entries = new[] { definition } })
        });

        File.WriteAllText(ManifestPath, JsonUtility.ToJson(output, true));
        AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void AddAuditedReferencePatches(PatchManifest output, PatchManifest source)
    {
        AddReferenceAssetPatch(output, source, "runway scrape",
            "UnityEngine.AudioClip, UnityEngine.AudioModule",
            AircraftLocation(string.Empty, "Aircraft, Assembly-CSharp", 0, "scrapeSound"));
        AddReferenceAssetPatch(output, source, "fuelLeak",
            "UnityEngine.GameObject, UnityEngine.CoreModule",
            AircraftLocation(string.Empty, "FuelTank, Assembly-CSharp", 0, "leakEffect"));
        AddReferenceAssetPatch(output, source, "fire_med",
            "UnityEngine.GameObject, UnityEngine.CoreModule",
            AircraftLocation(string.Empty, "FuelTank, Assembly-CSharp", 0, "fireEffect"));
        AddReferenceAssetPatch(output, source, "fireball_large",
            "UnityEngine.GameObject, UnityEngine.CoreModule",
            AircraftLocation(string.Empty, "FuelTank, Assembly-CSharp", 0, "fireball"));
        AddReferenceAssetPatch(output, source, "capacitor",
            "UnityEngine.AudioClip, UnityEngine.AudioModule",
            AircraftLocation("F117_Electrical", "UnityEngine.AudioSource, UnityEngine.AudioModule", 0, "clip"));
        AddReferenceAssetPatch(output, source, "MasterAudioMixer",
            "UnityEngine.Audio.AudioMixer, UnityEngine.AudioModule",
            AircraftLocation("F117_Electrical", "UnityEngine.AudioSource, UnityEngine.AudioModule", 0,
                "outputAudioMixerGroup::Effects_General"));
        AddReferenceAssetPatch(output, source, "ejectionSeat",
            "UnityEngine.AudioClip, UnityEngine.AudioModule",
            AircraftLocation("F117_Avionics/F117_CanopySystems/Canopy", "Canopy, Assembly-CSharp", 0, "ejectSound"));
        AddReferenceAssetPatch(output, source, "ejectionSeat",
            "UnityEngine.Mesh, UnityEngine.CoreModule",
            AircraftLocation("F117_Avionics/Cockpit/pilot/EjectionSeat",
                "UnityEngine.MeshFilter, UnityEngine.CoreModule", 0, "sharedMesh"));

        const string pilotModel = "F117_Avionics/Cockpit/pilot/pilot/pilot";
        const string pilotAnimator = "F117_Avionics/Cockpit/pilot/pilot";
        AddReferenceAssetPatch(output, source, "pilot",
            "UnityEngine.Mesh, UnityEngine.CoreModule",
            AircraftLocation(pilotModel, "UnityEngine.SkinnedMeshRenderer, UnityEngine.CoreModule", 0, "sharedMesh"));
        AddReferenceAssetPatch(output, source, "pilot",
            "UnityEngine.Material, UnityEngine.CoreModule",
            AircraftLocation(pilotModel, "UnityEngine.SkinnedMeshRenderer, UnityEngine.CoreModule", 0, "sharedMaterials[0]"));
        AddReferenceAssetPatch(output, source, "pilotAvatar",
            "UnityEngine.Avatar, UnityEngine.AnimationModule",
            AircraftLocation(pilotAnimator, "UnityEngine.Animator, UnityEngine.AnimationModule", 0, "avatar"));
        AddReferenceAssetPatch(output, source, "PilotController",
            "UnityEngine.RuntimeAnimatorController, UnityEngine.AnimationModule",
            AircraftLocation(pilotAnimator, "UnityEngine.Animator, UnityEngine.AnimationModule", 0, "runtimeAnimatorController"));
    }

    private static void AddCountermeasureAssetPatches(PatchManifest output, PatchManifest source)
    {
        LocationRef flare = AircraftLocation(string.Empty, "FlareEjector, Assembly-CSharp", 0, "displayImage");
        AddReferenceAssetPatch(output, source, "weaponicon_flares",
            "UnityEngine.Sprite, UnityEngine.CoreModule", flare);
        AddReferenceAssetPatch(output, source, "IRFlare",
            "UnityEngine.GameObject, UnityEngine.CoreModule",
            AircraftLocation(string.Empty, "FlareEjector, Assembly-CSharp", 0, "flarePrefab"));
        AddReferenceAssetPatch(output, source, "flare1",
            "UnityEngine.AudioClip, UnityEngine.AudioModule",
            AircraftLocation(string.Empty, "FlareEjector, Assembly-CSharp", 0, "ejectionSound"),
            AircraftLocation(string.Empty, "ChaffEjector, Assembly-CSharp", 0, "ejectionSound"));
        AddReferenceAssetPatch(output, source, "weaponicon_radarJammer",
            "UnityEngine.Sprite, UnityEngine.CoreModule",
            AircraftLocation(string.Empty, "ChaffEjector, Assembly-CSharp", 0, "displayImage"));
    }

    private static void AddLandingGearAudioAssetPatches(PatchManifest output, PatchManifest source)
    {
        string[] sides = { "Nose", "Left", "Right" };
        LocationRef[] gears = sides.Select(side => AircraftLocation(
            LandingGearPath(side), "LandingGear, Assembly-CSharp", 0, "foldSound")).ToArray();
        AddReferenceAssetPatch(output, source, "gearfold", "UnityEngine.AudioClip, UnityEngine.AudioModule", gears);

        gears = sides.Select(side => AircraftLocation(
            LandingGearPath(side), "LandingGear, Assembly-CSharp", 0, "latchSound")).ToArray();
        AddReferenceAssetPatch(output, source, "latch1", "UnityEngine.AudioClip, UnityEngine.AudioModule", gears);

        LocationRef[] wheelMixers = sides.SelectMany(side => new[]
        {
            AircraftLocation(WheelProxyPath(side), "UnityEngine.AudioSource, UnityEngine.AudioModule", 0,
                "outputAudioMixerGroup::Effects_General"),
            AircraftLocation(WheelProxyPath(side), "UnityEngine.AudioSource, UnityEngine.AudioModule", 1,
                "outputAudioMixerGroup::Effects_General")
        }).ToArray();
        AppendReferenceAssetPatch(output, source, "MasterAudioMixer",
            "UnityEngine.Audio.AudioMixer, UnityEngine.AudioModule", wheelMixers);
    }

    private static string LandingGearPath(string side)
    {
        return "F117_Visual/F117_Gear_" + side + "_Hinge_Axis/F117_Gear_" + side +
               "_Hinge/F117_Gear_" + side + "_Sprung";
    }

    private static string WheelProxyPath(string side)
    {
        return LandingGearPath(side) + "/F117_Gear_" + side + "_Unsprung/Axle/WheelProxy";
    }

    private static void AddHudIconAssetPatches(PatchManifest output, PatchManifest source)
    {
        AddReferenceAssetPatch(output, source, "hudIcon_aircraft",
            "UnityEngine.Sprite, UnityEngine.CoreModule",
            DefinitionLocation("friendlyIcon"),
            DefinitionLocation("hostileIcon"));
    }

    private static void AddAircraftSkinShaderPatches(PatchManifest output, PatchManifest source)
    {
        LocationRef[] locations = FindAircraftSkinMaterialPaths()
            .Select(path =>
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                return new LocationRef
                {
                    id = material.name + "/shader",
                    asset = BundleAsset(path, material.name, "UnityEngine.Material, UnityEngine.CoreModule"),
                    hierarchyPath = string.Empty,
                    componentType = string.Empty,
                    componentIndex = 0,
                    memberPath = "shader"
                };
            })
            .ToArray();
        if (locations.Length == 0)
            throw new InvalidOperationException("No generated F-117 AircraftSkin materials were found.");
        AddReferenceAssetPatch(output, source, "Shader Graphs/AircraftSkin",
            "UnityEngine.Shader, UnityEngine.CoreModule", locations);
    }

    private static string[] FindAircraftSkinMaterialPaths()
    {
        return AssetDatabase.FindAssets("t:Material", new[] { MaterialsRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                return material != null &&
                    (F117AircraftAssembler.UsesAircraftSkin(material.name) ||
                     string.Equals(material.name, F117AircraftAssembler.ParadeFlagMaterialName,
                         StringComparison.Ordinal));
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }


    private static void AddReferenceAssetPatch(PatchManifest output, PatchManifest source, string gameAssetName,
        string gameAssetType, params LocationRef[] locations)
    {
        AssetPatch sourcePatch = (source.Patches ?? new List<AssetPatch>())
            .FirstOrDefault(patch => patch.GameAsset?.asset?.name == gameAssetName &&
                patch.GameAsset.asset.type == gameAssetType);
        if (sourcePatch == null)
            throw new InvalidOperationException("Reference manifest is missing audited asset '" + gameAssetName +
                "' with exact type '" + gameAssetType + "'.");
        foreach (LocationRef location in locations)
            if (!ReferenceLocationExists(location))
                throw new InvalidOperationException("Audited patch location no longer exists: " +
                    location.hierarchyPath + " " + location.componentType + " " + location.memberPath);
        output.Patches.Add(new AssetPatch
        {
            GameAsset = sourcePatch.GameAsset,
            PatchLocations = locations.ToList()
        });
    }

    private static void AppendReferenceAssetPatch(PatchManifest output, PatchManifest source, string gameAssetName,
        string gameAssetType, params LocationRef[] locations)
    {
        AssetPatch sourcePatch = (source.Patches ?? new List<AssetPatch>())
            .FirstOrDefault(patch => patch.GameAsset?.asset?.name == gameAssetName &&
                patch.GameAsset.asset.type == gameAssetType);
        if (sourcePatch == null)
            throw new InvalidOperationException("Reference manifest is missing audited asset '" + gameAssetName +
                "' with exact type '" + gameAssetType + "'.");
        foreach (LocationRef location in locations)
            if (!ReferenceLocationExists(location))
                throw new InvalidOperationException("Audited patch location no longer exists: " +
                    location.hierarchyPath + " " + location.componentType + " " + location.memberPath);

        AssetPatch target = output.Patches.FirstOrDefault(patch => patch.GameAsset?.asset?.name == gameAssetName &&
            patch.GameAsset.asset.type == gameAssetType);
        if (target == null)
        {
            target = new AssetPatch { GameAsset = sourcePatch.GameAsset, PatchLocations = new List<LocationRef>() };
            output.Patches.Add(target);
        }
        target.PatchLocations.AddRange(locations);
    }

    private static LocationRef AircraftLocation(string hierarchyPath, string componentType, int componentIndex, string memberPath)
    {
        return new LocationRef
        {
            id = "F117A_Nighthawk/" + hierarchyPath + "/" + componentType + "#" + componentIndex,
            asset = BundleAsset(PrefabPath, "F117A_Nighthawk", "UnityEngine.GameObject, UnityEngine.CoreModule"),
            hierarchyPath = hierarchyPath,
            componentType = componentType,
            componentIndex = componentIndex,
            memberPath = memberPath
        };
    }

    private static bool ReferenceLocationExists(LocationRef location)
    {
        if (location?.asset == null || string.IsNullOrEmpty(location.asset.locator))
            return false;
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(location.asset.locator);
        if (asset == null)
            return false;
        if (string.IsNullOrEmpty(location.componentType))
            return true;

        string typeName = location.componentType.Split(',')[0].Trim();
        if (asset is GameObject root)
        {
            Transform target = string.IsNullOrEmpty(location.hierarchyPath)
                ? root.transform
                : root.transform.Find(location.hierarchyPath);
            if (target == null)
                return false;
            Component[] matches = target.GetComponents<Component>()
                .Where(component => component != null && component.GetType().FullName == typeName)
                .ToArray();
            if (location.componentIndex < 0 || location.componentIndex >= matches.Length)
                return false;
            return MemberExists(matches[location.componentIndex].GetType(), location.memberPath);
        }
        return asset.GetType().FullName == typeName && MemberExists(asset.GetType(), location.memberPath);
    }

    private static bool MemberExists(Type type, string memberPath)
    {
        if (string.IsNullOrEmpty(memberPath))
            return true;
        string root = memberPath.Split(new[] { '.', '[', ':' }, 2)[0];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return type.GetField(root, flags) != null || type.GetProperty(root, flags) != null;
    }

    private static void AddWeaponPatch(PatchManifest manifest, WeaponLoadoutSpec spec,
        int optionIndex, int loadoutIndex)
    {
        string weaponName = spec.AssetName;
        var locations = new List<LocationRef>
        {
            PrefabLocation("hardpointSets[0].weaponOptions[" + optionIndex + "]"),
            PrefabLocation("hardpointSets[1].weaponOptions[" + optionIndex + "]"),
            ParametersLocation("loadouts[" + loadoutIndex + "].weapons[0]"),
            ParametersLocation("loadouts[" + loadoutIndex + "].weapons[1]"),
            ParametersLocation("StandardLoadouts[" + loadoutIndex + "].loadout.weapons[0]"),
            ParametersLocation("StandardLoadouts[" + loadoutIndex + "].loadout.weapons[1]")
        };
        manifest.Patches.Add(new AssetPatch
        {
            GameAsset = new LocationRef
            {
                id = weaponName + "|WeaponMount, Assembly-CSharp",
                asset = new AssetRef { locator = weaponName, name = weaponName, type = "WeaponMount, Assembly-CSharp" },
                hierarchyPath = string.Empty,
                componentType = string.Empty,
                memberPath = string.Empty
            },
            PatchLocations = locations
        });
    }

    private static void AddFixedJammerPatch(PatchManifest manifest)
    {
        var locations = new List<LocationRef>
        {
            PrefabLocation("hardpointSets[2].weaponOptions[0]")
        };
        for (int index = 0; index < WeaponLoadoutCount; index++)
        {
            locations.Add(ParametersLocation("loadouts[" + index + "].weapons[2]"));
            locations.Add(ParametersLocation("StandardLoadouts[" + index + "].loadout.weapons[2]"));
        }
        manifest.Patches.Add(new AssetPatch
        {
            GameAsset = new LocationRef
            {
                id = FixedJammerAsset + "|WeaponMount, Assembly-CSharp",
                asset = new AssetRef
                {
                    locator = FixedJammerAsset,
                    name = FixedJammerAsset,
                    type = "WeaponMount, Assembly-CSharp"
                },
                hierarchyPath = string.Empty,
                componentType = string.Empty,
                memberPath = string.Empty
            },
            PatchLocations = locations
        });
    }

    private static void AddEngineAssetPatch(PatchManifest manifest, PatchManifest source, string gameAssetName,
        string gameAssetType, string componentType, int componentIndex, string memberPath)
    {
        AssetPatch sourcePatch = (source.Patches ?? new List<AssetPatch>())
            .FirstOrDefault(patch => patch.GameAsset?.asset?.name == gameAssetName &&
                patch.GameAsset.asset.type == gameAssetType);
        if (sourcePatch == null)
            throw new InvalidOperationException("Reference manifest is missing engine asset '" + gameAssetName +
                "' with exact type '" + gameAssetType + "'.");
        AssetPatch target = manifest.Patches.FirstOrDefault(patch => patch.GameAsset?.asset?.name == gameAssetName &&
            patch.GameAsset.asset.type == gameAssetType);
        if (target == null)
        {
            target = new AssetPatch { GameAsset = sourcePatch.GameAsset, PatchLocations = new List<LocationRef>() };
            manifest.Patches.Add(target);
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            target.PatchLocations.Add(new LocationRef
            {
                id = "F117A_Nighthawk/F117_Engine_" + side,
                asset = BundleAsset(PrefabPath, "F117A_Nighthawk", "UnityEngine.GameObject, UnityEngine.CoreModule"),
                hierarchyPath = "F117_Engine_" + side,
                componentType = componentType,
                componentIndex = componentIndex,
                memberPath = memberPath
            });
        }
    }

    private static LocationRef PrefabLocation(string memberPath)
    {
        return new LocationRef
        {
            id = "F117A_Nighthawk/F117_Avionics/WeaponManager#0",
            asset = BundleAsset(PrefabPath, "F117A_Nighthawk", "UnityEngine.GameObject, UnityEngine.CoreModule"),
            hierarchyPath = "F117_Avionics",
            componentType = "WeaponManager, Assembly-CSharp",
            componentIndex = 0,
            memberPath = memberPath
        };
    }

    private static LocationRef ParametersLocation(string memberPath)
    {
        return new LocationRef
        {
            id = "F117A_Nighthawk_Parameters",
            asset = BundleAsset(ParametersPath, "F117A_Nighthawk_Parameters", "AircraftParameters, Assembly-CSharp"),
            hierarchyPath = string.Empty,
            componentType = string.Empty,
            componentIndex = 0,
            memberPath = memberPath
        };
    }

    private static LocationRef DefinitionLocation(string memberPath)
    {
        return new LocationRef
        {
            id = "F117A_Nighthawk_Definition",
            asset = BundleAsset(DefinitionPath, "F117A_Nighthawk_Definition",
                "AircraftDefinition, Assembly-CSharp"),
            hierarchyPath = string.Empty,
            componentType = string.Empty,
            componentIndex = 0,
            memberPath = memberPath
        };
    }

    private static AssetRef BundleAsset(string path, string name, string type)
    {
        return new AssetRef { locator = path, name = name, type = type };
    }

    private static void BuildBundle()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/Blueprinter"));
        Directory.CreateDirectory(output);
        string[] shaderPatchMaterials = FindAircraftSkinMaterialPaths();
        if (shaderPatchMaterials.Length == 0)
            throw new InvalidOperationException("No F-117 AircraftSkin materials were available for bundle export.");
        string[] bundleAssetNames = new[]
        {
            PrefabPath, DefinitionPath, ParametersPath, LiveryPath,
            StatusPath, ManifestPath,
            IconPath, DamagePath
        }
            .Concat(ParadeLiveryPaths)
            .Concat(shaderPatchMaterials)
            .Concat(RuntimeProfileTexturePaths)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = bundleAssetNames
        };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
        string bundlePath = Path.Combine(output, BundleName);
        if (manifest == null || !File.Exists(bundlePath))
            throw new InvalidOperationException("Unity did not produce the F-117 .nobp bundle.");
        AssetBundle probe = AssetBundle.LoadFromFile(bundlePath);
        if (probe == null)
            throw new InvalidOperationException("Unity could not reopen the completed F-117 bundle.");
        var exportedNames = new HashSet<string>(probe.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
        string[] missingPatchMaterials = shaderPatchMaterials
            .Where(path => !exportedNames.Contains(path))
            .ToArray();
        string[] unloadablePatchMaterials = shaderPatchMaterials
            .Where(path => probe.LoadAsset<Material>(path) == null)
            .ToArray();
        string[] missingProfileTextures = RuntimeProfileTexturePaths
            .Where(path => !exportedNames.Contains(path))
            .ToArray();
        string[] unloadableProfileTextures = RuntimeProfileTexturePaths
            .Where(path => probe.LoadAsset<Texture2D>(path) == null)
            .ToArray();
        probe.Unload(true);
        if (missingPatchMaterials.Length > 0)
            throw new InvalidOperationException("AircraftSkin patch materials are missing from the bundle: " +
                string.Join(", ", missingPatchMaterials));
        if (unloadablePatchMaterials.Length > 0)
            throw new InvalidOperationException("AircraftSkin patch materials are present but cannot be loaded: " +
                string.Join(", ", unloadablePatchMaterials));
        if (missingProfileTextures.Length > 0 || unloadableProfileTextures.Length > 0)
            throw new InvalidOperationException("Exact bundled AircraftSkin texture contract failed. Missing: " +
                string.Join(", ", missingProfileTextures) + "; unloadable: " +
                string.Join(", ", unloadableProfileTextures));
        int coreReferences = ReplaceAscii(bundlePath, "BroomGameCoreXX", "Assembly-CSharp");
        ReplaceAscii(bundlePath, "BroomGameFirstpassRuntime", "Assembly-CSharp-firstpass");
        byte[] normalized = File.ReadAllBytes(bundlePath);
        if (coreReferences == 0 || ContainsAscii(normalized, "BroomGameCoreXX") ||
            ContainsAscii(normalized, "BroomGameFirstpassRuntime") || !ContainsAscii(normalized, "Assembly-CSharp"))
            throw new InvalidOperationException("The F-117 bundle contains unnormalized or missing game-script assembly references.");
        Debug.Log("Built F-117 Blueprinter bundle: " + bundlePath);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var receipt = new BundleValidationReceipt
            {
                version = Version,
                sha256 = BitConverter.ToString(sha.ComputeHash(normalized)).Replace("-", "").ToLowerInvariant(),
                result = "PASS"
            };
            File.WriteAllText(bundlePath + ".validation.json", JsonUtility.ToJson(receipt, true));
        }
    }

    [Serializable]
    private sealed class BundleValidationReceipt
    {
        public string version;
        public string sha256;
        public string result;
    }

    private static int ReplaceAscii(string path, string from, string to)
    {
        if (from.Length != to.Length)
            throw new InvalidOperationException("Asset-bundle assembly aliases must have equal lengths.");
        byte[] bytes = File.ReadAllBytes(path);
        byte[] source = System.Text.Encoding.ASCII.GetBytes(from);
        byte[] replacement = System.Text.Encoding.ASCII.GetBytes(to);
        int replacements = 0;
        for (int index = 0; index <= bytes.Length - source.Length; index++)
        {
            bool match = true;
            for (int offset = 0; offset < source.Length; offset++)
                if (bytes[index + offset] != source[offset]) { match = false; break; }
            if (!match)
                continue;
            Buffer.BlockCopy(replacement, 0, bytes, index, replacement.Length);
            replacements++;
            index += source.Length - 1;
        }
        if (replacements > 0)
            File.WriteAllBytes(path, bytes);
        Debug.Log("Normalized " + replacements + " '" + from + "' references.");
        return replacements;
    }

    private static bool ContainsAscii(byte[] bytes, string value)
    {
        byte[] target = System.Text.Encoding.ASCII.GetBytes(value);
        for (int index = 0; index <= bytes.Length - target.Length; index++)
        {
            bool match = true;
            for (int offset = 0; offset < target.Length; offset++)
                if (bytes[index + offset] != target[offset]) { match = false; break; }
            if (match)
                return true;
        }
        return false;
    }
    [Serializable]
    private sealed class PatchManifest
    {
        public string modName;
        public int schemaVersion = 3;
        public string modVersion;
        public List<AssetPatch> Patches = new List<AssetPatch>();
        public List<Op> Ops = new List<Op>();
        public List<AddressableOverride> Addressables = new List<AddressableOverride>();
    }

    [Serializable]
    private sealed class AssetPatch
    {
        public LocationRef GameAsset;
        public List<LocationRef> PatchLocations = new List<LocationRef>();
    }

    [Serializable]
    private sealed class LocationRef
    {
        public string id;
        public AssetRef asset;
        public string hierarchyPath;
        public string componentType;
        public int componentIndex;
        public string memberPath;
    }

    [Serializable]
    private sealed class AssetRef
    {
        public string locator;
        public string name;
        public string type;
    }

    [Serializable]
    private sealed class Op
    {
        public string opId;
        public string payloadJson;
    }

    [Serializable]
    private sealed class AddressableOverride
    {
        public string guid;
        public string subObjectName;
        public string subObjectType;
        public AssetRef BundleAsset;
    }

    [Serializable]
    private sealed class OpAddToHangarPayload
    {
        public AssetRef BundleAsset;
        public string[] Hangars;
    }

    [Serializable]
    private sealed class OpAddToEncyclopediaPayload
    {
        public AssetRef[] entries;
    }
}
