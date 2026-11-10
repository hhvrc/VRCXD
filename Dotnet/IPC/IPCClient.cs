using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

#if !LINUX
using CefSharp;
#endif


namespace VRCX
{
    public class IPCClient
    {
        private static readonly UTF8Encoding noBomEncoding = new UTF8Encoding(false, false);
        private readonly NamedPipeServerStream _ipcServer;
        private readonly byte[] _recvBuffer = new byte[1024 * 8];
        private readonly Newtonsoft.Json.JsonSerializer serializer = new Newtonsoft.Json.JsonSerializer();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly IPCPacketFramer _framer = new IPCPacketFramer();

        public IPCClient(NamedPipeServerStream ipcServer)
        {
            serializer.Culture = CultureInfo.InvariantCulture;
            serializer.Formatting = Formatting.None;

            _ipcServer = ipcServer;
        }

        public async Task ReadLoopAsync()
        {
            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await _ipcServer.ReadAsync(_recvBuffer, 0, _recvBuffer.Length);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    bytesRead = 0;
                }

                if (bytesRead <= 0)
                {
                    IPCServer.Clients.TryRemove(this, out _);
                    _ipcServer.Close();
                    return;
                }

                foreach (var packet in _framer.Push(_recvBuffer, 0, bytesRead))
                {
                    try
                    {
                        OnPacket(packet);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        private static void OnPacket(string packet)
        {
#if !LINUX
            if (MainForm.Instance?.Browser != null && !MainForm.Instance.Browser.IsLoading && MainForm.Instance.Browser.CanExecuteJavascriptInMainFrame)
                MainForm.Instance.Browser.ExecuteScriptAsync("window?.$pinia?.vrcx.ipcEvent", packet);
#endif
        }

        public void Send(IPCPacket ipcPacket)
        {
            byte[] bytes;
            try
            {
                bytes = Serialize(ipcPacket);
            }
            catch
            {
                IPCServer.Clients.TryRemove(this, out _);
                return;
            }

            _ = WriteAsync(bytes);
        }

        // Compact JSON as UTF-8 without a BOM, followed by the 0x00 packet separator.
        private byte[] Serialize(IPCPacket ipcPacket)
        {
            var stringWriter = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(stringWriter))
            {
                serializer.Serialize(writer, ipcPacket);
            }

            var json = stringWriter.ToString();
            var bytes = new byte[noBomEncoding.GetByteCount(json) + 1];
            noBomEncoding.GetBytes(json, 0, json.Length, bytes, 0);
            return bytes;
        }

        private async Task WriteAsync(byte[] bytes)
        {
            await _sendLock.WaitAsync();
            try
            {
                await _ipcServer.WriteAsync(bytes, 0, bytes.Length);
            }
            catch
            {
                IPCServer.Clients.TryRemove(this, out _);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public static void Close(IAsyncResult asyncResult)
        {
            var ipcClient = (NamedPipeClientStream)asyncResult.AsyncState;
            ipcClient?.EndWrite(asyncResult);
            ipcClient?.Close();
        }
    }
}
