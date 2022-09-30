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
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
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
                            if (command.ToLower() == "stop")
                            {
                                Console.WriteLine($"Received command \"{command}\" - stopping execution");
                                stop = true;
                            }
                            else
                            {
                                Console.WriteLine($"Received unknown command \"{command}\"");
                            }
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