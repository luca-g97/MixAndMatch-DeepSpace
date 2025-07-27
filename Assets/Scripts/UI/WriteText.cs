using System.Collections;
using KBCore.Refs;
using TMPro;
using UnityEngine;

public class WriteText : ValidatedMonoBehaviour
{
    [SerializeField, Self] private TMP_Text _text;
    [SerializeField] private float _timeBetweenCharacters = 0.01f;
    
    private Coroutine _currentTextCoroutine;

    private void Start()
    {
        _text.maxVisibleCharacters = 0;
        _text.ForceMeshUpdate();
    }

    public void Write()
    {
        _text.maxVisibleCharacters = 0;
        _text.ForceMeshUpdate();
        
        if (_currentTextCoroutine != null)
        {
            StopCoroutine(_currentTextCoroutine);
        }
        
        _currentTextCoroutine = StartCoroutine(TextRevealCoroutine());
    }
    
    private IEnumerator TextRevealCoroutine()
    {
        _text.ForceMeshUpdate();
        int totalCharacters = _text.textInfo.characterCount;
        int counter = 0;

        while (true)
        {
            int visibleCount = counter % (totalCharacters + 1);
            _text.maxVisibleCharacters = visibleCount;

            if (visibleCount >= totalCharacters)
            {
                break;
            }
            
            counter += 1;
            yield return new WaitForSeconds(_timeBetweenCharacters);
        }
    }
}
