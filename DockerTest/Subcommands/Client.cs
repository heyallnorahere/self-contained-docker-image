/*
   Copyright 2022 Nora Beda

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DockerTest.Subcommands
{
    [Subcommand]
    internal sealed class Client : ISubcommand
    {
        private static async Task<EndPoint?> FindEndpointAsync(string host, int port)
        {
            IPAddress? address;
            if (!IPAddress.TryParse(host, out address))
            {
                var entry = await Dns.GetHostEntryAsync(host);
                address = entry.AddressList.FirstOrDefault();
            }

            if (address == null)
            {
                return null;
            }

            return new IPEndPoint(address, port);
        }

        private static async Task<Socket?> ConnectAsync(string host, int port, ProtocolType protocol)
        {
            var endpoint = await FindEndpointAsync(host, port);

            if (endpoint == null)
            {
                Console.Error.WriteLine($"Could not resolve host name: {host}");
                return null;
            }

            Console.WriteLine($"Attempting to connect to server: {endpoint}");
            var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, protocol)
            {
                Blocking = true
            };

            try
            {
                await socket.ConnectAsync(endpoint);
            }
            catch (Exception ex)
            {
                var builder = new StringBuilder();
                builder.AppendLine("Exception thrown:");

                var type = ex.GetType();
                builder.AppendLine($"\tType: {type.FullName ?? type.Name}");
                builder.AppendLine($"\tMessage: {ex.Message}");

                Console.Error.Write(builder);

                socket.Dispose();
                return null;
            }

            return socket;
        }

        public async Task<int> InvokeAsync(IReadOnlyList<string> args)
        {
            const PortUsage usage = PortUsage.HostCommunication;
            var protocol = Program.GetPortProtocolType(usage);

            int port;
            if (args.Contains("--use-internal-port"))
            {
                port = Program.GetContainerPort(usage);
            }
            else
            {
                port = Program.GetHostPort(usage);
            }

            using var socket = await ConnectAsync("127.0.0.1", port, protocol);
            if (socket == null)
            {
                Console.Error.WriteLine("Failed to connect to server!");
                return 1;
            }
            else
            {
                Console.WriteLine("Successfully connected!");
            }

            var encoding = Encoding.UTF8;
            using var stream = new NetworkStream(socket, false);

            var buffer = new byte[1024];
            var received = string.Empty;

            Task<string?>? stdinReadTask = null;
            Task<int>? socketReadTask = null;

            bool disconnected = false;
            try
            {
                while (true)
                {
                    bool restartTask = false;
                    if (stdinReadTask == null)
                    {
                        restartTask = true;
                    }
                    else if (stdinReadTask.IsCompleted)
                    {
                        if (stdinReadTask.IsFaulted)
                        {
                            throw stdinReadTask.Exception!;
                        }

                        var command = stdinReadTask.Result;
                        if (command == null)
                        {
                            break;
                        }

                        if (command.Length > 0)
                        {
                            var bytes = encoding.GetBytes($"{command}\n");
                            await stream.WriteAsync(bytes);
                            await stream.FlushAsync();
                        }

                        restartTask = true;
                    }

                    if (restartTask)
                    {
                        stdinReadTask = Task.Run(Console.In.ReadLineAsync);
                    }

                    restartTask = false;
                    if (socketReadTask == null)
                    {
                        restartTask = true;
                    }
                    else if (socketReadTask.IsCompleted)
                    {
                        if (socketReadTask.IsFaulted)
                        {
                            throw socketReadTask.Exception!;
                        }

                        int numRead = socketReadTask.Result;
                        if (numRead < 0)
                        {
                            break;
                        }

                        if (numRead > 0)
                        {
                            string data = encoding.GetString(buffer[0..numRead]);
                            Console.Write(data);

                            restartTask = true;
                        }
                        else
                        {
                            disconnected = true;
                            break;
                        }
                    }

                    if (restartTask)
                    {
                        socketReadTask = Task.Run(async () => await stream.ReadAsync(buffer));
                    }
                }
            }
            catch (SocketException)
            {
                disconnected = true;
            }

            if (disconnected)
            {
                Console.WriteLine("Socket disconnected");
            }

            socket.Shutdown(SocketShutdown.Both);
            return 0;
        }
    }
}