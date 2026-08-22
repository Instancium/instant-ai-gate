using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.DTOs.Chat
{
    public record ImageUrl
    {
        /// <summary>
        /// Can be a standard URL (https://...) or a Base64 encoded string (data:image/jpeg;base64,...).
        /// </summary>
        public string Url { get; init; } = string.Empty;
    }
}
