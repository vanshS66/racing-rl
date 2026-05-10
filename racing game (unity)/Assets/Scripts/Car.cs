using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.InputSystem;

public class Car : MonoBehaviour
{
    PlayerControls controls;
    private Rigidbody rb;
    public Engine engine;

    [Header("Suspension")]
    public Suspension[] suspensions;

    [Header("Steering")]
    public float maxSteerAngle;

    [Header("Inputs")]
    public float steerInput;
    public float throttleInput;
    public float brakeInput;
    public bool ebrakeInput;

    [Header("Forces")]
    public float engineForce;
    public float brakeForce = 3000f;
    public float const_drag;
    public float const_rr; // rolling resistance

    [Header("Drift")]
    public bool isDrifting = false;
    public float maxGrip = 1f; // temp
    public float driftThreshold; // rear wheels lose grip if driftAngle past this threshold
    public float driftMulti; // traction multiplier when drifting (go fast when drift)
    public float rearGrip = 0.3f;
    public float frontGrip = 0.5f;
    public bool isRearGrounded;
    private float terrainGripMulti = 0.8f;

    [Header("Speeds")]
    public float moveDir;
    public float speed; // in km/h
    public float speedMs;
    private Vector3 lastVelocity;
    private Vector3 acceleration;

    [Header("FX")]
    public Transform brakeTrails;
    public float brakeTrailInputThreshold = 0.7f;
    public float brakeTrailSpeedThreshold = 80f; // in km/h
    private bool isBrakeTrailing = false;


    void Awake()
    {
        controls = new PlayerControls();
    }

    void OnEnable()
    {
        controls.Gameplay.Enable();
    }

    void OnDisable()
    {
        controls.Gameplay.Disable();
    }

    void Start()
    {
        rb = transform.GetComponent<Rigidbody>();
    }

    void Update()
    {
        Inputs();
        BrakeFX();
        Steering();
    }

    private void Inputs()
    {
        // old input system
        // steerInput = Input.GetAxis("Horizontal");
        // throttleInput = Input.GetAxis("Vertical");

        float targetThrottle = controls.Gameplay.Throttle.ReadValue<float>();
        if (targetThrottle < throttleInput)
            throttleInput = Mathf.Lerp(throttleInput, targetThrottle, 3f * Time.deltaTime);
        else
            throttleInput = targetThrottle;
        
        float targetBrake = controls.Gameplay.Brake.ReadValue<float>();
        if (targetBrake < brakeInput)
            brakeInput = Mathf.Lerp(brakeInput, targetBrake, 5f * Time.deltaTime);
        else
            brakeInput = targetBrake;

        steerInput = controls.Gameplay.Steering.ReadValue<Vector2>().x;
        ebrakeInput = controls.Gameplay.Ebrake.ReadValue<float>() == 1f ? true : false;
    }

    private void BrakeFX()
    {
        bool targetState;
        if (brakeInput > brakeTrailInputThreshold && speed > brakeTrailSpeedThreshold)
        {
            targetState = true;
        }
        else if (speed < brakeTrailSpeedThreshold * 0.5f || !isDrifting)
        {
            targetState = false;
        }
        else
        {
            return;
        }

        if (isBrakeTrailing == targetState)
        {
            return;
        }

        foreach (Transform child in brakeTrails)
        {
            child.gameObject.GetComponent<TrailRenderer>().emitting = targetState;
        }
        isBrakeTrailing = targetState;

    }

