using UnityEngine;

/// <summary>
/// Turns a sprite to face the isometric camera.
///
/// Without this the player quad sits at identity rotation facing world +Z while
/// the rig views from yaw 45, so the character renders about cos(45) = 71% of its
/// drawn width.
///
/// Angles come from IsoFollowCamera rather than the live camera transform on
/// purpose: the rig applies perlin shake, and following that would make the
/// sprite jitter every frame.
/// </summary>
[ExecuteAlways]
public class SpriteBillboard : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>Upright, turned to the camera's yaw. Feet stay planted; art is squashed vertically by cos(pitch).</summary>
        YAxisOnly,
        /// <summary>Square to the view, so the art is never distorted, but the character reads as leaning back.</summary>
        FaceCamera,
    }

    [SerializeField] Mode mode = Mode.YAxisOnly;

    [Header("Vertical Squash Compensation")]
    [Tooltip("Stretches Y by 1/cos(pitch) so the drawing keeps its proportions in YAxisOnly mode. " +
             "Ignored when this object owns a CharacterController, since scaling that transform " +
             "deforms the collision capsule.")]
    [SerializeField] bool compensateVerticalSquash;

    [Tooltip("The object's intended Y scale before compensation. This is an explicit value rather " +
             "than something read off the transform: reading back a value this component itself " +
             "wrote makes it compound by 1/cos(pitch) on every domain reload.")]
    [SerializeField] float baseScaleY = 1f;

    [Header("Rig")]
    [Tooltip("Leave empty to find the IsoFollowCamera automatically.")]
    [SerializeField] IsoFollowCamera rig;

    [SerializeField] float fallbackYaw = 45f;
    [SerializeField] float fallbackPitch = 35f;

    bool _warnedAboutController;

    void OnEnable()
    {
        if (!rig) rig = FindAnyObjectByType<IsoFollowCamera>();
        Apply();
    }

    void LateUpdate() => Apply();

    void Apply()
    {
        float yaw   = rig ? rig.yaw   : fallbackYaw;
        float pitch = rig ? rig.pitch : fallbackPitch;

        transform.rotation = mode == Mode.FaceCamera
            ? Quaternion.Euler(pitch, yaw, 0f)
            : Quaternion.Euler(0f, yaw, 0f);

        if (!compensateVerticalSquash || mode != Mode.YAxisOnly) return;

        // A CharacterController derives its capsule from the transform's scale,
        // and a non-uniform scale deforms it badly enough that isGrounded stops
        // reporting true - after which gravity accumulates until the character
        // tunnels through the floor. Put the sprite on a child object if you
        // want compensation on a controller-driven character.
        if (TryGetComponent(out CharacterController _))
        {
            if (!_warnedAboutController)
            {
                _warnedAboutController = true;
                Debug.LogWarning($"[SpriteBillboard] '{name}' has a CharacterController, so vertical " +
                                 "squash compensation is being skipped to avoid deforming its capsule.", this);
            }
            return;
        }

        // Always computed from the serialized base, never from current scale,
        // so repeated calls converge instead of compounding.
        float wanted = baseScaleY / Mathf.Max(Mathf.Cos(pitch * Mathf.Deg2Rad), 0.05f);

        var scale = transform.localScale;
        if (!Mathf.Approximately(scale.y, wanted))
        {
            scale.y = wanted;   // Only Y. PlayerController owns X, which it negates to flip facing.
            transform.localScale = scale;
        }
    }
}
