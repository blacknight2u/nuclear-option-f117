using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Blacknight2u.F117Nighthawk
{
    internal static class PresentationAssets
    {
        private const string DamageResource = "Blacknight2u.F117Nighthawk.F117_Damage.png";
        private const int SectionTextureSize = 256;
        private static readonly string[] DamagePartNames =
        {
            "F117_CentralBody", "F117_Nose", "F117_RearBody",
            "F117_Wing_Left_Root", "F117_Wing_Left_Inner", "F117_Wing_Left_Outer",
            "F117_Wing_Right_Root", "F117_Wing_Right_Inner", "F117_Wing_Right_Outer",
            "F117_Elevon_L_Inner", "F117_Elevon_L_Outer",
            "F117_Elevon_R_Inner", "F117_Elevon_R_Outer",
            "F117_Rudder_L", "F117_Rudder_R",
            "F117_Engine_Left", "F117_Engine_Right"
        };
        private static readonly FieldInfo DamageMaterialField =
            AccessTools.Field(typeof(UnitPart), "damageMaterial");

        private sealed class DamageSection
        {
            internal string Name;
            internal UnitPart Part;
            internal Vector2 Center;
            internal Vector2 Min;
            internal Vector2 Max;
            internal bool HasGeometry;

            internal float Area => Mathf.Max((Max.x - Min.x) * (Max.y - Min.y), 0.01f);
        }

        internal static Sprite DamageSilhouette { get; private set; }
        internal static IReadOnlyDictionary<string, Sprite> DamageSections { get; private set; }
        private static Texture2D damageTexture;

        internal static void Initialize()
        {
            if (DamageSilhouette != null)
                return;

            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(DamageResource))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded UI resource: " + DamageResource);

                byte[] data = new byte[stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int count = stream.Read(data, offset, data.Length - offset);
                    if (count <= 0)
                        throw new EndOfStreamException("Could not fully read " + DamageResource);
                    offset += count;
                }

                damageTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    name = "F117_Damage_Texture",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(damageTexture, data, false))
                    throw new InvalidOperationException("Unity could not decode " + DamageResource);
                UnityEngine.Object.DontDestroyOnLoad(damageTexture);

                DamageSilhouette = Sprite.Create(
                    damageTexture,
                    new Rect(0f, 0f, damageTexture.width, damageTexture.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0u,
                    SpriteMeshType.FullRect);
                DamageSilhouette.name = "F117_DamageSilhouette";
                UnityEngine.Object.DontDestroyOnLoad(DamageSilhouette);
            }
        }

        internal static void EnsureDamageSections(Aircraft aircraft)
        {
            if (DamageSections != null)
                return;
            if (aircraft == null || damageTexture == null)
                throw new InvalidOperationException("The F-117 damage display cannot build without its aircraft and silhouette.");

            Dictionary<string, UnitPart> parts = aircraft.partLookup
                .Where(part => part != null && DamagePartNames.Contains(part.gameObject.name))
                .GroupBy(part => part.gameObject.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            string[] missing = DamagePartNames.Where(name => !parts.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("The F-117 damage display cannot find: " + string.Join(", ", missing));

            Transform root = aircraft.transform;
            DamageSection[] sections = DamagePartNames.Select(name => CreateSection(parts[name], root)).ToArray();
            Vector2 aircraftMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 aircraftMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (DamageSection section in sections.Where(section => section.HasGeometry))
            {
                aircraftMin = Vector2.Min(aircraftMin, section.Min);
                aircraftMax = Vector2.Max(aircraftMax, section.Max);
            }
            if (!IsFinite(aircraftMin.x) || !IsFinite(aircraftMax.x) ||
                aircraftMax.x - aircraftMin.x < 1f || aircraftMax.y - aircraftMin.y < 1f)
                throw new InvalidOperationException("The F-117 damage display could not derive a valid planform.");

            // The engines are internal and intentionally own no exterior renderer. Give
            // each a compact planform zone at its real transform so engine damage remains
            // independently visible without stealing an entire rear-body section.
            foreach (DamageSection engine in sections.Where(section => !section.HasGeometry))
            {
                engine.Min = engine.Center - new Vector2(0.65f, 0.8f);
                engine.Max = engine.Center + new Vector2(0.65f, 0.8f);
            }

            Color32[] sourcePixels = damageTexture.GetPixels32();
            FindOpaqueBounds(sourcePixels, damageTexture.width, damageTexture.height,
                out int alphaMinX, out int alphaMinY, out int alphaMaxX, out int alphaMaxY);
            var pixelsBySection = sections.ToDictionary(section => section,
                section => new Color32[SectionTextureSize * SectionTextureSize]);
            for (int y = 0; y < SectionTextureSize; y++)
            {
                int sourceY = Mathf.Clamp((y * damageTexture.height + SectionTextureSize / 2) /
                    SectionTextureSize, 0, damageTexture.height - 1);
                for (int x = 0; x < SectionTextureSize; x++)
                {
                    int sourceX = Mathf.Clamp((x * damageTexture.width + SectionTextureSize / 2) /
                        SectionTextureSize, 0, damageTexture.width - 1);
                    Color32 source = sourcePixels[sourceY * damageTexture.width + sourceX];
                    if (source.a == 0)
                        continue;

                    float across = Mathf.InverseLerp(alphaMinX, alphaMaxX, sourceX);
                    float foreAft = Mathf.InverseLerp(alphaMinY, alphaMaxY, sourceY);
                    Vector2 planformPoint = new Vector2(
                        Mathf.Lerp(aircraftMin.x, aircraftMax.x, across),
                        Mathf.Lerp(aircraftMax.y, aircraftMin.y, foreAft));
                    DamageSection owner = null;
                    float bestScore = float.PositiveInfinity;
                    foreach (DamageSection section in sections)
                    {
                        float score = SectionScore(section, planformPoint);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            owner = section;
                        }
                    }
                    pixelsBySection[owner][y * SectionTextureSize + x] = source;
                }
            }

            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (DamageSection section in sections)
            {
                Texture2D texture = new Texture2D(SectionTextureSize, SectionTextureSize,
                    TextureFormat.RGBA32, false, true)
                {
                    name = section.Name + "_DamageMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels32(pixelsBySection[section]);
                texture.Apply(false, true);
                UnityEngine.Object.DontDestroyOnLoad(texture);
                Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, SectionTextureSize, SectionTextureSize),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
                sprite.name = section.Name + "_DamageSection";
                UnityEngine.Object.DontDestroyOnLoad(sprite);
                sprites.Add(section.Name, sprite);
            }
            DamageSections = sprites;
        }

        private static DamageSection CreateSection(UnitPart part, Transform aircraftRoot)
        {
            Vector3 localCenter = aircraftRoot.InverseTransformPoint(part.transform.position);
            var section = new DamageSection
            {
                Name = part.gameObject.name,
                Part = part,
                Center = new Vector2(localCenter.x, localCenter.z),
                Min = new Vector2(float.PositiveInfinity, float.PositiveInfinity),
                Max = new Vector2(float.NegativeInfinity, float.NegativeInfinity)
            };
            foreach (Renderer renderer in DamageRenderers(part))
                EncapsulateRenderer(section, renderer, aircraftRoot);
            section.HasGeometry = IsFinite(section.Min.x) && IsFinite(section.Max.x);
            return section;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static IEnumerable<Renderer> DamageRenderers(UnitPart part)
        {
            object damageMaterial = DamageMaterialField?.GetValue(part);
            if (damageMaterial == null)
                yield break;
            FieldInfo renderersField = AccessTools.Field(damageMaterial.GetType(), "renderers");
            if (!(renderersField?.GetValue(damageMaterial) is IEnumerable renderers))
                yield break;
            foreach (object value in renderers)
                if (value is Renderer renderer && renderer != null)
                    yield return renderer;
        }

        private static void EncapsulateRenderer(DamageSection section, Renderer renderer, Transform aircraftRoot)
        {
            Bounds localBounds;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                localBounds = filter.sharedMesh.bounds;
            else if (renderer is SkinnedMeshRenderer skinned)
                localBounds = skinned.localBounds;
            else
                return;
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3((mask & 1) == 0 ? min.x : max.x,
                    (mask & 2) == 0 ? min.y : max.y, (mask & 4) == 0 ? min.z : max.z);
                Vector3 local = aircraftRoot.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                Vector2 point = new Vector2(local.x, local.z);
                section.Min = Vector2.Min(section.Min, point);
                section.Max = Vector2.Max(section.Max, point);
            }
        }

        private static float SectionScore(DamageSection section, Vector2 point)
        {
            float dx = Mathf.Max(section.Min.x - point.x, 0f, point.x - section.Max.x);
            float dy = Mathf.Max(section.Min.y - point.y, 0f, point.y - section.Max.y);
            float outsideDistance = dx * dx + dy * dy;
            float centerDistance = (point - section.Center).sqrMagnitude;
            if (outsideDistance <= 0f)
                return -1f / section.Area + centerDistance * 0.00001f;
            return outsideDistance + centerDistance * 0.0001f;
        }

        private static void FindOpaqueBounds(Color32[] pixels, int width, int height,
            out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = width;
            minY = height;
            maxX = -1;
            maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * width + x].a > 0)
                    {
                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
            if (maxX < minX || maxY < minY)
                throw new InvalidOperationException("The F-117 damage silhouette has no opaque pixels.");
        }
    }

    [HarmonyPatch(typeof(StatusDisplay), nameof(StatusDisplay.Initialize))]
    internal static class F117StatusDisplayPatch
    {
        private const float RedStatusThreshold = 35f;
        private static readonly FieldInfo AircraftBackgroundField =
            AccessTools.Field(typeof(StatusDisplay), "aircraftBackground");
        private static readonly FieldInfo StatusDisplaysField =
            AccessTools.Field(typeof(StatusDisplay), "statusDisplays");

        [HarmonyPrefix]
        private static void Prefix(StatusDisplay __instance, Aircraft aircraft)
        {
            if (!Plugin.IsF117(aircraft))
                return;
            if (AircraftBackgroundField == null || StatusDisplaysField == null)
                throw new MissingFieldException("The game StatusDisplay layout changed; F-117 HUD repair cannot continue.");

            PresentationAssets.Initialize();
            PresentationAssets.EnsureDamageSections(aircraft);
            Image background = EnsureImage(__instance.gameObject, PresentationAssets.DamageSilhouette, Color.white);

            var displays = StatusDisplaysField.GetValue(__instance) as List<PartStatusDisplay>;
            if (displays == null)
            {
                displays = new List<PartStatusDisplay>();
                StatusDisplaysField.SetValue(__instance, displays);
            }
            displays.Clear();
            foreach (KeyValuePair<string, Sprite> section in PresentationAssets.DamageSections)
            {
                Transform partTransform = __instance.transform.Find(section.Key);
                if (partTransform == null)
                {
                    GameObject partObject = new GameObject(section.Key, typeof(RectTransform), typeof(CanvasRenderer));
                    partObject.layer = __instance.gameObject.layer;
                    partTransform = partObject.transform;
                    partTransform.SetParent(__instance.transform, false);
                }
                ConfigureStretch(partTransform as RectTransform);
                Image partImage = EnsureImage(partTransform.gameObject, section.Value,
                    new Color(1f, 1f, 0f, 0f));
                displays.Add(new PartStatusDisplay
                {
                    partImage = partImage,
                    redStatusThreshold = RedStatusThreshold
                });
            }
            AircraftBackgroundField.SetValue(__instance, background);
            Plugin.Log.LogDebug("F-117 status display wired " + displays.Count +
                " geometry-derived damage sections before HUD initialization.");
        }

        private static Image EnsureImage(GameObject target, Sprite sprite, Color color)
        {
            if (target.GetComponent<CanvasRenderer>() == null)
                target.AddComponent<CanvasRenderer>();
            Image image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        private static void ConfigureStretch(RectTransform rect)
        {
            if (rect == null)
                return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }
}
