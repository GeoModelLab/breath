using System;
using source.functions;

// ============================================================================
// Output Data Layer - SWELL Model State Variables
// ============================================================================
// This namespace contains all output data structures for the SWELL model.
//
// STATE ACCUMULATION PATTERN:
// All phenophases follow: outputT1.phase.state = outputT.phase.state + outputT1.phase.rate
// - outputT: Previous timestep (T-1)
// - outputT1: Current timestep (T)
// - Rate computed from environmental drivers → accumulated to state → threshold comparison
//
// TEMPORAL CONTINUITY:
// State variables persist across days via outputT0/outputT1 swapping in vvvvInterface.
// Boolean flags prevent re-entry into completed phases.
//
// PHASE TRANSITION LOGIC:
// Percentage = (state / threshold) × 100
// If percentage ≥ 100 → phase completed → phenoCode advances
// ============================================================================

namespace source.data
{
    /// <summary>
    /// Master output container for SWELL model state variables and computed values.
    /// Contains phenophase-specific state/rate variables, completion flags, VI dynamics, and carbon fluxes.
    ///
    /// USAGE PATTERN (vvvvInterface):
    /// 1. Previous day output passed as outputT (read-only reference)
    /// 2. Current day output passed as outputT1 (write target)
    /// 3. Rate functions compute daily rates from environmental drivers
    /// 4. States accumulated: outputT1.state = outputT.state + rate
    /// 5. Completion checked: if (state ≥ threshold) → advance phenoCode
    ///
    /// TEMPORAL SWAPPING:
    /// outputT0 = outputT1 (save current)
    /// outputT1 = new output() (prepare next timestep)
    ///
    /// STATE PERSISTENCE:
    /// Boolean flags (isDormancyInduced, etc.) persist until phase re-entry conditions met.
    /// Respiration smoothing states (lastRecoTree, lastRecoUnder) persist across hours AND days.
    /// </summary>
    public class output
    {
        #region Phenophase State/Rate Containers

        /// <summary>Dormancy induction state and rate variables (phenoCode 1)</summary>
        public dormancyInduction dormancyInduction = new dormancyInduction();

        /// <summary>Endodormancy state and rate variables (phenoCode 2, chilling)</summary>
        public endodormancy endodormancy = new endodormancy();

        /// <summary>Ecodormancy state and rate variables (phenoCode 2, forcing)</summary>
        public ecodormancy ecodormancy = new ecodormancy();

        /// <summary>Greendown state and rate variables (phenoCode 4, peak greenness)</summary>
        public greenDown greenDown = new greenDown();

        /// <summary>Growth state and rate variables (phenoCode 3, leaf expansion)</summary>
        public growth growth = new growth();

        /// <summary>Decline/senescence state and rate variables (phenoCode 5, leaf senescence)</summary>
        public decline decline = new decline();

        /// <summary>Carbon exchange variables (GPP, RECO, NEE) and canopy structure</summary>
        public exchanges exchanges = new exchanges();

        #endregion

        #region Phase Completion Boolean Flags

        /// <summary>
        /// Dormancy induction completed flag.
        /// Set true when dormancyInductionPercentage ≥ 100.
        /// Triggers transition to endodormancy (phenoCode 1 → 2).
        /// Prevents re-entry until new annual cycle begins.
        /// </summary>
        public bool isDormancyInduced { get; set; }

        /// <summary>
        /// Ecodormancy completed flag.
        /// Set true when ecodormancyPercentage ≥ 100.
        /// Triggers transition to growth phase (phenoCode 2 → 3).
        /// Dormancy fully released, budburst imminent.
        /// </summary>
        public bool isEcodormancyCompleted { get; set; }

        /// <summary>
        /// Growth phase completed flag.
        /// Set true when growthPercentage ≥ 100.
        /// Triggers transition to greendown (phenoCode 3 → 4).
        /// Leaf expansion complete, canopy closure achieved.
        /// </summary>
        public bool isGrowthCompleted { get; set; }

        /// <summary>
        /// Greendown phase completed flag.
        /// Set true when greenDownPercentage ≥ 100.
        /// Triggers transition to decline/senescence (phenoCode 4 → 5).
        /// Peak greenness period ended.
        /// </summary>
        public bool isGreendownCompleted { get; set; }

