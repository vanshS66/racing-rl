using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform car;
    private Car carScript;
    public Transform cam;
    private Rigidbody carRb;

    public Transform lookTarget;
    public Transform[] camPositions;
    private int camPosIndicator = 0;
    [Range(0, 1f)] public float baseSmoothTime;
    public float smoothTime;

    void Start()
    {
        carRb = car.GetComponent<Rigidbody>();
        carScript = car.GetComponent<Car>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            camPosIndicator += 1;
            camPosIndicator %= camPositions.Length;
            Debug.Log("camera cycle: " + camPosIndicator);
        }

        if (camPosIndicator == 0)
            return;

        // strict follow
        Transform cameraPos = camPositions[camPosIndicator];
        transform.position = cameraPos.position;
        transform.rotation = cameraPos.rotation;
    }

    void FixedUpdate()
    {
        if (camPosIndicator != 0)
            return;
            
        Transform cameraPos = camPositions[camPosIndicator];

        smoothTime = Mathf.Lerp(baseSmoothTime, 0.7f, Mathf.Clamp01((carScript.speed / 100) - 1));
        transform.position = cameraPos.position * (1 - smoothTime) + transform.position * smoothTime;
        transform.LookAt(lookTarget);

    }

}
