# Building a Nuclear Option aircraft mod

This guide records the practical lessons from building the F-117A as a new
Blueprinter aircraft. It focuses on the failure modes that are difficult to
diagnose from visuals alone: model hierarchy, rigid-body ownership, control
axes, landing gear, cameras, cockpit displays, weapons, and runtime telemetry.

It is not a replacement for Blueprinter documentation or the game's code. Game
updates can change serialized fields and runtime assumptions, so validate every
aircraft against the installed version.

## 1. Define the aircraft contract before modeling

Write down the intended values and behaviors first:

- unique aircraft and livery identifiers
- dry mass, fuel mass, maximum takeoff mass, and center of gravity
- part breakdown and damage ownership
- wing and control-surface areas
- control travel, rate, and hinge axes
- thrust, fuel consumption, power generation, and afterburner state
- landing-gear position, suspension, steering, brake, and fold sequence
- camera, HUD, cockpit display, targeting, and countermeasure requirements
- weapon stations, bay doors, release paths, and compatible stores
- radar, infrared, optical, and emission behavior

Keep this contract in one build-time validator. A visual inspection cannot prove
mass accounting, joint ownership, control signs, hidden components, or network
identity.

## 2. Start from a working aircraft structure, not its identity

A working stock or community aircraft is useful for learning which components
the game expects. It is not a safe final aircraft prefab.

Build a new, uniquely named aircraft and retain only systems that are genuinely
required. Remove inherited artwork, airframe-specific colliders, weapons,
effects, cameras, and identifiers. Validate that no donor aircraft names or
asset references survive in the finished prefab.

Replacing a stock aircraft or retaining its identity can break missions,
selection screens, multiplayer synchronization, damage displays, and cached
loadouts.

## 3. Treat the visual and physics hierarchies as separate contracts

The Blender hierarchy answers "what moves with what?" The Nuclear Option part
graph answers "what owns mass, force, damage, and detachment?" They usually
overlap, but they are not interchangeable.

A reliable fixed-wing structure is:

```text
Aircraft
└─ central AeroPart
   ├─ nose/rear/wing/engine AeroParts connected by explicit joints
   ├─ landing-gear systems
   ├─ weapon-bay systems
   └─ visual model roots and locators
```

Each detachable or force-producing section needs deliberate parent-part and
joint ownership. Do not put the entire aircraft on one shared rigidbody and do
not allow child part initialization to overwrite the root mass.

At runtime, the game may reparent created AeroParts away from the visible
aircraft hierarchy. Code and diagnostics must follow the aircraft's part lookup
and part ownership, not assume every live AeroPart remains under the aircraft
transform.

## 4. Bones, rigid objects, pivots, and what may be deleted

Before deleting anything in Blender, classify it:

- A deforming skinned mesh needs its armature and weighted bones.
- A rigid mechanical piece may be exported as its own object with an authored
  pivot or driven by sampled transforms.
- A locator or empty may carry a camera, hinge, weapon, center-of-mass, contact,
  or animation endpoint even though it renders nothing.
- A cosmetic mesh can be removed only after proving it is not a parent,
  animation target, material carrier, or required reference.

Hiding bones is safe; deleting unknown bones is not. Use Blender's Outliner and
inspect parent, constraints, modifiers, vertex groups, and animation data before
removing a part.

For complex landing gear and bay mechanisms, do not weld everything into one
mesh. If pieces change position relative to one another, they need independent
pivots or sampled rigid-transform tracks. A rigid-fit comparison at multiple
animation frames is a fast way to prove whether pieces can be joined.

Keep one canonical `.blend`. Export scripts should operate on that file, accept
portable output paths, fail when required objects are missing, and avoid
version-by-string-replacement wrappers.

## 5. Coordinate systems and neutral poses

Record the model-to-Unity axis conversion once and apply it consistently to:

- mesh vertices
- object transforms
- hinge axes
- camera forward/up
- lift normals
- landing-gear forward
- weapon release direction
- center-of-mass and contact locators

An angle can be represented by an equivalent angle with the opposite axis.
Normalizing both incorrectly can reverse a hinge while preserving its unsigned
angle. Validate the transformed endpoint, not only the number of degrees.

Export flight controls at their true aerodynamic neutral pose. A source
animation's nominal frame may already contain trim, deflection, suspension, or
unrelated later motion. Compare the surface to its fixed parent and require the
neutral panel to be aligned as intended.

## 6. AeroParts, colliders, mass, and joints

Every physical part needs explicit values for mass, center of mass, collision
size, aerodynamic area, damage, and joint limits. The sum of part masses must
match the intended dry aircraft mass; fuel and stores are added separately by
the game.

Measure center of gravity from the serialized mass references, not merely from
object origins. On a tricycle aircraft, the CG must be ahead of the main-wheel
plane by a reasonable margin. Too far forward overloads the nose gear; behind
the mains allows the aircraft to tip backward.

