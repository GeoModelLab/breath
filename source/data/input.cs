using System;

// ============================================================================
// Input Data Layer - SWELL Model Environmental Drivers
// ============================================================================
// This namespace contains all input data structures required for the SWELL
// (Seasonal Woody-Ecosystem Leaf-out and Leaf-loss) model execution.
//
// Data flows from daily weather observations through hourly disaggregation
// (performed by vvvvInterface.estimateHourly()) into phenology and carbon
// flux computation modules.
// ============================================================================

namespace source.data
{
    /// <summary>
    /// Primary input data container for SWELL model execution.
    /// Contains daily weather observations and their hourly disaggregations.
    ///
    /// USAGE PATTERN:
    /// 1. Populate daily weather fields from meteorological data sources
    /// 2. Call vvvvInterface.estimateHourly() to generate hourly arrays
    /// 3. Pass to phenology functions and carbon exchange computations
    ///
    /// COORDINATE SYSTEM:
    /// Latitude range: -65° to 65° (validation bounds for day length calculations)
    /// </summary>
    public class input
    {
        // ====================================================================
        // DAILY METEOROLOGICAL INPUTS
        // ====================================================================

        /// <summary>
        /// Photosynthetically Active Radiation (PAR)
        /// Units: MJ m⁻² d⁻¹
        /// PAR is approximately 50.5% of total solar radiation
        /// Conversion factor: 50.5% × 4.57 µmol/J
        /// </summary>
        public float PAR { get; set; }

        /// <summary>
        /// Vegetation index type identifier (e.g., "NDVI", "EVI")
        /// Used to specify which vegetation index is being simulated
        /// </summary>
        public string vegetationIndex { get; set; }

        /// <summary>
        /// Maximum daily air temperature
        /// Units: °C
        /// Used in temperature forcing functions and hourly disaggregation
        /// Peak hourly temperature occurs at hour 15 via sinusoidal interpolation
        /// </summary>
        public float airTemperatureMaximum { get; set; }

        /// <summary>
        /// Minimum daily air temperature
        /// Units: °C
        /// Used in chilling accumulation and thermal forcing calculations
        /// Minimum hourly temperature occurs at sunrise
        /// </summary>
        public float airTemperatureMinimum { get; set; }

        /// <summary>
        /// Minimum daily relative humidity
        /// Units: % (0-100)
        /// Used to compute vapor pressure deficit (VPD) for stomatal conductance
        /// </summary>
        public float relativeHumidityMinimum { get; set; }

        /// <summary>
        /// Maximum daily relative humidity
        /// Units: % (0-100)
        /// Used to compute vapor pressure deficit (VPD) for stomatal conductance
        /// </summary>
        public float relativeHumidityMaximum { get; set; }

        /// <summary>
        /// Global solar radiation (total shortwave radiation)
        /// Units: MJ m⁻²
        /// Partitioned into direct/diffuse components by PartitionRadiation()
        /// </summary>
        public float solarRadiation { get; set; }

        /// <summary>
        /// Wind speed at standard measurement height
        /// Units: m s⁻¹
        /// Used in evapotranspiration calculations
        /// </summary>
        public float windSpeed { get; set; }

        /// <summary>
        /// Dew point temperature
        /// Units: °C
        /// Used to calculate actual vapor pressure and VPD
        /// </summary>
        public float dewPointTemperature { get; set; }

        /// <summary>
        /// Daily precipitation
        /// Units: mm
        /// Fed into rolling window memory for water stress calculations
        /// Memory length controlled by parPhotosynthesis.waterStressDays
        /// </summary>
        public float precipitation { get; set; }

        /// <summary>
        /// Observation date
        /// Used for day-of-year extraction and solar geometry calculations
        /// </summary>
        public DateTime date { get; set; }

        /// <summary>
        /// Site latitude
        /// Units: decimal degrees (negative for Southern Hemisphere)
        /// Valid range: -65° to 65° (solar geometry calculation limits)
        /// Used in astronomy() function for day length and solar position
        /// </summary>
        public float latitude { get; set; }

        /// <summary>
        /// Nested radiation data object containing solar geometry calculations
        /// Populated by astronomy() function in utils.cs
        /// </summary>
        public radData radData = new radData();

        // ====================================================================
        // HOURLY DISAGGREGATED DATA (24-element arrays, indices 0-23)
        // ====================================================================
        // These arrays are automatically populated by vvvvInterface.estimateHourly()
        // using sinusoidal interpolation and solar geometry constraints.
        // All arrays use local solar time indexing (hour 0 = midnight).
        // ====================================================================

        /// <summary>
        /// Hourly timestamps (24 hours)
        /// Generated from daily date field
        /// </summary>
        public DateTime[] dateH = new DateTime[24];

