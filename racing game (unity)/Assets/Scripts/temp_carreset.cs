using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class temp_carreset : MonoBehaviour
{
    public GameObject car;
    public Vector3 storedPos;
    public Quaternion storedRot;
    private bool isKinematic = true;
    private RacingEnvironmentController environment;

    void Awake()
    {
        if (car == null)
        {
            Debug.LogError("temp_carreset requires the active training car.", this);
            enabled = false;
            return;
        }

    }

    void Start()
    {
        storedPos = car.transform.position;
        storedRot = car.transform.rotation;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            storedPos = car.transform.position;
            storedRot = car.transform.rotation;
            environment.SetRuntimeSpawnPose(storedPos, storedRot);
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            environment.ResetEpisode(enableRLControl: false);
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            car.GetComponent<Rigidbody>().isKinematic = isKinematic;
            isKinematic = !isKinematic;
        }
    }
}
