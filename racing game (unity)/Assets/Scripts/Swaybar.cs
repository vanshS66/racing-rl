using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Swaybar : MonoBehaviour
{
    public Suspension suspL;
    public Suspension suspR;
    public float antirollMulti;

    private Rigidbody rb;

    void Start()
    {
        rb = transform.root.GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        float compressionL = suspL.restLength - suspL.springLength;
        float compressionR = suspR.restLength - suspR.springLength;

        float difference = compressionL - compressionR;

        if(suspL.isGrounded)
            rb.AddForceAtPosition(1f * transform.up * difference * antirollMulti, suspL.transform.position);
        if(suspR.isGrounded)
            rb.AddForceAtPosition(-1f * transform.up * difference * antirollMulti, suspR.transform.position);
    }
}
