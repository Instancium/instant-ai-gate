namespace InstantAIGate.Admin.Config
{

    /// <summary>
    /// Configuration options for the API client.
    /// </summary>
    public class APIClientOptions
    {
        /// <summary>
        /// The name of the configuration section.
        /// </summary>
        public const string SectionName = "APIClientOptions";

        /// <summary>
        /// The base URL of the API.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// The public URL of the API.
        /// </summary>
        public string PublicUrl { get; set; } = string.Empty;
    }
}
