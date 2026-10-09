using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace KerbalSteamProgram
{
    public class ModuleSteamReservoar : PartModule
    {
        [KSPField]
        public float MAWP = 1400; //kPa

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

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Steam Valve", groupName = "sysSteaminfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemSteam_GroupName")]
        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 1f)]
        public float Steam_Valve = 100f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_SystemSteam_ModuleSystemSteam_Field_LoopID", groupName = "sysSteaminfo", groupDisplayName = "#LOC_SystemSteam_ModuleSystemSteam_GroupName")]
        [UI_ChooseOption(affectSymCounterparts = UI_Scene.Editor, options = new[] { "None" }, scene = UI_Scene.All, suppressEditorShipModified = false)]
        public int currentLoopID = 0;

        [KSPField]
        public float ThermalEnergy = 0f; //kJ

        [KSPField]
        public float Pressure = 101f; //kPa

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Pressure", guiUnits = "kPa", guiFormat = "F0", groupName = "sysSteaminfo")]
        [UI_ProgressBar(minValue = 0f, maxValue = 1500f)]
        public float Display_Pressure = 101f;

        [KSPField/*(isPersistant = false, guiActive = true, guiActiveEditor = true, guiName = "Temperature", guiUnits = "K", guiFormat = "F0", groupName = "sysSteaminfo")*/]
        public float Temperature = 288f; //Kelvin

        const float Gass_Constant = 461.5f; // kJ/tonne*K   0.4615 kJ/kg*K

        public (float Units, float Jouls) Flux = (0,0);

        public SteamLoop Loop => simulator?.Loop(currentLoopID);

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

        public void AddFlux((float Units,float Jouls)Flux)
        {
            this.Flux.Units += Flux.Units;
            this.Flux.Jouls += Flux.Jouls;
        }

        protected void FixedUpdate()
        {
            if (simulator == null)
            {
                FindSimulator();
            }
            
            PartResource internalSteam = this.part.Resources["Steam"];

            internalSteam.amount += Flux.Units;
            ThermalEnergy += Flux.Jouls;
            ModuleBoiler moduleBoiler = this.part.Modules.GetModule<ModuleBoiler>();
            if (moduleBoiler != null)
            {
                moduleBoiler.AddFlux(Flux.Jouls);
            }
            Flux = (0, 0);

            PartResourceDefinition steamDef = internalSteam.info;
            if (internalSteam.amount == 0)
            {
                Pressure = (float)this.part.staticPressureAtm * 101.325f;
                return;
            }
            float massSteamTonnes = (float)internalSteam.amount * 0.001f;

            Temperature = ThermalEnergy / (steamDef.specificHeatCapacity * massSteamTonnes);
            Pressure = ((float)internalSteam.amount * Gass_Constant * Temperature) / (volume);
            Display_Pressure = Mathf.Max((float)this.part.staticPressureAtm * 101.325f, Pressure);
            /*{
            // PV=mRT --> P=mRT/V  kPa = kg * kJ/Mg*K * K / Liter
            // kN/m^2 = kg * kJ/Mg / Liter
            // kN/m^2 = J / Liter
            // kJ/m^3 = J / Liter  // J = Nm -> N = J/m
            // kJ/m^3 = J * k / m^3  // 1000Liter = m^3
            // kJ = J * k    kJ = kJ
            // I hate doig math with non SI units
            }*/
            BaseField field = Fields["Display_Pressure"];
            if(field.uiControlFlight is UI_FloatRange rangeControl)
            {
                rangeControl.maxValue = (float)this.part.staticPressureAtm * 101.325f + MAWP;
            }
        }

        protected void Update()
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
