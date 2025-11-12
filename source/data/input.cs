using System;

// data namespace. It contains all the classes that are used to pass data to the swell model
namespace source.data
{

    // input data class. It is used as argument in each computing method
    public class input
    {
        public float PAR { get; set; } //photosynthetically active radiation, MJ m-2 d-1
       
        public string vegetationIndex { get; set; }

        public float airTemperatureMaximum { get; set; }  //air temperature maximum, °C
        public float relativeHumidityMinimum { get; set; } //air relative humidity minimum, %
        public float relativeHumidityMaximum { get; set; } //air relative humidity minimum, %
        public float solarRadiation { get; set; } //global solar radiation, MJ m-2
        public float windSpeed { get; set; }  //wind speed, m s-1
        public float airTemperatureMinimum { get; set; } //air temperature minimum, °C
        public float dewPointTemperature { get; set; } //dew point temperature, °C
        public float precipitation { get; set; }   //precipitation, mm
        public DateTime date { get; set; }     //date, DateTime object
        public float latitude { get; set; }     //latitude, decimal degrees

        public radData radData = new radData(); //radiation data, see below

        public DateTime[] dateH = new DateTime[24];
        public float[] windSpeedH = new float[24];            // m s-1
        public float[] airTemperatureH = new float[24];       // °C
        public float[] soilTemperatureH = new float[24];      // °C
        public float[] precipitationH = new float[24];        // mm
        public float[] solarRadiationH = new float[24];            // MJ m-2
        public float[] relativeHumidityH = new float[24];     // %
        public float[] vaporPressureDeficitH = new float[24]; // hPa
        public float[] referenceET0H = new float[24];         // mm h-1

    }

    // separate object containing radiation data    
    public class radData
    {
      
        public float dayLength { get; set; } //hours

        public float[] etrHourly = new float[24];
        public float[] gsrHourly = new float[24];
        public float gsr { get; set; } //global solar radiation, MJ m-2 d-1
        public float etr { get; set; } //extraterrestrial solar radiation, MJ m-2 d-1 
        public float hourSunrise { get; set; } //hour
        public float hourSunset { get; set; } //hour
        public float latitude { get; set; }  //latitude
    }
}
