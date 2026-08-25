# F-117 Nighthawk for Nuclear Option

Audited Blueprinter aircraft mod based on the Lockheed F-117A, with an explicit
F-117 flight-control law and contract-validated game-native runtime systems.

The implementation targets a dedicated aircraft hierarchy with aircraft-specific
mass, center of gravity, aerodynamics, propulsion, landing gear, control surfaces,
internal weapon bays, and drag-chute behavior. It does not replace a stock aircraft.
The runtime plugin is scoped to this aircraft and its internal weapon-bay systems.

Version 0.3.2 repairs the retail-game status HUD initialization path and requires
exact name-and-type matches for all game-native assets copied into the aircraft.

Version 0.3.3 corrects hangar-spawn gear initialization, moves internal stores to
the modeled bay suspension plane, and preserves cockpit depth behind smoked glazing.

Version 0.4.0 replaces the disconnected aerodynamic scaffold with the same structural
contract used by working Blueprinter aircraft: one central AeroPart and twelve jointed
child AeroParts. It also moves each LandingGear component into its sprung hierarchy,
uses the model's cockpit-camera locator, and restores dedicated cockpit/canopy renderer
groups. This fixes the shared-rigidbody mass overwrite that caused tilted or detached
spawns, invisible/nonfunctional gear, and camera/airframe disagreement.

Version 0.4.1 corrects the remaining source-pose errors: landing gear now exports from
the fully deployed source frame and flight controls from the neutral frame. Its physics
contacts use the measured tire planes, its mass graph has the complete 23,814 kg design
mass with the center of gravity ahead of the main wheels, and its cockpit view derives
from the seated pilot's helmet point. ControlSurface now animates dedicated child visual
pivots instead of rotating detachable AeroParts, eliminating the fan-blade behavior.

Version 0.4.2 corrects the weight accounting exposed by the game loadout screen. The
connected part graph is now the 13,380 kg dry aircraft; the game adds 8,250 kg internal
fuel and selected stores separately, leaving 2,184 kg below the 23,814 kg MTOW at full
fuel. The two single-store sockets are replaced by one game-native internal multi-store
rack whose selectable loads contain 6, 4, 2, or 1 weapon according to store size.

Version 0.4.3 moves the internal camera from the mismatched donor pilot helmet point to
the aircraft model's authored cockpit eye line, lowering it by approximately 25 cm. It
also removes the source model's baked boarding ladder geometrically and validates that
no ladder triangles remain in the generated exterior mesh.

Version 0.4.4 prevents low-speed braking from breaking the nose gear by allowing its
full 0.32 m suspension stroke before the game's compression failure test. It also maps
the canted rudders onto their measured source-model local-Z hinges and limits every
elevon's combined pitch-plus-roll motion to the source model's 22.5-degree endpoint.

Version 0.4.5 promotes the visually approved aircraft cleanup to the canonical export
source. It removes the baked ladder, parking chocks, ground-support equipment, and
unneeded cosmetic mechanism meshes while retaining every animated gear, bay-door,
canopy, and control-surface root. It also restores the authored landing-gear materials,
fixes decal import and alpha rendering, refreshes the aircraft/status silhouettes, and
reduces normal low-speed taxi scrub noise without suppressing real high-energy skids.

Version 0.4.6 connects each moving control-surface pivot to its AeroPart lift axis so
elevon and rudder commands generate aerodynamic force as well as animation. It reverses
the canopy''s runtime hinge direction so ejection opens upward, moves the cockpit camera
34 cm aft to the pilot eye position, silences false-positive skid squeal from the custom
wheel rig, and matches the retail HUD''s bottom-right anchor/pivot contract so the health
silhouette remains fully on-screen.

Version 0.4.7 gives each control surface a model-derived aerodynamic center so its lift
produces real pitch, roll, and yaw torque. It restores the game-native radar, tactical
screen, target camera, and aircraft HUD extras; binds their render texture to the modeled
left MFD; aligns the cockpit eye point with the seat; and authors the untextured canopy
frame as black instead of its previous white fallback. The elevons now export at measured
wing-aligned neutral subframes instead of the source animation's reflexed nominal frame;
selected landing-gear and bay-door hinge details are restored. Replacement aircraft also
clear the cockpit camera inertia retained from the previous crash before entering cockpit view.

