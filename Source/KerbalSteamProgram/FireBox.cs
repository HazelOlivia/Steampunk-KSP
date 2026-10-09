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
    class FireBox : PartModule
    {
        [KSPField(isPersistant = false)]
        public string moduleID = "firebox";

        [KSPField]
        public float InjectorRate = 1.0f; // U/s
        [KSPField]
        public float LF_ratio = 0.9f;
        [KSPField]
        public float OX_ratio = 1.1f;
        [KSPField]
        public float LF_calorific_value = 44500f; // kJ/kg 44.5MJ/kg
        [KSPField]
        public float HeatingSurface = 1.0f; // m^2
        [KSPField]
        public float HeatCapacity = 2f; // kJ/K

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Fuel Valve", guiUnits = "%", groupName = "sysFireBoxinfo", groupDisplayName = "FireBox")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Fuel_Valve = 0f;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Power", guiUnits = "kW", groupName = "sysFireBoxinfo")]
        public float Power = 0f;

        [KSPField]
        public float ThermalEnergy = 0f; //kJ

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Temperature", guiUnits = "K", groupName = "sysFireBoxinfo")]
        public float Temperature = 288f; //Kelvin

        private const float Stefan_Boltzmann_Constant = 5.67e-8f;

        private int LiquidFuelResourceID = -1;
        private int OxidizerResourceID = -1;

        public ModuleSystemHeat SystemHeatModule = null;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            LiquidFuelResourceID = PartResourceLibrary.Instance.GetDefinition("LiquidFuel").id;
            OxidizerResourceID = PartResourceLibrary.Instance.GetDefinition("Oxidizer").id;
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


        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (SystemHeatModule == null)
            {
                SystemHeatModule = this.part.FindModuleImplementing<ModuleSystemHeat>();
                if (SystemHeatModule != null)
                {
                    return;
                }
            }

            if (Fuel_Valve > 0f)
            {
                float targetUnitsPerSecond = InjectorRate * (Fuel_Valve / 100f);
                double unitsToPullThisFrame = targetUnitsPerSecond * TimeWarp.fixedDeltaTime;

                double LF_unitsActuallyDrained = this.part.RequestResource(LiquidFuelResourceID, unitsToPullThisFrame * LF_ratio);
                double OX_unitsActuallyDrained = this.part.RequestResource(OxidizerResourceID, unitsToPullThisFrame * OX_ratio);

                if (LF_unitsActuallyDrained == 0 ||
                   OX_unitsActuallyDrained == 0)
                {
                    this.part.RequestResource(LiquidFuelResourceID, -LF_unitsActuallyDrained);
                    this.part.RequestResource(OxidizerResourceID, -OX_unitsActuallyDrained);
                }
                else
                {
                    float Energy = LF_calorific_value * (float)LF_unitsActuallyDrained;
                    ThermalEnergy += Energy;
                    Power = Energy / TimeWarp.fixedDeltaTime;
                }
            }
            else Power = 0;

            float t0_Power = Stefan_Boltzmann_Constant * (Mathf.Pow(Temperature, 4) - Mathf.Pow(SystemHeatModule.currentLoopTemperature, 4)) * HeatingSurface;
            float delta_E = SteamLoop.delta_energy_exp_aproach(Temperature, HeatCapacity, SystemHeatModule.Loop, t0_Power, TimeWarp.fixedDeltaTime);

            SystemHeatModule.AddFlux(moduleID, Temperature, (delta_E / TimeWarp.fixedDeltaTime), false);
            ThermalEnergy -= delta_E;

            Temperature = 2970 * (1 - Mathf.Exp(-(ThermalEnergy / HeatCapacity) / 2970));
        }

        protected void Update()
        {

        }
    }
}
