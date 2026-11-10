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
    class ModuleSteamRocketEngine : ModuleSteamReservoir
    {
        [KSPField(isPersistant = false)]
        public float throat_area = 0.0246f; //meters^2
        [KSPField(isPersistant = false)]
        public float exhaust_area = 0.246f; //meters^2
        //[KSPField(isPersistant = false)]
        //public float Me = 3.45f;
        [KSPField(isPersistant = false)]
        public float thrust_at_MAWP_ATM = 110000;
        [KSPField(isPersistant = false)]
        public float ISP_at_MAWP_ATM = 77;
        [KSPField(isPersistant = false)]
        public float thrust_at_MAWP_VAC = 218571;
        [KSPField(isPersistant = false)]
        public float ISP_at_MAWP_VAC = 153;
        [KSPField(isPersistant = false)]
        public float timestep_count = 4;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Thrust", guiUnits = "kN", guiFormat = "F3", groupName = "sysSteamRocketEngineinfo", groupDisplayName = "SteamRocketEngine")]
        public float display_Force = 0;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "ISP", guiUnits = "s", guiFormat = "F0", groupName = "sysSteamRocketEngineinfo")]
        public float ISP = 0;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Mass Flow Physics", guiUnits = "kg/s", guiFormat = "F3", groupName = "sysSteamRocketEngineinfo")]
        public float display_m_flow = 0;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Mass Flow Intended", guiUnits = "kg/s", guiFormat = "F3", groupName = "sysSteamRocketEngineinfo")]
        public float display_m_flow_2 = 0;
        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Pressure", guiUnits = "kPa", guiFormat = "F3", groupName = "sysSteaminfo")]
        [UI_ProgressBar(minValue = 0f, maxValue = 1500f)]
        public float Pressure_t0 = 101f; //kPa

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);
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

        public override void SimulateIteration2(float timeStep_external, int numSteps_external)
        {
            Pressure_t0 = base.Pressure;
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (base.initialized)
            {
                if (FlightGlobals.ActiveVessel == part.vessel)
                {
                    base.Steam_Valve = FlightGlobals.ActiveVessel.ctrlState.mainThrottle * 100f;
                }

                TEST = "";
                display_Force = 0;
                ISP = 0;
                display_m_flow = 0;

                float atmos_pressure = (float)this.part.staticPressureAtm * 101325f; //Atm to Pa
                float Chamber_pressure = base.Pressure * 1000f; //kPa to Pa
                float Chamber_over_pressure = Chamber_pressure - atmos_pressure;

                PartResource internalSteam = this.part.Resources["Steam"];
                float m0 = (float)(internalSteam.amount + base.Mass_Air);
                float v0 = base.volume * 1000; //liter to m^3
                float t0 = base.Temperature;

                float t1 = (float)part.vessel.atmosphericTemperature;
                if (float.IsNaN(t1) || t1 < 3f) t1 = 3f;

                if (Chamber_over_pressure > 0)
                {
                    float thrust_at_MAWP = Mathf.Max(0,(float)(this.part.staticPressureAtm * thrust_at_MAWP_ATM + (1 - this.part.staticPressureAtm) * thrust_at_MAWP_VAC));
                    float ISP_at_MAWP = Mathf.Max(1,(float)(this.part.staticPressureAtm * ISP_at_MAWP_ATM + (1 - this.part.staticPressureAtm) * ISP_at_MAWP_VAC));

                    float steam_ratio = (float)internalSteam.amount / m0;
                    float Gass_Constant = ModuleSteamReservoir.Gas_Constant_Steam * steam_ratio + ModuleSteamReservoir.Gas_Constant_Air * (1 - steam_ratio);

                    float t, force, isp, mass_flow_rate;
                    float force_t0, isp_t0;
                    void compute()
                    {
                        atmos_pressure = (float)this.part.staticPressureAtm * 101325f; //Atm to Pa
                        Chamber_pressure = base.Pressure * 1000f; //kPa to Pa
                        Chamber_over_pressure = Chamber_pressure - atmos_pressure;
                        if(Chamber_over_pressure == 0)
                        {
                            t = 0; force = 0; isp = 0; mass_flow_rate = 0;
                        }
                        else
                        {
                            t = Mathf.Max(0, Chamber_over_pressure / (MAWP * 1000f));
                            if (float.IsNaN(t)) t = 0;
                            force = thrust_at_MAWP * t;
                            isp = ISP_at_MAWP * Mathf.Sqrt(t);
                            //t0_mass_flow_rate = force / (ISP * 9.81f);
                            mass_flow_rate = thrust_at_MAWP / (ISP_at_MAWP * 9.81f) * Mathf.Sqrt(t); // is always positive
                        }
                        //TEST += t + " / ";
                    }
                    void do_flux(float flux_timeStep)
                    {
                        float delta_mass = mass_flow_rate * flux_timeStep;

                        float delta_mass_steam = delta_mass * steam_ratio;
                        float delta_mass_air = delta_mass * (1 - steam_ratio);
                        float delta_Energy_thermal = t0 * (internalSteam.info.specificHeatCapacity * delta_mass_steam + ModuleSteamReservoir.specificHeatCapacity_Air * delta_mass_air) * 0.001f; // kJ = K * sum(kJ/tonne*K * kg) * tonne/kg

                        base.AddFlux((-delta_mass_steam, -delta_mass_air, -delta_Energy_thermal));
                    }
                    float acumilative_force = 0, acumilative_isp = 0, acumilative_mass_flow_rate = 0;
                    void acumilate()
                    {
                        if (!float.IsNaN(force)) acumilative_force += force;
                        if (!float.IsNaN(isp)) acumilative_isp += isp;
                        if (!float.IsNaN(mass_flow_rate)) acumilative_mass_flow_rate += mass_flow_rate;
                    }

                    compute();
                    force_t0 = force;
                    isp_t0 = isp;
                    if (t == 0) return;
                    float PA_per_kg = Chamber_pressure / m0;
                    float delta_mass_to_equilibrium = Mathf.Abs(atmos_pressure / PA_per_kg - m0); // Pa / Pa/kg
                    if (delta_mass_to_equilibrium == 0) return;
                    float delta_time_to_equilibrium = delta_mass_to_equilibrium / mass_flow_rate; //s = kg / kg/s
                    if (delta_time_to_equilibrium == 0) return;

                    TEST += $"{timeStep_external} * {timestep_count} > {delta_time_to_equilibrium}";
                    if (timeStep_external * timestep_count > delta_time_to_equilibrium)
                    {
                        int numSteps_internal = Mathf.Min(128, Mathf.CeilToInt(timestep_count * timeStep_external / delta_time_to_equilibrium));
                        float timeStep_internal = timeStep_external / numSteps_internal;

                        for (int i = 0; i < numSteps_internal; i++)
                        {
                            compute();
                            acumilate();
                            do_flux(timeStep_internal);
                            base.SimulateIteration1(timeStep_internal);
                        }
                        part.Rigidbody.AddForceAtPosition(this.part.transform.up * (force_t0 / 1000f / numSteps_external), part.transform.position, ForceMode.Force);

                        display_Force = force_t0 / 1000f;
                        ISP = isp_t0;

                        float mass_flow_rate_avg = acumilative_mass_flow_rate / numSteps_internal;
                        display_m_flow = mass_flow_rate_avg;

                        float mass_flow_rate_t0 = force_t0 / (isp_t0 * 9.81f);
                        display_m_flow_2 = mass_flow_rate_t0;

                        float delta_mass_debt = (mass_flow_rate_t0 - mass_flow_rate_avg) / numSteps_external;
                        base.Mass_Debt += delta_mass_debt;
                    }
                    else
                    {
                        part.Rigidbody.AddForceAtPosition(this.part.transform.up * force / 1000f / numSteps_external, part.transform.position, ForceMode.Force);
                        acumilate();
                        do_flux(timeStep_external);
                        base.SimulateIteration1(timeStep_external);

                        display_Force = force / 1000f;
                        ISP = isp;
                        display_m_flow = mass_flow_rate;
                    }
                }
                else
                {
                    float density = atmos_pressure / (ModuleSteamReservoir.Gas_Constant_Air * t1); // kg/m^3
                    float t0_mass_flow_rate = SteamLoop.Venturi_orifice_flow(throat_area, density, -Chamber_over_pressure);
                    t0_mass_flow_rate /= 100; //correction for an unknown unit mismatch

                    float delta_mass = Mathf.Abs(SteamLoop.delta_mass_exp_aproach(m0, v0, t0, atmos_pressure, t0_mass_flow_rate, timeStep_external, ModuleSteamReservoir.Gas_Constant_Air));

                    float delta_Energy = t1 * ModuleSteamReservoir.specificHeatCapacity_Air * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg
                    base.AddFlux((0, delta_mass, delta_Energy));
                }
            }
        }

        protected override void Update()
        {
            base.Update();

            BaseField field = base.Fields["Steam_Valve"];

            if (field != null)
            {
                field.guiActive = false;
                field.guiActiveEditor = false;
            }
        }
    }
}

