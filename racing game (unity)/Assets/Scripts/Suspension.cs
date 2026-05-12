using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class Suspension : MonoBehaviour
{
    [Header("Suspension")]
    public float restLength;
    public float springTravel;
    public float springStiffness;
    public float damperStiffness;
    [Range(0, 1f)]public float lerpTime;

    private float minLength;
    private float maxLength;
    private float lastLength;
    public float springLength;
    private float springForce;
    private float damperForce;
    private float springVelocity;
    private Vector3 suspensionForce;

    public Vector3 hitPoint;
    public Vector3 hitNormal;


    [Header("Wheels")]
    public GameObject wheelPrefab;
    private Transform wheelObj;
    public bool isRearWheel = false;
    public bool isLeftWheel = false;
    private float wheelAngle;
    public float targetWheelAngle;
    public float steeringSpeed;
    public float steeringSpeedMulti = 1f;
    public float wheelRotationMulti; // for visual rotation
    public float grip; // updated by car script
    public float longSlip; // longitudinal slip, from car script
    
    [Header("VFX & SFX")]
    private Skidmarks skidmarkScript;
    public int lastSkid = -1;
    public GameObject smokeParticlesPrefab;
    private ParticleSystem smokeParticles;
    public GameObject grassParticlesPrefab;
    private ParticleSystem grassParticles;
    private float tireHeat = 0f;
    private StudioEventEmitter tireEmitter;
    public float tireAudioVolume = 0f;

    [Header("Wheel")]
    public float wheelRadius;

    private Rigidbody rb;
    public bool isGrounded { get; private set; }
    public bool isOnTerrain { get; private set; }
    private Car carScript;
    private float lastFixedUpdateTime;

    void Start()
    {
        rb = transform.root.GetComponent<Rigidbody>();
        wheelObj = Instantiate(wheelPrefab, transform).transform;
        carScript = transform.root.GetComponent<Car>();
        skidmarkScript = Skidmarks.Instance;
        smokeParticles = Instantiate(smokeParticlesPrefab, transform).GetComponent<ParticleSystem>();
        grassParticles = Instantiate(grassParticlesPrefab, transform).GetComponent<ParticleSystem>();
        tireEmitter = GetComponent<FMODUnity.StudioEventEmitter>();

        minLength = restLength - springTravel;
        maxLength = restLength;// + springTravel;

        lastFixedUpdateTime = Time.time;
    }

    void Update()
    {
        if (!isRearWheel)
        {
            // transition smoothly from current wheel angle to target angle
            wheelAngle = Mathf.Lerp(wheelAngle, targetWheelAngle, steeringSpeed * steeringSpeedMulti * Time.fixedDeltaTime);
            transform.localRotation = Quaternion.Euler(transform.up * wheelAngle);
        }

        // move wheel with suspension
        wheelObj.position = transform.position + -1 * transform.up * springLength;
        smokeParticles.transform.position = wheelObj.position - new Vector3(0, wheelRadius, 0);

        if (!isGrounded)
        {
            grip = 0f;
            longSlip = 0f;
        }
        // spin wheel (visual)
        if (isRearWheel && carScript.ebrakeInput)
            return;
        float slipRotateSpeed = 0f;
        if (isRearWheel)
            slipRotateSpeed = Mathf.Lerp(0, carScript.speed, carScript.throttleInput * 100f * (1 - grip));

        float dirMulti = carScript.moveDir; // reverse spin dir if moving backward
        if (!isLeftWheel)
            dirMulti *= -1f; // right wheels turn proper dir
        wheelObj.Rotate(0, 0, (carScript.speed + slipRotateSpeed) * wheelRotationMulti * dirMulti * Time.deltaTime, Space.Self);
        // TODO: use math to calculate actual wheel rotate speed when full gripped and have some multiplier when drifting/slipping
    }

    void FixedUpdate()
    {
        // suspension physics
        isGrounded = false;
        if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, maxLength + wheelRadius))
        {
            lastLength = springLength;

            springLength = Mathf.Lerp(springLength, hit.distance - wheelRadius, lerpTime); // testing / temp
            springLength = Mathf.Clamp(springLength, minLength, maxLength);
            springVelocity = (lastLength - springLength) / Time.fixedDeltaTime;
            springForce = springStiffness * (restLength - springLength);
            damperForce = damperStiffness * springVelocity;

            suspensionForce = (springForce + damperForce) * transform.up;

            rb.AddForceAtPosition(suspensionForce, hit.point);

            isOnTerrain = hit.collider.gameObject.CompareTag("Terrain");
            isGrounded = true;
            hitPoint = hit.point;
            hitNormal = hit.normal;
        }


        lastFixedUpdateTime = Time.time;
    }

    void LateUpdate()
    {
        // vfx and sfx
        
        var smokeEmission = smokeParticles.emission;
        tireHeat = Mathf.Clamp(tireHeat, 0f, 5f);

        // airborne -> 0 grip
        if ((grip == 1f || grip == 0f || carScript.speed < 15f) && longSlip == 0f)
        {
            // NO FX

            lastSkid = -1;
            smokeEmission.enabled = false;

            tireHeat -= Time.deltaTime;
            if (carScript.speed < 20 || grip == 0f) // cool down faster
                tireHeat -= Time.deltaTime * 2f;

            tireAudioVolume *= 0.1f * Time.deltaTime;
            if (grip == 0f)
                tireAudioVolume = 0f;
            tireEmitter.SetParameter("tire volume", tireAudioVolume);
            return;
        }

        if (isOnTerrain)
        {
            // grass SFX and VFX on terrain
            grassParticles.Play();
            return;
        }
        grassParticles.Stop();

        tireHeat += Time.deltaTime;

        // tire screech
        float fxMulti = Mathf.Max(1 - grip, longSlip * 2f);
        tireAudioVolume = fxMulti * 0.4f;
        tireEmitter.SetParameter("tire volume", tireAudioVolume);

        // skidmarks
        if (grip < 1f) // dont skid if only long slipping
        {
            lastSkid = skidmarkScript.AddSkidMark(hitPoint + rb.velocity * Time.fixedDeltaTime, hitNormal, 1f, lastSkid);
        }
        // lastSkid = skidmarkScript.AddSkidMark(hitPoint + (rb.velocity * (Time.time - lastFixedUpdateTime)), hitNormal, 1f, lastSkid);
        // lastSkid = skidmarkScript.AddSkidMark(hitPoint, hitNormal, grip + 0.3f, lastSkid);

        if (tireHeat > 2) // min tire heat to start smokin
        {
            // smoke
            smokeEmission.enabled = true;
            smokeEmission.rateOverTime = Mathf.Clamp(fxMulti * 50f * tireHeat, 0, 100);
        } 
    }

    // reset runtime suspension state
    public void ResetForEpisode()
    {
        springLength = restLength;
        lastLength = restLength;
        springForce = 0f;
        damperForce = 0f;
        springVelocity = 0f;
        suspensionForce = Vector3.zero;
        hitPoint = Vector3.zero;
        hitNormal = Vector3.up;
        wheelAngle = 0f;
        targetWheelAngle = 0f;
        steeringSpeedMulti = 1f;
        grip = 0f;
        longSlip = 0f;
        isGrounded = false;
        isOnTerrain = false;
        lastSkid = -1;
        tireHeat = 0f;
        tireAudioVolume = 0f;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + transform.up * -springLength);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.up * -springLength, wheelRadius);
    }

}
