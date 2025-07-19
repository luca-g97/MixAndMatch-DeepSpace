using DG.Tweening;
using KBCore.Refs;
using System.Collections.Generic;
using UnityEngine;

public class Ventil : ValidatedMonoBehaviour
{
    [SerializeField] private int maxHealthPoints = 100;
    [SerializeField, Child] private MeshRenderer[] ventilMeshRenderers;
    [SerializeField] private int currentHealthPoints;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private MaterialPropertyBlock _materialPropertyBlock;
    [HideInInspector] public bool IsNotAlive => currentHealthPoints <= 0;

    private static readonly int _SATURATION = Shader.PropertyToID("_Saturation");
    private static readonly int _TINT = Shader.PropertyToID("_Tint");

    private Sequence _currentSpawnSequence;
    private Sequence _currentColorSequence;
    private float _defaultScale;
    private static List<Color> _colorPalette = ColorPalette.colorPalette;


    private void Awake()
    {
        _defaultScale = transform.localScale.x; // Assuming uniform scale
    }

    void Start()
    {
        _materialPropertyBlock = new MaterialPropertyBlock();
        transform.localScale = Vector3.zero;
        SpawnSequence();
    }

    // Update is called once per frame
    void Update()
    {
        UpdateSaturationByHealth();

        foreach (MeshRenderer ventilMeshRenderer in ventilMeshRenderers)
        {
            if (ventilMeshRenderer != null)
            {
                ventilMeshRenderer.SetPropertyBlock(_materialPropertyBlock);
            }
        }
    }

    public void UpdateHealth(int particlesReachedThisFrame)
    {
        if (IsNotAlive) return;
        currentHealthPoints -= particlesReachedThisFrame;

        if (currentHealthPoints <= 0)
        {
            DestroyedSequence();
        }
    }

    public void Kill()
    {
        UpdateHealth(10000);
    }

    public void UpdateTintByParticleType(int type)
    {
        if (IsNotAlive) return;

        if (type < 0 || type >= _colorPalette.Count)
        {
            return;
        }

        Color color = _colorPalette[type];
        ColoringSequence(color);
    }

    private void UpdateSaturationByHealth()
    {
        float saturationByHealth = Helper.RemapRange(currentHealthPoints, 0, maxHealthPoints, 0f, 1f);
        _materialPropertyBlock.SetFloat(_SATURATION, saturationByHealth);
    }

    public void DestroyedSequence()
    {
        _currentSpawnSequence?.Kill();
        _currentSpawnSequence = DOTween.Sequence()
            .Append(transform.DOScale(0, 0.5f).SetEase(Ease.InBack))
        .OnComplete((() => gameObject.SetActive(false)));
    }

    public void SpawnSequence()
    {
        currentHealthPoints = maxHealthPoints;
        _currentSpawnSequence?.Kill();
        gameObject.SetActive(true);
        _currentSpawnSequence = DOTween.Sequence()
            .Append(transform.DOScale(_defaultScale, 0.5f).SetEase(Ease.OutBack));
    }

    private void ColoringSequence(Color color)
    {
        _currentColorSequence?.Kill();
        _currentColorSequence = DOTween.Sequence()
            .AppendCallback((() => _materialPropertyBlock.SetColor(_TINT, color)));
    }

    private void OnDestroy()
    {
        _currentSpawnSequence?.Kill();
        _currentColorSequence?.Kill();
    }
}
