using UnityEngine;


namespace GogoGaga.OptimizedRopesAndCables
{
    [RequireComponent(typeof(Rope))]
    public class RopeWindEffect : MonoBehaviour
    {
        [Header("Wind Settings")]
        [Tooltip("Set wind direction perpendicular to the rope based on the start and end points")]
        public bool perpendicularWind = false;
        [Tooltip("Flip the direction of the wind")]
        public bool flipWindDirection = false;

        [Tooltip("Direction of the wind force in degrees")]
        [Range(-360f, 360f)]
        public float windDirectionDegrees;
        Vector3 windDirection;

        [Tooltip("Magnitude of the wind force")]
        [Range(0f, 500f)] public float windForce;
        float appliedWindForce;
        float windSeed; //gives a little variety on the movement when there are multiple ropes


        Rope rope;


        private void Awake()
        {
            rope = GetComponent<Rope>();
        }

        void Start()
        {
            windSeed = Random.Range(-0.3f, 0.3f);
        }

        // Update is called once per frame
        void Update()
        {
            GenerateWind();
        }

        void FixedUpdate()
        {
            SimulatePhysics();
        }

        void GenerateWind()
        {
            Vector3 gravityDirection = rope.gravityDirection;
            Vector3 up = -gravityDirection.normalized;

            // Basis vectors for the plane perpendicular to gravity
            Vector3 planeRight = Vector3.Cross(gravityDirection, Vector3.forward);
            if (planeRight.sqrMagnitude < 0.001f)
                planeRight = Vector3.Cross(gravityDirection, Vector3.right);
            planeRight.Normalize();

            Vector3 planeForward = Vector3.Cross(gravityDirection.normalized, planeRight).normalized;

            float noise = (Mathf.PerlinNoise(Time.time + windSeed, 0.0f) - 0.5f) * 2f * 20f; // full range [-20°, +20°]

            float noisyWindDirection;

            if (perpendicularWind)
            {
                Vector3 startToEnd = rope.EndPoint.position - rope.StartPoint.position;
                Vector3 perp = Vector3.Cross(startToEnd, up).normalized;

                // Project 'perp' onto our wind plane
                float x = Vector3.Dot(perp, planeRight);
                float y = Vector3.Dot(perp, planeForward);
                float baseAngle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

                noisyWindDirection = baseAngle + noise;
                windDirectionDegrees = baseAngle; // for inspector/debug
            }
            else
            {
                noisyWindDirection = windDirectionDegrees + noise;
            }

            // Rotate within the plane using the 2D basis
            float radians = noisyWindDirection * Mathf.Deg2Rad;
            windDirection = (Mathf.Cos(radians) * planeRight + Mathf.Sin(radians) * planeForward).normalized;

            // Apply Perlin noise to the wind force
            // Map Perlin noise to [-1, 1] and optionally combine two for variation
            float windNoise = ((Mathf.PerlinNoise(Time.time + windSeed, 0.0f) - 0.5f) * 2f)
                              * ((Mathf.PerlinNoise(0.5f * Time.time, 0.0f) - 0.5f) * 2f);

            // Apply force
            appliedWindForce = (flipWindDirection ? -1f : 1f) * (windForce * 5f) * windNoise;
        }


        void SimulatePhysics()
        {
            Vector3 windEffect = windDirection.normalized * appliedWindForce * Time.fixedDeltaTime;
            rope.otherPhysicsFactors = windEffect;
        }
    }
}