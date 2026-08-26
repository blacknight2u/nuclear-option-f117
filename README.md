# F-117A Nighthawk for Nuclear Option

A flyable F-117A stealth attack aircraft for Nuclear Option, built with
Blueprinter and a small aircraft-scoped BepInEx plugin.

The mod adds a new aircraft; it does not replace a stock airframe.

## Features

- Subsonic flight model with twin non-afterburning engines
- Animated tricycle landing gear, canopy, bomb bays, and drag chute
- Dynamic radar signature that increases when the landing gear or weapon bays
  are open
- Passive warning and targeting systems with game-native cockpit displays
- Internal weapon carriage with compatible bomb and missile loadouts
- Fixed onboard game-native jammer, chaff, and flares
- Multiplayer support when every player uses matching game, Blueprinter, and
  F-117 versions

The aircraft favors stealth and precision attack over speed or air-to-air
performance. It has no emitting search radar or afterburner.

## Requirements

- Nuclear Option `0.34.1`
- Blueprinter `1.8.21`
- NOMM for normal installation and mod management

## Installation

Install F-117A Nighthawk through NOMM when it is available in Discover. For a
local build, import the provided `.nommpack` through NOMM, then enable both
Blueprinter and F-117A Nighthawk before launching the game.

Do not copy the development source tree into the game directory.

## Aircraft controls

Normal flight, weapons, landing gear, countermeasures, and targeting use the
standard Nuclear Option controls.

The onboard jammer appears in the equipped-weapon cycle. Select it, designate a
tracked radar target, and hold the normal fire control to jam. It uses the
game's standard jammer behavior. Its dedicated 60 kJ capacitor supports about
five seconds of strong jamming from full charge and takes about 52 seconds to
recover at maximum engine RPM, so save it for critical moments.

The drag chute becomes available after a real airborne landing. With the gear
locked down and the aircraft settled on all three wheels, hold the wheel brakes
within the chute's landing-speed envelope. It jettisons automatically near taxi
speed.

## Reporting problems

Include the game, Blueprinter, and F-117 versions, the enabled mod list, and a
short reproduction. Attach `Player.log` from:

```text
%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption\Player.log
```

The optional Flight Data Logger can provide structured telemetry for flight,
landing-gear, damage, or performance problems.

## Development

- [Development and release process](DEVELOPMENT.md)
- [Aircraft modding guide](AIRCRAFT_MODDING_GUIDE.md)
- [Maintained and historical tools](Tools/README.md)
