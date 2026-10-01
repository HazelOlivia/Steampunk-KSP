using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity;
using UnityEngine;
using SystemHeat;

namespace KerbalSteamProgram
{
    public class ModuleBoiler : PartModule
    {
        //[KSPField]
        //float totalChamberVolume = 0.25f;
        [KSPField]
        public float InjectorRate = 1.0f; //U/s
        [KSPField]
        public float ThermalConductance = 1.0f; //kW/K

        [KSPField(isPersistant = false)]
        public string moduleID = "boiler";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Debug")]
        public string Debug_Text;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Injector")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Injector = 0f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Injection Rate")]
        public string flowRateLabel = "0.0 U/s";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "ThermalEnergy")]
        public float ThermalEnergy = 0f; //kJ

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Preasure kPa")]
        [UI_FloatRange(minValue = 0f, maxValue = 1500f, stepIncrement = 1f)]
        public float Preasure = 101f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Temperature")]
        public float Temperature = 288f; //Kelvin

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Temperature")]
        public string Temperature_text = "0.0 K";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Level")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Water_Level = 75f;

        public const string Water = "Water";
        private int waterResourceID = -1;
        protected float radiativeFlux; //kW
        const float Convertion_energy = 2260; // kJ/kg  2260 kJ/kg
        const float Gass_Constant = 461.5f; // kJ/tonne*K   0.4615 kJ/kg*K

        public ModuleSystemHeat SystemHeatModule = null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            waterResourceID = PartResourceLibrary.Instance.GetDefinition(Water).id;

            this.part.FindModuleImplementing<ModuleSystemHeat>();

            PartResource internalWater = this.part.Resources[Water];
            PartResource internalSteam = this.part.Resources["Steam"];
            PartResourceDefinition waterDef = internalWater.info;
            PartResourceDefinition steamDef = internalSteam.info;
            float massWaterTonnes = (float)internalWater.amount * 0.001f;
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature = UndoSaturationVaporPreasureCurve(101f);
            ThermalEnergy = Temperature * (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes);
        }

        private void OnDestroy()
        {
            // Always clean up event bindings to avoid memory leaks
            GameEvents.onPartActionUICreate.Remove(OnPartActionUICreate);
        }

        private void OnPartActionUICreate(Part p)
        {
            if (p != this.part) return;

            // Fetch the active right-click window
            UIPartActionWindow paw = UIPartActionController.Instance.GetItem(p);
            if (paw == null) return;

            // Grab the native resource UI elements attached to this menu
            var resourceControls = paw.gameObject.GetComponentsInChildren<UIPartActionResource>();

            foreach (var control in resourceControls)
            {
                if (control.Resource != null && control.Resource.resourceName == Water)
                {
                    // If the element is already hidden, skip it to prevent a layout update loop
                    if (!control.gameObject.activeSelf) continue;

                    // Turn off the visual object completely without breaking KSP's tracking link
                    control.gameObject.SetActive(false);

                    // Inform the parent container to readjust its layout boundaries cleanly
                    paw.UpdateWindow();
                }
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            PartResource internalWater = this.part.Resources[Water];
            PartResource internalSteam = this.part.Resources["Steam"];

            PartResourceDefinition waterDef = internalWater.info;
            PartResourceDefinition steamDef = internalSteam.info;

            if (Injector > 0f)
            {
                float targetUnitsPerSecond = InjectorRate * (Injector / 100f);
                double unitsToPullThisFrame = targetUnitsPerSecond * TimeWarp.fixedDeltaTime;
                if (internalWater.amount + unitsToPullThisFrame >= internalWater.maxAmount)
                {
                    unitsToPullThisFrame = internalWater.maxAmount - internalWater.amount;
                }

                double unitsActuallyDrained = this.part.RequestResource(waterResourceID, unitsToPullThisFrame);
                flowRateLabel = $"{unitsActuallyDrained / TimeWarp.fixedDeltaTime:F2} U/s";

                if (this.part.Resources.Contains(Water))
                {
                    internalWater.amount += unitsActuallyDrained;
                    ThermalEnergy += (float)unitsActuallyDrained * 0.001f * waterDef.specificHeatCapacity * 300f;// kJ = kg * tonne/kg * kJ/tonne*K * K
                }
            }
            else
            {
                flowRateLabel = $"{0.0f:F2} U/s";
            }
            Water_Level = Mathf.Round((float)(internalWater.amount / internalWater.maxAmount) * 100f);


            if (SystemHeatModule == null)
            {
                SystemHeatModule = this.part.FindModuleImplementing<ModuleSystemHeat>();
                if (SystemHeatModule != null)
                {
                    //Temperature_text = "SystemHeatModule == null";
                    return;
                }
            }
            //Temperature_text = $"{SystemHeatModule.currentLoopTemperature:F1} / {this.vessel.externalTemperature:F0} K";

            radiativeFlux = ThermalConductance * (Temperature - SystemHeatModule.currentLoopTemperature);
            SystemHeatModule.AddFlux(moduleID, 0f, radiativeFlux, false);

            //IMPORTANT
            //water 1Unit = 1Liter or 1kg according to CRP
            //steam 1Unit = ?Liter or 1kg

            ThermalEnergy -= radiativeFlux * (float)TimeWarp.fixedDeltaTime;


            float massWaterTonnes = (float)internalWater.amount * 0.001f; //tonne = kg * tonne/kg
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature = (ThermalEnergy) / (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes); //specificHeatCapacity kJ/tonne*K
            Temperature_text = $"{Temperature:F1} K / {(Temperature - 273.15f):F0} °C";

            float volume = (float)internalWater.maxAmount * 0.001f; // m^3 = Unit * m^3/Liter

            float V_Water = (float)internalWater.amount * 0.001f; // m^3 = Unit * m^3/Liter
            float V_Steam = Mathf.Max(volume - V_Water, 0.01f);

            float Preasure_Ideal = SaturationVaporPreasureCurve(Temperature);

            float Mass_Steam_Ideal = (Preasure_Ideal * V_Steam) / (Gass_Constant * Temperature); // PV=mRT --> m=PV/RT   kPa * m^3 = tonne * kJ/tonne*K * K
            float Units_Steam_Ideal = Mass_Steam_Ideal * 1000f; // Unit = tonn * kg/tonne

            float delta_units = (Units_Steam_Ideal - (float)internalSteam.amount) * (1 - Mathf.Pow(1.2f, -TimeWarp.fixedDeltaTime));
            Debug_Text = delta_units.ToString();
            delta_units = Mathf.Clamp(delta_units, -(float)internalSteam.amount, Mathf.Min((float)internalWater.amount, (ThermalEnergy / Convertion_energy)));
            Debug_Text += delta_units.ToString();

            ThermalEnergy -= Convertion_energy * delta_units;
            internalWater.amount -= delta_units;
            internalSteam.amount += delta_units;

            Preasure = ((float)internalSteam.amount * Gass_Constant * Temperature) / (V_Steam * 1000); // PV=mRT --> P=mRT/V  kPa = kg * kJ/tonne*K * K / m^3 * 1000Pa/kPa
        }

        float SaturationVaporPreasureCurve(float Temp_Kelvin) //kPa
        {
            float Temp_Celcius = Temp_Kelvin - 273.15f;
            return 0.6113f * Mathf.Exp((17.27f * Temp_Celcius) / (Temp_Celcius + 237.3f));
        }
        float UndoSaturationVaporPreasureCurve(float Preasure)
        {
            float L = Mathf.Log(Preasure / 0.6113f);
            float Temp_Celcius = (237.3f * L) / (17.27f - L);
            return Temp_Celcius + 273.15f;
        }
    }
}
