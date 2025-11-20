using source.data;
using source.functions;

// ============================================================================
// Utility Functions Module - SWELL Model Core Computational Library
// ============================================================================
// This static class provides all core computational functions used across the
// SWELL model for phenology, carbon exchanges, and data processing.
//
// FUNCTIONAL ORGANIZATION:
//
// 1. **WEATHER DATA PROCESSING**:
//    - hourlyTemperature(): Sinusoidal temperature disaggregation (Campbell 1985)
//    - astronomy(): Solar geometry, day length, extraterrestrial radiation
//    - dayLength(): Standalone day length calculation
//    - PartitionRadiation(): Direct/diffuse radiation partitioning (Erbs et al. 1982)
//
// 2. **PHENOLOGY FORCING FUNCTIONS**:
//    - forcingUnitFunction(): Yan & Hunt (1999) thermal forcing with 3 cardinal temps
//    - photoperiodFunctionInduction(): Sigmoid photoperiod response for dormancy
//    - temperatureFunctionInduction(): Sigmoid temperature response for dormancy
//    - endodormancyRate(): Hourly chilling accumulation (Utah model variant)
//    - ecodormancyRate(): Photothermal forcing with asymptote scaling
//
// 3. **VEGETATION INDEX & CANOPY STRUCTURE**:
//    - estimateVegetationCover(): VI-based fractional cover estimation
//    - estimateLAI(): Two-layer LAI from EVI with temporal continuity
//
// 4. **CARBON EXCHANGE ENVIRONMENTAL MODIFIERS**:
//    - waterStressFunction(): Rolling memory precipitation-ET0 balance
//    - VPDfunction(): Sigmoid vapor pressure deficit response
//    - phenologyFunction(): Logistic aging function during growth
//    - PARGppfunction(): Michaelis-Menten PAR saturation response
//    - temperatureFunction(): Symmetric polynomial temperature response for GPP
//
// 5. **RESPIRATION CALCULATIONS**:
//    - ComputeTscaleReco(): Lloyd-Taylor temperature response with safeguards
//    - gppRecoTreeFunction(): Overstory GPP-dependent respiration with aging
//    - gppRecoUnderFunction(): Understory GPP-dependent respiration
//    - RecoRespirationFunction(): Phenological aging modifier for respiration
//
// 6. **VVVV INTERFACE**:
//    - vvvvInterface class: Main execution wrapper for vvvv visual programming
//    - estimateHourly(): Hourly disaggregation from daily weather inputs
//    - vvvvExecution(): Complete model timestep with phenology and carbon fluxes
//
// CRITICAL CONSTANTS:
// - Solar constant: 4.921 MJ m⁻² h⁻¹ (extraterrestrial radiation)
// - PAR fraction: 50.5% of shortwave radiation
// - Reference temperature (Lloyd-Taylor): 288.15 K (15°C)
// - Temperature offset (Lloyd-Taylor): 227.13 K
// - Latitude validity: -65° to 65° for day length calculations
//
// MATHEMATICAL FOUNDATIONS:
// - Yan & Hunt (1999): Non-linear thermal forcing with asymmetric curves
// - Campbell (1985): Sinusoidal temperature disaggregation
// - Erbs et al. (1982): Direct/diffuse radiation partitioning
// - Lloyd-Taylor: Exponential temperature response for respiration
// - Michaelis-Menten: Hyperbolic saturation kinetics for PAR response
//
// STATE MANAGEMENT:
// Functions are stateless except for vvvvInterface which maintains
// outputT0 and outputT1 for temporal continuity between timesteps.
// ============================================================================

namespace source.functions
{
    /// <summary>
    /// Static utility class providing all core computational functions for SWELL model.
    ///
    /// DESIGN PHILOSOPHY:
    /// Pure functions with no side effects (except vvvvInterface state management).
    /// All functions accept explicit parameters and return computed values.
    ///
    /// USAGE PATTERN:
    /// Called by phenology functions (dormancySeason, growingSeason), vegetation
    /// index dynamics (VIdynamics), carbon exchanges (exchanges), and vvvv interface.
    ///
    /// NUMERICAL STABILITY:
    /// Temperature response functions include safeguards against:
    /// - Division by zero
    /// - Exponential overflow
    /// - Invalid logarithms
    /// - Out-of-range inputs
    /// </summary>
    public static class utils
    {
        #region BreathState Aggregation

