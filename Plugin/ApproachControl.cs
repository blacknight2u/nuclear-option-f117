using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Blacknight2u.F117Nighthawk
{
    // Schedule the native controller's request, leaving its AoA/ground limiting,
    // actuator travel, rate error calculation and aircraft aerodynamics intact.
    [HarmonyPatch(typeof(ControlsFilter.FlyByWire), nameof(ControlsFilter.FlyByWire.Filter))]
    internal static class F117ApproachControl
    {
        private const float FullEffectSpeed = 80f;
        private const float NoEffectSpeed = 110f;
        private const float TransitionSeconds = 0.5f;
        private const float ApproachRollTightness = 0.80f;
        private const float ApproachPitchRate = 11f * Mathf.Deg2Rad;
        private static readonly ConditionalWeakTable<ControlsFilter.FlyByWire, BlendState> Blends =
            new ConditionalWeakTable<ControlsFilter.FlyByWire, BlendState>();

        private sealed class BlendState { internal float Weight; }
        internal struct OriginalSettings
        {
            internal bool Restore;
            internal float PitchScale;
            internal float RollTightness;
        }

        private static void Prefix(ControlsFilter.FlyByWire __instance, Aircraft __0, bool __3,
            float ___cornerSpeed, ref float ___gLimitPositive, ref float ___rollTightness,
            out OriginalSettings __state)
        {
            __state = default(OriginalSettings);
            if (!__instance.Enabled || !Plugin.IsF117(__0))
                return;

            BlendState blend = Blends.GetValue(__instance, key => new BlendState());
            float target = __3 && __0.gearState == LandingGear.GearState.LockedExtended
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullEffectSpeed, NoEffectSpeed, __0.speed))
                : 0f;
            blend.Weight = Mathf.MoveTowards(blend.Weight, target, Time.fixedDeltaTime / TransitionSeconds);
            if (blend.Weight <= 0f)
                return;

            __state = new OriginalSettings
            {
                Restore = true,
                PitchScale = ___gLimitPositive,
                RollTightness = ___rollTightness
            };
            ___rollTightness = Mathf.Lerp(___rollTightness,
                Mathf.Max(___rollTightness, ApproachRollTightness), blend.Weight);

            if (___cornerSpeed <= 0f || ___gLimitPositive <= 0f)
                return;
            float pressureRatio = __0.speed * __0.speed * __0.airDensity /
                (___cornerSpeed * ___cornerSpeed * 1.225f);
            float nativeRate = ___gLimitPositive * 9.81f / Mathf.Max(__0.speed, ___cornerSpeed * 0.75f);
            if (pressureRatio < 1f)
                nativeRate *= Mathf.Clamp(pressureRatio, 0.3f, 1f);
            if (nativeRate > 0f && nativeRate < ApproachPitchRate)
            {
                // gLimitPositive supplies the native rate calculation. Scale it only
                // for this call to reach the approach floor BEFORE native AoA limiting.
                // Never reduce a higher native target, nor raise the aircraft's G limit.
                ___gLimitPositive *= Mathf.Lerp(1f, ApproachPitchRate / nativeRate, blend.Weight);
            }
        }

        // Restore even when the original method throws. Each call starts from the
        // current user's/base settings, so values cannot accumulate across frames.
        private static void Finalizer(ref float ___gLimitPositive, ref float ___rollTightness,
            OriginalSettings __state)
        {
            if (!__state.Restore)
                return;
            ___gLimitPositive = __state.PitchScale;
            ___rollTightness = __state.RollTightness;
        }
    }
}
