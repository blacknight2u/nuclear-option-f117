using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Blacknight2u.F117Nighthawk
{
    internal static class PresentationAssets
    {
        private const string DamageResource = "Blacknight2u.F117Nighthawk.F117_Damage.png";

        internal static Sprite DamageSilhouette { get; private set; }

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

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
                {
                    name = "F117_Damage_Texture",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                if (!ImageConversion.LoadImage(texture, data, true))
                    throw new InvalidOperationException("Unity could not decode " + DamageResource);
                UnityEngine.Object.DontDestroyOnLoad(texture);

                DamageSilhouette = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0u,
                    SpriteMeshType.FullRect);
                DamageSilhouette.name = "F117_DamageSilhouette";
                UnityEngine.Object.DontDestroyOnLoad(DamageSilhouette);
            }
        }
    }

    [HarmonyPatch(typeof(StatusDisplay), nameof(StatusDisplay.Initialize))]
    internal static class F117StatusDisplayPatch
    {
        private const string PartName = "F117_CentralBody";
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
            Image background = EnsureImage(__instance.gameObject, PresentationAssets.DamageSilhouette, Color.white);

            Transform partTransform = __instance.transform.Find(PartName);
            if (partTransform == null)
            {
                GameObject partObject = new GameObject(PartName, typeof(RectTransform), typeof(CanvasRenderer));
                partObject.layer = __instance.gameObject.layer;
                partTransform = partObject.transform;
                partTransform.SetParent(__instance.transform, false);
            }
            ConfigureStretch(partTransform as RectTransform);
            Image partImage = EnsureImage(partTransform.gameObject, PresentationAssets.DamageSilhouette,
                new Color(1f, 1f, 1f, 0f));

            var displays = StatusDisplaysField.GetValue(__instance) as List<PartStatusDisplay>;
            if (displays == null)
            {
                displays = new List<PartStatusDisplay>();
                StatusDisplaysField.SetValue(__instance, displays);
            }
            displays.Clear();
            displays.Add(new PartStatusDisplay
            {
                partImage = partImage,
                redStatusThreshold = RedStatusThreshold
            });
            AircraftBackgroundField.SetValue(__instance, background);
            Plugin.Log.LogInfo("F-117 status display repaired before HUD initialization.");
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