        /// <summary>
        /// Decline/senescence phase completed flag.
        /// Set true when declinePercentage ≥ 100.
        /// Triggers transition back to dormancy induction (phenoCode 5 → 1).
        /// Closes annual phenological cycle.
        /// </summary>
        public bool isDeclineCompleted { get; set; }

        #endregion

        #region Phase Completion Percentages

        /// <summary>
        /// Dormancy induction completion percentage.
        /// Calculated as: (photoThermalDormancyInductionState / photoThermalThreshold) × 100
        /// Range: 0-100+ (≥100 triggers phase completion)
        /// </summary>
        public float dormancyInductionPercentage { get; set; }

        /// <summary>
        /// Endodormancy completion percentage.
        /// Calculated as: (endodormancyState / chillingThreshold) × 100
        /// Range: 0-100+ (≥100 allows ecodormancy progression)
        /// CRITICAL: Scales ecodormancy asymptote → ecodormancy_rate × (endodormancyPercentage/100)
        /// </summary>
        public float endodormancyPercentage { get; set; }

        /// <summary>
        /// Ecodormancy completion percentage.
        /// Calculated as: (ecodormancyState / photoThermalThreshold) × 100
        /// Range: 0-100+ (≥100 triggers growth phase entry)
        /// Modulated by endodormancy completion via asymptote scaling.
        /// </summary>
        public float ecodormancyPercentage { get; set; }

        /// <summary>
        /// Growth phase completion percentage.
        /// Calculated as: (growthState / thermalThreshold) × 100
        /// Range: 0-100+ (≥100 triggers greendown entry)
        /// CRITICAL: Modulates VI growth rate via negative feedback: viRate × (1 - growthPercentage/100)
        /// Also drives phenology scaling in GPP calculation (logistic function).
        /// </summary>
        public float growthPercentage { get; set; }

        /// <summary>
        /// Greendown phase completion percentage.
        /// Calculated as: (greenDownState / thermalThreshold) × 100
        /// Range: 0-100+ (≥100 triggers decline/senescence entry)
        /// </summary>
        public float greenDownPercentage { get; set; }

        /// <summary>
        /// Decline/senescence phase completion percentage.
        /// Calculated as: (declineState / photoThermalThreshold) × 100
        /// Range: 0-100+ (≥100 closes annual cycle, returns to dormancy induction)
        /// </summary>
        public float declinePercentage { get; set; }

        #endregion

        #region Vegetation Index Dynamics

        /// <summary>
        /// Current simulated vegetation index (NDVI or EVI).
        /// Units: VI units scaled to 100 (e.g., NDVI 0.85 = 85.0)
        /// Updated daily via: vi_new = vi_old + viRate
        /// Bounded by parVegetationIndex.minimumVI and maximumVI.
        /// Drives LAI estimation and carbon flux scaling.
        /// </summary>
        public float vi { get; set; }

        /// <summary>
        /// Daily rate of VI change.
        /// Units: VI units per day
        /// Computed by NDVIdynamics.ndviNormalized() based on phenoCode.
        /// Positive during growth/ecodormancy, near-zero during greendown, negative during senescence.
        /// Phase-specific rate constants from parVegetationIndex (nVIGrowth, nVISenescence, etc.).
        /// </summary>
        public float viRate { get; set; }

        /// <summary>
        /// Reference VI value (observed/measured).
        /// Units: VI units scaled to 100
        /// Used for model calibration and validation.
        /// Not used in model computations, only for comparison/evaluation.
        /// </summary>
        public float viReference { get; set; }

        /// <summary>
        /// VI value captured at start of growth phase (phenoCode transition 2→3).
        /// Units: VI units scaled to 100
        /// Represents dormant understory baseline VI.
        /// CRITICAL: Used in overstory EVI calculation: EVIoverstory = current_vi - viAtGrowth
        /// Stored once per annual cycle at growth phase entry.
        /// </summary>
        public float viAtGrowth { get; set; }