    private void Steering()
    {
        foreach (Suspension suspension in suspensions)
        {
            if (suspension.isRearWheel)
            {
                continue;
            }
            // simple, fixed steering
            // suspension.targetWheelAngle = steerInput * maxSteerAngle;

            // dynamic steering
            if (suspension.grip == 1f && throttleInput > 0.5f)
            {
                suspension.targetWheelAngle = steerInput * Mathf.Clamp(maxSteerAngle - speed * 0.4f - 2f, maxSteerAngle * 0.3f, maxSteerAngle);
            }
            else
            {
                suspension.targetWheelAngle = steerInput * Mathf.Clamp(maxSteerAngle - speed * 0.2f - 2f, maxSteerAngle * 0.6f, maxSteerAngle);
            }
            
            suspension.steeringSpeedMulti = Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(40f, 120f, speed));
        }
    }

    void FixedUpdate()
    {
        MoveCar();
    }

    private void MoveCar()
    {
        // get velocity, acceleration, speed
        Vector3 globalVel = XZVector(rb.velocity);
        Vector3 localVel = transform.InverseTransformDirection(globalVel);
        acceleration = (localVel - lastVelocity) / Time.fixedDeltaTime;
        moveDir = Mathf.Sign(transform.InverseTransformDirection(globalVel).z);
        speedMs = globalVel.magnitude;
        speed = globalVel.magnitude * 3.6f; // convert m/s -> km/h
        // if (!isMovingForward)
        //     speed *= -1f;
        // yaw rotation velocity
        float yaw = rb.angularVelocity.y;


        isDrifting = false;
        foreach (Suspension suspension in suspensions)
        {
            if (!suspension.isGrounded && suspension.isRearWheel)
                isRearGrounded = false;
            if (!suspension.isGrounded)
            {
                continue;
            }

            // get lateral slip using velocity at wheel
            Vector3 wheelVel = XZVector(rb.GetPointVelocity(suspension.hitPoint));
            // wheelVel ~= car velocity * 0.25 (ROUGH APPROXIMATE)
            Vector3 slip = Vector3.Project(wheelVel, suspension.transform.right);
            float grip = maxGrip;

            // less grip if off-road
            if (suspension.isOnTerrain)
            {
                grip *= terrainGripMulti;
            }

            // ebrake
            if (ebrakeInput && suspension.isRearWheel)
            {
                // lose grip
                grip = Mathf.Lerp(0.4f, 1, Mathf.Clamp01(wheelVel.magnitude / 40f));
            }

            // drifting
            float driftAngle = Mathf.Abs(Mathf.Atan2(localVel.x, localVel.z));
            /* ^^^
            drift angle is pretty much just slip angle
            its being calculated for entire car, but maybe should be calculated per wheel using wheel vel instead
            */

            // no drift if reversing ( for now)
            // TODO: front tires can drift while reveerse (if this is realistic for RWD)
            if (engine.isReversing)
                driftAngle = 0f;

            float adjustedDriftThreshold = driftThreshold;
            adjustedDriftThreshold -= engine.gearIndex * 0.05f; // easier to slip in fast speed
            if (Mathf.Abs(yaw) > 1f)
                adjustedDriftThreshold *= 0.5f;

            Debug.Log(driftAngle + " /// " + adjustedDriftThreshold);
            if (driftAngle > adjustedDriftThreshold) // if drifting
            {
                float driftFactor = Mathf.Clamp01(driftAngle * 3.6f - driftThreshold);

                if (suspension.isRearWheel)
                    grip *= Mathf.Lerp(1f, rearGrip, driftFactor); // big slip
                else
                    grip *= Mathf.Lerp(1f, frontGrip, driftFactor); // small slip for front

                // regain grip
                if (Mathf.Abs(yaw) < 0.5f)
                    grip += ((0.5f - Mathf.Abs(yaw)) / 0.5f) * 0.15f;
            }

            if (speed < 15f)
            {
                grip = 1f;
            }

            if (grip < 1f)
            {
                isDrifting = true;
            }

            suspension.grip = grip;

            // apply forces

            float yawBoost = Mathf.Clamp01((driftAngle - driftThreshold) / 0.5f);
            float finalDriftMulti = Mathf.Lerp(1f, driftMulti, yawBoost);


            // Vector3 Ftraction = throttleInput * engineForce * finalDriftMulti * transform.forward.normalized;
            Vector3 Ftraction = (engine.wheelTorque / (2f * suspension.wheelRadius)) * finalDriftMulti * transform.forward.normalized;
            if (!suspension.isRearWheel) // RWD
                Ftraction = Vector3.zero;
            if (suspension.isRearWheel && ebrakeInput)
                Ftraction = Vector3.zero;

            // TODO: include weight transfer in these calcullations instead of a hardcoded max force (see marco monster's paper!)
            float longSlip = 0f;
            float maxForce = rb.mass * 9.8f * 0.5f * 0.5f * 1.45f;
            if (grip == 1f && Ftraction.magnitude > maxForce)
            {
                longSlip = Mathf.Pow((Ftraction.magnitude / maxForce) - 1f, 2f) - 0.004f;
                longSlip = Mathf.Clamp01(longSlip);
            }
            // Debug.Log("SLIP: " + longSlip + " ; MAG: " + Ftraction.magnitude);
            suspension.longSlip = longSlip;
            Ftraction *= 1f - longSlip;

            Vector3 Fbrake = suspension.transform.forward * brakeInput * brakeForce * -moveDir;
            if (suspension.isRearWheel) // only front brakes
                Fbrake = Vector3.zero;
            // later change braking to be smth like a 70/30 split between front/back wheels

            Vector3 Flateral = -1f * rb.mass * slip * grip; // lateral force on wheel
            Vector3 Fdrag = -1f * const_drag * globalVel * globalVel.magnitude;
            Vector3 Frr = -1f * const_rr * globalVel.normalized;

            if (speed < 25 && throttleInput == 0f)
                Frr *= 3f;

            // rb.AddForceAtPosition(Ftraction + Flateral + Fdrag + Frr, suspension.hitPoint); // old
            rb.AddForceAtPosition(Ftraction, suspension.hitPoint);
            rb.AddForceAtPosition(Fbrake, suspension.hitPoint);
            rb.AddForceAtPosition(Flateral, suspension.hitPoint);
            // rb.AddForceAtPosition(Fdrag, suspension.hitPoint);
            rb.AddForce(Fdrag); // this shhould prolly be applied once not once per wheel 
            rb.AddForceAtPosition(Frr, suspension.hitPoint);

            // Debug.Log(slip);
        }

        lastVelocity = localVel;

    }

    private Vector3 XZVector(Vector3 vector)
    {
        return new Vector3(vector.x, 0f, vector.z);
    }
    void OnDrawGizmos()
    {
        return;
        
        // get velocity, acceleration, speed
        Vector3 globalVel = XZVector(rb.velocity);
        Vector3 localVel = transform.InverseTransformDirection(globalVel);

        Gizmos.color = Color.red;
        Gizmos.DrawRay(transform.position, transform.forward * globalVel.magnitude);
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, globalVel);
        // Gizmos.DrawLine(suspension.hitPoint, Ftraction);
        // Gizmos.color = Color.green;
        // Gizmos.DrawLine(suspension.hitPoint, Ftraction2);
        // Gizmos.color = Color.blue;
        // Gizmos.DrawLine(suspension.hitPoint, Flateral);
    }
        
}


