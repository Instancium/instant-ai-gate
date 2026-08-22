using InstantAIGate.Core.Abstractions.Chat;
using InstantAIGate.Runners.Onnx.Adapters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Runners.Onnx.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all required ONNX runner services into the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IServiceCollection AddOnnxRunner(this IServiceCollection services)
        {
            // The factory is completely stateless, so Singleton is the most efficient lifetime.
            services.AddSingleton<IChatAdapterFactory, OnnxChatAdapterFactory>();

            return services;
        }
    }
}
