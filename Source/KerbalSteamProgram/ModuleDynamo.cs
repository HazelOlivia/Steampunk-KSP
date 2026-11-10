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
    public class ModuleDynamo : PartModule
    {
        [KSPField]
        public float Energy_per_Charge = 1.0f; //kJ/Unit
        [KSPField]
        public float Efficiency = 0.2f * 0.95f;
        [KSPField(isPersistant = false)]
        public float Length = 1.72f; //meters

        ModuleSteamReservoir steamReservoir = null;

        [KSPField(isPersistant = false)]
        public float Valve_Crosssection = 0.0123f; //meters^2

        [KSPField(isPersistant = true, guiActive = true, guiName = " ")]
        public string Text = "\"ModuleSteamReservoir\" not Found";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Throttle", guiUnits = "%")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 0f;


        [KSPField(isPersistant = true, guiActive = true, guiName = "Power")]
        public string Power = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            steamReservoir = this.part.parent.FindModuleImplementing<ModuleSteamReservoir>();
            if (steamReservoir != null)
            {
                steamReservoir.dynamos.Add(this);
            }
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
            if (steamReservoir == null)
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

        public void SimulateIteration(float timeStep)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (steamReservoir != null)
            {
                if (Steam_Valve != 0)
                {
                    float delta_P = (steamReservoir.Pressure - (float)this.part.staticPressureAtm * 101.325f) * 1000; //Pa

                    PartResource resource = steamReservoir.part.Resources["Steam"];
                    float specificHeatCapacity = resource.info.specificHeatCapacity; // kJ/tonne*K
                    float area = Valve_Crosssection * Mathf.Abs(Steam_Valve / 100f); //m^2

                    if (delta_P > 0)// flow Reserv --> Atmos
                    {
                        float m0 = (float)resource.amount + steamReservoir.Mass_Air; // kg
                        float v0 = steamReservoir.volume / 1000; // m^3
                        float t0 = steamReservoir.Temperature; // K
                        float density = m0 / v0; // kg/m^3

                        float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                        float t0_mass_flow_rate = SteamLoop.SwameeJain(2, area, density, delta_P, Length, 2 * Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                        float delta_mass = t0_mass_flow_rate * timeStep; //kg
                        float steam_ratio = (float)resource.amount / m0;
                        float delta_mass_steam = delta_mass * steam_ratio;
                        float delta_mass_air = delta_mass * (1 - steam_ratio);
                        float delta_Energy_thermal = t0 * (specificHeatCapacity * delta_mass_steam + ModuleSteamReservoir.specificHeatCapacity_Air * delta_mass_air) * 0.001f; // kJ = K * sum(kJ/tonne*K * kg) * tonne/kg
                        steamReservoir.AddFlux((-delta_mass_steam, -delta_mass_air, -delta_Energy_thermal));

                        float delta_Energy_turbine = delta_mass / density * delta_P * Efficiency / 1000; // kg * m^3/kg * Pa * 1
                        float delta_Charge = delta_Energy_turbine / Energy_per_Charge;
                        float actual_delta_Charge = (float)this.part.RequestResource("ElectricCharge", -(double)delta_Charge);
                        Power = $"{delta_Energy_turbine / timeStep:F2} kW / {(-actual_delta_Charge / timeStep):F2} EC/s";
                    }
                    if (delta_P < 0)// flow Atmos --> Reserv
                    {
                        float t1 = (float)part.vessel.atmosphericTemperature;
                        if (float.IsNaN(t1) || t1 < 3f) t1 = 3f;
                        float density = (float)(this.part.staticPressureAtm * 101325f) / (ModuleSteamReservoir.Gas_Constant_Air * t1); // kg/m^3  // Pa = Atm * 101325

                        float dynamic_viscocity = SteamLoop.Sutherlands_Law_Air(t1);
                        float t0_mass_flow_rate = SteamLoop.SwameeJain(2, area, density, delta_P, Length, 2 * Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                        t0_mass_flow_rate /= 100; //correction  fro a unknown unit mismatch
                        float delta_mass = t0_mass_flow_rate * timeStep; //kg
                        float delta_Energy_thermal = t1 * specificHeatCapacity * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg
                        steamReservoir.AddFlux((0, delta_mass, delta_Energy_thermal));
                        Power = $"{0} kW / {0} EC/s";
                    }
                }
                else
                {
                    Power = $"{0} kW / {0} EC/s";
                }
            }
        }

        protected void Update()
        {
            if (HighLogic.LoadedSceneIsEditor && steamReservoir == null)
            {
                steamReservoir = this.part.parent.FindModuleImplementing<ModuleSteamReservoir>();
                if(steamReservoir != null)
                {
                    steamReservoir.dynamos.Add(this);
                }
            }
        }
    }
}
