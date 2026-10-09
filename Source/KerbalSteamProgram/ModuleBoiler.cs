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
        [KSPField]
        public float InjectorRate = 1.0f; //U/s
        [KSPField]
        public float ThermalConductance = 1.0f; //kW/K

        [KSPField(isPersistant = false)]
        public string moduleID = "boiler";

        ModuleBoiler parent_Boiler = null;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Throttle", guiUnits = "%", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Injector", guiUnits = "%", groupName = "sysBoilerinfo")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Injector = 0f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Injection Rate", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        public string flowRateLabel = "0.0 U/s";

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = " ", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        public string parent_Boiler_connected_text = "paired with parentBoiler";

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "ThermalEnergy", guiUnits = "kJ", groupName = "sysBoilerinfo")]
        public float ThermalEnergy = 0f; //kJ

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Pressure", guiUnits = "kPa")]
        //[UI_ProgressBar(minValue = 0f, maxValue = 1500f)]
        //public float Pressure = 101f;

        [KSPField]
        public float Temperature = 288f; //Kelvin

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Temperature", groupName = "sysBoilerinfo")]
        public string Temperature_text = "0.0 K";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Water Level", groupName = "sysBoilerinfo")]
        [UI_ProgressBar(minValue = 0f, maxValue = 100f)]
        public float Water_Level = 75f;

        public const string Water = "Water";
        private int waterResourceID = -1;
        protected float radiativeFlux; //kW
        const float Convertion_energy = 2260; // kJ/kg  2260 kJ/kg
        const float Gass_Constant = 461.5f; // kJ/tonne*K   0.4615 kJ/kg*K

        public ModuleSystemHeat SystemHeatModule = null;
        public ModuleSteamReservoar SteamReservoarModule = null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            parent_Boiler = this.part.parent.FindModuleImplementing<ModuleBoiler>();

            waterResourceID = PartResourceLibrary.Instance.GetDefinition(Water).id;

            this.part.FindModuleImplementing<ModuleSystemHeat>();

            PartResource internalWater = this.part.Resources[Water];
            PartResource internalSteam = this.part.Resources["Steam"];
            PartResourceDefinition waterDef = internalWater.info;
            PartResourceDefinition steamDef = internalSteam.info;
            float massWaterTonnes = (float)internalWater.amount * 0.001f;
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature = UndoSaturationVaporPressureCurve(101.325f);

            /// <summary>
            /// Derived from:
            /// float volume = (float)internalWater.maxAmount * 0.001f; // m^3 = Unit * m^3/Liter
            /// float V_Water = (float)internalWater.amount * 0.001f; // m^3 = Unit * m^3/Liter
            /// float V_Steam = Mathf.Max(volume - V_Water, 0.01f);
            /// Temperature = ThermalEnergy / (steamDef.specificHeatCapacity * massSteamTonnes);
            /// Pressure = ((float)internalSteam.amount * Gass_Constant * Temperature) / (V_Steam);
            /// </summary>
            float initial_steam = (float)((internalSteam.amount * Gass_Constant * Temperature * 1000) - 101.325f * (internalWater.maxAmount - internalWater.amount)) / (101.325f - Gass_Constant * Temperature * 1000);

            internalWater.amount -= initial_steam;
            internalSteam.amount += initial_steam;

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

            string[] fields_in_dep = { /*"Injector", "Steam_Valve" */     }; //independant
            string[] fields_co_dep = { "parent_Boiler_connected_text" }; //codependant
            if (parent_Boiler == null)
            {
                for (int i = 0; i < fields_in_dep.Length; i++)
                {
                    BaseField field = Fields[fields_in_dep[i]];
                    field.guiActive = true;
                    field.guiActiveEditor = true;
                }
                for (int i = 0; i < fields_co_dep.Length; i++)
                {
                    BaseField field = Fields[fields_co_dep[i]];
                    field.guiActive = false;
                    field.guiActiveEditor = false;
                }
            }
            else
            {
                for (int i = 0; i < fields_co_dep.Length; i++)
                {
                    BaseField field = Fields[fields_co_dep[i]];
                    field.guiActive = true;
                    field.guiActiveEditor = true;
                }
                for (int i = 0; i < fields_in_dep.Length; i++)
                {
                    BaseField field = Fields[fields_in_dep[i]];
                    field.guiActive = false;
                    field.guiActiveEditor = false;
                }
            }
        }
        public void AddFlux(float Flux)
        {
            ThermalEnergy += Flux;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            //int stepcount = 1;
            float deltatime = TimeWarp.fixedDeltaTime;
            //float WarpRate = TimeWarp.CurrentRate;
            //if(WarpRate > 10)
            //{
            //    stepcount = Mathf.CeilToInt(WarpRate / 10);
            //    deltatime = TimeWarp.fixedDeltaTime / stepcount;
            //}
            
            //for (int i = 0; i < stepcount; i++)
            {
                PartResource internalWater = this.part.Resources[Water];
                PartResource internalSteam = this.part.Resources["Steam"];

                PartResourceDefinition waterDef = internalWater.info;
                PartResourceDefinition steamDef = internalSteam.info;

                if(parent_Boiler != null)
                {
                    PartResource parent_internalWater = parent_Boiler.part.Resources[Water];
                    PartResource parent_internalSteam = parent_Boiler.part.Resources["Steam"];

                    Steam_Valve = parent_Boiler.Steam_Valve;
                    Injector = parent_Boiler.Injector;

                    double volume_AB = internalWater.maxAmount + parent_internalWater.maxAmount;
                    double volume_rA =        internalWater.maxAmount / volume_AB;
                    double volume_rB = parent_internalWater.maxAmount / volume_AB;

                    double water_AB = internalWater.amount + parent_internalWater.amount;
                           internalWater.amount = water_AB * volume_rA;
                    parent_internalWater.amount = water_AB * volume_rB;

                    double steam_AB = internalSteam.amount + parent_internalSteam.amount;
                           internalSteam.amount = steam_AB * volume_rA;
                    parent_internalSteam.amount = steam_AB * volume_rB;

                    double energy_AB = ThermalEnergy + parent_Boiler.ThermalEnergy;
                                  ThermalEnergy = (float)(energy_AB * volume_rA);
                    parent_Boiler.ThermalEnergy = (float)(energy_AB * volume_rB);
                }


                if (Injector > 0f)
                {
                    float targetUnitsPerSecond = InjectorRate * (Injector / 100f);
                    double unitsToPullThisFrame = targetUnitsPerSecond * deltatime;
                    if (internalWater.amount + unitsToPullThisFrame >= internalWater.maxAmount)
                    {
                        unitsToPullThisFrame = internalWater.maxAmount - internalWater.amount;
                    }

                    double unitsActuallyDrained = this.part.RequestResource(waterResourceID, unitsToPullThisFrame);
                    flowRateLabel = $"{unitsActuallyDrained / deltatime:F2} U/s";

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
                        return;
                    }
                }
                if (SteamReservoarModule == null)
                {
                    SteamReservoarModule = this.part.FindModuleImplementing<ModuleSteamReservoar>();
                    if (SteamReservoarModule != null)
                    {
                        return;
                    }
                }

                radiativeFlux = ThermalConductance * (Temperature - SystemHeatModule.currentLoopTemperature);

                float massWaterTonnes = (float)internalWater.amount * 0.001f; //tonne = kg * tonne/kg
                float massSteamTonnes = (float)internalSteam.amount * 0.001f;
                float HeatCapacity = (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes);
                if (HeatCapacity < 0.000001f)
                {
                    SystemHeatModule.AddFlux(moduleID, Temperature, (radiativeFlux / deltatime), false);
                    ThermalEnergy -= radiativeFlux;
                    Temperature = 0;
                }
                else
                {
                    float delta_E = SteamLoop.delta_energy_exp_aproach(Temperature, HeatCapacity, SystemHeatModule.Loop, radiativeFlux, deltatime);

                    SystemHeatModule.AddFlux(moduleID, Temperature, (delta_E / deltatime), false);
                    ThermalEnergy -= delta_E;

                    Temperature = ThermalEnergy / HeatCapacity;
                }
                Temperature_text = $"{Temperature:F1} K / {(Temperature - 273.15f):F0} °C";

                float volume = (float)internalWater.maxAmount * 0.001f; // m^3 = Unit * m^3/Liter

                float V_Water = (float)internalWater.amount * 0.001f; // m^3 = Unit * m^3/Liter
                float V_Steam = Mathf.Max(volume - V_Water, 0.01f);

                float Pressure_Ideal = SaturationVaporPressureCurve(Temperature);

                float Mass_Steam_Ideal = (Pressure_Ideal * V_Steam) / (Gass_Constant * Temperature); // PV=mRT --> m=PV/RT   kPa * m^3 = tonne * kJ/tonne*K * K
                float Units_Steam_Ideal = Mass_Steam_Ideal * 1000f; // Unit = tonn * kg/tonne

                float delta_units = (Units_Steam_Ideal - (float)internalSteam.amount) * (1 - Mathf.Pow(1.5f, -deltatime));

                delta_units = Mathf.Clamp(delta_units, -(float)internalSteam.amount, Mathf.Min((float)internalWater.amount, (ThermalEnergy / Convertion_energy)));

                ThermalEnergy -= Convertion_energy * delta_units;
                internalWater.amount -= delta_units;
                internalSteam.amount += delta_units;


                //Pressure = ((float)internalSteam.amount * Gass_Constant * Temperature) / (V_Steam * 1000); // PV=mRT --> P=mRT/V  kPa = kg * kJ/tonne*K * K / m^3 * 1000Pa/kPa
                if (SteamReservoarModule != null)
                {
                    HeatCapacity = (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes);
                    if (HeatCapacity >= 0.000001f) Temperature = ThermalEnergy / HeatCapacity;
                    volume = (float)internalWater.maxAmount * 0.001f;

                    V_Water = (float)internalWater.amount * 0.001f;
                    V_Steam = Mathf.Max(volume - V_Water, 0.01f);
                    SteamReservoarModule.volume = V_Steam * 1000;
                    SteamReservoarModule.ThermalEnergy = Temperature * (steamDef.specificHeatCapacity * massSteamTonnes);
                }

            }
        }
        protected void Update()
        {
            if(HighLogic.LoadedSceneIsEditor)
            {
                parent_Boiler = this.part.parent.FindModuleImplementing<ModuleBoiler>();
            }
            if (SteamReservoarModule == null)
            {
                SteamReservoarModule = this.part.FindModuleImplementing<ModuleSteamReservoar>();
                if (SteamReservoarModule != null)
                {
                    return;
                }
            }
            if (SteamReservoarModule != null)
            {
                SteamReservoarModule.Steam_Valve = Steam_Valve;

                BaseField field = SteamReservoarModule.Fields["Steam_Valve"];

                if (field != null)
                {
                    field.guiActive = false;
                    field.guiActiveEditor = false;
                }
            }
        }

        float SaturationVaporPressureCurve(float Temp_Kelvin) //kPa
        {
            float Temp_Celcius = Temp_Kelvin - 273.15f;
            return 0.6113f * Mathf.Exp((17.27f * Temp_Celcius) / (Temp_Celcius + 237.3f));
        }
        float UndoSaturationVaporPressureCurve(float Pressure)
        {
            float L = Mathf.Log(Pressure / 0.6113f);
            float Temp_Celcius = (237.3f * L) / (17.27f - L);
            return Temp_Celcius + 273.15f;
        }
    }
}