        /// <summary>
        /// Aggregates a sequence of BreathState instances into a property-oriented dictionary structure.
        /// Transposes from hour-oriented (list of BreathStates) to property-oriented (dictionary of lists).
        ///
        /// TRANSFORMATION:
        /// Input: Sequence of N BreathState instances, each with enum-keyed properties dictionary
        /// Output: Dictionary with CarbonFluxProperty enum keys and lists of N values
        ///
        /// ASSUMPTION:
        /// All BreathState instances contain the same set of property keys.
        /// Missing properties will use 0.0f as default value.
        ///
        /// USAGE:
        /// Inverse operation of exchanges.GetHourlyStates() - converts hour-oriented data
        /// back to property-oriented time series for analysis or export.
        ///
        /// EXAMPLE:
        /// Input: [BreathState{nee:1, gpp:2}, BreathState{nee:4, gpp:5}, BreathState{nee:7, gpp:8}]
        /// Output: {CarbonFluxProperty.nee: [1, 4, 7], CarbonFluxProperty.gpp: [2, 5, 8]}
        /// </summary>
        /// <param name="breathStates">Sequence of BreathState instances to aggregate</param>
        /// <returns>Dictionary mapping CarbonFluxProperty enum keys to lists of values across all instances</returns>
        public static Dictionary<CarbonFluxProperty, List<float>> AggregateBreathStates(IEnumerable<BreathState> breathStates)
        {
            var result = new Dictionary<CarbonFluxProperty, List<float>>();

            // Convert to list for multiple enumeration
            var statesList = breathStates.ToList();

            // Return empty dictionary if no states provided
            if (statesList.Count == 0)
            {
                return result;
            }

            // First pass: Collect all unique property keys from all BreathState instances
            var allKeys = new HashSet<CarbonFluxProperty>();
            foreach (var state in statesList)
            {
                if (state?.Properties != null)
                {
                    foreach (var key in state.Properties.Keys)
                    {
                        allKeys.Add(key);
                    }
                }
            }

            // Second pass: For each property key, collect values from all BreathState instances
            foreach (var key in allKeys)
            {
                var valuesList = new List<float>();

                foreach (var state in statesList)
                {
                    // Use GetProperty which returns 0.0f if property doesn't exist
                    float value = state?.GetProperty(key) ?? 0.0f;
                    valuesList.Add(value);
                }

                result[key] = valuesList;
            }

            return result;
        }

        #endregion

        #region additional weather inputs
        //hourly temperatures for chilling (24 values in a list) (Campbell, 1985)
        public static List<float> hourlyTemperature(input input)
        {
            //empty list
            List<float> hourlyTemperatures = new List<float>();

            //average temperature
            double Tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) / 2;
            //daily range
            double DT = input.airTemperatureMaximum - input.airTemperatureMinimum;
            for (int h = 0; h < 24; h++)
            {
                //14 is set as the hour with maximum temperature
                hourlyTemperatures.Add((float)(Tavg + DT / 2 * Math.Cos(0.2618F * (h - 14))));
            }
            //return the list of hourly temperatures
            return hourlyTemperatures;
        }

