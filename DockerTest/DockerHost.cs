using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DockerTest
{
    public class DockerHost : IDisposable
    {
        public static Uri DockerEngine
        {
            get
            {
                string path;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    path = "npipe://./pipe/docker_engine";
                }
                else
                {
                    path = "unix:///var/run/docker.sock";
                }

                return new Uri(path);
            }
        }

        public DockerHost(Uri? engine = null)
        {
            mDisposed = false;

            using var config = new DockerClientConfiguration(engine ?? DockerEngine);
            mClient = config.CreateClient();
        }

        // todo: add log in method

        public async Task PullImageAsync(string name, string? tag = null, IProgress<JSONMessage>? progress = null, AuthConfig? auth = null)
        {
            VerifyUsable();

            await mClient.Images.CreateImageAsync(new ImagesCreateParameters
            {
                FromImage = name,
                Tag = tag ?? "latest"
            }, auth, progress ?? new Progress<JSONMessage>());
        }

        public async Task BuildImageAsync(ImageBuildParameters parameters, BuildContext context, IProgress<JSONMessage>? progress = null, IEnumerable<AuthConfig>? auth = null)
        {
            VerifyUsable();

            await context.GetStreamAsync(async stream =>
            {
                await mClient.Images.BuildImageFromDockerfileAsync(parameters, stream, auth, null, progress ?? new Progress<JSONMessage>());
            });
        }

        private void VerifyUsable()
        {
            if (mDisposed)
            {
                var type = GetType();
                throw new ObjectDisposedException(type.FullName ?? type.Name);
            }
        }

        public void Dispose()
        {
            if (mDisposed)
            {
                return;
            }

            mClient.Dispose();
            mDisposed = true;
        }

        public DockerClient Client => mClient;

        private bool mDisposed;
        private readonly DockerClient mClient;
    }
}