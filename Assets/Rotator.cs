using UnityEngine;

public class TurbineRotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    public float rotationSpeed = 180f; // degrees per second

    [Header("Axis")]
    public bool rotateX = false;
    public bool rotateY = false;
    public bool rotateZ = true; // usually this one

    void Update()
    {
        Vector3 axis = new Vector3(
            rotateX ? 1f : 0f,
            rotateY ? 1f : 0f,
            rotateZ ? 1f : 0f
        );

        transform.Rotate(axis, rotationSpeed * Time.deltaTime, Space.Self);
    }
}
