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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace DockerTest.Subcommands
{
    [Subcommand]
    internal sealed class Daemon : ISubcommand
    {
        public async Task<int> InvokeAsync(IReadOnlyList<string> args)
        {
            Console.WriteLine("Starting server...");

            const PortUsage portUsage = PortUsage.HostCommunication;
            int port = Program.GetContainerPort(portUsage);
            var protocolType = Program.GetPortProtocolType(portUsage);

            var ipAddress = IPAddress.Parse("0.0.0.0");
            var endpoint = new IPEndPoint(ipAddress, port);
            using var server = new Socket(endpoint.AddressFamily, SocketType.Stream, protocolType);

            var commands = new Queue<string>();
            var data = string.Empty;
            var encoding = Encoding.UTF8;

            server.Bind(endpoint);
            server.Listen(100);

            Console.WriteLine($"Now listening on {ipAddress}:{port}");

            bool stop = false;
            while (!stop)
            {
                using var socket = await server.AcceptAsync();
                using var stream = new NetworkStream(socket, false);

                Console.WriteLine($"Received connection from {socket.RemoteEndPoint}");

                var buffer = new byte[1024];
                try
                {
                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(buffer);
                        if (bytesRead <= 0)
                        {
                            break;
                        }

                        data += encoding.GetString(buffer[0..bytesRead]);
                        var splitData = data.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        if (data.EndsWith('\n'))
                        {
                            data = string.Empty;
                            foreach (var command in splitData)
                            {
                                commands.Enqueue(command);
                            }
                        }
                        else
                        {
                            data = splitData[^1];
                            for (int i = 0; i < splitData.Length - 1; i++)
                            {
                                commands.Enqueue(splitData[i]);
                            }
                        }

                        while (commands.Count > 0)
                        {
                            var command = commands.Dequeue();
                            string message;

                            if (command.ToLower() == "stop")
                            {
                                message = $"Received command \"{command}\" - stopping execution";
                                stop = true;
                            }
                            else
                            {
                                message = $"Received unknown command \"{command}\"";
                            }

                            var bytes = encoding.GetBytes($"{message}\n");
                            await stream.WriteAsync(bytes);
                            await stream.FlushAsync();

                            Console.WriteLine(message);
                        }

                        if (stop)
                        {
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    // socket disconnected, most likely
                }
            }

            server.Shutdown(SocketShutdown.Both);
            return 0;
        }
    }
}