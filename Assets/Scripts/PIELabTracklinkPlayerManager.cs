using Assets.UnityPharusAPI.Helper;
using Assets.UnityPharusAPI.Managers;
using Assets.UnityPharusAPI.Player;
using Seb.Fluid2D.Simulation;
using UnityEngine;
using UnityPharusAPI;
using UnityPharusAPI.TransmissionFrameworks.Tracklink;

namespace Assets.Tracking_Example.Scripts
{
    public class PIELabTracklinkPlayerManager : ATracklinkPlayerManager
    {
        [SerializeField] private float zeroAbsoluteX;
        [SerializeField] private float zeroAbsoluteY;
        [SerializeField] private float fullAbsoluteX;
        [SerializeField] private float fullAbsoluteY;
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
            factorX = simulationBounds.x / 2 * (-1);
            factorY = simulationBounds.y / 2 * (-1);

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
                    newPlayerPos.x = factorX + percentX * simulationBounds.x;
                    newPlayerPos.y = factorY + Mathf.InverseLerp(zeroAbsoluteY, fullAbsoluteY, trackRecord.currentPos.y) * simulationBounds.y;
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
