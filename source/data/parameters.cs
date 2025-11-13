using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// ============================================================================
// Parameters Layer - SWELL Model Calibration Parameters
// ============================================================================
// This namespace contains all calibration parameters for the SWELL model.
//
// PARAMETER STRUCTURE:
// - Phenology parameters: Control dormancy cycles and growth phases
// - Vegetation index parameters: Control VI dynamics during each phase
// - Carbon exchange parameters: Split into photosynthesis and respiration
//
// USAGE:
// Parameters are typically loaded from CSV files (see vvvv/example_parameters.csv)
// and passed to phenology and carbon flux functions.
//
// IMPORTANT: Parameter changes require corresponding CSV file updates for
// consistency with parameter loading routines.
// ============================================================================

namespace source.data
{
    /// <summary>
    /// Master parameter container for SWELL model.
    /// Aggregates all phenophase-specific and process-specific parameter classes.
    ///
    /// PARAMETER CATEGORIES:
    /// 1. Phenology: Dormancy induction, endo/ecodormancy, growth, greendown, senescence
    /// 2. Vegetation Index: VI dynamics per phenophase
    /// 3. Carbon Exchange: Photosynthesis and respiration (split architecture)
    ///
    /// CSV LOADING:
    /// Parameter values loaded from external CSV files must match this structure.
    /// See vvvv/example_parameters.csv for template.
    /// </summary>
    public class parameters
    {
        /// <summary>Dormancy induction phase parameters (phenoCode 1)</summary>
        public parDormancyInduction parDormancyInduction = new parDormancyInduction();

        /// <summary>Endodormancy phase parameters (phenoCode 2, chilling accumulation)</summary>
        public parEndodormancy parEndodormancy = new parEndodormancy();

        /// <summary>Ecodormancy phase parameters (phenoCode 2, photothermal forcing)</summary>
        public parEcodormancy parEcodormancy = new parEcodormancy();

        /// <summary>Growth phase parameters (phenoCode 3, leaf expansion)</summary>
        public parGrowth parGrowth = new parGrowth();

        /// <summary>Greendown phase parameters (phenoCode 4, peak greenness maintenance)</summary>
        public parGreendown parGreendown = new parGreendown();

        /// <summary>Senescence/decline phase parameters (phenoCode 5, leaf senescence)</summary>
        public parSenescence parSenescence = new parSenescence();

        /// <summary>Vegetation index dynamics parameters (all phenophases)</summary>
        public parVegetationIndex parVegetationIndex = new parVegetationIndex();

        /// <summary>Photosynthesis parameters (carbon uptake, GPP calculation)</summary>
        public parPhotosynthesis parPhotosynthesis = new parPhotosynthesis();

        /// <summary>Respiration parameters (carbon release, RECO calculation)</summary>
        public parRespiration parRespiration = new parRespiration();
    }

    /// <summary>
    /// Dormancy induction phase parameters (phenoCode 1).
    /// Controls entry into dormancy via photoperiod and temperature signals.
    ///
    /// MECHANISM:
    /// Dormancy induction follows multiplicative photothermal model:
    /// Rate = photoperiodFunction(dayLength) × temperatureFunction(Tair)
    ///
    /// Both functions are sigmoid (0 to 1 range):
    /// - Short days (below limitingPhotoperiod) promote dormancy
    /// - Cool temperatures (below limitingTemperature) promote dormancy
    ///
    /// THRESHOLD:
    /// Accumulated photothermal units must exceed photoThermalThreshold
    /// to complete induction and transition to endodormancy (phenoCode 2).
    ///
    /// IMPLEMENTATION:
    /// See dormancySeason.induction() in functions/phenology/dormancySeason.cs
    /// </summary>
    public class parDormancyInduction
    {
        /// <summary>
        /// Day length threshold where photoperiod starts limiting dormancy induction.
        /// Units: hours
        /// At this photoperiod, sigmoid function = 0.5 (inflection point).
        /// Shorter days → stronger induction signal (approach 1.0).
        /// </summary>
        public float limitingPhotoperiod { get; set; }

