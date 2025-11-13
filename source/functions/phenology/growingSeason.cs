using source.data;
using System;

// ============================================================================
// Growing Season Module - SWELL Model Active Phenology Computations
// ============================================================================
// This module implements the three sequential phases of the active growing season:
//
// 1. GROWTH (phenoCode 3):
//    Leaf expansion driven by thermal forcing (Yan & Hunt 1999)
//    Accumulated thermal units from ecodormancy completion to full canopy
//
// 2. GREENDOWN (phenoCode 4):
//    Peak greenness maintenance period
//    Constant thermal accumulation with no environmental modulation
//    Represents mature canopy before senescence initiation
//
// 3. DECLINE/SENESCENCE (phenoCode 5):
//    Weighted thermal + photothermal forcing for leaf senescence
//    Progressive shift from thermal to photothermal dominance
//    Rate = thermalUnit × (1 - progress) + photoThermalUnit × progress
//
// COMPUTATIONAL PATTERN:
// All phases follow rate-state accumulation with threshold-based transitions.
// Growth and greendown use pure thermal forcing (Yan & Hunt 1999).
// Decline uses weighted combination that shifts dominance as phase progresses.
//
// STATE MANAGEMENT:
// - outputT: Previous timestep (T-1) provides state for accumulation
// - outputT1: Current timestep (T) receives updated state and rates
// - Boolean flags prevent re-entry and control phase transitions
// - Dormancy induction state reset at growth completion
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Growing season computation module for SWELL phenological model.
    /// Implements sequential active season phases: growth, greendown, decline/senescence.
    ///
    /// PHASE SEQUENCE:
    /// Ecodormancy completion → Growth → Greendown → Decline → Dormancy induction
    ///
    /// CRITICAL INTERACTIONS:
    /// - Growth percentage modulates VI dynamics and respiration aging
    /// - Greendown completion triggers decline phase and resets dormancy flags
    /// - Decline percentage drives weighted thermal-photothermal forcing
    ///
    /// IMPLEMENTATION:
    /// Growth and greendown use Yan & Hunt (1999) thermal forcing.
    /// Decline uses adaptive weighted combination of thermal and photothermal forcing.
    /// </summary>
    public class growingSeason
    {
        /// <summary>
        /// Computes daily growth rate and state using thermal forcing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Leaf expansion phase from bud burst to full canopy development.
        /// Driven purely by temperature accumulation (thermal units).
        /// Represents spring leaf-out period in deciduous vegetation.
        ///
        /// MATHEMATICAL MODEL (Yan & Hunt 1999):
        /// Rate = f(Tmin, Topt, Tmax) where:
        /// - f = 0 when T ≤ Tmin or T ≥ Tmax
        /// - f maximized at T = Topt
        /// - Non-linear response with asymmetric curves
        ///
        /// State(t) = State(t-1) + Rate(t)
        /// Percentage = (State / thermalThreshold) × 100
        ///
        /// PHASE TRANSITION:
        /// When percentage ≥ 100%:
        /// - isGrowthCompleted = true
        /// - phenoCode = 3
        /// - dormancyInductionState reset to 0 (prevents carryover)
        /// - ecodormancyRate and endodormancyRate set to 0
        /// - endodormancyState and endodormancyPercentage reset to 0
        ///
        /// RATE GATING:
        /// Growth rate set to 0 when state ≥ threshold to prevent overshooting.
        /// State clamped to threshold upon completion.
        ///
        /// CRITICAL COUPLING:
        /// Growth percentage modulates:
        /// - Vegetation index growth rate (inverse relationship)
        /// - Respiration aging function via phenologyFunction()
        /// - GPP phenology scaler (logistic function of growth percentage)
        ///
        /// PARAMETERS:
        /// - parGrowth.thermalThreshold: Cumulative thermal units required
        /// - parGrowth.minimumTemperature: Base temperature (°C)
        /// - parGrowth.optimumTemperature: Optimal temperature (°C)
        /// - parGrowth.maximumTemperature: Maximum temperature (°C)
        ///
        /// IMPLEMENTATION:
        /// See utils.forcingUnitFunction() for Yan & Hunt (1999) thermal forcing
        /// </summary>
        /// <param name="input">Daily weather inputs with temperature (min/max)</param>
        /// <param name="parameters">Growth parameters (thermal threshold, cardinal temperatures)</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void growthRate(input input, parameters parameters, output output, output outputT1)
        {
            //check if the growth phenophase is not completed and ecodormancy is completed
            if (!outputT1.isGrowthCompleted && outputT1.isEcodormancyCompleted) 
            {
                //check if the growth state is below the critical threshold
                if (output.growth.growthState < parameters.parGrowth.thermalThreshold)
                {
                    //compute growth rate
                    outputT1.growth.growthRate =
                            utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                            parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);
                }
                else
                {
                    outputT1.growth.growthRate = 0;
                }

                //update the growth state
                outputT1.growth.growthState =  output.growth.growthState + outputT1.growth.growthRate;

                //update phenological code
                if (outputT1.growth.growthState > 0 && outputT1.ecodormancyPercentage==100)
                {
                    outputT1.ecodormancy.ecodormancyRate = 0;
                    outputT1.endodormancy.endodormancyRate = 0;
                    outputT1.endodormancy.endodormancyState = 0;
                    outputT1.endodormancyPercentage = 0;
                    outputT1.phenoCode = 3;                    
                }

                //if growth state is above the threshold, set it to the critical threshold
                if (outputT1.growth.growthState > parameters.parGrowth.thermalThreshold &&
                    !outputT1.isGrowthCompleted)
                {
                    outputT1.growth.growthState = parameters.parGrowth.thermalThreshold;
                    outputT1.dormancyInduction.photoThermalDormancyInductionState = 0;
                    outputT1.isGrowthCompleted = true;
                }

                //compute the completion percentage of the growth state
                outputT1.growthPercentage = outputT1.growth.growthState /
                parameters.parGrowth.thermalThreshold *100;
            }
            else //otherwise growth percentage is kept to the previous value
            {
                outputT1.growthPercentage = output.growthPercentage;
            }
        }

        /// <summary>
        /// Computes daily greendown rate and state using thermal forcing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Peak greenness maintenance period after full canopy development.
        /// Mature leaf period with stable photosynthetic capacity.
        /// Represents summer period in deciduous vegetation before senescence.
        ///
        /// MATHEMATICAL MODEL:
        /// Rate = f(Tmin, Topt, Tmax) (Yan & Hunt 1999)
        /// Same thermal forcing as growth phase with same cardinal temperatures.
        /// State(t) = State(t-1) + Rate(t)
        /// Percentage = (State / thermalThreshold) × 100
        ///
        /// PHASE TRANSITION:
        /// When percentage ≥ 100%:
        /// - isGreendownCompleted = true
        /// - isDormancyInduced reset to false (allows new dormancy cycle)
        /// - greenDownRate set to 0 (stops accumulation)
        /// - phenoCode remains 4 until decline phase begins
        ///
        /// CRITICAL DIFFERENCE FROM GROWTH:
        /// Greendown has separate threshold (parGreendown.thermalThreshold) but
        /// uses same cardinal temperatures (parGrowth.minimumTemperature, etc.).
        /// This allows calibration of peak greenness duration independent of
        /// leaf expansion rate.
        ///
        /// PARAMETERS:
        /// - parGreendown.thermalThreshold: Cumulative thermal units required
        /// - parGrowth.minimumTemperature: Base temperature (°C, shared with growth)
        /// - parGrowth.optimumTemperature: Optimal temperature (°C, shared with growth)
        /// - parGrowth.maximumTemperature: Maximum temperature (°C, shared with growth)
        ///
        /// IMPLEMENTATION:
        /// See utils.forcingUnitFunction() for Yan & Hunt (1999) thermal forcing
        /// </summary>
        /// <param name="input">Daily weather inputs with temperature (min/max)</param>
        /// <param name="parameters">Greendown and growth parameters (thresholds, cardinal temperatures)</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void greendownRate(input input, parameters parameters,
          output output, output outputT1)
        {
            //check if the growth phenophase is  completed and greendown is not completed
            if (outputT1.growthPercentage == 100 && !outputT1.isGreendownCompleted)
            {
                //compute thermal unit (call to an external function in the utils static class)
                outputT1.greenDown.greenDownRate =
                        utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                        parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                //update greendown state variable
                outputT1.greenDown.greenDownState = output.greenDown.greenDownState +
                    outputT1.greenDown.greenDownRate;

                //update greendown percentage
                outputT1.greenDownPercentage = outputT1.greenDown.greenDownState /
                    parameters.parGreendown.thermalThreshold * 100;

                //limit the greendown percentage to 100
                if (outputT1.greenDownPercentage >= 100)
                {
                    outputT1.greenDownPercentage = 100;
                    outputT1.isGreendownCompleted = true;
                    outputT1.isDormancyInduced = false;
                    outputT1.greenDown.greenDownRate = 0;
                }

                //update phenological code
                if (!outputT1.isGreendownCompleted)
                {
                    outputT1.phenoCode = 4;
                }
            }
        }

        /// <summary>
        /// Computes daily decline/senescence rate and state using weighted thermal-photothermal forcing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Leaf senescence and abscission phase triggered by autumn conditions.
        /// Combined response to cooling temperatures and shortening photoperiods.
        /// Represents autumn leaf-fall period in deciduous vegetation.
        ///
        /// MATHEMATICAL MODEL (Weighted Adaptive):
        /// thermalUnit = f(Tmin, Topt, Tmax) (Yan & Hunt 1999)
        /// photoThermalUnit = photoperiodFunction × temperatureFunction (sigmoid functions)
        /// progress = declineState(t-1) / photoThermalThreshold
        ///
        /// Rate = thermalUnit × (1 - progress) + photoThermalUnit × progress
        ///
        /// State(t) = State(t-1) + Rate(t)
        /// Percentage = (State / photoThermalThreshold) × 100
        ///
        /// ADAPTIVE WEIGHTING MECHANISM:
        /// Early decline (progress ≈ 0): Thermal forcing dominates (70% weight)
        /// Mid decline (progress ≈ 0.5): Balanced forcing (50/50 weight)
        /// Late decline (progress ≈ 1): Photothermal forcing dominates (30/70 weight)
        ///
        /// This creates biological realism where initial senescence responds to
        /// temperature stress, but accelerates as photoperiod becomes limiting.
        ///
        /// PHASE TRANSITION:
        /// When percentage ≥ 100%:
        /// - isDeclineCompleted = true
        /// - isDormancyInduced reset to false (allows new dormancy induction)
        /// - greenDownRate and declineRate set to 0 (stops all accumulation)
        /// - phenoCode remains 5 until dormancy induction begins
        ///
        /// PHOTOTHERMAL FUNCTIONS (from dormancy induction):
        /// photoperiodFunction: Sigmoid response to day length (high when days short)
        /// temperatureFunction: Sigmoid response to temperature (high when cool)
        /// Uses same parameters as dormancy induction (parDormancyInduction)
        ///
        /// PARAMETERS:
        /// - parSenescence.photoThermalThreshold: Cumulative units required
        /// - parGrowth.minimumTemperature: Base temperature (°C) for thermal component
        /// - parGrowth.optimumTemperature: Optimal temperature (°C) for thermal component
        /// - parGrowth.maximumTemperature: Maximum temperature (°C) for thermal component
        /// - parDormancyInduction.photoperiodThreshold: Day length threshold (hours)
        /// - parDormancyInduction.photoperiodSensitivity: Sigmoid steepness
        /// - parDormancyInduction.temperatureThreshold: Temperature threshold (°C)
        /// - parDormancyInduction.temperatureSensitivity: Sigmoid steepness
        ///
        /// IMPLEMENTATION:
        /// See utils.forcingUnitFunction() for thermal forcing
        /// See utils.photoperiodFunctionInduction() for photoperiod sigmoid
        /// See utils.temperatureFunctionInduction() for temperature sigmoid
        /// </summary>
        /// <param name="input">Daily weather inputs with date, latitude, and temperature</param>
        /// <param name="parameters">Senescence, growth, and dormancy induction parameters</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation and progress calculation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void declineRate(input input, parameters parameters,
           output output,  output outputT1)
        {
            //check if the greendown phase is completed and the decline phase is not completed
            if (outputT1.greenDownPercentage == 100 && !outputT1.isDeclineCompleted)
            {
                //compute thermal unit
                float thermalUnit =
                        utils.forcingUnitFunction(input, parameters.parGrowth.minimumTemperature,
                        parameters.parGrowth.optimumTemperature, parameters.parGrowth.maximumTemperature);

                //compute rad data
                input.radData = utils.astronomy(input, false);
                //call photoperiod function
                float photoFunction = utils.photoperiodFunctionInduction(input, parameters, outputT1);
                float tempFunction = utils.temperatureFunctionInduction(input, parameters, outputT1);
                float induPhotoThermal = photoFunction * tempFunction;

                //compute the percentage completion of the decline phase before updating, to compute the weighted average
                float declinePercentageYesterday = output.decline.declineState /
                    parameters.parSenescence.photoThermalThreshold;

                //compute the weighted average of the decline rate
                outputT1.decline.declineRate = thermalUnit * (1 - declinePercentageYesterday) +
                     induPhotoThermal * declinePercentageYesterday;

                //state variable
                outputT1.decline.declineState = output.decline.declineState +
                    outputT1.decline.declineRate;

                //update decline percentage
                outputT1.declinePercentage = outputT1.decline.declineState /
                    parameters.parSenescence.photoThermalThreshold * 100;

                //limit the decline percentage to 100
                if (outputT1.declinePercentage >= 100)
                {
                    outputT1.declinePercentage = 100;
                    outputT1.isDeclineCompleted = true;
                    outputT1.isDormancyInduced = false;
                    outputT1.greenDown.greenDownRate = 0;
                    outputT1.decline.declineRate = 0;
                }

                //update the phenological code
                if (!outputT1.isDeclineCompleted)
                {
                    outputT1.phenoCode = 5;
                }
            }
        }      
    }
}