        /// <summary>
        /// Hourly wind speed
        /// Units: m s⁻¹
        /// Assumed constant throughout day from daily average
        /// </summary>
        public float[] windSpeedH = new float[24];

        /// <summary>
        /// Hourly air temperature
        /// Units: °C
        /// Sinusoidal interpolation: Tavg + DT/2 × cos(0.2618 × (h - 15))
        /// Peak at hour 15, minimum at sunrise
        /// CRITICAL: Used directly in carbon flux calculations (no energy balance model)
        /// </summary>
        public float[] airTemperatureH = new float[24];

        /// <summary>
        /// Hourly soil temperature
        /// Units: °C
        /// Used in heterotrophic respiration calculations via Lloyd-Taylor function
        /// </summary>
        public float[] soilTemperatureH = new float[24];

        /// <summary>
        /// Hourly precipitation
        /// Units: mm
        /// Distributed proportionally from daily total
        /// </summary>
        public float[] precipitationH = new float[24];

        /// <summary>
        /// Hourly solar radiation
        /// Units: MJ m⁻²
        /// Distributed proportional to extraterrestrial radiation curve
        /// Conversion to W/m²: multiply by 1269.44
        /// Conversion in VPRM: multiply by 277.78 (W/m² to MJ/m²/h)
        /// Zero during nighttime hours (before sunrise, after sunset)
        /// </summary>
        public float[] solarRadiationH = new float[24];

        /// <summary>
        /// Hourly relative humidity
        /// Units: % (0-100)
        /// Derived from temperature and dew point curves
        /// </summary>
        public float[] relativeHumidityH = new float[24];

        /// <summary>
        /// Hourly vapor pressure deficit
        /// Units: hPa
        /// Calculated from temperature and relative humidity
        /// Used in VPDfunction() for stomatal conductance response
        /// </summary>
        public float[] vaporPressureDeficitH = new float[24];

        /// <summary>
        /// Hourly reference evapotranspiration (FAO-56 Penman-Monteith)
        /// Units: mm h⁻¹
        /// Used in water stress function as demand component
        /// Balanced against precipitation memory to compute water availability
        /// </summary>
        public float[] referenceET0H = new float[24];
    }

    /// <summary>
    /// Solar radiation and geometry data container.
    /// Populated by astronomy() function using site latitude and date.
    ///
    /// SOLAR GEOMETRY CALCULATIONS:
    /// - Solar declination angle from day of year
    /// - Sunrise/sunset hour angles and times
    /// - Day length (hours between sunrise and sunset)
    /// - Extraterrestrial radiation (top-of-atmosphere solar flux)
    ///
    /// CRITICAL CONSTANT:
    /// Solar constant = 4.921 MJ m⁻² h⁻¹ (used in ETR calculation)
    /// </summary>
    public class radData
    {
        /// <summary>
        /// Photoperiod (hours of daylight)
        /// Units: hours
        /// Critical driver for dormancy induction and release
        /// Used in photoperiod limiting functions (sigmoid response)
        /// Valid for latitudes between -65° and 65°
        /// </summary>
        public float dayLength { get; set; }

        /// <summary>
        /// Hourly extraterrestrial radiation (top-of-atmosphere)
        /// Units: MJ m⁻² h⁻¹ (24-element array)
        /// Used as template for distributing measured solar radiation across day
        /// Zero during nighttime hours
        /// </summary>
        public float[] etrHourly = new float[24];

        /// <summary>
        /// Hourly global solar radiation (disaggregated from daily total)
        /// Units: MJ m⁻² h⁻¹ (24-element array)
        /// Proportional to etrHourly with measured daily total as constraint
        /// Conversion to W/m²: multiply by 1269.44
        /// </summary>
        public float[] gsrHourly = new float[24];

        /// <summary>
        /// Daily global solar radiation (measured)
        /// Units: MJ m⁻² d⁻¹
        /// Total incoming shortwave radiation at surface
        /// </summary>
        public float gsr { get; set; }

        /// <summary>
        /// Daily extraterrestrial radiation (calculated)
        /// Units: MJ m⁻² d⁻¹
        /// Top-of-atmosphere solar flux for given latitude and date
        /// Used to compute clearness index and partition direct/diffuse radiation
        /// </summary>
        public float etr { get; set; }

        /// <summary>
        /// Hour of sunrise (local solar time)
        /// Units: decimal hours (0-24)
        /// Marks start of daylight period and minimum temperature time
        /// </summary>
        public float hourSunrise { get; set; }

        /// <summary>
        /// Hour of sunset (local solar time)
        /// Units: decimal hours (0-24)
        /// Marks end of daylight period
        /// </summary>
        public float hourSunset { get; set; }

        /// <summary>
        /// Site latitude (stored for reference)
        /// Units: decimal degrees (negative for Southern Hemisphere)
        /// </summary>
        public float latitude { get; set; }
    }
}
