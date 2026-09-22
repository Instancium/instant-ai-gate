using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.AspNetCore.Mvc;

namespace InstantAIGate.Server.Controllers.admin;

[ApiController]
[Route("admin/queue")]
public class AdminQueueController : ControllerBase
{
    private readonly IModelManager _modelManager;
    private readonly IQueueManager _queueManager;

    public AdminQueueController(IModelManager modelManager, IQueueManager queueManager)
    {
        _modelManager = modelManager;
        _queueManager = queueManager;
    }

    [HttpGet("metrics")]
    public IActionResult GetMetrics()
    {
        var metrics = _modelManager.GetMetrics();
        return Ok(new
        {
            activeLeases = metrics.ActiveLeases,
            pendingRequests = metrics.PendingRequests,
            maxQueueSize = _queueManager.MaxQueueSize
        });
    }

    [HttpPost("limit")]
    public IActionResult SetLimit([FromBody] SetLimitRequest request)
    {
        if (request.MaxQueueSize <= 0)
        {
            return BadRequest(new { error = "Limit must be greater than zero." });
        }

        _queueManager.UpdateQueueLimit(request.MaxQueueSize);
        return Ok(new { message = $"Queue limit updated to {request.MaxQueueSize}." });
    }
}

public record SetLimitRequest(int MaxQueueSize);