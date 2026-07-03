using System.Diagnostics;

namespace MovieTimestampNotes.Core;

public interface IMonotonicTimeSource
{
    TimeSpan Elapsed { get; }
}

public sealed class StopwatchTimeSource : IMonotonicTimeSource
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    public TimeSpan Elapsed => _stopwatch.Elapsed;
}

public interface ITimelineClock
{
    TimeSpan Duration { get; }
    TimeSpan Current { get; }
    bool IsRunning { get; }
    void SetDuration(TimeSpan duration);
    void Start();
    void Pause();
    void Toggle();
    void Reset();
    void Calibrate(TimeSpan position);
}

public sealed class TimelineClock : ITimelineClock
{
    private readonly object _gate = new();
    private readonly IMonotonicTimeSource _timeSource;
    private TimeSpan _duration;
    private TimeSpan _basePosition;
    private TimeSpan _startedAt;
    private bool _isRunning;

    public TimelineClock(IMonotonicTimeSource? timeSource = null, TimeSpan? initialDuration = null)
    {
        _timeSource = timeSource ?? new StopwatchTimeSource();
        _duration = initialDuration ?? TimeSpan.FromMinutes(90);
        if (_duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDuration));
        }
    }

    public TimeSpan Duration
    {
        get { lock (_gate) return _duration; }
    }

    public TimeSpan Current
    {
        get
        {
            lock (_gate)
            {
                var current = CalculateCurrent();
                if (current >= _duration)
                {
                    _basePosition = _duration;
                    _isRunning = false;
                    return _duration;
                }

                return current;
            }
        }
    }

    public bool IsRunning
    {
        get { lock (_gate) return _isRunning; }
    }

    public void SetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "电影时长必须大于零。");
        }

        lock (_gate)
        {
            _basePosition = CalculateCurrent();
            _duration = duration;
            if (_basePosition >= _duration)
            {
                _basePosition = _duration;
                _isRunning = false;
            }
            _startedAt = _timeSource.Elapsed;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_isRunning || _basePosition >= _duration) return;
            _startedAt = _timeSource.Elapsed;
            _isRunning = true;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (!_isRunning) return;
            _basePosition = CalculateCurrent();
            if (_basePosition > _duration) _basePosition = _duration;
            _isRunning = false;
        }
    }

    public void Toggle()
    {
        if (IsRunning) Pause(); else Start();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _basePosition = TimeSpan.Zero;
            _startedAt = _timeSource.Elapsed;
            _isRunning = false;
        }
    }

    public void Calibrate(TimeSpan position)
    {
        lock (_gate)
        {
            if (position < TimeSpan.Zero || position > _duration)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "校准时间必须位于电影时长范围内。");
            }

            _basePosition = position;
            _startedAt = _timeSource.Elapsed;
            if (position == _duration) _isRunning = false;
        }
    }

    private TimeSpan CalculateCurrent() =>
        _isRunning ? _basePosition + (_timeSource.Elapsed - _startedAt) : _basePosition;
}
