# Fidelity Contract: Atmospherics System

## Core Architecture
The Atmospherics system in Unitystation is NOT a true fluid dynamics simulation. It is a discrete, queue-based cellular automaton that relies on instant equalization rather than pressure waves. The Godot port MUST preserve this specific behavior, as it defines the "SS13 feel".

## Rule 1: The Ideal Gas Law
All pressure calculations MUST strictly follow the ideal gas law: `P = nRT/V`.
- **n**: Total moles of all gases in the mix
- **R**: 8.314f (Ideal Gas Constant)
- **T**: Temperature in Kelvin
- **V**: Cell volume (fixed at 2500L)

## Rule 2: Instant Equalization
When an open tile has a pressure or mole differential with a neighbor greater than `MIN_PRESSURE_DIFFERENCE` (0.1f), the simulation does NOT diffuse gas slowly. It instantly calculates the mean gas mix of the tile and all its open neighbors, and copies that mean to all involved tiles.

## Rule 3: Space Venting
Space tiles participate in the mean calculation (drawing gas out of the room), but immediately after equalization, space tiles execute `SetToEmpty()`, destroying the gas and dropping their temperature to `SPACE_TEMPERATURE` (2.7K).

## Rule 4: Energy Conservation During Transfer
When gas is transferred between mixes, the temperature does not average linearly. It blends based on the `WholeHeatCapacity` of the gases involved. Each gas has a specific molar heat capacity (e.g., Oxygen = 20, Plasma = 200).

## Rule 5: Solid Conductivity
Heat transfer between solid objects (walls) or between gas and walls is calculated entirely separately from gas equalization. It requires a starting temperature of at least 693.15K to begin, and stops if the temperature drops below 373.15K.

## Rule 6: Reaction Oppression
If a tile contains 5 or more moles of HyperNoblium, ALL chemical reactions on that tile are completely suppressed, regardless of temperature or pressure.

## Implementation Boundary
- `Project.Common` MUST implement the pure math of `GasMix`, `AtmosTile`, and `AtmosSimulation`.
- `game/` (Godot) MUST implement the TileMap overlay system, reading the `GasMix` from `Project.Common` to display the correct color overlays and ice effects.
