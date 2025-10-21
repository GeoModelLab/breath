using source.data;
using System;
using System.ComponentModel.Design;

namespace source.functions
{
    //this class contains the method to simulate the growth, greendown and decline processes
    public class exchanges
    {
        // fuori dal loop orario, p.es. a livello di metodo o di istanza del modello
        float prevRecoState = float.NaN; // persiste tra i giorni


        public void VPRM(input input, parameters parameters, output output, output outputT1)
        {
            hourlyData hourlyData = input.hourlyData;
           
            //estimate vegetation cover
            outputT1.exchanges.vegetationCover = utils.estimateVegetationCover(outputT1, parameters);

            //estimate LAI
            var (LAIoverstory, LAIunderstory, EVIoverstory, EVIunderstory) = utils.estimateLAI(outputT1);
            outputT1.exchanges.LAIoverstory = LAIoverstory;
            outputT1.exchanges.LAIunderstory= LAIunderstory;
            outputT1.exchanges.EVIoverstory = EVIoverstory;
            outputT1.exchanges.EVIunderstory = EVIunderstory;

            //estimate hourly extraterrestrial radiation
            float[] extraterrestrialRadiationHourly = utils.astronomy(input, true).etrHourly;

            // maximum air temperature
            float tAirMaxDay = hourlyData.airTemperature.Max();

            // Constants for PAR conversion (if you start from SW)
            const float FRACTION_PAR_OF_SW = 0.505f;  // ~50.5% of SW is PAR (tunable 0.45–0.5)
            const float UMOL_PER_J = 4.57f;          // µmol per joule
            const float SW_TO_PAR = FRACTION_PAR_OF_SW * UMOL_PER_J;

            
            //hourly loop
            for (int h = 0; h < hourlyData.airTemperature.Count; h++)
            {
                #region Radiation partitioning and PAR conversion 
                
                // same units 
                float SW_IN = hourlyData.photoActiveRadiation[h];               
                float SW_TOA = extraterrestrialRadiationHourly[h];
                var (SW_DIR_H, SW_DIF_H) = utils.PartitionRadiation(SW_IN, SW_TOA);

                // compute PAR
                float PAR_DIR_H = SW_DIR_H * SW_TO_PAR; // direct PAR on horizontal
                float PAR_DIF_H = SW_DIF_H * SW_TO_PAR; // diffuse PAR on horizontal
                outputT1.exchanges.PARdirect.Add(PAR_DIR_H);
                outputT1.exchanges.PARdiffuse.Add(PAR_DIF_H);

                // overstory attenuation from LAIover (phenology-aware) ---
                float LAIover_eff = (outputT1.phenoCode < 3) ? 0f : LAIoverstory;

                // light extinction coefficients
                float kb_over = parameters.parExchanges.LightExtinctionCoefficient;      
                float k_d_over = 0.8f * kb_over; 

                // light interception
                float Pgap_dir_over = (float)Math.Exp(-kb_over * LAIover_eff); // beam gap prob
                float Pgap_dif_over = (float)Math.Exp(-k_d_over * LAIover_eff); // diffuse transmittance

                // PAR that reaches the TOP of the understory
                float PAR_under_top_dir = PAR_DIR_H * Pgap_dir_over;
                float PAR_under_top_dif = PAR_DIF_H * Pgap_dif_over;

                // PAR absorbed by the OVERSTORY 
                float Iabs_over = (PAR_DIR_H * (1f - Pgap_dir_over)) + (PAR_DIF_H * (1f - Pgap_dif_over));
                outputT1.exchanges.LightOverstory.Add(Iabs_over);

                // understory absorption
                float LAIunder_eff = LAIunderstory;

                // simplification: same extinction coefficients of the overstory
                float kb_under = kb_over;
                float kdif_under = k_d_over;

                // light interception from understory
                float Iabs_under =
                    PAR_under_top_dir * (1f - (float)Math.Exp(-kb_under * LAIunder_eff)) +
                    PAR_under_top_dif * (1f - (float)Math.Exp(-kdif_under * LAIunder_eff));
                outputT1.exchanges.LightUnderstory.Add(Iabs_under);

                // PAR modifier for GPP overstory 
                float PARscaleOverstory = (outputT1.phenoCode < 3)
                    ? 0f
                    : utils.PARGppfunction(outputT1, Iabs_over, parameters.parExchanges.halfSaturationTree);

                // PAR modifier for GPP understory
                float PARscaleUnderstory = utils.PARGppfunction(outputT1, Iabs_under,
                    parameters.parExchanges.halfSaturationUnder);

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
                    LeafTemperatureOver = utils.LeavesTemperature(hourlyData.airTemperature[h], hourlyData.soilTemperature[h],
                        hourlyData.vaporPressureDeficit[h], tAirMaxDay, absorptionCoefficient: parameters.parExchanges.absorptionCoefficient,
                        LeafOrientation: 40, DirectLight: PAR_DIR_H, DiffuseLight: PAR_DIF_H, LeavesEmissivity: 0.97F,
                        StomatalResistance: 600, CuticolarResistance: 500, IntercellularSpaceResistance: 25,
                        WindSpeed: .55F, LeafLength: parameters.parExchanges.leafLength, LeafShapeParam: 4F);
                    
                    TscaleOver = utils.temperatureFunction(LeafTemperatureOver, parameters.parExchanges.minimumTemperature,
                           parameters.parExchanges.optimumTemperature, parameters.parExchanges.maximumTemperature);
                }
                outputT1.exchanges.TleafOver.Add(LeafTemperatureOver);
                outputT1.exchanges.TscaleOver.Add(TscaleOver);

                //leaves temperature of the understory
                float LeafTemperatureUnder = utils.LeavesTemperature(hourlyData.airTemperature[h], hourlyData.soilTemperature[h],
                  hourlyData.vaporPressureDeficit[h], tAirMaxDay, absorptionCoefficient: parameters.parExchanges.absorptionCoefficient,
                  LeafOrientation: 40, DirectLight: PAR_under_top_dir, DiffuseLight: PAR_under_top_dif, LeavesEmissivity: 0.97F,
                  StomatalResistance: 600, CuticolarResistance: 500, IntercellularSpaceResistance: 25,
                  WindSpeed: .55F, LeafLength: parameters.parExchanges.leafLength, LeafShapeParam: 4F);

                //temperature scale factor (understory)
                float TscaleUnder = utils.temperatureFunction(LeafTemperatureUnder, parameters.parExchanges.minimumTemperature,
                       parameters.parExchanges.optimumTemperature, 
                       parameters.parExchanges.maximumTemperature);

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
                float VPDscale = utils.VPDfunction(hourlyData.vaporPressureDeficit[h] / 10, parameters);
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
                float Treco = utils.temperatureRecoFunction(hourlyData.soilTemperature[h],  parameters);
                
                outputT1.exchanges.TscaleReco.Add(Treco);
                //same water stress than GPP
                outputT1.exchanges.WscaleReco.Add(waterStress);

                //estimate RECO from GPP
                
                float RECOgppFunctionUnder = utils.gppRecoFunction(input, gppOver, gppUnder, 
                    PARscaleOverstory,PARscaleUnderstory, outputT1, parameters);

                float rawReco = estimateRECO(RECOgppFunctionUnder, Treco, waterStress);
                // Limite ±X%/h solo di notte, con gestione prima ora (lista vuota)

                float reco = LimitRecoNight(rawReco, SW_IN, prevRecoState, parameters, (PARscaleUnderstory));
                // aggiorna stato per l'ora successiva (e per il giorno successivo!)
                prevRecoState = reco;

                

                //just for saving the variable
                outputT1.exchanges.reco.Add(reco);           

                #endregion

                //Net Ecosystem Exchange
                outputT1.exchanges.nee.Add(gppOver - reco);
            }

