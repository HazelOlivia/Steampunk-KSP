
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using SystemHeat;

namespace KerbalSteamProgram
{
    /// <summary>
    /// This class holds the configuration and simulation for a single Steam Loop
    /// </summary>
    public class SteamLoop
    {
        /// <summary>
        /// The loop ID
        /// </summary>
        public int ID { get; set; }
        /// <summary>
        /// The current loop temperature
        /// </summary>
        public float Temperature { get; set; }
        /// <summary>
        /// The loop nominal temperature
        /// </summary>
        public float NominalTemperature { get; set; }
        /// <summary>
        /// The loop's current net flux
        /// </summary>
        public float NetFlux { get; set; }
        /// <summary>
        /// The loop's total positive flux from all sources
        /// </summary>
        public float PositiveFlux { get; set; }
        /// <summary>
        /// The loop's total negative flux from all sources
        /// </summary>
        public float NegativeFlux { get; set; }
        /// <summary>
        /// The loop's current coolant volume
        /// </summary>
        public float Volume { get; set; }
        /// <summary>
        /// The current convective flux in W/m2
        /// </summary>
        public float ConvectionFlux { get; private set; }
        public float ConvectionTemperature { get; private set; }

        public List<ModuleSteamReservoar> LoopModules
        {
            get { return modules; }
        }

        protected List<ModuleSteamReservoar> modules;
        protected SystemSteamSimulator simulator;
        /// <summary>
        /// Build a new SteamLoop
        /// </summary>
        /// <param name="id">The loop ID number</param>
        public SteamLoop(int id)
        {
            ID = id;
            modules = new List<ModuleSteamReservoar>();
        }
        /// <summary>
        /// Build a new SteamLoop
        /// </summary>
        /// <param name="sim"></param>
        /// <param name="id">The loop ID number</param>
        public SteamLoop(SystemSteamSimulator sim, int id)
        {
            ID = id;
            modules = new List<ModuleSteamReservoar>();
            Temperature = GetEnvironmentTemperature();
            simulator = sim;
        }
        /// <summary>
        /// Build a new SteamLoop from a list of modules
        /// </summary>
        /// <param name="id">the loop ID</param>
        /// <param name="SteamModules">the modules to add</param>
        public SteamLoop(int id, List<ModuleSteamReservoar> SteamModules)
        {
            ID = id;
            modules = new List<ModuleSteamReservoar>();
            modules = SteamModules;
            //Temperature = SteamModules.Average(x => x.LoopTemperature);
            // Get loop properties set up
            Volume = CalculateLoopVolume();
        }

        const float Gass_Constant = 461.5f; // kJ/tonne*K   0.4615 kJ/kg*K

        /// <summary>
        /// Add a ModuleSteamReservoar to this loop. Adding means adding Volume and recalculating the nominal temperature
        /// </summary>
        /// <param name="SteamModule">the module to add</param>
        public void AddSteamModule(ModuleSteamReservoar SteamModule)
        {
            Volume += SteamModule.volume;
            modules.Add(SteamModule);
            modules.Sort(CompareModuleSteamReservoarPriority);
        }

        /// <summary>
        /// Remove a ModuleSteamReservoar to this loop. Removing means removing Volume and recalculating the nominal temperature
        /// </summary>
        /// <param name="SteamModule">the module to remove</param>
        public void RemoveSteamModule(ModuleSteamReservoar SteamModule)
        {
            Volume -= SteamModule.volume;
            modules.Remove(SteamModule);
        }

        /// <summary>
        /// Simulate this loop given a time warp level. First breaks the time step down if needed,
        /// then proceeds to iterate over all  time steps
        /// </summary>
        /// <param name="fixedDeltaTime">the current fixed delta time</param>
        public void Simulate(float fixedDeltaTime)
        {
            SimulateIteration(fixedDeltaTime);
        }

