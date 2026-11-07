using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

// Exercise the shipped controller against an inactive copy of the real prefab.
// Gear compression/velocity are fixture inputs; this is not a flight simulation.
internal static class AutomaticChuteChecks
{
    private static object Get(object owner, string name) => AccessTools.Field(owner.GetType(), name).GetValue(owner);
    private static void Set(object owner, string name, object value) => AccessTools.Field(owner.GetType(), name).SetValue(owner, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Automatic chute: " + message);
    }

    internal static void Run(Aircraft source, List<string> lines)
    {
        var parent = new GameObject("InactiveF117ChuteFixture");
        parent.SetActive(false);
        var copy = UnityEngine.Object.Instantiate(source.gameObject, parent.transform);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        var aircraft = copy.GetComponent<Aircraft>();
        var body = copy.GetComponent<Rigidbody>();
        body.isKinematic = false;
        var type = AccessTools.TypeByName("Blacknight2u.F117Nighthawk.F117RuntimeController");
        var controller = copy.GetComponent(type) ?? copy.AddComponent(type);
        AccessTools.Method(type, "Initialize").Invoke(controller, new object[] { aircraft });
        Require((bool)Get(controller, "chuteAvailable"), "real prefab chute geometry was unavailable");
        Set(controller, "cockpitDisplayBound", true);
        var nose = (LandingGear)Get(controller, "noseGear");
        var mains = (LandingGear[])Get(controller, "mainGears");
        var gears = mains.Concat(new[] { nose }).ToArray();
        var update = AccessTools.Method(type, "Update");
        Action step = () => update.Invoke(controller, null);
        Func<bool> deployed = () => (bool)Get(controller, "deployed");
        Action reset = () =>
        {
            foreach (string field in new[] { "deployed", "spent", "wasAirborne", "landingRollArmed",
                "previousMainGearContact", "noseGearBroken", "brokenGearTouchdown" }) Set(controller, field, false);
            Set(controller, "brokenMainGearCount", 0);
            Set(controller, "airborneEvidenceTime", 0f);
            aircraft.gearState = LandingGear.GearState.LockedExtended;
            aircraft.radarAlt = 0f;
            body.velocity = Vector3.forward * 72f;
            foreach (var gear in gears) Set(gear, "compressionDistance", .05f);
        };

        reset();
        step();
        Require(!deployed(), "spawn/taxi contact deployed the chute without prior flight");
        aircraft.radarAlt = 20f;
        foreach (var gear in gears) Set(gear, "compressionDistance", 0f);
        step();
        Require(!deployed() && (bool)Get(controller, "wasAirborne"), "airborne arming failed");
        aircraft.radarAlt = 0f;
        foreach (var gear in mains) Set(gear, "compressionDistance", .05f);
        step();
        Require(!deployed(), "main-wheel contact deployed before nose-wheel support");
        Set(nose, "compressionDistance", .05f);
        step();
        Require(deployed(), "valid touchdown failed to deploy without a brake command");
        body.velocity = Vector3.forward * 10f;
        step();
        Require(!deployed() && (bool)Get(controller, "spent"), "low-speed jettison failed");
        aircraft.radarAlt = 20f;
        foreach (var gear in gears) Set(gear, "compressionDistance", 0f);
        body.velocity = Vector3.forward * 72f;
        step();
        aircraft.radarAlt = 0f;
        foreach (var gear in gears) Set(gear, "compressionDistance", .05f);
        step();
        Require(!deployed(), "spent chute redeployed after another touchdown");
        lines.Add("PASS automatic chute: airborne -> mains -> nose deploys without brakes; taxi excluded; jettison and single-use retained");

        Action arm = () => { reset(); Set(controller, "wasAirborne", true); };
        arm();
        body.velocity = Vector3.forward * 115f;
        step();
        Require(!deployed(), "overspeed touchdown deployed");
        body.velocity = Vector3.forward * 72f;
        step();
        Require(deployed(), "armed rollout did not deploy after slowing into range");
        arm();
        body.velocity = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward * 72f;
        step();
        Require(!deployed(), "misaligned touchdown deployed");
        body.velocity = Vector3.forward * 72f;
        step();
        Require(deployed(), "armed rollout did not deploy after alignment");
        arm();
        body.velocity = new Vector3(0f, -7f, 72f);
        step();
        Require(!deployed(), "excessive sink rate deployed");
        body.velocity = Vector3.forward * 72f;
        step();
        Require(deployed(), "settled touchdown did not deploy");
        arm();
        aircraft.radarAlt = 5f;
        step();
        Require(!deployed(), "high radar altitude deployed despite fixture wheel contact");
        arm();
        aircraft.gearState = LandingGear.GearState.LockedRetracted;
        step();
        Require(!deployed(), "retracted gear deployed");
        arm();
        body.velocity = Vector3.forward * 24f;
        step();
        Require(!deployed(), "below-minimum touchdown speed armed deployment");
        arm();
        Set(mains[0], "compressionDistance", 0f);
        step();
        Require(!deployed(), "one unsupported main wheel deployed");
        lines.Add("PASS automatic chute: speed, alignment, sink-rate, altitude and gear-contact gates retained");

        arm();
        Set(nose, "compressionDistance", 0f);
        AccessTools.Method(type, "OnLandingGearBreak").Invoke(controller, new object[] { nose });
        step();
        Require(deployed(), "confirmed nose-gear break lost touchdown evidence");
        lines.Add("PASS automatic chute: damaged-gear touchdown fallback retained");
        // Leave the fixture inactive until the isolated test process exits.
    }
}