        /// <summary>
        /// VI value captured at start of dormancy induction (phenoCode transition 5→1 or at first induction).
        /// Units: VI units scaled to 100
        /// Represents peak greenness before senescence begins.
        /// Used for VI decline trajectory calculations.
        /// </summary>
        public float viAtSenescence { get; set; }

        /// <summary>
        /// VI value captured at start of decline phase (phenoCode transition 4→5).
        /// Units: VI units scaled to 100
        /// Represents post-greendown VI level.
        /// Used as starting point for senescence-driven VI decline toward minimumVI.
        /// </summary>
        public float viAtGreendown { get; set; }

        #endregion

        #region Phenological Phase Identifiers

        /// <summary>
        /// Current phenological phase code.
        /// Values:
        /// 1 = Dormancy induction (short days/cool temps trigger dormancy entry)
        /// 2 = Endo/ecodormancy (chilling accumulation + photothermal forcing)
        /// 3 = Growth (leaf expansion via thermal forcing)
        /// 4 = Greendown (peak greenness maintenance)
        /// 5 = Decline/senescence (leaf senescence via photothermal signals)
        ///
        /// PHASE TRANSITIONS:
        /// 1→2: Dormancy induction complete
        /// 2→3: Ecodormancy complete (budburst)
        /// 3→4: Growth complete (canopy closure)
        /// 4→5: Greendown complete (senescence begins)
        /// 5→1: Decline complete (new annual cycle)
        /// </summary>
        public float phenoCode { get; set; }

        /// <summary>
        /// Human-readable phenological phase name.
        /// Examples: "Dormancy Induction", "Endodormancy", "Growth", "Greendown", "Senescence"
        /// For logging, visualization, and user output.
        /// Synchronized with phenoCode value.
        /// </summary>
        public string phenoString { get; set; }

        #endregion

        /// <summary>
        /// Input weather data snapshot for current timestep.
        /// Used to preserve input context associated with this output state.
        /// Enables retrospective analysis linking outputs to driving variables.
        /// </summary>
        public input weather = new input();
    }

    /// <summary>
    /// Dormancy induction state and rate variables (phenoCode 1).
    /// Stores photoperiod and temperature components of photothermal induction model.
    ///
    /// MULTIPLICATIVE MODEL:
    /// photoThermalRate = photoperiodRate × temperatureRate
    /// Both components are sigmoid functions (0-1 range).
    ///
    /// STATE ACCUMULATION:
    /// photoThermalState_new = photoThermalState_old + photoThermalRate
    /// </summary>
    public class dormancyInduction
    {
        /// <summary>
        /// Photoperiod dormancy induction rate (sigmoid function of day length).
        /// Units: dimensionless (0-1 range)
        /// Computed by photoperiodFunctionInduction() in utils.cs.
        /// Short days → rate approaches 1.0 (strong induction signal).
        /// </summary>
        public float photoperiodDormancyInductionRate { get; set; }

        /// <summary>
        /// Temperature dormancy induction rate (sigmoid function of air temperature).
        /// Units: dimensionless (0-1 range)
        /// Computed by temperatureFunctionInduction() in utils.cs.
        /// Cool temperatures → rate approaches 1.0 (strong induction signal).
        /// </summary>
        public float temperatureDormancyInductionRate { get; set; }

        /// <summary>
        /// Combined photothermal dormancy induction rate.
        /// Units: photothermal units per day (dimensionless)
        /// Calculated as: photoperiodRate × temperatureRate
        /// Accumulated daily to photoThermalDormancyInductionState.
        /// </summary>
        public float photoThermalDormancyInductionRate { get; set; }

        /// <summary>
        /// Accumulated photothermal dormancy induction state.
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Compared to parDormancyInduction.photoThermalThreshold.
        /// When state ≥ threshold → isDormancyInduced = true, phenoCode → 2.
        /// </summary>
        public float photoThermalDormancyInductionState { get; set; }
    }

