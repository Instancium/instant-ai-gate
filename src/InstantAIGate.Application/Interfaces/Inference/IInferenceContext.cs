namespace InstantAIGate.Application.Interfaces.Inference
{
    /// <summary>
    /// A pure Application-layer abstraction representing an active model session.
    /// Completely decoupled from any native LLM libraries.
    /// </summary>
    public interface IInferenceContext : IDisposable { }
}
