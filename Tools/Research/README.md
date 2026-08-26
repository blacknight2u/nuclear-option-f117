# Source-model research

This directory contains model-inspection utilities and source inputs used to
maintain the production aircraft. It is not executed during a release.

- `SourceModel` contains read-only hierarchy, material, animation, rig, neutral
  pose, gear, canopy, cockpit, and geometry investigations.

The scripts document important source-object names and animation frames that
would be expensive to rediscover. They may depend on historical local files or
paths. Promote any reusable behavior into `Tools/Export` or `Tools/Audits` and
give it command-line inputs before treating it as supported tooling.