    /// <summary>
    /// Endodormancy state and rate variables (phenoCode 2, first sub-phase).
    /// Stores chilling unit accumulation during deep dormancy.
    ///
    /// HOURLY ACCUMULATION:
    /// endodormancyRate computed hourly (24 times per day).
    /// Daily state = sum of 24 hourly chilling unit contributions.
    ///
    /// CHILLING MODEL:
    /// Utah model variant with efficiency function peaking at optimal temperatures.
    /// </summary>
    public class endodormancy
    {
        /// <summary>
        /// Hourly endodormancy rate (chilling accumulation efficiency).
        /// Units: chilling units per hour (dimensionless)
        /// Computed by endodormancyRate() in utils.cs.
        /// Temperature-dependent efficiency function (peaks at 5-10°C typically).
        /// Summed over 24 hours to get daily state increment.
        /// </summary>
        public float endodormancyRate { get; set; }

        /// <summary>
        /// Accumulated chilling units (endodormancy state).
        /// Units: chilling units (dimensionless, integrated hourly over season)
        /// Compared to parEndodormancy.chillingThreshold.
        /// Completion percentage scales ecodormancy asymptote.
        /// Full completion not required for ecodormancy progression (partial chilling allowed).
        /// </summary>
        public float endodormancyState { get; set; }
    }

    /// <summary>
    /// Ecodormancy state and rate variables (phenoCode 2, second sub-phase).
    /// Stores photothermal forcing accumulation for dormancy release.
    ///
    /// ASYMPTOTE MODULATION:
    /// Rate scaled by endodormancy completion: rate × min(1.0, endodormancyPercentage/100)
    /// Incomplete chilling slows but does not prevent ecodormancy progression.
    ///
    /// PHOTOTHERMAL FORCING:
    /// Driven by warming temperatures and lengthening photoperiod.
    /// </summary>
    public class ecodormancy
    {
        /// <summary>
        /// Daily ecodormancy rate (photothermal forcing).
        /// Units: photothermal units per day (dimensionless)
        /// Computed by ecodormancyRate() in utils.cs.
        /// Modulated by endodormancy completion percentage (asymptote scaling).
        /// Driven by temperature forcing × photoperiod response.
        /// </summary>
        public float ecodormancyRate { get; set; }

        /// <summary>
        /// Accumulated photothermal forcing state (ecodormancy state).
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Compared to parEcodormancy.photoThermalThreshold.
        /// When state ≥ threshold → isEcodormancyCompleted = true, phenoCode → 3 (growth).
        /// </summary>
        public float ecodormancyState { get; set; }
    }

    /// <summary>
    /// Growth phase state and rate variables (phenoCode 3).
    /// Stores thermal forcing accumulation during leaf expansion.
    ///
    /// THERMAL FORCING:
    /// Yan & Hunt (1999) function with three cardinal temperatures.
    /// Zero forcing outside [Tmin, Tmax], maximum at Topt.
    ///
    /// VI COUPLING:
    /// Growth percentage drives VI growth rate via negative feedback.
    /// Also drives phenology scaling in GPP calculation (logistic function).
    /// </summary>
    public class growth
    {
        /// <summary>
        /// Daily growth rate (thermal forcing).
        /// Units: thermal units per day (dimensionless)
        /// Computed by growthRate() in growingSeason.cs.
        /// Temperature-dependent forcing with cardinal temperature constraints.
        /// Accumulated daily to growthState.
        /// </summary>
        public float growthRate { get; set; }

        /// <summary>
        /// Accumulated thermal units (growth state).
        /// Units: thermal units (dimensionless, integrated over days)
        /// Compared to parGrowth.thermalThreshold.
        /// When state ≥ threshold → isGrowthCompleted = true, phenoCode → 4 (greendown).
        /// Percentage completion modulates VI growth rate and GPP phenology scaling.
        /// </summary>
        public float growthState { get; set; }
    }

    /// <summary>
    /// Decline/senescence phase state and rate variables (phenoCode 5).
    /// Stores weighted photothermal forcing during leaf senescence.
    ///
    /// WEIGHTED MODEL:
    /// Rate = 0.7 × temperatureFunction + 0.3 × photoperiodFunction
    /// Temperature signal dominates over photoperiod signal.
    ///
    /// VI COUPLING:
    /// Drives VI decline from viAtGreendown toward minimumVI.
    /// </summary>
    public class decline
    {
        /// <summary>
        /// Daily decline rate (weighted photothermal senescence forcing).
        /// Units: photothermal units per day (dimensionless)
        /// Computed by declineRate() in growingSeason.cs.
        /// Weighted combination of temperature and photoperiod signals.
        /// Accumulated daily to declineState.
        /// </summary>
        public float declineRate { get; set; }