        /// <summary>
        /// Day length threshold where photoperiod no longer limits induction.
        /// Units: hours
        /// Below this photoperiod, sigmoid function → 1.0 (maximum induction).
        /// Defines lower bound of photoperiod sensitivity window.
        /// </summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>
        /// Accumulated photothermal units required to complete dormancy induction.
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Upon reaching threshold, isDormancyInduced flag set and phenoCode → 2.
        /// </summary>
        public float photoThermalThreshold { get; set; }

        /// <summary>
        /// Temperature threshold where temperature starts limiting dormancy induction.
        /// Units: °C
        /// At this temperature, sigmoid function = 0.5 (inflection point).
        /// Lower temperatures → stronger induction signal (approach 1.0).
        /// </summary>
        public float limitingTemperature { get; set; }

        /// <summary>
        /// Temperature threshold where temperature no longer limits induction.
        /// Units: °C
        /// Below this temperature, sigmoid function → 1.0 (maximum induction).
        /// Defines lower bound of temperature sensitivity window.
        /// </summary>
        public float notLimitingTemperature { get; set; }
    }

    /// <summary>
    /// Endodormancy phase parameters (phenoCode 2, first sub-phase).
    /// Controls chilling accumulation during deep dormancy.
    ///
    /// MECHANISM:
    /// Chilling accumulation follows Utah model variant with hourly integration.
    /// Chilling efficiency function peaks between notLimitingLower and notLimitingUpper
    /// temperatures, declining toward zero at limiting thresholds.
    ///
    /// HOURLY ACCUMULATION:
    /// endodormancyRate() called 24 times per day in dormancySeason.endodormancy()
    /// Units accumulated hourly, summed to daily state variable.
    ///
    /// COUPLING:
    /// Endodormancy completion percentage scales ecodormancy asymptote:
    /// ecodormancy_asymptote = endodormancyPercentage / 100
    ///
    /// IMPLEMENTATION:
    /// See dormancySeason.endodormancy() and utils.endodormancyRate()
    /// </summary>
    public class parEndodormancy
    {
        /// <summary>
        /// Lower limiting temperature for chilling accumulation.
        /// Units: °C
        /// Below this temperature, chilling efficiency → 0 (too cold).
        /// Defines cold boundary of chilling window.
        /// </summary>
        public float limitingLowerTemperature { get; set; }

        /// <summary>
        /// Lower non-limiting temperature for chilling accumulation.
        /// Units: °C
        /// Above this temperature, chilling efficiency increases toward maximum.
        /// Defines optimal lower bound of chilling window.
        /// </summary>
        public float notLimitingLowerTemperature { get; set; }

        /// <summary>
        /// Upper non-limiting temperature for chilling accumulation.
        /// Units: °C
        /// Below this temperature, chilling efficiency remains at maximum.
        /// Defines optimal upper bound of chilling window.
        /// </summary>
        public float notLimitingUpperTemperature { get; set; }

        /// <summary>
        /// Upper limiting temperature for chilling accumulation.
        /// Units: °C
        /// Above this temperature, chilling efficiency → 0 (too warm).
        /// Defines warm boundary of chilling window.
        /// </summary>
        public float limitingUpperTemperature { get; set; }

        /// <summary>
        /// Critical accumulated chilling units required to complete endodormancy.
        /// Units: chilling units (dimensionless, integrated hourly over season)
        /// Upon completion, model transitions to ecodormancy sub-phase.
        /// Completion percentage modulates ecodormancy forcing rate.
        /// </summary>
        public float chillingThreshold { get; set; }
    }

