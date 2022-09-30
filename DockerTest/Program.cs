using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DockerTest
{
    public enum PortUsage
    {
        HostCommunication
    }

    public struct ContainerPortBinding
    {
        public int ContainerPort { get; set; }
        public int HostPort { get; set; }
        public ProtocolType Protocol { get; set; }
    }

    public interface ISubcommand
    {
        public Task<int> InvokeAsync(IReadOnlyList<string> arguments);
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SubcommandAttribute : Attribute
    {
        public SubcommandAttribute()
        {
            ID = null;
        }

        public SubcommandAttribute(string id)
        {
            ID = id;
        }

        public string? ID { get; }
    }

    public static class Program
    {
        private static readonly IReadOnlyDictionary<PortUsage, ContainerPortBinding> sUsedPorts;
        static Program()
        {
            sUsedPorts = new Dictionary<PortUsage, ContainerPortBinding>
            {
                [PortUsage.HostCommunication] = new ContainerPortBinding
                {
                    ContainerPort = 5000,
                    HostPort = 11000,
                    Protocol = ProtocolType.Tcp
                }
            };
        }

        public static IEnumerable<ContainerPortBinding> BoundPorts => sUsedPorts.Values;

        public static int GetContainerPort(PortUsage usage)
        {
            if (!sUsedPorts.ContainsKey(usage))
            {
                throw new ArgumentException("Unbound port!");
            }

            return sUsedPorts[usage].ContainerPort;
        }

        public static int GetHostPort(PortUsage usage)
        {
            if (!sUsedPorts.ContainsKey(usage))
            {
                throw new ArgumentException("Unbound port!");
            }

            return sUsedPorts[usage].HostPort;
        }

        public static ProtocolType GetPortProtocolType(PortUsage usage)
        {
            if (!sUsedPorts.ContainsKey(usage))
            {
                throw new ArgumentException("Unbound port!");
            }

            return sUsedPorts[usage].Protocol;
        }

        private static string GetSubcommandID(SubcommandAttribute attribute, Type type)
        {
            string name = attribute.ID ?? type.Name;
            string id = string.Empty;

            foreach (char character in name)
            {
                if (char.IsUpper(character))
                {
                    if (id.Length > 0)
                    {
                        id += '-';
                    }

                    id += char.ToLower(character);
                }
                else
                {
                    id += character;
                }
            }

            return id;
        }

        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("No subcommand provided!");
                return 1;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var types = assembly.GetTypes();

            var subcommands = new Dictionary<string, ConstructorInfo>();
            foreach (var type in types)
            {
                var interfaces = type.GetInterfaces();
                if (!interfaces.Contains(typeof(ISubcommand)))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<SubcommandAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Array.Empty<Type>());
                if (constructor == null)
                {
                    continue;
                }

                string id = GetSubcommandID(attribute, type);
                if (subcommands.ContainsKey(id))
                {
                    Console.Error.WriteLine($"Duplicate subcommand ID: {id}");
                    return 1;
                }

                subcommands.Add(id, constructor);
            }

            if (!subcommands.ContainsKey(args[0]))
            {
                Console.Error.WriteLine($"Subcommand not found: {args[0]}");
                return 1;
            }

            var subcommandConstructor = subcommands[args[0]];
            var subcommand = (ISubcommand)subcommandConstructor.Invoke(null);

            IReadOnlyList<string> subcommandArgs;
            if (args.Length > 1)
            {
                subcommandArgs = args[1..];
            }
            else
            {
                subcommandArgs = Array.Empty<string>();
            }

            return await subcommand.InvokeAsync(subcommandArgs);
        }
    }
}