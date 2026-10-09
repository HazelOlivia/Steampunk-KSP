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
    class SafetyValve : PartModule
    {
        ModuleSteamReservoar steamReservoar = null;

        [KSPField(isPersistant = false)]
        public float Valve_Crosssection = 0.0246f; //meters^2
        [KSPField(isPersistant = false)]
        public float Length = 0.32f; //meters

        [KSPField(isPersistant = true, guiActive = true, guiName = " ")]
        public string Text = "\"ModuleSteamReservoar\" not Found";

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
                //BaseField field = steamReservoar.Fields["Pressure"];
                //if(field.uiControlFlight is UI_FloatRange rangeControl)
                {
                    if (steamReservoar.Pressure - (float)this.part.staticPressureAtm * 101.325f >= steamReservoar.MAWP)
                    {
                        valve_open = true;
                    }
                    else if (steamReservoar.Pressure <= steamReservoar.MAWP - 69f)
                    {
                        valve_open = false;
                    }
                    valve_open_text = valve_open.ToString();
                    if (valve_open || Steam_Valve != 0)
                    {
                        float delta_P = (steamReservoar.Pressure - (float)this.part.staticPressureAtm * 101.325f) * 1000;
                        if (delta_P > 0)
                        { 
                            PartResource resource = steamReservoar.part.Resources["Steam"];
                            float specificHeatCapacity = resource.info.specificHeatCapacity; // kJ/tonne*K
                            float m0 = (float)resource.amount; // kg
                            float v0 = steamReservoar.volume / 1000; // m^3
                            float t0 = steamReservoar.Temperature; // K
                            float density = m0 / v0; // kg/m^3
                            float area = 0; // m^2
                            float t0_mass_flow_rate = 0;
                            if (valve_open)
                            {
                                area = Valve_Crosssection * 2;
                                float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                                t0_mass_flow_rate = SteamLoop.SwameeJain(2, area, density, delta_P, Length, Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                            }
                            else if (Steam_Valve != 0)
                            {
                                area = Valve_Crosssection * Mathf.Abs(Steam_Valve / 100f);
                                float dynamic_viscocity = SteamLoop.Sutherlands_Law_Steam(t0);
                                t0_mass_flow_rate = SteamLoop.SwameeJain(1, area, density, delta_P, Length, Mathf.Sqrt(area / Mathf.PI), dynamic_viscocity);
                            }
                            //float t0_mass_flow_rate = 0.6f * area * Mathf.Sqrt(2 * density * delta_P); //orifice flow equation (also known as the differential pressure flow equation)

                            t0_mass_flow_rate /= 100; //correction  fro a unknown unit mismatch

                            float delta_mass = t0_mass_flow_rate * TimeWarp.deltaTime;
                            float delta_Energy = t0 * specificHeatCapacity * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg
                            steamReservoar.AddFlux((-delta_mass, -delta_Energy));
                        }
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
            //GameSettings.
            if (Input.GetKeyUp(KeyCode.Mouse0))
            {
                Steam_Valve = 0f;
            }
        }
    }
}
