using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace VRCX
{
    public class IPCServer
    {
        public static readonly IPCServer Instance;
        public static readonly ConcurrentDictionary<IPCClient, byte> Clients = new();

        static IPCServer()
        {
            Instance = new IPCServer();
        }

        public void Init()
        {
            new IPCServer().CreateIPCServer();
        }

        public static void Send(IPCPacket ipcPacket)
        {
            foreach (var client in Clients)
            {
                client.Key?.Send(ipcPacket);
            }
        }

        public static string GetIpcName()
        {
            var hash = 0;
            foreach (var c in Environment.UserName)
            {
                hash += c;
            }
            return $"vrcx-ipc-{hash}";
        }

        public void CreateIPCServer()
        {
            _ = AcceptLoopAsync(CreatePipe());
        }

        private static NamedPipeServerStream CreatePipe()
        {
            return new NamedPipeServerStream(GetIpcName(), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        private static async Task AcceptLoopAsync(NamedPipeServerStream ipcServer)
        {
            while (true)
            {
                try
                {
                    await ipcServer.WaitForConnectionAsync();

                    var ipcClient = new IPCClient(ipcServer);
                    Clients.TryAdd(ipcClient, 0);
                    _ = ipcClient.ReadLoopAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    ipcServer.Dispose();
                }

                ipcServer = CreatePipe();
            }
        }
    }
}
