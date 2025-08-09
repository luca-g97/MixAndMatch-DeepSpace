using Assets.UnityPharusAPI.Player;
using UnityEngine;

namespace Assets.Tracking_Example.Scripts
{
    public class TestPlayer : ATrackingEntity
    {
        public PlayerSound playerSound;
        public PlayerEffects playerEffects;
        
        public override void SetPosition(Vector2 theNewPosition)
        {
            base.SetPosition(theNewPosition);
        }
    }
}
