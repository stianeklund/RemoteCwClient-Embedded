using System;
using System.Device.Gpio;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using nanoFramework.Hardware.Esp32;
using nanoFramework.Networking;
using nanoFramework.Runtime.Events;
using System.IO.Ports;
using System.Threading;
using System.Net.NetworkInformation;
using System.Device.Wifi;

namespace RemoteCwClient_Embedded
{
    public class CwClient
    {

        private readonly TcpClient _tcpClient;
        private static SerialPort _serialPort;
        private const string User = "MG:Callsign MyCallsign;\r\n";
        private const string Password = "MG:Password MyPassword;\r\n";


        private CwClient(string host, int port)

        {
            _tcpClient = new TcpClient(host, port);
            _tcpClient.Client.Send(Encoding.UTF8.GetBytes(User));
            _tcpClient.Client.Send(Encoding.UTF8.GetBytes(Password));
        }

        private static void Main()
        {
            bool success;

            CancellationTokenSource cs = new(60000);
        
#if HAS_WIFI
        private static string MySsid = "mySsid";
        private static string MyPassword = "myPassword";
#endif
        
#if HAS_WIFI
        success = WifiNetworkHelper.Reconnect();
#else
        success = NetworkHelper.SetupAndConnectNetwork(cs.Token);
#endif

            Configuration.SetPinFunction(32, DeviceFunction.COM1_RX);
            Configuration.SetPinFunction(33, DeviceFunction.COM1_TX);

            _serialPort = new SerialPort("COM1", 1200);
            _serialPort.Open();
            // Set contest spacing, no auto space, paddle echo watchdog on, etc.
            var winKeyConnect = new byte[]
            {
            0x55, 0x1F, 0x8A, 0xC0
            };
            var enablePtt = new byte[] { 0x8A, 0xC0 };

            // Host enable
            _serialPort.Write(new byte[] { 0x00, 0x02 }, 0, 2);
            Thread.Sleep(50);

            // Set register values
            _serialPort.Write(winKeyConnect, 0, winKeyConnect.Length);
            Thread.Sleep(50);
            // Enable ptt 
            _serialPort.Write(enablePtt, 0, enablePtt.Length);

            const string hostname = "10.0.0.8";
            var cwc = new CwClient(hostname, 5555);

            // TODO destruct / decode this
            const string keyerSpeed = "WS:25|25|40|23|1;\r\n";

            // turn server echo on
            cwc._tcpClient.Client.Send(Encoding.UTF8.GetBytes("E:1\r\n"));
            cwc._tcpClient.Client.Send(Encoding.UTF8.GetBytes(keyerSpeed));

            void OnSerialPortOnDataReceived(object o, SerialDataReceivedEventArgs e)
            {
                if (!_serialPort.IsOpen) return;

                var serialData = _serialPort.ReadExisting();

                // only send real ascii resulting in proper morse code chars
                // TODO consider handling commands separately

                var match = Regex.Match(serialData, "[A-Za-z0-9?]");
                if (match.Success)
                {
                    var data = $"WK:{serialData};\r\n";
                    var buf = Encoding.UTF8.GetBytes(data);
                    Console.WriteLine($"{data}\r\n");
                    cwc._tcpClient.GetStream().Write(buf, 0, buf.Length);
                }
            }

            _serialPort.DataReceived += OnSerialPortOnDataReceived;

            var bytes = new byte[1000];

            do
            {
                var stream = cwc._tcpClient.GetStream();
                int i;
                while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                {
                    // TODO handle keyer speed etc..
                    var msg = Encoding.UTF8.GetString(bytes, 0, i);
                    // Console.WriteLine($"Received: {msg}");
                    //    _serialPort.Write(msg);
                }
            } while (cwc._tcpClient.Client.Available != -1);
        }
    }
}
