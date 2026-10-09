using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KerbalSteamProgram
{
    class Dynamo : PartModule
    {
        [KSPField]
        public float Energy_per_Charge = 1.0f; //kJ/Unit
        [KSPField]
        public float Efficiency = 0.2f * 0.95f;
        [KSPField(isPersistant = false)]
        public float Length = 1.72f; //meters

        ModuleSteamReservoar steamReservoar = null;

        [KSPField(isPersistant = false)]
        public float Valve_Crosssection = 0.0123f; //meters^2

        [KSPField(isPersistant = true, guiActive = true, guiName = " ")]
        public string Text = "\"ModuleSteamReservoar\" not Found";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Throttle", guiUnits = "%")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 0f;


        [KSPField(isPersistant = true, guiActive = true, guiName = "Power")]
        public string Power = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            steamReservoar = this.part.parent.FindModuleImplementing<ModuleSteamReservoar>();
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

            BaseField fieldText = Fields["Text"];
            BaseField fieldHolding = Fields["Steam_Valve"];
            if (steamReservoar == null)
            {
                fieldText.guiActive = true;
                fieldText.guiActiveEditor = true;
                fieldHolding.guiActive = false;
                fieldHolding.guiActiveEditor = false;
            }
            else
            {
                fieldHolding.guiActive = true;
                fieldHolding.guiActiveEditor = true;
                fieldText.guiActive = false;
                fieldText.guiActiveEditor = false;
            }
        }

        public void FixedUpdate()
        {
            if (steamReservoar != null)
            {
                if (Steam_Valve != 0)
                {
                    float delta_P = (steamReservoar.Pressure - (float)this.part.staticPressureAtm * 101.325f) * 1000; //Pa
                    if (delta_P > 0)
                    {
                        PartResource resource = steamReservoar.part.Resources["Steam"];
                        float specificHeatCapacity = resource.info.specificHeatCapacity; // kJ/tonne*K
                        float m0 = (float)resource.amount; // kg
                        float v0 = steamReservoar.volume / 1000; // m^3
                        float t0 = steamReservoar.Temperature; // K
                        float density = m0 / v0; // kg/m^3
                        float area = Valve_Crosssection * Mathf.Abs(Steam_Valve / 100f); //m^2

                        //float t0_mass_flow_rate = 0.6f * area * Mathf.Sqrt(2 * density * delta_P); //orifice flow equation (also known as the differential pressure flow equation)

                        float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                        float t0_mass_flow_rate = SteamLoop.SwameeJain(2, area, density, delta_P, Length, Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                        t0_mass_flow_rate /= 100; //correction  fro a unknown unit mismatch
                        float delta_mass = t0_mass_flow_rate * TimeWarp.deltaTime; //kg
                        float delta_Energy_thermal = t0 * specificHeatCapacity * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg
                        steamReservoar.AddFlux((-delta_mass, -delta_Energy_thermal));

                        float delta_Energy_turbine = delta_mass / density * delta_P * Efficiency / 1000; // kg * m^3/kg * Pa * 1
                        float delta_Charge = delta_Energy_turbine / Energy_per_Charge;
                        float actual_delta_Charge = (float)this.part.RequestResource("ElectricCharge", -(double)delta_Charge);
                        Power = $"{delta_Energy_turbine / TimeWarp.deltaTime:F2} kW / {(-actual_delta_Charge / TimeWarp.deltaTime):F2} EC/s";
                    }
                }
            }
        }

        protected void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                steamReservoar = this.part.parent.FindModuleImplementing<ModuleSteamReservoar>();
            }
        }
    }
}