    /// <summary>
    /// Ecodormancy phase parameters (phenoCode 2, second sub-phase).
    /// Controls photothermal forcing for dormancy release after chilling requirements met.
    ///
    /// MECHANISM:
    /// Photothermal forcing driven by increasing photoperiod and warming temperatures.
    /// Rate modulated by endodormancy completion percentage (asymptote scaling).
    ///
    /// FORCING FUNCTION:
    /// Rate = forcingUnitFunction(T, notLimitingT) × photoperiodModulation × asymptote
    /// where asymptote = min(1.0, endodormancyPercentage / 100)
    ///
    /// PHOTOPERIOD RESPONSE:
    /// Dormancy release accelerates as photoperiod exceeds notLimitingPhotoperiod.
    /// Uses ecodormancyRate() with photoperiod-weighted forcing.
    ///
    /// IMPLEMENTATION:
    /// See dormancySeason.ecodormancy() and utils.ecodormancyRate()
    /// </summary>
    public class parEcodormancy
    {
        /// <summary>
        /// Non-limiting photoperiod for dormancy release.
        /// Units: hours
        /// Above this day length, photoperiod no longer limits forcing rate.
        /// Longer photoperiods accelerate dormancy release.
        /// </summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>
        /// Non-limiting temperature for dormancy release.
        /// Units: °C
        /// Optimum temperature for photothermal forcing function.
        /// Used in Yan & Hunt (1999) thermal forcing calculation.
        /// </summary>
        public float notLimitingTemperature { get; set; }

        /// <summary>
        /// Critical accumulated photothermal units required to complete ecodormancy.
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Upon completion, isEcodormancyCompleted flag set and phenoCode → 3 (growth).
        /// </summary>
        public float photoThermalThreshold { get; set; }
    }

    /// <summary>
    /// Growth phase parameters (phenoCode 3).
    /// Controls leaf expansion via thermal forcing following dormancy release.
    ///
    /// MECHANISM:
    /// Growth rate follows Yan & Hunt (1999) thermal forcing function with
    /// three cardinal temperatures defining optimal growth window.
    ///
    /// FORCING FUNCTION:
    /// Rate = forcingUnitFunction(T, Tmin, Topt, Tmax)
    /// Zero forcing outside [Tmin, Tmax], maximum at Topt.
    ///
    /// VI COUPLING:
    /// Growth percentage modulates VI growth rate via negative feedback:
    /// viRate = baseRate × (1 - growthPercentage/100)
    /// Ensures VI stabilizes as growth approaches completion.
    ///
    /// IMPLEMENTATION:
    /// See growingSeason.growthRate() in functions/phenology/growingSeason.cs
    /// </summary>
    public class parGrowth
    {
        /// <summary>
        /// Minimum temperature for thermal forcing (lower cardinal temperature).
        /// Units: °C
        /// Below this temperature, growth rate = 0 (too cold for growth).
        /// </summary>
        public float minimumTemperature { get; set; }

        /// <summary>
        /// Optimum temperature for thermal forcing (optimal cardinal temperature).
        /// Units: °C
        /// At this temperature, growth rate is maximized.
        /// </summary>
        public float optimumTemperature { get; set; }

        /// <summary>
        /// Maximum temperature for thermal forcing (upper cardinal temperature).
        /// Units: °C
        /// Above this temperature, growth rate = 0 (too warm for growth).
        /// </summary>
        public float maximumTemperature { get; set; }

        /// <summary>
        /// Critical accumulated thermal units required to complete growth phase.
        /// Units: thermal units (dimensionless, integrated over days)
        /// Upon completion, isGrowthCompleted flag set and phenoCode → 4 (greendown).
        /// </summary>
        public float thermalThreshold { get; set; }
    }

    /// <summary>
    /// Senescence/decline phase parameters (phenoCode 5).
    /// Controls leaf senescence via weighted photothermal model.
    ///
    /// MECHANISM:
    /// Senescence driven by combination of:
    /// 1. Photoperiod signal (shorter days promote senescence)
    /// 2. Temperature signal (cooler temperatures promote senescence)
    ///
    /// WEIGHTED MODEL:
    /// Rate = 0.7 × temperatureFunction + 0.3 × photoperiodFunction
    /// Temperature signal dominates (70%) over photoperiod signal (30%).
    ///
    /// VI COUPLING:
    /// Senescence drives VI decline from viAtGreendown toward minimumVI.
    ///
    /// IMPLEMENTATION:
    /// See growingSeason.declineRate() in functions/phenology/growingSeason.cs
    /// </summary>
    public class parSenescence
    {
        /// <summary>
        /// Day length threshold where photoperiod starts limiting senescence.
        /// Units: hours
        /// Shorter days → stronger senescence signal.
        /// Similar sigmoid structure to dormancy induction photoperiod function.
        /// </summary>
        public float limitingPhotoperiod { get; set; }

