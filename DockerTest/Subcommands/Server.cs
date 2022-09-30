using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DockerTest.Subcommands
{
    [Subcommand]
    internal sealed class Server : ISubcommand
    {
        private sealed class ProgressReporter : IProgress<JSONMessage>
        {
            public void Report(JSONMessage message)
            {
                var type = message.GetType();
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                Console.WriteLine("Got progress report:");
                foreach (var property in properties)
                {
                    string value;
                    try
                    {
                        var propertyValue = property.GetValue(message, BindingFlags.Public | BindingFlags.Instance, null, null, null);
                        value = propertyValue?.ToString() ?? "null";
                    }
                    catch (Exception ex)
                    {
                        var exceptionType = ex.GetType();
                        value = $"{exceptionType.FullName ?? exceptionType.Name}: {ex.Message}";
                    }

                    Console.WriteLine($"\t{property.Name}: {value}");
                }
            }
        }

        private static async Task BuildContextAsync(IContextBuilder contextBuilder)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyDir = Path.GetDirectoryName(assembly.Location) ?? Directory.GetCurrentDirectory();

            using var dockerfileStream = assembly.GetManifestResourceStream("DockerTest.Resources.Dockerfile");
            if (dockerfileStream == null)
            {
                throw new Exception("Failed to get dockerfile stream!");
            }

            int totalBytesRead = 0;
            var dockerfileBytes = new byte[dockerfileStream.Length];

            do
            {
                int numRead = await dockerfileStream.ReadAsync(dockerfileBytes, totalBytesRead, dockerfileBytes.Length - totalBytesRead);
                if (numRead <= 0)
                {
                    break;
                }

                totalBytesRead += numRead;
            }
            while (totalBytesRead < dockerfileBytes.Length);

            await contextBuilder.AddDirectoryAsync(assemblyDir, "/");
            await contextBuilder.AddEntryAsync("/Dockerfile", dockerfileBytes);
        }

        public async Task<int> InvokeAsync(IReadOnlyList<string> args)
        {
            using var host = new DockerHost();
            var reporter = new ProgressReporter();

            // login? maybe?

            using var context = new BuildContext();
            await context.RebuildAsync(BuildContextAsync);

            var assembly = Assembly.GetExecutingAssembly();
            var configurationAttribute = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();

            string imageTag = "docker-test:runtime-build";
            if (configurationAttribute != null)
            {
                imageTag += $"-{configurationAttribute.Configuration.ToLower()}";
            }

            var buildParams = new ImageBuildParameters
            {
                Pull = "missing",
                Remove = true,
                Tags = new List<string>
                {
                    imageTag
                }
            };

            var containerList = await host.Client.Containers.ListContainersAsync(new ContainersListParameters());
            var containerMatches = containerList.Where(container => container.Image == imageTag);

            foreach (var match in containerMatches)
            {
                await host.Client.Containers.RemoveContainerAsync(match.ID, new ContainerRemoveParameters
                {
                    Force = true
                });
            }

            var imageList = await host.Client.Images.ListImagesAsync(new ImagesListParameters());
            var imageMatches = imageList.Where(image => image.RepoTags?.Contains(imageTag) ?? false);

            foreach (var match in imageMatches)
            {
                await host.Client.Images.DeleteImageAsync(imageTag, new ImageDeleteParameters());
            }

            var exposedPorts = new Dictionary<string, EmptyStruct>();
            var portBindings = new Dictionary<string, IList<PortBinding>>();

            foreach (var binding in Program.BoundPorts)
            {
                string key = $"{binding.ContainerPort}/{binding.Protocol.ToString().ToLower()}";

                exposedPorts.Add(key, new EmptyStruct());
                portBindings.Add(key, new PortBinding[]
                {
                    new PortBinding
                    {
                        HostPort = binding.HostPort.ToString()
                    }
                });
            }

            await host.BuildImageAsync(buildParams, context, reporter);
            var response = await host.Client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = imageTag,
                ExposedPorts = exposedPorts,
                HostConfig = new HostConfig
                {
                    PortBindings = portBindings,
                    AutoRemove = true
                }
            });

            await host.Client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());
            return 0;
        }
    }
}