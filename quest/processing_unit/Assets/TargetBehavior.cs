using UnityEngine;

public class TargetBehavior : MonoBehaviour
{
    public int targetIndex;
    private Renderer _renderer;
    private Material _mat;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null)
        {
            _mat = new Material(_renderer.material);
            _renderer.material = _mat;
        }

        SetNeutral();
    }

    public void SetNeutral()
    {
        if (_mat != null)
            _mat.color = Color.gray;
    }

    public void SetStart()
    {
        if (_mat != null)
            _mat.color = Color.gray;
    }

    public void SetEnd()
    {
        if (_mat != null)
            _mat.color = Color.green;
    }
}
