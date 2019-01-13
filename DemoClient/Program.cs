using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;

namespace DemoClient
{
    public static class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private enum ExitCode
        {
            Success = 0,
            InvalidArgument = 1,
            FailedConnectToServer = 2,
        }

        public static void Main(string[] args)
        {
            var (addresses, port) = ParseArguments(args);
            var socket = Connect(addresses, port);
            if (socket == null)
            {
                Logger.Error("failed to connect to server");
                Environment.Exit((int) ExitCode.FailedConnectToServer);
            }

            var testCases = new[]
            {
                "111.222312312",
                "123123124324132512351.23412354",
                "1254351234515325235123.351351325",
                "0",
                "",
                "381578937517",
            };
            Logger.Info("start testing");
            using (var stream = new NetworkStream(socket))
            using (var writer = new StreamWriter(stream))
            using (var reader = new BinaryReader(stream))
            {
                writer.AutoFlush = true; // must auto flush to prevent block
                foreach (var testCase in testCases)
                {
                    writer.Write(testCase + '\n');

                    if (!double.TryParse(testCase, out var result))
                        result = double.NaN;

                    var remoteResult = reader.ReadDouble();
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (remoteResult == result || double.IsNaN(remoteResult) && double.IsNaN(result))
                        Console.WriteLine($"case passed: {testCase}, result: {result}");
                    else
                        Console.Error.WriteLine($"failed on case: {testCase}, local: {result}, remote: {remoteResult}");
                }
            }

            socket.Close();
            Environment.Exit((int) ExitCode.Success);
        }

        private static Socket Connect(IReadOnlyList<IPAddress> addresses, int port)
        {
            var addressNumber = addresses.Count;
            var socketList = new Socket[addressNumber];
            var taskList = new Task[addressNumber];
            for (var i = 0; i < addressNumber; ++i)
            {
                var endPoint = new IPEndPoint(addresses[i], port);
                var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                socketList[i] = socket;
                taskList[i] = socket.ConnectAsync(endPoint);
            }

            var connectedIndex = Task.WaitAny(taskList);
            for (var i = 0; i < addressNumber; ++i)
            {
                if (i == connectedIndex && socketList[connectedIndex].Connected)
                    continue; // skip connected
                socketList[i].Close();
                socketList[i] = null;
            }

            return socketList[connectedIndex]; // can be null
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
    }
}