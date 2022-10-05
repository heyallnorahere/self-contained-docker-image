using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DockerTest.Subcommands
{
    [Subcommand]
    internal sealed class Test : ISubcommand
    {
        public async Task<int> InvokeAsync(IReadOnlyList<string> args)
        {
            var builder = new StringBuilder();
            builder.AppendLine("If you're seeing this, everything is working correctly!");

            builder.AppendLine($"Command-line: {Environment.CommandLine}");
            builder.AppendLine($"Current directory: {Environment.CurrentDirectory}");
            builder.AppendLine($"Processor count: {Environment.ProcessorCount}");
            builder.AppendLine($"Process path: {Environment.ProcessPath}");
            builder.AppendLine($"CLR version: {Environment.Version}");
            builder.AppendLine($"OS version: {Environment.OSVersion}");

            await Console.Out.WriteAsync(builder);
            await Console.Out.FlushAsync();

            return 0;
        }
    }
}