        /// <summary>
        /// Accumulated photothermal units (decline state).
        /// Units: photothermal units (dimensionless, integrated over days)
        /// Compared to parSenescence.photoThermalThreshold.
        /// When state ≥ threshold → isDeclineCompleted = true, phenoCode → 1 (new cycle).
        /// </summary>
        public float declineState { get; set; }
    }

    /// <summary>
    /// Greendown phase state and rate variables (phenoCode 4).
    /// Stores simple thermal time accumulation during peak greenness period.
    ///
    /// MECHANISM:
    /// Constant rate (no environmental modulation).
    /// VI maintained near maximum with minimal change.
    ///
    /// PURPOSE:
    /// Represents period of maximum photosynthetic capacity between growth and senescence.
    /// </summary>
    public class greenDown
    {
        /// <summary>
        /// Daily greendown rate (constant thermal accumulation).
        /// Units: thermal units per day (dimensionless)
        /// Computed by greendownRate() in growingSeason.cs.
        /// Typically constant value (no temperature or photoperiod response).
        /// </summary>
        public float greenDownRate { get; set; }

        /// <summary>
        /// Accumulated thermal units (greendown state).
        /// Units: thermal units (dimensionless, integrated over days)
        /// Compared to parGreendown.thermalThreshold.
        /// When state ≥ threshold → isGreendownCompleted = true, phenoCode → 5 (decline).
        /// </summary>
        public float greenDownState { get; set; }
    }

    /// <summary>
    /// Carbon exchange variables and canopy structure.
    /// Contains hourly GPP, RECO, NEE calculations and two-layer canopy properties.
    ///
    /// TWO-LAYER ARCHITECTURE:
    /// - Overstory: Phenology-dependent, deciduous canopy
    /// - Understory: Always active, evergreen or herbaceous
    ///
    /// HOURLY LISTS:
    /// All List&lt;float&gt; fields contain 24 elements (hours 0-23).
    /// Populated during VPRM() hourly loop in exchanges.VPRM().
    ///
    /// DAILY SUMMATION:
    /// gppDaily, recoDaily, neeDaily = sum of 24 hourly values.
    ///
    /// STATE PERSISTENCE:
    /// lastRecoTree and lastRecoUnder (if added) persist across hours AND days for smoothing.
    /// </summary>
    public class exchanges
    {
        // ====================================================================
        // ROLLING MEMORY FOR WATER STRESS
        // ====================================================================

        /// <summary>
        /// Rolling window of daily precipitation values.
        /// Units: mm (each element is daily total)
        /// Length controlled by parPhotosynthesis.waterStressDays.
        /// Used in waterStressFunction() to compute precipitation memory.
        /// </summary>
        public List<float> PrecipitationMemory = new List<float>();

        /// <summary>
        /// Rolling window of daily ET0 values.
        /// Units: mm (each element is daily total)
        /// Length matches PrecipitationMemory.
        /// Used in waterStressFunction() to compute atmospheric demand memory.
        /// Water stress = f(sum(Precip) / sum(ET0), threshold, sensitivity)
        /// </summary>
        public List<float> ET0memory = new List<float>();

        // ====================================================================
        // HOURLY RADIATION PARTITIONING (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly diffuse PAR (24 elements).
        /// Units: µmol m⁻² s⁻¹
        /// Computed by PartitionRadiation() using Erbs et al. (1982) model.
        /// Diffuse extinction = 0.8 × beam extinction coefficient.
        /// </summary>
        public List<float> PARdiffuse = new List<float>();

        /// <summary>
        /// Hourly direct PAR (24 elements).
        /// Units: µmol m⁻² s⁻¹
        /// Computed by PartitionRadiation() using clearness index.
        /// Direct + Diffuse = Total incident PAR.
        /// </summary>
        public List<float> PARdirect = new List<float>();

