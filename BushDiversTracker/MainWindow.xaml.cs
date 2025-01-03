using BushDiversTracker.Models;
using BushDiversTracker.Models.Enums;
using BushDiversTracker.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.FlightSimulator.SimConnect;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using System.Windows.Interop;
using BushDiversTracker.Models.NonApi;
using System.Globalization;
using AutoUpdaterDotNET;
using System.Reflection;

namespace BushDiversTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        APIService _api;
        AddonBrowser _addonBrowser;

        private enum MessageState
        {
            OK,
            Neutral,
            Error
        }

        public MainWindow()
        {
            InitializeComponent();

            // Initialise visibility here to help UI editability
            lblErrorText.Visibility = Visibility.Hidden;
            grpFlight.Visibility = Visibility.Hidden;
            lblDeadHead.Visibility = Visibility.Hidden;

            txtPirep.Visibility = Visibility.Hidden;
            txtDepLat.Visibility = Visibility.Hidden;
            txtDepLon.Visibility = Visibility.Hidden;
            txtArrLat.Visibility = Visibility.Hidden;
            txtArrLon.Visibility = Visibility.Hidden;
            lblDepartureError.Visibility = Visibility.Hidden;
            lblCargoError.Visibility = Visibility.Hidden;
            lblAircraftError.Visibility = Visibility.Hidden;
            lblFuelError.Visibility = Visibility.Hidden;

            btnStop.Visibility = Visibility.Hidden;
            lblFetch.Visibility = Visibility.Hidden;
            lblStart.Visibility = Visibility.Hidden;
            lblEnd.Visibility = Visibility.Hidden;
            lblDistanceLabel.Visibility = Visibility.Hidden;
            lblDistance.Visibility = Visibility.Hidden;
            lblSubmitting.Visibility = Visibility.Hidden;

            txtKey.Password = Properties.Settings.Default.Key;
            _api = new APIService();
            if (!System.Diagnostics.Debugger.IsAttached)
            {
              AutoUpdater.Start("https://bushdivers-resource.s3.amazonaws.com/bush-tracker/bushtracker-info.xml");
            }
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            lblVersion.Content = version;

            if (Properties.Settings.Default.UseMetric)
                rdoUnitMetric.IsChecked = true;
            else
                rdoUnitUS.IsChecked = true;
        }

