namespace InstantAIGate.Server.Configuration;

using System;
using System.Globalization;
using InstantAIGate.Core.Dtos.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Central policy for hardware profile resolution.
/// Guarantees that embedding models are never booted with a chat-oriented
/// hardware profile, regardless of the explicitly requested profile name.
/// </summary>
public sealed class HardwareProfilePolicy
{
    public const string DefaultProfileName = "Default";
    public const string EmbeddingProfileName = "Embedding";
    private const string ProfileSectionTemplate = "InstantAIGate:HardwareProfiles:{0}";

    private readonly IConfiguration _configuration;
    private readonly ILogger<HardwareProfilePolicy> _logger;

    public HardwareProfilePolicy(IConfiguration configuration, ILogger<HardwareProfilePolicy> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static bool IsEmbeddingModel(string repoId, string? architecture = null)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            return false;
        }

        return repoId.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(architecture)
                && architecture.Contains("embed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the effective hardware profile name.
    /// Embedding models requested with a non-embedding profile are auto-routed
    /// to the 'Embedding' profile when it is defined in configuration.
    /// </summary>
    public string ResolveProfileName(string? requestedProfile, bool isEmbeddingModel)
    {
        string requested = string.IsNullOrWhiteSpace(requestedProfile)
            ? DefaultProfileName
            : requestedProfile.Trim();

        if (!isEmbeddingModel)
        {
            return requested;
        }

        if (string.Equals(requested, EmbeddingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return EmbeddingProfileName;
        }

        if (!ProfileExists(EmbeddingProfileName))
        {
            _logger.LogWarning(
                "Embedding model requested profile '{Requested}', but hardware profile '{EmbeddingProfile}' is not defined. Falling back to '{Requested}'. Define 'InstantAIGate:HardwareProfiles:{EmbeddingProfile}' to isolate embedding workloads.",
                requested,
                EmbeddingProfileName,
                requested,
                EmbeddingProfileName);
            return requested;
        }

        HardwareProfileSettings? requestedSettings = ResolveProfile(requested);
        if (requestedSettings is { Embeddings: true })
        {
            return requested;
        }

        _logger.LogInformation(
            "Auto-routing embedding model from non-embedding profile '{Requested}' to '{EmbeddingProfile}'.",
            requested,
            EmbeddingProfileName);
        return EmbeddingProfileName;
    }

    public HardwareProfileSettings ResolveProfile(string? profileName)
    {
        string name = string.IsNullOrWhiteSpace(profileName)
            ? DefaultProfileName
            : profileName.Trim();

        return _configuration.GetSection(string.Format(CultureInfo.InvariantCulture, ProfileSectionTemplate, name))
                   .Get<HardwareProfileSettings>()
               ?? new HardwareProfileSettings();
    }

    private bool ProfileExists(string profileName) =>
        _configuration.GetSection(string.Format(CultureInfo.InvariantCulture, ProfileSectionTemplate, profileName)).Exists();
}
