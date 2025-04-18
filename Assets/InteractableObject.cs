using UnityEngine;

public class InteractableObject : MonoBehaviour
{
    public GameObject player;

    public float proximityDistance = 3f;

    public float flashSpeed = 3f;

    private Renderer rend;
    private Color originalColor;

    void Start()
    {
        rend = GetComponent<Renderer>();
        originalColor = rend.material.color;
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.transform.position);

        if (dist <= proximityDistance)
        {
            // PingPong goes 0→1→0 repeatedly
            float t = Mathf.PingPong(Time.time * flashSpeed, 1f);
            // Lerp from original to red
            rend.material.color = Color.Lerp(originalColor, Color.red, t);
        }
        else
        {
            // back to normal
            rend.material.color = originalColor;
        }
    }
}
