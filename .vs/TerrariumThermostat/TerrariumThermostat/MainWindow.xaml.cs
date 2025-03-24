using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO.Ports;
using System.Management;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Newtonsoft.Json;
using System.Windows.Controls.DataVisualization.Charting;
using System.IO;

namespace TerrariumThermostat {

    /// <summary>
    /// Date:       Aug.11/2013
    /// Coder:      Richard Payne
    /// Version:    1.3 Beta
    /// Notes:      Still seems to have issues with crashing after running for 20+ hours 
    /// Test Notes: 4 thermal sensors (1500 readings per cycle), 1 photosensor, 7 devices (4 heaters, 1 fan, 2 lights), (26 +/- 0.25) second cycle from sketch TempControlledDevices v_1_3_Released
    /// ------------------------------------------------------------------------------------
    /// 
    /// Date:       Oct.27/2013
    /// Coder:      Richard Payne
    /// Version:    1.4 Beta
    /// Notes:      Background colors applied to Photo Sensor and Photo Device grids.
    ///               Messages from Com Port now fired syncronously to GUI.  
    ///               Seems stable when removing items from Temperature Sensor History chart
    ///               Crashes after extended (2-3 weeks) continous operation.
    /// Test Notes: No change since 1.3 Beta.
    /// --------------------------------------------------------------------------------------
    /// 
    /// Date:       Dec.21/2013
    /// Coder:      RIchard Payne
    /// Version:    1.5 Beta
    /// Notes:      Application settings now read from config file ([working directory]/config.txt JSON formatted
    ///                 Errors log to file([working directory/log.txt])
    ///                 Added error logging to MainWIndow_Closing
    ///                 Added checks before attempting to close serial port (Looks like solves issue with application crashing when closed after serial port is externally diconnected)
    ///                 Removed ItemSource binding from listViewComChannels.  Managed internally, should implement on ObservableDictionary class
    ///                 Removed all references to SerialPort objects except [_comports]
    ///                 Modified ComPort to handle disconnects, reconnects of selected ports.  Seems a bit hacky catching an exception inside class ComPort, 
    ///                     but also seems to work. TODO add in reselect port if selected port is dropped and reconnects (and no other selection has been made)
    ///                 Fix Data Grid background colors set incorrectly in rowloading events - not fully tested.
    ///                 Messages now added synchronously
    ///                 1.4 Beta crashes after extended (2-3 weeks) continous operation.
    ///                 Reduced Chart title size http://stackoverflow.com/questions/3595310/change-margin-around-plot-area-and-title-in-wpf-toolkit-chart
    ///                                                   http://stackoverflow.com/questions/4591535/how-to-remove-space-between-wpf-toolkit-chart-area-and-plot-area
    /// Test Notes: No change since 1.4 Beta
    /// -----------------------------------------------------------------------------------------
    /// 
    /// Date:       Dec.29/2013
    /// Coder:      Richard Payne
    /// Version :   1.6 Beta
    /// Notes:      Reduced Tempurature Sensor Chart size
    ///                 Added Tempurature Device State Chart
    /// Test Notes: No change since 1.5 Beta
    /// -----------------------------------------------------------------------------------------
    /// 
    /// Date:       Jan.04/2014
    /// Coder:      Richard Payne
    /// Version:    1.6.1 Beta
    /// Notes:      Colors changed, Photo Indexes colored on Temperature Devices chart loading
    ///                 Border color indicates Sensor/Device Indexes instead of background color
    ///                 Add protection against array out of bounds for [_chartColors]
    ///                 Add gridLineStyle to DependantRangeAxis on tempDevicesChart
    /// Test Notes: No change since 1.6 Beta
    /// -----------------------------------------------------------------------------------------
    /// 
    /// Date:       Dec.31/2024
    /// Coder:      Richard Payne
    /// Version:    1.6.2 Beta
    /// Notes:      Chart data management performance improvements
    ///             Chart (lines, dots) Opacity changed to 1 for performance
    ///             Device and Sensor collection management performance improvements
    ///             Code Formatting changed
    /// Test Notes: No change since 1.6 Beta
    /// -----------------------------------------------------------------------------------------
    /// </summary>
    public partial class MainWindow : Window {
        #region Internal Classes
        /// <summary>
        /// Wrapper to raise event for/on internal class propery change.
        /// </summary>
        public abstract class NotifyPropertyChanged : INotifyPropertyChanged {
            public event PropertyChangedEventHandler PropertyChanged;
            protected void Changed(string s)
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(s));
                }
            }
        }

        /// <summary>
        /// Class used for binding to UI Chart Properties
        /// </summary>
        public class ChartOption : NotifyPropertyChanged {
            public ChartOption() { }
            private int _maxChartY;
            public int MaxChartY {
                get { return _maxChartY; }
                set
                {
                    if (value > _minChartY)
                    {
                        _maxChartY = value; Changed("MaxChartY");
                    }
                }
            }

            private int _minChartY;
            public int MinChartY {
                get { return _minChartY; }
                set
                {
                    if (value < _maxChartY)
                    {
                        _minChartY = value; Changed("MinChartY");
                    }
                }
            }
        }

        public class TempSensor : NotifyPropertyChanged {
            public TempSensor()
            {
            }

            public TempSensor(int pin, float reading, float maxExpected, float minExpected, int errorState, uint errorCount, int arrayIndex, float tempOffset)
            {
                _pin = pin;
                _reading = reading;
                _max_expected = maxExpected;
                _max_expected = minExpected;
                _error_state = errorState;
                _error_count = errorCount;
                _array_index = arrayIndex;
                _temp_offset = tempOffset;
            }

            private int _pin; // use for key
            public int Pin {
                get { return _pin; }
                set { _pin = value; Changed("Pin"); }
            }

            private float _reading;
            public float Reading {
                get { return _reading; }
                set { _reading = value; Changed("Reading"); }
            }

            private float _max_expected;
            public float Max_expected {
                get { return _max_expected; }
                set { _max_expected = value; Changed("Max_expected"); }
            }

            private float _min_expected;
            public float Min_expected {
                get { return _min_expected; }
                set { _min_expected = value; Changed("Min_expected"); }
            }

            private int _error_state;
            public int Error_state {
                get { return _error_state; }
                set { _error_state = value; Changed("Error_state"); }
            }

            private uint _error_count;
            public uint Error_count {
                get { return _error_count; }
                set { _error_count = value; Changed("Error_count"); }
            }

            private int _array_index;
            public int Array_index {
                get { return _array_index; }
                set { _array_index = value; Changed("Pin"); }
            }

            private float _temp_offset;
            public float Temp_offset {
                get { return _temp_offset; }
                set { _temp_offset = value; Changed("Temp_offset"); }
            }

            private DateTime _time_stamp;
            public DateTime Time_stamp {
                get { return _time_stamp; }
                set { _time_stamp = value; Changed("Time_stamp"); }
            }
        }

        public class PhotoSensor : NotifyPropertyChanged {
            public PhotoSensor()
            {
            }

            private int _pin; // use for key
            public int Pin {
                get { return _pin; }
                set { _pin = value; Changed("Pin"); }
            }

            private int _reading;
            public int Reading {
                get { return _reading; }
                set { _reading = value; Changed("Reading"); }
            }

            private int _array_index;
            public int Array_index {
                get { return _array_index; }
                set { _array_index = value; Changed("Array_index"); }
            }

            private DateTime _time_stamp;
            public DateTime Time_stamp {
                get { return _time_stamp; }
                set { _time_stamp = value; Changed("Time_stamp"); }
            }
        }

        public class TempControlledDevice : NotifyPropertyChanged {
            public TempControlledDevice()
            {
            }

            private int _array_index;
            public int Array_index {
                get { return _array_index; }
                set { _array_index = value; Changed("Array_index"); }
            }

            private int _output_pin; // use for key?
            public int Output_pin {
                get { return _output_pin; }
                set { _output_pin = value; Changed("Output_pin"); }
            }

            private bool _current_state;
            public bool Current_state {
                get { return _current_state; }
                set { _current_state = value; Changed("Current_state"); }
            }

            private int _temp_sensor_array_index;
            public int Temp_sensor_array_index {
                get { return _temp_sensor_array_index; }
                set { _temp_sensor_array_index = value; Changed("Temp_sensor_array_index"); }
            }

            private int _temp_sensor_state_when_on;
            public int Temp_sensor_state_when_on {
                get { return _temp_sensor_state_when_on; }
                set { _temp_sensor_state_when_on = value; Changed("Temp_sensor_state_when_on"); }
            }

            private float _day_time_switch_temp;
            public float Day_time_switch_temp {
                get { return _day_time_switch_temp; }
                set { _day_time_switch_temp = value; Changed("Day_time_switch_temp"); }
            }

            private float _night_time_switch_temp;
            public float Night_time_switch_temp {
                get { return _night_time_switch_temp; }
                set { _night_time_switch_temp = value; Changed("Night_time_switch_temp"); }
            }

            private int _state_on_sensor_error;
            public int State_on_sensor_error {
                get { return _state_on_sensor_error; }
                set { _state_on_sensor_error = value; Changed("State_on_sensor_error"); }
            }

            private int _photo_sensor_array_index;
            public int Photo_sensor_array_index {
                get { return _photo_sensor_array_index; }
                set { _photo_sensor_array_index = value; Changed("Photo_sensor_array_index"); }
            }

            private int _photo_sensor_switch_reading;
            public int Photo_sensor_switch_reading {
                get { return _photo_sensor_switch_reading; }
                set { _photo_sensor_switch_reading = value; Changed("Photo_sensor_switch_reading"); }
            }

            private string _sensor_reading;
            public string Sensor_reading {
                get { return _sensor_reading; }
                set { _sensor_reading = value; Changed("Sensor_reading"); }
            }

            private float _temp_range;
            public float Temp_range {
                get { return _temp_range; }
                set { _temp_range = value; Changed("Temp_range"); }
            }

            private DateTime _time_stamp;
            public DateTime Time_stamp {
                get { return _time_stamp; }
                set { _time_stamp = value; Changed("Time_stamp"); }
            }
        }

        public class PhotoControlledDevice : NotifyPropertyChanged {
            public PhotoControlledDevice()
            {
            }

            private int _array_index;
            public int Array_index {
                get { return _array_index; }
                set { _array_index = value; Changed("Array_index"); }
            }

            private int _output_pin; // use for key?
            public int Output_pin {
                get { return _output_pin; }
                set { _output_pin = value; Changed("Output_pin"); }
            }

            private int _photo_sensor_array_index;
            public int Photo_sensor_array_index {
                get { return _photo_sensor_array_index; }
                set { _photo_sensor_array_index = value; Changed("Photo_sensor_array_index"); }
            }

            private float _switch_reading;
            public float Switch_reading {
                get { return _switch_reading; }
                set { _switch_reading = value; Changed("Switch_reading"); }
            }

            private bool _day_time_state_on;
            public bool Day_time_state_on {
                get { return _day_time_state_on; }
                set { _day_time_state_on = value; Changed("Day_time_state_on"); }
            }

            private bool _current_state;
            public bool Current_state {
                get { return _current_state; }
                set { _current_state = value; Changed("Current_state"); }
            }

            private DateTime _time_stamp;
            public DateTime Time_stamp {
                get { return _time_stamp; }
                set { _time_stamp = value; Changed("Time_stamp"); }
            }
        }

        /// <summary>
        /// Used to update Available USB connections 
        /// Call default contructor and Subscribe to event UsbConnectEventArrived
        /// http://msdn.microsoft.com/en-us/library/system.management.managementeventwatcher.query(v=vs.110).aspx
        /// </summary>
        private class UsbEventListener {
            private ManagementEventWatcher _insertWatcher;
            private ManagementEventWatcher _removalWatcher;
            public delegate void EventArrivedEventHandler(EventType type, string port);
            public event EventArrivedEventHandler UsbConnectEventArrived;
            public enum EventType { Disconnect = 0, Connect };

            /// <summary>
            /// Provides simplfied access to detect Serial Port connect and disconnect events.
            /// http://msdn.microsoft.com/en-us/library/aa394124(v=vs.85).aspx
            ///http://stackoverflow.com/questions/5278860/using-wmi-to-identify-which-device-caused-a-win32-devicechangeevent
            ///http://stackoverflow.com/questions/9467707/dont-get-instanceoperationevent-when-disablingqenabling-some-devices
            /// </summary>
            public UsbEventListener()
            {

                WqlEventQuery _query;
                _insertWatcher = new ManagementEventWatcher();
                _removalWatcher = new ManagementEventWatcher();


                _query = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_SerialPort'");
                _insertWatcher.Query = _query;
                //_insertWatcher.Options.Timeout = new TimeSpan(0, 0, 2);
                _insertWatcher.Start();
                _insertWatcher.EventArrived += _watcher_ConnectEventArrived;

                _query = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 3 WHERE TargetInstance ISA 'Win32_SerialPort'");
                _removalWatcher.Query = _query;
                //_removalWatcher.Options.Timeout = new TimeSpan(0, 0, 2);
                _removalWatcher.Start();
                _removalWatcher.EventArrived += _watcher_DisconnectEventArrived;
            }

            public void Stop()
            {
                try
                {
                    _insertWatcher.Stop();
                    _removalWatcher.Stop();
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            }

            private void _watcher_DisconnectEventArrived(object sender, EventArrivedEventArgs e)
            {
                try
                {
                    if (UsbConnectEventArrived == null)
                    {
                        return;
                    }

                    ManagementBaseObject mo = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                    UsbConnectEventArrived(EventType.Disconnect, mo.Properties["DeviceID"].Value.ToString());
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, null);
                }
            }

            private void _watcher_ConnectEventArrived(object sender, EventArrivedEventArgs e)
            {
                try
                {
                    if (UsbConnectEventArrived == null)
                    {
                        return;
                    }

                    ManagementBaseObject mo = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                    UsbConnectEventArrived(EventType.Connect, mo.Properties["DeviceID"].Value.ToString());
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, null);
                }
            }
        }

        /// <summary>
        /// ComPort wrapper to handle serial port operations.
        /// Receives data on a separate thread for un-interrupted GUI interaction.
        /// </summary>
        public class ComPort {
            private string _buffer;
            private SerialPort _serPort;
            private string _message;
            public delegate void MessageReceivedHandler(object sender, string message);
            public event MessageReceivedHandler OnMessageReceived;
            //public event PropertyChangedEventHandler PropertyChanged;

            internal SerialPort SerPort {
                get { return _serPort; }
                set { _serPort = value; }
            }

            /// <summary>
            ///  Constructor taking a SerialPort
            /// </summary>
            /// <param name="port"></param>
            public ComPort(SerialPort port)
            {
                _serPort = port;
            }

            public void Disconnect()
            {
                if (_serPort == null)
                {
                    return;
                }
                try
                {
                    _serPort.DataReceived -= DataReceivedHandler;
                    if (SerialPort.GetPortNames().Contains(_serPort.PortName) && _serPort.IsOpen && !_serPort.BreakState)
                    {
                        _serPort.Close();
                        _serPort.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex, "Warning: SerialPort internal error, calling SerialPort.Close() seems to work for dropped connections, but throws an exception");
                }
            }

            /// <summary>
            /// Attempts to the internal SerialPort
            /// Assigns an event handler on DataReceived (DataReceivedHanderl)
            /// </summary>
            public void Connect()
            {
                try
                {
                    string[] ports = SerialPort.GetPortNames();

                    Disconnect();
                    _serPort.BaudRate = 9600;
                    _serPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);
                    _serPort.ErrorReceived += _serPort_ErrorReceived;
                    _serPort.PinChanged += _serPort_PinChanged;
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
                int failedAttemptsToOpen = 0;

                while (!_serPort.IsOpen && failedAttemptsToOpen < 13)
                {
                    try
                    {
                        _serPort.Open();
                    }
                    catch (Exception ex)
                    {
                        ++failedAttemptsToOpen;
                        Logger.Log(ex, "Attempt " + failedAttemptsToOpen + " of 13");
                    }
                }
            }

            void _serPort_PinChanged(object sender, SerialPinChangedEventArgs e)
            {
                Logger.Log(null, "Handle this event: _serPort_PinChanged.");
            }

            void _serPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
            {
                Logger.Log(null, "Handle this event: _serPort_ErrorReceived.");
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return _serPort.PortName;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="name"></param>
            //private void Notify(string name) {
            //    if (PropertyChanged == null)
            //        return;

            //    PropertyChanged(this, new PropertyChangedEventArgs(name));
            //}

            /// <summary>
            /// 
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
            {
                SerialPort sp = (SerialPort)sender;
                if (!sp.Equals(MainWindow._comPorts[sp.PortName].SerPort))// _selectedComPort.SerPort))
                {
                    Logger.Log(null, "Data received, but not from selected com port");
                    return;
                }

                string indata = sp.ReadExisting();
                _buffer += indata;
                while (HasMessage())
                {
                    if (OnMessageReceived != null)
                    {
                        OnMessageReceived(this, _message);
                    }
                }
            }

            private bool HasMessage()
            {
                _message = String.Empty;
                bool ret = false;


                if (_buffer.Contains("\r\n")) // delimiter is end of line, ends up with Windows style end of line
                {
                    try
                    {
                        _message = _buffer.Substring(0, _buffer.IndexOf("\r\n"));
                        _buffer = _buffer.Substring(_buffer.IndexOf("\r\n") + 2);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(ex, "Failed to parse JSON");
                    }
                }
                if (_message != String.Empty)
                {
                    ret = true;
                }
                return ret;
            }
        } // end class ComPort
        #endregion

        #region Global variables
        private int _maxMessageCount;
        private TimeSpan _chartRefreshRate;
        private TimeSpan _minChartRangeX;
        private int _maxChartReadings;
        private readonly TimeSpan _oneMinute = new TimeSpan(0, 1, 0);
        private bool _syncChartXAxis = true;

        #region required  application defaults defined here to guarantee a value
        private Dictionary<string, object> _appSettings = new Dictionary<string, object>{
            { "MAX_MESSAGE_COUNT", 200 },
            { "MAX_CHART_READINGS", 300 },
            { "CHART_REFRESH_RATE", new TimeSpan(0, 0, 10) },
            { "MIN_CHART_RANGE_X", new TimeSpan(1, 0, 0) },
            { "MIN_CHART_Y", 24 },
            { "MAX_CHART_Y", 36 }
        };
        #endregion

        private readonly List<Color> _chartColors = new List<Color>();
        private readonly int _chartColorsCount;
        private Color ON_COLOR = new Color() { A = 255, G = 100, R = 0, B = 0 };
        private Color OFF_COLOR = Colors.Red;
        private Color ERROR_COLOR = new Color() { A = 255, G = 50, R = 255, B = 50 };
        private static Dictionary<string, ComPort> _comPorts;
        private static string _selectedComPort;
        private static string _configPath = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + @"\config.txt";
        private static UsbEventListener _usbEventListener = new UsbEventListener();

        public delegate void SelectedComPortChangeHandler(object sender);
        public ObservableCollection<string> Messages { get; private set; }
        public ObservableCollection<TempSensor> TemperatureSensors { get; private set; }
        public ObservableCollection<TempControlledDevice> TempControlledDevices { get; private set; }
        public ObservableCollection<PhotoSensor> PhotoSensors { get; private set; }
        public ObservableCollection<PhotoControlledDevice> PhotoControlledDevices { get; private set; }
        public ObservableCollection<ObservableCollection<KeyValuePair<DateTime, double>>> TempSensorsHistory { get; private set; }
        public ObservableCollection<ObservableCollection<KeyValuePair<DateTime, int>>> TempDevicesHistory { get; private set; }
        public ObservableCollection<ChartOption> ChartOptions { get; private set; } // TODO, remove the ObservableCollection, replace with a List
        #endregion

        /// <summary>
        /// MainWindow setup and initialization.
        /// </summary>
        public MainWindow()
        {
            try
            {
                SetupDefaults();
                _chartColors.Add(Colors.Blue); // fixed in .xaml
                _chartColors.Add(Colors.Green);
                _chartColors.Add(new Color() { A = 255, B = 70, G = 60, R = 60 });
                _chartColors.Add(Colors.Purple);
                _chartColors.Add(Colors.DarkOrange);
                _chartColors.Add(Colors.DarkRed);
                _chartColorsCount = _chartColors.Count();

                // Get list of serial ports 
                DataContext = this;
                string[] ports = SerialPort.GetPortNames();
                _usbEventListener.UsbConnectEventArrived += usbEventListener_UsbConnectEventArrived;
                _comPorts = new Dictionary<string, ComPort>();
                Messages = new ObservableCollection<string>();
                TemperatureSensors = new ObservableCollection<TempSensor>();
                TempControlledDevices = new ObservableCollection<TempControlledDevice>();
                PhotoSensors = new ObservableCollection<PhotoSensor>();
                PhotoControlledDevices = new ObservableCollection<PhotoControlledDevice>();
                TempSensorsHistory = new ObservableCollection<ObservableCollection<KeyValuePair<DateTime, double>>>();
                TempDevicesHistory = new ObservableCollection<ObservableCollection<KeyValuePair<DateTime, int>>>();

                InitializeComponent();
                listBoxLogs.ItemsSource = Messages;
                dataGridPhotoSensors.ItemsSource = PhotoSensors;
                dataGridTempSensors.ItemsSource = TemperatureSensors;
                dataGridTempDevices.ItemsSource = TempControlledDevices;
                dataGridPhotoDevices.ItemsSource = PhotoControlledDevices;

                foreach (string port in ports)
                {
                    ComPort newPort = new ComPort(new SerialPort(port));
                    _comPorts.Add(port, newPort);
                    listViewComChannels.Items.Add(port);
                }

                tempSensorsChart.Title = new TextBlock {
                    Text = "Temperature Sensors",
                    FontSize = 12,
                    FontFamily = new FontFamily("Arial"),
                    Foreground = Brushes.Black,
                    Height = 15
                };

                tempDevicesChart.Title = new TextBlock {
                    Text = "Temperature Device",
                    FontSize = 12,
                    FontFamily = new FontFamily("Arial"),
                    Foreground = Brushes.Black,
                    Height = 15
                };

                dataGridTempSensors.LoadingRow += dataGridTempSensors_LoadingRow;
                dataGridTempDevices.LoadingRow += dataGridTempDevices_LoadingRow;
                dataGridPhotoDevices.LoadingRow += dataGridPhotoDevices_LoadingRow;
                dataGridPhotoSensors.LoadingRow += dataGridPhotoSensors_LoadingRow;
                TempSensorsHistory.CollectionChanged += TempSensorsHistory_CollectionChanged;
                TempDevicesHistory.CollectionChanged += TempDevicesHistory_CollectionChanged;
                this.Closing += new CancelEventHandler(MainWindow_Closing);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                // TODO show message to user
                this.Close();
            }
        }

        private void TempDevicesHistory_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //Messages.Add("TempDevicesHistory_CollectionChanged invoked");
            if (e.NewStartingIndex != 0)
            {
                TempDeviceLineChartAdd(); // first chart coded in .xaml
            }

            ((LineSeries)tempDevicesChart.Series[e.NewStartingIndex]).ItemsSource = TempDevicesHistory[e.NewStartingIndex];
        }

        private void TempSensorsHistory_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            //Messages.Add("TempSensorsHistory_CollectionChanged invoked");
            if (e.NewStartingIndex != 0)
            {
                TempSensorScatterChartAdd(); // first chart coded in .xaml
            }

            ((ScatterSeries)tempSensorsChart.Series[e.NewStartingIndex]).ItemsSource = TempSensorsHistory[e.NewStartingIndex];
        }

        private void UpdateAvailableComChannels(UsbEventListener.EventType type, string port)
        {
            try
            {
                if (type == UsbEventListener.EventType.Connect)
                {
                    ComPort newPort = new ComPort(new SerialPort(port));
                    if (!_comPorts.ContainsKey(port))
                    {
                        if (!_comPorts.ContainsKey(port))
                        {
                            _comPorts.Add(port, newPort);
                        }
                    }
                    else
                    {
                        //   _comPorts[port] = newPort;
                    }
                    listViewComChannels.Items.Add(port);
                }
                else
                {
                    if (!_comPorts.ContainsKey(port))
                    {
                        return;
                    }

                    ItemCollection items = listViewComChannels.Items;
                    for (int i = 0; i < items.Count; ++i)
                    {
                        string it = (string)items[i];
                        if (it == port)
                        {
                            try
                            {
                                if (it == _comPorts[_selectedComPort].SerPort.PortName)
                                {
                                    _comPorts[_selectedComPort].Disconnect();
                                }
                                else
                                {
                                    _comPorts[port].Disconnect();
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Log(ex);
                            }

                            listViewComChannels.Items.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        private void usbEventListener_UsbConnectEventArrived(UsbEventListener.EventType type, string port)
        {
            Invoke(() => UpdateAvailableComChannels(type, port));
        }

        private void listViewComChannels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ListView lv = (ListView)sender;
                if (lv == null || lv.SelectedIndex < 0)
                {
                    return;
                }
                if (_selectedComPort != null && _comPorts[_selectedComPort] != null)
                {
                    _comPorts[_selectedComPort].Disconnect();
                }

                string selectItem = (string)lv.SelectedItems[0];
                _selectedComPort = selectItem;

                Messages = new ObservableCollection<string>();
                TemperatureSensors = new ObservableCollection<TempSensor>();
                TempControlledDevices = new ObservableCollection<TempControlledDevice>();
                PhotoSensors = new ObservableCollection<PhotoSensor>();
                PhotoControlledDevices = new ObservableCollection<PhotoControlledDevice>();

                TempSensorsHistory = new ObservableCollection<ObservableCollection<KeyValuePair<DateTime, double>>>();
                TempSensorsHistory.CollectionChanged += TempSensorsHistory_CollectionChanged;
                int seriesCount = tempSensorsChart.Series.Count;
                for (int i = 1; i < seriesCount; ++i)
                {
                    ISeries s = tempSensorsChart.Series[1];
                    tempSensorsChart.Series.Remove(s);
                }

                TempDevicesHistory = new ObservableCollection<ObservableCollection<KeyValuePair<DateTime, int>>>();
                TempDevicesHistory.CollectionChanged += TempDevicesHistory_CollectionChanged;
                seriesCount = tempDevicesChart.Series.Count;
                for (int i = 1; i < seriesCount; ++i)
                {
                    ISeries s = tempDevicesChart.Series[1];
                    tempDevicesChart.Series.Remove(s);
                }

                listBoxLogs.ItemsSource = Messages;
                dataGridPhotoSensors.ItemsSource = PhotoSensors;
                dataGridTempSensors.ItemsSource = TemperatureSensors;
                dataGridTempDevices.ItemsSource = TempControlledDevices;
                dataGridPhotoDevices.ItemsSource = PhotoControlledDevices;

                _comPorts[_selectedComPort].OnMessageReceived -= _selectedComPort_OnMessageReceived;
                _comPorts[_selectedComPort].OnMessageReceived += new ComPort.MessageReceivedHandler(_selectedComPort_OnMessageReceived);
                _comPorts[_selectedComPort].Connect();
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        /// <summary>
        /// Handles Row loading event for dataGridTempDevices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridTempDevices_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            DataGridRow row = e.Row as DataGridRow;
            if (dataGridTempDevices.Columns[0].GetCellContent(row) == null)
            {
                return;
            }
            DataGridCell cellState = dataGridTempDevices.Columns[0].GetCellContent(row).Parent as DataGridCell;
            if (cellState != null)
            { // assume all cells are available if the first one is.
                DataGridCell cellDeviceIdx = dataGridTempDevices.Columns[2].GetCellContent(row).Parent as DataGridCell;
                Int32.TryParse(((TextBlock)cellDeviceIdx.Content).Text, out int deviceIdx);

                #region Set On/Off indicator cell
                if (TempControlledDevices[deviceIdx].Current_state)
                {
                    cellState.Background = new SolidColorBrush(ON_COLOR);
                }
                else
                {
                    cellState.Background = new SolidColorBrush(OFF_COLOR);

                }
                #endregion

                #region Set Temp Sensor index indicator cell
                DataGridCell cellSensor = dataGridTempDevices.Columns[3].GetCellContent(row).Parent as DataGridCell;
                int sensorIdx;
                Int32.TryParse(((TextBlock)cellSensor.Content).Text, out sensorIdx);
                cellSensor.BorderBrush = new SolidColorBrush(GetChartColor(sensorIdx));
                #endregion

                #region Set Photo Sensor index indicator cell
                cellSensor = dataGridTempDevices.Columns[11].GetCellContent(row).Parent as DataGridCell;
                Int32.TryParse(((TextBlock)cellSensor.Content).Text, out sensorIdx);
                cellSensor.BorderBrush = new SolidColorBrush(GetChartColor(sensorIdx));
                #endregion

                #region Set Error State indicator cell
                if (TemperatureSensors.Count > TempControlledDevices[deviceIdx].Temp_sensor_array_index)
                {
                    DataGridCell cellErrorState = dataGridTempDevices.Columns[9].GetCellContent(row).Parent as DataGridCell;
                    if (TemperatureSensors[TempControlledDevices[deviceIdx].Temp_sensor_array_index].Error_state != 0)
                    {
                        if (TempControlledDevices[deviceIdx].State_on_sensor_error == 1)
                        {
                            cellErrorState.Background = new SolidColorBrush(ON_COLOR);
                        }
                        else
                        {
                            cellErrorState.Background = new SolidColorBrush(OFF_COLOR);
                        }
                    }
                    else
                    {
                        DataGridCell cellTimeStamp = dataGridTempDevices.Columns[12].GetCellContent(row).Parent as DataGridCell;
                        cellErrorState.Background = cellTimeStamp.Background;
                    }
                }
                #endregion

                #region Set Temp Controlled Device index indicator cell
                cellDeviceIdx.BorderBrush = new SolidColorBrush(GetChartColor(deviceIdx));
                #endregion
            }
        }

        /// <summary>
        /// Handles Row loading event for dataGridTempSensors
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridTempSensors_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            DataGridRow row = e.Row as DataGridRow;
            if (dataGridTempSensors.Columns[0].GetCellContent(row) == null)
            {
                return;
            }

            DataGridCell cell = dataGridTempSensors.Columns[0].GetCellContent(row).Parent as DataGridCell;
            if (cell != null)
            {
                DataGridCell cellMax = dataGridTempSensors.Columns[4].GetCellContent(row).Parent as DataGridCell;
                DataGridCell cellMin = dataGridTempSensors.Columns[3].GetCellContent(row).Parent as DataGridCell;
                DataGridCell cellTimeStamp = dataGridTempSensors.Columns[8].GetCellContent(row).Parent as DataGridCell;
                DataGridCell cellArrayIdx = dataGridTempDevices.Columns[2].GetCellContent(row).Parent as DataGridCell;

                Int32.TryParse(((TextBlock)cellArrayIdx.Content).Text, out int tempSensorIdx);
                #region set Error State indicator cell
                if (TemperatureSensors[tempSensorIdx].Error_state != 0)
                {
                    cell.Background = new SolidColorBrush(ERROR_COLOR);

                    if (TemperatureSensors[tempSensorIdx].Reading >= TemperatureSensors[tempSensorIdx].Max_expected)
                    {
                        cellMin.Background = new SolidColorBrush(ERROR_COLOR);
                    }
                    else
                    {
                        cellMax.Background = new SolidColorBrush(ERROR_COLOR);
                    }
                }
                else
                {
                    cell.Background = cellTimeStamp.Background;
                    cellMax.Background = cellTimeStamp.Background;
                    cellMin.Background = cellTimeStamp.Background;
                }
                #endregion

                cellArrayIdx.BorderBrush = new SolidColorBrush(GetChartColor(tempSensorIdx));
            }
        }

        /// <summary>
        /// Handles Row loading event for dataGridPhotoDevices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridPhotoDevices_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            DataGridRow row = e.Row as DataGridRow;
            if (dataGridPhotoDevices.Columns[0].GetCellContent(row) == null)
            {
                return;
            }
            DataGridCell cellState = dataGridPhotoDevices.Columns[0].GetCellContent(row).Parent as DataGridCell;
            if (cellState != null)
            { // assume all cells are available if the first one is.
                DataGridCell cellArrayIdx = dataGridTempDevices.Columns[2].GetCellContent(row).Parent as DataGridCell;
                if (Int32.TryParse(((TextBlock)cellArrayIdx.Content).Text, out int deviceIdx))
                {

                    #region Set On/Off indicator cell
                    if (PhotoControlledDevices[deviceIdx].Current_state)
                    {
                        cellState.Background = new SolidColorBrush(ON_COLOR);
                    }
                    else
                    {
                        cellState.Background = new SolidColorBrush(OFF_COLOR);

                    }
                    #endregion

                    #region Set Photo Controlled Device index indicator cell
                    cellArrayIdx.BorderBrush = new SolidColorBrush(GetChartColor(deviceIdx));
                    #endregion
                }

                #region Set Photo Sensor index indicator cell
                DataGridCell cellPhtSnsrIdx = dataGridPhotoDevices.Columns[3].GetCellContent(row).Parent as DataGridCell;
                if (Int32.TryParse(((TextBlock)cellPhtSnsrIdx.Content).Text, out int photoSensorIdx))
                {
                    cellPhtSnsrIdx.BorderBrush = new SolidColorBrush(GetChartColor(photoSensorIdx));
                }
                #endregion
            }
        }

        /// <summary>
        /// Handles Row loading event for dataGridPhotoSensors
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dataGridPhotoSensors_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            DataGridRow row = e.Row as DataGridRow;
            if (dataGridPhotoSensors.Columns[0].GetCellContent(row) == null)
            {
                return;
            }
            DataGridCell cellReading = dataGridPhotoSensors.Columns[0].GetCellContent(row).Parent as DataGridCell;

            if (cellReading != null)
            {
                #region Set Photo Sensor index indicator cell
                DataGridCell cellArrayIdx = dataGridTempDevices.Columns[2].GetCellContent(row).Parent as DataGridCell;
                int arrayIdx;
                Int32.TryParse(((TextBlock)cellArrayIdx.Content).Text, out arrayIdx);
                cellArrayIdx.BorderBrush = new SolidColorBrush(GetChartColor(arrayIdx));
                #endregion
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Make sure to null Self on window close.  Cross-thread events will hang otherwise
            try
            {
                _usbEventListener.Stop(); // this SEEMS to work, another solution not needed, micro-manage RCW easier at this time, violates separation of concerns 
                                          //Refer here http://stackoverflow.com/questions/2085972/release-excel-object-in-my-destructor for another solution to prevent micro-managing and violations to sepearation of concerns

                //http://msdn.microsoft.com/en-us/library/system.io.ports.serialport.cdholding(v=vs.110).aspx
                //http://msdn.microsoft.com/en-us/library/system.io.ports.serialport.breakstate(v=vs.110).aspx
                if (_selectedComPort != null && _comPorts[_selectedComPort] != null)
                {
                    _comPorts[_selectedComPort].Disconnect();
                    _comPorts[_selectedComPort] = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }



        private Color GetChartColor(int chartColorIdx)
        {
            return _chartColors[chartColorIdx % _chartColorsCount];
        }

        private TimeSpan GetChartOffset(DateTime first, DateTime last)
        {
            TimeSpan r = last - first;
            TimeSpan range = new TimeSpan(r.Hours, r.Minutes, 0);

            if (range < _minChartRangeX)
            {
                return _minChartRangeX;
            }

            return range;
        }

        private double GetChartInterval_X(double minutes)
        {
            if (minutes < 240)
            {
                return 15;
            }
            else if (minutes < 300)
            {
                return 30;
            }
            else if (minutes < 420)
            {
                return 45;
            }
            else if (minutes < 720)
            {
                return 60;
            }
            else if (minutes < 1080)
            {
                return 90;
            }
            return 120;
        }



        private void TempDeviceAddReading(TempControlledDevice tempDevice)
        {
            #region update chart
            try
            {
                int tdIdx = tempDevice.Array_index;
                lock (TempControlledDevices)
                {
                    lock (TempDevicesHistory)
                    {
                        if (TempControlledDevices.Count > tempDevice.Array_index)
                        { // update already existing Temp Device
                          //Messages.Add("Updating TempDevice[" + tempDevice.Array_index + "].Count: " + tdhCnt);
                            TempControlledDevices[tdIdx] = tempDevice;
                        }
                        else if (TempControlledDevices.Count == tempDevice.Array_index)
                        { // This is a new Temp Device
                          //Messages.Add("Adding new TempDevice[" + tempDevice.Array_index + "]");
                            TempControlledDevices.Add(tempDevice);
                        }
                        else
                        {
                            return; // Devices may come in out of order. First reading may be missed.
                        }

                        int onStateIdx = tempDevice.Current_state == true ? tempDevice.Array_index + 1 : 0; // Y-Axis plot point
                        KeyValuePair<DateTime, int> newHistory = new KeyValuePair<DateTime, int>(tempDevice.Time_stamp, onStateIdx);
                        if (TempDevicesHistory.Count > tempDevice.Array_index)
                        { // update already existing Temp Device
                            int tdhCnt = TempDevicesHistory[tdIdx].Count();
                            //Messages.Add("Updating TempDevicesHistory[" + tempDevice.Array_index + "].Count: " + tdhCnt);
                            // NOTE: Order of add/removal of items is important (Key is a DateTime), expected to be in order
                            if (TempDevicesHistory[tdIdx].Last().Value == newHistory.Value)
                            {
                                if ((tdhCnt > 2 && TempDevicesHistory[tdIdx][tdhCnt - 2].Value == newHistory.Value) || (tdhCnt == 2))
                                {
                                    // On/Off State has NOT changed for the last three readings, remove last item, add the new item
                                    TempDevicesHistory[tdIdx].RemoveAt(tdhCnt - 1);
                                }
                            }// else { // if (TempDevicesHistory[tdIdx].Last().Value != newHistory.Value) {
                             // On/Off State HAS changed, add an item just before (datetime) the new item with the same value to prevent diagonal vertical lines on state change.
                             // Essentially moving the current item to just before the new, about to be added item
                             //TempDevicesHistory[tdIdx].RemoveAt(tdhCnt - 1);
                             //TempDevicesHistory[tdIdx].Add(new KeyValuePair<DateTime, int>(newHistory.Key.AddMilliseconds(-1), TempDevicesHistory[tdIdx].Last().Value));
                             // OR do nothing, have diagonal lines on state change
                             // }

                            TempDevicesHistory[tdIdx].Add(newHistory);

                            if (TempDevicesHistory[tdIdx].Count() > 3)
                            {
                                //Messages.Add("TempDevicesHistory[tdIdx].Count() > 3 -> TempDevicesHistory[" + tempDevice.Array_index + "].Count: " + tdhCnt);
                                DateTime? chartfirstTime = (DateTime?)tempDevicesChart?.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Minimum;
                                if (chartfirstTime != null && TempDevicesHistory[tdIdx][1].Key < chartfirstTime)
                                {
                                    // There are two points off the chart, remove one. // && TempDevicesHistory[tdIdx].ElementAt(0).Key < chartfirstTime 
                                    // Could check to see of there are more, but should not be necessary as the collection is added to one by one.
                                    TempDevicesHistory[tdIdx].RemoveAt(0);
                                }
                            }
                        }
                        else if (TempDevicesHistory.Count == tempDevice.Array_index)
                        { // This is a new Temp Device
                          //Messages.Add("Adding new TempDevicesHistory[" + tempDevice.Array_index + "], TempDevicesHistory.Count=" + TempDevicesHistory.Count);
                            ObservableCollection<KeyValuePair<DateTime, int>> newHistories = new ObservableCollection<KeyValuePair<DateTime, int>> {
                            newHistory,
                            new KeyValuePair<DateTime, int> (tempDevice.Time_stamp.AddMilliseconds(-1), newHistory.Value) // Line charts appear to require at least 2 points or it locks up the Application window
                        };
                            TempDevicesHistory.Add(newHistories);
                        }
                        else
                        {
                            // This is a new Device, but has come in out of order, this reading will not be charted.
                            return;
                        }

                        //Messages.Add("TempDevicesHistory[" + tempDevice.Array_index + "].Count: " + TempDevicesHistory[tdIdx].Count);
                        //Messages.Add("TempDevicesHistory[" + tempDevice.Array_index + "] first time: " + TempDevicesHistory[tdIdx][0].Key);
                        //Messages.Add("TempDevicesHistory[" + tempDevice.Array_index + "] last time: " + TempDevicesHistory[tdIdx][TempDevicesHistory[tdIdx].Count - 1].Key);

                        if (tempDevice.Array_index == 0)
                        {
                            TempDeviceChartSetXAxisRange();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, null);
            }
            #endregion
        }

        private void TempDeviceChartSetXAxisRange()
        {
            if (_syncChartXAxis == true)
            {
                if (TempSensorsHistory.Count == 0 || TempSensorsHistory[0].Count == 0)
                {
                    return;
                }

                DateTime firstTime = TempSensorsHistory[0].First<KeyValuePair<DateTime, double>>().Key;
                DateTime lastTime = TempSensorsHistory[0].Last<KeyValuePair<DateTime, double>>().Key;

                DateTime chartLastTime = lastTime + _oneMinute;
                DateTime posChartFirstTime = chartLastTime - (GetChartOffset(firstTime, chartLastTime));
                DateTime chartFirstTime = posChartFirstTime < firstTime ? posChartFirstTime : firstTime;


                //Messages.Add("TempSensorChartSetXAxisRange Syncing -> chartFirstTime: " + chartFirstTime);
                //Messages.Add("TempSensorChartSetXAxisRange Syncing -> chartLastTime: " + chartLastTime);

                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Maximum = chartLastTime;
                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Minimum = chartFirstTime;
                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Interval = GetChartInterval_X((chartLastTime - chartFirstTime).TotalMinutes);
            }
            else
            { // if (_syncChartXAxis == false) {  // --------------------------------- UNTESTED  ----------------------------------------------------------------------------------------------------------------
                if (TempDevicesHistory.Count == 0 || TempDevicesHistory[0].Count == 0 || TempSensorsHistory.Count == 0 || TempSensorsHistory[0].Count == 0)
                {
                    return;
                }

                DateTime firstTime = TempDevicesHistory[0].First<KeyValuePair<DateTime, int>>().Key;
                DateTime lastTime = TempDevicesHistory[0].Last<KeyValuePair<DateTime, int>>().Key;

                DateTime chartLastTime = lastTime + _oneMinute;
                DateTime posChartFirstTime = chartLastTime - (GetChartOffset(firstTime, chartLastTime));
                DateTime chartFirstTime = posChartFirstTime < firstTime ? posChartFirstTime : firstTime;

                //Messages.Add("TempSensorChartSetXAxisRange Non-syncing -> chartFirstTime: " + chartFirstTime);
                //Messages.Add("TempSensorChartSetXAxisRange Non-Syncing -> chartLastTime: " + chartLastTime);

                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Maximum = chartLastTime;
                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Minimum = chartFirstTime;
                tempDevicesChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Interval = GetChartInterval_X((chartLastTime - chartFirstTime).TotalMinutes);
            }
        }

        private void TempDeviceLineChartAdd()
        {
            //Messages.Add("TempDeviceLineChartAdd invoked");
            SolidColorBrush brush = new SolidColorBrush(GetChartColor(tempDevicesChart.Series.Count));

            AreaSeries aSeries = new AreaSeries();
            aSeries.DependentValueBinding = new Binding("Value");
            aSeries.IndependentValueBinding = new Binding("Key");

            Style dataPointStyle = new Style(typeof(LineDataPoint));
            dataPointStyle.Setters.Add(new Setter(DataPoint.BackgroundProperty, brush));
            dataPointStyle.Setters.Add(new Setter(DataPoint.BorderBrushProperty, brush));
            dataPointStyle.Setters.Add(new Setter(DataPoint.ForegroundProperty, brush));
            dataPointStyle.Setters.Add(new Setter(DataPoint.HeightProperty, 3.0)); // Interger values do not work for some reason
            dataPointStyle.Setters.Add(new Setter(DataPoint.WidthProperty, 3.0)); // Interger values do not work for some reason


            Style polyLineStyle = new Style(typeof(Polyline), (Style)Resources["polyLineStyle"]);
            polyLineStyle.Setters.Add(new Setter(Polyline.StrokeProperty, brush));

            LineSeries line = new LineSeries() { // TODO change to use a Resource defined in .xaml
                Title = "Device " + (tempDevicesChart.Series.Count + 1),
                DependentValuePath = "Y",
                IndependentValuePath = "X",
                IndependentValueBinding = aSeries.IndependentValueBinding,
                DependentValueBinding = aSeries.DependentValueBinding,
                DataPointStyle = dataPointStyle,
                Opacity = 1.0,
                PolylineStyle = polyLineStyle,
            };

            tempDevicesChart.Series.Add(line);
            tempDevicesChart.ActualAxes.OfType<LinearAxis>().FirstOrDefault(ay => ay.Orientation == AxisOrientation.Y).Maximum = TempDevicesHistory.Count + 1;
            tempDevicesChart.ActualAxes.OfType<LinearAxis>().FirstOrDefault(ay => ay.Orientation == AxisOrientation.Y).Minimum = 0;
        }

        private void TempSensorAddReading(TempSensor tempSensor)
        {
            #region update chart
            lock (TemperatureSensors)
            {
                lock (TempSensorsHistory)
                {
                    // Temperature Sensors Grid data handling
                    if (TemperatureSensors.Count > tempSensor.Array_index)
                    { // update already existing
                        TemperatureSensors[tempSensor.Array_index] = tempSensor;
                    }
                    else if (TemperatureSensors.Count - tempSensor.Array_index == 0)
                    { // new insert, Sensors may come in out of order
                        TemperatureSensors.Add(tempSensor);
                    }
                    else
                    {
                        // Prevent out of range exception on observable collections as code uses array indexes and sensors can come in out of order
                        return;
                    }
                    #endregion

                    // Tempature Sensor Chart data handling
                    KeyValuePair<DateTime, double> newHistory = new KeyValuePair<DateTime, double>(tempSensor.Time_stamp, tempSensor.Reading);
                    if (TempSensorsHistory.Count > tempSensor.Array_index)
                    { // update an existing TempSensorsHistory
                        if (newHistory.Key - _chartRefreshRate > TempSensorsHistory[tempSensor.Array_index].Last<KeyValuePair<DateTime, double>>().Key)
                        {
                            if (TempSensorsHistory[tempSensor.Array_index].Count == _maxChartReadings)
                            {
                                // Remove earliest entry to maintain collection size, update existing sensor history
                                TempSensorsHistory[tempSensor.Array_index].RemoveAt(0);
                                TempSensorsHistory[tempSensor.Array_index].Add(newHistory);
                            }
                            else if (TempSensorsHistory[tempSensor.Array_index].Count > _maxChartReadings)
                            {
                                Logger.Log(null, "TempSensorsHistory[tempSensor.Array_index].Count > _maxChartReadings");
                            }
                            else
                            { // add the new entry to the sensor history
                                TempSensorsHistory[tempSensor.Array_index].Add(newHistory);
                            }
                        }
                    }
                    else if (TempSensorsHistory.Count == tempSensor.Array_index)
                    { // this is a new sensor history
                        ObservableCollection<KeyValuePair<DateTime, double>> newHistories = new ObservableCollection<KeyValuePair<DateTime, double>>();
                        newHistories.Add(newHistory);
                        TempSensorsHistory.Add(newHistories);
                    }

                    if (tempSensor.Array_index == 0)
                    {
                        //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "].Count: " + TempSensorsHistory[tempSensor.Array_index].Count);
                        //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "] first time: " + TempSensorsHistory[tempSensor.Array_index][0].Key);
                        //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "] last time: " + TempSensorsHistory[tempSensor.Array_index][TempSensorsHistory[tempSensor.Array_index].Count - 1].Key);
                        TempSensorChartSetXAxisRange();
                    }

                    if (tempSensor.Array_index == TempSensorsHistory.Count() - 1)
                    {
                        TempSensorsChartSetYAxisRange();
                    }
                }
            }

            //if (tempSensor.Array_index == 0) {
            //    //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "].Count: " + TempSensorsHistory[tempSensor.Array_index].Count);
            //    //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "] first time: " + TempSensorsHistory[tempSensor.Array_index][0].Key);
            //    //Messages.Add("TempSensorsHistory[" + tempSensor.Array_index + "] last time: " + TempSensorsHistory[tempSensor.Array_index][TempSensorsHistory[tempSensor.Array_index].Count - 1].Key);
            //    TempSensorChartSetXAxisRange();
            //}
        }

        private void TempSensorChartSetXAxisRange()
        {
            if (TempSensorsHistory.Count == 0 || TempSensorsHistory[0].Count == 0)
            {
                return;
            }

            DateTime firstTime = TempSensorsHistory[0].First<KeyValuePair<DateTime, double>>().Key;
            DateTime lastTime = TempSensorsHistory[0].Last<KeyValuePair<DateTime, double>>().Key;

            DateTime chartLastTime = lastTime + _oneMinute;
            DateTime posChartFirstTime = chartLastTime - (GetChartOffset(firstTime, chartLastTime));
            DateTime chartFirstTime = posChartFirstTime < firstTime ? posChartFirstTime : firstTime;

            //Messages.Add("TempSensorChartSetXAxisRange -> chartFirstTime: " + chartFirstTime);
            //Messages.Add("TempSensorChartSetXAxisRange -> chartLastTime: " + chartLastTime);

            tempSensorsChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Maximum = chartLastTime;
            tempSensorsChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Minimum = chartFirstTime;
            tempSensorsChart.ActualAxes.OfType<DateTimeAxis>().FirstOrDefault(ax => ax.Orientation == AxisOrientation.X).Interval = GetChartInterval_X((chartLastTime - chartFirstTime).TotalMinutes);
        }

        private void TempSensorsChartSetYAxisRange()
        {
            if (TempSensorsHistory[0].Count == 0)
            { // No point to set with, use ConfigOption defaults.
                tempSensorsChart.ActualAxes.OfType<LinearAxis>().FirstOrDefault(ay => ay.Orientation == AxisOrientation.Y).Maximum = ChartOptions[0].MaxChartY;
                tempSensorsChart.ActualAxes.OfType<LinearAxis>().FirstOrDefault(ay => ay.Orientation == AxisOrientation.Y).Minimum = ChartOptions[0].MinChartY;
                return;
            }

            double? newMax = null;
            double? newMin = null;
            foreach (ObservableCollection<KeyValuePair<DateTime, double>> tsh in TempSensorsHistory)
            {
                double maxValue = tsh.Max(x => x.Value);
                if (newMax == null || maxValue > newMax)
                {
                    newMax = maxValue;
                }

                double minValue = tsh.Min(x => x.Value);
                if (newMin == null || minValue < newMin)
                {
                    newMin = minValue;
                }
            }

            LinearAxis yAxis = tempSensorsChart.ActualAxes.OfType<LinearAxis>().FirstOrDefault(ay => ay.Orientation == AxisOrientation.Y);
            newMax = Math.Ceiling((double)newMax);
            if (newMax != null && yAxis.Maximum != (int)newMax)
            {
                yAxis.Maximum = ((int)newMax);
            }

            newMin = (int)Math.Floor((double)newMin);
            if (newMin != null && yAxis.Minimum != newMin)
            {
                yAxis.Minimum = (int)newMin;
            }
        }

        private void TempSensorScatterChartAdd()
        {
            SolidColorBrush brush = new SolidColorBrush(GetChartColor(tempSensorsChart.Series.Count));
            Style style = new Style(typeof(ScatterDataPoint));

            style.Setters.Add(new Setter(DataPoint.BackgroundProperty, brush));
            style.Setters.Add(new Setter(DataPoint.BorderBrushProperty, brush));
            style.Setters.Add(new Setter(DataPoint.ForegroundProperty, brush));
            style.Setters.Add(new Setter(DataPoint.HeightProperty, 1.0));
            style.Setters.Add(new Setter(DataPoint.WidthProperty, 3.0));

            AreaSeries aSeries = new AreaSeries();
            aSeries.IndependentValueBinding = new System.Windows.Data.Binding("Key");
            aSeries.DependentValueBinding = new System.Windows.Data.Binding("Value");

            ScatterSeries line = new ScatterSeries() {  // TODO change to use a Resource defined in .xaml
                Title = "Sensor " + (tempSensorsChart.Series.Count + 1),
                DependentValuePath = "Y",
                IndependentValuePath = "X",
                IndependentValueBinding = aSeries.IndependentValueBinding,
                DependentValueBinding = aSeries.DependentValueBinding,
                DataPointStyle = style,
                Opacity = 1.0,
            };
            tempSensorsChart.Series.Add(line);
        }

        private void AddMessage(string message)
        {
            try
            {
                Messages.Add("Message Received: " + message);
                if (message[0] == '{')
                {

                    dynamic objectMessage = JsonConvert.DeserializeObject(message);

                    if (objectMessage.PhotoSensor != null)
                    {
                        PhotoSensor photoSensor = new PhotoSensor();

                        photoSensor.Array_index = objectMessage.PhotoSensor.array_index;
                        photoSensor.Pin = objectMessage.PhotoSensor.pin;
                        photoSensor.Reading = objectMessage.PhotoSensor.reading;
                        photoSensor.Time_stamp = DateTime.Now;

                        if (PhotoSensors.Count > photoSensor.Array_index)
                        {
                            PhotoSensors[photoSensor.Array_index] = photoSensor;
                        }
                        else if (PhotoSensors.Count <= photoSensor.Array_index)
                        {
                            PhotoSensors.Add(photoSensor);
                        }
                    }
                    else if (objectMessage.TempSensor != null)
                    { // has an issue with not catching the first "Message" and trying to insert to indexes > 0 first (can happen on a switch when messages are being received)
                        TempSensor tempSensor = new TempSensor();

                        tempSensor.Pin = objectMessage.TempSensor.pin;
                        tempSensor.Reading = objectMessage.TempSensor.reading;
                        tempSensor.Max_expected = objectMessage.TempSensor.max_expected_temp;
                        tempSensor.Min_expected = objectMessage.TempSensor.min_expected_temp;
                        tempSensor.Error_state = objectMessage.TempSensor.error_state;
                        tempSensor.Error_count = objectMessage.TempSensor.error_count;
                        tempSensor.Array_index = objectMessage.TempSensor.array_index;
                        tempSensor.Temp_offset = objectMessage.TempSensor.temp_offset;
                        tempSensor.Time_stamp = DateTime.Now;
                        TempSensorAddReading(tempSensor);
                    }
                    else if (objectMessage.TempControlledDevice != null)
                    { // has an issue with not catching the first "Message" and trying to insert to indexes > 0 first (can happen on a switch when messages are being received)
                        TempControlledDevice temp = new TempControlledDevice();

                        temp.Current_state = objectMessage.TempControlledDevice.current_state;
                        temp.Day_time_switch_temp = objectMessage.TempControlledDevice.day_time_switch_temp;
                        temp.Night_time_switch_temp = objectMessage.TempControlledDevice.night_time_switch_temp;
                        temp.Output_pin = objectMessage.TempControlledDevice.output_pin;
                        temp.Photo_sensor_array_index = objectMessage.TempControlledDevice.photo_sensor_array_index;
                        temp.Photo_sensor_switch_reading = objectMessage.TempControlledDevice.photo_sensor_switch_reading;
                        temp.State_on_sensor_error = objectMessage.TempControlledDevice.state_on_sensor_error;
                        temp.Temp_sensor_array_index = objectMessage.TempControlledDevice.temp_sensor_array_index;
                        temp.Temp_sensor_state_when_on = objectMessage.TempControlledDevice.temp_sensor_state_when_on;
                        temp.Temp_range = objectMessage.TempControlledDevice.temp_range;
                        temp.Array_index = objectMessage.TempControlledDevice.array_index;
                        temp.Time_stamp = DateTime.Now;

                        if (TemperatureSensors.Count > temp.Temp_sensor_array_index)
                        {
                            temp.Sensor_reading = TemperatureSensors[temp.Temp_sensor_array_index].Reading.ToString("#00.00");
                        }
                        else
                        {
                            temp.Sensor_reading = "N/A";
                        }

                        TempDeviceAddReading(temp);
                    }
                    else if (objectMessage.PhotoControlledDevice != null)
                    { // has an issue with not catching the first "Message" and trying to insert to indexes > 0 first (can happen on a switch when messages are being received)
                        PhotoControlledDevice temp = new PhotoControlledDevice();

                        temp.Current_state = objectMessage.PhotoControlledDevice.current_state;
                        temp.Day_time_state_on = objectMessage.PhotoControlledDevice.day_time_state_on;
                        temp.Output_pin = objectMessage.PhotoControlledDevice.output_pin;
                        temp.Photo_sensor_array_index = objectMessage.PhotoControlledDevice.photo_sensor_array_index;
                        temp.Switch_reading = objectMessage.PhotoControlledDevice.switch_reading;
                        temp.Array_index = objectMessage.PhotoControlledDevice.array_index;
                        temp.Time_stamp = DateTime.Now;

                        if (PhotoControlledDevices.Count > temp.Array_index)
                        {  // update already existing
                            PhotoControlledDevices[temp.Array_index] = temp;
                        }
                        else if (PhotoControlledDevices.Count <= temp.Array_index)
                        {  // new insert
                            PhotoControlledDevices.Add(temp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex, "Processing Message: " + message);
                return;
            }
        }

        private void WriteConfigFile()
        {
            try
            {
                string appSettings = JsonConvert.SerializeObject(_appSettings);
                File.WriteAllText(_configPath, appSettings);
            }
            catch (Exception ex)
            {
                Logger.Log(ex, null);
            }
        }

        private void UseApplicationSettings()
        {
            _maxChartReadings = _appSettings["MAX_CHART_READINGS"] == null ? _maxChartReadings : Int32.Parse(_appSettings["MAX_CHART_READINGS"].ToString());
            _maxMessageCount = _appSettings["MAX_MESSAGE_COUNT"] == null ? _maxMessageCount : Int32.Parse(_appSettings["MAX_MESSAGE_COUNT"].ToString());
            _chartRefreshRate = _appSettings["CHART_REFRESH_RATE"] == null ? _chartRefreshRate : TimeSpan.Parse(_appSettings["CHART_REFRESH_RATE"].ToString());
            _minChartRangeX = _appSettings["MIN_CHART_RANGE_X"] == null ? _minChartRangeX : TimeSpan.Parse(_appSettings["MIN_CHART_RANGE_X"].ToString());
            if (_appSettings["MAX_CHART_Y"] != null && _appSettings["MIN_CHART_Y"] != null)
            {
                ChartOptions = new ObservableCollection<ChartOption>
                {
                    new ChartOption{
                        MaxChartY = Int32.Parse(_appSettings["MAX_CHART_Y"].ToString()),
                        MinChartY = Int32.Parse(_appSettings["MIN_CHART_Y"].ToString())
                    }
                };
            }
        }

        private void SetupDefaults()
        {
            try
            {
                UseApplicationSettings();
                if (!File.Exists(_configPath))
                {
                    WriteConfigFile();
                    return;
                }

                string appSettings = File.ReadAllText(_configPath);
                _appSettings = JsonConvert.DeserializeObject<Dictionary<string, object>>(appSettings);
                UseApplicationSettings();
            }
            catch (Exception ex)
            {
                Logger.Log(ex, null);
            }
        }




        /// <summary>
        /// Used to run an asyncronous action on the GUI thread 
        /// </summary>
        /// <param name="action">Action to run</param>
        public static void BeginInvoke(Action action)
        {
            Application.Current.Dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Used to run an syncronous action on the GUI thread 
        /// </summary>
        /// <param name="action">Action to run</param>
        public static void Invoke(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }

        /// <summary>
        /// Message recieved from Arduino
        /// Limits size of Messages collection.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="message"></param>
        public void _selectedComPort_OnMessageReceived(object sender, string message)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(message) == true)
                {
                    Logger.Log(null, "_selectedComPort_OnMessageReceived -> message null or empty");
                    return;
                }

                // Keep Messages.Count from exceeding Max Items for ObservableCollection
                if (Messages.Count > _maxMessageCount)
                {
                    int numToRemove = Messages.Count - _maxMessageCount;
                    for (int i = 0; i < numToRemove; ++i)
                    {
                        Invoke(() => Messages.RemoveAt(0));
                    }
                }
                Invoke(() => AddMessage(message));
            }
            catch (Exception ex)
            {
                Logger.Log(ex, null);
            }
        }
    } // end MainWindow

    public class Logger {
        private static string _logPath = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) + @"\errorlog.txt";

        public static void Log(Exception ex, string msg = null)
        {
            if (ex == null && msg == null)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(new String('-', 50));
            sb.AppendLine("Date: " + DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToLongTimeString());
            if (!String.IsNullOrWhiteSpace(msg))
            {
                sb.AppendLine("Application Message: " + msg);
            }

            #region exception output
            if (ex != null)
            {
                Type exType = ex.GetType();
                sb.AppendLine("Message: " + ex.Message);
                sb.AppendLine("Type: " + ex.GetType());
                sb.AppendLine("Target Site : " + ex.TargetSite);
                sb.AppendLine("Source: " + ex.Source);
                sb.AppendLine("Stack Track: " + ex.StackTrace);
            }
            #endregion

            sb.AppendLine(new String('-', 50));
            sb.AppendLine("");

            try
            {
                File.AppendAllText(_logPath, sb.ToString());
            }
            catch (Exception)
            {
                // crop, no log for this getting generated
            }

            if (ex != null && ex.InnerException != null)
            {
                Log(ex.InnerException, "Inner Exception");
            }
        }
    }

    #region IValueConverter Classes
    [ValueConversion(typeof(string), typeof(bool))]
    public class OnOfConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            // test the paramater to apply correct formatting or default string for defined values.
            if (parameter.ToString().ToLower() == "state")
            {
                bool v = bool.Parse(value.ToString());
                if (v)
                    return "On";
                else
                    return "Off";
            }
            return "convert not defined - OnOfConverter";
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }

    [ValueConversion(typeof(string), typeof(int))]
    public class StateConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (int.TryParse(value.ToString(), out int v))
            {
                switch (parameter.ToString())
                {
                    case "state":
                        {
                            if (v == 1)
                            {
                                return "Above reading";
                            }
                            else if (v == 0)
                            {
                                return "Below reading";
                            }
                            else
                            {
                                return "convert not defined for number " + v + " - StateConverter.Convert";
                            }
                        }
                    case "error_state":
                        {
                            if (v > 0)
                            {
                                return "Error!";
                            }
                            else
                            {
                                return "Good";
                            }
                        }
                    case "on_error_state":
                        {
                            if (v == 1)
                            {
                                return "On";
                            }
                            else if (v == 0)
                            {
                                return "Off";
                            }
                            else
                            {
                                return "convert not defined for number " + v + " - StateConverter.Convert";
                            }
                        }
                    default:
                        return "convert not defined for parameter - StateConverter.Convert";
                }
            }
            return "value failed to convert - StateConverter.Convert";
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
            //return null;
        }
    }

    [ValueConversion(typeof(string), typeof(double))]
    public class DoubleConverterFixedTwo : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (Double.TryParse(value.ToString(), out double v))
            {
                return v.ToString("0.00");
            }
            else
            {
                return "value failed to convert - DoubleConverterFixedTwo.Convert";
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(string), typeof(DateTime))]
    public class DateTimeConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            try
            {
                return ((DateTime)value).ToString("HH:mm:ss");
            }
            catch (InvalidCastException ex)
            {
                Logger.Log(ex, null);
                return "value failed to convert - DateTimeConverter.Convert";
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion
} // end Namespace
