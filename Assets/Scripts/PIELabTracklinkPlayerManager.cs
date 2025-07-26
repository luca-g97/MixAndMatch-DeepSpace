using Assets.UnityPharusAPI.Helper;
using Assets.UnityPharusAPI.Managers;
using Assets.UnityPharusAPI.Player;
using Seb.Fluid2D.Simulation;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TMPro;
using UnityEngine;
using UnityPharusAPI;
using UnityPharusAPI.Services;
using UnityPharusAPI.TransmissionFrameworks.Tracklink;

namespace Assets.Tracking_Example.Scripts
{
    public class PIELabTracklinkPlayerManager : ATracklinkPlayerManager
    {
        [SerializeField] private float zeroAbsoluteX;
        [SerializeField] private float zeroAbsoluteY;
        [SerializeField] private float fullAbsoluteX;
        [SerializeField] private float fullAbsoluteY;

        [SerializeField] private float xOffset;
        [SerializeField] private float yOffset;

        // TODO: Remove Text! Only for Debug!
        [SerializeField] private TextMeshPro textXOffset;
        [SerializeField] private TextMeshPro textYOffset;

        private FluidSim2D fluidSim;
        private Vector2 simulationBounds;
        private float factorX;
        private float factorY;

        void Start()
        {
            if (GameObject.FindFirstObjectByType<FluidSim2D>())
            {
                fluidSim = GameObject.FindFirstObjectByType<FluidSim2D>().gameObject.GetComponent<FluidSim2D>();
            }
            else
            {
                this.gameObject.SetActive(false);
                return;
            }

            simulationBounds = fluidSim.boundsSize;

            UpdateBoundaries();

            factorX = (simulationBounds.x - xOffset) / 2 * (-1);
            factorY = (simulationBounds.y - yOffset) / 2 * (-1);

            textXOffset.text = "X-Offset: " + xOffset.ToString("0.0000") + "m";
            textYOffset.text = "Y-Offset: " + yOffset.ToString("0.0000") + "m";
        }

        // TODO: INEFFICENT!! Remove the Update Function before the final Build - only for testing in the Deep Space
        /*
        void Update()
        {
            if (Input.GetKey(KeyCode.UpArrow))
            {
                yOffset += 0.0025f;
            }
            else if (Input.GetKey(KeyCode.DownArrow))
            {
                yOffset -= 0.0025f;
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                xOffset += 0.0025f;
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                xOffset -= 0.0025f;
            }
            else
            {
                return;
            }

            factorX = (simulationBounds.x - xOffset) / 2 * (-1);
            factorY = (simulationBounds.y - yOffset) / 2 * (-1);

            textXOffset.text = "X-Offset: " + xOffset.ToString("0.0000") + "m";
            textYOffset.text = "Y-Offset: " + yOffset.ToString("0.0000") + "m";
        }
        */