Version 0.4.8 removes the repacked custom cockpit UI that could lose its retail scripts,
throw during aircraft initialization, and leave a full-screen black overlay. The serialized
aircraft now contains a harmless fallback while the runtime plugin selects verified native-game
HUD and tactical-screen prefabs before initialization. If a native screen is unavailable, the
guard disables only that screen instead of leaving a broken overlay. This also restores the
standard always-rendered flight readouts instead of the angle-dependent copied HUD behavior.

Version 0.4.9 gives the radar and target-camera feed the same dedicated screen-renderer
contract used by stock aircraft instead of a combined cockpit material slot and disabled
dummy output. It refreshes the deterministic bad first-spawn cockpit camera state after the
spawn frame, makes the physical HUD projector substantially clearer, transfers the cockpit
frame's exact Blender-authored black material response, and freezes restored gear/bay details
at their user-approved saved transforms. Aerodynamic surfaces now use their real orientation
instead of cancelling takeoff angle-of-attack through artificial airflow alignment, and native
scrape audio plus a valid inert spark emitter prevent collision-error log flooding.

Version 0.4.30 replaces the plugin-driven aerodynamic workaround with the game's native
control-surface contract. Each elevon and rudder now carries its aerodynamic lift axis beneath
the same servo pivot that animates the model, so visible and physical deflection share the
55-degree-per-second actuator rate. Fixed lifting area is constant in every gear state and is
distributed across the blended body and wings without changing the previous total area or
fore/aft aerodynamic centroid. Per-spawn AeroPart re-registration, speed-dependent wing area,
gear-dependent zero rudders, runtime global airfoil-table replacement, and per-tick manual lift
axes are removed. Pitch damping is raised to 2.8, yaw tightness returns to 1.0, and synthetic
weathervaning is removed before controlled flight testing.

Version 0.4.33 keeps the corrected physical attachment graph and replaces every solid
control-surface box with an inset convex mesh collider generated in hinge space. This
matches the proven Shrike/FS-41 pattern and removes the empty box volume that could hit
unrelated aircraft parts, tear off the rear assembly, or bounce the elevons at spawn.
The same build corrects the measured neutral bias on only the two inner elevons while
leaving the already-aligned outer elevons unchanged.
Elevons now parent and joint to their matching wing, while both rudders parent and joint to
the rear body, matching the working Shrike and FS-41 aircraft contract. Broad automatically
generated collider envelopes are replaced by contained Awake proxies, and the explicit wing
and tail boxes no longer overlap sibling rigidbodies. This prevents lifting parts from moving
more than the game's 0.5 m attachment limit and silently detaching during spawn or taxi. The
nose gear gains a longer terrain cast and larger soft-ground contact patch, while its proven
55-degree-per-second steering and low self-alignment are restored to remove taxi bias.

Version 0.4.44 fixes the high-speed, no-input runway ground loop at its source. The previous
validator measured AeroPart origins instead of their referenced mass points and therefore
reported the dry center of gravity as 0.84 m ahead of the main wheels when it was actually
1.663 m ahead. The corrected authoring balances the true 13,380 kg mass graph exactly 0.50 m
ahead of the main-wheel plane, reducing static nose load from 29.4% to 8.8%. The validator now
uses every serialized centerOfMass reference so the error cannot silently return.

Version 0.4.45 fixes the remaining speed-amplified fishtail on the runway and in flight. Runtime
telemetry proved the stability controller requested the correct opposite-yaw correction, but the
positive rudder range converted it into a tail moment that reinforced the measured yaw rate. Both
rudders now retain their coordinated shared hinge sign while using the feedback-opposing travel
direction. Contract validation checks that signed range so the unstable loop cannot return.

