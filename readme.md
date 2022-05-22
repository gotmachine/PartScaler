# PartScaler

This plugin is a continuation of the TweakScale mod for KSP.
It is based on the [original work from Pelinor](https://github.com/pellinor0/TweakScale), itself a continuation of the work from Goodspeed and Biotronic.

### Stuff to do

- Remove the SCALETYPE thing
- Allow setting the scale mode directly (m or %)
- Allow arbitrary scale instead of discrete values, use a global preset for stack sizes
- Allow defining min/max scale on a per-part basis
- Autodetect default scale mode (m/%) and default size with some basic attach node and bounding box heuristics
- Global PartModule blacklist
- Don't use prefab values anymore and persist scaled values instead :
  - In flight, store scaled values
  - In the editor, also store original unscaled values
- Refactor exponent definitions :
  - Separate PART, MODULE, RESOURCE and ATTACHNODE definitions
  - Per definition module blacklist
  - Top-level definitions can be overriden by in scaler module local definitions
  - Per scaler module exponent definition blacklist
  - Those are instantiated as Exponent base class derivatives, which can also implement custom behavior from code
  - Exponents are serialized/persisted on the module to ensure config changes don't break existing vessels/crafts

### Things that we might want to backport or implement :
- CrewCapacity scaling
- Prevent downscaling to ridiculous masses
- 