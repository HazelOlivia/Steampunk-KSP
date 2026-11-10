using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity;
using UnityEngine;
using SystemHeat;
using DynamicBatteryStorage;

namespace KerbalSteamProgram
{
    public class ModuleBoiler : ModuleSystemHeat
    {
        [KSPField]
        public float InjectorRate = 1.0f; //U/s
        [KSPField]
        public float ThermalConductance = 1.0f; //kW/K

        [KSPField]
        public ModuleBoiler parent_Boiler = null;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Throttle", guiUnits = "%", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Throttle = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Injector", guiUnits = "%", groupName = "sysBoilerinfo")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Injector = 0f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Injection Rate", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        public string flowRateLabel = "0.0 U/s";

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = " ", groupName = "sysBoilerinfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemBoiler_GroupName")]
        public string parent_Boiler_connected_text = "paired with parentBoiler";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "ThermalEnergy", guiUnits = "kJ", groupName = "sysBoilerinfo")]
        public float ThermalEnergy_Boiler = 1000f; //kJ

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Pressure", guiUnits = "kPa")]
        //[UI_ProgressBar(minValue = 0f, maxValue = 1500f)]
        //public float Pressure = 101f;

        [KSPField]
        public float Temperature_Boiler = 288f; //Kelvin

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Temperature", groupName = "sysBoilerinfo")]
        public string Temperature_text = "0.0 K";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Water Level", groupName = "sysBoilerinfo")]
        [UI_ProgressBar(minValue = 0f, maxValue = 100f)]
        public float Water_Level = 75f;

        public const string Water = "Water";
        private int waterResourceID = -1;
        protected float radiativeFlux; //kW
        const float Convertion_energy = 2260; // kJ/kg  2260 kJ/kg

        //public ModuleSystemHeat SystemHeatModule = null;
        public ModuleSteamReservoir SteamReservoirModule = null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            if (this.part.parent != null)
            {
                parent_Boiler = this.part.parent.FindModuleImplementing<ModuleBoiler>();
                if(parent_Boiler != null)
                {
                    while (true)
                    {
                        if (parent_Boiler.part.parent == null) break;
                        ModuleBoiler potential_parent_Boiler = parent_Boiler.part.parent.FindModuleImplementing<ModuleBoiler>();
                        if (potential_parent_Boiler != null) parent_Boiler = potential_parent_Boiler;
                        else break;
                    }
                }
            }

            SteamReservoirModule = this.part.FindModuleImplementing<ModuleSteamReservoir>();
            if (SteamReservoirModule != null)
            {
                PartResource internalWater = this.part.Resources[Water];
                float volume = (float)internalWater.maxAmount; // Liters
                float V_Water = (float)internalWater.amount; // Liters
                float V_Steam = volume - V_Water;
                SteamReservoirModule.volume = V_Steam;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                // Start the waiting process without blocking the game
                StartCoroutine(WaitForAtmosphere());
            }
        }
        private System.Collections.IEnumerator WaitForAtmosphere()
        {
            if (FlightGlobals.ready && vessel != null && vessel.loaded)
            {
                for (int i = 0; i < 30; i++)
                {
                    if (part.staticPressureAtm > 0)
                    {
                        break;
                    }

                    yield return null; // Wait for the next frame
                }
            }
            InitializeModule();
        }
        private void InitializeModule()
        {

            waterResourceID = PartResourceLibrary.Instance.GetDefinition(Water).id;

            PartResource internalWater = this.part.Resources[Water];
            PartResource internalSteam = this.part.Resources["Steam"];
            PartResourceDefinition waterDef = internalWater.info;
            PartResourceDefinition steamDef = internalSteam.info;
            float massWaterTonnes = (float)internalWater.amount * 0.001f;
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature_Boiler = 288f;

            float volume = (float)internalWater.maxAmount; // Liters
            float V_Water = (float)internalWater.amount; // Liters
            float V_Steam = volume - V_Water;
            base.volume = V_Steam;

            SteamReservoirModule.Mass_Air = ((float)part.staticPressureAtm * 101.325f) * V_Steam / (ModuleSteamReservoir.Gas_Constant_Air * Temperature_Boiler);
            float massAirTonnes = SteamReservoirModule.Mass_Air * 0.001f;

            ThermalEnergy_Boiler = Temperature_Boiler * (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes + ModuleSteamReservoir.specificHeatCapacity_Air * massAirTonnes);

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

            string[] fields_in_dep = { "Injector", "Steam_Valve"      }; //independant
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
            ThermalEnergy_Boiler += Flux;
        }

        //public new void UpdateSimulationValues(float nominalTemp, float currentTemp, float currentNetFlux)
        //{
        //    base.UpdateSimulationValues(nominalTemp, currentTemp, currentNetFlux);

        //    try
        //    {
        //        float deltatime = base.Loop.timeStep;

        //        if (SteamReservoirModule == null) SteamReservoirModule = this.part.FindModuleImplementing<ModuleSteamReservoir>();

        //        PartResource internalWater = this.part.Resources[Water];
        //        PartResource internalSteam = this.part.Resources["Steam"];

        //        PartResourceDefinition waterDef = internalWater.info;
        //        PartResourceDefinition steamDef = internalSteam.info;

        //        if (parent_Boiler != null)
        //        {
        //            PartResource parent_internalWater = parent_Boiler.part.Resources[Water];
        //            PartResource parent_internalSteam = parent_Boiler.part.Resources["Steam"];

        //            Throttle = parent_Boiler.Throttle;
        //            Injector = parent_Boiler.Injector;

        //            double volume_AB = internalWater.maxAmount + parent_internalWater.maxAmount;
        //            double volume_rA = internalWater.maxAmount / volume_AB;
        //            double volume_rB = parent_internalWater.maxAmount / volume_AB;

        //            double water_AB = internalWater.amount + parent_internalWater.amount;
        //            internalWater.amount = water_AB * volume_rA;
        //            parent_internalWater.amount = water_AB * volume_rB;

        //            double steam_AB = internalSteam.amount + parent_internalSteam.amount;
        //            internalSteam.amount = steam_AB * volume_rA;
        //            parent_internalSteam.amount = steam_AB * volume_rB;

        //            double air_AB = SteamReservoirModule.Mass_Air + parent_Boiler.SteamReservoirModule.Mass_Air;
        //            SteamReservoirModule.Mass_Air = (float)(air_AB * volume_rA);
        //            parent_Boiler.SteamReservoirModule.Mass_Air = (float)(air_AB * volume_rB);

        //            double energy_AB = ThermalEnergy_Boiler + parent_Boiler.ThermalEnergy_Boiler;
        //            ThermalEnergy_Boiler = (float)(energy_AB * volume_rA);
        //            parent_Boiler.ThermalEnergy_Boiler = (float)(energy_AB * volume_rB);
        //        }


        //        if (Injector > 0f)
        //        {
        //            float targetUnitsPerSecond = InjectorRate * (Injector / 100f);
        //            double unitsToPullThisFrame = targetUnitsPerSecond * deltatime;
        //            if (internalWater.amount + unitsToPullThisFrame >= internalWater.maxAmount)
        //            {
        //                unitsToPullThisFrame = internalWater.maxAmount - internalWater.amount;
        //            }

        //            double unitsActuallyDrained = this.part.RequestResource(waterResourceID, unitsToPullThisFrame);
        //            flowRateLabel = $"{unitsActuallyDrained / deltatime:F2} U/s";

        //            if (this.part.Resources.Contains(Water))
        //            {
        //                internalWater.amount += unitsActuallyDrained;
        //                ThermalEnergy_Boiler += (float)unitsActuallyDrained * 0.001f * waterDef.specificHeatCapacity * 300f;// kJ = kg * tonne/kg * kJ/tonne*K * K
        //            }
        //        }
        //        else
        //        {
        //            flowRateLabel = $"{0.0f:F2} U/s";
        //        }
        //        Water_Level = Mathf.Round((float)(internalWater.amount / internalWater.maxAmount) * 100f);

        //        float massWaterTonnes = (float)internalWater.amount * 0.001f; //tonne = kg * tonne/kg
        //        float massSteamTonnes = (float)internalSteam.amount * 0.001f;
        //        float massAirTonnes = SteamReservoirModule.Mass_Air * 0.001f;
        //        float HeatCapacity = (waterDef.specificHeatCapacity * massWaterTonnes + steamDef.specificHeatCapacity * massSteamTonnes + ModuleSteamReservoir.specificHeatCapacity_Air * massAirTonnes);
        //        if (HeatCapacity < 0.000001f) HeatCapacity = 0.000001f;

        //        radiativeFlux = ThermalConductance * (Temperature_Boiler - base.currentLoopTemperature);
        //        float delta_E = SteamLoop.delta_energy_exp_aproach(Temperature_Boiler, HeatCapacity, base.Loop, radiativeFlux, deltatime);
        //        base.AddFlux(moduleID, Temperature_Boiler, (delta_E / deltatime), false);
        //        ThermalEnergy_Boiler -= delta_E;

        //        Temperature_Boiler = ThermalEnergy_Boiler / HeatCapacity;
        //        Temperature_text = $"{Temperature_Boiler:F1} K / {(Temperature_Boiler - 273.15f):F0} °C";

        //        float volume_max = (float)internalWater.maxAmount * 0.001f; // m^3 = Unit * m^3/Liter
        //        float V_Water = (float)internalWater.amount * 0.001f; // m^3 = Unit * m^3/Liter
        //        float V_Steam = Mathf.Max(volume_max - V_Water, 0.01f); // m^3

        //        /// define steam and volume in ModuleSteamReservoir
        //        SteamReservoirModule.volume = V_Steam * 1000;
        //        SteamReservoirModule.ThermalEnergy = Temperature_Boiler * (steamDef.specificHeatCapacity * massSteamTonnes + ModuleSteamReservoir.specificHeatCapacity_Air * massAirTonnes);

        //        float Pressure_Steam_Ideal = SaturationVaporPressureCurve(Temperature_Boiler);

        //        float Mass_Steam_Ideal = (Pressure_Steam_Ideal * V_Steam) / (ModuleSteamReservoir.Gas_Constant_Steam * Temperature_Boiler); // PV=mRT --> m=PV/RT   kPa * m^3 = tonne * kJ/tonne*K * K
        //        float Units_Steam_Ideal = Mass_Steam_Ideal * 1000f; // Unit = tonn * kg/tonne

        //        float delta_units = (Units_Steam_Ideal - (float)internalSteam.amount) /* (1 - Mathf.Pow(1.5f, -deltatime))*/;

        //        delta_units = Mathf.Clamp(delta_units, -(float)internalSteam.amount, Mathf.Min((float)internalWater.amount, (ThermalEnergy_Boiler / Convertion_energy)));

        //        ThermalEnergy_Boiler -= Convertion_energy * delta_units;
        //        internalWater.amount -= delta_units;
        //        internalSteam.amount += delta_units;
        //    }
        //    catch { }
        //}
        protected new void Update()
        {
            base.Update();
            if (HighLogic.LoadedSceneIsEditor)
            {
                parent_Boiler = this.part.parent.FindModuleImplementing<ModuleBoiler>();
                if (parent_Boiler != null)
                {
                    while (true)
                    {
                        ModuleBoiler potential_parent_Boiler = parent_Boiler.part.parent.FindModuleImplementing<ModuleBoiler>();
                        if (potential_parent_Boiler != null) parent_Boiler = potential_parent_Boiler;
                        else break;
                    }
                }
            }
            if(SteamReservoirModule != null)
            {
                SteamReservoirModule.Steam_Valve = Throttle;

                BaseField field = SteamReservoirModule.Fields["Steam_Valve"];

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