Use contained colliders that approximate the visible part. Large invisible
boxes can strike sibling rigidbodies, terrain, weapons, or control surfaces and
cause spawn explosions, bouncing, drag, or unexplained tipping. Control-surface
colliders should remain inside the panel geometry and must not overlap the
fixed wing or tail.

Connect each part to the correct physical parent. Elevons normally belong to
their wing; rudders belong to the rear body or tail structure. A joint to an
unrelated parent can exceed the game's attachment-distance checks or produce
large impulses as the graph initializes.

## 7. Control surfaces: one pivot for visuals and force

A working control surface needs all of these to agree:

1. the visible hinge pivot
2. the ControlSurface component's signed input ranges
3. the AeroPart lift axis and center of lift
4. the physical parent and joint
5. the neutral pose

Rotate a dedicated visual/force pivot, not the complete detachable AeroPart.
Rotating the part rigidbody itself can make panels spin like fan blades, collide
with the aircraft, or detach.

For elevons, pitch and roll commands combine. Ensure the maximum combined
deflection stays inside the real mechanical envelope. For canted rudders,
derive the local hinge and signed yaw range from the model; copying a vertical
tail sign can turn stability feedback into positive feedback and produce a
speed-amplified fishtail.

Do not judge authority from animation alone. Log desired deflection, actual
surface transform, local angular rate, aerodynamic force, and moment about the
center of gravity.

## 8. Aerodynamics and flight-control tuning

Establish correct geometry before tuning controllers:

- measure projected planform and control-surface areas
- place centers of lift relative to the true center of gravity
- align fixed lift axes with the intended aircraft reference plane
- verify force signs at small positive and negative angle of attack
- verify pitch, roll, and yaw moments independently
- keep areas constant unless the aircraft truly changes lifting geometry

A controller cannot repair a reversed rudder, wrong lift normal, bad center of
gravity, or overlapping collider. Runtime scripts that rewrite wing area,
control axes, or global airfoil tables every frame conceal authoring mistakes
and make multiplayer/debugging harder. Prefer serialized native physics and use
runtime code only for behavior the asset format cannot express.

Test in this order:

1. spawn at rest with no damage or motion
2. taxi slowly and brake
3. accelerate hands-off and watch yaw/roll drift
4. rotate with one clean pitch input
5. retract and extend gear
6. test isolated pitch, roll, and yaw in flight
7. repeat at increasing speed
8. test damage and replacement-aircraft spawns

## 9. Landing gear and doors

Landing gear combines physics, animation, and ground-contact coordinate frames.
Validate:

- deployed hinge is the game's expected zero pose
- the gear transform's forward direction produces positive runway speed
- tire radius and contact point match visible tires
- suspension travel, spring, damping, and failure threshold agree
- the nose cast/contact remains reliable on uneven terrain
- steering and self-alignment do not create a taxi bias
- retracted parts fit inside the airframe

The native gear fold coordinate is a staged animation value, not necessarily a
normalized percentage. It may legitimately exceed `1.0` while outer doors close.
Clamp only calculations that require a normalized strut position; preserve the
extra door-staging phase.

Inner doors, outer doors, struts, wheels, and linkages may follow different
timelines. Sample the source animation and validate both endpoints plus at least
one intermediate pose. Separating rigid moving pieces prevents folded-backward
gear, floating hinges, and door linkages that cut through the fuselage.

## 10. Cameras, canopy, HUD, and cockpit displays

Author a cockpit-eye locator at the seated pilot's real eye line with the same
forward/up frame as the aircraft. Test first spawn, replacement aircraft, and
cycling away from and back to cockpit view; camera state objects can retain
inertial offsets between aircraft.

Use game-native HUD and tactical-screen logic where possible. Copying only the
visible UI mesh can omit scripts, render textures, or initialization behavior.
Scope compatibility repairs to the new aircraft and fail by disabling only the
broken display—not the aircraft or game mode.

Cockpit screens often sample regions of a shared render-texture atlas. A mesh
with collapsed `(0,0)` UVs shows one pixel; mapping the full atlas to every MFD
stretches or combines unrelated panels; rotating UV axes in the wrong direction
can turn a 90-degree error into an upside-down image. Validate each disconnected
screen island's bounds, aspect ratio, and axis direction.

Canopy frame and glass require separate materials. Keep the frame opaque and
airframe-colored; keep only the window polygons transparent. Verify shader
compatibility after Blueprinter replaces or rebinds materials.

## 11. Weapon bays and release paths

Separate fixed bay cavities from moving door panels and mechanical linkages.
Give each door a native BayDoor component and validate its closed, intermediate,
and open positions.

