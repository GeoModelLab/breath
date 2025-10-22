using System;

// data namespace. It contains all the classes that are used to pass data to the swell model
namespace source.data
{

    // input data class. It is used as argument in each computing method
    public class input
    {
        public float PAR { get; set; } //photosynthetically active radiation, MJ m-2 d-1
        public float airTemperatureMaximum { get; set; }  //air temperature maximum, °C
        public float airTemperatureMinimum { get; set; } //air temperature minimum, °C
        public float dewPointTemperature { get; set; } //dew point temperature, °C
        public float precipitation { get; set; }   //precipitation, mm
        public DateTime date { get; set; }     //date, DateTime object
        public float latitude { get; set; }     //latitude, decimal degrees

        public radData radData = new radData(); //radiation data, see below

        public hourlyData hourlyData = new hourlyData();

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
    }

    public class hourlyData
    {
        public List<float> airTemperature = new List<float>(); //°C
        public List<float> soilTemperature = new List<float>(); //°C
        public List<float> precipitation = new List<float>(); //mm
        public List<float> photoActiveRadiation = new List<float>(); //micromoles m-2 s-1
        public List<float> relativeHumidity = new List<float>();//%
        public List<float> vaporPressureDeficit = new List<float>(); //hPa
        public List<float> referenceET0 = new List<float>(); //mm h-1
    }
}
