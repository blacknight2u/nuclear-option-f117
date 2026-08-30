using System;
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
        private const string DamageSectionResourcePrefix =
            "Blacknight2u.F117Nighthawk.DamageSections.";
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

        internal static Sprite DamageSilhouette { get; private set; }
        internal static IReadOnlyDictionary<string, Sprite> DamageSections { get; private set; }
        internal static IReadOnlyList<string> DamagePartOrder => DamagePartNames;

        internal static void Initialize()
        {
            if (DamageSilhouette != null)
                return;

            Assembly assembly = Assembly.GetExecutingAssembly();
            DamageSilhouette = LoadSprite(assembly, DamageResource, "F117_DamageSilhouette");
            var sections = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (string partName in DamagePartNames)
            {
                string resource = DamageSectionResourcePrefix + partName + ".png";
                sections.Add(partName, LoadSprite(assembly, resource, partName + "_DamageSection"));
            }
            DamageSections = sections;
        }

        internal static void EnsureDamageSections(Aircraft aircraft)
        {
            if (aircraft == null || DamageSections == null)
                throw new InvalidOperationException("The F-117 damage display assets were not initialized.");
            var partNames = new HashSet<string>(aircraft.partLookup
                .Where(part => part != null)
                .Select(part => part.gameObject.name), StringComparer.Ordinal);
            string[] missing = DamagePartNames.Where(name => !partNames.Contains(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("The F-117 damage display cannot find: " + string.Join(", ", missing));
        }

        private static Sprite LoadSprite(Assembly assembly, string resourceName, string assetName)
        {
            byte[] data;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Missing embedded UI resource: " + resourceName);
                if (stream.Length <= 0 || stream.Length > int.MaxValue)
                    throw new InvalidOperationException("Invalid embedded UI resource length: " + resourceName);
                data = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int count = stream.Read(data, offset, data.Length - offset);
                    if (count <= 0)
                        throw new EndOfStreamException("Could not fully read " + resourceName);
                    offset += count;
                }
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = assetName + "_Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, data, true))
                throw new InvalidOperationException("Unity could not decode " + resourceName);
            UnityEngine.Object.DontDestroyOnLoad(texture);
            Sprite sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
            sprite.name = assetName;
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            return sprite;
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
            foreach (string partName in PresentationAssets.DamagePartOrder)
            {
                Sprite section = PresentationAssets.DamageSections[partName];
                Transform partTransform = __instance.transform.Find(partName);
                if (partTransform == null)
                {
                    GameObject partObject = new GameObject(partName, typeof(RectTransform), typeof(CanvasRenderer));
                    partObject.layer = __instance.gameObject.layer;
                    partTransform = partObject.transform;
                    partTransform.SetParent(__instance.transform, false);
                }
                ConfigureStretch(partTransform as RectTransform);
                Image partImage = EnsureImage(partTransform.gameObject, section,
                    new Color(1f, 1f, 0f, 0f));
                displays.Add(new PartStatusDisplay
                {
                    partImage = partImage,
                    redStatusThreshold = RedStatusThreshold
                });
            }
            AircraftBackgroundField.SetValue(__instance, background);
            Plugin.Log.LogDebug("F-117 status display wired " + displays.Count +
                " exact authored damage-section masks before HUD initialization.");
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
