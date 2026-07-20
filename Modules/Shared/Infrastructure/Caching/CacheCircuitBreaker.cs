namespace Kuvox.Api.Modules.Shared.Infrastructure.Caching;

public sealed class CacheCircuitBreaker(ICacheClock clock)
{
    private const int FailureThreshold = 5;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private int _failures;
    private long _openedAt;
    private CircuitState _state;
    private bool _halfOpenInFlight;

    public string State
    {
        get
        {
            lock (_gate)
            {
                return _state.ToString().ToLowerInvariant();
            }
        }
    }

    public bool AllowRequest()
    {
        lock (_gate)
        {
            if (_state == CircuitState.Closed)
            {
                return true;
            }

            if (_state == CircuitState.Open)
            {
                if (clock.GetElapsedTime(_openedAt) < OpenDuration)
                {
                    return false;
                }

                SetState(CircuitState.HalfOpen);
            }

            if (_halfOpenInFlight)
            {
                return false;
            }

            _halfOpenInFlight = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failures = 0;
            _halfOpenInFlight = false;
            SetState(CircuitState.Closed);
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _halfOpenInFlight = false;
            _failures++;
            if (_state == CircuitState.HalfOpen || _failures >= FailureThreshold)
            {
                _openedAt = clock.GetTimestamp();
                SetState(CircuitState.Open);
            }
        }
    }

    private void SetState(CircuitState state)
    {
        _state = state;
        CacheMetrics.SetCircuitState((int)state);
    }

    private enum CircuitState
    {
        Closed = 0,
        Open = 1,
        HalfOpen = 2
    }
}
