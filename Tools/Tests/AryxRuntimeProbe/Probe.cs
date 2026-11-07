using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

// Install only in an isolated game copy. The probe creates inactive test rigs,
// exercises native mount/attach/rearm/release code and quits automatically.
[BepInPlugin("local.f117.aryxprobe", "F117 Aryx Runtime Probe", "1.0.0")]
public sealed class Probe : BaseUnityPlugin
{
    private static readonly string[] Keys = { "aryx_glidebomb400_single", "Aryx_FastAGM1_Internal_x4",
        "Aryx_FastAGM1_Internal_x6", "Aryx_HeavyRocket1_x2", "Aryx_SmallCruiseMissile1_Quad_Internal",
        "Aryx_LRIRM1_Triple_Internal", "Aryx_SRIRM1_Double" };
    private readonly List<string> lines = new List<string>();
    private readonly List<MountedMissile> launched = new List<MountedMissile>();
    private readonly List<Vector3> launchPositions = new List<Vector3>();
    private float start, launchedAt;
    private bool inspected, finished;
    private Type integration;
    private static object F(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    private static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private void Awake() { start = Time.realtimeSinceStartup; }
    private void Update()
    {
        if (finished) return;
        if (inspected) Time.timeScale = 1f;
        try
        {
            if (!inspected)
            {
                if (Time.realtimeSinceStartup - start < 8f) return;
                Inspect();
                inspected = true;
                launchedAt = Time.realtimeSinceStartup;
                if (launched.Count == 0) Complete();
            }
            else if (Time.realtimeSinceStartup - launchedAt > 2.5f)
            {
                for (int i = 0; i < launched.Count; i++)
                {
                    var missile = launched[i];
                    Require(!missile.gameObject.activeSelf, "Native rail release did not finish: " + missile.name +
                        "; rail=" + F(missile, "railPosition") + "; timescale=" + Time.timeScale);
                    Vector3 delta = missile.transform.position - launchPositions[i];
                    Require(delta.y < -1.99f && Math.Abs(delta.x) < 0.001f && Math.Abs(delta.z) < 0.001f,
                        "Release did not travel down out of the bay: " + delta);
                }
                lines.Add("PASS native rail release: " + launched.Count + " mounted weapons moved down >=2m and deactivated");
                Complete();
            }
        }
        catch (Exception ex) { lines.Add("FAIL " + ex); Complete(false); }
    }
    private void Complete(bool success = true)
    {
        finished = true;
        lines.Add(success ? "RESULT PASS" : "RESULT FAIL");
        lines.Add("LIMIT: inactive test rigs; no live projectile spawning, combat, interception, laser tracking or multiplayer session test.");
        File.WriteAllLines(Path.Combine(Paths.GameRootPath, "f117-aryx-probe.txt"), lines);
        Application.Quit();
    }
    private void Inspect()
    {
        integration = AccessTools.TypeByName("Blacknight2u.F117Nighthawk.AryxWeaponIntegration");
        Require(integration != null, "New F117 integration was not loaded");
        var definition = (AircraftDefinition)Encyclopedia.Lookup["blacknight2u_F117A_Nighthawk"];
        var original = definition.unitPrefab.GetComponent<Aircraft>();
        var sets = original.weaponManager.hardpointSets;
        var bays = sets.Where(set => set.name == "Left Weapon Bay" || set.name == "Right Weapon Bay").ToArray();
        bool pack = Encyclopedia.WeaponLookup.ContainsKey(Keys[0]);
        lines.Add("Game " + Application.version + "; pack=" + pack);
        lines.Add("F117 module " + integration.Assembly.ManifestModule.ModuleVersionId);
        Require(bays.Length == 2, "Missing aircraft bays");
        foreach (var bay in bays)
            Require(bay.weaponOptions.Count(mount => mount != null && Keys.Contains(mount.jsonKey)) == (pack ? 7 : 0),
                "Wrong optional mount count in " + bay.name);
        var parameters = definition.aircraftParameters;
        Require(Mathf.Approximately(parameters.DefaultFuelLevel, .25f), "Default fuel is not 25%");
        Require(parameters.StandardLoadouts.Length == (pack ? 29 : 19), "Wrong preset count: " + parameters.StandardLoadouts.Length);
        int[] optionCounts = sets.Select(set => set.weaponOptions.Count).ToArray();
        int loadoutCount = parameters.loadouts.Count;
        var indexSnapshot = Encyclopedia.WeaponLookup.ToDictionary(pair => pair.Key, pair => ((INetworkDefinition)pair.Value).LookupIndex);
        AccessTools.Method(integration, "Register").Invoke(null, null);
        Require(parameters.StandardLoadouts.Length == (pack ? 29 : 19) && parameters.loadouts.Count == loadoutCount,
            "Repeated registration duplicated presets");
        Require(sets.Select(set => set.weaponOptions.Count).SequenceEqual(optionCounts), "Repeated registration duplicated mounts");
        Require(indexSnapshot.All(pair => ((INetworkDefinition)Encyclopedia.WeaponLookup[pair.Key]).LookupIndex == pair.Value),
            "Registration changed network indices");
        foreach (var preset in parameters.StandardLoadouts)
        {
            Require(Mathf.Approximately(preset.FuelRatio, .25f), "Preset fuel is not 25%: " + preset.Name);
            Require(preset.loadout.weapons.Count == sets.Length, "Wrong preset station count");
            for (int i = 0; i < sets.Length; i++)
            {
                WeaponMount mount = preset.loadout.weapons[i];
                if (mount == null) continue;
                Require(sets[i].weaponOptions.Contains(mount), "Preset weapon unavailable at station " + i);
                Require(Encyclopedia.WeaponLookup[mount.jsonKey] == mount && ((INetworkDefinition)mount).LookupIndex.HasValue,
                    "Preset cannot resolve its original weapon identity");
            }
            Require(preset.loadout.weapons.Any(m => m != null && m.jsonKey == "JammingPod1"), "Preset lost fixed jammer");
        }
        lines.Add("PASS presets, original registry identities, fixed jammer and idempotent registration");
        lines.Add("PASS 25% default fuel and all " + parameters.StandardLoadouts.Length + " preset fuel levels");
        var laser = original.GetComponentInChildren<LaserDesignator>(true);
        Require(laser != null && (int)F(laser, "maxTargets") == 1 && (float)F(laser, "range") == 20000f,
            "Built-in laser designator changed");
        if (!pack) return;

        foreach (string key in Keys)
        {
            var mount = Encyclopedia.WeaponLookup[key];
            string snapshot = Snapshot(mount);
            foreach (var sourceBay in bays)
                CheckMount(definition, sourceBay.hardpoints[0], mount, true);
            // A non-F117 aircraft must retain this pack's original mount and release.
            CheckMount(definition, bays[0].hardpoints[0], mount, false);
            Require(Snapshot(mount) == snapshot, "Shared Aryx mount was modified: " + key);
            lines.Add("PASS " + key + ": both bays fit; mounted settings/rearm valid; other aircraft and source prefab unchanged");
        }
    }
    private string Snapshot(WeaponMount mount)
    {
        return string.Join("|", new[] { mount.jsonKey, mount.mass.ToString("R"), mount.RCS.ToString("R"),
            mount.prefab.transform.localPosition.ToString("F6"), mount.prefab.transform.localScale.ToString("F6") }
            .Concat(mount.prefab.GetComponentsInChildren<MountedMissile>(true).Select(m =>
                m.transform.localPosition.ToString("F6") + ":" + F(m,"railDirection") + ":" + F(m,"railLength") +
                ":" + F(m,"railSpeed") + ":" + F(m,"railDelay") + ":" + m.info.GetInstanceID())).ToArray());
    }
    private void CheckMount(AircraftDefinition definition, Hardpoint source, WeaponMount mount, bool f117)
    {
        var rig = new GameObject(f117 ? "F117AryxTestRig" : "UnrelatedAircraftTestRig");
        rig.SetActive(false);
        rig.AddComponent<Mirage.NetworkIdentity>();
        var aircraft = rig.AddComponent<Aircraft>();
        ((Unit)aircraft).definition = f117 ? definition : Encyclopedia.Lookup.Values.OfType<AircraftDefinition>().First(d => d != definition);
        var manager = rig.AddComponent<WeaponManager>();
        aircraft.weaponManager = manager;
        Set(manager, "aircraft", aircraft);
        var socket = new GameObject(source.transform.name).transform;
        socket.SetParent(rig.transform, false);
        socket.localPosition = source.transform.localPosition;
        var partObject = new GameObject("TestMountMass");
        partObject.transform.SetParent(rig.transform, false);
        var part = partObject.AddComponent<AeroPart>();
        Set(part, "parentUnit", aircraft);
        var body = partObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        Set(part, "rb", body);
        var doorObject = new GameObject("TestBayDoor");
        doorObject.transform.SetParent(rig.transform, false);
        var door = doorObject.AddComponent<BayDoor>();
        var hardpoint = new Hardpoint { transform = socket, part = part, bayDoors = new[] { door }, doorOpenDuration = 1.2f,
            BuiltInWeapons = new Weapon[0], BuiltInTurrets = new Turret[0], HardpointIndex = -1 };
        GameObject instance = hardpoint.SpawnMount(aircraft, mount);
        Require(instance != null, "Native SpawnMount returned null");
        var stores = instance.GetComponentsInChildren<MountedMissile>(true);
        Require(stores.Length == mount.ammo, "Wrong spawned store count");
        if (!f117)
        {
            var originals = mount.prefab.GetComponentsInChildren<MountedMissile>(true);
            for (int i = 0; i < stores.Length; i++)
                foreach (string field in new[] { "railDirection", "railLength", "railSpeed", "railDelay" })
                    Require(F(stores[i], field).Equals(F(originals[i], field)), "Non-F117 release changed");
            Require(instance.transform.localPosition == mount.prefab.transform.localPosition, "Non-F117 rack moved");
            return;
        }
        object[] args = { socket, instance.transform, null };
        Require((bool)AccessTools.Method(integration, "TryMeasure").Invoke(null, args), "No mounted meshes");
        Bounds bounds = (Bounds)args[2];
        var center = new Vector3(0f, -0.31f, 0.10f);
        var extents = new Vector3(0.39f, 0.41f, 2.10f);
        for (int axis = 0; axis < 3; axis++)
            Require(bounds.min[axis] >= center[axis] - extents[axis] + 0.009f &&
                bounds.max[axis] <= center[axis] + extents[axis] - 0.009f, "Rack clips bay: " + mount.jsonKey);
        foreach (var missile in stores)
        {
            Require(F(missile, "railDirection").ToString() == "Down" && (float)F(missile, "railLength") == 2f &&
                (float)F(missile, "railSpeed") == 4f && (float)F(missile, "railDelay") >= 0.5f,
                "Incorrect bay release parameters");
            Vector3 initial = missile.transform.localPosition;
            AccessTools.Method(typeof(MountedMissile), "RemoveFromHardpoint").Invoke(missile, null);
            Set(missile, "fired", true);
            missile.ammo = 0;
            missile.transform.localPosition += Vector3.down;
            missile.Rearm(1, (WeaponStation)F(missile, "weaponStation"));
            Require(Vector3.Distance(initial, missile.transform.localPosition) < 0.0001f, "Rearm lost mounted position");
            launchPositions.Add(missile.transform.position);
            launched.Add(missile);
            // Inactive rig has no server identity, so no combat projectile is spawned.
            missile.Fire(aircraft, null, Vector3.forward, (WeaponStation)F(missile, "weaponStation"), default(GlobalPosition));
        }
    }
}
