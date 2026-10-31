namespace InstantAIGate.Core.Services.Infrastructure;

using System;
using System.IO;

/// <summary>
/// Minimal parser for the Linux <c>/proc/meminfo</c> virtual file.
/// Returns false (instead of throwing) on any I/O or format anomaly so callers
/// can fall back to GC-derived system memory estimates.
/// </summary>
internal static class ProcMemInfoReader
{
    public static bool TryReadSystemMemory(string path, out long totalBytes, out long availableBytes)
    {
        totalBytes = 0;
        availableBytes = 0;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            long totalKb = 0;
            long availableKb = 0;
            bool hasTotal = false;
            bool hasAvailable = false;
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                {
                    totalKb = ParseKilobytes(line);
                    hasTotal = true;
                }
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                {
                    availableKb = ParseKilobytes(line);
                    hasAvailable = true;
                }
                if (hasTotal && hasAvailable)
                {
                    break;
                }
            }
            if (!hasTotal || !hasAvailable || totalKb <= 0)
            {
                return false;
            }
            totalBytes = totalKb * 1024;
            availableBytes = Math.Clamp(availableKb * 1024, 0, totalBytes);
            return true;
        }
        catch
        {
            totalBytes = 0;
            availableBytes = 0;
            return false;
        }
    }

    private static long ParseKilobytes(string line)
    {
        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out long value) ? value : 0;
    }
}
