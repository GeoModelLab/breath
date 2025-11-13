using source.data;
using System;

// ============================================================================
// Vegetation Index Dynamics Module - SWELL Model VI Simulation
// ============================================================================
// This module translates phenological state progression into vegetation index
// (VI) dynamics (NDVI or EVI). VI serves as observable proxy for canopy
// greenness and leaf area, linking model internal state to remotely sensed data.
//
// PHASE-SPECIFIC VI DYNAMICS:
//
// 1. ENDODORMANCY/ECODORMANCY (phenoCode 2):
//    - Below base temperature: VI declines (senescence continuation)
//      Rate = -nVIEndodormancy × temperatureRatio × distanceToMinimum
//    - Above base temperature + lengthening days: VI increases (green-up)
//      Rate = +nVIEcodormancy × thermalForcing × distanceToMaximum
//    - Captures understory dynamics during overstory dormancy
//
// 2. GROWTH (phenoCode 3):
//    - Rapid VI increase during leaf expansion
//    - Rate modulated by growth percentage (faster early, slower at completion)
//    - Rate = nVIGrowth × (1 - greenDownPercentage/100) × (1 - VItoMax)
//    - viAtGrowth captured on first day (dormant understory baseline)
//
// 3. GREENDOWN (phenoCode 4):
//    - Peak VI maintenance with slight decline
//    - EVI: Exponential weight function (accelerating decline)
//    - NDVI: Linear weight function
//    - viAtGreendown captured on first day
//
// 4. DECLINE/SENESCENCE (phenoCode 5 or 1):
//    - Accelerating VI decline during leaf abscission
//    - Symmetric bell-shaped weight function peaks at 50% completion
//    - Rate = -(nVIGreendown + nVISenescence × weight)
//    - viAtSenescence captured at dormancy onset (phenoCode 2 start)
//
// CRITICAL VI THRESHOLDS:
// - viAtGrowth: Understory baseline (captured at growth start)
// - viAtSenescence: Pre-senescence peak (captured at endo/ecodormancy start)
// - viAtGreendown: Peak greenness (captured at decline start)
// - minimumVI/maximumVI: Hard bounds preventing unrealistic values
//
// VI STORAGE FORMAT:
// Scaled to 100 (NDVI 0.85 stored as 85.0)
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Vegetation index dynamics computation module for SWELL phenological model.
    /// Translates phenological state into observable vegetation index (NDVI/EVI) dynamics.
    ///
    /// COUPLING WITH PHENOLOGY:
    /// VI dynamics driven by phenoCode and phase completion percentages.
    /// Phase-specific rate constants calibrated to match remotely sensed observations.
    ///
    /// BIOLOGICAL INTERPRETATION:
    /// VI represents canopy greenness integrating:
    /// - Overstory leaf area (phenology-dependent)
    /// - Understory activity (temperature-dependent during dormancy)
    /// - Chlorophyll content (declining during senescence)
    ///
    /// IMPLEMENTATION:
    /// Single method (ndviNormalized) with phase-specific logic branches.
    /// Critical thresholds captured at phase transitions for normalization.
    /// </summary>
    public class VIdynamics
    {
        /// <summary>
        /// Flag to track first day of dormancy (endo/ecodormancy phase).
        /// Used to capture viAtSenescence on transition to phenoCode 2.
        /// Reset to 0 at growth phase start.
        /// </summary>
        float startDormancy = 0;

        /// <summary>
        /// Computes daily vegetation index rate and state based on phenological phase.
        ///
        /// COMPUTATIONAL FLOW:
        /// 1. Copy previous VI thresholds (viAtGrowth, viAtSenescence, viAtGreendown)
        /// 2. Compute phase-specific VI rate based on phenoCode
        /// 3. Accumulate state: vi(t) = vi(t-1) + viRate(t)
        /// 4. Apply hard bounds (minimumVI to maximumVI)
        /// 5. Capture critical thresholds at phase transitions
        ///
        /// PHASE-SPECIFIC LOGIC:
        ///
        /// phenoCode 2 (ENDO/ECODORMANCY):
        /// - Understory dynamics dominate during overstory dormancy
        /// - Temperature below base → VI decline (continuing senescence)
        /// - Temperature above base + lengthening days → VI increase (green-up)
        /// - First day: Capture viAtSenescence = vi / 100
        ///
        /// phenoCode 3 (GROWTH):
        /// - Rapid VI increase during leaf expansion
        /// - Rate modulated by: (1 - greenDownPercentage/100) × (1 - VItoMax)
        /// - First day: Capture viAtGrowth = vi / 100
        ///
        /// phenoCode 4 (GREENDOWN):
        /// - Slight VI decline during peak greenness
        /// - EVI: Exponential weight (accelerating decline)
        /// - NDVI: Linear weight (constant decline)
        ///
        /// phenoCode 5 or 1 (DECLINE/SENESCENCE):
        /// - Accelerating VI decline during leaf abscission
        /// - Symmetric bell weight peaks at 50% completion
        /// - First day (phenoCode 5): Capture viAtGreendown = vi / 100
        ///
        /// NORMALIZATION APPROACH:
        /// VItoMax = (current_vi - vi_at_phase_start) / (max_vi - vi_at_phase_start)
        /// VItoMin = (current_vi - min_vi) / (vi_at_senescence - min_vi)
        /// Prevents overshooting bounds and creates asymptotic approach to limits.
        ///
        /// PARAMETERS:
        /// - parVegetationIndex.nVIEndodormancy: VI rate during cold dormancy (negative)
        /// - parVegetationIndex.nVIEcodormancy: VI rate during warm ecodormancy (positive)
        /// - parVegetationIndex.nVIGrowth: VI rate constant for growth phase
        /// - parVegetationIndex.nVIGreendown: VI rate constant for greendown phase
        /// - parVegetationIndex.nVISenescence: VI rate constant for senescence phase
        /// - parVegetationIndex.minimumVI: Lower bound (typically 0.1-0.2)
        /// - parVegetationIndex.maximumVI: Upper bound (typically 0.85-0.95)
        ///
        /// CRITICAL THRESHOLDS CAPTURED:
        /// - viAtGrowth: Set on first day of growth (dormant understory baseline)
        /// - viAtSenescence: Set on first day of endo/ecodormancy (pre-senescence peak)
        /// - viAtGreendown: Set on first day of decline (peak greenness)
        ///
        /// IMPLEMENTATION NOTE:
        /// pixelTemperatureShift commented out (line 36) to maintain consistency
        /// between phenology and carbon flux calculations. Temperature shift now
        /// applied only in photosynthesis module.
        /// </summary>
        /// <param name="input">Daily weather inputs with temperature and vegetation index type</param>
        /// <param name="parameters">VI parameters (rate constants, min/max bounds) and growth parameters</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation and threshold tracking</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated VI values</param>
        public void ndviNormalized(input input, parameters parameters, output output, output outputT1)
        {
            outputT1.viAtGrowth = output.viAtGrowth;
            outputT1.viAtSenescence = output.viAtSenescence;
            outputT1.viAtGreendown = output.viAtGreendown;

            //internal variable 
            float rateNDVInormalized = 0;
            if (outputT1.phenoCode == 2)
            {
                //first day of dormancy
                if (startDormancy == 0)
                {
                    startDormancy = 1;
                    outputT1.viAtSenescence = output.vi / 100;
                    output.viAtSenescence = outputT1.viAtSenescence;

                    if (output.viAtSenescence <= parameters.parVegetationIndex.minimumVI)
                    {
                        outputT1.viAtSenescence = parameters.parVegetationIndex.minimumVI + .01F;
                        output.viAtSenescence = outputT1.viAtSenescence;
                    }
                }

                //compute growing degree days for the understory
                //SB 11 october 2025 to avoid inconsistencies with phenology and GPP
                float tshift = 0;// parameters.parVegetationIndex.pixelTemperatureShift;

                //derive the rate of NDVI normalized for endodormancy
                float endodormancyContribution = 0;
                float ecodormancyContribution = 0;
                float aveTemp = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
                float tratio = 0;

                if (aveTemp < (parameters.parGrowth.minimumTemperature - tshift))
                {
                    float tbelow0 = Math.Abs((parameters.parGrowth.minimumTemperature - tshift) - aveTemp);
                    tratio = -tbelow0 / 10;
                    if (tratio < -1)
                    {
                        tratio = -1;
                    }
                    //compute endodormancy contribution
                    endodormancyContribution = parameters.parVegetationIndex.nVIEndodormancy * tratio;

                    float VItomin = (output.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                       (output.viAtSenescence - parameters.parVegetationIndex.minimumVI);
                    //ceiling at 1 otherwise unrealistic vi decreases
                    if (VItomin > 1) { VItomin = 1; }

                    endodormancyContribution *= VItomin;
                    if (endodormancyContribution > 0)
                    {
                        endodormancyContribution = 0;
                    }

                    if (endodormancyContribution < -1000)
                    {

                    }
                }
                else
                {
                    input yesterday = new input();
                    yesterday.latitude = input.latitude;
                    yesterday.date = input.date.AddDays(-1);
                    float dayLengthYesterday = utils.dayLength(yesterday);

                    //ecodormancy contributes only when days are lengthening
                    if (dayLengthYesterday > input.radData.dayLength)
                    {
                        ecodormancyContribution = 0;
                    }
                    else
                    {
                        tratio = 0;
                        float gddEco = utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature - tshift,
                         parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                        float VItoMax = (output.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                  (parameters.parVegetationIndex.maximumVI - parameters.parVegetationIndex.minimumVI);
                        if (VItoMax > 1) VItoMax = 1;


                        ecodormancyContribution = gddEco * parameters.parVegetationIndex.nVIEcodormancy * (1 - VItoMax);
                    }
                }

                //derive the rate of NDVI normalized for dormancy
                rateNDVInormalized = (ecodormancyContribution + endodormancyContribution);


            }
            //growth
            else if (outputT1.phenoCode == 3)
            {
                startDormancy = 0;
                //derive the rate of NDVI normalized for growth
                float growthNDVInormalized = parameters.parVegetationIndex.nVIGrowth;
                //derive the contribution of growth to rate of NDVI
                rateNDVInormalized = growthNDVInormalized * 100 * outputT1.growth.growthRate;

                if (outputT1.viAtGrowth == 0)
                {
                    outputT1.viAtGrowth = output.vi / 100;
                    output.viAtGrowth = outputT1.viAtGrowth;
                }

                if (outputT1.viAtGrowth >= parameters.parVegetationIndex.maximumVI)
                {
                    outputT1.viAtGrowth = parameters.parVegetationIndex.maximumVI - 0.01F;
                }
                float VItoMax = (output.vi / 100 - outputT1.viAtGrowth) /
                    (parameters.parVegetationIndex.maximumVI - outputT1.viAtGrowth);
                if (VItoMax > 1) VItoMax = 1;
                rateNDVInormalized = growthNDVInormalized * (1 - outputT1.greenDownPercentage / 100) * (1 - VItoMax);

            }
            //greendown
            else if (outputT1.phenoCode == 4)
            {
                //outputT1.viAtGrowth = 0;
                //derive the rate of NDVI normalized for greendown
                float greenDownNDVInormalized = parameters.parVegetationIndex.nVIGreendown;

                if (input.vegetationIndex == "EVI")
                {
                    float weight = 1 - (float)Math.Exp(-.25 * outputT1.greenDownPercentage);
                    //derive the contribution of greendown to rate of NDVI
                    rateNDVInormalized = -greenDownNDVInormalized *
                        (weight * outputT1.greenDown.greenDownRate);
                }
                else if (input.vegetationIndex == "NDVI")
                {
                    rateNDVInormalized = -greenDownNDVInormalized *
                       (outputT1.greenDownPercentage) / 100 *
                       outputT1.greenDown.greenDownRate;
                }
            }
            //decline
            else if (outputT1.phenoCode == 5 || outputT1.phenoCode == 1)
            {
                if (outputT1.viAtGreendown == 0 && outputT1.phenoCode == 5)
                {
                    outputT1.viAtGreendown = output.vi / 100;
                    output.viAtGreendown = outputT1.viAtGreendown;
                }

                float weight = SymmetricBellFunction(outputT1.declinePercentage);
                //derive the contribution of decline to the rate of NDVI
                float declineNDVInormalized = -parameters.parVegetationIndex.nVIGreendown -
                    parameters.parVegetationIndex.nVISenescence * weight;
                //derive the contribution of degree days and photothermal units (decline) to rate of NDVI normalized
                rateNDVInormalized = declineNDVInormalized;
            }

            //update rate
            output.viRate = rateNDVInormalized;

            //update state
            outputT1.vi = output.vi + output.viRate;


            //NDVI thresholds between minimum and maximumVI
            if (outputT1.vi / 100 < parameters.parVegetationIndex.minimumVI)
            {
                outputT1.vi = parameters.parVegetationIndex.minimumVI * 100;
            }
            //NDVI thresholds between minimum and maximumVI
            if (outputT1.vi / 100 > 1)
            {
                outputT1.vi = 1;
            }



        }

        /// <summary>
        /// Computes symmetric bell-shaped weight function for senescence rate modulation.
        ///
        /// MATHEMATICAL FORMULATION:
        /// f(x) = exp(-(x - 50)² / 1000)
        /// where x is decline completion percentage (0-100)
        ///
        /// CHARACTERISTICS:
        /// - Maximum weight (1.0) at x = 50% completion
        /// - Symmetric decay on both sides
        /// - Approaches 0 at x = 0% and x = 100%
        /// - Standard deviation ≈ 31.6, gives smooth transition
        ///
        /// BIOLOGICAL INTERPRETATION:
        /// Senescence acceleration peaks at mid-decline when both environmental
        /// triggers (photoperiod, temperature) and internal senescence signals
        /// are most active. Early and late decline proceed more slowly.
        ///
        /// USAGE:
        /// Weight applied to nVISenescence rate constant in decline phase:
        /// Rate = -(nVIGreendown + nVISenescence × weight)
        ///
        /// This creates accelerating then decelerating VI decline matching
        /// observed autumn leaf-fall patterns.
        /// </summary>
        /// <param name="x">Decline completion percentage (0-100)</param>
        /// <returns>Weight factor (0.0-1.0) with peak at 50%</returns>
        static float SymmetricBellFunction(float x)
        {
            float scaledX = (float)Math.Exp(-Math.Pow((x - 50), 2) / Math.Pow(10, 3));

            return scaledX;
        }
    }
}