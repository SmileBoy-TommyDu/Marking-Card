using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace DrSoft.MarkCard.Impl.PowerMeter
{
    public class StreamingPowerMeter : IPowerMeter
    {
        private SerialPort? _serialPort;
        private TcpClient? _tcpClient;
        private StreamReader? _reader;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private readonly StringBuilder _serialBuffer = new();
        private readonly object _lock = new();

        public event Action<string>? FeedbackValueReceived;

        public bool IsConnected { get; private set; }

        public MarkErrorCode Connect(PowerMeterConfig config)
        {
            var disconnectResult = Disconnect();
            if (disconnectResult != MarkErrorCode.None)
            {
                return disconnectResult;
            }

            if (config == null || string.IsNullOrWhiteSpace(config.ConnectString))
            {
                return MarkErrorCode.InvalidParameter;
            }

            try
            {
                return config.ConnectType switch
                {
                    ConnectType.Com => ConnectSerial(config.ConnectString),
                    ConnectType.Ethernet => ConnectTcp(config.ConnectString),
                    _ => MarkErrorCode.InvalidParameter
                };
            }
            catch
            {
                Disconnect();
                return MarkErrorCode.UnknownError;
            }
        }

        public MarkErrorCode Disconnect()
        {
            try
            {
                IsConnected = false;
                _cts?.Cancel();
            }
            catch
            {
            }

            try
            {
                if (_serialPort != null)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
            catch
            {
            }

            try
            {
                _reader?.Dispose();
                _reader = null;
            }
            catch
            {
            }

            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
            }
            catch
            {
            }

            try
            {
                _cts?.Dispose();
                _cts = null;
            }
            catch
            {
            }

            _readTask = null;
            return MarkErrorCode.None;
        }

        private MarkErrorCode ConnectSerial(string connectString)
        {
            var parts = connectString.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return MarkErrorCode.InvalidParameter;
            }

            string portName = parts[0];
            int baudRate = parts.Length > 1 && int.TryParse(parts[1], out var baud) ? baud : 9600;
            int dataBits = parts.Length > 2 && int.TryParse(parts[2], out var bits) ? bits : 8;
            Parity parity = parts.Length > 3 ? ParseParity(parts[3]) : Parity.None;
            StopBits stopBits = parts.Length > 4 ? ParseStopBits(parts[4]) : StopBits.One;

            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\r\n",
                ReadTimeout = 1000
            };
            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();

            IsConnected = true;
            return MarkErrorCode.None;
        }

        private MarkErrorCode ConnectTcp(string connectString)
        {
            var parts = connectString.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
            {
                return MarkErrorCode.InvalidParameter;
            }

            _tcpClient = new TcpClient();
            _tcpClient.Connect(parts[0], port);
            _reader = new StreamReader(_tcpClient.GetStream(), Encoding.ASCII);
            _cts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadTcpLoopAsync(_cts.Token));
            IsConnected = true;
            return MarkErrorCode.None;
        }

        private async Task ReadTcpLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    PublishFeedback(line);
                }
            }
            catch
            {
            }
            finally
            {
                IsConnected = false;
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    return;
                }

                string incoming = _serialPort.ReadExisting();
                if (string.IsNullOrWhiteSpace(incoming))
                {
                    return;
                }

                lock (_lock)
                {
                    _serialBuffer.Append(incoming);
                    string buffer = _serialBuffer.ToString();
                    string[] lines = buffer.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[i]))
                        {
                            PublishFeedback(lines[i]);
                        }
                    }

                    _serialBuffer.Clear();
                    _serialBuffer.Append(lines[^1]);
                }
            }
            catch
            {
            }
        }

        private void PublishFeedback(string rawValue)
        {
            string value = TryExtractNumericValue(rawValue) ?? rawValue.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                FeedbackValueReceived?.Invoke(value);
            }
        }

        private static string? TryExtractNumericValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = Regex.Match(text, @"[-+]?\d+(\.\d+)?");
            return match.Success ? match.Value : null;
        }

        private static Parity ParseParity(string value)
        {
            return value.ToUpperInvariant() switch
            {
                "O" or "ODD" => Parity.Odd,
                "E" or "EVEN" => Parity.Even,
                "M" or "MARK" => Parity.Mark,
                "S" or "SPACE" => Parity.Space,
                _ => Parity.None
            };
        }

        private static StopBits ParseStopBits(string value)
        {
            return value switch
            {
                "0" => StopBits.None,
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => StopBits.One
            };
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
