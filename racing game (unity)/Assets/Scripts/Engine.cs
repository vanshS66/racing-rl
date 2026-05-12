using System;
using System.Collections;
using System.Collections.Generic;
using FMODUnity;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Jobs;

public class Engine : MonoBehaviour
{
    PlayerControls controls;
    private Car carScript;
    private StudioEventEmitter emitter;
    private float wheelRadius;
    private bool useRLThrottle;
    private float rlThrottle;

    [Header("RPMs")]
    public float throttleInput;
    public float currentRPM { get; private set; } // Read-only from outside
    public float engineTorque { get; private set; } // torque directly from engine (rpm -> enginetorque)
    public float wheelTorque { get; private set; } // torque to each wheel (engine -> trans -> diff -> wheeltorque)
    private float minRPM; // get these 2 from rpm torque curve
    private float maxRPM;
    public float rpmIncreaseRate = 3000f; // How fast RPM increases per second at full throttle
    public float rpmDecreaseRate = 2000f; // How fast RPM drops when off throttle
    public RpmTorquePair[] rpmTorqueList; // rpm to torque conversion pairs

    [Header("Gearing")]
    public bool isShifting = false;
    public bool isReversing { get; private set; } = false;
    public int gearIndex { get; private set; } = 0;
    public float currentGearRatio { get; private set; }
    [SerializeField] private float diffRatio = 4.3f; // differential ratio (constant)
    [SerializeField] private float trans_efficiency = 0.7f; 
    [SerializeField] private float gearReverse = 2.5f; // reverse gear ratio
    public float[] gearRatios;



    void Awake()
    {
        controls = new PlayerControls();
        carScript = transform.root.GetComponent<Car>();
        emitter = GetComponent<FMODUnity.StudioEventEmitter>();

        wheelRadius = carScript.suspensions[0].wheelRadius;
        minRPM = rpmTorqueList[0].rpm;
        maxRPM = rpmTorqueList[^1].rpm;

        currentGearRatio = gearRatios[gearIndex];
    }
    void OnEnable()
    {
        controls.Gameplay.Enable();
    }
    void OnDisable()
    {
        controls.Gameplay.Disable();
    }
    void Update()
    {
        Controls();
    }

    private void Controls()
    {
        if (useRLThrottle)
        {
            throttleInput = rlThrottle;
            return;
        }

        float targetThrottle = controls.Gameplay.Throttle.ReadValue<float>();
        throttleInput = Mathf.Lerp(throttleInput, targetThrottle, 3f * Time.deltaTime);

        float reverseInput = controls.Gameplay.Reverse.ReadValue<float>();
        if (reverseInput == 1f)
        {
            ReverseToggle();
        }
    }

    // use throttle from RL
    public void SetRLThrottle(float throttle)
    {
        useRLThrottle = true;
        rlThrottle = Mathf.Clamp01(throttle);
    }

    // use throttle from player controls
    public void UsePlayerInput()
    {
        useRLThrottle = false;
        rlThrottle = 0f;
    }

    // reset runtime drivetrain state
    public void ResetForEpisode()
    {
        StopAllCoroutines();
        isShifting = false;
        isReversing = false;
        gearIndex = 0;
        currentGearRatio = gearRatios != null && gearRatios.Length > 0 ? gearRatios[0] : 0f;
        currentRPM = minRPM;
        engineTorque = 0f;
        wheelTorque = 0f;
        throttleInput = 0f;
        rlThrottle = 0f;
    }

    private void ReverseToggle()
    {
        if (isShifting)
            return;

        float targetRpm;
        if (isReversing) // reverse -> normal
        {
            targetRpm = currentRPM * (gearRatios[gearIndex] / currentGearRatio);
            currentGearRatio = gearRatios[gearIndex];
        }
        else // normal -> reverse
        {
            targetRpm = currentRPM * (gearReverse / currentGearRatio);
            currentGearRatio = gearReverse;
        }


        isShifting = true;
        isReversing = !isReversing;
        gearIndex = 0;
        StartCoroutine(HandleShift(targetRpm));
    }

    private void ShiftGear(int gearChange)
    {
        if (isShifting)
            return;
        if (isReversing)
            return;
        if (currentRPM > maxRPM && gearChange == -1) // cant downshift if overrevving (shouldnt be possible anyways)
                return;

        int targetGear = gearIndex + gearChange;
        if (targetGear < 0 || targetGear > gearRatios.Length - 1)
        {
            return;
        }

        isShifting = true;
        float targetRpm = currentRPM * (gearRatios[targetGear] / currentGearRatio);
        gearIndex = targetGear;
        currentGearRatio = gearRatios[gearIndex];
        StartCoroutine(HandleShift(targetRpm));
    }

    
    private IEnumerator HandleShift(float targetRPM)
    {
        float duration = 0.5f;
        float time = 0f;
        float initialRPM = currentRPM;

        while (time < duration)
        {
            currentRPM = Mathf.Lerp(initialRPM, targetRPM, time / duration);
            time += Time.deltaTime;
            yield return null;
        }

        currentRPM = targetRPM; // ensure it finishes at the exact target
        isShifting = false;
    }

    void FixedUpdate()
    {
        UpdateRPM();
        GearShiftCheck();
        UpdateTorque();
        Audio();

    }