        /// <summary>
        /// Light interception fraction for understory.
        /// Units: dimensionless (0-1)
        /// Calculated once per day (not hourly list).
        /// Represents fraction of incident light reaching understory through overstory gaps.
        /// Uses Beer-Lambert law: exp(-k × LAIoverstory)
        /// </summary>
        public float LightInterceptionUnder { get; set; }

        // ====================================================================
        // HOURLY LEAF TEMPERATURE (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly overstory leaf temperature (24 elements).
        /// Units: °C
        /// SIMPLIFIED: Set equal to air temperature (no energy balance model).
        /// TleafOver[h] = airTemperatureH[h]
        /// </summary>
        public List<float> TleafOver = new List<float>();

        /// <summary>
        /// Hourly understory leaf temperature (24 elements).
        /// Units: °C
        /// Includes temperature shift for microclimate/species differences.
        /// TleafUnder[h] = airTemperatureH[h] - pixelTemperatureShift
        /// </summary>
        public List<float> TleafUnder = new List<float>();

        // ====================================================================
        // HOURLY GPP SCALING FACTORS (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly temperature scaler for overstory GPP (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Symmetric polynomial function with cardinal temperatures.
        /// Zero outside [Tmin, Tmax], maximum at Topt.
        /// Computed by temperatureFunction() in utils.cs.
        /// </summary>
        public List<float> TscaleOver = new List<float>();

        /// <summary>
        /// Hourly temperature scaler for understory GPP (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Uses shifted temperature optimum (Topt - pixelTemperatureShift).
        /// </summary>
        public List<float> TscaleUnder = new List<float>();

        /// <summary>
        /// Hourly PAR scaler for overstory (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Michaelis-Menten function: PAR / (PAR + halfSaturation)
        /// Asymptotically approaches 1.0 at high PAR.
        /// </summary>
        public List<float> PARscaleOverstory = new List<float>();

        /// <summary>
        /// Hourly PAR scaler for understory (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Uses different halfSaturation (typically lower for shade adaptation).
        /// </summary>
        public List<float> PARscaleUnderstory = new List<float>();

        /// <summary>
        /// Hourly water stress scaler (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Computed once per hour, added to rolling memory.
        /// Based on P/ET0 ratio from rolling window.
        /// Shared by GPP and heterotrophic respiration.
        /// </summary>
        public List<float> WaterStress = new List<float>();

        /// <summary>
        /// Hourly phenology scaler for overstory GPP (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Logistic function of growth percentage during growth phase.
        /// Constant (1.0) during greendown and decline phases.
        /// phenologyScale = 1 / (1 + exp(-factor × (growthPercentage - 50)))
        /// </summary>
        public List<float> phenologyScale = new List<float>();

        /// <summary>
        /// Hourly VPD scaler for GPP (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Sigmoid response function with vpdMin, vpdMax, vpdSensitivity.
        /// Represents stomatal conductance reduction at high VPD.
        /// Computed by VPDfunction() in utils.cs.
        /// </summary>
        public List<float> vpdScale = new List<float>();

        // ====================================================================
        // CANOPY STRUCTURE (daily values, not hourly)
        // ====================================================================

        /// <summary>
        /// Vegetation cover fraction (overstory canopy closure).
        /// Units: dimensionless (0-1)
        /// Calculated from VI values: function of (vi - minimumVI) / (maximumVI - minimumVI)
        /// Used to weight overstory vs understory contributions.
        /// </summary>
        public float vegetationCover { get; set; }

        /// <summary>
        /// Overstory Leaf Area Index.
        /// Units: m² leaf area per m² ground area (dimensionless)
        /// Estimated from overstory EVI using estimateLAI() in utils.cs.
        /// Drives light interception via Beer-Lambert law.
        /// </summary>
        public float LAIoverstory { get; set; }

        /// <summary>
        /// Understory Leaf Area Index.
        /// Units: m² leaf area per m² ground area (dimensionless)
        /// Estimated from understory EVI with temporal continuity.
        /// Understory EVI = outputT.exchanges.EVIunderstory + deltaVi × (1 - vegetationCover)
        /// </summary>
        public float LAIunderstory { get; set; }

