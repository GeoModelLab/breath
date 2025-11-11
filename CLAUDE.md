# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# vegetation phenology and carbon flux modeling system implementing the SWELL (Seasonal Woody-Ecosystem Leaf-out and Leaf-loss) model with carbon exchange computations. The model simulates plant dormancy cycles, growth phases, vegetation index dynamics, and photosynthesis/respiration processes.

**Target Framework**: .NET 8.0
**Integration**: vvvv visual programming environment (via `vvvvInterface` class in [utils.cs:786-998](source/utils.cs#L786-L998))

## Build & Run

### Build the project
```bash
dotnet build source/source.csproj
```

### Build release version
```bash
dotnet build source/source.csproj -c Release
```

## Architecture

### Core Model Structure

The model operates as a **daily timestep state machine** progressing through vegetation phenological phases:

1. **Dormancy Induction** (phenoCode 1) → photoperiod/temperature triggers entry into dormancy
2. **Endo/Ecodormancy** (phenoCode 2) → chilling accumulation and photothermal forcing for dormancy release
3. **Growth** (phenoCode 3) → leaf expansion driven by thermal units
4. **Greendown** (phenoCode 4) → peak greenness maintenance period
5. **Decline/Senescence** (phenoCode 5) → photoperiod/temperature-driven leaf senescence

### Three-Layer Data Architecture

**Input Layer** ([source/data/input.cs](source/data/input.cs))
- Daily weather inputs: PAR, temperature (min/max), dew point, precipitation, latitude
- `radData` nested object: day length, solar radiation, sunrise/sunset times
- `hourlyData` nested object: automatically disaggregated from daily values

**Parameters Layer** ([source/data/parameters.cs](source/data/parameters.cs))
- Nested parameter classes for each phenophase (e.g., `parGrowth`, `parEndodormancy`)
- Carbon exchange parameters (`parExchanges`): quantum yields, temperature thresholds, respiration coefficients
- Vegetation index parameters: minimum/maximum EVI/NDVI, rate constants per phase

**Output Layer** ([source/data/output.cs](source/data/output.cs))
- Phenophase state variables (e.g., `growth.growthState`, `endodormancy.endodormancyState`)
- Completion percentages and boolean flags (e.g., `isGrowthCompleted`)
- Vegetation index tracking (`vi`, `viRate`, `viAtGrowth`, `viAtSenescence`)
- Carbon flux outputs in `exchanges` class: hourly GPP/RECO/NEE lists, daily sums, LAI, light partitioning

### Functional Modules

**[source/functions/phenology/dormancySeason.cs](source/functions/phenology/dormancySeason.cs)**
- `induction()`: Photoperiod × temperature dormancy induction (sigmoid functions)
- `endodormancy()`: Hourly chilling accumulation (Utah model variant)
- `ecodormancy()`: Photothermal forcing for dormancy release (asymptote modulated by chilling completion)

**[source/functions/phenology/growingSeason.cs](source/functions/phenology/growingSeason.cs)**
- `growthRate()`: Thermal forcing function (Yan & Hunt 1999)
- `greendownRate()`: Peak greenness maintenance
- `declineRate()`: Weighted thermal + photothermal senescence rate

**[source/functions/NDVIdynamics.cs](source/functions/NDVIdynamics.cs)**
- `ndviNormalized()`: Translates phenological state to vegetation index dynamics
- Phase-specific VI rate constants applied to state completion percentages
- Critical VI thresholds stored at phase transitions (`viAtGrowth`, `viAtSenescence`, `viAtGreendown`)

**[source/functions/exchanges/exchanges.cs](source/functions/exchanges/exchanges.cs)**
- `VPRM()`: Hourly carbon flux computation (Vegetation Photosynthesis Respiration Model)
- Two-layer canopy (overstory/understory) with separate LAI, light attenuation, GPP calculations
- GPP scalers: PAR (rectangular hyperbola), temperature (symmetric polynomial), VPD (sigmoid), water stress (EVI-based NDVI/ET0 memory), phenology (logistic aging function)
- RECO: GPP-dependent respiration with temperature (Lloyd-Taylor) and water stress modulation, nighttime smoothing filter

**[source/utils.cs](source/utils.cs)**
- `astronomy()`: Solar geometry calculations (day length, extraterrestrial radiation, sunrise/sunset)
- `forcingUnitFunction()`: General thermal forcing function (Yan & Hunt 1999)
- `photoperiodFunctionInduction()`, `temperatureFunctionInduction()`: Sigmoid limiting functions for dormancy
- `endodormancyRate()`: Hourly chilling unit accumulation
- `ecodormancyRate()`: Photoperiod-modulated photothermal forcing
- `estimateLAI()`: EVI-to-LAI conversion for two canopy layers
- `PartitionRadiation()`: Direct/diffuse radiation partitioning (Erbs et al. 1982)
- `LeavesTemperature()`: Energy balance leaf temperature model (sensible + latent + radiative fluxes)
- `waterStressFunction()`: Rolling window precipitation/ET0 memory with NDVI normalization
- `VPDfunction()`, `phenologyFunction()`, `temperatureRecoFunction()`, `gppRecoFunction()`: Carbon flux scalers
- `vvvvInterface` class: Main execution wrapper for vvvv integration with hourly disaggregation

## Key Computational Patterns

### State Accumulation Pattern
All phenophases follow: `outputT1.phase.phaseState = output.phase.phaseState + outputT1.phase.phaseRate`
- `output`: Previous timestep (T-1)
- `outputT1`: Current timestep (T)
- Rate computed from environmental drivers → accumulated to state → compared to threshold → phase transition

### Phase Transition Logic
Transitions triggered by percentage completion:
```csharp
phasePercentage = phaseState / threshold * 100
if (phasePercentage >= 100) {
    isPhaseCompleted = true;
    phenoCode = nextPhase;
}
```

### Hourly Disaggregation
Daily weather inputs → hourly values via sinusoidal interpolation:
- Temperature: `Tavg + DT/2 * cos(0.2618 * (h - 14))` (peak at hour 14)
- Solar radiation: Distributed proportional to extraterrestrial radiation
- Relative humidity, VPD, ET0: Derived from temperature and radiation curves

### Two-Layer Carbon Flux
Overstory and understory handled separately:
1. **Light partitioning**: Direct/diffuse → overstory absorption (Beer-Lambert) → transmitted to understory
2. **GPP calculation**: Layer-specific LAI, quantum yield, temperature optima, PAR scalers
3. **Respiration**: Overstory aging function modulates GPP-dependent RECO term

## Critical Implementation Details

### Vegetation Index Behavior
- Stored scaled to 100 (multiply by 100 from 0-1 range)
- `viAtGrowth`: Captured at start of growth phase (dormant understory baseline)
- `viAtSenescence`: Captured at dormancy induction start
- `viAtGreendown`: Captured at start of decline phase
- Used to compute vegetation cover fraction and LAI estimates

### Phenological Code Persistence
- Previous timestep output passed as `output` parameter, current as `outputT1`
- Boolean flags (`isDormancyInduced`, `isEcodormancyCompleted`, etc.) prevent re-entry into completed phases
- State variables persist across days until reset by new phase entry

### Parameter Coupling
- Endodormancy completion scales ecodormancy asymptote: `asymptote = endodormancyPercentage / 100`
- Growth percentage modulates VI growth rate: `rate * (1 - growthPercentage/100)`
- Phenology percentage drives respiration aging: `1 / (1 + exp(10 * (seasonProgress - agingFactor)))`

### Hourly Carbon Flux Initialization
- Lists cleared implicitly via new `output()` object each day
- Previous RECO state persisted via `prevRecoState` field for nighttime smoothing
- Memory lists for water stress (precipitation/ET0) maintained with rolling window removal

## vvvv Integration Notes

The `vvvvInterface` class ([utils.cs:786-998](source/utils.cs#L786-L998)) provides the execution wrapper:

```csharp
public output vvvvExecution(input input, parameters parameters)
```

**Workflow**:
1. Hourly disaggregation from daily inputs
2. Pass previous day's output (`outputT0`) to current timestep methods
3. Execute phenology sequence: dormancy → growth → VI dynamics
4. Execute carbon exchanges (VPRM)
5. Return updated `outputT1` with all state variables

**State Management**: `outputT0` and `outputT1` swapped each call to maintain temporal continuity.

## Important Constants & Thresholds

- Solar constant: 4.921 MJ m⁻² h⁻¹
- PAR fraction of shortwave: 50.5% × 4.57 µmol/J
- Latitude validity for day length: -65° to 65°
- Reference temperature for respiration: 288.15 K (15°C)
- Minimum/maximum VI bounds prevent undershoots/overshoots

## Testing & Parameter Files

**Example parameters**: [vvvv/example_parameters.csv](vvvv/example_parameters.csv) (CSV format for parameter loading)
**Data directory**: [source/data/](source/data/) (ignore pattern in .gitignore)

When modifying parameter classes, ensure CSV parameter file structure matches new fields.
