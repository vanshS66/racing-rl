using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Grpc.Core;
using RacingRl.V1;
using UnityEngine;

// gRPC server for the local training process
public sealed class RacingGrpcServer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RacingEnvironmentController environment;

    [Header("Server")]
    [SerializeField, Min(1)] private int port = 50051;

    private readonly ConcurrentQueue<System.Action> mainThreadWork = new ConcurrentQueue<System.Action>();
    private Server server;

    public bool IsRunning => server != null;

    private void Awake()
    {
        if (environment == null)
            environment = GetComponent<RacingEnvironmentController>();
    }

    private void Start()
    {
        if (environment == null)
        {
            Debug.LogError("RacingGrpcServer needs a RacingEnvironmentController reference.", this);
            enabled = false;
            return;
        }

        RacingGrpcService service = new RacingGrpcService(environment.ObservationSize);
        server = new Server
        {
            Services = { RacingEnvironment.BindService(service) },
            Ports = { new ServerPort("127.0.0.1", port, ServerCredentials.Insecure) }
        };

        server.Start();
        Debug.Log("Racing gRPC server listening on 127.0.0.1:" + port, this);
    }

    private void Update()
    {
        while (mainThreadWork.TryDequeue(out System.Action work))
            work();
    }

    private async void OnDestroy()
    {
        if (server == null)
            return;

        await server.ShutdownAsync();
        server = null;
    }

    // run Unity work from an RPC handler
    public Task<T> RunOnMainThread<T>(Func<T> work)
    {
        TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        mainThreadWork.Enqueue(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }
}

// service implementation kept free of Unity API calls
public sealed class RacingGrpcService : RacingEnvironment.RacingEnvironmentBase
{
    private const string ServiceVersion = "0.1.0";

    private readonly uint observationSize;

    public RacingGrpcService(int observationSize)
    {
        this.observationSize = (uint)observationSize;
    }

    public override Task<HealthResponse> Health(HealthRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HealthResponse
        {
            ServiceVersion = ServiceVersion,
            ObservationSize = observationSize,
            ActionSize = 3
        });
    }
}