        void SimulateIteration(float simTimeStep)
        {
            float specificHeatCapacity = modules[0].part.Resources["Steam"].info.specificHeatCapacity; // kJ/tonne*K
            for (int i0 = 0; i0 < modules.Count - 1; i0++)
            {
                float Area_relative_0 = modules[i0].Steam_Valve * modules[i0].Max_Valve_Crosssection;
                if(Area_relative_0 != 0)
                {
                    for (int i1 = i0 + 1; i1 < modules.Count; i1++)
                    {
                        float Area_relative_1 = modules[i1].Steam_Valve * modules[i1].Max_Valve_Crosssection;
                        if (Area_relative_1 != 0)
                        {
                            float Area_relative = Mathf.Sqrt(Area_relative_0 * Area_relative_1);

                            float m0 = (float)modules[i0].part.Resources["Steam"].amount;
                            float v0 = modules[i0].volume;
                            float t0 = modules[i0].Temperature;
                            Vector3 pos0 = modules[i0].part.orgPos;

                            float m1 = (float)modules[i1].part.Resources["Steam"].amount;
                            float v1 = modules[i1].volume;
                            float t1 = modules[i1].Temperature;
                            Vector3 pos1 = modules[i1].part.orgPos;

                            float distance = (pos0 - pos1).magnitude;

                            float delta_P = modules[i0].Pressure - modules[i1].Pressure;
                            if (delta_P > 0) // flow i0 --> i1
                            {
                                float density = m0 / v0;
                                //float t0_mass_flow_rate = 0.6f * Area_relative * Mathf.Sqrt(2 * density * delta_P); //orifice flow equation (also known as the differential pressure flow equation)
                                float dynamic_viscocity = Sutherlands_Law_Steam(t0);
                                float t0_mass_flow_rate = SwameeJain(1, Area_relative, density, delta_P, distance, Mathf.Sqrt(Area_relative/Mathf.PI), dynamic_viscocity);
                                float delta_mass = delta_mass_exp_aproach(m0, v0, t0, m1, v1, t1, t0_mass_flow_rate, simTimeStep);
                                float delta_Energy = t0 * specificHeatCapacity * delta_mass * 0.001f; // kJ = K * kJ/tonne*K * kg * tonne/kg

                                modules[i1].AddFlux((delta_mass, delta_Energy)); // (kg, kJ)
                                modules[i0].AddFlux((-delta_mass, -delta_Energy));
                            }
                            else if (delta_P < 0) // flow i1 --> i0
                            {
                                float density = m1 / v1;
                                float dynamic_viscocity = Sutherlands_Law_Steam(t1);
                                float t0_mass_flow_rate = SwameeJain(1, Area_relative, density, delta_P, distance, Mathf.Sqrt(Area_relative / Mathf.PI), dynamic_viscocity);
                                float delta_mass = delta_mass_exp_aproach(m1, v1, t1, m0, v0, t0, t0_mass_flow_rate, simTimeStep);
                                float delta_Energy = t1 * specificHeatCapacity * delta_mass * 0.001f;

                                modules[i0].AddFlux((delta_mass, delta_Energy));
                                modules[i1].AddFlux((-delta_mass, -delta_Energy));
                            }
                        }
                    }
                }
            }
        }

        public static float Sutherlands_Law_Steam(float Temperature)
        {
            const float C = 673;
            return 0.0000112f * (373.15f + C) / (Temperature + C) * Mathf.Pow(Temperature / 373.15f, 1.5f);
        }
        public static float Sutherlands_Law_Air(float Temperature)
        {
            const float C = 120;
            return 0.00001827f * (291.15f + C) / (Temperature + C) * Mathf.Pow(Temperature / 291.15f, 1.5f);
        }

        public static float SwameeJain(float n, float A, float density, float delta_P, float L, float D, float mu)
        {
            float epsilon = 0.000045f;
            float abs_delta_P = Mathf.Abs(delta_P);
            float root_result = Mathf.Sqrt(density * D * abs_delta_P * 2 / L);
            if (root_result <= 1e-7f) return 0;
            float logterm = Mathf.Log10(epsilon / (3.7f * D) + (1.78f * mu) / (D * root_result));
            float mass_flow_rate = n * density * A * Mathf.Clamp(-2.22f * root_result * logterm, 0, 480) * Mathf.Sign(delta_P);
            if (float.IsNaN(mass_flow_rate)) return 0;
            return mass_flow_rate;
        }

