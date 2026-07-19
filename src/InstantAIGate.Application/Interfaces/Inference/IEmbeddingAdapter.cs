namespace InstantAIGate.Application.Interfaces.Inference
{
    public interface IEmbeddingAdapter
    {
        /// <summary>
        /// Model name or RepoId
        /// </summary>
        /// <param name="model"></param>
        /// <param name="inputs"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<IReadOnlyList<float[]>> GetEmbeddingAsync(string model, List<string> inputs, CancellationToken ct = default);
    }
}