    private void Audio()
    {
        emitter.SetParameter("RPM", currentRPM);
    }

    private void UpdateRPM()
    {
        if (isShifting) // rpm during shift handled by HandleShift
        {
            return;
        }

        float rearGrip = GetRearGrip();

        float slipMulti = 1f + (1 - rearGrip);
        float rpm = currentRPM;

        if (throttleInput > 0f && currentRPM < minRPM + (maxRPM - minRPM) * throttleInput) // this works just trust me
        {

            rpm += throttleInput * rpmIncreaseRate * currentGearRatio * (0.75f * slipMulti) * Time.fixedDeltaTime;
        }
        else
        {
            rpm -= rpmDecreaseRate * slipMulti * Time.fixedDeltaTime;
        }

        float realRPM = (carScript.speedMs / wheelRadius) * currentGearRatio * diffRatio * (60f / (2f * (float)Math.PI));
        if (realRPM < minRPM)
            realRPM = minRPM;

        // this is basically for when you regain grip and your rpm is crazy high
        if (realRPM > 2000f && rpm > realRPM + 500f && rearGrip == 1f)
        {
            rpm = Mathf.Lerp(rpm, realRPM, 0.5f * Time.deltaTime);
        }

        if (rpm < minRPM && gearIndex == 0)
        {
            rpm = minRPM;
        }

        currentRPM = rpm;
    }

    private void GearShiftCheck()
    {
        float rearGrip = GetRearGrip();

        if (currentRPM < minRPM + (1000f * gearIndex)) // && rearGrip == 1f)
        {
            // downshifting
            ShiftGear(-1);
        }
        else if (carScript.speed < GetMinCarSpeed(currentGearRatio) && currentRPM < 0.65f * maxRPM)
        {
            // ^ downshift if car speed way too low for current gear
            // but dont downshift if high rpms (will overrev)
            ShiftGear(-1);
        }
        else if (currentRPM > maxRPM)
        {
            // upshifting
            float minUpshiftSpeed = GetMaxCarSpeed((maxRPM - minRPM) / 2, currentGearRatio);
            if (rearGrip >= 0.8f && carScript.speed > minUpshiftSpeed)
            {
                // ^upshift if not drifting and if speed is not too low (will get stuck revving with no torque)
                ShiftGear(1);
            }
        }
    }

    private void UpdateTorque()
    {
        if (carScript.speed > GetMaxCarSpeed(currentRPM, currentGearRatio))
        {
            wheelTorque = 0f;
            return;
        }

        if (isShifting)
        {
            wheelTorque = 0f;
            return;
        }

        for (int i = 0; i < rpmTorqueList.Length - 1; i++)
            {
                var lower = rpmTorqueList[i];
                var upper = rpmTorqueList[i + 1];

                if (currentRPM >= lower.rpm && currentRPM < upper.rpm)
                {
                    float t = (currentRPM - lower.rpm) / (float)(upper.rpm - lower.rpm);
                    engineTorque = Mathf.Lerp(lower.torque, upper.torque, t);
                    engineTorque = FtLbsToNm(engineTorque);
                    engineTorque *= throttleInput;

                    wheelTorque = engineTorque * currentGearRatio * diffRatio * trans_efficiency;
                    if (isReversing)
                        wheelTorque *= -1f;
                    // wheelTorque = engineTorque * 3f * diffRatio * trans_efficiency;

                    break;
                }
            }

    }

    public float GetLongitudinalSlip()
    {
        /*
        Return rear tire slip in the forward-backward (z) direction
        */

        float realRPM = (carScript.speedMs / wheelRadius) * currentGearRatio * diffRatio * (60f / (2f * (float)Math.PI));
        if (realRPM < minRPM)
            realRPM = minRPM;
            
        float rpmDifference = currentRPM - realRPM;

        float slip = rpmDifference / (maxRPM - minRPM);
        slip = Mathf.Clamp01(slip);

        return slip;
    }

    private float GetRearGrip()
    {
        float rearGrip = 0f;
        foreach (Suspension suspension in carScript.suspensions)
        {
            if (suspension.isRearWheel && suspension.grip > rearGrip)
                rearGrip = suspension.grip;
        }
        return rearGrip;
    }

    private float GetMaxCarSpeed(float rpm, float gearRatio)
    {
        /*
        Returns max car speed in km/h based on gear ratio and rpm 
        */

        float maxSpeed = rpm / (gearRatio * diffRatio);
        maxSpeed *= 2f * (float)Math.PI * wheelRadius / 60f;
        maxSpeed *= 3.6f; // to km/h

        return maxSpeed;
    }

    private float GetMinCarSpeed(float gearRatio)
    {
        /*
        Returns minimum speed (km/h) that car should be going in this gear
        
        min speed is higher if not drifting
        */

        float minSpeed = (minRPM / maxRPM) * GetMaxCarSpeed(maxRPM, gearRatio);
        if (GetRearGrip() == 1f)
        {
            minSpeed *= 2f;
        }

        return minSpeed;
    }

    private float FtLbsToNm(float FtLbs)
    {
        /*
        Converts torque in Ft/Lbs to torque in N/m
        */
        return FtLbs * 1.3556f;
    }
    
    [System.Serializable]
    public struct RpmTorquePair
    {
        public int rpm;
        public int torque;
    }

}