        /// <summary>
        /// Day length threshold where photoperiod no longer limits senescence.
        /// Units: hours
        /// Below this photoperiod, senescence signal → maximum.
        /// </summary>
        public float notLimitingPhotoperiod { get; set; }

        /// <summary>
        /// Accumulated photothermal units required to complete senescence phase.
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Upon completion, isDeclineCompleted flag set and phenoCode → 1 (dormancy induction).
        /// Closes annual phenological cycle.
        /// </summary>
        public float photoThermalThreshold { get; set; }

        /// <summary>
        /// Temperature threshold where temperature starts limiting senescence.
        /// Units: °C
        /// Lower temperatures → stronger senescence signal.
        /// </summary>
        public float limitingTemperature { get; set; }

        /// <summary>
        /// Temperature threshold where temperature no longer limits senescence.
        /// Units: °C
        /// Below this temperature, senescence signal → maximum.
        /// </summary>
        public float notLimitingTemperature { get; set; }
    }

    /// <summary>
    /// Greendown phase parameters (phenoCode 4).
    /// Controls peak greenness maintenance period between growth and senescence.
    ///
    /// MECHANISM:
    /// Simple thermal time accumulation without environmental modulation.
    /// Rate = constant (no temperature or photoperiod response function).
    /// VI maintained near maximum during this phase.
    ///
    /// PURPOSE:
    /// Represents period of maximum photosynthetic capacity and canopy closure.
    /// Duration controlled by thermalThreshold parameter.
    ///
    /// IMPLEMENTATION:
    /// See growingSeason.greendownRate() in functions/phenology/growingSeason.cs
    /// </summary>
    public class parGreendown
    {
        /// <summary>
        /// Accumulated thermal units required to complete greendown phase.
        /// Units: thermal units (dimensionless, integrated over days)
        /// Upon completion, isGreendownCompleted flag set and phenoCode → 5 (decline).
        /// Controls duration of peak greenness period.
        /// </summary>
        public float thermalThreshold { get; set; }
    }

    /// <summary>
    /// Vegetation index dynamics parameters.
    /// Controls VI change rates during each phenophase and defines VI bounds.
    ///
    /// STORAGE CONVENTION:
    /// VI values stored scaled to 100 (multiply 0-1 range by 100).
    /// Example: NDVI 0.85 stored as 85.0
    ///
    /// PHASE-SPECIFIC RATES:
    /// Each phenophase has distinct rate constant controlling VI dynamics:
    /// - Growth: VI increases from dormant baseline to maximum
    /// - Greendown: VI maintained near maximum (minimal change)
    /// - Senescence: VI decreases from peak to dormant minimum
    /// - Dormancy: VI at minimum (dormant vegetation)
    ///
    /// CRITICAL THRESHOLDS:
    /// viAtGrowth: Captured at growth start (dormant understory baseline)
    /// viAtSenescence: Captured at dormancy induction start
    /// viAtGreendown: Captured at decline phase start
    ///
    /// IMPLEMENTATION:
    /// See NDVIdynamics.ndviNormalized() in functions/NDVIdynamics.cs
    /// </summary>
    public class parVegetationIndex
    {
        /// <summary>
        /// Maximum VI rate constant during growth phenophase.
        /// Units: VI units per day
        /// Controls speed of greening-up during leaf expansion.
        /// Rate modulated by growth completion: rate × (1 - growthPercentage/100)
        /// </summary>
        public float nVIGrowth { get; set; }

        /// <summary>
        /// Maximum VI rate constant during endodormancy phenophase.
        /// Units: VI units per day
        /// Typically near zero (VI remains at minimum during chilling accumulation).
        /// </summary>
        public float nVIEndodormancy { get; set; }

        /// <summary>
        /// Maximum VI rate constant during senescence phenophase.
        /// Units: VI units per day (negative values for decline)
        /// Controls speed of leaf senescence and color change.
        /// VI decreases from viAtGreendown toward minimumVI.
        /// </summary>
        public float nVISenescence { get; set; }

        /// <summary>
        /// Maximum VI rate constant during greendown phenophase.
        /// Units: VI units per day
        /// Typically near zero (VI maintained at peak during greendown).
        /// Small positive values allow minor VI adjustments.
        /// </summary>
        public float nVIGreendown { get; set; }

