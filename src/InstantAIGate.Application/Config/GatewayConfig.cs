namespace InstantAIGate.Application.Config
{
    public class GatewayConfig
    {
        private static string GetDefaultRootPath()
        {
            // Check for environment variable (for Docker/Linux support)
            var envPath = Environment.GetEnvironmentVariable("INSTANTAIGATE_CONFIG_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
                return envPath;

            // Cross-platform default: ApplicationData works on both Windows and Linux
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InstantAIGate"
            );
        }

        public string RootPath { get; set; } = GetDefaultRootPath();

        public const string AdminKeyFileName = "admin.key";
        public string AdminKeyPath => Path.Combine(RootPath, AdminKeyFileName);

        public string? AdminKey { get; set; }
    }
}
