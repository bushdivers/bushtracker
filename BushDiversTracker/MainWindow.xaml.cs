using BushDiversTracker.Models;
using BushDiversTracker.Models.NonApi;
using BushDiversTracker.Models.Enums;
using BushDiversTracker.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoUpdaterDotNET;
using System.Reflection;
using static BushDiversTracker.Services.TrackerService;
using System.Linq;
using System.Windows.Controls;
using BushDiversTracker.Properties;

namespace BushDiversTracker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        APIService _api;
        AddonBrowser _addonBrowser;
        ISimService _simConnect;
        TrackerService _tracker;

        internal enum MessageState
        {
            OK,
            Neutral,
            Error
        }

        public MainWindow()
        {
            InitializeComponent();

            HelperService.RotateLog();

            if (Settings.Default.IsUpdateRequired)
            {
                Settings.Default.Upgrade();
                Settings.Default.IsUpdateRequired = false;
                Settings.Default.Save();
            }

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

            //btnStop.Visibility = Visibility.Hidden;
            lblFetch.Visibility = Visibility.Hidden;
            lblStart.Visibility = Visibility.Hidden;
            lblDistanceLabel.Visibility = Visibility.Hidden;
            lblDistance.Visibility = Visibility.Hidden;
            lblSubmitting.Visibility = Visibility.Hidden;

            btnStop.IsEnabled = false;

            txtKey.Password = Settings.Default.Key;
            _api = new APIService();
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                //AutoUpdater.Start("http://localhost/api/tracker-version");
                AutoUpdater.Start("https://fly.bushdivers.com/api/tracker-version");
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            lblVersion.Content = version;

            if (Settings.Default.UseMetric)
                rdoUnitMetric.IsChecked = true;
            else
                rdoUnitUS.IsChecked = true;

            btnConnect_SetSim(Settings.Default.SimType != "XP" ? mnuSetSimMSFS : mnuSetSimXP, null);

            if (_simConnect != null)
            {
                _simConnect.OnSimConnected += SimConnect_OnSimConnected;
                _simConnect.OnSimDisconnected += SimConnect_OnSimDisconnected;
                _simConnect.OnSimDataReceived += SimConnect_OnSimDataReceived;
            }

            _tracker = new TrackerService(this, _simConnect, _api);
            _tracker.OnTrackerStateChanged += Tracker_OnStateChange;
            _tracker.OnFlightStatusChanged += (sender, status) => lblFlightStatus.Content = status.ToString();
            _tracker.OnDispatchError += Tracker_OnDispatchError;
            _tracker.OnSetDispatch += Tracker_SetDispatchData;
            _tracker.OnStatusMessage += (sender, message) => SetStatusMessage(message.Message, message.State);

            lblTrackerStatus.Content = "Not tracking";
            lblFlightStatus.Visibility = Visibility.Hidden;

            chkAutoStart.IsChecked = Settings.Default.AutoStart;
            chkTextToSim.IsChecked = Settings.Default.ShowSimText;
        }

        #region SimConnect

        private void SimConnect_OnSimConnected(object sender, EventArgs e)
        {
            elConnection.Fill = Brushes.Green;
            elConnection.Stroke = Brushes.Green;
            btnConnect.IsEnabled = false;
            SetStatusMessage("Connected");
        }

        private void SimConnect_OnSimDisconnected(object sender, EventArgs e)
        {
            elConnection.Fill = Brushes.Red;
            elConnection.Stroke = Brushes.Red;
            btnConnect.IsEnabled = true;
        }

        private void SimConnect_OnSimDataReceived(object sender, SimData data)
        {
            if (_tracker?.Dispatch != null)
                txtSimFuel.Text = FormatFuel(new decimal(data.fuel_qty), _tracker.Dispatch.FuelType);
            else
                txtSimFuel.Text = "";
        }


        #endregion

        #region Tracker UI interaction
        private void Tracker_OnStateChange(object sender, TrackerState state)
        {
            switch (state)
            {
                case TrackerState.None:
                    btnStop.IsEnabled = false;
                    btnSubmit.IsEnabled = true;
                    btnFetchBookings.IsEnabled = true;
                    lblDistance.Visibility = Visibility.Hidden;
                    lblDistanceLabel.Visibility = Visibility.Hidden;
                    btnSubmit.IsEnabled = false;
                    lblSubmitting.Visibility = Visibility.Hidden;
                    chkAutoStart.IsEnabled = true;
                    lblFlightStatus.Visibility = Visibility.Hidden;
                    lblTrackerStatus.Content = "Not tracking";
                    break;

                case TrackerState.HasDispatch:
                    btnStop.IsEnabled = true;
                    btnFetchBookings.IsEnabled = true;
                    chkAutoStart.IsEnabled = true;
                    lblFlightStatus.Visibility = Visibility.Visible;
                    lblTrackerStatus.Content = "Has dispatch";
                    break;

                case TrackerState.ReadyToStart:
                    lblDistance.Visibility = Visibility.Visible;
                    lblDistanceLabel.Visibility = Visibility.Visible;
                    lblFlightStatus.Visibility = Visibility.Visible;
                    btnStop.IsEnabled = true;
                    btnFetchBookings.IsEnabled = false;
                    chkAutoStart.IsEnabled = false;
                    lblTrackerStatus.Content = "Ready to start";
                    break;

                case TrackerState.InFlight:
                    btnSubmit.IsEnabled = false;
                    chkAutoStart.IsEnabled = false;
                    chkAutoStart.IsChecked = Settings.Default.AutoStart;
                    lblTrackerStatus.Content = "Flight active";
                    break;

                case TrackerState.Shutdown:
                    btnSubmit.IsEnabled = true;
                    chkAutoStart.IsEnabled = true;
                    lblTrackerStatus.Content = "Shutdown";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(state));

            }

            HelperService.WriteToLog($"Tracker state changed to {state}");
        }

        private void Tracker_OnDispatchError(object sender, TrackingStartErrorArgs status)
        {
            lblAircraftError.Visibility = status.AircraftError ? Visibility.Visible : Visibility.Hidden;
            lblCargoError.Visibility = status.CargoError ? Visibility.Visible : Visibility.Hidden;
            lblDepartureError.Visibility = status.DepartureError ? Visibility.Visible : Visibility.Hidden;
            lblFuelError.Visibility = status.FuelError ? Visibility.Visible : Visibility.Hidden;
        }

        #endregion

        #region Form_Iteraction

        private string FormatFuel(decimal fuel, FuelType? fuelType)
        {
            bool isMetric = rdoUnitMetric.IsChecked == true;
            string fuelString = isMetric ? HelperService.GalToLitre(fuel).ToString("0.## L") : fuel.ToString("0.## gal");
            if (fuelType.HasValue)
                fuelString += " | " + (isMetric ? HelperService.LbsToKG(HelperService.GalToLbs(fuel, fuelType.Value)).ToString("0.## kg") : HelperService.GalToLbs(fuel, fuelType.Value).ToString("0.## lbs"));

            return fuelString;
        }

        private async void btnFetchBookings_Click(object sender, RoutedEventArgs e)
        {
            await FetchDispatch();
        }


        private async void btnSubmit_Click(object sender, RoutedEventArgs e)
        {
            lblSubmitting.Visibility = Visibility.Visible;
            btnSubmit.IsEnabled = false;
            lblErrorText.Visibility = Visibility.Hidden;
            await _tracker.SubmitFlight();

        }

        private async void btnStop_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("If you cancel you will need to restart your flight at a later time.\n\nAre you sure you wish to cancel your flight?", "Cancel?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                await _tracker.Stop();
                chkAutoStart.IsChecked = Settings.Default.AutoStart;
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_simConnect != null)
            {
                btnConnect.IsEnabled = false;
                _simConnect.OpenConnection();
            }
            else
                SetStatusMessage("Connection type not set", MessageState.Error);
        }

        private void btnConnect_ContextOpen(object sender, RoutedEventArgs e)
        {
            e.Handled = (_simConnect != null && _simConnect.IsConnected) || !System.Diagnostics.Debugger.IsAttached;
        }

        private async void btnConnect_SetSim(object sender, RoutedEventArgs e)
        {
            if (_tracker != null)
                await _tracker.Stop();

            _simConnect?.CloseConnection();
            if (sender == mnuSetSimMSFS)
            {
                _simConnect = new SimServiceMSFS(this);
                Settings.Default.SimType = "MSFS";
                lblConnectStatus.Content = "MSFS Connection Status:";
                mnuSetSimMSFS.IsChecked = true;
                mnuSetSimXP.IsChecked = false;
            }
            else
            {
                _simConnect = null;
                Settings.Default.SimType = "XP";
                lblConnectStatus.Content = "XPlane Connection Status:";
                mnuSetSimMSFS.IsChecked = false;
                mnuSetSimXP.IsChecked = true;
            }

            if (e != null) 
                _simConnect?.OpenConnection();
        }

        private void rdoUnitType_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Default.UseMetric = rdoUnitMetric.IsChecked == true;

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

            if (chkAutoStart.IsEnabled)
                Settings.Default.AutoStart = chkAutoStart.IsChecked == true;

            Settings.Default.Save();

            if (_tracker.State > TrackerState.HasDispatch)
            {
                if (_tracker.State == TrackerState.Shutdown || MessageBox.Show("A flight is currently in progress. You will need to restart your flight at a later time.\n\nAre you sure you wish to quit?", "Cancel current flight?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    e.Cancel = true;

                    await _tracker.Stop();
                    if (_tracker.State > TrackerState.HasDispatch || MessageBox.Show("There was an error cancelling your flight. If you continue you will need to cancel your dispatch via the web.\n\nQuit anyway?", "Quit?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        ((Window)sender).Close();
                    }
                }
                else
                    e.Cancel = true;
            }
            else if (_tracker.State == TrackerState.HasDispatch)
                await _tracker.Stop();

            if (!e.Cancel)
                _simConnect?.CloseConnection();
        }

        #endregion


        #region Helper_methods

        
        /// <summary>
        /// Sets the dispatch information from server
        /// </summary>
        /// <param name="dispatch">Dispatch info to be set</param>
        private void Tracker_SetDispatchData(object sender, Dispatch dispatch)
        {
            if (dispatch == null)
            {
                grpFlight.Visibility = Visibility.Hidden;
                txtKey.IsEnabled = true;
                dgBookings.ItemsSource = null;
                chkAutoStart.IsChecked = Settings.Default.AutoStart;
                return;
            }

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
            txtKey.IsEnabled = false;
            UpdateDispatchWeight();
        }

        /// <summary>
        /// Update the display fields of weights and volumes based on user settings
        /// </summary>
        private void UpdateDispatchWeight()
        {
            if (_tracker?.Dispatch == null)
                return;

            bool isMetric = rdoUnitMetric.IsChecked == true;

            txtFuel.Text = FormatFuel(_tracker.Dispatch.PlannedFuel, _tracker.Dispatch.FuelType);
            txtCargoWeight.Text = isMetric ? HelperService.LbsToKG(_tracker.Dispatch.CargoWeight).ToString("0.# kg") : _tracker.Dispatch.CargoWeight.ToString("0 lbs");
            txtPaxCount.Text = _tracker.Dispatch.PassengerCount.ToString();
            if (_tracker.Dispatch.PassengerCount > 0)
            {
                decimal paxWeight = _tracker.Dispatch.PassengerCount * 170;
                var total = _tracker.Dispatch.CargoWeight + paxWeight;
                txtPayloadTotal.Text = isMetric ? HelperService.LbsToKG(total).ToString("0.# kg") : total.ToString("0 lbs");
            }
            else
            {
                txtPayloadTotal.Text = isMetric ? HelperService.LbsToKG(_tracker.Dispatch.CargoWeight).ToString("0.# kg") : _tracker.Dispatch.CargoWeight.ToString("0 lbs");
            }
        }
       

        /// <summary>
        /// Gets dispatch info from api
        /// </summary>
        public async Task FetchDispatch()
        {
            SetStatusMessage("Fetching dispatch");
            
            if (txtKey.Password == "")
            {
                MessageBox.Show("Please enter your API key", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            if (txtKey.Password != Properties.Settings.Default.Key)
                Settings.Default.Key = txtKey.Password;

            lblFetch.Visibility = Visibility.Visible;
            // make api request to get bookings/dispatch

            if (_tracker.State >= TrackerState.HasDispatch)
            {
                if (_tracker.State == TrackerState.HasDispatch
                    || MessageBox.Show("A flight is currently in progress. Are you sure you wish to cancel the current tracking and fetch another dispatch?", "Cancel tracking?", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    await _tracker.Stop();

                }
            }

            try
            {
                var dispatch = await _api.GetDispatchInfoAsync();
                if (dispatch.IsEmpty == 0)
                {
                    var dispatchCargo = await _api.GetDispatchCargoAsync();
                    // load bookings into grid
                    dgBookings.ItemsSource = dispatchCargo;
                    dgBookings.Columns.Last().Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
                else
                {
                    lblDeadHead.Visibility = Visibility.Visible;
                }

                SetStatusMessage("Ok");

                _tracker.SetDispatchAndReset(dispatch);
            }
            catch (Exception ex)
            {
                SetStatusMessage(ex.Message, MessageState.Error);
                if (ex.Message == "Fetching dispatch info: No Content")
                {
                    dgBookings.ItemsSource = null;
                    grpFlight.Visibility = Visibility.Hidden;
                }
            }
            lblFetch.Visibility = Visibility.Hidden;
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

        internal void SetStatusMessage(string message, MessageState state = MessageState.OK)
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _simConnect?.OpenConnection();
        }

        private void chkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            _tracker.AllowStart = chkAutoStart.IsChecked == true;
        }

        private void chkQuickstart_Checked(object sender, RoutedEventArgs e)
        {
            _tracker.AllowEngineHotstart = chkQuickstart.IsChecked == true;
        }

        private void chkSimText_Checked(object sender, RoutedEventArgs e)
        {
            _simConnect.SendSimText = chkTextToSim.IsChecked == true;
            Settings.Default.ShowSimText = chkTextToSim.IsChecked == true;
        }
    }
}
