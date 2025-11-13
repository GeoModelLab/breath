using source.data;
using System.Collections.Generic;

// ============================================================================
// Dormancy Season Module - SWELL Model Phenological Computations
// ============================================================================
// This module implements the three sequential phases of winter dormancy:
//
// 1. DORMANCY INDUCTION (phenoCode 0→1):
//    Photoperiod × temperature forcing triggers entry into dormancy
//    Multiplicative photothermal model with sigmoid limiting functions
//
// 2. ENDODORMANCY (phenoCode 1→2 progression):
//    Chilling accumulation using Utah model variant
//    Hourly integration (24 chilling units per day)
//    Completion modulates ecodormancy asymptote
//
// 3. ECODORMANCY (phenoCode 2→3):
//    Photothermal forcing for dormancy release
//    Asymptote scaled by endodormancy completion percentage
//    Resets growth/decline flags upon completion
//
// COMPUTATIONAL PATTERN:
// Rate computed from environmental drivers → accumulated to state →
// compared to threshold → percentage computed → phase transition at ≥100%
//
// STATE MANAGEMENT:
// - outputT: Previous timestep (T-1) provides state for accumulation
// - outputT1: Current timestep (T) receives updated state and rates
// - Boolean flags prevent re-entry into completed phases
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Dormancy season computation module for SWELL phenological model.
    /// Implements sequential dormancy phases: induction, endodormancy, ecodormancy.
    ///
    /// PHASE SEQUENCE:
    /// Active growth → Dormancy induction → Endodormancy → Ecodormancy → Growth resumption
    ///
    /// CRITICAL INTERACTIONS:
    /// - Endodormancy completion scales ecodormancy asymptote
    /// - Photoperiod thresholds control induction timing
    /// - Chilling accumulation gates spring phenology
    ///
    /// IMPLEMENTATION:
    /// All methods follow rate-state accumulation pattern with threshold-based transitions.
    /// </summary>
    public class dormancySeason
    {
        #region dormancy induction

        /// <summary>
        /// Computes daily dormancy induction rate and state using photothermal forcing.
        ///
        /// BIOLOGICAL PROCESS:
        /// Entry into winter dormancy triggered by short photoperiods and cool temperatures
        /// in late summer/autumn. Prevents premature growth during favorable conditions.
        ///
        /// MATHEMATICAL MODEL:
        /// Rate = photoperiodFunction(dayLength) × temperatureFunction(Tmin, Tmax)
        /// State(t) = State(t-1) + Rate(t)
        /// Percentage = (State / threshold) × 100
        ///
        /// PHOTOPERIOD FUNCTION (sigmoid):
        /// f(L) = 1 / (1 + exp(sensitivity × (L - threshold)))
        /// High rate when day length < threshold, approaches 0 as days lengthen
        ///
        /// TEMPERATURE FUNCTION (sigmoid):
        /// f(T) = 1 / (1 + exp(sensitivity × (Tavg - threshold)))
        /// High rate when temperature < threshold, approaches 0 as temperature rises
        ///
        /// PHASE TRANSITION:
        /// When percentage ≥ 100%:
        /// - isDormancyInduced = true
        /// - phenoCode = 1
        /// - ecodormancyState reset to 0 (prevents carryover from previous year)
        ///
        /// PARAMETERS:
        /// - parDormancyInduction.photoThermalThreshold: Cumulative units required
        /// - parDormancyInduction.photoperiodThreshold: Day length threshold (hours)
        /// - parDormancyInduction.photoperiodSensitivity: Sigmoid steepness
        /// - parDormancyInduction.temperatureThreshold: Temperature threshold (°C)
        /// - parDormancyInduction.temperatureSensitivity: Sigmoid steepness
        ///
        /// IMPLEMENTATION:
        /// See utils.photoperiodFunctionInduction() and utils.temperatureFunctionInduction()
        /// </summary>
        /// <param name="input">Daily weather inputs with date and latitude for astronomy calculations</param>
        /// <param name="parameters">Dormancy induction parameters (thresholds, sensitivities)</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void induction(input input, parameters parameters, output output, output outputT1)
        {

            //check if dormancy induction started
            if (!outputT1.isDormancyInduced)
            {
                //estimate photoperiod 
                input.radData = utils.astronomy(input, false);

                #region photothermal units

                #region photothermal rate
                //call photoperiod function
                outputT1.dormancyInduction.photoperiodDormancyInductionRate=
                    utils.photoperiodFunctionInduction(input, parameters,outputT1);
                //call temperature function
                outputT1.dormancyInduction.temperatureDormancyInductionRate =
                    utils.temperatureFunctionInduction(input, parameters, outputT1);
                
                //compute dormancy induction rate
                outputT1.dormancyInduction.photoThermalDormancyInductionRate =
                    outputT1.dormancyInduction.photoperiodDormancyInductionRate *
                    outputT1.dormancyInduction.temperatureDormancyInductionRate;
                #endregion

                #region photothermal state and completion percentage
                //integrate the rate variable to compute the state variable
                outputT1.dormancyInduction.photoThermalDormancyInductionState = 
                    output.dormancyInduction.photoThermalDormancyInductionState +
                    outputT1.dormancyInduction.photoThermalDormancyInductionRate;

                //derive the percentage of phase completion
                outputT1.dormancyInductionPercentage = outputT1.dormancyInduction.photoThermalDormancyInductionState /
                    parameters.parDormancyInduction.photoThermalThreshold * 100;
               
                //check if dormancy induction is completed
                if (outputT1.dormancyInductionPercentage >= 100)
                {
                    //reset to 100% in case it exceeds (last day integration could be higher than threshold
                    outputT1.dormancyInductionPercentage = 100;
                    //boolean to state that dormancy is induced
                    outputT1.isDormancyInduced = true;
                    //reset to 0 the ecodormancy state
                    outputT1.ecodormancy.ecodormancyState = 0;

                   
                }

                #endregion

                #endregion

                #region update phenological code
                if (outputT1.dormancyInduction.photoThermalDormancyInductionState > 0)
                {
                    outputT1.phenoCode = 1;
                }
                #endregion
            }

        }

        #endregion

        #region endodormancy

        /// <summary>
        /// Computes hourly chilling accumulation and endodormancy state progression.
        ///
        /// BIOLOGICAL PROCESS:
        /// Endodormancy (true dormancy) is the deepest dormancy state where internal
        /// physiological blocks prevent growth even under favorable conditions.
        /// Release requires cumulative chilling exposure to break dormancy.
        ///
        /// CHILLING MODEL:
        /// Utah model variant with temperature-dependent chilling efficiency.
        /// Hourly integration over 24 hours per day for precise accumulation.
        ///
        /// TEMPERATURE RESPONSE (chilling efficiency):
        /// Efficient chilling occurs within optimal temperature window
        /// (typically 2-9°C depending on species).
        /// Warm temperatures (>16°C) may contribute negative chilling units.
        ///
        /// MATHEMATICAL MODEL:
        /// Daily chilling units = Σ(hourly chilling efficiency) for h=0 to 23
        /// State(t) = State(t-1) + Daily chilling units
        /// Percentage = (State / chillingThreshold) × 100
        ///
        /// CRITICAL COUPLING:
        /// Endodormancy percentage scales ecodormancy asymptote:
        /// ecodormancy_asymptote = endodormancyPercentage / 100
        ///
        /// This creates biological realism where partial chilling satisfaction
        /// limits subsequent warming response (ecodormancy progression).
        ///
        /// PHASE PROGRESSION:
        /// Percentage ≥ 100% → full chilling requirement satisfied
        /// Allows ecodormancy to progress to completion (asymptote = 1.0)
        ///
        /// PARAMETERS:
        /// - parEndodormancy.chillingThreshold: Total chilling units required
        /// - parEndodormancy.temperatureThreshold[0-3]: 4 cardinal temperatures
        ///   defining chilling efficiency curve (typically: -5, 2, 9, 16°C)
        ///
        /// IMPLEMENTATION:
        /// See utils.endodormancyRate() for hourly chilling efficiency calculation
        /// See utils.hourlyTemperature() for temperature disaggregation
        /// </summary>
        /// <param name="input">Daily weather inputs with hourly temperature arrays</param>
        /// <param name="parameters">Endodormancy parameters (chilling threshold, temperature thresholds)</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void endodormancy(input input, parameters parameters,
            output output,  output outputT1)
        {

            //check if dormancy is induced and ecodormancy is not completed
            if (outputT1.isDormancyInduced && !outputT1.isEcodormancyCompleted)
            {
                //initialize hourly temperature lists (call to the external function in utils static class)
                List<float> hourlyTemperatures = utils.hourlyTemperature(input);

                //internal variable to store chilling units
                float chillingUnits = utils.endodormancyRate(input, parameters, hourlyTemperatures, out List<float> chillingUnitsList);

                //compute daily chilling rate in a 0-1 scale
                outputT1.endodormancy.endodormancyRate = chillingUnits;

                //compute endodormancy progress
                outputT1.endodormancy.endodormancyState = output.endodormancy.endodormancyState+
                    outputT1.endodormancy.endodormancyRate;

                //compute endodormancy percentage
                outputT1.endodormancyPercentage = outputT1.endodormancy.endodormancyState /
                    parameters.parEndodormancy.chillingThreshold * 100;

                //if endodormancy is completed, set the variable to 100
                if (outputT1.endodormancyPercentage >= 100)
                {
                    outputT1.endodormancyPercentage = 100;                  
                }

            }

        }
        #endregion

        #region ecodormancy

        /// <summary>
        /// Computes daily ecodormancy rate and state using photothermal forcing with asymptote scaling.
        ///
        /// BIOLOGICAL PROCESS:
        /// Ecodormancy (eco-dormancy, external dormancy) is the phase where internal
        /// dormancy is released but growth remains suppressed by unfavorable environmental
        /// conditions (cold temperatures, short photoperiods). Spring warming and lengthening
        /// days drive progression toward bud burst and leaf-out.
        ///
        /// MATHEMATICAL MODEL:
        /// Rate = photoperiodFunction(dayLength) × temperatureFunction(Tmin, Tmax) × asymptote
        /// asymptote = endodormancyPercentage / 100
        /// State(t) = State(t-1) + Rate(t)
        /// Percentage = (State / photoThermalThreshold) × 100
        ///
        /// CRITICAL ASYMPTOTE SCALING:
        /// Ecodormancy rate modulated by endodormancy completion:
        /// - 0% chilling → asymptote = 0.0 → no ecodormancy progression
        /// - 50% chilling → asymptote = 0.5 → half maximum rate
        /// - 100% chilling → asymptote = 1.0 → full rate potential
        ///
        /// This creates biological realism where insufficient chilling delays spring phenology.
        ///
        /// PHOTOPERIOD FUNCTION (sigmoid):
        /// f(L) = 1 / (1 + exp(-sensitivity × (L - threshold)))
        /// High rate when day length > threshold, approaches 0 as days shorten
        ///
        /// TEMPERATURE FUNCTION (Yan & Hunt 1999):
        /// Non-linear thermal forcing with three cardinal temperatures (Tmin, Topt, Tmax).
        /// See utils.forcingUnitFunction() for implementation.
        ///
        /// PHASE TRANSITION:
        /// When percentage ≥ 100%:
        /// - isEcodormancyCompleted = true
        /// - phenoCode = 2
        /// - isGrowthCompleted = false (resets for new growing season)
        /// - isDeclineCompleted = false (resets for new growing season)
        ///
        /// PARAMETERS:
        /// - parEcodormancy.photoThermalThreshold: Cumulative units required
        /// - parEcodormancy.photoperiodThreshold: Day length threshold (hours)
        /// - parEcodormancy.photoperiodSensitivity: Sigmoid steepness
        /// - parEcodormancy.minimumTemperature: Base temperature (°C)
        /// - parEcodormancy.optimumTemperature: Optimal temperature (°C)
        /// - parEcodormancy.maximumTemperature: Maximum temperature (°C)
        ///
        /// IMPLEMENTATION:
        /// See utils.ecodormancyRate() for photothermal forcing calculation
        /// </summary>
        /// <param name="input">Daily weather inputs with date, latitude, and temperature</param>
        /// <param name="parameters">Ecodormancy parameters (thresholds, cardinal temperatures)</param>
        /// <param name="output">Previous timestep output (T-1) for state accumulation</param>
        /// <param name="outputT1">Current timestep output (T) to receive updated values</param>
        public void ecodormancy(input input, parameters parameters,
           output output, output outputT1)
        {
            //estimate photoperiod 
            input.radData = utils.astronomy(input,false);

            //check if dormancy is induced and ecodormancy is not completed
            if (outputT1.isDormancyInduced && !outputT1.isEcodormancyCompleted)
            {
                //the asymptote of photothermal units for ecodormancy depends on endodormancy percentage
                float asymptote = outputT1.endodormancyPercentage / 100;

                //compute ecodormancy rate (call to the external function in utils static class)
                outputT1.ecodormancy.ecodormancyRate = utils.ecodormancyRate(input, asymptote,parameters);

                //compute ecodormancy progress
                outputT1.ecodormancy.ecodormancyState = output.ecodormancy.ecodormancyState + outputT1.ecodormancy.ecodormancyRate;

                //ecodormancy completion percentage
                outputT1.ecodormancyPercentage = outputT1.ecodormancy.ecodormancyState /
                    parameters.parEcodormancy.photoThermalThreshold * 100;

                //if ecodormancy is completed, set the variable to 100 and set the boolean variable
                if (outputT1.ecodormancyPercentage >= 100)
                {
                    outputT1.ecodormancyPercentage = 100;
                    outputT1.isEcodormancyCompleted = true;
                }

                #region update phenological code
                if (outputT1.ecodormancy.ecodormancyState > 0)
                {
                    outputT1.phenoCode = 2;
                    outputT1.isGrowthCompleted = false;
                    outputT1.isDeclineCompleted = false;
                }
                #endregion
            }
            else
            {
                outputT1.ecodormancyPercentage = output.ecodormancyPercentage;               
            }
        }
        #endregion
    }
}
