using source.data;
using System;
using System.ComponentModel.Design;

// ============================================================================
// Carbon Exchanges Module - VPRM Implementation for SWELL Model
// ============================================================================
// This module implements the Vegetation Photosynthesis Respiration Model (VPRM)
// adapted for two-layer canopy architecture with hourly carbon flux calculations.
//
// KEY ARCHITECTURE FEATURES:
//
// 1. TWO-LAYER CANOPY STRUCTURE:
//    - Overstory: Phenology-dependent (active phenoCode ≥3), deciduous behavior
//    - Understory: Always active, evergreen/herbaceous behavior
//    - Separate LAI, EVI, light interception, and temperature responses
//
// 2. LIGHT PARTITIONING (pre-computed outside hourly loop):
//    - Direct/diffuse radiation partitioning (Erbs et al. 1982)
//    - Beer-Lambert light extinction through overstory canopy
//    - Transmitted light to understory (separate direct/diffuse paths)
//    - Light interception coefficients computed once per day
//
// 3. GPP CALCULATION (hourly, minimum limiting factor approach):
//    GPP = QuantumYield × Tscale × min(Wscale, min(VPD, PARscale)) × PAR × Phenology × EVI
//    - Temperature scaler: Symmetric polynomial (zero outside Tmin-Tmax)
//    - Leaf temperature simplified to air temperature (no energy balance)
//    - Understory has temperature shift via pixelTemperatureShift parameter
//    - Overstory includes phenology scaler (logistic function of growth percentage)
//    - Understory has no phenology modulation (always active)
//
// 4. RECO CALCULATION (hourly, three additive components):
//    - recoOver: Overstory autotrophic respiration (GPP-dependent with aging)
//    - recoUnder: Understory autotrophic respiration (GPP-dependent)
//    - recoHetero: Heterotrophic soil respiration (temperature and water dependent)
//    - Each uses Lloyd-Taylor temperature response with separate activation energies
//    - Exponential moving average smoothing prevents discontinuities
//    - State variables lastRecoTree/lastRecoUnder persist across hours and days
//
// 5. CARBON FLUX OUTPUTS:
//    - Hourly arrays (24 elements): GPP (over/under/total), RECO (over/under/hetero/total), NEE
//    - Daily sums: gppDaily, recoDaily, neeDaily (µmol CO₂ m⁻² d⁻¹)
//    - Supporting hourly arrays: Temperature scales, PAR scales, water stress, VPD scale, phenology scale
//
// UNIT CONVERSIONS:
// - Input solar radiation: W/m² → MJ/m²/h (× 277.78)
// - PAR conversion: Shortwave → PAR (× 0.505 × 4.57 = × 2.31 µmol/J)
// - Output units: µmol CO₂ m⁻² s⁻¹ (hourly), µmol CO₂ m⁻² d⁻¹ (daily sums)
//
// CRITICAL DEPENDENCIES:
// - Phenology: phenoCode controls overstory activity (≥3 = active)
// - VI dynamics: EVI feeds LAI estimates and direct GPP calculation
// - Water stress: Rolling memory (precipitation - ET0) over waterStressDays
// - Respiration smoothing: Prevents unrealistic discontinuities from rapid GPP changes
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Carbon exchanges computation module implementing VPRM for two-layer canopy.
    /// Calculates hourly GPP, RECO, and NEE with phenology-dependent overstory
    /// and always-active understory layers.
    ///
    /// BIOLOGICAL REALISM:
    /// - Overstory represents deciduous trees with seasonal phenology
    /// - Understory represents evergreen/herbaceous layer with year-round activity
    /// - Light competition determines productivity partitioning
    /// - Temperature, water, VPD, and phenology modulate carbon uptake
    /// - Respiration components track autotrophic and heterotrophic processes
    ///
    /// IMPLEMENTATION:
    /// Main method VPRM() orchestrates hourly calculations with pre-computed
    /// daily light interception coefficients for computational efficiency.
    /// Helper methods compute GPP (2 layers) and RECO (3 components).
    /// </summary>
    public class exchanges
    {
        /// <summary>
        /// Unused state variable (legacy code).
        /// RECO smoothing now uses lastRecoTree and lastRecoUnder.
        /// </summary>
        float prevRecoState = float.NaN; // persiste tra i giorni

        /// <summary>
        /// Computes hourly carbon fluxes using Vegetation Photosynthesis Respiration Model (VPRM).
        ///
        /// COMPUTATIONAL WORKFLOW:
        /// 1. Pre-computation (outside hourly loop):
        ///    - Estimate vegetation cover from VI thresholds
        ///    - Compute LAI and EVI for both canopy layers (uses outputT for temporal continuity)
        ///    - Calculate extraterrestrial radiation (top-of-atmosphere solar flux)
        ///    - Compute light extinction coefficients for overstory and understory
        ///    - Determine overstory gap probabilities (direct and diffuse)
        ///
        /// 2. Hourly loop (h = 0 to 23):
        ///    a. RADIATION PARTITIONING:
        ///       - Partition incoming shortwave into direct/diffuse components
        ///       - Convert shortwave to PAR (photosynthetically active radiation)
        ///       - Calculate absorbed PAR for overstory and understory
        ///       - Compute PAR scaling factors for GPP functions
        ///
        ///    b. TEMPERATURE EFFECTS:
        ///       - Overstory leaf temperature = air temperature (simplified)
        ///       - Understory leaf temperature = air temperature (simplified)
        ///       - Compute temperature scalers using symmetric polynomial function
        ///       - Understory uses shifted optimum temperature
        ///
        ///    c. ENVIRONMENTAL MODIFIERS:
        ///       - Water stress: Rolling memory of (precipitation - ET0)
        ///       - Phenology scale: Logistic function of growth percentage
        ///       - VPD scale: Sigmoid response function
        ///
        ///    d. GPP CALCULATION:
        ///       - Overstory GPP (if phenoCode ≥3): With phenology scaling
        ///       - Understory GPP: Always active, no phenology scaling
        ///       - Total GPP = gppOver + gppUnder
        ///
        ///    e. RECO CALCULATION:
        ///       - Compute Lloyd-Taylor temperature responses (3 components)
        ///       - Overstory autotrophic: GPP-dependent with aging function
        ///       - Understory autotrophic: GPP-dependent
        ///       - Heterotrophic soil: Temperature and water dependent
        ///       - Apply exponential smoothing to prevent discontinuities
        ///       - Total RECO = recoOver + recoUnder + recoHetero
        ///
        ///    f. NEE CALCULATION:
        ///       - NEE = RECO - GPP (positive = carbon source, negative = carbon sink)
        ///
        /// 3. Post-processing:
        ///    - Sum hourly fluxes to daily totals (gppDaily, recoDaily, neeDaily)
        ///
        /// CRITICAL IMPLEMENTATION DETAILS:
        /// - LAI estimation uses outputT (previous timestep) for temporal continuity in understory EVI
        /// - Light interception computed once per day (not in hourly loop) for efficiency
        /// - Overstory only active when phenoCode ≥3 (growth, greendown, decline phases)
        /// - Minimum limiting factor approach: min(Wscale, min(VPD, PARscale)) for co-limitation
        /// - Respiration smoothing state (lastRecoTree, lastRecoUnder) persists across hours and days
        ///
        /// PARAMETERS USED:
        /// - parPhotosynthesis: Quantum yields, cardinal temperatures, PAR half-saturation,
        ///   VPD response, water stress thresholds, light extinction, phenology scaling
        /// - parRespiration: Activation energies, reference rates, GPP response coefficients,
        ///   aging factor, smoothing factor
        /// - parVegetationIndex: Min/max VI for vegetation cover estimation
        ///
        /// OUTPUT POPULATION:
        /// - exchanges.LAIoverstory, LAIunderstory: Leaf area indices (m² m⁻²)
        /// - exchanges.EVIoverstory, EVIunderstory: Enhanced vegetation indices
        /// - exchanges.vegetationCover: Fraction of ground covered (0-1)
        /// - exchanges.gpp[h], gppOver[h], gppUnder[h]: Hourly GPP (µmol CO₂ m⁻² s⁻¹)
        /// - exchanges.reco[h], recoOver[h], recoUnder[h], recoHetero[h]: Hourly RECO
        /// - exchanges.nee[h]: Hourly NEE (µmol CO₂ m⁻² s⁻¹)
        /// - exchanges.gppDaily, recoDaily, neeDaily: Daily sums (µmol CO₂ m⁻² d⁻¹)
        /// - Supporting hourly arrays: PARdirect, PARdiffuse, TscaleOver, TscaleUnder,
        ///   WaterStress, vpdScale, phenologyScale, PARscaleOverstory, PARscaleUnderstory
        /// </summary>
        /// <param name="input">Daily weather inputs with hourly disaggregated arrays</param>
        /// <param name="parameters">Photosynthesis and respiration parameters</param>
        /// <param name="output">Previous timestep output (T-1) for LAI temporal continuity and respiration smoothing</param>
        /// <param name="outputT1">Current timestep output (T) to receive carbon flux calculations</param>
        public void VPRM(input input, parameters parameters, output output, output outputT1)
        {
            //estimate vegetation cover
            outputT1.exchanges.vegetationCover = utils.estimateVegetationCover(outputT1, parameters);

            //estimate LAI
            var (LAIoverstory, LAIunderstory, EVIoverstory, EVIunderstory) = utils.estimateLAI(output, outputT1);
            outputT1.exchanges.LAIoverstory = LAIoverstory;
            outputT1.exchanges.LAIunderstory = LAIunderstory;
            outputT1.exchanges.EVIoverstory = EVIoverstory;
            outputT1.exchanges.EVIunderstory = EVIunderstory;

            //estimate hourly extraterrestrial radiation
            float[] extraterrestrialRadiationHourly = utils.astronomy(input, true).etrHourly;

            // maximum air temperature
            float tAirMaxDay = input.airTemperatureMaximum;

            // Constants for PAR conversion (if you start from SW)
            const float FRACTION_PAR_OF_SW = 0.505f;  // ~50.5% of SW is PAR (tunable 0.45–0.5)
            const float UMOL_PER_J = 4.57f;          // µmol per joule
            const float SW_TO_PAR = FRACTION_PAR_OF_SW * UMOL_PER_J;

            // light extinction coefficients
            float kb_over = parameters.parPhotosynthesis.LightExtinctionCoefficient;
            float k_d_over = 0.8f * kb_over;

            // overstory attenuation from LAIover (phenology-aware) ---
            float LAIover_eff = (outputT1.phenoCode < 3) ? 0f : LAIoverstory;

            // light interception
            float Pgap_dir_over = (float)Math.Exp(-kb_over * LAIover_eff); // beam gap prob
            float Pgap_dif_over = (float)Math.Exp(-k_d_over * LAIover_eff); // diffuse transmittance

            // simplification: same extinction coefficients of the overstory
            float kb_under = kb_over;
            float kdif_under = k_d_over;

            float LAIunder_eff = LAIunderstory;

            // light interception from understory
            float lightIntUnderDirect = (1f - (float)Math.Exp(-kb_under * LAIunder_eff));
            float lightIntUndeDiffuse = (1f - (float)Math.Exp(-kdif_under * LAIunder_eff));
            outputT1.exchanges.LightInterceptionUnder = lightIntUnderDirect;

            //hourly loop
            for (int h = 0; h < 24; h++)
            {
                #region Radiation partitioning and PAR conversion 
                // same units 
                float SW_IN = input.solarRadiationH[h]*277.78f;
                float SW_TOA = extraterrestrialRadiationHourly[h];
                var (SW_DIR_H, SW_DIF_H) = utils.PartitionRadiation(SW_IN, SW_TOA);

                // compute PAR
                float PAR_DIR_H = SW_DIR_H * SW_TO_PAR; // direct PAR on horizontal
                float PAR_DIF_H = SW_DIF_H * SW_TO_PAR; // diffuse PAR on horizontal
                outputT1.exchanges.PARdirect.Add(PAR_DIR_H);
                outputT1.exchanges.PARdiffuse.Add(PAR_DIF_H);

     
                // PAR that reaches the TOP of the understory
                float PAR_under_top_dir = PAR_DIR_H * Pgap_dir_over;
                float PAR_under_top_dif = PAR_DIF_H * Pgap_dif_over;

                // PAR absorbed by the OVERSTORY 
                float Iabs_over = (PAR_DIR_H * (1f - Pgap_dir_over)) + (PAR_DIF_H * (1f - Pgap_dif_over));
                outputT1.exchanges.LightOverstory.Add(Iabs_over);

                // understory absorption
                float Iabs_under =
                    PAR_under_top_dir * lightIntUnderDirect +
                    PAR_under_top_dif * lightIntUndeDiffuse;
                outputT1.exchanges.LightUnderstory.Add(Iabs_under);

                // PAR modifier for GPP overstory 
                float PARscaleOverstory = (outputT1.phenoCode < 3)
                    ? 0f
                    : utils.PARGppfunction(outputT1, Iabs_over, parameters.parPhotosynthesis.halfSaturationTree);

                // PAR modifier for GPP understory
                float PARscaleUnderstory = utils.PARGppfunction(outputT1, Iabs_under,
                    parameters.parPhotosynthesis.halfSaturationUnder);

                // add variables to the list
                outputT1.exchanges.PARscaleOverstory.Add(PARscaleOverstory);
                outputT1.exchanges.PARscaleUnderstory.Add(PARscaleUnderstory);
                #endregion

                #region Temperature effects

                //leaves temperature of the overstory
                float LeafTemperatureOver = 0;
                //temperature scale factor (overstory)
                float TscaleOver = 0;
                if (outputT1.phenoCode >= 3)
                {
                    LeafTemperatureOver = input.airTemperatureH[h];

                    TscaleOver = utils.temperatureFunction(LeafTemperatureOver, parameters.parPhotosynthesis.minimumTemperature,
                           parameters.parPhotosynthesis.optimumTemperature, parameters.parPhotosynthesis.maximumTemperature);
                }
                outputT1.exchanges.TleafOver.Add(LeafTemperatureOver);
                outputT1.exchanges.TscaleOver.Add(TscaleOver);

              
                float LeafTemperatureUnder = input.airTemperatureH[h];

                //temperature scale factor (understory)
                float TscaleUnder = utils.temperatureFunction(LeafTemperatureUnder, parameters.parPhotosynthesis.minimumTemperature,
                       parameters.parPhotosynthesis.optimumTemperature - parameters.parPhotosynthesis.pixelTemperatureShift,
                       parameters.parPhotosynthesis.maximumTemperature);

                outputT1.exchanges.TleafUnder.Add(LeafTemperatureUnder);
                outputT1.exchanges.TscaleUnder.Add(TscaleUnder);
                #endregion

                #region Water availability effects
                //compute water stress
                var waterStress = utils.waterStressFunction(outputT1, input, parameters, h);
                outputT1.exchanges.WaterStress.Add(waterStress);
                #endregion

                #region Phenology effect
                //compute phenology effect
                float PhenologyScale = utils.phenologyFunction(outputT1, parameters);
                outputT1.exchanges.phenologyScale.Add(PhenologyScale);
                #endregion

                #region VPD effect
                //compute VPD effect
                float VPDscale = utils.VPDfunction(input.vaporPressureDeficitH[h], parameters);
                outputT1.exchanges.vpdScale.Add(VPDscale);
                #endregion

                #region GPP estimation
                // Gross Primary Production              
                float gppOver = 0f;
                if (outputT1.phenoCode >= 3)
                {
                    float eviOver = EVIoverstory;
                    gppOver = estimateGPPoverstory(parameters, VPDscale, TscaleOver, /*light*/ Iabs_over,
                        PARscaleOverstory, waterStress, PhenologyScale, eviOver);
                }
                //EVI and GPP of the understory
                float eviUnder = EVIunderstory;
                float gppUnder = estimateGPPunderstory(parameters, VPDscale, TscaleUnder,
                    Iabs_under, PARscaleUnderstory, waterStress, eviUnder);

                //pass to the list
                outputT1.exchanges.gppOver.Add(gppOver);
                outputT1.exchanges.gppUnder.Add(gppUnder);
                float gpp = gppUnder + gppOver;
                outputT1.exchanges.gpp.Add(gpp);
                #endregion

                #region Respiration estimation

                //temperature soil scaler
                float TrecoSoil = utils.ComputeTscaleReco(input.airTemperatureH[h], parameters.parRespiration.activationEnergyParameterSoil);
                float TrecoOver = utils.ComputeTscaleReco(input.airTemperatureH[h], parameters.parRespiration.activationEnergyParameterOver);
                float TrecoUnder = utils.ComputeTscaleReco(input.airTemperatureH[h], parameters.parRespiration.activationEnergyParameterUnder);

                float TrecoResponse = TrecoSoil;
                outputT1.exchanges.TscaleReco.Add(TrecoResponse);
                outputT1.exchanges.WscaleReco.Add(waterStress);

                float RECOgppFunctionTree = utils.gppRecoTreeFunction(input, Iabs_over, gppOver, output, outputT1, parameters, h);
                float RECOgppFunctionUnder = utils.gppRecoUnderFunction(input, Iabs_under, gppUnder, outputT1, parameters, h);

                float recoOver = estimateRECOtree(h, input, output, outputT1, 
                    parameters, RECOgppFunctionTree,
                    TrecoOver);

                float recoUnder = estimateRECOunder(h, input, output, outputT1,
                    parameters, RECOgppFunctionUnder, TrecoUnder);

                float recoHetero = estimateRECOHetero(h, input, output, outputT1, 
                    parameters, RECOgppFunctionUnder, TrecoResponse, waterStress);

                float reco = recoUnder + recoOver+recoHetero;

                outputT1.exchanges.PhenologyscaleReco.Add(utils.RecoRespirationFunction(input,
                    outputT1, parameters));
                outputT1.exchanges.recoUnder.Add(recoUnder);
                outputT1.exchanges.recoOver.Add(recoOver);
                outputT1.exchanges.recoHetero.Add(recoHetero);
                outputT1.exchanges.reco.Add(reco);


                #endregion

               
                //Net Ecosystem Exchange
                outputT1.exchanges.nee.Add(reco - gpp);

            }



            // --- Compute daily sums ---
            outputT1.exchanges.gppDaily = outputT1.exchanges.gpp.Sum();
            outputT1.exchanges.recoDaily = outputT1.exchanges.reco.Sum();
            outputT1.exchanges.neeDaily = outputT1.exchanges.recoDaily - outputT1.exchanges.gppDaily;


            //float deltaVI = outputT1.vi - output.vi;

            //var last24 = outputT1.exchanges.nee
            //    .Skip(Math.Max(0, outputT1.exchanges.nee.Count - 24))
            //    .ToArray();

            //if (last24.Length < 24)
            //{
            //    for (int h = 0; h < 24; h++) outputT1.exchanges.viHourly.Add(output.vi);
            //    return;
            //}

            //// --- 1️⃣ Integrazione cumulativa continua ---
            //// Calcola il cumulativo (integrale discreto) di NEE nel giorno
            //float[] cumNEE = new float[24];
            //cumNEE[0] = last24[0];
            //for (int h = 1; h < 24; h++)
            //    cumNEE[h] = cumNEE[h - 1] + last24[h];

            //// --- 2️⃣ Normalizza tra 0 e 1 (range cumulativo giornaliero) ---
            //float minCum = cumNEE.Min();
            //float maxCum = cumNEE.Max();
            //float range = maxCum - minCum;
            //if (range < 1e-6f) range = 1f;

            //float[] fracNEE = cumNEE.Select(v => (v - minCum) / range).ToArray();

            //// --- 3️⃣ Applica ΔVI con il segno corretto ---
            //float sign = Math.Sign(deltaVI);  // positivo = crescita, negativo = perdita

            //outputT1.exchanges.viHourly.Clear();
            //for (int h = 0; h < 24; h++)
            //{
            //    float viHour = output.vi + deltaVI * fracNEE[h];
            //    outputT1.exchanges.viHourly.Add(viHour);
            //}

            //// --- 4️⃣ Correzione finale ---
            //outputT1.exchanges.viHourly[^1] = outputT1.vi;      
        }

        #region GPP functions

        /// <summary>
        /// Computes hourly Gross Primary Production (GPP) for overstory canopy layer.
        ///
        /// MATHEMATICAL FORMULATION:
        /// GPP = QuantumYield × Tscale × min(Wscale, min(VPD, PARscale)) × PAR × Phenology × EVI
        ///
        /// LIMITING FACTOR APPROACH:
        /// Uses minimum of water stress, VPD, and PAR scaling factors (co-limitation).
        /// This represents physiological constraint where most limiting resource determines uptake rate.
        ///
        /// COMPONENTS:
        /// - maximumQuantumYieldOver: Light use efficiency (µmol CO₂ µmol⁻¹ photon)
        /// - Tscale: Temperature response (0-1, symmetric polynomial)
        /// - limitingFactor: min(Wscale, min(VPD, PARscale)) for co-limitation
        /// - par: Absorbed PAR by overstory (µmol photon m⁻² s⁻¹)
        /// - PhenologyScaleGPP: Logistic function of growth percentage (0-1)
        /// - evi: Enhanced vegetation index (overstory fraction)
        ///
        /// PHENOLOGY SCALING:
        /// Phenology scaler modulates GPP during leaf expansion:
        /// - Early growth: Low scaler (partial canopy, immature leaves)
        /// - Full canopy: High scaler (maximum photosynthetic capacity)
        /// - Constant during greendown and decline phases
        ///
        /// UNITS:
        /// Input: PAR (µmol photon m⁻² s⁻¹), EVI (dimensionless 0-1)
        /// Output: GPP (µmol CO₂ m⁻² s⁻¹)
        /// </summary>
        /// <param name="parameters">Photosynthesis parameters including quantum yield</param>
        /// <param name="VPDmodifier">Vapor pressure deficit response (0-1)</param>
        /// <param name="Tscale">Temperature response (0-1)</param>
        /// <param name="par">Absorbed PAR by overstory (µmol photon m⁻² s⁻¹)</param>
        /// <param name="PARscale">PAR saturation response (0-1)</param>
        /// <param name="Wscale">Water stress response (0-1)</param>
        /// <param name="PhenologyScaleGPP">Phenological modifier (0-1)</param>
        /// <param name="evi">Enhanced vegetation index for overstory</param>
        /// <returns>Hourly GPP for overstory (µmol CO₂ m⁻² s⁻¹)</returns>
        private float estimateGPPoverstory(parameters parameters, float VPDmodifier, float Tscale, float par, float PARscale,
            float Wscale, float PhenologyScaleGPP, float evi)
        {
            float limitingFactor = Math.Min(Wscale, Math.Min(VPDmodifier, PARscale));
            float gpp = parameters.parPhotosynthesis.maximumQuantumYieldOver *
                Tscale * limitingFactor * par * PhenologyScaleGPP * evi;
            return gpp;
        }

        /// <summary>
        /// Computes hourly Gross Primary Production (GPP) for understory canopy layer.
        ///
        /// MATHEMATICAL FORMULATION:
        /// GPP = QuantumYield × Tscale × min(Wscale, min(VPD, PARscale)) × PAR × EVI
        ///
        /// DIFFERENCES FROM OVERSTORY:
        /// - No phenology scaling (understory always active)
        /// - Temperature optimum shifted via pixelTemperatureShift parameter
        /// - Uses transmitted light through overstory canopy gaps
        /// - Typically lower quantum yield than overstory
        ///
        /// BIOLOGICAL INTERPRETATION:
        /// Understory represents evergreen or herbaceous vegetation layer that
        /// maintains photosynthetic activity year-round, with productivity limited
        /// by light availability (shading from overstory) and cooler microclimate.
        ///
        /// UNITS:
        /// Input: PAR (µmol photon m⁻² s⁻¹), EVI (dimensionless 0-1)
        /// Output: GPP (µmol CO₂ m⁻² s⁻¹)
        /// </summary>
        /// <param name="parameters">Photosynthesis parameters including quantum yield</param>
        /// <param name="VPDmodifier">Vapor pressure deficit response (0-1)</param>
        /// <param name="Tscale">Temperature response (0-1) with shifted optimum</param>
        /// <param name="par">Absorbed PAR by understory (µmol photon m⁻² s⁻¹)</param>
        /// <param name="PARscale">PAR saturation response (0-1)</param>
        /// <param name="Wscale">Water stress response (0-1)</param>
        /// <param name="evi">Enhanced vegetation index for understory</param>
        /// <returns>Hourly GPP for understory (µmol CO₂ m⁻² s⁻¹)</returns>
        private float estimateGPPunderstory(parameters parameters, float VPDmodifier, float Tscale, float par, float PARscale,
          float Wscale, float evi)
        {
            float limitingFactor = Math.Min(Wscale, Math.Min(VPDmodifier, PARscale));
            float gpp = parameters.parPhotosynthesis.maximumQuantumYieldUnder *
                Tscale * limitingFactor * par * evi;
            return gpp;
        }
        #endregion

        #region Respiration functions

        /// <summary>
        /// Computes hourly heterotrophic soil respiration (RECO hetero).
        ///
        /// BIOLOGICAL PROCESS:
        /// Heterotrophic respiration represents CO₂ release from microbial decomposition
        /// of soil organic matter. Independent of current photosynthesis (unlike autotrophic
        /// respiration), driven by temperature and soil moisture conditions.
        ///
        /// MATHEMATICAL FORMULATION:
        /// RECO_hetero = ReferenceRate × TscaleReco × WaterStress
        ///
        /// LLOYD-TAYLOR TEMPERATURE RESPONSE:
        /// TscaleReco computed using Lloyd-Taylor exponential function with
        /// activationEnergyParameterSoil (typically 100-200 for soil processes).
        ///
        /// WATER STRESS MODULATION:
        /// Soil moisture affects microbial activity and gas diffusion:
        /// - Dry conditions limit microbial respiration
        /// - Optimal moisture maximizes decomposition
        /// - Waterlogged conditions restrict oxygen availability
        ///
        /// PARAMETERS:
        /// - referenceRespirationSoil: Base respiration rate at reference temperature (µmol CO₂ m⁻² s⁻¹)
        /// - activationEnergyParameterSoil: Temperature sensitivity (typically 100-200)
        ///
        /// UNITS:
        /// Output: RECO hetero (µmol CO₂ m⁻² s⁻¹)
        /// </summary>
        /// <param name="h">Hour of day (0-23)</param>
        /// <param name="input">Daily weather inputs</param>
        /// <param name="outputT">Previous timestep output (T-1)</param>
        /// <param name="outputT1">Current timestep output (T)</param>
        /// <param name="parameters">Respiration parameters</param>
        /// <param name="gppTreeRECO">Unused parameter (kept for signature compatibility)</param>
        /// <param name="TscaleReco">Lloyd-Taylor temperature response for soil</param>
        /// <param name="waterScaleReco">Water stress modifier (0-1)</param>
        /// <returns>Hourly heterotrophic respiration (µmol CO₂ m⁻² s⁻¹)</returns>
        private float estimateRECOHetero(int h, input input, output outputT, output outputT1, parameters parameters,
   float gppTreeRECO, float TscaleReco, float waterScaleReco)
        {
            float reco = 0;

            reco = parameters.parRespiration.referenceRespirationSoil * TscaleReco * waterScaleReco;
            return reco;
        }

        /// <summary>
        /// State variable for exponential moving average smoothing of overstory respiration.
        /// Persists across hours and days to prevent discontinuities when GPP changes rapidly
        /// (e.g., nighttime, cloudy conditions, phenological transitions).
        /// </summary>
        private float lastRecoTree = 0f;

        /// <summary>
        /// State variable for exponential moving average smoothing of understory respiration.
        /// Persists across hours and days to maintain temporal continuity in respiration estimates.
        /// </summary>
        private float lastRecoUnder = 0f;

        /// <summary>
        /// Computes hourly autotrophic respiration for overstory with exponential smoothing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Autotrophic respiration represents CO₂ release from plant maintenance and growth
        /// processes. Coupled to current photosynthesis (GPP) with aging modulation during
        /// growing season progression.
        ///
        /// MATHEMATICAL FORMULATION:
        /// RECO_raw = GPP × RespirationResponse × AgingFunction × TscaleReco
        /// RECO_smoothed = RECO_last + α × (RECO_raw - RECO_last)
        ///
        /// EXPONENTIAL MOVING AVERAGE SMOOTHING:
        /// Prevents unrealistic discontinuities when GPP drops rapidly:
        /// - α (smoothingFactor): Typically 0.3-0.7
        /// - α = 0.5 gives half-life of 1 hour
        /// - α = 1.0 disables smoothing (raw values)
        /// - State persists across hours and days via lastRecoTree
        ///
        /// AGING FUNCTION MODULATION:
        /// During growth phase, respiration per unit GPP declines as leaves mature:
        /// - Early growth: High respiration (young, expanding leaves)
        /// - Late growth: Lower respiration (mature, efficient leaves)
        /// - Greendown/decline: Constant respiration response
        ///
        /// PARAMETERS:
        /// - respirationResponseOver: Base GPP-to-respiration conversion factor
        /// - respirationAgingFactor: Controls aging function inflection point
        /// - respirationSmoothingFactor: Exponential smoothing coefficient (α)
        /// - activationEnergyParameterOver: Temperature sensitivity (typically 50-150)
        ///
        /// UNITS:
        /// Input: gppTreeRECO (µmol CO₂ m⁻² s⁻¹)
        /// Output: RECO overstory (µmol CO₂ m⁻² s⁻¹)
        /// </summary>
        /// <param name="h">Hour of day (0-23)</param>
        /// <param name="input">Daily weather inputs</param>
        /// <param name="outputT">Previous timestep output (T-1)</param>
        /// <param name="outputT1">Current timestep output (T)</param>
        /// <param name="parameters">Respiration parameters including smoothing factor</param>
        /// <param name="gppTreeRECO">GPP-dependent respiration (raw, before smoothing)</param>
        /// <param name="TscaleReco">Lloyd-Taylor temperature response</param>
        /// <returns>Hourly overstory autotrophic respiration (µmol CO₂ m⁻² s⁻¹)</returns>
        private float estimateRECOtree(int h, input input, output outputT, output outputT1,
            parameters parameters, float gppTreeRECO, float TscaleReco)
        {
            float reco_raw = gppTreeRECO;

            // applica smoothing process-based
            float alpha = parameters.parRespiration.respirationSmoothingFactor;

            float reco = lastRecoTree + alpha * (reco_raw - lastRecoTree);
            lastRecoTree = reco;

            return reco;
        }

        /// <summary>
        /// Computes hourly autotrophic respiration for understory with exponential smoothing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Understory autotrophic respiration similar to overstory, but:
        /// - No phenological aging (understory always active)
        /// - Typically cooler microclimate (lower respiration rates)
        /// - Light-limited GPP results in lower respiration demand
        ///
        /// MATHEMATICAL FORMULATION:
        /// RECO_raw = GPP × RespirationResponse × TscaleReco
        /// RECO_smoothed = RECO_last + α × (RECO_raw - RECO_last)
        ///
        /// EXPONENTIAL MOVING AVERAGE SMOOTHING:
        /// Same smoothing approach as overstory to maintain temporal continuity
        /// and prevent discontinuities during rapid environmental changes.
        ///
        /// PARAMETERS:
        /// - respirationResponseUnder: Base GPP-to-respiration conversion factor
        /// - respirationSmoothingFactor: Exponential smoothing coefficient (α)
        /// - activationEnergyParameterUnder: Temperature sensitivity (typically 50-150)
        ///
        /// UNITS:
        /// Input: gppUnderRECO (µmol CO₂ m⁻² s⁻¹)
        /// Output: RECO understory (µmol CO₂ m⁻² s⁻¹)
        /// </summary>
        /// <param name="h">Hour of day (0-23)</param>
        /// <param name="input">Daily weather inputs</param>
        /// <param name="outputT">Previous timestep output (T-1)</param>
        /// <param name="outputT1">Current timestep output (T)</param>
        /// <param name="parameters">Respiration parameters including smoothing factor</param>
        /// <param name="gppUnderRECO">GPP-dependent respiration (raw, before smoothing)</param>
        /// <param name="TscaleReco">Lloyd-Taylor temperature response</param>
        /// <returns>Hourly understory autotrophic respiration (µmol CO₂ m⁻² s⁻¹)</returns>
        private float estimateRECOunder(int h, input input, output outputT, output outputT1,
            parameters parameters, float gppUnderRECO, float TscaleReco)
        {
            float reco_raw = gppUnderRECO;

            float alpha = parameters.parRespiration.respirationSmoothingFactor;

            float reco = lastRecoUnder + alpha * (reco_raw - lastRecoUnder);
            lastRecoUnder = reco;

            return reco;
        }

        #endregion

    }
}
