using InstantAIGate.Cli.Core;
using InstantAIGate.Cli.Services;
using InstantAIGate.Cli.State;
using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Cli.Pipeline.Steps
{
    public class GitAddAllStep : IPipelineStep
    {
        private readonly IGitService _gitService;

        public string Name => "Stage all changes (git add .)";

        public GitAddAllStep(IGitService gitService)
        {
            _gitService = gitService;
        }

        public async Task ExecuteAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            await _gitService.AddAllAsync(cancellationToken);
        }
    }
}
