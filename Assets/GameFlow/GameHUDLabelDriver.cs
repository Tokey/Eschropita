using UnityEngine;

/// <summary>
/// Runs <see cref="GameHUD.PositionWorldLabels"/> late enough in the frame that the camera has
/// already been moved.
///
/// GameHUD itself is at execution order -100 so that its singleton and canvas exist before anything
/// else's Start runs. CinemachineBrain moves the real camera in its own LateUpdate at order 0, which
/// is after that — so a label projected during GameHUD.LateUpdate uses the previous frame's camera.
/// The rig (IsoFollowCamera) drives a CinemachineBasicMultiChannelPerlin, so the camera moves by a
/// visible amount every single frame and that one-frame lag reads as the labels drifting around the
/// screen independently of the world.
///
/// Splitting the call into a separate component is what buys the ordering: the attribute is per
/// class, so this is the only way to keep GameHUD early and still place labels late. GameHUD adds
/// this component to its own GameObject, so there is nothing to set up in the scene.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class GameHUDLabelDriver : MonoBehaviour
{
    GameHUD _hud;

    void Awake() => _hud = GetComponent<GameHUD>();

    void LateUpdate()
    {
        if (_hud == null) return;
        _hud.PositionWorldLabels();
    }
}
