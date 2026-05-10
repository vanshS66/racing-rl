using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class temp_carreset : MonoBehaviour
{
    public GameObject car;
    public Vector3 storedPos;
    public Quaternion storedRot;
    private bool isKinematic = true;
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
        }

        if (Input.GetKeyDown(KeyCode.V))
        {
            car.GetComponent<Rigidbody>().isKinematic = true;
            car.transform.position = storedPos;
            car.transform.rotation = storedRot;
            car.GetComponent<Rigidbody>().isKinematic = false;
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            car.GetComponent<Rigidbody>().isKinematic = isKinematic;
            isKinematic = !isKinematic;
        }
    }
}
