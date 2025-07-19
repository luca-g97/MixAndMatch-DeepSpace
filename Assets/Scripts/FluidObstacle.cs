using Seb.Fluid2D.Simulation;
using UnityEngine;

public class FluidObstacle : MonoBehaviour
{
    // A static reference to the sim manager. 
    // This is efficient because we only need to find it once for all obstacles.
    private static FluidSim2D fluidSim;

    void OnEnable()
    {
        // If we haven't found the simulation manager yet, find it.
        if (fluidSim == null)
        {
            fluidSim = FindFirstObjectByType<FluidSim2D>();
        }

        // If the manager exists, register this GameObject with it.
        if (fluidSim != null)
        {
            fluidSim.RegisterObstacle(this.gameObject);
        }
        else
        {
            Debug.LogWarning("FluidObstacle could not find FluidSim2D in the scene!", this);
        }
    }

    void OnDisable()
    {
        // If the simulation manager still exists (it might be destroyed first on scene close),
        // unregister this GameObject.
        if (fluidSim != null)
        {
            if (this.gameObject != null)
            {
                fluidSim.UnregisterObstacle(this.gameObject);
            }
        }
    }
}