        /// <summary>
        /// Overstory Enhanced Vegetation Index.
        /// Units: dimensionless (0-1 range, but stored scaled)
        /// Calculated as: current_vi - viAtGrowth
        /// Represents phenology-dependent green biomass.
        /// Zero during dormancy, maximum during greendown.
        /// </summary>
        public float EVIoverstory { get; set; }

        /// <summary>
        /// Understory Enhanced Vegetation Index.
        /// Units: dimensionless (0-1 range, but stored scaled)
        /// Tracks changes with vegetation cover weighting for temporal continuity.
        /// Always active (not phenology-dependent like overstory).
        /// </summary>
        public float EVIunderstory { get; set; }

        /// <summary>
        /// Hourly absorbed PAR by overstory (24 elements).
        /// Units: µmol m⁻² s⁻¹
        /// Calculated using light extinction and LAI via Beer-Lambert law.
        /// Drives overstory GPP calculation.
        /// </summary>
        public List<float> LightOverstory = new List<float>();

        /// <summary>
        /// Hourly absorbed PAR by understory (24 elements).
        /// Units: µmol m⁻² s⁻¹
        /// Receives light transmitted through overstory gaps.
        /// Drives understory GPP calculation.
        /// </summary>
        public List<float> LightUnderstory = new List<float>();

        // ====================================================================
        // HOURLY GPP COMPONENTS (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly overstory Gross Primary Production (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Phenology-dependent, zero during dormancy.
        /// Uses minimum limiting factor approach: min(Wscale, min(VPD, PARscale))
        /// </summary>
        public List<float> gppOver = new List<float>();

        /// <summary>
        /// Hourly understory Gross Primary Production (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Always active (not phenology-dependent).
        /// Uses shifted temperature optimum.
        /// </summary>
        public List<float> gppUnder = new List<float>();

        /// <summary>
        /// Hourly total Gross Primary Production (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Sum of overstory and understory GPP.
        /// gpp[h] = gppOver[h] + gppUnder[h]
        /// </summary>
        public List<float> gpp = new List<float>();

        /// <summary>
        /// Daily total Gross Primary Production.
        /// Units: µmol CO₂ m⁻² d⁻¹
        /// Sum of 24 hourly gpp values.
        /// gppDaily = Σ(gpp[h]) for h = 0 to 23
        /// </summary>
        public float gppDaily { get; set; }

        // ====================================================================
        // HOURLY RECO SCALING FACTORS (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly temperature scaler for respiration (24 elements).
        /// Units: dimensionless (exponential, >1.0 possible)
        /// Lloyd-Taylor exponential temperature response.
        /// Computed by ComputeTscaleReco() in utils.cs.
        /// Applied to all three RECO components.
        /// </summary>
        public List<float> TscaleReco = new List<float>();

        /// <summary>
        /// Hourly water stress scaler for heterotrophic respiration (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Same as WaterStress list, specific to RECO context.
        /// Reduces soil respiration during drought.
        /// </summary>
        public List<float> WscaleReco = new List<float>();

        /// <summary>
        /// Hourly phenology scaler for autotrophic respiration (24 elements).
        /// Units: dimensionless (0-1 range)
        /// Logistic aging function based on season progress.
        /// Modulates overstory respiration with phenological state.
        /// Computed by RecoRespirationFunction() in utils.cs.
        /// </summary>
        public List<float> PhenologyscaleReco = new List<float>();

        // ====================================================================
        // HOURLY RECO COMPONENTS (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly overstory autotrophic respiration (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// GPP-dependent with aging function and smoothing filter.
        /// Computed by estimateRECOtree() in utils.cs.
        /// Smoothed via exponential moving average (prevents discontinuities).
        /// </summary>
        public List<float> recoOver = new List<float>();

        /// <summary>
        /// Hourly understory autotrophic respiration (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// GPP-dependent with smoothing filter (no aging function).
        /// Computed by estimateRECOunder() in utils.cs.
        /// </summary>
        public List<float> recoUnder = new List<float>();