        /// <summary>
        /// Maximum VI rate constant during ecodormancy phenophase.
        /// Units: VI units per day
        /// Controls pre-leaf emergence VI changes during warm dormancy.
        /// Usually small positive values reflecting bud swelling.
        /// </summary>
        public float nVIEcodormancy { get; set; }

        /// <summary>
        /// Minimum vegetation index (dormant vegetation baseline).
        /// Units: VI units (scaled to 100, e.g., NDVI 0.15 = 15.0)
        /// Represents fully dormant canopy with no leaves.
        /// Prevents VI undershoots during senescence.
        /// </summary>
        public float minimumVI { get; set; }

        /// <summary>
        /// Maximum vegetation index (peak greenness).
        /// Units: VI units (scaled to 100, e.g., NDVI 0.85 = 85.0)
        /// Represents full canopy closure and maximum leaf area.
        /// Prevents VI overshoots during growth.
        /// </summary>
        public float maximumVI { get; set; }
    }

    /// <summary>
    /// Photosynthesis parameters for Gross Primary Production (GPP) calculation.
    /// Implements Vegetation Photosynthesis Respiration Model (VPRM) approach.
    ///
    /// TWO-LAYER CANOPY ARCHITECTURE:
    /// - Overstory: Phenology-dependent, uses viAtGrowth baseline for EVI
    /// - Understory: Always active, uses temperature shift for species differences
    ///
    /// GPP FORMULATION:
    /// GPP = maxQuantumYield × Tscale × min(Wscale, min(VPD, PARscale)) × absorbedPAR × phenologyScale × EVI
    ///
    /// LIMITING FACTOR APPROACH:
    /// Changed from multiplicative to minimum limiting factor.
    /// Most limiting environmental factor dominates (co-limitation).
    ///
    /// CRITICAL CONVERSION:
    /// Solar radiation input conversion: solarRadiationH[h] × 277.78 (W/m² to MJ/m²/h)
    ///
    /// IMPLEMENTATION:
    /// See exchanges.VPRM() in functions/exchanges/exchanges.cs
    /// </summary>
    public class parPhotosynthesis
    {
        /// <summary>
        /// Temperature shift for understory layer.
        /// Units: °C
        /// Shifts understory temperature optimum relative to overstory.
        /// Accounts for microclimate and species differences.
        /// Typical values: -2 to +2°C
        /// Applied as: Tleaf_understory = Tair - pixelTemperatureShift
        /// </summary>
        public float pixelTemperatureShift { get; set; }

        /// <summary>
        /// Maximum quantum yield for overstory canopy.
        /// Units: µmol CO₂ per µmol photons
        /// Light use efficiency at optimal conditions (no environmental stress).
        /// Typical range: 0.01 to 0.08 for C3 vegetation.
        /// </summary>
        public float maximumQuantumYieldOver { get; set; }

        /// <summary>
        /// Maximum quantum yield for understory canopy.
        /// Units: µmol CO₂ per µmol photons
        /// Often lower than overstory due to shade adaptation.
        /// Typical range: 0.005 to 0.04 for shade-adapted species.
        /// </summary>
        public float maximumQuantumYieldUnder { get; set; }

        /// <summary>
        /// Minimum temperature for photosynthesis (lower cardinal temperature).
        /// Units: °C
        /// Below this temperature, Tscale = 0 (no photosynthesis).
        /// Defines cold boundary of photosynthetic temperature response.
        /// </summary>
        public float minimumTemperature { get; set; }

        /// <summary>
        /// Optimum temperature for photosynthesis (optimal cardinal temperature).
        /// Units: °C
        /// At this temperature, Tscale = maximum (symmetric polynomial peak).
        /// Typical range: 15-25°C for temperate species.
        /// </summary>
        public float optimumTemperature { get; set; }

        /// <summary>
        /// Maximum temperature for photosynthesis (upper cardinal temperature).
        /// Units: °C
        /// Above this temperature, Tscale = 0 (heat inhibition).
        /// Defines warm boundary of photosynthetic temperature response.
        /// </summary>
        public float maximumTemperature { get; set; }

