using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class RacingCheckpoint : MonoBehaviour
{
    [SerializeField] private RacingEnvironmentController environment;
    [SerializeField, Min(0)] private int checkpointIndex;

    private void Reset()
    {
        Collider triggerCollider = GetComponent<Collider>();
        triggerCollider.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (environment == null)
            return;

        Car car = other.GetComponentInParent<Car>();
        if (environment.OwnsCar(car))
            environment.NotifyCheckpointPassed(checkpointIndex);
    }
}