        /// <summary>
        /// Hourly heterotrophic soil respiration (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Temperature and water stress dependent (independent of GPP).
        /// Computed by estimateRECOHetero() in utils.cs.
        /// Uses soil temperature and water stress scaler.
        /// </summary>
        public List<float> recoHetero = new List<float>();

        /// <summary>
        /// Hourly total ecosystem respiration (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Sum of three respiration components.
        /// reco[h] = recoOver[h] + recoUnder[h] + recoHetero[h]
        /// </summary>
        public List<float> reco = new List<float>();

        /// <summary>
        /// Hourly vegetation index (24 elements).
        /// Units: VI units scaled to 100
        /// Typically constant throughout day (daily VI value replicated 24 times).
        /// Used for hourly diagnostics and visualization.
        /// </summary>
        public List<float> viHourly = new List<float>();

        /// <summary>
        /// Daily total ecosystem respiration.
        /// Units: µmol CO₂ m⁻² d⁻¹
        /// Sum of 24 hourly reco values.
        /// recoDaily = Σ(reco[h]) for h = 0 to 23
        /// </summary>
        public float recoDaily { get; set; }

        // ====================================================================
        // HOURLY NET ECOSYSTEM EXCHANGE (24 elements)
        // ====================================================================

        /// <summary>
        /// Hourly Net Ecosystem Exchange (24 elements).
        /// Units: µmol CO₂ m⁻² s⁻¹
        /// Net carbon flux (positive = source, negative = sink).
        /// nee[h] = reco[h] - gpp[h]
        /// Negative during daytime (photosynthesis > respiration = CO₂ sink).
        /// Positive at night (respiration only = CO₂ source).
        /// </summary>
        public List<float> nee = new List<float>();

        /// <summary>
        /// Daily total Net Ecosystem Exchange.
        /// Units: µmol CO₂ m⁻² d⁻¹
        /// Sum of 24 hourly nee values.
        /// neeDaily = Σ(nee[h]) = recoDaily - gppDaily
        /// Negative = daily net carbon uptake (sink).
        /// Positive = daily net carbon release (source).
        /// </summary>
        public float neeDaily { get; set; }

        /// <summary>
        /// Hour indices (24 elements: 0-23).
        /// Used for indexing and visualization.
        /// Corresponds to local solar time.
        /// </summary>
        public List<int> hours = new List<int>();

        // NOTE: State persistence fields for respiration smoothing
        // (lastRecoTree, lastRecoUnder) should be added here if not present
        // in the actual implementation. These persist across hours AND days.

        /// <summary>
        /// Returns all hourly time series (List&lt;float&gt; properties) as a dictionary.
        /// Dictionary keys are property names, values are the corresponding hourly lists.
        ///
        /// INCLUDED PROPERTIES:
        /// - Carbon fluxes: nee, reco, gpp, gppOver, gppUnder, recoOver, recoUnder, recoHetero
        /// - Temperature scalers: TscaleReco, TscaleOver
        /// - Environmental scalers: vpdScale, WaterStress
        /// - Light scalers: PARscaleUnderstory, PARscaleOverstory
        ///
        /// USAGE:
        /// Enables efficient batch export of hourly data for analysis, visualization, or output to vvvv.
        /// All lists contain 24 elements (hours 0-23).
        /// </summary>
        /// <returns>Dictionary mapping property names to their hourly value lists</returns>
        public Dictionary<string, List<float>> GetHourlyTimeSeriesAsDictionary()
        {
            return new Dictionary<string, List<float>>
            {
                { "nee", nee },
                { "reco", reco },
                { "gpp", gpp },
                { "gppOver", gppOver },
                { "gppUnder", gppUnder },
                { "recoUnder", recoUnder },
                { "recoOver", recoOver },
                { "recoHetero", recoHetero },
                { "TscaleReco", TscaleReco },
                { "TscaleOver", TscaleOver },
                { "vpdScale", vpdScale },
                { "WaterStress", WaterStress },
                { "PARscaleUnderstory", PARscaleUnderstory },
                { "PARscaleOverstory", PARscaleOverstory }
            };
        }
    }
}
