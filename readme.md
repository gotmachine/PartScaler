# PartResizer

This plugin is a continuation of the TweakScale mod for KSP.
It is (very) loosly based on the [original work from Pelinor](https://github.com/pellinor0/TweakScale), itself a continuation of the work from Goodspeed and Biotronic.

### Stuff to do

- Remove the SCALETYPE thing
- Always allow arbitrary scaling (%), stack presets are optional
- Autodetect stack size with some basic attach rules and bounding box heuristics, allow config-defined override
- Allow defining min/max scale on a per-part basis
- Don't use prefab values anymore and persist scaled values instead :
  - In flight, store scaled values
  - In the editor, also store original unscaled values
- Refactor exponent definitions :
  - Separate PART, MODULE, RESOURCE and ATTACHNODE definitions
  - Top level resource blacklist (those resources are never scaled)
  - MODULE definitions can define PART overrides
  - Per definition module blacklist (don't apply the definition if module X is present)
  - Top-level definitions can be overriden by in-module local definitions
  - Those are instantiated as Exponent base class derivatives, which can also implement custom behavior from code
  - Exponents are serialized/persisted on the module to ensure config changes don't break existing vessels/crafts

```
RESIZER_PART_EXPONENT
{
   massExponent = 2 // dry mass exponent
   costExponent = 2 // base part cost exponent

  // field exponents (compact notation)
  // always applies absolute scaling
  EXPONENTS
  {
    breakingForce = 2
    breakingTorque 2
  }

  // field exponent (detailed notation)
  EXPONENT
  {
    name = buoyancy 
    exponent = 2
    useRelativeScaling = true
  }
}

RESIZER_MODULE_EXPONENT
{
  name = ModuleEngine // module class name
  includeDerivatives = true // are derived classes included
  massExponent = 2 // module mass modifier exponent
  costExponent = 2 // module cost modifier exponent
  
  RESIZER_PART_EXPONENT
  {
      EXPONENTS
      {
        mass = 2
        breakingForce = 2
        breakingTorque 2
      }
  }

  EXPONENTS
  {
    maxThrust = 2.5
  }
}

RESIZER_RESOURCE_EXPONENT
{
  
}
```

### Things that we might want :
- CrewCapacity scaling
- Prevent downscaling to ridiculous masses

### Modding ecosystem review

#### B9PartSwitch
- Supports scaling out of the box
- The module has a `scale` field set through the normal module field editing mechanism
- Not sure how a change is detected, B9PS doesn't seem to have an `onEditorShipModified` callback
- Scaling *seems* to work right now.
- B9PS can move AttachNodes. Not sure how this will interact.

#### ConfigurableContainers
- Implements the `OnPartScaleChanged` callback
- Handle resources on its own

#### ModularFuelSystem
- Implements the `OnPartScaleChanged` callback
- Also implements the `IRescalable` interface

#### Firespitter
- Seems support is/was on tweakscale side

#### Other fuel switchers
- SimpleFuelSwitch ?

#### Mods implementating the `IRescalable` interface
- OnDemandFuelCells
- InfernalRobotics (the old one, not sure about the new one)
- IFS
- Various stuff in KSPIE
- FAR

