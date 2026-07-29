using System;
using UnityEngine;

// RL environment controller
public sealed class RacingEnvironmentController : MonoBehaviour
{
    [Serializable]
    public struct RaySensor
    {
        public Vector3 localOrigin;
    }

    [Serializable]
    public struct Observation
    {
        public float[] rayDistances;
        public float forwardSpeed;
        public float lateralSpeed;
        public float slipAngle;
        public float yawRate;
        public float targetLateral;
        public float targetForward;
        public float progress;

        public float[] ToFlatArray()
        {
            int rayCount = rayDistances == null ? 0 : rayDistances.Length;
            float[] values = new float[rayCount + 7];
            if (rayCount > 0)
                Array.Copy(rayDistances, values, rayCount);

            values[rayCount] = forwardSpeed;
            values[rayCount + 1] = lateralSpeed;
            values[rayCount + 2] = slipAngle;
            values[rayCount + 3] = yawRate;
            values[rayCount + 4] = targetLateral;
            values[rayCount + 5] = targetForward;
            values[rayCount + 6] = progress;
            return values;
        }
    }

    [Serializable]
    public struct EpisodeTransition
    {
        public float reward;
        public bool terminated;
        public bool truncated;
        public string reason;
    }

    [Header("References")]
    [SerializeField] private Car car;
    [SerializeField] private Transform spawnPoint;