An internally compatible weapon still needs a release path that clears the
airframe. A rack designed for forward launch can spawn a missile inside the
fuselage. Configure native rail/drop motion on an aircraft-specific cloned mount
rather than modifying the global weapon. Test every store family from external
view and confirm the live projectile—not only the decorative store—clears the
bay before propulsion or guidance begins.

Keep weapon counts, mass, and maximum takeoff weight synchronized. Large stores
may allow one or two; smaller stores may use multi-store racks. Validate cached,
mission-provided, and networked loadouts as well as the hangar selection path.

## 12. Signatures, sensors, and electrical systems

Use the game's native detection mechanics before adding custom patches. A
stealth aircraft can use a small nonzero clean radar signature, with progressive
penalties from bay opening and gear deployment. Clamp the native animation value
when converting it into a normalized signature penalty.

Do not add an emitting search radar merely to populate a tactical screen. Shared
friendly tracks, passive warning receivers, and optical targeting can remain
available without giving the aircraft radar emissions it should not have.

Engine IR sources must use the correct transform direction so aspect naturally
matters. Match afterburner data, effects, and HUD labels to the actual engine.
Power-consuming equipment must be balanced against serialized generation and
storage rather than bypassing the native power system.

## 13. Damage, effects, and multiplayer

Use a coherent stock-scale damage profile unless the aircraft has a deliberate
reason to differ. Initialize hit points, structural thresholds, parent joints,
engine/fuel critical paths, and the health silhouette together.

Missing particle materials, invalid spark emitters, or copied effects can create
pink geometry and log flooding only after movement or damage. Validate shaders
and effects in the packaged bundle, not only in Blender or the Unity editor.

Every runtime patch must first prove it is operating on this aircraft. Avoid
global weapon, airfoil, camera, UI, or detection changes. Multiplayer clients
must load matching definitions, plugin versions, dependencies, and loadouts.

## 14. Runtime plugin boundaries

Runtime code is appropriate for aircraft-scoped behavior the serialized asset
cannot provide, such as dynamic signature calculation, staged visual linkages,
camera-state compatibility, or a guarded native-UI integration.

Runtime code should not be the primary author of mass, CG, colliders,
aerodynamic area, lift axes, control travel, or part ownership. Those belong in
the asset and validator. Harmony patches must exit immediately for non-target
aircraft. Routine initialization belongs at debug level; warnings and errors
should describe an actionable missing contract.

## 15. Validation and telemetry

Use several layers of evidence:

- Blender audits for geometry, transforms, neutral poses, and animation tracks
- Unity contract validation for components, fields, references, counts, and
  generated assets
- bundle inspection for the data that actually ships
- game logs for initialization and exceptions
- structured flight telemetry for motion, controls, forces, gear, damage, and
  performance

Interpret telemetry in context. Raw input is the pilot's command; filtered input
may include stability assistance after the pilot releases the control. A steady
filtered command with low angular rate is not automatically a failed control
surface. Likewise, a native gear fold value above one can be a door phase rather
than an out-of-range error. Prefer multi-sample, phase-aware rules over single
threshold alarms.

## 16. Symptom-to-cause checklist

| Symptom | Inspect first |
| --- | --- |
| Aircraft spawns tilted, tips, or slides | tire contacts, spawn height, CG versus mains, collider overlap |
| Camera appears beside or misaligned with aircraft | cockpit locator frame, reused camera-state offsets, aircraft identity |
| Parts explode or fall off on spawn | joint parent, attachment distance, sibling collider overlap, hinge rest pose |
| Control panels spin or bounce | rotating AeroPart instead of child pivot, collider containment, hinge axis |
| Little or no lift/control authority | lift normal, area, center of lift, control sign, controller handoff |
| Fishtail worsens with speed | rudder feedback sign, yaw moment, wheel alignment, CG, asymmetric drag |
| Gear folds backward or doors/linkages clip | signed axis conversion, wrong source frame, welded moving pieces, staged timing |
| Pink or missing geometry appears after movement | packaged material/shader/effect reference, hidden moving renderer |
| MFD is blank, stretched, rotated, or intermittent | render texture, material binding, UV island and axis orientation |
| Internal missile clips through the aircraft | mount rail/drop path, bay clearance, ignition timing |
| Missions fail after enabling the mod | stock asset replacement, duplicate identity, missing required component, startup exception |
| FPS collapses during logging | transition promotion, lifecycle/UI churn, dynamic snapshot rate, logger self-cost |

## 17. Release checklist

- build from a clean checkout plus documented local dependencies
- run model, Unity contract, and final bundle audits
- verify no machine-specific paths are required by the supported workflow
- verify package metadata, dependency versions, hash, and archive layout
- inspect the archive for source files, logs, backups, and credentials
- import through NOMM on a clean installation
- test first spawn, replacement spawn, takeoff, gear, each weapon family,
  landing, damage, and multiplayer version matching
- keep one canonical model and one supported exporter
- preserve research separately from the release workflow