            //daily values
            outputT1.exchanges.gppDaily = outputT1.exchanges.gpp.Sum();
            outputT1.exchanges.recoDaily = outputT1.exchanges.reco.Sum();

            //daily net ecosystem exchange
            outputT1.exchanges.neeDaily = outputT1.exchanges.gppDaily - 
                outputT1.exchanges.recoDaily;
        }

        #region GPP functions
        //Gross Primary Production of the overstory
        private float estimateGPPoverstory(parameters parameters, float VPDmodifier, float Tscale, float par, float PARscale, 
            float Wscale, float PhenologyScaleGPP, float evi)
        {
            float gpp = parameters.parExchanges.maximumQuantumYieldTree * VPDmodifier *
                Tscale * Wscale * par * PARscale * PhenologyScaleGPP * evi;
            return gpp;
        }

        //Gross Primary Production of the understory
        private float estimateGPPunderstory(parameters parameters, float VPDmodifier, float Tscale, float par, float PARscale,
          float Wscale, float evi)
        {
            float gpp = parameters.parExchanges.maximumQuantumYieldUnder * VPDmodifier *
                Tscale * Wscale * par * PARscale * evi;
            return gpp;
        }
        #endregion

        #region Respiration functions

        private float estimateRECO(float gppRECO, float TscaleReco, float WscaleReco)
        {
            return gppRECO * TscaleReco * WscaleReco; 
        }

       private float LimitRecoNight(
       float rawReco,      // RECO calcolata “grezza” per l’ora h
       float par,          // PAR/SW_IN dell’ora h
       float prevReco,     // ultima RECO uscita (può essere NaN alla prima ora)
       parameters p,
       float PARscaleFactor)
        {
            // prima ora: nessun blending per evitare salti iniziali
            if (float.IsNaN(prevReco)) return rawReco;

            float a = 0;

            bool isNight = par <  p.parExchanges.parRespSmoothing; // es. 5

            if(isNight) { a = Math.Clamp(p.parExchanges.nightSensitivityScale, 0f, 1f); }
            else { return rawReco; };
            if (prevReco > 0)
            { }

            return prevReco + a * (rawReco - prevReco);   // sempre attivo di notte
            
        }

        #endregion
    }
}