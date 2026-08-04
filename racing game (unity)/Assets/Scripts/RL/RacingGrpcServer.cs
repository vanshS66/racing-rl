using System;
using System.Collections;
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
    private bool decisionPending;

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

        RacingGrpcService service = new RacingGrpcService(this, environment.ObservationSize);
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

    // reset the environment from an RPC handler
    public Task<RacingEnvironmentController.Observation> ResetAsync(ulong seed)
    {
        return RunOnMainThread(() =>
        {
            if (decisionPending)
                throw new InvalidOperationException("Cannot reset while a decision is running.");

            environment.ResetEpisode(seed);
            return environment.BuildObservation();
        });
    }

    // run one physics decision from an RPC handler
    public Task<DecisionResult> StepAsync(float steering, float throttle, float brake)
    {
        TaskCompletionSource<DecisionResult> completion = new TaskCompletionSource<DecisionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        mainThreadWork.Enqueue(() =>
        {
            if (decisionPending)
            {
                completion.SetException(new InvalidOperationException("Only one decision can run at a time."));
                return;
            }

            decisionPending = true;
            environment.SetAction(steering, throttle, brake);
            StartCoroutine(CompleteStepAfterPhysics(completion));
        });
        return completion.Task;
    }

    private IEnumerator CompleteStepAfterPhysics(TaskCompletionSource<DecisionResult> completion)
    {
        yield return new WaitForFixedUpdate();

        try
        {
            RacingEnvironmentController.EpisodeTransition transition = environment.CompleteDecisionStep();
            completion.SetResult(new DecisionResult
            {
                observation = environment.BuildObservation(),
                transition = transition,
                decisionStep = environment.DecisionStep,
            });
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
        finally
        {
            decisionPending = false;
        }
    }

    public struct DecisionResult
    {
        public RacingEnvironmentController.Observation observation;
        public RacingEnvironmentController.EpisodeTransition transition;
        public int decisionStep;
    }
}

// service implementation queues Unity work through RacingGrpcServer
public sealed class RacingGrpcService : RacingEnvironment.RacingEnvironmentBase
{
    private const string ServiceVersion = "0.1.0";

    private readonly RacingGrpcServer server;
    private readonly uint observationSize;

    public RacingGrpcService(RacingGrpcServer server, int observationSize)
    {
        this.server = server;
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

    public override async Task<ResetResponse> Reset(ResetRequest request, ServerCallContext context)
    {
        try
        {
            RacingEnvironmentController.Observation observation = await server.ResetAsync(request.Seed);
            ResetResponse response = new ResetResponse
            {
                Observation = ToProtoObservation(observation)
            };
            response.Info["seed"] = request.Seed.ToString();
            return response;
        }
        catch (InvalidOperationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<StepResponse> Step(StepRequest request, ServerCallContext context)
    {
        if (request.Action == null)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "StepRequest needs an action."));

        try
        {
            RacingGrpcServer.DecisionResult result = await server.StepAsync(request.Action.Steering, request.Action.Throttle, request.Action.Brake);
            StepResponse response = new StepResponse
            {
                Observation = ToProtoObservation(result.observation),
                Reward = result.transition.reward,
                Terminated = result.transition.terminated,
                Truncated = result.transition.truncated,
            };
            response.Info["reason"] = result.transition.reason;
            response.Info["decision_step"] = result.decisionStep.ToString();
            return response;
        }
        catch (InvalidOperationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private static Observation ToProtoObservation(RacingEnvironmentController.Observation source)
    {
        Observation observation = new Observation
        {
            ForwardSpeed = source.forwardSpeed,
            LateralSpeed = source.lateralSpeed,
            SlipAngle = source.slipAngle,
            YawRate = source.yawRate,
            TargetLateral = source.targetLateral,
            TargetForward = source.targetForward,
            Progress = source.progress,
        };
        observation.RayDistances.Add(source.rayDistances);
        return observation;
    }
}
