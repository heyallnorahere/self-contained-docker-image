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
using System.IO;
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
        private static async Task<EndPoint?> FindEndpointAsync(string host, PortUsage usage)
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

            int port = Program.GetHostPort(usage);
            return new IPEndPoint(address, port);
        }

        private static async Task<Socket?> ConnectAsync(string host, PortUsage usage)
        {
            var protocol = Program.GetPortProtocolType(usage);
            var endpoint = await FindEndpointAsync(host, usage);

            if (endpoint == null)
            {
                Console.Error.WriteLine($"Could not resolve host name: {host}");
                return null;
            }

            Console.WriteLine($"Attempting to connect to server: {endpoint}");
            var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, protocol);

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
            using var socket = await ConnectAsync("127.0.0.1", PortUsage.HostCommunication);
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

            using var reader = new StreamReader(stream, encoding: encoding, leaveOpen: true);
            using var writer = new StreamWriter(stream, encoding: encoding, leaveOpen: true);

            socket.Blocking = true;

            var buffer = new char[1024];
            var received = string.Empty;

            Task<string?>? stdinReadTask = null;
            Task<int>? socketReadTask = null;

            var shutdown = () => socket.Shutdown(SocketShutdown.Both);
            ConsoleCancelEventHandler onCtrlC = (sender, args) => shutdown();

            Console.CancelKeyPress += onCtrlC;
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
                            await writer.WriteLineAsync(command);
                            await writer.FlushAsync();
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
                            Console.Write(buffer[0..numRead]);
                        }

                        restartTask = true;
                    }

                    if (restartTask)
                    {
                        socketReadTask = Task.Run(() => reader.ReadBlock(buffer, 0, buffer.Length));
                    }
                }
            }
            catch (SocketException)
            {
                // probably disconnected
            }

            shutdown();
            Console.CancelKeyPress -= onCtrlC;
            
            return 0;
        }
    }
}