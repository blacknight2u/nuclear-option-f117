using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Call the actual patched native filter with frozen aircraft state. These checks
// establish requests and transition behavior, not aerodynamic response in flight.
internal static class ApproachControlChecks
{
    private static object Get(object owner, string name) => AccessTools.Field(owner.GetType(), name).GetValue(owner);
    private static void Set(object owner, string name, object value) => AccessTools.Field(owner.GetType(), name).SetValue(owner, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Approach control: " + message);
    }
    private static void Near(float actual, float expected, string message)
    {
        Require(Math.Abs(actual - expected) < .001f, message + ": actual=" + actual + ", expected=" + expected);
    }
    private struct Output { internal float PitchRate; internal float Roll; }

    internal static void Run(Aircraft source, List<string> lines)
    {
        var parent = new GameObject("InactiveF117ApproachFixture");
        parent.SetActive(false);
        var copy = UnityEngine.Object.Instantiate(source.gameObject, parent.transform);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        var aircraft = copy.GetComponent<Aircraft>();
        var body = copy.GetComponent<Rigidbody>();
        body.isKinematic = false;
        aircraft.SetRB(body);
        var filter = copy.GetComponentInChildren<ControlsFilter>(true);
        Set(aircraft, "controlsFilter", filter);
        var template = filter.GetFlyByWire();
        Func<ControlsFilter.FlyByWire> fresh = () =>
        {
            var result = new ControlsFilter.FlyByWire();
            foreach (var field in typeof(ControlsFilter.FlyByWire).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                field.SetValue(result, field.GetValue(template));
            Set(result, "limitFactorSmoothed", 1f);
            return result;
        };
        Func<ControlsFilter.FlyByWire, float, bool, bool, float, int, Output> step =
            (fbw, speed, gear, assist, alpha, frames) =>
        {
            aircraft.speed = speed;
            aircraft.airDensity = 1.225f;
            aircraft.radarAlt = 50f;
            aircraft.gearDeployed = gear;
            aircraft.gearState = gear ? LandingGear.GearState.LockedExtended : LandingGear.GearState.LockedRetracted;
            body.velocity = new Vector3(0f, -Mathf.Sin(alpha * Mathf.Deg2Rad), Mathf.Cos(alpha * Mathf.Deg2Rad)) * speed;
            float originalG = (float)Get(fbw, "gLimitPositive");
            float originalRoll = (float)Get(fbw, "rollTightness");
            var output = new Output();
            for (int i = 0; i < frames; i++)
            {
                var inputs = new ControlInputs { pitch = -1f, roll = 1f };
                fbw.Filter(aircraft, inputs, Vector3.zero, assist);
                Near((float)Get(fbw, "gLimitPositive"), originalG, "pitch-scale restoration");
                Near((float)Get(fbw, "rollTightness"), originalRoll, "roll-gain restoration");
                output.PitchRate = -fbw.GetTargetPitchAngVel() * Mathf.Rad2Deg;
                output.Roll = inputs.roll - (float)Get(fbw, "rollTrim");
            }
            return output;
        };

        var active = fresh();
        Output first = step(active, 76f, true, true, 0f, 1);
        Require(first.PitchRate > 8.99f && first.PitchRate < 10f && first.Roll < .70f,
            "approach response snapped to full strength on entry");
        Output full = step(active, 76f, true, true, 0f, 120);
        Near(full.PitchRate, 11f, "settled approach pitch rate");
        Near(full.Roll, .70f, "settled approach roll command before trim");
        Near((float)Get(active, "alphaLimiter"), 18f, "AoA onset changed");
        Near((float)Get(active, "alphaLimiterStrength"), .1f, "AoA limiter strength changed");
        Output limited = step(active, 76f, true, true, 23f, 1);
        Near(limited.PitchRate, 5.5f, "native 23-degree AoA reduction");
        Near(step(active, 76f, true, true, 28f, 1).PitchRate, 0f, "native 28-degree AoA suppression");
        lines.Add("PASS approach control: 11 deg/s pitch and 70% roll before trim; native 18/23/28-degree AoA protection retained");

        step(active, 76f, true, true, 0f, 1);
        Output gearTransition = step(active, 76f, false, true, 0f, 1);
        Require(gearTransition.Roll > .569f && gearTransition.Roll < .70f, "gear-up response did not blend");
        Output gearUp = step(active, 76f, false, true, 0f, 120);
        Near(gearUp.PitchRate, 8.993146f, "gear-up native pitch rate");
        Near(gearUp.Roll, .56875f, "gear-up native roll command");
        Output middle = step(active, 95f, true, true, 0f, 120);
        Require(middle.Roll > .56875f && middle.Roll < .70f, "speed fade lacks intermediate response");
        Output fast = step(active, 150f, true, true, 0f, 120);
        Near(fast.Roll, .56875f, "high-speed native roll command");
        Near(fast.PitchRate, 22.482864f, "higher native pitch rate was reduced");
        step(active, 76f, true, true, 0f, 120);
        step(active, 76f, true, false, 0f, 120);
        Output assistOff = step(active, 76f, true, false, 0f, 1200);
        Near(assistOff.Roll, .56875f, "assist-off native roll command");
        Near(assistOff.PitchRate, .45f * Mathf.Rad2Deg, "assist-off native pitch endpoint");
        lines.Add("PASS approach control: smooth gear/speed transitions; normal high-speed and assist-off behavior restored");

        var second = fresh();
        Output secondEntry = step(second, 76f, true, true, 0f, 1);
        Require(secondEntry.PitchRate < 10f, "blend state leaked between aircraft controllers");
        var stronger = fresh();
        Set(stronger, "gLimitPositive", 9f);
        Set(stronger, "rollTightness", .9f);
        Output retained = step(stronger, 76f, true, true, 0f, 120);
        Near(retained.PitchRate, 13.489719f, "higher configured pitch demand was reduced");
        // The test's stored trim can make the final output clamp at 1; remove it
        // for one call so the higher configured roll response can be measured.
        Set(stronger, "rollTrim", 0f);
        Near(step(stronger, 76f, true, true, 0f, 1).Roll, .7875f, "higher configured roll gain was reduced");

        step(second, 76f, true, true, 0f, 120);
        bool threw = false;
        try { second.Filter(aircraft, null, Vector3.zero, true); }
        catch (NullReferenceException) { threw = true; }
        Require(threw, "native exception was suppressed");
        Near((float)Get(second, "gLimitPositive"), 6f, "exception left altered pitch scale");
        Near((float)Get(second, "rollTightness"), .65f, "exception left altered roll gain");

        copy.name = "UnrelatedApproachFixture";
        ((Unit)aircraft).definition = Encyclopedia.Lookup.Values.OfType<AircraftDefinition>()
            .First(d => d.jsonKey != "blacknight2u_F117A_Nighthawk");
        Output unrelated = step(fresh(), 76f, true, true, 0f, 120);
        Near(unrelated.PitchRate, 8.993146f, "unrelated aircraft pitch changed");
        Near(unrelated.Roll, .56875f, "unrelated aircraft roll changed");
        lines.Add("PASS approach control: settings restored after every call/exception; stronger settings retained; per-controller isolation; other aircraft unaffected");
    }
}