Version 0.4.46 was the first landing-gear and bomb-bay visual cleanup. It corrected exterior
material bindings and exposed the source model's translated gear-door roots, but still represented
each complete gear as one joined rigid mesh. Version 0.4.48 supersedes that provisional gear pose
with the source animation's actual structure and endpoints.

Version 0.4.47 removes the remaining spawn-time flight-model and model-authoring repairs. The
serialized aircraft is again the sole authority for aerodynamic area, lift axes, control travel,
mass, center of gravity, collision geometry, gear geometry, and bay-panel ownership. Both fixed bay
cavities remain on the fuselage outside their matching native door, and all six controls use parent-matched
joints, inset convex colliders, the native-reference 100 HP durability, and constant aerodynamic
area in every gear state. The electrical system now recharges its 1,200 kJ supply at the correct
two-engine native rate. Native self-registering flare and chaff ejectors, including a bundled
RadarChaff payload, replace the missing countermeasure setup. Native tactical-screen and HUD
selection is deterministic, and only the selected F-117 HUD clone has incompatible donor engine
telemetry removed. The runtime plugin retains only F-117 features and retail-asset compatibility;
it no longer rewrites the aircraft physics graph at spawn or during flight.

Version 0.4.48 is a telemetry- and source-animation-derived flight/gear correction. The v0.4.47
flight log proved that all five fixed horizontal lift axes were accidentally pitched 9 degrees
nose-up: at 150-235 m/s the aircraft climbed at a median 16.8 m/s while visibly 3.2 degrees
nose-down, and the pitch controller saturated near 0.94 merely trying to unload the wing. Fixed
lift axes are now aircraft-aligned. Elevon areas are measured directly from the production mesh
(2.992 m2 inner and 2.418 m2 outer), while fixed-wing area is reduced by the identical amount so
the established 73.0 m2 horizontal planform is unchanged. The real 22.5-degree actuator envelope
is allocated as 15 degrees pitch plus 7.5 degrees roll instead of the old 12-plus-3 workaround.

The landing gear now uses frame 81, the actual deployed endpoint of the source gear sequence;
frame 218 contains unrelated later wheel/suspension motion and was never a valid rest pose. All
39 rigid source meshes retain nine source-derived linkage poses from deployed to stowed. Offline
rigid-fit error is at most 0.00000136 m. Gear-door pivots are derived from their endpoint geometry
with at most 0.000027 m residual, tire contacts are remeasured from the deployed geometry, and the
ground spawn height clears the main tires before the aircraft settles onto its authored 2.3-degree
ground attitude. Weapon-bay cavity meshes stay fixed to the airframe, eliminating the large
rectangles that previously rotated with the bomb doors.

Version 0.4.49 restores the four main landing-gear door-linkage meshes that the 0.4.48 build
silently omitted by falling back to the pre-restoration 0.4.46 Blender source. The exporter now
uses the user-approved 0.4.47 cleaned source and fails instead of producing an incomplete model if
any restored mesh is absent. It also synchronizes the bundle and plugin metadata. This internal
build exposed that the restored linkages cannot share the strut's complete fold timeline: the
source geometry moves as much as 0.83 m relative to its outer door.

Version 0.4.50 reproduces the original model's landing-gear sequence instead of assigning every
panel to the game's generic outer-door animation. The inner main doors now move continuously with
their struts. The outer doors stay open through strut travel and close afterward. Each restored
outer-door linkage has separate 17-sample, source-derived tracks for those two stages, parameterized
by the source door's measured angle rather than guessed timing. The nose door remains on the native
outer-door sequence. The build contract independently requires all base linkage poses and both
staged linkage tracks, preventing either silent mesh loss or a return to the incorrect sequencing.

Version 0.4.51 removes the borrowed stock HUD's incorrect AFTERBURNER indication. The operational
F-117A used non-afterburning F404-GE-F1D2 engines, and the aircraft already serialized no afterburner
effects or supplemental thrust. The runtime change disables only the F-117 throttle gauge's visual
afterburner flag. Both engines retain their full 0-to-1 throttle mapping and 47,150 N dry thrust.

