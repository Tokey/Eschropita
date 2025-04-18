using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField]
    private float moveSpeed = 1f;
    [SerializeField]
    private float gravity = -9.81f;

    Animator animator;
    CharacterController cc;
    float scaleX;
    Vector3 velocity;

    private void Start()
    {
        animator = GetComponent<Animator>();
        cc = GetComponent<CharacterController>();
        scaleX = transform.localScale.x;
        velocity = Vector3.zero;
    }

    void Update()
    {
        Vector3 input = new Vector3(
            Input.GetAxis("Horizontal"),
            0f,
            Input.GetAxis("Vertical")
        ).normalized;

        Vector3 horizontalMove = input * moveSpeed;

        if (cc.isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;

        // move character
        Vector3 move = horizontalMove + Vector3.up * velocity.y;
        cc.Move(move * Time.deltaTime);

        animator.SetFloat("Speed", input.sqrMagnitude);

        Debug.Log(input.sqrMagnitude);

        // flip on X
        if (input.x != 0f)
        {
            Vector3 s = transform.localScale;
            s.x = scaleX * (input.x > 0 ? 1 : -1);
            transform.localScale = s;
        }
    }
}
