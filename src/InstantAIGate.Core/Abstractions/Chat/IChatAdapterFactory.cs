using InstantAIGate.Core.DTOs.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Core.Abstractions.Chat
{
    /// <summary>
    /// Factory responsible for instantiating the correct ONNX chat adapter 
    /// based on the provided model manifest capabilities.
    /// </summary>
    public interface IChatAdapterFactory
    {
        /// <summary>
        /// Creates an appropriate IChatAdapter for the given manifest.
        /// </summary>
        /// <param name="manifest">The model configuration manifest.</param>
        /// <returns>An instance of IChatAdapter (TextAdapter or MultiModalAdapter).</returns>
        IChatAdapter CreateAdapter(ModelManifest manifest);
    }
}