        /// <summary>
        /// PAR half-saturation constant for overstory (K parameter in Michaelis-Menten).
        /// Units: µmol m⁻² s⁻¹
        /// PAR level at which light response reaches 50% of maximum.
        /// Typical range: 300-800 µmol m⁻² s⁻¹
        /// PARscale = PAR / (PAR + halfSaturation)
        /// </summary>
        public float halfSaturationTree { get; set; }

        /// <summary>
        /// PAR half-saturation constant for understory.
        /// Units: µmol m⁻² s⁻¹
        /// Often lower than overstory due to shade acclimation.
        /// Typical range: 100-400 µmol m⁻² s⁻¹
        /// </summary>
        public float halfSaturationUnder { get; set; }

        /// <summary>
        /// VPD sensitivity parameter (steepness of sigmoid response).
        /// Units: hPa⁻¹
        /// Controls how sharply VPD response transitions from optimal to stressed.
        /// Typical range: 0.1-0.5
        /// </summary>
        public float vpdSensitivity { get; set; }

        /// <summary>
        /// Minimum VPD threshold (lower bound of optimal range).
        /// Units: hPa
        /// Below this VPD, stomatal conductance not limited.
        /// Typical range: 5-10 hPa
        /// </summary>
        public float vpdMin { get; set; }

        /// <summary>
        /// Maximum VPD threshold (upper bound causing stress).
        /// Units: hPa
        /// Above this VPD, stomatal closure reduces photosynthesis.
        /// Typical range: 20-40 hPa
        /// </summary>
        public float vpdMax { get; set; }

        /// <summary>
        /// Rolling window memory length for water stress calculation.
        /// Units: days
        /// Number of previous days included in precipitation/ET0 balance.
        /// Typical range: 7-30 days
        /// Longer windows = slower drought response
        /// </summary>
        public float waterStressDays { get; set; }

        /// <summary>
        /// Water stress threshold (P/ET0 ratio defining onset of stress).
        /// Units: dimensionless ratio
        /// Below this P/ET0 ratio, water stress begins.
        /// Typical value: 1.0 (stress when precipitation < ET0)
        /// </summary>
        public float waterStressThreshold { get; set; }

        /// <summary>
        /// Water stress sensitivity (steepness of stress response).
        /// Units: dimensionless
        /// Controls how rapidly photosynthesis declines with water deficit.
        /// Typical range: 0.5-2.0
        /// </summary>
        public float waterStressSensitivity { get; set; }

        /// <summary>
        /// Phenology scaling factor for growth phase.
        /// Units: dimensionless
        /// Controls logistic function steepness in phenologyFunction().
        /// Modulates GPP during leaf expansion:
        /// phenologyScale = 1 / (1 + exp(-factor × (growthPercentage - 50)))
        /// Typical range: 0.05-0.15
        /// </summary>
        public float growthPhenologyScalingFactor { get; set; }

        /// <summary>
        /// Light extinction coefficient for Beer-Lambert law.
        /// Units: dimensionless
        /// Controls light attenuation through overstory canopy:
        /// Transmitted = Incident × exp(-k × LAI)
        /// Typical range: 0.4-0.8
        /// Diffuse extinction = 0.8 × beam extinction
        /// </summary>
        public float LightExtinctionCoefficient { get; set; }
    }

    /// <summary>
    /// Respiration parameters for Ecosystem Respiration (RECO) calculation.
    /// Split from photosynthesis parameters in two-layer canopy architecture.
    ///
    /// THREE RESPIRATION COMPONENTS:
    /// 1. Overstory autotrophic (recoOver): GPP-dependent with aging function
    /// 2. Understory autotrophic (recoUnder): GPP-dependent
    /// 3. Heterotrophic soil (recoHetero): Temperature and water stress dependent
    ///
    /// TEMPERATURE RESPONSE:
    /// All components use Lloyd-Taylor exponential function:
    /// R = Rref × exp[E₀ × (1/T_ref - 1/(T - T₀))]
    /// where T_ref = 288.15 K (15°C), T₀ = 227.13 K
    ///
    /// SMOOTHING FILTER:
    /// Exponential moving average applied to tree and understory RECO:
    /// RECO_smoothed = RECO_last + alpha × (RECO_raw - RECO_last)
    /// Prevents discontinuities from rapid GPP changes (nighttime, clouds).
    ///
    /// STATE PERSISTENCE:
    /// lastRecoTree and lastRecoUnder persist across hours AND days.
    ///
    /// IMPLEMENTATION:
    /// See exchanges.VPRM() and associated respiration functions in utils.cs
    /// </summary>
    public class parRespiration
    {
        /// <summary>
        /// Activation energy parameter for heterotrophic soil respiration.
        /// Units: K (Kelvin, temperature dependence strength)
        /// Controls temperature sensitivity of soil microbial respiration.
        /// Typical range: 100-400 K
        /// Higher values = stronger temperature dependence
        /// </summary>
        public float activationEnergyParameterSoil { get; set; }

