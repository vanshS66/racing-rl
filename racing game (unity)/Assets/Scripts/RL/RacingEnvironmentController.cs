using System;
using UnityEngine;

// RL environment controller
public sealed class RacingEnvironmentController : MonoBehaviour
{
    [Serializable]
    public struct RaySensor
    {
        public Vector3 localOrigin;
        public float yawDegrees;
    }

    [Serializable]
    public struct Observation
    {
        public float[] rayDistances;
        public float forwardSpeed;
        public float lateralSpeed;
        public float slipAngle;
        public float yawRate;
        public float progress;

        public float[] ToFlatArray()
        {
            int rayCount = rayDistances == null ? 0 : rayDistances.Length;
            float[] values = new float[rayCount + 5];
            if (rayCount > 0)
                Array.Copy(rayDistances, values, rayCount);

            values[rayCount] = forwardSpeed;
            values[rayCount + 1] = lateralSpeed;
            values[rayCount + 2] = slipAngle;
            values[rayCount + 3] = yawRate;
            values[rayCount + 4] = progress;
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
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = -70f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = -40f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = -15f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = 0f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = 15f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = 40f },
        new RaySensor { localOrigin = new Vector3(0f, 0.5f, 0.6f), yawDegrees = 70f },
    };
    [SerializeField, Min(0.1f)] private float rayLength = 30f;
    [SerializeField] private LayerMask raycastMask = ~0;

    [Header("Episode")]
    [SerializeField, Min(1)] private int maxDecisionSteps = 1_000;
    [SerializeField] private float minimumWorldY = -5f;
    [SerializeField, Range(1f, 180f)] private float maximumUprightAngle = 75f;

    [Header("Checkpoints")]
    [SerializeField, Min(0)] private int checkpointCount;
    [SerializeField] private float checkpointReward = 1f;
    [SerializeField] private float lapCompletionReward = 10f;
    [SerializeField] private float stepPenalty = -0.001f;
    [SerializeField] private float failurePenalty = -5f;

    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private bool hasRuntimeSpawnPose;
    private Vector3 runtimeSpawnPosition;
    private Quaternion runtimeSpawnRotation;
    private int decisionStep;
    private int nextCheckpointIndex;
    private int pendingCheckpointPasses;
    private bool completedLap;

    public Car Car => car;
    public int RayCount => raySensors == null ? 0 : raySensors.Length;
    public int ObservationSize => RayCount + 5;
    public int DecisionStep => decisionStep;

    private void Awake()
    {
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
        nextCheckpointIndex = 0;
        pendingCheckpointPasses = 0;
        completedLap = false;
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
        float[] distances = new float[RayCount];

        for (int i = 0; i < RayCount; i++)
        {
            RaySensor sensor = raySensors[i];
            Vector3 origin = car.transform.TransformPoint(sensor.localOrigin);
            Vector3 direction = Quaternion.AngleAxis(sensor.yawDegrees, car.transform.up) * car.transform.forward;
            bool hit = Physics.Raycast(origin, direction, out RaycastHit raycastHit, rayLength, raycastMask, QueryTriggerInteraction.Ignore);
            distances[i] = hit ? Mathf.Clamp01(raycastHit.distance / rayLength) : 1f;
        }

        return new Observation
        {
            rayDistances = distances,
            forwardSpeed = Mathf.Clamp(localVelocity.z, -50f, 50f),
            lateralSpeed = Mathf.Clamp(localVelocity.x, -50f, 50f),
            slipAngle = Mathf.Clamp(slipAngle, -Mathf.PI, Mathf.PI),
            yawRate = Mathf.Clamp(body.angularVelocity.y, -20f, 20f),
            progress = checkpointCount > 0 ? (float)nextCheckpointIndex / checkpointCount : 0f,
        };
    }

    // get reward and terminal state after one RL step
    public EpisodeTransition CompleteDecisionStep()
    {
        decisionStep++;
        EpisodeTransition transition = new EpisodeTransition
        {
            reward = stepPenalty + pendingCheckpointPasses * checkpointReward,
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
        else if (decisionStep >= maxDecisionSteps)
        {
            transition.truncated = true;
            transition.reason = "decision_limit";
        }

        return transition;
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
    }

    public bool OwnsCar(Car candidate)
    {
        return candidate != null && candidate == car;
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
            Vector3 direction = Quaternion.AngleAxis(sensor.yawDegrees, car.transform.up) * car.transform.forward;
            Gizmos.DrawRay(origin, direction * rayLength);
        }
    }
}
