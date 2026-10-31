namespace InstantAIGate.Dashboard.Services;

using System.Collections;

public sealed class CircularBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;
    private readonly object _syncLock = new();

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _count;
            }
        }
    }

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        Capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        lock (_syncLock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity)
            {
                _count++;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (_syncLock)
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                int logicalStart = _count < Capacity ? 0 : _head;
                int physical = (logicalStart + index) % Capacity;
                return _buffer[physical];
            }
        }
    }

    public T[] Snapshot()
    {
        lock (_syncLock)
        {
            var result = new T[_count];
            if (_count == 0)
            {
                return result;
            }

            int logicalStart = _count < Capacity ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(logicalStart + i) % Capacity];
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            if (_count == 0)
            {
                return;
            }

            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        T[] snapshot = Snapshot();
        for (int i = 0; i < snapshot.Length; i++)
        {
            yield return snapshot[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
