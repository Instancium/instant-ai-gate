using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.Config
{
    public class GatewayConfig
    {
        public string RootPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantAIGate"
        );

        public const string AdminKeyFileName = "admin.key";
        public string AdminKeyPath => Path.Combine(RootPath, AdminKeyFileName);

        public string? AdminKey { get; set; }
    }
}