    [Header("Rays")]
    [SerializeField] private RaySensor[] raySensors =
    {
        new RaySensor { localOrigin = new Vector3(-3.5f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(-2.25f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(-1f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(0f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(1f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(2.25f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(3.5f, 2f, 0.6f) },
        new RaySensor { localOrigin = new Vector3(-3.5f, 2f, 5f) },
        new RaySensor { localOrigin = new Vector3(-1.75f, 2f, 5f) },
        new RaySensor { localOrigin = new Vector3(0f, 2f, 5f) },
        new RaySensor { localOrigin = new Vector3(1.75f, 2f, 5f) },
        new RaySensor { localOrigin = new Vector3(3.5f, 2f, 5f) },
        new RaySensor { localOrigin = new Vector3(-3.5f, 2f, 10f) },
        new RaySensor { localOrigin = new Vector3(-1.75f, 2f, 10f) },
        new RaySensor { localOrigin = new Vector3(0f, 2f, 10f) },
        new RaySensor { localOrigin = new Vector3(1.75f, 2f, 10f) },
        new RaySensor { localOrigin = new Vector3(3.5f, 2f, 10f) },
    };
    [SerializeField, Min(0.1f)] private float rayLength = 3f;
    [SerializeField, Min(0)] private int centerRoadProbeIndex = 3;

    [Header("Episode")]
    [SerializeField, Min(1)] private int maxDecisionSteps = 1_500;
    [SerializeField] private float minimumWorldY = -5f;
    [SerializeField, Range(1f, 180f)] private float maximumUprightAngle = 75f;
    [SerializeField, Min(0f)] private float stalledSpeedThreshold = 0.5f;
    [SerializeField, Min(1)] private int maxStalledDecisionSteps = 150;

    [Header("Checkpoints")]
    [SerializeField, Min(0)] private int checkpointCount;
    [SerializeField] private float checkpointReward = 1f;
    [SerializeField] private float checkpointDistanceReward = 0.05f;
    [SerializeField] private float lapCompletionReward = 10f;
    [SerializeField] private float stepPenalty = -0.001f;
    [SerializeField] private float failurePenalty = -5f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool hasRuntimeSpawnPose;
    private Vector3 runtimeSpawnPosition;
    private Quaternion runtimeSpawnRotation;
    private int decisionStep;
    private int stalledDecisionSteps;
    private int nextCheckpointIndex;
    private int pendingCheckpointPasses;
    private bool completedLap;
    private int roadSensorMask;
    private Transform[] checkpointTargets;
    private float previousCheckpointDistance;

    public Car Car => car;
    public int RayCount => raySensors == null ? 0 : raySensors.Length;
    public int ObservationSize => RayCount + 7;
    public int DecisionStep => decisionStep;

    private void Awake()
    {
        int roadSensorLayer = LayerMask.NameToLayer("RoadSensor");
        if (roadSensorLayer < 0)
            throw new InvalidOperationException("The RoadSensor layer is required for RL road probes.");

        roadSensorMask = 1 << roadSensorLayer;

        if (car == null)
            car = GetComponent<Car>();

        if (car == null)
        {
            Debug.LogError("RacingEnvironmentController needs a car reference.", this);
            enabled = false;
            return;
        }

        initialPosition = car.transform.position;
        initialRotation = car.transform.rotation;
        CacheCheckpointTargets();
    }

    // set action from RL
    public void SetAction(float steering, float throttle, float brake)
    {
        car.SetRLAction(steering, throttle, brake);
    }

    // reset car and episode state
    public void ResetEpisode(ulong seed = 0, bool enableRLControl = true)
    {
        if (car == null || car.Rigidbody == null)
            throw new InvalidOperationException("Cannot reset before the Car and Rigidbody are available.");

        Transform source = spawnPoint == null ? null : spawnPoint;
        Vector3 position = source != null ? source.position : hasRuntimeSpawnPose ? runtimeSpawnPosition : initialPosition;
        Quaternion rotation = source != null ? source.rotation : hasRuntimeSpawnPose ? runtimeSpawnRotation : initialRotation;
        Rigidbody body = car.Rigidbody;

        body.isKinematic = true;
        body.position = position;
        body.rotation = rotation;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        car.ResetForEpisode();
        Physics.SyncTransforms();
        body.isKinematic = false;

        decisionStep = 0;
        stalledDecisionSteps = 0;
        nextCheckpointIndex = 0;
        pendingCheckpointPasses = 0;
        completedLap = false;
        previousCheckpointDistance = GetNextCheckpointDistance();
        if (enableRLControl)
            SetAction(0f, 0f, 0f);
        else
            car.UsePlayerInput();
    }

    // get car state for RL
    public Observation BuildObservation()
    {
        if (car == null || car.Rigidbody == null)
            throw new InvalidOperationException("Cannot build an observation without a Car and Rigidbody.");

        Rigidbody body = car.Rigidbody;
        Vector3 localVelocity = car.transform.InverseTransformDirection(body.velocity);
        float planarMagnitude = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        float slipAngle = planarMagnitude < 0.001f ? 0f : Mathf.Atan2(localVelocity.x, localVelocity.z);
        Vector2 targetDirection = GetNextCheckpointDirection();
        float[] distances = new float[RayCount];

        for (int i = 0; i < RayCount; i++)
        {
            RaySensor sensor = raySensors[i];
            Vector3 origin = car.transform.TransformPoint(sensor.localOrigin);
            bool hit = Physics.Raycast(origin, -car.transform.up, rayLength, roadSensorMask, QueryTriggerInteraction.Ignore);
            distances[i] = hit ? 1f : 0f;
        }

        return new Observation
        {
            rayDistances = distances,
            forwardSpeed = Mathf.Clamp(localVelocity.z, -50f, 50f),
            lateralSpeed = Mathf.Clamp(localVelocity.x, -50f, 50f),
            slipAngle = Mathf.Clamp(slipAngle, -Mathf.PI, Mathf.PI),
            yawRate = Mathf.Clamp(body.angularVelocity.y, -20f, 20f),
            targetLateral = targetDirection.x,
            targetForward = targetDirection.y,
            progress = checkpointCount > 0 ? (float)nextCheckpointIndex / checkpointCount : 0f,
        };
    }

    // get reward and terminal state after one RL step
    public EpisodeTransition CompleteDecisionStep()
    {
        decisionStep++;
        if (GetForwardSpeed() < stalledSpeedThreshold)
            stalledDecisionSteps++;
        else
            stalledDecisionSteps = 0;

        EpisodeTransition transition = new EpisodeTransition
        {
            reward = stepPenalty + pendingCheckpointPasses * checkpointReward + GetCheckpointDistanceReward(),
            reason = string.Empty,
        };
        pendingCheckpointPasses = 0;

        if (completedLap)
        {
            transition.reward += lapCompletionReward;
            transition.terminated = true;
            transition.reason = "lap_complete";
        }
        else if (car.transform.position.y < minimumWorldY)
        {
            transition.reward += failurePenalty;
            transition.terminated = true;
            transition.reason = "below_world";
        }
        else if (Vector3.Angle(car.transform.up, Vector3.up) > maximumUprightAngle)
        {
            transition.reward += failurePenalty;
            transition.terminated = true;
            transition.reason = "rolled_over";
        }
        else if (!IsCenterRoadProbeHit())
        {
            transition.reward += failurePenalty;
            transition.terminated = true;
            transition.reason = "off_road";
        }
        else if (stalledDecisionSteps >= maxStalledDecisionSteps)
        {
            transition.reward += failurePenalty;
            transition.terminated = true;
            transition.reason = "stalled";
        }
        else if (decisionStep >= maxDecisionSteps)
        {
            transition.truncated = true;
            transition.reason = "decision_limit";
        }

        return transition;
    }

    private bool IsCenterRoadProbeHit()
    {
        if (centerRoadProbeIndex < 0 || centerRoadProbeIndex >= RayCount)
            return true;

        RaySensor sensor = raySensors[centerRoadProbeIndex];
        Vector3 origin = car.transform.TransformPoint(sensor.localOrigin);
        return Physics.Raycast(origin, -car.transform.up, rayLength, roadSensorMask, QueryTriggerInteraction.Ignore);
    }

    private float GetForwardSpeed()
    {
        Vector3 localVelocity = car.transform.InverseTransformDirection(car.Rigidbody.velocity);
        return localVelocity.z;
    }

    // checkpoint trigger calls this
    public void NotifyCheckpointPassed(int checkpointIndex)
    {
        if (checkpointCount <= 0 || checkpointIndex != nextCheckpointIndex)
            return;

        pendingCheckpointPasses++;
        nextCheckpointIndex = (nextCheckpointIndex + 1) % checkpointCount;
        if (nextCheckpointIndex == 0)
            completedLap = true;
        previousCheckpointDistance = GetNextCheckpointDistance();
    }

    public bool OwnsCar(Car candidate)
    {
        return candidate != null && candidate == car;
    }

    private void CacheCheckpointTargets()
    {
        if (checkpointCount == 0)
        {
            checkpointTargets = Array.Empty<Transform>();
            return;
        }

        checkpointTargets = new Transform[checkpointCount];
        RacingCheckpoint[] sceneCheckpoints = FindObjectsOfType<RacingCheckpoint>();
        foreach (RacingCheckpoint checkpoint in sceneCheckpoints)
        {
            if (!checkpoint.BelongsTo(this))
                continue;

            int index = checkpoint.CheckpointIndex;
            if (index < 0 || index >= checkpointCount || checkpointTargets[index] != null)
                throw new InvalidOperationException("Racing checkpoints need one unique index from 0 to " + (checkpointCount - 1) + ".");

            checkpointTargets[index] = checkpoint.transform;
        }

        for (int i = 0; i < checkpointTargets.Length; i++)
        {
            if (checkpointTargets[i] == null)
                throw new InvalidOperationException("Missing RacingCheckpoint with index " + i + ".");
        }
    }

    private float GetNextCheckpointDistance()
    {
        if (checkpointCount == 0)
            return 0f;

        Vector3 offset = checkpointTargets[nextCheckpointIndex].position - car.transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private Vector2 GetNextCheckpointDirection()
    {
        if (checkpointCount == 0)
            return Vector2.zero;

        Vector3 offset = checkpointTargets[nextCheckpointIndex].position - car.transform.position;
        offset.y = 0f;
        Vector3 localOffset = car.transform.InverseTransformDirection(offset);
        Vector2 direction = new Vector2(localOffset.x, localOffset.z);
        return direction.sqrMagnitude < 0.001f ? Vector2.zero : direction.normalized;
    }

    private float GetCheckpointDistanceReward()
    {
        if (checkpointCount == 0)
            return 0f;

        float currentDistance = GetNextCheckpointDistance();
        float distanceChange = previousCheckpointDistance - currentDistance;
        previousCheckpointDistance = currentDistance;
        return Mathf.Clamp(distanceChange, -1f, 1f) * checkpointDistanceReward;
    }

    // set reset point if no spawn transform is assigned
    public void SetRuntimeSpawnPose(Vector3 position, Quaternion rotation)
    {
        runtimeSpawnPosition = position;
        runtimeSpawnRotation = rotation;
        hasRuntimeSpawnPose = true;
    }

    [ContextMenu("RL/Reset Episode")]
    private void ResetEpisodeFromContextMenu()
    {
        ResetEpisode();
    }

    [ContextMenu("RL/Log Observation")]
    private void LogObservationFromContextMenu()
    {
        Observation observation = BuildObservation();
        Debug.Log($"RL observation ({ObservationSize} values): [{string.Join(", ", observation.ToFlatArray())}]", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (car == null || raySensors == null)
            return;

        Gizmos.color = Color.cyan;
        foreach (RaySensor sensor in raySensors)
        {
            Vector3 origin = car.transform.TransformPoint(sensor.localOrigin);
            Gizmos.DrawRay(origin, -car.transform.up * rayLength);
        }
    }
}
