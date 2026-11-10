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
    public class ModuleSafetyValve : PartModule
    {
        ModuleSteamReservoir steamReservoir = null;

        [KSPField(isPersistant = false)]
        public float Valve_Crosssection = 0.0246f; //meters^2
        [KSPField(isPersistant = false)]
        public float Length = 0.32f; //meters

        [KSPField(isPersistant = true, guiActive = true, guiName = " ")]
        public string Text = "\"ModuleSteamReservoir\" not Found";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Manual Safety", guiUnits = "%")]
        [UI_FloatRange(minValue = -100f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 0f;

        public bool valve_open = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Open")]
        public string valve_open_text = "";

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

            steamReservoir = this.part.parent.FindModuleImplementing<ModuleSteamReservoir>();
            if (steamReservoir != null)
            {
                steamReservoir.safetyValves.Add(this);
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
                //BaseField field = steamReservoir.Fields["Pressure"];
                //if(field.uiControlFlight is UI_FloatRange rangeControl)
                {
                    float delta_P = (steamReservoir.Pressure - (float)this.part.staticPressureAtm * 101.325f);
                    if (delta_P >= steamReservoir.MAWP)
                    {
                        valve_open = true;
                    }
                    else if (valve_open && delta_P <= steamReservoir.MAWP - 69f)
                    {
                        valve_open = false;
                    }
                    valve_open_text = valve_open.ToString();
                    if (valve_open || Steam_Valve != 0)
                    {
                        delta_P *= 1000; //kPa -> Pa
                        if (delta_P > 0)
                        {
                            PartResource resource = steamReservoir.part.Resources["Steam"];
                            float specificHeatCapacity = resource.info.specificHeatCapacity; // kJ/tonne*K

                            float m0 = (float)resource.amount + steamReservoir.Mass_Air; // kg
                            float v0 = steamReservoir.volume / 1000; // m^3
                            float t0 = steamReservoir.Temperature; // K
                            float density = m0 / v0; // kg/m^3
                            float area = 0;
                            float t0_mass_flow_rate = 0;
                            if (valve_open)
                            {
                                area = Valve_Crosssection * 2;
                                float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                                t0_mass_flow_rate = SteamLoop.SwameeJain(2, area, density, delta_P, Length, 2 * Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                            }
                            else if (Steam_Valve != 0)
                            {
                                area = Valve_Crosssection * Mathf.Abs(Steam_Valve / 100f);
                                float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                                t0_mass_flow_rate = SteamLoop.SwameeJain(1, area, density, delta_P, Length, 2 * Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                            }

                            float delta_mass = t0_mass_flow_rate * timeStep;
                            float steam_ratio = (float)resource.amount / m0;
                            float delta_mass_steam = delta_mass * steam_ratio;
                            float delta_mass_air = delta_mass * (1 - steam_ratio);
                            float delta_Energy_thermal = t0 * (specificHeatCapacity * delta_mass_steam + ModuleSteamReservoir.specificHeatCapacity_Air * delta_mass_air) * 0.001f; // kJ = K * sum(kJ/tonne*K * kg) * tonne/kg

                            steamReservoir.AddFlux((-delta_mass_steam, -delta_mass_air, -delta_Energy_thermal));

                            float exaust_v = t0_mass_flow_rate / (density * area); // m/s = kg/s / (kg/m^3 * m^2)
                            float force = t0_mass_flow_rate * exaust_v; // N
                            part.Rigidbody.AddForceAtPosition(-this.part.transform.up * force / 1000f, part.transform.position, ForceMode.Force);
                        }
                        else if (delta_P < 0)
                        {
                            float t1 = (float)part.vessel.atmosphericTemperature;
                            if (float.IsNaN(t1) || t1 < 3f) t1 = 3f;
                            float density = (float)(this.part.staticPressureAtm * 101325f) / (ModuleSteamReservoir.Gas_Constant_Air * t1); // kg/m^3  // Pa = Atm * 101325
                            float area = Valve_Crosssection * Mathf.Abs(Steam_Valve / 100f);
                            float dynamic_viscocity = SteamLoop.Sutherlands_Law_Air(t1);
                            float t0_mass_flow_rate = SteamLoop.SwameeJain(1, area, density, delta_P, Length, 2 * Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);

                            t0_mass_flow_rate /= 100; //correction for an unknown unit mismatch

                            float delta_mass = t0_mass_flow_rate * timeStep;
                            float delta_Energy = t1 * ModuleSteamReservoir.specificHeatCapacity_Air * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg
                            steamReservoir.AddFlux((0, delta_mass, delta_Energy));
                        }
                    }
                }
            }
        }

        protected void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                steamReservoir = this.part.parent.FindModuleImplementing<ModuleSteamReservoir>();
                if (steamReservoir != null)
                {
                    steamReservoir.safetyValves.Add(this);
                }
            }
            //GameSettings.
            if (Input.GetKeyUp(KeyCode.Mouse0))
            {
                Steam_Valve = 0f;
            }
        }
    }
}
