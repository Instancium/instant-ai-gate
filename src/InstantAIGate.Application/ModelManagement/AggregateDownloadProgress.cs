using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Application.ModelManagement
{
    public class AggregateDownloadProgress
    {
        public long TotalDownloadedBytes { get; set; }
        public long TotalModelSizeBytes { get; set; }

        public double Percentage => TotalModelSizeBytes == 0
            ? 0
            : (double)TotalDownloadedBytes / TotalModelSizeBytes * 100;
    }
}