        /// <summary>
        /// Activation energy parameter for overstory autotrophic respiration.
        /// Units: K (Kelvin, temperature dependence strength)
        /// Controls temperature sensitivity of overstory maintenance respiration.
        /// Typical range: 100-400 K
        /// </summary>
        public float activationEnergyParameterOver { get; set; }

        /// <summary>
        /// Activation energy parameter for understory autotrophic respiration.
        /// Units: K (Kelvin, temperature dependence strength)
        /// Controls temperature sensitivity of understory maintenance respiration.
        /// Typical range: 100-400 K
        /// </summary>
        public float activationEnergyParameterUnder { get; set; }

        /// <summary>
        /// Reference respiration rate for heterotrophic soil respiration at 15°C.
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Baseline soil respiration at reference temperature (288.15 K).
        /// Typical range: 0.5-5.0 µmol CO₂ m⁻² s⁻¹
        /// </summary>
        public float referenceRespirationSoil { get; set; }

        /// <summary>
        /// Reference respiration rate for overstory autotrophic respiration at 15°C.
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Baseline overstory maintenance respiration at reference temperature.
        /// Scaled by GPP and phenology aging function.
        /// Typical range: 0.1-2.0 µmol CO₂ m⁻² s⁻¹
        /// </summary>
        public float referenceRespirationOver { get; set; }

        /// <summary>
        /// Reference respiration rate for understory autotrophic respiration at 15°C.
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Baseline understory maintenance respiration at reference temperature.
        /// Scaled by GPP.
        /// Typical range: 0.05-1.0 µmol CO₂ m⁻² s⁻¹
        /// </summary>
        public float referenceRespirationUnder { get; set; }

        /// <summary>
        /// Response coefficient of overstory respiration to GPP.
        /// Units: dimensionless
        /// Linear scaling factor: RECO_over = referenceRespiration × (1 + coefficient × GPP)
        /// Represents coupling between photosynthesis and autotrophic respiration.
        /// Typical range: 0.01-0.2
        /// </summary>
        public float respirationResponseOver { get; set; }

        /// <summary>
        /// Response coefficient of understory respiration to GPP.
        /// Units: dimensionless
        /// Linear scaling factor: RECO_under = referenceRespiration × (1 + coefficient × GPP)
        /// Typical range: 0.01-0.2
        /// </summary>
        public float respirationResponseUnder { get; set; }

        /// <summary>
        /// Aging factor for phenological respiration scaling.
        /// Units: dimensionless (0-1 range)
        /// Inflection point of logistic aging function:
        /// agingScale = 1 / (1 + exp(10 × (seasonProgress - agingFactor)))
        /// Higher values shift aging curve later in growing season.
        /// Typical range: 0.3-0.7
        /// </summary>
        public float respirationAgingFactor { get; set; }

        /// <summary>
        /// Smoothing factor for exponential moving average filter (alpha).
        /// Units: dimensionless (0-1 range)
        /// Controls smoothing strength in exponential moving average:
        /// RECO_smoothed = RECO_last + alpha × (RECO_raw - RECO_last)
        /// alpha = 0: infinite memory (no smoothing)
        /// alpha = 1: no memory (no smoothing)
        /// Typical range: 0.1-0.5 for moderate smoothing
        /// Prevents discontinuities from rapid GPP fluctuations.
        /// </summary>
        public float respirationSmoothingFactor { get; set; }
    }
}

