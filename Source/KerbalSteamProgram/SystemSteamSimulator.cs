using System;
using System.Collections.Generic;
using System.Linq;
using KerbalSteamProgram.UI;

namespace KerbalSteamProgram
{
    public class SystemSteamSimulator
    {
        public List<SteamLoop> SteamLoops { get; private set; }

        public float SimulationAltitude { get; set; }

        public float SimulationSpeed { get; set; }

        public CelestialBody SimulationBody { get; set; }

        public float TotalVolume
        {
            get
            {
                float total = 0;
                foreach (SteamLoop loop in SteamLoops)
                {

                    total += loop.Volume;
                }
                return total;
            }
        }

        public SystemSteamSimulator()
        {
            //atmoSim = new SystemSteamAtmosphereSimulator();
            SimulationAltitude = 0f;
            SimulationSpeed = 0f;
        }
        /// TODO: This should update the simulator in place instead of reset completely
        public void Refresh(List<Part> parts)
        {
            Reset(parts);
        }
        public void Reset(List<Part> parts)
        {
            SteamLoops = new List<SteamLoop>();
            List<ModuleSteamReservoar> SteamModules = new List<ModuleSteamReservoar>();

            for (int i = parts.Count - 1; i >= 0; --i)
            {
                Part part = parts[i];
                SteamModules.AddRange(part.GetComponents<ModuleSteamReservoar>().ToList());
            }

            Utils.Log(String.Format("[SystemSteamSimulator]: Building Steam loops from {0} ModuleSteamReservoar modules", SteamModules.Count.ToString()), LogType.Simulator);


            foreach (ModuleSteamReservoar SteamModule in SteamModules)
            {
                AddSteamModule(SteamModule);
            }
        }

        /// <summary>
        /// Do simulation in flight
        /// </summary>
        public virtual void Simulate()
        {
            if (SteamLoops != null)
            {
                foreach (SteamLoop loop in SteamLoops)
                {
                    loop.Simulate(TimeWarp.fixedDeltaTime);
                }
            }
        }

        /// <summary>
        /// Do simulation in the editor
        /// </summary>
        public virtual void SimulateEditor()
        {

            if (SteamLoops != null)
            {
                if (SimulationBody != null)
                {
                    //atmoSim.SimulateAtmosphere(SimulationBody, SimulationSpeed, SimulationAltitude);
                }
                foreach (SteamLoop loop in SteamLoops)
                {
                    loop.Simulate(SystemSteamSettings.SimulationRateEditor);
                }
            }
        }

        /// <summary>
        /// Add a Steam module to the simulation
        /// </summary>
        /// <param name="module">the module to add</param>
        public void AddSteamModule(ModuleSteamReservoar module)
        {
            if (module.moduleUsed)
                AddSteamModuleToLoop(module.currentLoopID, module);
        }

        /// <summary>
        /// Add a Steam module to a specific SteamLoop
        /// </summary>
        /// <param name="loopID">the loop to add to</param>
        /// <param name="module">the module to add</param>
        public void AddSteamModuleToLoop(int loopID, ModuleSteamReservoar module)
        {
            if (module.moduleUsed)
            {
                // Build a new Steam loop as needed
                if (!HasLoop(loopID))
                {
                    if (SteamLoops != null)
                        SteamLoops.Add(new SteamLoop(this, loopID));
                    Utils.Log(String.Format("[SystemSteamSimulator]: Created new Steam Loop {0}", loopID), LogType.Simulator);
                }
                if (SteamLoops != null)
                {
                    foreach (SteamLoop loop in SteamLoops)
                    {
                        if (loop.ID == loopID)
                            loop.AddSteamModule(module);
                    }
                }

                Utils.Log(String.Format("[SystemSteamSimulator]: Added module {0} to Steam Loop {1}", module.moduleID, loopID), LogType.Simulator);
            }
        }

        /// <summary>
        /// Remove a part with all its Steam modules from the system
        /// </summary>
        /// <param name="part">the part to remove</param>
        public void RemovePart(Part part)
        {
            ModuleSteamReservoar[] SteamModules = part.GetComponents<ModuleSteamReservoar>();
            foreach (ModuleSteamReservoar module in SteamModules)
            {
                RemoveSteamModule(module);
            }
        }

        /// <summary>
        /// Remove a Steam module from the system
        /// </summary>
        /// <param name="module">the module to remove</param>
        public void RemoveSteamModule(ModuleSteamReservoar module)
        {
            RemoveSteamModuleFromLoop(module.currentLoopID, module);
        }

        /// <summary>
        /// Remove a Steam module from a specific SteamLoop
        /// </summary>
        /// <param name="loopID">the loop to remove from</param>
        /// <param name="module">the module to remove</param>
        public void RemoveSteamModuleFromLoop(int loopID, ModuleSteamReservoar module)
        {
            if (HasLoop(loopID))
            {
                Loop(loopID).RemoveSteamModule(module);

                Utils.Log(String.Format("[SystemSteamSimulator]: Removed module {0} from Steam Loop {1}", module.moduleID, loopID), LogType.Simulator);

                if (Loop(loopID).LoopModules.Count == 0)
                {
                    SteamLoops.Remove(Loop(loopID));

                    Utils.Log(String.Format("[SystemSteamSimulator]: Steam Loop {0} has no more members, removing", loopID), LogType.Simulator);
                }
            }
        }
        public bool HasLoop(int id)
        {

            if (SteamLoops != null)
            {
                foreach (SteamLoop loop in SteamLoops)
                {
                    if (loop.ID == id)
                        return true;
                }
            }
            return false;
        }
        public void ChangeLoopID(int oldID, int newID)
        {
            Loop(oldID).ID = newID;
        }
        public SteamLoop Loop(int id)
        {
            if (SteamLoops != null)
            {
                foreach (SteamLoop loop in SteamLoops)
                {
                    if (loop.ID == id)
                        return loop;
                }
            }
            return null;
        }
        public void ResetTemperatures()
        {
            if (SteamLoops != null)
            {
                foreach (SteamLoop loop in SteamLoops)
                {
                    //loop.ResetTemperatures();
                }
            }
        }
    }
}
