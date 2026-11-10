using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalSteamProgram
{
    public class ModuleSteamReservoir : PartModule
    {
        [KSPField]
        public float MAWP = 2000; //kPa

        // Unique name of the module on a part
        [KSPField(isPersistant = false)]
        public string moduleID = "SteamModule";

        // Name of the icon to use
        [KSPField(isPersistant = false)]
        public string iconName = "Icon_Gears";

        // Volume of coolant provided by this system in Liters
        [KSPField(isPersistant = false)]
        public float volume = 10f; //Liters

        [KSPField(isPersistant = false)]
        public float Max_Valve_Crosssection = 0.01f; //meters^2

        [KSPField(isPersistant = false)]
        public bool ignoreTemperature = false;

        // 
        [KSPField(isPersistant = false)]
        public int priority = 1;

        // Whether this module should be used by the Steam system at all
        public bool moduleUsed = true;

        //------------------------------------

        public bool initialized = false;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steam Valve", groupName = "sysSteaminfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemSteam_GroupName")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_SystemSteam_ModuleSystemSteam_Field_LoopID", groupName = "sysSteaminfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemSteam_GroupName")]
        [UI_ChooseOption(affectSymCounterparts = UI_Scene.Editor, options = new[] { "None" }, scene = UI_Scene.All, suppressEditorShipModified = false)]
        public int currentLoopID = 0;

        [KSPField]
        public float ThermalEnergy = 0; //kJ

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Pressure", guiUnits = "kPa", guiFormat = "F3", groupName = "sysSteaminfo")]
        [UI_ProgressBar(minValue = 0f, maxValue = 1500f)]
        public float Pressure = 101f; //kPa

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Partial Pressure", groupName = "sysSteaminfo")]
        public string Pressure_Display = "";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, groupName = "sysSteaminfo")]
        public string TEST = "";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Temperature", guiUnits = "K", guiFormat = "F0", groupName = "sysSteaminfo")]
        public float Temperature = 288f; //Kelvin

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Mass Steam", guiUnits = "kg", guiFormat = "F3", groupName = "sysSteaminfo")]
        public double Mass_steam_Display = 0;

        //[KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false)]
        public float Mass_Air = 0;

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, groupName = "sysSteaminfo")]
        public float Mass_Debt = 0;

        public const float Gas_Constant_Steam = 461.5f; // kJ/tonne*K or J/kg*K
        public const float Gas_Constant_Air = 287.05f; // kJ/tonne*K or J/kg*K
        public const float specificHeatCapacity_Air = 718f; // kJ/tonne*K or J/kg*K

        public (float Units_Steam, float Units_Air, float Jouls) Flux = (0, 0, 0);

        public SteamLoop Loop => simulator?.Loop(currentLoopID);

        public List<ModuleSafetyValve> safetyValves;
        public List<ModuleDynamo> dynamos;

        public int LoopID
        {
            get { return currentLoopID; }
            set { currentLoopID = value; }
        }

        protected SystemSteamSimulator simulator;
        protected UIPartActionWindow cachedPaw = null;

        public void Start()
        {
            SetupUI();

            Fields["totalSystemTemperature"].guiActive = SystemSteamSettings.DebugPartUI;
            Fields["totalSystemTemperature"].guiActiveEditor = SystemSteamSettings.DebugPartUI;
            Fields["totalSystemFlux"].guiActive = SystemSteamSettings.DebugPartUI;
            Fields["totalSystemFlux"].guiActiveEditor = SystemSteamSettings.DebugPartUI;
            Fields["nominalLoopTemperature"].guiActive = SystemSteamSettings.DebugPartUI;
            Fields["nominalLoopTemperature"].guiActiveEditor = SystemSteamSettings.DebugPartUI;
            Fields["currentLoopTemperature"].guiActive = SystemSteamSettings.DebugPartUI;
            Fields["currentLoopTemperature"].guiActiveEditor = SystemSteamSettings.DebugPartUI;
            Fields["currentLoopFlux"].guiActive = SystemSteamSettings.DebugPartUI;
            Fields["currentLoopFlux"].guiActiveEditor = SystemSteamSettings.DebugPartUI;

            Utils.Log("[ModuleSystemSteam]: Setup complete", LogType.Modules);
        }
        void SetupUI()
        {
            BaseField chooseField = Fields["currentLoopID"];
            UI_ChooseOption chooseOption = HighLogic.LoadedSceneIsFlight ? chooseField.uiControlFlight as UI_ChooseOption : chooseField.uiControlEditor as UI_ChooseOption;
            chooseOption.options = new string[] {"0", "1", "2", "3", "4", "5", "6", "7", "8", "9"};
            chooseOption.onFieldChanged = ChangeLoop;
        }
        private void ChangeLoop(BaseField field, object oldFieldValueObj)
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            var oldLoopID = (int)oldFieldValueObj;
            if (Utils.IsLogEnabled(LogType.Modules))
                Utils.Log($"[ModuleSystemSteam] Changing part from loop {oldLoopID} to loop {currentLoopID}", LogType.Modules);
            simulator.RemoveSteamModuleFromLoop(oldLoopID, this);
            simulator.AddSteamModuleToLoop(currentLoopID, this);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            //GameEvents.onPartActionUICreate.Add(OnPartActionUICreate);

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
                        //TEST += (i).ToString();
                        break;
                    }

                    yield return null; // Wait for the next frame
                }
            }
            InitializeModule();
            initialized = true;
        }
        private void InitializeModule()
        {
            PartResource internalSteam = this.part.Resources["Steam"];
            PartResourceDefinition steamDef = internalSteam.info;
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature = 288f;
            //TEST += ((float)part.staticPressureAtm * 101.325f).ToString();
            Mass_Air = ((float)part.staticPressureAtm * 101.325f) * volume / (Gas_Constant_Air * Temperature);
            float massAirTonnes = Mass_Air * 0.001f;

            ThermalEnergy = Temperature * (specificHeatCapacity_Air * massAirTonnes + steamDef.specificHeatCapacity * massSteamTonnes);
        }

        public void AddFlux((float Units_Steam, float Units_Air, float Jouls)Flux)
        {
            if (!float.IsNaN(Flux.Units_Steam)) this.Flux.Units_Steam += Flux.Units_Steam;
            if (!float.IsNaN(Flux.Units_Air)) this.Flux.Units_Air += Flux.Units_Air;
            if (!float.IsNaN(Flux.Jouls)) this.Flux.Jouls += Flux.Jouls;
        }

        public void SimulateIteration1(float timeStep)
        {
            if (simulator == null)
            {
                FindSimulator();
            }
            
            PartResource internalSteam = this.part.Resources["Steam"];
            Mass_steam_Display = internalSteam.amount;

            internalSteam.amount += Flux.Units_Steam;
            if (internalSteam.amount < 0) internalSteam.amount = 0;
            Mass_Air += Flux.Units_Air;
            if (Mass_Air < 0) Mass_Air = 0;
            ThermalEnergy += Flux.Jouls;
            if (ThermalEnergy < 0) ThermalEnergy = 0;
            ModuleBoiler moduleBoiler = this.part.Modules.GetModule<ModuleBoiler>();
            if (moduleBoiler != null)
            {
                moduleBoiler.AddFlux(Flux.Jouls);
            }
            Flux = (0, 0, 0);

            PartResourceDefinition steamDef = internalSteam.info;
            if (internalSteam.amount + Mass_Air == 0)
            {
                Temperature = (float)this.part.temperature;
                Pressure = 0;
                Pressure_Display = $"Steam: {0} / Air: {0}";
                return;
            }
            else
            {
                float massSteamTonnes = (float)internalSteam.amount * 0.001f;
                float massAirTonnes = Mass_Air * 0.001f;
                Temperature = ThermalEnergy / (specificHeatCapacity_Air * massAirTonnes + steamDef.specificHeatCapacity * massSteamTonnes);
                Pressure = ((float)internalSteam.amount * Gas_Constant_Steam + Mass_Air * Gas_Constant_Air) * Temperature / volume;
                /*{
                // PV=mRT --> P=mRT/V  kPa = kg * kJ/Mg*K * K / Liter
                // kN/m^2 = kg * kJ/Mg / Liter
                // kN/m^2 = J / Liter
                // kJ/m^3 = J / Liter  // J = Nm -> N = J/m
                // kJ/m^3 = J * k / m^3  // 1000Liter = m^3
                // kJ = J * k    kJ = kJ
                // I hate doig math with non SI units
                }*/
                {
                    float Pressure_Partial_Steam = ((float)internalSteam.amount * Gas_Constant_Steam) * Temperature / volume;
                    float Pressure_Partial_Air = (Mass_Air * Gas_Constant_Air) * Temperature / volume;
                    Pressure_Display = $"Steam: {Pressure_Partial_Steam:F3} / Air: {Pressure_Partial_Air:F3} kPa";
                }
            }
            if (Temperature < 3f) Temperature = 3f;
            BaseField field = Fields["Pressure"];
            if(field.uiControlFlight is UI_FloatRange rangeControl)
            {
                rangeControl.maxValue = (float)this.part.staticPressureAtm * 101.325f + MAWP;
            }

            foreach (ModuleSafetyValve safetyValve in safetyValves)
            {
                safetyValve.SimulateIteration(timeStep);
            }
            foreach (ModuleDynamo dynamo in dynamos)
            {
                dynamo.SimulateIteration(timeStep);
            }
        }

        public virtual void SimulateIteration2(float timeStep, int numSteps)
        {

        }

        protected virtual void Update()
        {
            //cachedPaw = UIPartActionController.Instance.GetItem(this.part);
            //if (cachedPaw != null)
            //{
            //    PartResource targetResource = this.part.Resources["Steam"];
            //    if (targetResource != null)
            //    {
            //        // Only update and refresh if a state change is actually required
            //        if (targetResource.isVisible || targetResource.isTweakable)
            //        {
            //            targetResource.isVisible = false;
            //            targetResource.isTweakable = false;

            //            // This will now only execute ONCE when hiding, preventing the lag loop
            //            MonoUtilities.RefreshPartContextWindow(this.part);
            //        }
            //    }
            //}
        }

        protected void FindSimulator()
        {
            if (HighLogic.LoadedSceneIsFlight)
            {
                simulator = part.vessel.GetComponent<SystemSteamVessel>().Simulator;
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                simulator = SystemSteamEditor.Instance.Simulator;
            }
        }
    }
}
