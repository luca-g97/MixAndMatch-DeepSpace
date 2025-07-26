using Seb.Fluid2D.Simulation;
using System.IO;
using System.Xml;
using UnityEngine;

public class XMLSettings : MonoBehaviour
{
    private FluidSim2D floorSIM;
    private FluidSim2D_Wall wallSIM;

    public void XMLReload(int sceneIndex)
    {
        try
        {
            floorSIM = GameObject.FindFirstObjectByType<FluidSim2D>().GetComponent<FluidSim2D>();
            wallSIM = GameObject.FindFirstObjectByType<FluidSim2D_Wall>().GetComponent<FluidSim2D_Wall>();
        }
        catch { }


        string configFileName = "SIM_Config.xml";
        string path = Path.Combine(Application.streamingAssetsPath, configFileName);

        if (!File.Exists(path))
        {
            Debug.LogError("SIM-Settings file Error! Using default values from build instead");
            return;
        }

        try
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load(path);

            XmlNode root = xmlDocument.SelectSingleNode("settings");
            if (root == null)
            {
                Debug.LogError("XML in wrong configuration. Using default values.");
                return;
            }

            XmlNode updateNode = root.SelectSingleNode("useXML");
            if (updateNode != null && bool.TryParse(updateNode.InnerText, out bool shouldUpdate) && shouldUpdate)
            {
                switch (sceneIndex)
                {
                    case 0:
                        if (floorSIM)
                        {
                            XmlNode boundsXNode = root.SelectSingleNode("boundsX");
                            if (boundsXNode != null && float.TryParse(boundsXNode.InnerText, out float value_boundsXNode))
                            {
                                floorSIM.boundsSize.x = value_boundsXNode;
                            }
                            XmlNode boundsYNode = root.SelectSingleNode("boundsY");
                            if (boundsYNode != null && float.TryParse(boundsYNode.InnerText, out float value_boundsYNode))
                            {
                                floorSIM.boundsSize.y = value_boundsYNode;
                            }
                            XmlNode timeScaleNode = root.SelectSingleNode("timeScale");
                            if (timeScaleNode != null && float.TryParse(timeScaleNode.InnerText, out float value_timeScaleNode))
                            {
                                floorSIM.timeScale = value_timeScaleNode;
                            }
                            XmlNode fpsNode = root.SelectSingleNode("fps");
                            if (fpsNode != null && float.TryParse(fpsNode.InnerText, out float value_fpsNode))
                            {
                                floorSIM.maxTimestepFPS = value_fpsNode;
                            }
                            XmlNode iterationsNode = root.SelectSingleNode("iterationsFrame");
                            if (iterationsNode != null && int.TryParse(iterationsNode.InnerText, out int value_iterationsNode))
                            {
                                floorSIM.iterationsPerFrame = value_iterationsNode;
                            }
                            XmlNode gravityNode = root.SelectSingleNode("gravity");
                            if (gravityNode != null && float.TryParse(gravityNode.InnerText, out float value_gravityNode))
                            {
                                floorSIM.gravity = value_gravityNode;
                            }
                            XmlNode totalParticles = root.SelectSingleNode("totalParticles");
                            if (totalParticles != null && int.TryParse(totalParticles.InnerText, out int value_totalParticles))
                            {
                                floorSIM.maxTotalParticles = value_totalParticles;
                            }
                        }
                        break;
                    case 1:
                        if (wallSIM)
                        {
                            XmlNode boundsXNode = root.SelectSingleNode("boundsX_Wall");
                            if (boundsXNode != null && float.TryParse(boundsXNode.InnerText, out float value_boundsXNode))
                            {
                                wallSIM.boundsSize.x = value_boundsXNode;
                            }
                            XmlNode boundsYNode = root.SelectSingleNode("boundsY_Wall");
                            if (boundsYNode != null && float.TryParse(boundsYNode.InnerText, out float value_boundsYNode))
                            {
                                wallSIM.boundsSize.y = value_boundsYNode;
                            }
                            XmlNode timeScaleNode = root.SelectSingleNode("timeScale_Wall");
                            if (timeScaleNode != null && float.TryParse(timeScaleNode.InnerText, out float value_timeScaleNode))
                            {
                                wallSIM.timeScale = value_timeScaleNode;
                            }
                            XmlNode fpsNode = root.SelectSingleNode("fps_Wall");
                            if (fpsNode != null && float.TryParse(fpsNode.InnerText, out float value_fpsNode))
                            {
                                wallSIM.maxTimestepFPS = value_fpsNode;
                            }
                            XmlNode iterationsNode = root.SelectSingleNode("iterationsFrame_Wall");
                            if (iterationsNode != null && int.TryParse(iterationsNode.InnerText, out int value_iterationsNode))
                            {
                                wallSIM.iterationsPerFrame = value_iterationsNode;
                            }
                            XmlNode gravityNode = root.SelectSingleNode("gravity_Wall");
                            if (gravityNode != null && float.TryParse(gravityNode.InnerText, out float value_gravityNode))
                            {
                                wallSIM.gravity = value_gravityNode;
                            }
                            XmlNode totalParticles = root.SelectSingleNode("totalParticles_Wall");
                            if (totalParticles != null && int.TryParse(totalParticles.InnerText, out int value_totalParticles))
                            {
                                wallSIM.maxTotalParticles = value_totalParticles;
                            }
                        }
                        break;
                    default:
                        Debug.LogWarning("XML-Settings called from wrong scene! Index: " + sceneIndex);
                        break;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogErrorFormat("XML read error: {0}", e.Message);
        }
    }
}