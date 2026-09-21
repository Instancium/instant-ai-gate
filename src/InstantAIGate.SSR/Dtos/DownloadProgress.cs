using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.SSR.Dtos
{
    public record DownloadProgress(
        string ModelId,
        long BytesDownloaded,
        long TotalBytes,
        double SpeedBytesPerSecond,
        float Percentage
    );
}