        public void UpdateBoundaries()
        {
            string configFileName = "Pharus_Config.xml";
            string path = Path.Combine(Application.streamingAssetsPath, configFileName);

            if (!File.Exists(path))
            {
                Debug.LogError("Pharus file Error! Using default values instead");
                return;
            }

            try
            {
                XmlDocument xmlDocument = new XmlDocument();
                xmlDocument.Load(path);

                XmlNode root = xmlDocument.SelectSingleNode("pharus");
                if (root == null)
                {
                    Debug.LogError("XML in wrong configuration. Using default values.");
                    return;
                }

                XmlNode updateNode = root.SelectSingleNode("useXML");
                if (updateNode != null && bool.TryParse(updateNode.InnerText, out bool shouldUpdate) && shouldUpdate)
                {
                    XmlNode zxNode = root.SelectSingleNode("zeroAbsoluteX");
                    if (zxNode != null && float.TryParse(zxNode.InnerText, out float zx))
                    {
                        zeroAbsoluteX = zx / 100.0f;
                    }
                    XmlNode zyNode = root.SelectSingleNode("zeroAbsoluteY");
                    if (zyNode != null && float.TryParse(zyNode.InnerText, out float zy))
                    {
                        zeroAbsoluteY = zy / 100.0f;
                    }
                    XmlNode fxNode = root.SelectSingleNode("fullAbsoluteX");
                    if (fxNode != null && float.TryParse(fxNode.InnerText, out float fx))
                    {
                        fullAbsoluteX = fx / 100.0f;
                    }
                    XmlNode fyNode = root.SelectSingleNode("fullAbsoluteY");
                    if (fyNode != null && float.TryParse(fyNode.InnerText, out float fy))
                    {
                        fullAbsoluteY = fy / 100.0f;
                    }
                    XmlNode xNode = root.SelectSingleNode("xOffset");
                    if (xNode != null && float.TryParse(xNode.InnerText, out float xO))
                    {
                        xOffset = xO / 100.0f;
                    }
                    XmlNode yNode = root.SelectSingleNode("yOffset");
                    if (yNode != null && float.TryParse(yNode.InnerText, out float yO))
                    {
                        yOffset = yO / 100.0f;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogErrorFormat("XML read error: {0}", e.Message);
            }
        }

        public override void AddPlayer(TrackRecord trackRecord)
        {
            Vector2 position = VectorAdapter.ToUnityVector2(TrackingAdapter.GetScreenPositionFromRelativePosition(trackRecord.relPos.x, trackRecord.relPos.y));
            ATrackingEntity aPlayer = (GameObject.Instantiate(_playerPrefab, new Vector3(position.x, position.y, 0), Quaternion.identity) as GameObject).GetComponent<ATrackingEntity>();
            aPlayer.TrackID = trackRecord.trackID;
            aPlayer.AbsolutePosition = new Vector2(trackRecord.currentPos.x, trackRecord.currentPos.y);
            aPlayer.NextExpectedAbsolutePosition = new Vector2(trackRecord.expectPos.x, trackRecord.expectPos.y);
            aPlayer.RelativePosition = new Vector2(trackRecord.relPos.x, trackRecord.relPos.y);
            aPlayer.Orientation = new Vector2(trackRecord.orientation.x, trackRecord.orientation.y);
            aPlayer.Speed = trackRecord.speed;
            aPlayer.Echoes.Clear();
            trackRecord.echoes.AddToVector2List(VectorAdapter.ToPharusVector2List(aPlayer.Echoes));

            aPlayer.gameObject.name = string.Format("PharusPlayer_{0}", aPlayer.TrackID);

            _playerList.Add(aPlayer);

            fluidSim.obstacles.Add(aPlayer.gameObject);
        }

        public override void RemovePlayer(int trackID)
        {
            foreach (ATrackingEntity player in _playerList.ToArray())
            {
                if (player.TrackID.Equals(trackID))
                {
                    fluidSim.obstacles.Remove(player.gameObject);
                    GameObject.Destroy(player.gameObject);
                    _playerList.Remove(player);
                    // return here in case you are really really sure the trackID is in our list only once!
                    //				return;
                }
            }
        }

        public override void UpdatePlayerPosition(TrackRecord trackRecord)
        {
            foreach (ATrackingEntity aPlayer in _playerList)
            {
                if (aPlayer.TrackID == trackRecord.trackID)
                {
                    aPlayer.AbsolutePosition = new Vector2(trackRecord.currentPos.x, trackRecord.currentPos.y);
                    aPlayer.NextExpectedAbsolutePosition = new Vector2(trackRecord.expectPos.x, trackRecord.expectPos.y);
                    aPlayer.RelativePosition = new Vector2(trackRecord.relPos.x, trackRecord.relPos.y);
                    aPlayer.Orientation = new Vector2(trackRecord.orientation.x, trackRecord.orientation.y);
                    aPlayer.Speed = trackRecord.speed;
                    // use AddToVector2List() instead of ToVector2List() as it is more performant
                    aPlayer.Echoes.Clear();
                    trackRecord.echoes.AddToVector2List(VectorAdapter.ToPharusVector2List(aPlayer.Echoes));
                    //aPlayer.SetPosition(TracklinkTrackingService.GetScreenPositionFromRelativePosition(trackRecord.relPos));
                    //aPlayer.SetPosition(VectorAdapter.ToUnityVector2(TrackingAdapter.GetScreenPositionFromRelativePosition(trackRecord.relPos.x, trackRecord.relPos.y)));

                    Vector2 newPlayerPos = new Vector2();
                    float percentX = Mathf.InverseLerp(zeroAbsoluteX, fullAbsoluteX, trackRecord.currentPos.x);
                    float percentY = Mathf.InverseLerp(zeroAbsoluteY, fullAbsoluteY, trackRecord.currentPos.y);
                    newPlayerPos.x = factorX + percentX * (simulationBounds.x - xOffset);
                    newPlayerPos.y = factorY + percentY * (simulationBounds.y - yOffset);
                    aPlayer.SetPosition(newPlayerPos);

                    return;
                }
            }

            if (_addUnknownPlayerOnUpdate)
            {
                AddPlayer(trackRecord);
            }
        }
    }
}