        #region astronomy
        public static radData astronomy(input input, bool hourlyTimeStep)
        {
            float solarConstant = 4.921F;
            float DtoR = (float)Math.PI / 180;
            float dd;
            float ss;
            float cc;
            float ws;
            float dayHours = 0;

            dd = 1 + 0.0334F * (float)Math.Cos(0.01721 * input.date.DayOfYear - 0.0552);
            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            float SolarDeclinationMinimum = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + 356));//winter solstice
            ss = (float)Math.Sin(SolarDeclination) * (float)Math.Sin(input.latitude * DtoR);
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));
            float wsMinimum = (float)Math.Acos(-Math.Tan(SolarDeclinationMinimum) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            if (input.latitude < 65 && input.latitude > -65)
            {
                input.radData.dayLength = 0.13333F / DtoR * ws;
                input.radData.etr = solarConstant * dd * 24 / (float)Math.PI
                    * (ws * ss + cc * (float)Math.Sin(ws));
            }
            else
            {
                input.radData.dayLength = dayHours;
            }
            input.radData.hourSunrise = 12 - input.radData.dayLength / 2;
            input.radData.hourSunset = 12 + input.radData.dayLength / 2;

            if (hourlyTimeStep)
            {
                for (int h = 0; h < 24; h++)
                {
                    //hour angle (degrees)
                    float HourAngleHourly = 15 * (h - 12);
                    //hourly sun ElevationMatrix (radians)
                    float cosQHadj = ss + cc * (float)Math.Cos(DtoR * HourAngleHourly);

                    float SolarElevationHourly = 0;
                    if (cosQHadj > 0) { SolarElevationHourly = (float)Math.Asin(cosQHadj); }
                    else { SolarElevationHourly = 0; }
                    if (input.radData.etrHourly[h] > 0) { dayHours += dayHours++; }
                    input.radData.etrHourly[h] = solarConstant * dd * (float)Math.Sin(SolarElevationHourly);
                }
            }
            return input.radData;
        }

        public static (float LAIoverstory, float LAIunderstory, float EVIoverstory, float EVIunderstory) estimateLAI(output outputT,
            output outputT1)
        {
            //estimate EVI of the overstory
            float EVIoverstory;
            float EVIunderstory;
            float LAIoverstory = 0;
            float LAIunderstory = 0;

            

            if (outputT1.phenoCode < 3)
            {
                EVIoverstory = 0;
                LAIoverstory = 0;
                EVIunderstory = outputT1.vi / 100;
            }
            else
            {
                float deltaVi = (outputT1.vi - outputT.vi) / 100;
                EVIoverstory = outputT1.vi / 100 - outputT1.viAtGrowth;
                LAIoverstory = 9.41F * outputT1.vi / 100 - 1.67F;
                EVIunderstory = Math.Min(outputT.exchanges.EVIunderstory + deltaVi * (1-outputT.exchanges.vegetationCover),
                    outputT1.vi/100);
            }

            //estimate LAI overstory (https://doi.org/10.1016/j.agrformet.2012.09.003)


            //estimate LAI understory (ENVImethod
            LAIunderstory = 3.618F * EVIunderstory - 0.118F;

            if (LAIoverstory < 0) LAIoverstory = 0;
            if (LAIunderstory < 0) LAIunderstory = 0;
            if (EVIoverstory < 0) EVIoverstory = 0;
            if (EVIunderstory < 0) EVIunderstory = 0;

            return (LAIoverstory, LAIunderstory, EVIoverstory, EVIunderstory);
        }

        public static (float SW_DIR, float SW_DIF) PartitionRadiation(float SW_IN, float SW_TOA_H)
        {
            //Erbs et al. (1982)
            if (SW_IN <= 0f || SW_TOA_H <= 0f) return (0f, 0f);

            float kt = Math.Clamp(SW_IN / SW_TOA_H, 0f, 1.2f);

            float kd; // diffuse fraction
            if (kt <= 0.22f)
                kd = 1.0f - 0.09f * kt;
            else if (kt <= 0.80f)
                kd = 0.9511f - 0.1604f * kt + 4.388f * kt * kt - 16.638f * kt * kt * kt + 12.336f * kt * kt * kt * kt;
            else
                kd = 0.165f;

            float SW_DIF = Math.Clamp(kd * SW_IN, 0f, SW_IN);
            float SW_DIR = SW_IN - SW_DIF;
            return (SW_DIR, SW_DIF);
        }

        public static float dayLength(input input)
        {
            float DtoR = (float)Math.PI / 180;
            float cc;
            float ws;
            float dayHours = 0;

            float SolarDeclination = 0.4093F * (float)Math.Sin((6.284 / 365) * (284 + input.date.DayOfYear));
            cc = (float)Math.Cos(SolarDeclination) * (float)Math.Cos(input.latitude * DtoR);
            ws = (float)Math.Acos(-Math.Tan(SolarDeclination) * (float)Math.Tan(input.latitude * DtoR));

            //if -65 < Latitude and Latitude < 65 dayLength and ExtraterrestrialRadiation are
            //approximated using the algorithm in the hourly loop
            //if (rd.Latitude <65 || rd.Latitude>-65)
            if (input.latitude < 65 && input.latitude > -65)
            {
                dayHours = 0.13333F / DtoR * ws;
            }
            else
            {
                dayHours = 0;
            }

            return dayHours;
        }

        #endregion

        #endregion

        #region SWELL phenophase specific functions

        #region growth, greendown, decline thermal units
        //this method computes the forcing thermal unit (Yan & Hunt, 1999)
        public static float forcingUnitFunction(input input, float tmin, float topt, float tmax)
        {
            //local output variable
            float forcingRate = 0;

            //average air temperature
            float averageAirTemperature = (input.airTemperatureMaximum +
                input.airTemperatureMinimum) / 2;

            //if average temperature is below minimum or above maximum
            if (averageAirTemperature < tmin || averageAirTemperature > tmax)
            {
                forcingRate = 0;
            }
            else
            {
                //intermediate computations
                float firstTerm = (tmax - averageAirTemperature) / (tmax - topt);
                float secondTerm = (averageAirTemperature - tmin) / (topt - tmin);
                float Exponential = (topt - tmin) / (tmax - topt);

                //compute forcing rate
                forcingRate = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            }
            //assign to output variable
            return forcingRate;
        }
        #endregion

        #region dormancy induction 
        //photoperiod function
        public static float photoperiodFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //local variable to store the output
            float photoperiodFunction = 0;

            //day length is non limiting PT
            if (input.radData.dayLength < parameters.parDormancyInduction.notLimitingPhotoperiod)
            {
                photoperiodFunction = 1;
            }
            else if (input.radData.dayLength > parameters.parDormancyInduction.limitingPhotoperiod)
            {
                photoperiodFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingPhotoperiod + parameters.parDormancyInduction.notLimitingPhotoperiod) * 0.5F;
                float width = parameters.parDormancyInduction.limitingPhotoperiod - parameters.parDormancyInduction.notLimitingPhotoperiod;

                //compute function
                photoperiodFunction = 1 / (1 + (float)Math.Exp(10 / width *
                    ((input.radData.dayLength - midpoint))));

            }
            //return the photoperiod function
            return photoperiodFunction;
        }

        //temperature function
        public static float temperatureFunctionInduction(input input,
           parameters parameters, output outputT1)
        {
            //average temperature
            float tAverage = (float)(input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;

            //local variable to store the output
            float temperatureFunction = 0;

            if (tAverage <= parameters.parDormancyInduction.notLimitingTemperature)
            {
                temperatureFunction = 1;
            }
            else if (tAverage >= parameters.parDormancyInduction.limitingTemperature)
            {
                temperatureFunction = 0;
            }
            else
            {
                float midpoint = (parameters.parDormancyInduction.limitingTemperature + parameters.parDormancyInduction.notLimitingTemperature) * .5F;
                float width = (parameters.parDormancyInduction.limitingTemperature - parameters.parDormancyInduction.notLimitingTemperature);
                //compute function
                temperatureFunction = 1 / (1 + (float)Math.Exp(10 / width * (tAverage - midpoint)));

            }
            //return the output
            return temperatureFunction;
        }
        #endregion

        #region endodormancy
        public static float endodormancyRate(input input, parameters parameters, //internal list to store hourly temperatures
            List<float> hourlyTemperatures, out List<float> chillingUnitsList)
        {

            chillingUnitsList = new List<float>();
            //internal variable to store chilling units
            float chillingUnits = 0;

            #region chilling units accumulation
            foreach (var temperature in hourlyTemperatures)
            {
                //when hourly temperature is below the limiting lower temperature or above the limiting upper temperature
                if (temperature < parameters.parEndodormancy.limitingLowerTemperature ||
                    temperature > parameters.parEndodormancy.limitingUpperTemperature)
                {
                    //no chilling units are accumulated 
                    chillingUnits = 0; //not needed, just to be clear
                }
                //when hourly temperature is between the limiting lower temperature
                //and the non limiting lower temperature
                else if (temperature >= parameters.parEndodormancy.limitingLowerTemperature &&
                    temperature < parameters.parEndodormancy.notLimitingLowerTemperature)
                {
                    //compute lag and slope
                    double midpoint = (parameters.parEndodormancy.limitingLowerTemperature +
                        parameters.parEndodormancy.notLimitingLowerTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingLowerTemperature -
                        parameters.parEndodormancy.notLimitingLowerTemperature);

                    //update chilling units
                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / -width * ((temperature - midpoint))));
                }
                //when hourly temperature is between the non limiting lower temperature and the 
                //non limiting upper temperature
                else if (temperature >= parameters.parEndodormancy.notLimitingLowerTemperature &&
                    temperature <= parameters.parEndodormancy.notLimitingUpperTemperature)
                {
                    chillingUnits = 1;
                }
                //when hourly temperature is between the non limiting upper temperature and the
                //limiting upper temperature
                else
                {
                    double midpoint = (parameters.parEndodormancy.limitingUpperTemperature +
                       parameters.parEndodormancy.notLimitingUpperTemperature) / 2;
                    double width = Math.Abs(parameters.parEndodormancy.limitingUpperTemperature -
                        parameters.parEndodormancy.notLimitingUpperTemperature);

                    chillingUnits = 1 / (1 + (float)Math.Exp(10 / width * ((temperature - midpoint))));
                }

                chillingUnitsList.Add(chillingUnits);
            }
            #endregion

            //return the output
            return chillingUnitsList.Sum() / 24;
        }
        #endregion

        #region ecodormancy
        public static float ecodormancyRate(input input, float asymptote, parameters parameters)
        {
            //local variable to store the output
            float ecodormancyRate = 0;


            //the slope of the photothermal function depends on day length 
            float ratioPhotoperiod = input.radData.dayLength / parameters.parEcodormancy.notLimitingPhotoperiod;
            if (ratioPhotoperiod > 1)
            {
                ratioPhotoperiod = 1;
            }

            //modify asymptote depending on day length and endodormancy completion
            float asymptoteModifier = ratioPhotoperiod * asymptote;
            float newAsymptote = asymptote + (1 - asymptote) * asymptoteModifier;

            //lag depends on maximum temperature and day length
            float midpoint = parameters.parEcodormancy.notLimitingTemperature * 0.5F +
                (1 - ratioPhotoperiod) * parameters.parEcodormancy.notLimitingTemperature;
            float tavg = (input.airTemperatureMaximum + input.airTemperatureMinimum) * 0.5F;
            float width = parameters.parEcodormancy.notLimitingTemperature * ratioPhotoperiod;

            //compute ecodormancy rate
            ecodormancyRate = newAsymptote /
              (1 + (float)Math.Exp(-10 / width * ((tavg - midpoint)))); ;

            //return the output
            return ecodormancyRate;
        }
        #endregion

        #endregion

        #region exchanges

        public static float estimateVegetationCover(output outputT1, parameters parameters)
        {
            //estimate vegetation cover
            float vegetationCover = 0;
            if (outputT1.phenoCode == 3)
            {
                vegetationCover = (outputT1.vi / 100 - outputT1.viAtGrowth) /
                    ((parameters.parVegetationIndex.maximumVI - outputT1.viAtGrowth));
            }
            else if (outputT1.phenoCode == 4)
            {
                vegetationCover = 1;
            }
            else if (outputT1.phenoCode == 5)
            {
                vegetationCover = 1 - (((outputT1.viAtGreendown - outputT1.vi / 100) /
                   ((outputT1.viAtGreendown - outputT1.viAtGrowth))));
            }
            //check to avoid inconsistencies
            if (vegetationCover < 0)
            {
                vegetationCover = 0;
            }

            return vegetationCover;
        }

        //gpp
        public static float  waterStressFunction(output outputT1, input input, parameters parameters, int hour)
        {
            float waterAvailability = 0;
            float waterStress = 0;

            outputT1.exchanges.PrecipitationMemory.Add(input.precipitationH[hour]);
            outputT1.exchanges.ET0memory.Add(input.referenceET0H[hour]);


            if (outputT1.exchanges.PrecipitationMemory.Count <
                (int)parameters.parPhotosynthesis.waterStressDays * 24)
            {
                waterAvailability = 1;
                waterStress = 1;
            }
            else
            {
                //compute water stress: https://doi.org/10.1016/j.geoderma.2021.115003
                float ndviNorm = (outputT1.vi / 100 - parameters.parVegetationIndex.minimumVI) /
                 (parameters.parVegetationIndex.maximumVI - parameters.parVegetationIndex.minimumVI);

                float et0Sum = outputT1.exchanges.ET0memory.Sum();
                float prec = outputT1.exchanges.PrecipitationMemory.Sum();

                if (prec > et0Sum) et0Sum = prec;

                waterAvailability = ndviNorm * (.5F + .5F *
                    (prec / et0Sum)) +
                    (1 - ndviNorm) * (prec / et0Sum);

                if (waterAvailability < 0)
                {
                    waterAvailability = 0;
                }

                //compute water stress GPP
                if (waterAvailability >= parameters.parPhotosynthesis.waterStressThreshold)
                {
                    waterStress = 1;
                }
                else
                {
                    waterStress = parameters.parPhotosynthesis.waterStressSensitivity *
                        (waterAvailability - parameters.parPhotosynthesis.waterStressThreshold) + 1;
                }
               


                //remove when the memory effect ends
                if (outputT1.exchanges.ET0memory.Count == (int)parameters.parPhotosynthesis.waterStressDays * 24)
                {
                    outputT1.exchanges.ET0memory.RemoveAt(0);
                    outputT1.exchanges.PrecipitationMemory.RemoveAt(0);
                }

            }

            //set maximum water stress to 0
            if (waterStress < 0)
            {
                waterStress = 0;
            }


            return (waterStress);
        }

        public static float temperatureFunction(float temperature, float tmin, float topt, float tmax)
        {
            float tScale = 0;

            if (temperature < tmin || temperature > tmax)
            {
                tScale = 0;
            }
            else
            {
                float numerator = (temperature - tmin) *
                    (temperature - tmax);

                float denominator = numerator -
                    (float)Math.Pow((temperature - topt), 2);

                tScale = numerator / denominator;
            }



            ////if average temperature is below minimum or above maximum
            //if (temperature < tmin || temperature > tmax)
            //{
            //    tScale = 0;
            //}
            //else
            //{
            //    //intermediate computations
            //    float firstTerm = (tmax - temperature) / (tmax - topt);
            //    float secondTerm = (temperature - tmin) / (topt - tmin);
            //    float Exponential = (topt - tmin) / (tmax - topt);

            //    //compute forcing rate
            //    tScale = (float)(firstTerm * Math.Pow(secondTerm, Exponential));
            //}

            return tScale;
        }

        public static float PARGppfunction(output outputsT1, float par, float halfSaturationValue)
        {
            float parScaleTree = 1 / (1 + (par / halfSaturationValue));

            return parScaleTree;
        }

        public static float phenologyFunction(output outputsT1, parameters parameters)
        {
            float phenologyScale = 0;

            if (outputsT1.phenoCode == 3)
            {
                float leafActivity = 1 / (1 + (float)Math.Exp(3 *
                    (parameters.parPhotosynthesis.growthPhenologyScalingFactor - outputsT1.growthPercentage / 100)));
                phenologyScale = leafActivity;
            }
            else if (outputsT1.phenoCode == 5)
            {
                //float leafActivity = 1 / (1 + (float)Math.Exp(-10 *
                //    (parameters.parExchanges.declinePhenologyScalingFactor - outputsT1.declinePercentage / 100)));
                //phenologyScale = leafActivity;
                phenologyScale = 1;
            }
            else if (outputsT1.phenoCode == 4) //greendown
            {
                phenologyScale = 1;
            }
            else
            {
                phenologyScale = 0;
            }

            return phenologyScale;
        }

        public static float VPDfunction(float vpd, parameters parameters)
        {
            float vpdMin = parameters.parPhotosynthesis.vpdMin;
            float vpdMax = parameters.parPhotosynthesis.vpdMax;
            float kVPD = parameters.parPhotosynthesis.vpdSensitivity;


            if (vpd < vpdMin)
            {
                return 1f;
            }
            else
            {
                float midpoint = (vpdMax + vpdMin) / 2f;
                float exponent = kVPD * (vpd - midpoint);
                float result = 1f / (1f + (float)Math.Exp(exponent));
                return result;
            }
        }

        public static float ComputeTscaleReco(float temperature, float activatonEnergyParameter)
        {
            const float Tref = 288.15f;  // reference temperature [K]  (15 °C)
            const float T0 = 227.13f;  // temperature offset [K]  (-46.02 °C)

            // --- convert to Kelvin
            float TK = 273.15f + temperature;

            // --- numerical and physical safeguards ---
            // below -45 °C the Lloyd-Taylor formula becomes unstable
            if (TK <= (T0 + 0.5f))
                return 0f;  // respiration effectively zero (or clamp to minimal value)

            // avoid division by zero or extremely small denominators
            float denom1 = Tref - T0;
            float denom2 = TK - T0;

            if (denom1 < 1e-6f || denom2 < 1e-6f)
                return 0f;

            // --- compute exponential term safely ---
            float exponent = activatonEnergyParameter *
                             ((1f / denom1) - (1f / denom2));

            // clamp exponent to prevent overflow in Math.Exp
            if (exponent > 50f) exponent = 50f;   // e^50 ≈ 3.0e21
            if (exponent < -50f) exponent = -50f; // e^-50 ≈ 1.9e-22

            float Tscale = (float)Math.Exp(exponent);

            // optional: limit scaling to reasonable physiological range
            if (Tscale > 10f) Tscale = 10f;
            if (Tscale < 0f) Tscale = 0f;

            return Tscale;
        }

        public static float gppRecoUnderFunction(input input, float PARintercept, float gpp, output outputs1, parameters parameters, int hour)
        {
            float gppRecoFunction = 0;

            float respirationReference = parameters.parRespiration.referenceRespirationUnder;
            float PARrecoFunction = 1;// PARRECOfunction(PARintercept, parameters.parExchanges.respirationHalfSaturationUnder, hour, input);
            //compute function
            gppRecoFunction = respirationReference +
                parameters.parRespiration.respirationResponseUnder * (PARrecoFunction) * gpp;

            return gppRecoFunction;
        }

        public static float gppRecoTreeFunction(input input, float PARintercept, float gpp, output outputT, output outputs1,
            parameters parameters, int hour)
        {
            float gppRecoFunction = 0;
            float recoModifier = RecoRespirationFunction(input, outputs1, parameters);

            float respirationReference = parameters.parRespiration.referenceRespirationOver;
            float PARrecoFunction = 1;// PARRECOfunction(PARintercept, parameters.parExchanges.respirationHalfSaturationOver, hour, input);
            //compute function
            if (outputs1.phenoCode < 3)
            {
                gppRecoFunction = 0;
            }
            else
            {
                gppRecoFunction = respirationReference +
                        (parameters.parRespiration.respirationResponseOver * (PARrecoFunction)) * gpp;
            }

            return gppRecoFunction;
        }

        public static float PARRECOfunction(float par, float halfSaturation, int hour, input input)
        {

            float parScaleRECO = 1 / (1 + (par / halfSaturation));

            return parScaleRECO;


        }


        public static float RecoRespirationFunction(input input, output outputsT1, parameters parameters)
        {
            float recoReferenceFunction = 0;// parameters.parExchanges.respirationMinimumFactor;

            float growingSeasonPercentage = 0;
            float growingSeasonTotal = parameters.parGrowth.thermalThreshold +
                parameters.parGreendown.thermalThreshold + parameters.parSenescence.photoThermalThreshold;

            if (outputsT1.phenoCode == 3)
            {
                growingSeasonPercentage = outputsT1.growth.growthState / growingSeasonTotal;
            }
            else if (outputsT1.phenoCode == 4)
            {
                growingSeasonPercentage = (parameters.parGrowth.thermalThreshold +
                    outputsT1.greenDown.greenDownState) / growingSeasonTotal;
            }
            else if (outputsT1.phenoCode == 5)
            {
                growingSeasonPercentage = (parameters.parGrowth.thermalThreshold +
                     parameters.parGreendown.thermalThreshold +
                     outputsT1.decline.declineState) / growingSeasonTotal;
            }


            if (outputsT1.phenoCode >= 3)
            {

                recoReferenceFunction = 1 / (1 + (float)Math.Exp(10 * (growingSeasonPercentage -
                     parameters.parRespiration.respirationAgingFactor)));
            }


            if (outputsT1.phenoCode < 3)
            {
                recoReferenceFunction = 0;// parameters.parExchanges.respirationMinimumFactor;
            }

            return recoReferenceFunction;
        }



        #endregion
    }

    #region vvvv execution interface
    public class vvvvInterface
    {
        //initialize the SWELL phenology classes with functions
        dormancySeason dormancy = new dormancySeason();
        growingSeason growing = new growingSeason();
        VIdynamics VIdynamics = new VIdynamics();
        source.functions.exchanges exchanges = new source.functions.exchanges();
        //initialize the outputT1
        output outputT0 = new output();
        output outputT1 = new output();

        //this method contains the logic for the execution in vvvv
        public output vvvvExecution(input input, parameters parameters)
        {

            input = estimateHourly(input);

            //pass values from the previous day
            outputT0 = outputT1;
            outputT1 = new output();

            //call the functions
            //dormancy season
            dormancy.induction(input, parameters, outputT0, outputT1);
            dormancy.endodormancy(input, parameters, outputT0, outputT1);
            dormancy.ecodormancy(input, parameters, outputT0, outputT1);
            //growing season
            growing.growthRate(input, parameters, outputT0, outputT1);
            growing.greendownRate(input, parameters, outputT0, outputT1);
            growing.declineRate(input, parameters, outputT0, outputT1);
            //NDVI dynamics
            VIdynamics.ndviNormalized(input, parameters, outputT0, outputT1);
            exchanges.VPRM(input, parameters, outputT0, outputT1);

            outputT1.weather.date = input.date;


            return outputT1;
        }

        #region private methods
        public input estimateHourly(input inputDaily)
        {
            input hourlyData = inputDaily;

            float avgT = (inputDaily.airTemperatureMaximum + inputDaily.airTemperatureMinimum) / 2;
            float dailyRange = inputDaily.airTemperatureMaximum - inputDaily.airTemperatureMinimum;
            float dewPoint = Math.Clamp(inputDaily.dewPointTemperature, inputDaily.airTemperatureMinimum - 5,
                inputDaily.airTemperatureMaximum);
            float rain = inputDaily.precipitation;

            for (int h = 0; h < 24; h++)
            {
                // Temperature Estimate
                float hourlyT = (float)(avgT + dailyRange / 2 * Math.Cos(0.2618f * (h - 15)));
                hourlyData.airTemperatureH[h] = hourlyT;

                // Relative Humidity Estimate            
                float es = 0.6108f * (float)Math.Exp((17.27f * hourlyT) / (237.3F + hourlyT));
                float ea = 0.6108f * (float)Math.Exp((17.27F * dewPoint) / (237.3F + dewPoint));
                float rh_hour = ea / es * 100;
                hourlyData.relativeHumidityH[h] = Math.Clamp(rh_hour, 0f, 100f);

                // Precipitation
                hourlyData.precipitationH[h] = rain / 24;

                // evenly distribute or use sinusoidal pattern
                inputDaily.radData = dayLength(inputDaily, inputDaily.airTemperatureMaximum, inputDaily.airTemperatureMinimum);

                //TODO: CONVERT FROM MJ
                hourlyData.solarRadiationH[h] = inputDaily.radData.gsrHourly[h] * 1269.44F;

                // Wind Speed ?

                // VPD
                hourlyData.vaporPressureDeficitH[h] = vpd(hourlyData, h);

                // ET₀
                hourlyData.referenceET0H[h] = referenceEvapotranspiration(inputDaily, hourlyData, h);
            }

            return hourlyData;
        }
        #endregion


        public radData dayLength(input input, float Tmax, float Tmin)
        {
            radData _radData = input.radData;
            _radData.gsr = input.PAR;
            // Constants
            float solarConstant = 4.921f; // MJ m⁻² h⁻¹ (derived from 1367 W/m²)
            float DtoR = (float)Math.PI / 180f;

            int doy = input.date.DayOfYear;
            float latitudeRad = input.latitude * DtoR;

            // Solar geometry
            float inverseEarthSun = 1f + 0.0334f * (float)Math.Cos(0.01721f * doy - 0.0552f); // Earth-Sun distance correction
            float solarDeclination = 0.4093f * (float)Math.Sin((6.284f / 365f) * (284 + doy)); // radians
            float sinDec = (float)Math.Sin(solarDeclination);
            float cosDec = (float)Math.Cos(solarDeclination);
            float sinLat = (float)Math.Sin(latitudeRad);
            float cosLat = (float)Math.Cos(latitudeRad);
            float ss = sinDec * sinLat;
            float cc = cosDec * cosLat;
            float ws = (float)Math.Acos(-Math.Tan(solarDeclination) * Math.Tan(latitudeRad)); // sunset hour angle (radians)

            // Initialize arrays
            float[] HourAngleHourly = new float[24];
            float[] SolarElevationHourly = new float[24];
            float[] ExtraterrestrialRadiationHourly = new float[24];
            float[] Distribution = new float[24];
            _radData.etrHourly = new float[24];
            _radData.gsrHourly = new float[24];

            float dayHours = 0f;
            _radData.etr = 0f;

            // Loop through 24 hours to calculate hourly ETR
            for (int h = 0; h < 24; h++)
            {
                HourAngleHourly[h] = 15f * (h - 12f); // degrees
                float cosQHadj = ss + cc * (float)Math.Cos(DtoR * HourAngleHourly[h]);
                cosQHadj = Math.Clamp(cosQHadj, -1f, 1f);

                SolarElevationHourly[h] = (cosQHadj > 0f) ? (float)Math.Asin(cosQHadj) : 0f;

                float hourlyETR = solarConstant * inverseEarthSun * (float)Math.Sin(SolarElevationHourly[h]);
                hourlyETR = Math.Max(0f, hourlyETR); // no negative values

                _radData.etrHourly[h] = hourlyETR;
                _radData.etr += hourlyETR;

                if (hourlyETR > 0f) dayHours++;
            }

            // Compute analytical ETR and day length if within valid latitudes
            if (input.latitude < 65 && input.latitude > -65)
            {
                float dayLength = 0.13333f / DtoR * ws; // day length (hours)
                float Ra_analytical = (24f / (float)Math.PI) * solarConstant * inverseEarthSun *
                    (ws * ss + cc * (float)Math.Sin(ws));

                _radData.dayLength = dayLength;
                _radData.etr = Ra_analytical; // use analytical instead of loop? choose one
            }
            else
            {
                _radData.dayLength = dayHours;
            }

            // Redistribute GSR over 24 hours using ETR fractions
            for (int h = 0; h < 24; h++)
            {
                Distribution[h] = (_radData.etr > 0f) ? _radData.etrHourly[h] / _radData.etr : 0f;
                _radData.gsrHourly[h] = Distribution[h] * _radData.gsr;
            }

            // Estimate sunrise and sunset
            _radData.hourSunrise = 12f - _radData.dayLength / 2f;
            _radData.hourSunset = 12f + _radData.dayLength / 2f;

            return _radData;
        }

        public float vpd(input Input, int hour)
        {
            //VPD calculation method by Monteith and Unsworth (1990)

            float SVP = 0.6108f * (float)Math.Exp((17.27f * Input.airTemperatureH[hour]) / (Input.airTemperatureH[hour] + 237.3f)); // in kPa
            float AVP = SVP * Input.relativeHumidityH[hour] / 100f;
            float VPD = SVP - AVP;
            return VPD;
        }

        public float referenceEvapotranspiration(input dailyInput, input Input, int hour)
        {
            // Convert Rs from W/m² to MJ/m²/h
            double Rs_MJ = dailyInput.PAR;

            // Given coefficients
            double c0 = 0.1396;
            double c1 = -3.019e-3;
            double c2 = -1.2109e-3;
            double c3 = 1.626e-5;
            double c4 = 8.224e-5;
            double c5 = 0.1842;
            double c6 = -1.095e-3;
            double c7 = 3.655e-3;
            double c8 = -4.442e-3;

            // Compute ET0 using the equation
            double ET0 = c0
                         + c1 * Input.relativeHumidityH[hour]
                         + c2 * Input.airTemperatureH[hour]
                         + c3 * Math.Pow(Input.relativeHumidityH[hour], 2)
                         + c4 * Math.Pow(Input.airTemperatureH[hour], 2)
                         + c5 * Rs_MJ
                         + 0.5 * Rs_MJ * (c6 * Input.relativeHumidityH[hour]
                         + c7 * Input.airTemperatureH[hour])
                         + c8 * Math.Pow(Rs_MJ, 2);

            if (ET0 < 0) { ET0 = 0; }
            ;
            return (float)ET0;

        }

    }
    #endregion

}