using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Radios;

namespace TouchBarStudio
{
    /// <summary>Four-function calculator with percent, used by Calc keys and the CalcDisplay tile.</summary>
    public static class CalcEngine
    {
        static string _entry = "0";
        static double _acc;
        static char _op;
        static bool _fresh = true;
        static string _expression = "";

        public static readonly List<string> History = new List<string>();

        public static string Entry => _entry;
        public static string Expression => _expression;

        static double Value => double.TryParse(_entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        static string Format(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "Error";
            return v.ToString("G12", CultureInfo.InvariantCulture);
        }

        static double Apply(double a, char op, double b)
        {
            switch (op)
            {
                case '+': return a + b;
                case '-': return a - b;
                case '*': return a * b;
                case '/': return b == 0 ? double.NaN : a / b;
                default: return b;
            }
        }

        public static void Press(string key)
        {
            if (_entry == "Error" && key != "C") return;

            switch (key)
            {
                case "C":
                    _entry = "0"; _acc = 0; _op = '\0'; _fresh = true; _expression = "";
                    return;
                case "⌫":
                    if (!_fresh) _entry = _entry.Length > 1 ? _entry.Substring(0, _entry.Length - 1) : "0";
                    if (_entry == "-" || _entry == "") _entry = "0";
                    return;
                case "±":
                    if (_entry != "0") _entry = _entry.StartsWith("-") ? _entry.Substring(1) : "-" + _entry;
                    return;
                case ".":
                    if (_fresh) { _entry = "0."; _fresh = false; }
                    else if (!_entry.Contains(".")) _entry += ".";
                    return;
                case "%":
                    {
                        double v = Value;
                        // 200 + 10 %  -> 20 (10 % of the left operand); otherwise plain /100.
                        v = (_op == '+' || _op == '-') ? _acc * v / 100.0 : v / 100.0;
                        _entry = Format(v);
                        _fresh = true;
                        return;
                    }
                case "+": case "-": case "×": case "÷":
                    {
                        char op = key == "×" ? '*' : key == "÷" ? '/' : key[0];
                        if (_op != '\0' && !_fresh)
                        {
                            _acc = Apply(_acc, _op, Value);
                            _entry = Format(_acc);
                        }
                        else
                        {
                            _acc = Value;
                        }

                        _op = op;
                        _fresh = true;
                        _expression = Format(_acc) + " " + key;
                        return;
                    }
                case "=":
                    {
                        if (_op == '\0') return;
                        double right = Value;
                        double result = Apply(_acc, _op, right);
                        string line = Format(_acc) + " " + OpSymbol(_op) + " " + Format(right) + " = " + Format(result);
                        History.Insert(0, line);
                        if (History.Count > 20) History.RemoveAt(History.Count - 1);
                        _expression = Format(_acc) + " " + OpSymbol(_op) + " " + Format(right) + " =";
                        _entry = Format(result);
                        _op = '\0';
                        _fresh = true;
                        return;
                    }
                default:
                    if (key.Length == 1 && char.IsDigit(key[0]))
                    {
                        if (_fresh || _entry == "0") { _entry = key; _fresh = false; }
                        else if (_entry.Length < 14) _entry += key;
                    }

                    return;
            }
        }

        static string OpSymbol(char op) => op == '*' ? "×" : op == '/' ? "÷" : op.ToString();
    }

    /// <summary>
    /// In-memory clipboard history (never written to disk). Items that the source program marks as
    /// excluded from history (password managers do this) are not stored.
    /// </summary>
    public static class ClipboardHistory
    {
        public const int Max = 30;
        public static readonly List<string> Items = new List<string>();
        public static bool Enabled = true;
        static string _ignore;

        /// <summary>Call when the clipboard changed. Returns true if the history changed.</summary>
        public static bool Capture()
        {
            if (!Enabled) return false;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var data = Clipboard.GetDataObject();
                    if (data == null) return false;
                    if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")) return false;
                    if (data.GetDataPresent("CanIncludeInClipboardHistory") && !IsAllowed(data)) return false;
                    if (!data.GetDataPresent(DataFormats.UnicodeText)) return false;

                    string text = data.GetData(DataFormats.UnicodeText) as string;
                    if (string.IsNullOrWhiteSpace(text) || text.Length > 5000) return false;
                    if (_ignore != null && text == _ignore) { _ignore = null; return false; }

                    Items.Remove(text);
                    Items.Insert(0, text);
                    if (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(30); // clipboard is busy
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        static bool IsAllowed(IDataObject data)
        {
            try
            {
                var raw = data.GetData("CanIncludeInClipboardHistory");
                if (raw is System.IO.MemoryStream ms && ms.Length >= 4)
                {
                    return BitConverter.ToInt32(ms.ToArray(), 0) != 0;
                }
            }
            catch
            {
            }

            return true;
        }

        public static string Get(int slot) => slot >= 0 && slot < Items.Count ? Items[slot] : null;

        /// <summary>Puts a stored entry back on the clipboard without re-ordering the history.</summary>
        public static void Restore(int slot)
        {
            string text = Get(slot);
            if (text == null) return;
            _ignore = text;
            Clipboard.SetText(text);
        }

        public static void Clear()
        {
            Items.Clear();
        }
    }

    /// <summary>Wi-Fi and Bluetooth radio state and toggle through the Windows Radios API.</summary>
    public static class RadioControl
    {
        public static int Wifi = -1;      // -1 unknown / absent, 0 off, 1 on
        public static int Bluetooth = -1;

        static async Task<IReadOnlyList<Radio>> GetAsync()
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return new List<Radio>();
            return await Radio.GetRadiosAsync();
        }

        static int StateOf(IEnumerable<Radio> radios, RadioKind kind)
        {
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            if (radio == null) return -1;
            return radio.State == RadioState.On ? 1 : 0;
        }

        public static async Task RefreshAsync()
        {
            try
            {
                var radios = await GetAsync();
                Wifi = StateOf(radios, RadioKind.WiFi);
                Bluetooth = StateOf(radios, RadioKind.Bluetooth);
            }
            catch
            {
                Wifi = -1;
                Bluetooth = -1;
            }
        }

        public static async Task ToggleAsync(RadioKind kind)
        {
            var radios = await GetAsync();
            var radio = radios.FirstOrDefault(r => r.Kind == kind);
            if (radio == null) throw new InvalidOperationException(Loc.T("Not supported"));

            var target = radio.State == RadioState.On ? RadioState.Off : RadioState.On;
            var result = await radio.SetStateAsync(target);
            if (result != RadioAccessStatus.Allowed) throw new InvalidOperationException(Loc.T("Not supported"));
            await RefreshAsync();
        }
    }
}
