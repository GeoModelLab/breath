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
                float VPDscale = utils.VPDfunction(input.vaporPressureDeficitH[h] / 10, parameters);
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
        //Gross Primary Production of the overstory
        private float estimateGPPoverstory(parameters parameters, float VPDmodifier, float Tscale, float par, float PARscale,
            float Wscale, float PhenologyScaleGPP, float evi)
        {
            float limitingFactor = Math.Min(Wscale, Math.Min(VPDmodifier, PARscale));
            float gpp = parameters.parPhotosynthesis.maximumQuantumYieldOver *
                Tscale * limitingFactor * par * PhenologyScaleGPP * evi;
            return gpp;
        }

        //Gross Primary Production of the understory
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

        private float estimateRECOHetero(int h, input input, output outputT, output outputT1, parameters parameters,
   float gppTreeRECO, float TscaleReco, float waterScaleReco)
        {
            float reco = 0;

            reco = parameters.parRespiration.referenceRespirationSoil * TscaleReco * waterScaleReco;
            return reco;
        }

        // variabili di stato per mantenere il valore dell'ora precedente
        private float lastRecoTree = 0f;
        private float lastRecoUnder = 0f;

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

        private float estimateRECOunder(int h, input input, output outputT, output outputT1,
            parameters parameters, float gppUnderRECO, float TscaleReco)
        {
            float reco_raw = gppUnderRECO;

            float alpha = parameters.parRespiration.respirationSmoothingFactor;

            float reco = lastRecoUnder + alpha * (reco_raw - lastRecoUnder);
            lastRecoUnder = reco;

            return reco;
        }


    }
}

#endregion