Version 0.4.52 corrects the signed landing-gear fold direction. Unity returned the source stow
rotation in its equivalent long-angle form; the authoring helper incorrectly negated both that
angle and its axis, producing the inverse rotation while preserving the expected unsigned angle.
The nose and main gear therefore folded aft instead of following the original model forward into
their wells. Angle-axis normalization now reverses the axis exactly once, and validation checks the
simulated stowed transform against the source target—not merely its angle magnitude.

Version 0.4.53 implements the game's directly supported low-observable systems as one coherent
profile. The clean aircraft has a small nonzero 0.0001 radar return. Each weapon bay independently
adds up to 0.04 in proportion to its native door animation, and landing gear progressively adds up
to 0.05 from the native fold state; enclosed stores keep their own signatures shielded until
release. The inherited emitting search radar is removed, while the passive RWR and native optical
tactical-screen path remain. EOTS detects optically to 15 km at 3x magnification, aircraft optical
visibility is 2.5 km, the two forward-aligned dry-engine IR sources range from 0.5 at idle to 2.2
at full power, and the aircraft carries 16 flares. It retains native chaff, shared friendly contacts,
no afterburner, and no permanent vapor/contrail components. Aspect-dependent RCS
and special damage/fire IR changes are intentionally excluded because the stock systems do not
provide those behaviors without custom detection patches.

Version 0.4.54 corrects the bomb-bay mechanical struts at their source. The previous exporter
welded two moving linkages into each rigid door even though the original animation rotates those
parts approximately 91 degrees relative to the panel. All four struts are now separate meshes with
nine source-derived, door-angle-parameterized poses covering closed through fully open. The runtime
follows the native BayDoor open amount, while the panels remain on their existing native hinges.
Offline rigid-fit error remains below one micrometre, and validation requires two independent
linkages per door, every pose locator, and greater-than-80-degree endpoint travel.

Version 0.4.55 completes the non-afterburning throttle-display correction by matching the stock
fixed-wing HUD contract used by the SFB and trainer aircraft. The borrowed fighter HUD contained a
serialized AFTERBURNER region in addition to its afterburner flag; disabling only the flag therefore
left the region able to override the ordinary percentage display. The F-117 gauge now has the same
configuration as those OEM dry-thrust aircraft: afterburner disabled, no throttle regions, and no
afterburner boundary marker. This is a display-only correction; both engines retain their full
0-to-1 throttle mapping and 47,150 N dry thrust.

Version 0.4.56 fixes all three physical cockpit displays at the model-data source. The native
Cricket tactical-screen prefab, renderer material, render texture, optical/radar presentation, and
target-camera integration were already initializing, but the marketplace cockpit mesh assigned
every MFD vertex the same `(0,0)` UV coordinate. All three screens consequently sampled one pixel
and appeared blank. Each of the three disconnected display surfaces now independently maps the
complete native 0-to-1 screen texture, so they present the stock tactical and target-camera feed.
Build validation now requires exactly three display islands and full UV coverage on every island.

Version 0.4.57 corrects the blue canopy at the model and material sources. The canopy mesh retained
six unrelated opaque cockpit materials plus two metallic, environment-reflective tinted glass
materials, so the solid frame did not consistently match the aircraft and the windows reflected the
blue sky like painted panels. All solid canopy faces now resolve to the exact same black material as
the cockpit frame. Only the two window groups remain glass; both are neutral, nearly clear,
non-metallic, texture-free, and excluded from environment reflections. Build validation requires
the imported canopy to resolve to one shared black frame group and exactly two clear-window groups.

Version 0.4.58 fixes the takeoff control discontinuity shown by the flight-data capture. The F-117
inherited the `ControlsFilter` defaults that bypass the complete fly-by-wire controller below 25 m/s
or 1 m radar altitude. During rotation, crossing 1 m abruptly handed the aircraft from raw input to
an already-active PID controller; in the recorded failure the same full pitch command changed from
-1.00 to -0.43 in one sample and then to -0.93. The controller now owns the aircraft continuously
from spawn, matching the working Shrike and FS-41 configuration (`minSpeed = 0`, `minAlt = 0`). The
measured tire contacts and the model's intentional 2.3-degree ground attitude remain unchanged.

