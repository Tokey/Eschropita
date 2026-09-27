using UnityEngine;

/// <summary>
/// Keeps the Chowder Fill shader's projection basis matched to the camera rig.
///
/// The shader projects world position onto the camera's image plane using yaw and
/// pitch supplied as material values rather than reading the live view matrix -
/// that is deliberate, since IsoFollowCamera adds perlin shake and speed-based
/// zoom, and sampling the real matrix would make the pattern wobble and breathe.
/// The cost of that choice is that the two can drift apart if you retune the rig
/// in the inspector, so this component copies the angles across.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class ChowderFillCameraSync : MonoBehaviour
{
    [Tooltip("Leave empty to find the IsoFollowCamera in the scene automatically.")]
    [SerializeField] IsoFollowCamera rig;

    [Tooltip("Used only when no rig is found, so the effect still works in a test scene.")]
    [SerializeField] float fallbackYaw = 45f;
    [SerializeField] float fallbackPitch = 35f;

    static readonly int YawID   = Shader.PropertyToID("_CamYaw");
    static readonly int PitchID = Shader.PropertyToID("_CamPitch");

    Renderer _renderer;
    MaterialPropertyBlock _mpb;
    float _lastYaw = float.NaN;
    float _lastPitch = float.NaN;

    void OnEnable()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();
        if (!rig) rig = FindAnyObjectByType<IsoFollowCamera>();
        Apply(true);
    }

    void LateUpdate() => Apply(false);

    void Apply(bool force)
    {
        float yaw   = rig ? rig.yaw   : fallbackYaw;
        float pitch = rig ? rig.pitch : fallbackPitch;

        if (!force && Mathf.Approximately(yaw, _lastYaw) && Mathf.Approximately(pitch, _lastPitch))
            return;

        _lastYaw = yaw;
        _lastPitch = pitch;

        // Read first so we inherit whatever the SpriteRenderer already wrote into
        // the block for the current animation frame, rather than replacing it.
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(YawID, yaw);
        _mpb.SetFloat(PitchID, pitch);
        _renderer.SetPropertyBlock(_mpb);
    }
}
