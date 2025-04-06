using System.Runtime.ConstrainedExecution;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerController : MonoBehaviour
{
    [SerializeField]

    private float moveSpeed = 1;
    Animator animator;
    Rigidbody2D rigidbody2d;

    float scaleX;

    // Start is called before the first frame update O references

    private void Start()
    {
        animator = GetComponent<Animator>(); rigidbody2d = GetComponent<Rigidbody2D>();
        rigidbody2d = GetComponent<Rigidbody2D>();
        scaleX = transform.localScale.x;
    }

void Update()
    {
        Vector2 velocity = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).normalized * moveSpeed;
        rigidbody2d.linearVelocity = velocity;
        animator.SetFloat("Speed", velocity.sqrMagnitude);

        // Flip Animation

        Vector3 scale = transform.localScale; 
        scale.x = scaleX*(velocity.x >= 0 ? 1 : -1);
        transform.localScale = scale; 
    }
}
