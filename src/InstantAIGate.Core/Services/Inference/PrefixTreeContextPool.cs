namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using System;
using System.Collections.Generic;
using System.Linq;

public class RadixContextNode
{
    public int[] PrefixTokens { get; set; } = Array.Empty<int>();
    public IContextHandle? CachedContext { get; set; }
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<int, RadixContextNode> Children { get; } = new();
}

public class PrefixTreeContextPool
{
    private readonly RadixContextNode _root = new();
    private readonly object _lock = new();
    private readonly IBackendFacade _backendFacade;

    public PrefixTreeContextPool(IBackendFacade backendFacade)
    {
        _backendFacade = backendFacade;
    }

    public (IContextHandle? Handle, int MatchedLength) AcquireBestContext(int[] targetPrefix)
    {
        lock (_lock)
        {
            RadixContextNode bestNode = _root;
            int bestMatchLength = 0;

            foreach (var child in _root.Children.Values)
            {
                int matchLen = GetCommonPrefixLength(child.PrefixTokens, targetPrefix);
                if (matchLen > bestMatchLength)
                {
                    bestMatchLength = matchLen;
                    bestNode = child;
                }
            }

            if (bestNode.CachedContext != null)
            {
                var handle = bestNode.CachedContext;
                bestNode.CachedContext = null;
                return (handle, bestMatchLength);
            }

            return (null, 0);
        }
    }

    public void ReturnContext(IContextHandle handle, int[] prefixSequence)
    {
        lock (_lock)
        {
            var node = new RadixContextNode
            {
                PrefixTokens = prefixSequence.ToArray(),
                CachedContext = handle,
                LastAccessed = DateTimeOffset.UtcNow
            };

            int hash = CalculateSequenceHash(prefixSequence);
            _root.Children[hash] = node;
        }
    }

    public void ApplyEvictionPolicy(int maxCapacity)
    {
        lock (_lock)
        {
            int currentContexts = _root.Children.Values.Count(n => n.CachedContext != null);
            if (currentContexts <= maxCapacity) return;

            var oldest = _root.Children.Values
                .Where(n => n.CachedContext != null)
                .OrderBy(n => n.LastAccessed)
                .FirstOrDefault();

            if (oldest != null && oldest.CachedContext != null)
            {
                _backendFacade.ClearContextMemory(oldest.CachedContext, true);
                _root.Children.Remove(CalculateSequenceHash(oldest.PrefixTokens));
            }
        }
    }

    private int GetCommonPrefixLength(int[] a, int[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return len;
    }

    private int CalculateSequenceHash(int[] sequence)
    {
        unchecked
        {
            int hash = 17;
            foreach (var t in sequence) hash = hash * 31 + t;
            return hash;
        }
    }
}