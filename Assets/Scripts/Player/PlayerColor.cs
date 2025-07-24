using UnityEngine;
using UnityEngine.UI;

public class PlayerColor : MonoBehaviour
{
    [SerializeField] private MeshRenderer _boatColorRenderer;
    [SerializeField] private MeshRenderer _boatLightBulbRenderer;
    [SerializeField] private MeshRenderer _volumetricSphereRenderer;
    [SerializeField] private Light _boatPointLight;
    [SerializeField] private Image _lightConeImage;
    [SerializeField] private Image _lightCircleImage;

    private MaterialPropertyBlock _boatColorBlock;
    private MaterialPropertyBlock _boatLightBulbBlock;
    private MaterialPropertyBlock _volumetricSphereBlock;

    private static readonly int _COLOR = Shader.PropertyToID("_Color");
    private static readonly int _BASE_COLOR = Shader.PropertyToID("_BaseColor");
    private static readonly int _EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");

    private void Start()
    {
        _boatColorBlock = new MaterialPropertyBlock();
        _boatLightBulbBlock = new MaterialPropertyBlock();
        _volumetricSphereBlock = new MaterialPropertyBlock();
    }

    public void UpdateColor(Color color)
    {
        // Update boat color
        if (_boatColorBlock == null || _boatLightBulbBlock == null || _volumetricSphereBlock == null)
        {
            Start();
        }

        _boatColorBlock.SetColor(_BASE_COLOR, color);
        _boatColorRenderer.SetPropertyBlock(_boatColorBlock);

        // Update boat light bulb color
        _boatLightBulbBlock.SetColor(_BASE_COLOR, color);
        _boatLightBulbBlock.SetVector(_EMISSION_COLOR, color * 4f);
        _boatLightBulbRenderer.SetPropertyBlock(_boatLightBulbBlock);

        // Light cone color
        float lightConeAlpha = _lightConeImage.color.a;
        Color newLightConeColor = color;
        newLightConeColor.a = lightConeAlpha;
        _lightConeImage.color = newLightConeColor;
        
        // Light circle color
        float lightCircleAlpha = _lightCircleImage.color.a;
        Color newLightCircleColor = color;
        newLightCircleColor.a = lightCircleAlpha;
        _lightCircleImage.color = newLightCircleColor;

        // Update boat point light color
        _boatPointLight.color = color;

        // Volumetric sphere color
        _volumetricSphereBlock.SetColor(_COLOR, color);
        _volumetricSphereRenderer.SetPropertyBlock(_volumetricSphereBlock);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            // Example usage: Change color to red when R is pressed
            UpdateColor(Color.red);
        }
        else if (Input.GetKeyDown(KeyCode.Y))
        {
            // Example usage: Change color to green when G is pressed
            UpdateColor(Color.yellow);
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            // Example usage: Change color to blue when B is pressed
            UpdateColor(Color.cyan);
        }
    }
}
