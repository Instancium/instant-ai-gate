namespace InstantAIGate.Core.Services.Inference;

using InstantAIGate.Core.Interfaces.Inference;
using InstantAIGate.Core.Interfaces.Native;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class PrefixTreeNode
{
    public int TokenId { get; init; }
    public IContextHandle? CachedContext { get; set; }
    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<int, PrefixTreeNode> Children { get; } = new();

    public PrefixTreeNode(int tokenId)
    {
        TokenId = tokenId;
    }
}

public sealed class PrefixTreeContextPool
{
    private readonly PrefixTreeNode _root = new(-1);
    private readonly object _syncLock = new();
    private readonly IBackendFacade _backendFacade;

    public PrefixTreeContextPool(IBackendFacade backendFacade)
    {
        _backendFacade = backendFacade ?? throw new ArgumentNullException(nameof(backendFacade));
    }

    public (IContextHandle? Handle, int MatchedLength) AcquireBestContext(ReadOnlySpan<int> targetPrefix)
    {
        lock (_syncLock)
        {
            var currentNode = _root;
            PrefixTreeNode? bestMatchNode = null;
            int currentDepth = 0;
            int bestMatchDepth = 0;

            for (int i = 0; i < targetPrefix.Length; i++)
            {
                int token = targetPrefix[i];
                if (!currentNode.Children.TryGetValue(token, out var nextNode))
                {
                    break;
                }

                currentNode = nextNode;
                currentDepth++;

                if (currentNode.CachedContext != null)
                {
                    bestMatchNode = currentNode;
                    bestMatchDepth = currentDepth;
                }
            }

            if (bestMatchNode?.CachedContext != null)
            {
                var handle = bestMatchNode.CachedContext;
                bestMatchNode.CachedContext = null;
                bestMatchNode.LastAccessed = DateTimeOffset.UtcNow;
                return (handle, bestMatchDepth);
            }

            return (null, 0);
        }
    }

    public void ReturnContext(IContextHandle handle, ReadOnlySpan<int> prefixSequence)
    {
        ArgumentNullException.ThrowIfNull(handle);

        lock (_syncLock)
        {
            var currentNode = _root;
            foreach (int token in prefixSequence)
            {
                if (!currentNode.Children.TryGetValue(token, out var child))
                {
                    child = new PrefixTreeNode(token);
                    currentNode.Children[token] = child;
                }
                currentNode = child;
            }

            if (currentNode.CachedContext != null)
            {
                _backendFacade.FreeContext(currentNode.CachedContext);
            }

            currentNode.CachedContext = handle;
            currentNode.LastAccessed = DateTimeOffset.UtcNow;
        }
    }

    public void EvictOldest(int maxRetainedContexts)
    {
        lock (_syncLock)
        {
            var allActiveNodes = new List<PrefixTreeNode>();
            TraverseActiveNodes(_root, allActiveNodes);

            if (allActiveNodes.Count <= maxRetainedContexts)
            {
                return;
            }

            var evictionCandidates = allActiveNodes
                .OrderBy(n => n.LastAccessed)
                .Take(allActiveNodes.Count - maxRetainedContexts);

            foreach (var node in evictionCandidates)
            {
                if (node.CachedContext != null)
                {
                    _backendFacade.ClearContextMemory(node.CachedContext, clearKvCache: true);
                    _backendFacade.FreeContext(node.CachedContext);
                    node.CachedContext = null;
                }
            }
        }
    }

    private static void TraverseActiveNodes(PrefixTreeNode node, List<PrefixTreeNode> accumulator)
    {
        if (node.CachedContext != null)
        {
            accumulator.Add(node);
        }

        foreach (var child in node.Children.Values)
        {
            TraverseActiveNodes(child, accumulator);
        }
    }
}