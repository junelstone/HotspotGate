namespace StonesHotspot.Network.Gate;

/// <summary>
/// Holds shaped packets until their release time and hands them back in time order on one thread. Bounded: when
/// full, new packets are refused (the caller drops them, like a full router queue).
/// </summary>
public sealed class PacketDelayLine<T> : IDisposable
{
    public const int DefaultCapacity = 20_000;

    private readonly PriorityQueue<T, long> _queue = new();
    private readonly object _lock = new();
    private readonly Action<T> _release;
    private readonly TimeProvider _clock;
    private readonly int _capacity;
    private readonly Thread _thread;
    private bool _stopping;

    public PacketDelayLine(Action<T> release, TimeProvider clock, string name, int capacity = DefaultCapacity)
    {
        _release = release;
        _clock = clock;
        _capacity = capacity;
        _thread = new Thread(Run) { Name = name, IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _queue.Count;
        }
    }

    /// <summary>Queues <paramref name="item"/> for release after <paramref name="delay"/>. False when full or stopping.</summary>
    public bool Enqueue(T item, TimeSpan delay)
    {
        var due = _clock.GetUtcNow().UtcTicks + Math.Max(0, delay.Ticks);
        lock (_lock)
        {
            if (_stopping || _queue.Count >= _capacity)
                return false;
            _queue.Enqueue(item, due);
            Monitor.Pulse(_lock);
            return true;
        }
    }

    private void Run()
    {
        while (true)
        {
            T item;
            lock (_lock)
            {
                while (true)
                {
                    if (_stopping)
                        return;
                    if (_queue.TryPeek(out _, out var due))
                    {
                        var wait = due - _clock.GetUtcNow().UtcTicks;
                        if (wait <= 0)
                            break;
                        Monitor.Wait(_lock, TimeSpan.FromTicks(Math.Min(wait, TimeSpan.TicksPerSecond)));
                    }
                    else
                    {
                        Monitor.Wait(_lock);
                    }
                }
                item = _queue.Dequeue();
            }

            try
            {
                _release(item);
            }
            catch
            {
                // A failed re-injection must not stop the line; the gate counts injection errors.
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _stopping = true;
            _queue.Clear();
            Monitor.PulseAll(_lock);
        }
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
