using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;

namespace DemoServer
{   
    public static class RemoteDoubleParser
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private enum ExitCode
        {
            Success = 0,
            InvalidArgument = 1
        }

        private const int BufferSize = 4096;

        public static void Main(string[] args)
        {
            var (addresses, port) = ParseArguments(args);

            var addressNumber = addresses.Length;
            var taskList = new Task[addressNumber + 1];

            try
            {
                for (var i = 0; i < addressNumber; ++i)
                {
                    var endPoint = new IPEndPoint(addresses[i], port);
                    taskList[i] = Listen(endPoint, BufferSize);
                }

                taskList[addressNumber] = WaitConsoleKey();

                Console.WriteLine("press any key to exit");
                Task.WaitAny(taskList);
            }
            catch (Exception e)
            {
                Logger.Error("listen error: {}", new[] {e});
            }

            Console.WriteLine("exiting");
            Environment.Exit((int) ExitCode.Success);
        }

        private static (IPAddress[], int) ParseArguments(IReadOnlyList<string> args)
        {
            if (args.Count != 2)
            {
                Logger.Error("usage: hostname port");
                Environment.Exit((int) ExitCode.InvalidArgument);
            }

            var ipHostInfo = Dns.GetHostEntry(args[0]);
            if (ipHostInfo.AddressList.Length == 0)
            {
                Logger.Error("can not resolve hostname {}", args[0]);
                Environment.Exit((int) ExitCode.InvalidArgument);
            }

            if (!int.TryParse(args[1], out var port))
            {
                Logger.Error("invalid port {}", args[1]);
                Environment.Exit((int) ExitCode.InvalidArgument);
            }

            return (ipHostInfo.AddressList, port);
        }

        [SuppressMessage("ReSharper", "FunctionNeverReturns")]
        private static async Task Listen(EndPoint endPoint, int bufferSize)
        {
            var listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(endPoint);
            listener.Listen(100);
            Logger.Info("listening on {}", endPoint);

            var channelOptions = new UnboundedChannelOptions {SingleReader = true, SingleWriter = true};
            while (true)
            {
                var socket = await listener.AcceptAsync();
                var channel = Channel.CreateUnbounded<double>(channelOptions);
                ReadFromClient(socket, channel.Writer, bufferSize).FireAndForget(
                    e => Logger.Warn("read from client error: {}", new[] {e})
                );
                WriteToClient(socket, channel.Reader).FireAndForget(
                    e => Logger.Warn("write to client error: {}", new[] {e})
                );
            }
        }

        private static async Task ReadFromClient(Socket socket, ChannelWriter<double> channel, int bufferSize)
        {
            var endPoint = socket.RemoteEndPoint; // save remote end point for logging
            Logger.Trace("read from client {} started", endPoint);

            using (var stream = new NetworkStream(socket))
            {
                var buffer = new byte[bufferSize];
                var current = new List<byte>();
                while (true)
                {
                    var readCount = await stream.ReadAsync(buffer);
                    if (readCount == 0)
                        break;

                    var newLineIndex = Array.IndexOf(buffer, (byte) '\n', 0, readCount);
                    if (newLineIndex == -1)
                    {
                        // not find new line
                        current.AddRange(buffer.Take(readCount));
                        Logger.Trace("get {} bytes unfinished data from client {}", readCount, socket.RemoteEndPoint);
                    }
                    else
                    {
                        // find new line
                        current.AddRange(buffer.Take(newLineIndex));
                        var stringData = Encoding.UTF8.GetString(current.ToArray());
                        Logger.Trace("get string with length {} from client {}: {}",
                            stringData.Length,
                            socket.RemoteEndPoint,
                            stringData);
                        current.Clear();
                        if (readCount > newLineIndex + 1)
                        {
                            // more data in this read
                            current.AddRange(buffer.Take(readCount).Skip(newLineIndex + 1)); // skip '\n'
                            Logger.Trace("get {} bytes unfinished data from client {}",
                                readCount - newLineIndex - 1,
                                socket.RemoteEndPoint);
                        }

                        if (!double.TryParse(stringData, out var result))
                            result = double.NaN;
                        await channel.WriteAsync(result);
                    }
                }

                channel.Complete();
            }

            Logger.Trace("end read from client {}", endPoint);
        }

        private static async Task WriteToClient(Socket socket, ChannelReader<double> channel)
        {
            var endPoint = socket.RemoteEndPoint; // save remote end point for logging
            Logger.Trace("write to client {} started", endPoint);

            using (var stream = new NetworkStream(socket))
            {
                while (true)
                {
                    try
                    {
                        var number = await channel.ReadAsync();
                        var buffer = BitConverter.GetBytes(number);
                        Logger.Trace("write number {} ({} bytes) to client", number, buffer.Length);
                        await stream.WriteAsync(buffer); // ensure write sequence
                    }
                    catch (ChannelClosedException)
                    {
                        // if channel closed
                        break;
                    }
                }
            }

            socket.Close(); // no more data to send, close the socket
            Logger.Trace("end write to client {}", endPoint);
        }

        private static async Task WaitConsoleKey()
        {
            await Task.Run(() => Console.ReadKey(true));
        }
    }
}