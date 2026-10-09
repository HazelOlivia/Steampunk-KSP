using UnityEngine;

public class ModuleIsolatedStorage : PartModule
{
    [KSPField]
    public string resourceToLock = "Water";

    [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Isolated Water")]
    public string displayLabel = "";

    // Track the active Part Action Window container reference
    private UIPartActionWindow cachedPaw = null;

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        // Force backend data rules right at launch
        LockBackendResource();
    }

    public override void OnUpdate()
    {
        base.OnUpdate();

        // 1. Keep your custom text indicator label updated with fresh amounts
        if (this.part.Resources.Contains(resourceToLock))
        {
            PartResource res = this.part.Resources[resourceToLock];
            displayLabel = $"{res.amount:F2} / {res.maxAmount:F2} Units";

            // Re-enforce isolation tracking on the data level
            res.flowMode = PartResource.FlowMode.None;
            res.hideFlow = true;
        }

        // 2. Continuous UI Lockdown Sweep
        // Find the active window controller for this part
        cachedPaw = UIPartActionController.Instance.GetItem(this.part);

        if (cachedPaw != null)
        {
            // Grab EVERY resource-related visual element currently inside the active window tree
            var resourceControls = cachedPaw.gameObject.GetComponentsInChildren<UIPartActionResource>();

            foreach (var control in resourceControls)
            {
                if (control.Resource != null && control.Resource.resourceName == resourceToLock)
                {
                    // Kill the green bar's visual rendering entirely
                    if (control.gameObject.activeSelf)
                    {
                        control.gameObject.SetActive(false);
                    }
                }
            }

            // CRITICAL: Look for and eliminate the newly spawned orange "Transfer" / "Stop" buttons
            var transferControls = cachedPaw.gameObject.GetComponentsInChildren<UIPartActionResourceTransfer>();
            foreach (var transfer in transferControls)
            {
                // Verify if this specific transfer block belongs to our forbidden resource type
                if (transfer.Resource != null && transfer.Resource.resourceName == resourceToLock)
                {
                    // Immediately shut down the active transfer flow engine logic
                    transfer.FlowStop();

                    // Wipe the transfer controller elements cleanly from the display layout
                    if (transfer.gameObject.activeSelf)
                    {
                        transfer.gameObject.SetActive(false);
                        cachedPaw.UpdateWindow(); // Force layout snap adjustment
                    }
                }
            }
        }
    }

    private void LockBackendResource()
    {
        if (this.part.Resources.Contains(resourceToLock))
        {
            PartResource res = this.part.Resources[resourceToLock];
            res.flowMode = PartResource.FlowMode.None; // Stops engine crossfeed drains
            res.hideFlow = true;                       // Removes the tiny play/pause flow checkmark
            res.flowState = false;                     // Shuts off the physical valve channel
        }
    }
}