//float density = m0 / v0;

//float t0_mass_flow_rate = SteamLoop.Venturi_orifice_flow(area, density, Chamber_over_pressure);
//t0_mass_flow_rate /= 10f; //correction for some oversight of mine idk
//float throat_v = t0_mass_flow_rate / (density * throat_area); // m/s = kg/s / (kg/m^3 * m^2)

//////https://www.grc.nasa.gov/www/k-12/airplane/rktthsum.html

//float specific_heatcapacity_ratio = 1.33f;
////float term1 = (specific_heatcapacity_ratio + 1) / 2;
////float term2 = (specific_heatcapacity_ratio - 1) / 2;
////float term3 = (specific_heatcapacity_ratio + 1) / (2 * (specific_heatcapacity_ratio - 1));
////float term4 = specific_heatcapacity_ratio / (specific_heatcapacity_ratio - 1);
////float term5 = 1 + term2 * Me * Me;

////float exhaust_Temperature = steamReservoir.Temperature / term5;
////float pressure_ratio = Mathf.Pow(term5, -term4);

////float exhaust_velocity = Me * Mathf.Sqrt(specific_heatcapacity_ratio * ModuleSteamReservoir.Gas_Constant_Steam * exhaust_Temperature);

////float k = (((throat_area / Mathf.Sqrt(steamReservoir.Temperature)) * Mathf.Sqrt(specific_heatcapacity_ratio / ModuleSteamReservoir.Gas_Constant_Steam) * Mathf.Pow(term1, -term3)) * exhaust_velocity + pressure_ratio * exhaust_area);

////float PF0 = (atmos_pressure * exhaust_area) / k;
////float force = (k * Mathf.Sqrt(Chamber_over_pressure * Chamber_over_pressure + PF0 * PF0) - PF0 * k);

////ideal gas aproximation
//float term4inv = (specific_heatcapacity_ratio - 1) / specific_heatcapacity_ratio;

//float exaust_v = Mathf.Sqrt(throat_v * throat_v + 2 * term4inv * Chamber_pressure / density * (1 - Mathf.Pow(atmos_pressure / Chamber_pressure, term4inv)));
//exaust_v /= 10f; //correction for another/or the same oversight of mine, sorry
//float force = t0_mass_flow_rate * exaust_v; // N