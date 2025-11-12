# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a C# vegetation phenology and carbon flux modeling system implementing the SWELL (Seasonal Woody-Ecosystem Leaf-out and Leaf-loss) model with carbon exchange computations. The model simulates plant dormancy cycles, growth phases, vegetation index dynamics, and photosynthesis/respiration processes using a two-layer canopy approach.

**Target Framework**: .NET 8.0
**Integration**: vvvv visual programming environment (via `vvvvInterface` class in [source/utils.cs:616-996](source/utils.cs#L616-L996))

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
- Daily weather inputs: PAR, temperature (min/max), dew point, precipitation, solar radiation, wind speed, relative humidity (min/max), latitude
- Hourly arrays (24 elements): `airTemperatureH[]`, `solarRadiationH[]`, `precipitationH[]`, `relativeHumidityH[]`, `vaporPressureDeficitH[]`, `referenceET0H[]`, `soilTemperatureH[]`, `windSpeedH[]`
- `radData` nested object: day length, solar radiation, sunrise/sunset times
- Hourly data automatically disaggregated from daily values in `vvvvInterface.estimateHourly()`

**Parameters Layer** ([source/data/parameters.cs](source/data/parameters.cs))
- Nested parameter classes for each phenophase (e.g., `parGrowth`, `parEndodormancy`)
- **Split carbon exchange parameters**:
  - `parPhotosynthesis`: quantum yields, temperature thresholds, PAR half-saturation, VPD parameters, water stress, light extinction coefficient
  - `parRespiration`: activation energy parameters (soil/over/under), reference respiration rates, GPP response coefficients, aging factor, smoothing factor
- Vegetation index parameters: minimum/maximum EVI/NDVI, rate constants per phase

**Output Layer** ([source/data/output.cs](source/data/output.cs))
- Phenophase state variables (e.g., `growth.growthState`, `endodormancy.endodormancyState`)
- Completion percentages and boolean flags (e.g., `isGrowthCompleted`)
- Vegetation index tracking (`vi`, `viRate`, `viAtGrowth`, `viAtSenescence`, `viAtGreendown`)
- Carbon flux outputs in `exchanges` class:
  - Hourly lists: GPP (over/under/total), RECO (over/under/hetero/total), NEE, temperature scales, PAR scales, water stress, VPD scale, phenology scale
  - Daily sums: `gppDaily`, `recoDaily`, `neeDaily`
  - Canopy structure: LAI (overstory/understory), EVI (overstory/understory), vegetation cover, light interception

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
- Understory dynamics driven by temperature when dormant, thermal forcing during growth

**[source/functions/exchanges/exchanges.cs](source/functions/exchanges/exchanges.cs)**
- `VPRM()`: Hourly carbon flux computation (Vegetation Photosynthesis Respiration Model)
- **Two-layer canopy** with separate calculations for overstory and understory
- **Light partitioning** (moved outside hourly loop for efficiency):
  - Direct/diffuse radiation partitioning (Erbs et al. 1982)
  - Overstory light interception using Beer-Lambert with extinction coefficients
  - Understory receives transmitted light through overstory gaps
  - Light interception coefficients pre-computed once per day
- **GPP calculation**:
  - Separate for overstory (phenology-dependent) and understory (always active)
  - Uses **minimum limiting factor** approach: `min(Wscale, min(VPDmodifier, PARscale))` instead of multiplicative
  - Temperature scaler: Symmetric polynomial function (non-zero only between Tmin and Tmax)
  - Leaf temperature simplified to air temperature (hourly)
  - Understory has temperature shift via `pixelTemperatureShift` parameter
- **RECO calculation** (three components):
  - `estimateRECOtree()`: Overstory autotrophic respiration (GPP-dependent with aging function)
  - `estimateRECOunder()`: Understory autotrophic respiration (GPP-dependent)
  - `estimateRECOHetero()`: Heterotrophic soil respiration (temperature and water stress dependent)
  - Each component uses Lloyd-Taylor temperature response with separate activation energy parameters
  - **Smoothing filter**: Exponential moving average applied to tree and understory RECO to prevent discontinuities (`respirationSmoothingFactor`)
  - State variables `lastRecoTree` and `lastRecoUnder` persist across hours and days

**[source/utils.cs](source/utils.cs)**
- `astronomy()`: Solar geometry calculations (day length, extraterrestrial radiation, sunrise/sunset)
- `forcingUnitFunction()`: General thermal forcing function (Yan & Hunt 1999)
- `photoperiodFunctionInduction()`, `temperatureFunctionInduction()`: Sigmoid limiting functions for dormancy
- `endodormancyRate()`: Hourly chilling unit accumulation
- `ecodormancyRate()`: Photoperiod-modulated photothermal forcing
- `estimateLAI()`: EVI-to-LAI conversion for two canopy layers with temporal continuity
  - Overstory EVI = current VI - VI at growth start
  - Understory EVI tracks changes with vegetation cover weighting
- `PartitionRadiation()`: Direct/diffuse radiation partitioning (Erbs et al. 1982)
- `waterStressFunction()`: Rolling window precipitation/ET0 memory with NDVI normalization
- `VPDfunction()`: Sigmoid VPD response function
- `phenologyFunction()`: Logistic aging function during growth phase, constant during greendown/decline
- `temperatureFunction()`: Symmetric polynomial temperature response (from numerator/denominator formulation)
- `ComputeTscaleReco()`: Lloyd-Taylor temperature response with numerical safeguards
- `gppRecoTreeFunction()`, `gppRecoUnderFunction()`: GPP-dependent respiration functions with reference rates
- `RecoRespirationFunction()`: Phenological aging modifier for respiration (logistic function based on season progress)
- `vvvvInterface` class: Main execution wrapper for vvvv integration with hourly disaggregation

## Key Computational Patterns

### State Accumulation Pattern
All phenophases follow: `outputT1.phase.phaseState = outputT.phase.phaseState + outputT1.phase.phaseRate`
- `outputT`: Previous timestep (T-1) - passed as `output` parameter
- `outputT1`: Current timestep (T) - passed as second `output` parameter
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
Daily weather inputs → hourly arrays via sinusoidal interpolation:
- Temperature: `Tavg + DT/2 * cos(0.2618 * (h - 15))` (peak at hour 15)
- Solar radiation: Distributed proportional to extraterrestrial radiation, converted to W/m² via `gsrHourly[h] * 1269.44`
- Input conversion in VPRM: `solarRadiationH[h] * 277.78` (W/m² to MJ/m²/h)
- Relative humidity, VPD, ET0: Derived from temperature and radiation curves
- All hourly arrays stored directly in `input` object (not nested `hourlyData` object)

### Two-Layer Carbon Flux Architecture

**Pre-computation Phase** (outside hourly loop):
1. **LAI estimation**: Uses previous timestep (`outputT`) for temporal continuity in understory EVI calculation
2. **Light extinction setup**: Compute overstory and understory gap probabilities once per day
3. **Maximum temperature**: Extract from daily inputs for temperature response functions

**Hourly Loop**:
1. **Light partitioning**: Direct/diffuse → overstory absorption (Beer-Lambert) → transmitted to understory
2. **Temperature effects**: Use hourly air temperature directly (no energy balance model)
3. **Water stress**: Compute once per hour, add to rolling memory, shared by GPP and heterotrophic respiration
4. **Phenology scaler**: Logistic function of growth percentage (constant during other phases)
5. **VPD scaler**: Sigmoid response function
6. **GPP calculation**:
   - Overstory: `maxQuantumYield * Tscale * min(Wscale, min(VPD, PARscale)) * absorbedPAR * phenologyScale * EVI`
   - Understory: Same but no phenology scaling, uses shifted temperature optimum
7. **RECO calculation**: Three additive components with separate temperature responses and smoothing

### Parameter Structure Changes

**Photosynthesis parameters** (`parPhotosynthesis`):
- Quantum yields: `maximumQuantumYieldOver`, `maximumQuantumYieldUnder` (renamed from Tree/Under)
- Temperature: `minimumTemperature`, `optimumTemperature`, `maximumTemperature`, `pixelTemperatureShift`
- Light: `LightExtinctionCoefficient`, `halfSaturationTree`, `halfSaturationUnder`
- VPD: `vpdMin`, `vpdMax`, `vpdSensitivity`
- Water stress: `waterStressDays`, `waterStressThreshold`, `waterStressSensitivity`
- Phenology: `growthPhenologyScalingFactor`

**Respiration parameters** (`parRespiration`):
- Activation energies: `activationEnergyParameterSoil`, `activationEnergyParameterOver`, `activationEnergyParameterUnder`
- Reference rates: `referenceRespirationSoil`, `referenceRespirationOver`, `referenceRespirationUnder`
- GPP responses: `respirationResponseOver`, `respirationResponseUnder`
- Modifiers: `respirationAgingFactor`, `respirationSmoothingFactor`

## Critical Implementation Details

### Vegetation Index Behavior
- Stored scaled to 100 (multiply by 100 from 0-1 range)
- `viAtGrowth`: Captured at start of growth phase (dormant understory baseline)
- `viAtSenescence`: Captured at dormancy induction start
- `viAtGreendown`: Captured at start of decline phase
- Used to compute vegetation cover fraction and LAI estimates
- Understory EVI computation uses temporal continuity: `outputT.exchanges.EVIunderstory + deltaVi * (1 - vegetationCover)`

### Phenological Code Persistence
- Previous timestep output passed as first `output` parameter, current as second `output` parameter (named `outputT1`)
- Boolean flags (`isDormancyInduced`, `isEcodormancyCompleted`, etc.) prevent re-entry into completed phases
- State variables persist across days until reset by new phase entry

### Parameter Coupling
- Endodormancy completion scales ecodormancy asymptote: `asymptote = endodormancyPercentage / 100`
- Growth percentage modulates VI growth rate: `rate * (1 - growthPercentage/100)`
- Phenology percentage drives respiration aging: `1 / (1 + exp(10 * (seasonProgress - agingFactor)))`

### Respiration State Management
- `lastRecoTree` and `lastRecoUnder` fields in `exchanges` class persist across hours **and days**
- Smoothing formula: `reco = lastReco + alpha * (reco_raw - lastReco)` where alpha is `respirationSmoothingFactor`
- Prevents discontinuities when GPP drops rapidly (e.g., nighttime, cloudy conditions)

### Temperature Response Functions
- **GPP**: Symmetric polynomial with zero outside [Tmin, Tmax]: `numerator / denominator` where numerator = `(T - Tmin) * (T - Tmax)`
- **RECO**: Lloyd-Taylor exponential with safeguards against extreme temperatures and overflow
- Understory uses temperature shift for optimum: `Topt - pixelTemperatureShift`

### GPP Limiting Factor Approach
Changed from multiplicative to **minimum limiting factor**:
- Old: `VPD * Wscale * PARscale * ...`
- New: `min(Wscale, min(VPD, PARscale)) * ...`
- Represents co-limitation where most limiting factor dominates

## vvvv Integration Notes

The `vvvvInterface` class ([source/utils.cs:616-996](source/utils.cs#L616-L996)) provides the execution wrapper:

```csharp
public output vvvvExecution(input input, parameters parameters)
```

**Workflow**:
1. Hourly disaggregation from daily inputs via `estimateHourly()` (modifies input object in place)
2. Swap previous day's output: `outputT0 = outputT1; outputT1 = new output()`
3. Execute phenology sequence: dormancy (induction → endo → eco) → growth phases → VI dynamics
4. Execute carbon exchanges (VPRM)
5. Return updated `outputT1` with all state variables

**State Management**: `outputT0` and `outputT1` swapped each call to maintain temporal continuity.

## Important Constants & Thresholds

- Solar constant: 4.921 MJ m⁻² h⁻¹
- PAR fraction of shortwave: 50.5% × 4.57 µmol/J
- Solar radiation conversion: 1269.44 (MJ to W/m² for hourly) then 277.78 (W/m² to MJ/m²/h in VPRM)
- Latitude validity for day length: -65° to 65°
- Reference temperature for Lloyd-Taylor: 288.15 K (15°C)
- Temperature offset for Lloyd-Taylor: 227.13 K
- Minimum/maximum VI bounds prevent undershoots/overshoots
- Diffuse extinction coefficient: 0.8 × beam extinction coefficient

## Testing & Parameter Files

**Example parameters**: [vvvv/example_parameters.csv](vvvv/example_parameters.csv) (CSV format for parameter loading)
**Data directory**: [source/data/](source/data/) (contains gitignored data files)
**Excluded files**: `parameters_ok.cs`, `utils_old.cs` (removed from compilation in .csproj)

When modifying parameter classes, ensure CSV parameter file structure matches new fields, especially the split between `parPhotosynthesis` and `parRespiration`.
