using System;
using System.Linq;
using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace TouchBarStudio
{
    /// <summary>
    /// Reads finger positions from the Touch Bar HID digitizer (Apple VID 05AC, PID 8302).
    /// Only the X axis is meaningful on this hardware. Positions are reported as 0..1 of the bar width.
    /// The report layout follows the one used by imbushuo/DFRContentHost.
    /// </summary>
    public class TouchInput : IDisposable
    {
        const uint FingerIdentifier = 0xd0038;
        const int Slots = 11;

        HidDevice _device;
        ReportDescriptor _descriptor;
        HidStream _stream;
        HidDeviceInputReceiver _receiver;
        DeviceItemInputParser _parser;

        int _active = -1;
        double _lastX;

        public event Action<double> Down;
        public event Action<double> Move;
        public event Action<double> Up;

        public string Error { get; private set; } = "not started";

        public bool Start()
        {
            try
            {
                _device = DeviceList.Local.GetHidDeviceOrNull(0x05ac, 0x8302);
                if (_device == null)
                {
                    Error = "touch digitizer not found";
                    return false;
                }

                _descriptor = _device.GetReportDescriptor();
                if (!_device.TryOpen(out _stream))
                {
                    Error = "cannot open the touch digitizer";
                    return false;
                }

                _receiver = _descriptor.CreateHidDeviceInputReceiver();
                _parser = _descriptor.DeviceItems[0].CreateDeviceItemInputParser();
                _receiver.Received += OnReceived;
                _receiver.Start(_stream);

                Error = "";
                return true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }
        }

        void OnReceived(object sender, EventArgs e)
        {
            try
            {
                var buffer = new byte[_device.GetMaxInputReportLength()];
                while (_receiver.TryRead(buffer, 0, out Report report))
                {
                    if (_parser.TryParseReport(buffer, 0, report))
                    {
                        ProcessReport();
                    }
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }

        void ProcessReport()
        {
            if (!_parser.HasChanged)
            {
                return;
            }

            var x = new double[Slots];
            var down = new bool[Slots];
            int slot = -1;

            for (int i = 0; i < _parser.ValueCount; i++)
            {
                var data = _parser.GetValue(i);
                if (data.Usages.FirstOrDefault() == FingerIdentifier)
                {
                    slot++;
                }
                else
                {
                    continue;
                }

                if (slot >= Slots || i + 3 >= _parser.ValueCount)
                {
                    break;
                }

                // Layout assumed from the report descriptor: id, tap status, tap status 2, X, Y
                var tap = _parser.GetValue(i + 1);
                var xData = _parser.GetValue(i + 3);
                double max = xData.DataItem.PhysicalMaximum;

                x[slot] = max > 0 ? xData.GetPhysicalValue() / max : 0;
                down[slot] = tap.GetPhysicalValue() != 0;
            }

            if (_active >= 0)
            {
                if (down[_active])
                {
                    _lastX = x[_active];
                    Move?.Invoke(_lastX);
                }
                else
                {
                    _active = -1;
                    Up?.Invoke(_lastX);
                }
            }

            if (_active < 0)
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (down[i])
                    {
                        _active = i;
                        _lastX = x[i];
                        Down?.Invoke(_lastX);
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            try
            {
                _stream?.Dispose();
            }
            catch
            {
            }
        }
    }
}