        public static float delta_mass_exp_aproach(float m1, float v1, float t1, float m2, float v2, float t2, float t0_mass_flow_rate, float simTimeStep)
        {
            if (t0_mass_flow_rate == 0) return 0;
            float W1 = v1 / t1;
            float W2 = v2 / t2;
            float target_m1 = (m1 + m2) * (W1 / (W1 + W2));
            float max_delta_m = target_m1 - m1;

            if (Mathf.Abs(max_delta_m) < 1e-7f) return 0;

            float k = Mathf.Abs(t0_mass_flow_rate / max_delta_m);
            float delta_m = max_delta_m * (1 - Mathf.Exp(-k * simTimeStep));
            if (float.IsNaN(delta_m)) return 0;
            return -delta_m;
        }
        public static float delta_mass_exp_aproach(float m1, float v1, float t1, float t0_mass_flow_rate, float simTimeStep)
        {
            if (t0_mass_flow_rate == 0) return 0;
            float target_m1 = (100000f * v1) / (Gass_Constant * t1);
            float max_delta_m = target_m1 - m1;

            if (Mathf.Abs(max_delta_m) < 1e-7f) return 0;

            float k = Mathf.Abs(t0_mass_flow_rate / max_delta_m);
            float delta_m = max_delta_m * (1 - Mathf.Exp(-k * simTimeStep));
            if (float.IsNaN(delta_m)) return 0;
            return -delta_m;
        }

        public static float delta_energy_exp_aproach(float t1, float C1, HeatLoop heatLoop, float t0_Power, float simTimeStep)
        {
            if (t0_Power == 0) return 0;
            //C = HeatCapacity kJ/K
            //c = SpecificHeatCapacity kJ/(kg*K)
            //b = (beta) ReciprocalHeatCapacity K/kJ
            // System Heat is wrong about CoolantType.HeatCapacity it is actually SpecificHeatCapacity // HeatLoop.cs Line 169 proves it
            float C2 = (heatLoop.Volume * heatLoop.CoolantType.Density * heatLoop.CoolantType.HeatCapacity); // m^3 * (kg/m^3) *(kJ/(kg*K))
            float t2 = heatLoop.Temperature;

            float b1 = 1 / C1;
            float b2 = 1 / C2;

            float max_delta_E = (t1 - t2) / (b1 + b2);

            if (Mathf.Abs(max_delta_E) < 1e-7f) return 0;

            float k = Mathf.Abs(t0_Power / max_delta_E);
            float delta_m = max_delta_E * (1 - Mathf.Exp(-k * simTimeStep));
            if (float.IsNaN(delta_m)) return 0;
            return delta_m;
        }

        static int CompareModuleSteamReservoarPriority(ModuleSteamReservoar a, ModuleSteamReservoar b)
        {
            return b.priority - a.priority;
        }

        /// <summary>
        /// Gets the radiative environment temperature
        /// </summary>
        /// <returns></returns>
        protected float GetEnvironmentTemperature()
        {
            if (HighLogic.LoadedSceneIsEditor)
                return SystemSteamSettings.SpaceTemperature;

            if (modules.Count > 0 && modules[0] != null)
            {
                if (modules[0].part.vessel.mainBody.GetTemperature(modules[0].part.vessel.altitude) > 50000d)
                    return SystemSteamSettings.SpaceTemperature;

                return Mathf.Clamp((float)modules[0].part.vessel.mainBody.GetTemperature(modules[0].part.vessel.altitude), SystemSteamSettings.SpaceTemperature, 50000f);
            }
            return SystemSteamSettings.SpaceTemperature;
        }

        /// <summary>
        /// Gets the total volume of the loop based on its members
        /// </summary>
        protected float CalculateLoopVolume()
        {
            float total = 0f;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module.moduleUsed)
                    total += module.volume;
            }
            return total;
        }


        public int GetActiveModuleCount()
        {
            int count = 0;
            for (int i = 0; i < modules.Count; i++)
            {
                var module = modules[i];
                if (module.moduleUsed)
                {
                    count++;
                }
            }
            return count;
        }

    }
}