Version 0.4.59 separates the F-117A's landing drag chute from the donor HUD's fictional AIRBRAKE
state. The throttle display no longer advertises an in-flight speedbrake that the aircraft and model
do not contain. The chute is now armed only by a main-gear touchdown transition after sustained
airborne evidence. Deployment then requires locked gear, positive load on all three wheels, a
settled vertical speed, runway alignment, a held wheel-brake command, and no more than the documented
215-knot deployment limit. It jettisons near the documented 20-knot ground speed. If the aircraft
slows to taxi speed without deploying it, that landing window closes until another real airborne and
touchdown cycle, preventing the chute from releasing during later taxi braking or turns.

Version 0.4.60 gives radar chaff a bounded silver-glint particle cloud in addition to its existing
native guidance-deception behavior. The previous invisible renderer fallback is removed and contract
validation requires a real material-backed effect.

Version 0.4.61 corrects the onboard jammer integration. The earlier build retained `RadarJammer`,
which is the game's defensive countermeasure and is not the target-fired weapon. That component is
now removed. Every F-117 loadout instead receives the unchanged vanilla `JammingPod1` WeaponMount.
Its native name, description, icon, targeting, power draw, range falloff, effectiveness, and firing
behavior are not overridden. Select it through the normal weapon controls, designate a tracked radar
target, and hold the normal fire control to jam. The mount is installed on an internal fixed station,
so it appears in the equipped-weapon cycle but is not presented as a removable loadout choice. Old
cached loadouts, imported missions, and networked loadout assignments are normalized to retain it.
Only the external pod's mesh, collider, drag, RCS, and duplicate structural mass are suppressed for
the internal installation; the vanilla jammer logic and data remain untouched. The aircraft retains
its 300 kJ electrical bus and stock-equivalent two-engine generation rate, so the native jammer draw
remains burst-limited and requires recovery time.

Version 0.4.62 corrects the three cockpit displays without replacing or modifying the game's native
feed. The stock Cricket tactical display is a 1024-by-512 atlas containing separate camera/radar,
basic-flight, and engine-instrument regions; the earlier full-texture mapping crushed that entire
atlas onto every physical screen. The large center MFD now maps the native camera/radar region, the
left MFD maps the native basic-flight instruments, and the right MFD maps the native engine
instruments. Each region is cropped to the modeled panel's aspect ratio rather than stretched.
Runtime setup also restores the intended transparent render state of the HUD combiner after the
launcher replaces its placeholder shader, preventing its thin glass edge from becoming an opaque
vertical obstruction.

Version 0.4.63 fixes internal AGM-48 release and aircraft durability at their actual configuration
points. The stock `AGM1_quad_internal` rack is intended for a forward-clear bay and uniquely has no
rail travel; on the F-117 it spawned missiles inside the fuselage. Only the F-117's cloned rack now
uses the unchanged stock heavy-AGM motion of 2 m downward at 4 m/s, placing the live missile below
the aircraft as the doors finish opening. The aircraft's 13 AeroParts now use the common stock
FastBomber damage profile, standard 100 HP initialization, and a -25 structural margin. The center
body and engine AeroParts are no longer incorrect instant-kill components; normal pilot, engine,
fuel, structural, and system damage paths remain active.

Version 0.4.64 fixes the two side instrument screens being rotated 90 degrees clockwise. The center
camera/radar display was already correct and is left unchanged. Only the left basic-flight and right
engine-instrument UV islands now counter-rotate their stock atlas content by 90 degrees while
preserving each panel's aspect ratio. Build validation now checks the direction of both UV axes, so a
correct atlas rectangle with a rotated image can no longer pass.

Version 0.4.65 corrects the direction of that side-screen adjustment. Version 0.4.64 sampled the two
rotated instrument regions in the same visual direction as their existing clockwise rotation, adding
another 90 degrees and displaying them upside down. The side UV axes now use the actual inverse
mapping; the already-correct center camera/radar island remains byte-for-byte unchanged.