#region Sim_Connect

        CultureInfo Eng = CultureInfo.GetCultureInfo("en-GB");

        private DispatcherTimer timer;

        // Bush Tracker variables
        private Dispatch dispatchData = null;
        protected bool bDispatch = false;
        private bool bConnected = false;
        private bool bFlightTracking = false;
        private bool bEndFlight = false;
        private bool bReady = false;
        private bool bEnginesRunning = false;
        private bool bFlightCompleted = false;
        private bool fuelErrorShown = false;
        private bool bFirstData = true;
        private bool bLastEngineStatus;
        private double startLat;
        private double startLon;
        private double endLat;
        private double endLon;
        private double lastLat;
        private double lastLon;
        private double currentDistance = 0;
        private double startFuelQty;
        private double endFuelQty;
        private double currentFuelQty;
        private string startTime;
        private string endTime;
        private PirepStatusType flightStatus;
        protected double lastHeading;
        protected double lastAltitude;
        protected double landingRate;
        protected double landingBank;
        protected double landingGforce;
        protected double landingPitch;
        protected double landingLat;
        protected double landingLon;
        protected double lastVs;
        protected double lastGforce;
        protected bool lastOnground;
        protected DateTime dataLastSent;
        protected string aircraftName;

        // sim connect setup variables
        SimConnect simConnect = null;
        const int WM_USER_SIMCONNECT = 0x0402;
        SimVersion? version = null; 

        enum DEFINITIONS
        {
            Struct1,
            LandingStruct
        }
        enum DAT_REQUESTS
        {
            REQUEST_1,
            REQUEST_2,
        }

        // items to set in sim
        enum SET_DATA
        {
            ATC_ID
            // TODO: Fuel
            //LEFT_FUEL,
            //RIGHT_FUEL
        }

        // TODO: for events
        //enum EVENT_ID
        //{
        //    EVENT_PAUSED,
        //    EVENT_UNPAUSED,
        //}

        enum SimVersion
        {
            FS2020,
            FS2024
        }

        // Sim variables
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct Struct1
        {
            // variables to bind to simconnect simvars
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string title;
            public double latitude;
            public double longitude;
            public double indicated_altitude;
            public double plane_altitude;
            public double ac_pitch;
            public double ac_bank;
            public double airspeed_true;
            public double airspeed_indicated;
            public double vspeed;
            public double heading_m;
            public double heading_t;
            public double gforce;
            public int eng1_combustion;
            public int eng2_combustion;
            public int eng3_combustion;
            public int eng4_combustion;
            public double aircraft_max_rpm;
            public double max_rpm_attained;
            public int zulu_time;
            public int local_time;
            public int on_ground;
            public int surface_type;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string atcId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string atcType;
            public double fuel_qty;
            public double fuelsystem_tank1_capacity;
            public double unusable_fuel_qty;
            public int is_overspeed;
            public int is_unlimited;
            public int payload_station_count;
            public double payload_station_weight;
            public double max_g;
            public double min_g;
            public double eng_damage_perc;
            //public int flap_damage;
            //public int gear_damage;
            //public int flap_speed_exceeded;
            //public int gear_speed_exceeded;
            public int eng_mp;
            public int fuel_flow;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
            public string atcModel;
            public double total_weight;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct LandingStruct
        {
            public double touchdown_bank;
            public double touchdown_heading_m;
            public double touchdown_heading_t;
            public double touchdown_lat;
            public double touchdown_lon;
            public double touchdown_velocity;
            public double touchdown_pitch;
        }

        protected HwndSource GetHWinSource() => PresentationSource.FromVisual((Visual)this) as HwndSource;

        private IntPtr WndProc(IntPtr hWnd, int iMsg, IntPtr hWParam, IntPtr hLParam, ref bool bHandled)
        {
            try
            {
                if (iMsg == 1026)
                {
                    //SimConnect simConnect = this.simConnect;
                    if (simConnect != null)
                    {
                        simConnect.ReceiveMessage();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                CloseConnection();
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (simConnect != null)
            {
                try
                {
                    simConnect.RequestDataOnSimObjectType(DAT_REQUESTS.REQUEST_1, DEFINITIONS.Struct1, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);
                }
                catch (COMException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            else
                OpenConnection();
        }

        /// <summary>
        /// Initiates a data request with the sim to setup the simvars to receive
        /// </summary>
        private void initDataRequest()
        {
            try
            {
                simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(simconnect_OnRecvException);

                // define a data structure
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "Title", (string)null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "Plane Latitude", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "Plane Longitude", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "INDICATED ALTITUDE", "Feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PLANE ALTITUDE", "Feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PLANE PITCH DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PLANE BANK DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "AIRSPEED TRUE", "Knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "AIRSPEED INDICATED", "Knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "VERTICAL SPEED", "Feet per second", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PLANE HEADING DEGREES MAGNETIC", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PLANE HEADING DEGREES TRUE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "G FORCE", "GForce", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG COMBUSTION:1", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG COMBUSTION:2", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG COMBUSTION:3", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG COMBUSTION:4", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "MAX RATED ENGINE RPM", "Rpm", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "GENERAL ENG MAX REACHED RPM", "Rpm", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ZULU TIME", "seconds", SIMCONNECT_DATATYPE.INT32, 1E+09f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "LOCAL TIME", "seconds", SIMCONNECT_DATATYPE.INT32, 1E+09f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "SIM ON GROUND", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "SURFACE TYPE", "Enum", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ATC ID", (string)null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ATC TYPE", (string)null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "FUEL TOTAL QUANTITY", "Gallons", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "FUELSYSTEM TANK CAPACITY:1", "Gallons", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED); // NEW FUEL SYSTEM simvar borked in MSFS2024, use this to test
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "UNUSABLE FUEL TOTAL QUANTITY", "Gallons", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "OVERSPEED WARNING", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "UNLIMITED FUEL", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PAYLOAD STATION COUNT", "Number", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "PAYLOAD STATION WEIGHT:1", "Pounds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "MAX G FORCE", "Gforce", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "MIN G FORCE", "Gforce", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "GENERAL ENG DAMAGE PERCENT", "Percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                //simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "FLAP DAMAGE BY SPEED", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                //simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "GEAR DAMAGE BY SPEED", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                //simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "FLAP SPEED EXCEEDED", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                //simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "GEAR SPEED EXCEEDED", "Bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG MANIFOLD PRESSURE", "inHG", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ENG FUEL FLOW GPH", "Gallons per hour", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "ATC MODEL", (string)null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.Struct1, "TOTAL WEIGHT", "Pounds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                //simConnect.AddToDataDefinition(SET_DATA.RIGHT_FUEL, "FUEL TANK RIGHT MAIN QUANTITY", "Gallons", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                // IMPORTANT: register it with the simconnect managed wrapper marshaller
                simConnect.RegisterDataDefineStruct<Struct1>(DEFINITIONS.Struct1);

                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN BANK DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN HEADING DEGREES MAGNETIC", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN HEADING DEGREES TRUE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN LATITUDE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN LONGITUDE", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN NORMAL VELOCITY", "Feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.AddToDataDefinition(DEFINITIONS.LandingStruct, "PLANE TOUCHDOWN PITCH DEGREES", "Degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simConnect.RegisterDataDefineStruct<LandingStruct>(DEFINITIONS.LandingStruct);

                // catch a simobject data request
                simConnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(simConnect_OnRecvSimobjectDataBytype);

                simConnect.RequestDataOnSimObject(DAT_REQUESTS.REQUEST_2, DEFINITIONS.LandingStruct, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
                simConnect.OnRecvSimobjectData += new SimConnect.RecvSimobjectDataEventHandler(simConnect_OnRecvSimobjectData);

            }
            catch (COMException ex)
            {
                Console.WriteLine(ex.Message);
            }

        }

        /// <summary>
        /// Triggered when communication with simconnect has been opened, sets the connection status
        /// </summary>
        /// <param name="sender">The SimConnect library.</param>
        /// <param name="data">Data received from sim.</param>
        private void simConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            elConnection.Fill = Brushes.Green;
            elConnection.Stroke = Brushes.Green;

            if (data.szApplicationName == "KittyHawk")
            {
                version = SimVersion.FS2020;
                HelperService.WriteToLog("Connected to FS2020");
            }
            else if (data.szApplicationName == "SunRise")
            {
                version = SimVersion.FS2024;
                HelperService.WriteToLog("Connected to FS2024");
            }
        }

        /// <summary>
        /// Triggered when communication with simconnect has been closed, stops tracking
        /// </summary>
        /// <param name="sender">The SimConnect library.</param>
        /// <param name="data">Data received from sim.</param>
        private void simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            CloseConnection();
            if (!bFlightCompleted && bFlightTracking) StopTracking();
        }

        /// <summary>
        /// Triggered when an exception within simconnect library ocurrs, sets the connection status
        /// </summary>
        /// <param name="sender">The SimConnect library.</param>
        /// <param name="data">Data received from sim.</param>
        private void simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            elConnection.Fill = Brushes.Red;
            elConnection.Stroke = Brushes.Red;
            simConnect = null;
        }

        // TODO: Someday - handle pause of sim to make time of flight more accurate
        // Simconnect does not report this correctly for MSFS

        //private void simconnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT recEvent)
        //{
        //    Console.WriteLine("Event received");
        //    switch (recEvent.uEventID)
        //    {
        //        case (uint)EVENT_ID.EVENT_PAUSED:
        //            Console.WriteLine("Paused");
        //            break;
        //        case (uint)EVENT_ID.EVENT_UNPAUSED:
        //            Console.WriteLine("Unpaused");
        //            break;
        //    }

        //}

        /// <summary>
        /// Triggered on each data receipt from simconnect, handles logic of sim data
        /// </summary>
        /// <param name="sender">The SimConnect library.</param>
        /// <param name="data">Data received from sim.</param>
        private void simConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            if (data.dwRequestID == (uint)DAT_REQUESTS.REQUEST_1)
            {
                Struct1 data1 = (Struct1)data.dwData[0];

                if (data1.fuelsystem_tank1_capacity > 0)
                {
                    // new fuel system treats total fuel qty excluding unusable fuel
                    // old fuel system this var includes unusable fuel
                    data1.fuel_qty += data1.unusable_fuel_qty;
                }

                // engine status

                bEnginesRunning = data1.eng1_combustion > 0 || data1.eng2_combustion > 0 || data1.eng3_combustion > 0 || data1.eng4_combustion > 0;
                txtSimFuel.Text = data1.fuel_qty.ToString("0.## gal");
                //if (!bFlightTracking)
                //  return;
                if (!bFlightTracking)
                {
                    if (dispatchData == null)
                    {
                        return;
                    }
                    if (!bEnginesRunning)
                    {
                        return;
                    }

                }

                // set reg number
                // simConnect.SetDataOnSimObject(SET_DATA.ATC_ID, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, txtRegistration.Text);

                // check if in a state to start flight tracking
                if (!bReady)
                {
                    // check if unlimited fuel is turned on
                    if (data1.is_unlimited == 1)
                    {
                        MessageBox.Show("Please turn off unlimited fuel", "Bush Tracker", MessageBoxButton.OK);
                        bReady = false;
                        lblStart.Visibility = Visibility.Collapsed;
                        return;
                    }

                    // TODO: someday - set fuel - Currently fuel tanks are not quite what they seem.
                    //var qty = Convert.ToDouble(txtFuel.Text) / 2;
                    //simConnect.SetDataOnSimObject(SET_DATA.LEFT_FUEL, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, qty);
                    //simConnect.SetDataOnSimObject(SET_DATA.RIGHT_FUEL, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, qty);

                    var startCheckStructure = new CheckStructure
                    {
                        Aircraft = data1.title,
                        AircraftType = data1.atcType,
                        Fuel = data1.fuel_qty,
                        Payload = data1.total_weight,
                        Pax = 0,
                        CurrentLat = data1.latitude,
                        CurrentLon = data1.longitude
                    };
                    var status = CheckReadyForStart(startCheckStructure);
                    if (status)
                    {
                        bReady = true;
                        btnStart.Visibility = Visibility.Collapsed;
                        btnStop.Visibility = Visibility.Visible;
                        SetStatusMessage("Ready to start", MessageState.OK);
                        StartFlight();
                        // Clear landing rate so next change event as per simconnect is viewed as 'new'
                        landingRate = 0.0;
                    } else
                    {
                        bReady = false;
                        lblStart.Visibility = Visibility.Collapsed;
                        return;
                    }
                    lblStart.Visibility = Visibility.Collapsed;
                    lblDistance.Visibility = Visibility.Visible;
                    lblDistanceLabel.Visibility = Visibility.Visible;
                }

                if (bEnginesRunning && bFirstData)
                {
                    bLastEngineStatus = false;
                    bFirstData = false;
                    // fix starting of engines during pre-start tests (but after initial 'engine off' test)
                    lastLat = data1.latitude;
                    lastLon = data1.longitude;
                }

                // Checks for start of flight and sets offblocks time
                if (bEnginesRunning && Convert.ToBoolean(data1.on_ground) && !bLastEngineStatus)
                {
                    startLat = data1.latitude;
                    startLon = data1.longitude;
                    // startTime = HelperService.SetZuluTime(data1.zulu_time).ToString("yyyy-MM-dd HH:mm:ss");
                    startTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    startFuelQty = data1.fuel_qty;
                    flightStatus = PirepStatusType.BOARDING;
                    _api.PostPirepStatusAsync(new PirepStatus { PirepId = txtPirep.Text, Status = (int)PirepStatusType.BOARDING });

                    SetStatusMessage("Pre-flight|Loading");
                    SendTextToSim("Bush Tracker Status: Pre-Flight - Ready");
                }

                // check for take off
                if (flightStatus == PirepStatusType.BOARDING && !Convert.ToBoolean(data1.on_ground) && data1.plane_altitude > 20) // arbitrary number to avoid advancing state on bouncy water takeoff
                {
                    flightStatus = PirepStatusType.DEPARTED;
                    _api.PostPirepStatusAsync(new PirepStatus { PirepId = txtPirep.Text, Status = (int)PirepStatusType.DEPARTED });

                    SetStatusMessage("Departed");
                    SendTextToSim("Bush Tracker Status: Departed - Have a good flight!");
                }

                // check for landed
                if (flightStatus == PirepStatusType.DEPARTED || flightStatus == PirepStatusType.LANDED)
                {
                    // Increase resolution in case of water ditch
                    if (data1.plane_altitude < 500 && timer.Interval.Seconds > 2)
                        timer.Interval = TimeSpan.FromSeconds(1.0);
                    else if (data1.plane_altitude > 1000 && timer.Interval.Seconds < 8)
                        timer.Interval = TimeSpan.FromSeconds(10.0);

                    // If on ground
                    if (Convert.ToBoolean(data1.on_ground))
                    {
                        // And we either slowing for land
                        // 25knt arbitrary number to avoid advancing state on bouncy water takeoff)
                        if (data1.airspeed_indicated < 25 && flightStatus != PirepStatusType.LANDED)
                        {
                            flightStatus = PirepStatusType.LANDED;
                            _api.PostPirepStatusAsync(new PirepStatus { PirepId = txtPirep.Text, Status = (int)PirepStatusType.LANDED });
                            btnEndFlight.IsEnabled = true;
                            SetStatusMessage("Landed");
                            SendTextToSim("Bush Tracker Status: Landed");
                        }
                        else if (!lastOnground && data1.surface_type == 2) // landed on water
                        {
                            var rate = -(data1.vspeed + lastVs) * 60.0 / 2.0;    // f/s to f/m... api needs f/s on the pirep progress log

                            // In case of 'bounce' between the two refs
                            if (rate < 0.0)
                                rate = 0.0;

                            if (landingRate < rate || (landingLat == 0.0 && landingLon == 0.0))
                            {
                                landingRate = rate;
                                landingPitch = data1.ac_pitch;
                                landingBank = data1.ac_bank;
                                landingLat = data1.latitude;
                                landingLon = data1.longitude;
                            }
                        }
                    }
                }

                // check if user wants to end flight
                if (bEndFlight)
                {
                    // only end flight if on ground with engines off
                    if (!bEnginesRunning && Convert.ToBoolean(data1.on_ground))
                    {
                        bFlightCompleted = true;
                        bFlightTracking = false;
                        flightStatus = PirepStatusType.ARRIVED;
                        _api.PostPirepStatusAsync(new PirepStatus { PirepId = txtPirep.Text, Status = (int)PirepStatusType.ARRIVED });
                        SetStatusMessage("Flight ended");
                        SendTextToSim("Bush Tracker Status: Flight ended - Thanks for working with Bush Divers");

                        endFuelQty = data1.fuel_qty;
                        endLat = data1.latitude;
                        endLon = data1.longitude;
                        // endTime = HelperService.SetZuluTime(data1.zulu_time).ToString("yyyy-MM-dd HH:mm:ss");
                        endTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        aircraftName = data1.title;
                        // btnStop.Visibility = Visibility.Visible;
                        btnSubmit.IsEnabled = true;

                        bLastEngineStatus = bEnginesRunning;
                        bEndFlight = false;
                        btnEndFlight.IsEnabled = false;
                        lblEnd.Visibility = Visibility.Hidden;
                    } else
                    {
                        bEndFlight = false;
                        btnEndFlight.IsEnabled = true;
                        lblEnd.Visibility = Visibility.Hidden;
                        MessageBox.Show("You must be on the ground with engines off to end your flight", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // calculate cumulative distance
                if (!bFirstData)
                {
                    // calc distance
                    var d = HelperService.CalculateDistance(lastLat, lastLon, data1.latitude, data1.longitude);
                    if (d > 50)
                    {
                        bFlightTracking = false;
                        StopTracking();
                        MessageBox.Show("It looks like you have abandoned your flight, tracking will now stop and your progress cancelled." + "\n" + "You can start your flight again by returning to the departure location", "Bush Divers", MessageBoxButton.OK);
                        return;
                    }
                    currentDistance += d;
                    lblDistance.Content = currentDistance.ToString("0.## nm");
                }

                lastLat = data1.latitude;
                lastLon = data1.longitude;

                // Send data to api
                var headingChanged = HelperService.CheckForHeadingChange(lastHeading, data1.heading_m);
                var altChanged = HelperService.CheckForAltChange(lastAltitude, data1.indicated_altitude);
                // determine if data has changed or not
                if (headingChanged || altChanged)
                {
                    SendFlightLog(data1);
                    dataLastSent = DateTime.UtcNow;
                } else if (DateTime.UtcNow > dataLastSent.AddSeconds(60))
                {
                    SendFlightLog(data1);
                    dataLastSent = DateTime.UtcNow;
                }

                bLastEngineStatus = bEnginesRunning;

                lastAltitude = data1.indicated_altitude;
                lastHeading = data1.heading_m;
                lastVs = data1.vspeed;
                lastGforce = data1.gforce;
                lastOnground = Convert.ToBoolean(data1.on_ground);
            }
        }

        private void simConnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)DAT_REQUESTS.REQUEST_2)
            {
                LandingStruct data1 = (LandingStruct)data.dwData[0];
                if (landingRate < data1.touchdown_velocity)
                {
                    landingRate = data1.touchdown_velocity;
                    landingPitch = data1.touchdown_pitch;
                    landingBank = data1.touchdown_bank;
                    landingLat = data1.touchdown_lat;
                    landingLon = data1.touchdown_lon;
                }
            }
        }

        /// <summary>
        /// Starts a connection with SimConnect
        /// </summary>
        public void OpenConnection()
        {
            try
            {
                GetHWinSource().AddHook(new HwndSourceHook(WndProc));

                simConnect = new SimConnect("Managed Data Request", GetHWinSource().Handle, WM_USER_SIMCONNECT, null, 0);
                //simConnect.SubscribeToSystemEvent(EVENT_ID.EVENT_PAUSED, "Paused");
                //simConnect.SubscribeToSystemEvent(EVENT_ID.EVENT_UNPAUSED, "Unpaused");
                simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(simConnect_OnRecvOpen);
                simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(simconnect_OnRecvQuit);
                //simConnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(simconnect_OnRecvEvent);

                initDataRequest();

                bConnected = true;
                if (bDispatch) btnStart.IsEnabled = true;

                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(10.0);
                timer.Tick += new EventHandler(Timer_Tick);
                timer.Start();
                lblErrorText.Visibility = Visibility.Hidden;
            }
            catch (COMException ex)
            {
                HelperService.WriteToLog($"Issue connecting to sim: {ex.Message}");
                lblErrorText.Text = $"Issue connecting to sim: {ex.Message}";
                lblErrorText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Closes a connection with SimConnect
        /// </summary>
        public void CloseConnection()
        {
            if (simConnect != null)
            {
                //simConnect.UnsubscribeFromSystemEvent(EVENT_ID.EVENT_PAUSED);
                //simConnect.UnsubscribeFromSystemEvent(EVENT_ID.EVENT_UNPAUSED);
                simConnect.Dispose();
                simConnect = null;
            }

            if (timer != null)
                timer.Stop();

            elConnection.Fill = Brushes.Red;
            elConnection.Stroke = Brushes.Red;
            btnConnect.IsEnabled = true;
            btnStart.IsEnabled = false;
            bConnected = false;
        }

        /// <summary>
        /// Starts request for data with simconnect
        /// </summary>
        public void StartTracking()
        {
            try
            {
                initDataRequest();
            }
            catch (COMException ex)
            {
                HelperService.WriteToLog($"Issue getting update from sim: {ex.Message}");
            }
        }

        public void StartFlight()
        {
            lblStart.Visibility = Visibility.Visible;
            btnStart.IsEnabled = false;
            btnFetchBookings.IsEnabled = false;
            bFlightTracking = true;
            lblErrorText.Visibility = Visibility.Hidden;
            timer.Stop();
            // Might still get a double-fire if the dispatch is ready to send or even if the timer has ticked but response yet to be received - "shouldn't" be an issue
            Timer_Tick(null, null);
            timer.Start();
        }

        /// <summary>
        /// Sends text to display on Sim - currently broken in simconnect
        /// </summary>
        /// <param name="text">string to be sent to sim</param>
        public void SendTextToSim(string text)
        {
            simConnect.Text(SIMCONNECT_TEXT_TYPE.PRINT_BLACK, 5, SIMCONNECT_EVENT_FLAG.DEFAULT, text);
        }

        #endregion

        #region Form_Iteraction

        private void btnFetchBookings_Click(object sender, RoutedEventArgs e)
        {
            FetchDispatch();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartFlight();
        }

        private async void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            lblSubmitting.Visibility = Visibility.Visible;
            btnSubmit.IsEnabled = false;
            lblErrorText.Visibility = Visibility.Hidden;
            btnFetchBookings.IsEnabled = true;
            SubmitFlight();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("If you cancel you will need to restart your flight at a later time.\n\nAre you sure you wish to cancel your flight?", "Cancel?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                fuelErrorShown = false;
                StopTracking();
                lblErrorText.Visibility = Visibility.Hidden;
                CloseConnection();
                btnConnect.IsEnabled = true;
                btnFetchBookings.IsEnabled = true;
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            OpenConnection();
            if (bConnected)
            {
                lblErrorText.Visibility = Visibility.Hidden;
                btnConnect.IsEnabled = false;
            }
        }

        private void btnEndFlight_Click(object sender, RoutedEventArgs e)
        {
            btnEndFlight.IsEnabled = false;
            lblEnd.Visibility = Visibility.Visible;
            bEndFlight = true;
        }

        private void rdoUnitType_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.UseMetric = rdoUnitMetric.IsChecked == true;
            Properties.Settings.Default.Save();

            UpdateDispatchWeight();
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_addonBrowser != null && _addonBrowser.IsVisible)
            {
                _addonBrowser.Close();
                if (_addonBrowser != null)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (bReady)
            {
                if (MessageBox.Show("A flight is currently in progress. You will need to restart your flight at a later time.\n\nAre you sure you wish to quit?", "Cancel current flight?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    e.Cancel = true;

                    await StopTracking();
                    if (!bReady || MessageBox.Show("There was an error cancelling your flight. If you continue you will need to cancel your dispatch via the web.\n\nQuit anyway?", "Quit?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        bReady = false;
                        ((Window)sender).Close();
                    }
                }
                else
                    e.Cancel = true;
            }

            if (!e.Cancel)
                CloseConnection();
        }

        #endregion


        #region Helper_methods

        /// <summary>
        /// Sends a flight log to the api
        /// </summary>
        /// <param name="d">data from simconnect</param>
        public async void SendFlightLog(Struct1 d)
        {
            var log = new FlightLog()
            {
                PirepId = txtPirep.Text,
                Lat = d.latitude,
                Lon = d.longitude,
                Heading = Convert.ToInt32(d.heading_m),
                Altitude = Convert.ToInt32(d.indicated_altitude),
                IndicatedSpeed = Convert.ToInt32(d.airspeed_indicated),
                GroundSpeed = Convert.ToInt32(d.airspeed_true),
                FuelFlow = d.fuel_flow,
                VS = d.vspeed,
                SimTime = HelperService.SetZuluTime(d.local_time),
                ZuluTime = HelperService.SetZuluTime(d.zulu_time),
                Distance = currentDistance
            };

            try
            {
                await _api.PostFlightLogAsync(log);
            }
            catch (Exception)
            {
                lblErrorText.Text = "Error submitting flight update";
                lblErrorText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Ends a flight and submits the pirep
        /// </summary>
        public async void SubmitFlight()
        {
            // check distance
            var distance = HelperService.CalculateDistance(Convert.ToDouble(txtArrLat.Text), Convert.ToDouble(txtArrLon.Text), endLat, endLon, true);
            if (distance > 2)
            {
                // get nearest airport and update pirep destination (return icao)
                var req = new NewLocationRequest
                {
                    Lat = endLat,
                    Lon = endLon,
                    PirepId = txtPirep.Text
                };

                try
                {
                    var newLocation = await _api.PostNewLocationAsync(req);

                    // update labels (destination icao)
                    txtArrLat.Text = endLat.ToString();
                    txtArrLon.Text = endLon.ToString();
                    txtArrival.Text = newLocation.Icao;
                }
                catch (Exception ex)
                {
                    lblSubmitting.Visibility = Visibility.Hidden;
                    MessageBox.Show("Unable to find landing airport.\n\nPlease resume flying and land within 2NM of an airport", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
                    lblErrorText.Text = "No airport within 2NM";
                    lblErrorText.Visibility = Visibility.Visible;
                    bEndFlight = false;
                    bFlightTracking = true;
                    btnSubmit.IsEnabled = false;
                    btnEndFlight.IsEnabled = true;
                    lblEnd.Visibility = Visibility.Hidden;
                    return;
                }
            }

            var pirep = new Pirep()
            {
                PirepId = txtPirep.Text,
                FuelUsed = startFuelQty - endFuelQty,
                LandingRate = landingRate,
                TouchDownLat = landingLat,
                TouchDownLon = landingLon,
                TouchDownBank = landingBank,
                TouchDownPitch = landingPitch,
                BlockOffTime = startTime,
                BlockOnTime = endTime,
                Distance = currentDistance,
                AircraftUsed = aircraftName,
                SimUsed = version.Value.ToString()
            };

            bool res = false;
            try
            {
                res = await _api.PostPirepAsync(pirep);
            }
            catch (Exception)
            {
                lblErrorText.Text = "Error submitting to server";
                lblErrorText.Visibility = Visibility.Visible;
            }

            if (res)
            {
                lblSubmitting.Visibility = Visibility.Hidden;
                lblErrorText.Text = "";
                lblErrorText.Visibility = Visibility.Hidden;
                MessageBox.Show("Pirep submitted!", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                TidyUpAfterPirepSubmission();
            }
            else
            {
                lblSubmitting.Visibility = Visibility.Hidden;
                MessageBox.Show("Pirep Not Submitted!", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
                btnSubmit.IsEnabled = true;
            }
        }

        /// <summary>
        /// Clears variables and flight related info after pirep submission
        /// </summary>
        protected void TidyUpAfterPirepSubmission()
        {
            btnStop.Visibility = Visibility.Hidden;

            ClearVariables();
            btnSubmit.IsEnabled = false;
            btnStart.Visibility = Visibility.Visible;
            btnEndFlight.IsEnabled = false;
            btnStart.IsEnabled = false;
            lblDistanceLabel.Visibility = Visibility.Hidden;
            lblDistance.Visibility = Visibility.Hidden;
            lblDeadHead.Visibility = Visibility.Hidden;
            FetchDispatch();
        }

        /// <summary>
        /// Clears variables related to a flight
        /// </summary>
        private void ClearVariables()
        {
            bDispatch = false;
            bReady = false;
            bEndFlight = false;
            bFlightCompleted = false;
            bFirstData = true;
            bLastEngineStatus = false;
            startLat = 0;
            startLon = 0;
            endLat = 0;
            endLon = 0;
            lastLat = 0;
            lastLon = 0;
            currentDistance = 0;
            startFuelQty = 0;
            endFuelQty = 0;
            startTime = "";
            endTime = "";
            flightStatus = PirepStatusType.BOARDING;
            lastHeading = 0;
            lastAltitude = 0;
            // stop simconnect tracking
            bFlightTracking = false;
            currentFuelQty = 0;
        }

        /// <summary>
        /// Cancels the tracking and progress of a flight
        /// </summary>
        private async Task StopTracking()
        {
            btnStop.IsEnabled = false;

            bool res = false;
            try
            {
                res = await _api.CancelTrackingAsync();
            }
            catch (Exception)
            {
                SetStatusMessage("Error submitting to server", MessageState.Error);
            }

            if (res)
            {
                ClearVariables();
                lblDistance.Visibility = Visibility.Hidden;
                lblDistanceLabel.Visibility = Visibility.Hidden;
                // reset pirep to draft and remove any logs

                SetStatusMessage("Tracking Stopped");
                btnStop.Visibility = Visibility.Hidden;
                btnStart.Visibility = Visibility.Visible;
                btnStart.IsEnabled = true;
                btnEndFlight.IsEnabled = false;
            }
            else
            {
                SetStatusMessage("Issue cancelling pirep", MessageState.Error);
            }
            btnStop.IsEnabled = true;
        }

        /// <summary>
        /// Sets the dispatch information from server
        /// </summary>
        /// <param name="dispatch">Dispatch info to be set</param>
        private void SetDispatchData(Dispatch dispatch)
        {
            dispatchData = dispatch;
            btnStart.IsEnabled = true;
            grpFlight.Visibility = Visibility.Visible;
            txtDeparture.Text = dispatch.Departure.ToString();
            txtArrival.Text = dispatch.Arrival.ToString();
            txtAircraft.Text = dispatch.Aircraft.ToString();
            txtAircraftType.Text = dispatch.AircraftType.ToString();
            txtRegistration.Text = dispatch.Registration.ToString();
            txtPirep.Text = dispatch.Id.ToString();
            txtDepLat.Text = dispatch.DepLat.ToString();
            txtDepLon.Text = dispatch.DepLon.ToString();
            txtArrLat.Text = dispatch.ArrLat.ToString();
            txtArrLon.Text = dispatch.ArrLon.ToString();
            string tourText  = dispatch.Tour != null ? dispatch.Tour.ToString() : "";
            txtTour.Text = tourText;
            UpdateDispatchWeight();
            grpFlight.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Update the display fields of weights and volumes based on user settings
        /// </summary>
        private void UpdateDispatchWeight()
        {
            if (dispatchData == null)
                return;

            bool isMetric = rdoUnitMetric.IsChecked == true;

            txtFuel.Text = isMetric ? HelperService.GalToLitre(dispatchData.PlannedFuel).ToString("0.## L") : dispatchData.PlannedFuel.ToString("0.## gal");
            txtCargoWeight.Text = isMetric ? HelperService.LbsToKG(dispatchData.CargoWeight).ToString("0.# kg") : dispatchData.CargoWeight.ToString("0 lbs");
            txtPaxCount.Text = dispatchData.PassengerCount.ToString();
            if (dispatchData.PassengerCount > 0)
            {
                decimal paxWeight = dispatchData.PassengerCount * 170;
                var total = dispatchData.CargoWeight + paxWeight;
                txtPayloadTotal.Text = isMetric ? HelperService.LbsToKG(total).ToString("0.# kg") : total.ToString("0 lbs");
            }
            else
            {
                txtPayloadTotal.Text = isMetric ? HelperService.LbsToKG(dispatchData.CargoWeight).ToString("0.# kg") : dispatchData.CargoWeight.ToString("0 lbs");
            }
        }

        /// <summary>
        /// Runs checks to make sure in a ready state to start flight
        /// </summary>
        /// <param name="data">data to check if flight is read to start</param>
        /// <returns>
        /// True if ready, false if something is not setup correctly
        /// </returns>
        public bool CheckReadyForStart(CheckStructure data)
        {
            // clear text errors
            ClearCheckErrors();
            var status = true;
            // check aircraft contains text in chosen aircraft
            //var test = data.Aircraft.Contains(txtAircraftType.Text);
            //if (!data.Aircraft.Contains(txtAircraftType.Text) || data.AircraftType != txtAircraftType.Text)
            //{
            //    // set error text for aircraft
            //    lblAircraftError.Content = "Aircraft does not match";
            //    lblAircraftError.Visibility = Visibility.Visible;
            //    status = false;
            //}

            if (dispatchData == null)
                return false;

            // check fuel qty matches planned fuel
            var tolerance = decimal.ToDouble(dispatchData.PlannedFuel) * .01;
            if (tolerance < 5.0)
                tolerance = 5;
            var maxVal = decimal.ToDouble(dispatchData.PlannedFuel) + tolerance;
            var minVal = decimal.ToDouble(dispatchData.PlannedFuel) - tolerance;
            //var isFuelValid = Enumerable.Range(Convert.ToInt32(minVal), Convert.ToInt32(maxVal)).Contains(Convert.ToInt32(data.Fuel));
            //if (data.Fuel <= max && data.Fuel >= min)
            if (!(minVal <= data.Fuel) || !(data.Fuel <= maxVal))
            {
                // set error text for fuel
                if (!fuelErrorShown)
                {
                    fuelErrorShown = true;
                    MessageBox.Show("Your fuel in sim does not match the dispatch", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                lblFuelError.Content = "Fuel does not match";
                lblFuelError.Visibility = Visibility.Visible;
                status = false;
            }

            // TODO: check cargo weight matches
            //var w = Math.Round(data.Payload, 2).ToString();
            //if (txtCargoWeight.Text != Math.Floor(data.Payload).ToString())
            //{
            //    // set error text for payload
            //    lblCargoError.Content = "Cargo does not match";
            //    lblCargoError.Visibility = Visibility.Visible;
            //    status = false;
            //}

            // check current position
            var distance = HelperService.CalculateDistance(decimal.ToDouble(dispatchData.DepLat), decimal.ToDouble(dispatchData.DepLon), data.CurrentLat, data.CurrentLon, false);
            if (distance > 2)
            {
                // set error text for departure
                lblDepartureError.Content = "Incorrect location";
                lblDepartureError.Visibility = Visibility.Visible;
                status = false;
            }
            return status;
        }

        /// <summary>
        /// Clears any errors from previous check
        /// </summary>
        public void ClearCheckErrors()
        {
            lblDepartureError.Visibility = Visibility.Hidden;
            lblAircraftError.Visibility = Visibility.Hidden;
            lblCargoError.Visibility = Visibility.Hidden;
            lblFuelError.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// Gets dispatch info from api
        /// </summary>
        public async void FetchDispatch()
        {
            fuelErrorShown = false;
            lblStatusText.Text = "Ok";
            lblStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
            lblStatusText.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
            if (txtKey.Password == "")
            {
                MessageBox.Show("Please enter your API key", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }
            if (txtKey.Password != Properties.Settings.Default.Key)
            {
                Properties.Settings.Default.Key = txtKey.Password;
                Properties.Settings.Default.Save();
            }
            lblFetch.Visibility = Visibility.Visible;
            // make api request to get bookings/dispatch
            try
            {
                var dispatch = await _api.GetDispatchInfoAsync();
                if (dispatch.IsEmpty == 0)
                {
                    var dispatchCargo = await _api.GetDispatchCargoAsync();
                    // load bookings into grid
                    dgBookings.ItemsSource = dispatchCargo;
                }
                else
                {
                    lblDeadHead.Visibility = Visibility.Visible;
                }
                SetDispatchData(dispatch);
                bDispatch = true;
                if (bConnected)
                {
                    btnStart.IsEnabled = true;
                }
                else
                {
                    btnStart.IsEnabled = false;
                }
                lblFetch.Visibility = Visibility.Hidden;
                lblErrorText.Text = "";
                lblErrorText.Visibility = Visibility.Hidden;
            }
            catch (Exception ex)
            {
                lblFetch.Visibility = Visibility.Hidden;
                lblErrorText.Text = ex.Message;
                lblErrorText.Visibility = Visibility.Visible;
                if (ex.Message == "Fetching dispatch info: No Content")
                {
                    dgBookings.ItemsSource = null;
                    grpFlight.Visibility = Visibility.Hidden;
                }
            }
        }
        #endregion

        private void btnAddons_Click(object sender, RoutedEventArgs e)
        {
            if (_addonBrowser == null)
            {
                _addonBrowser = new AddonBrowser();
                _addonBrowser.Closed += delegate { _addonBrowser = null; };
                _addonBrowser.Show();
            }
            else
            {
                _addonBrowser.Focus();
            }
            
        }

        private void SetStatusMessage(string message, MessageState state = MessageState.OK)
        {
            if (state == MessageState.Error)
            {
                lblStatusText.Visibility = Visibility.Hidden;
                lblErrorText.Text = message;
                lblErrorText.Visibility = Visibility.Visible;
            }
            else
            {
                lblErrorText.Visibility = Visibility.Hidden;
                lblStatusText.Text = message;
                lblStatusText.Visibility = Visibility.Visible;
            }
        }
    }
}
