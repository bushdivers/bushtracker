﻿using BushDiversTracker.Models.Enums;
using BushDiversTracker.Models.NonApi;
using BushDiversTracker.Models;
using System;
using System.Threading.Tasks;
using System.Windows;
using BushDiversTracker.Properties;
using Windows.Data.Text;

namespace BushDiversTracker.Services
{
    internal class TrackerService
    {
        // Define events that other classes can subscribe to
        public event EventHandler<MessageEventArgs> OnStatusMessage;
        public struct MessageEventArgs
        {
            public string Message { get; set; }
            public MainWindow.MessageState State { get; set; }
        }

        public event EventHandler<TrackingStartErrorArgs> OnDispatchError;
        public struct TrackingStartErrorArgs
        {
            public bool DepartureError { get; init; }
            public bool AircraftError { get; init; }
            public bool FuelError { get; init; }
            public bool CargoError { get; init; }
            public bool GameSettingsError { get; init; }
        }

        public event EventHandler<TrackerState> OnTrackerStateChanged;
        public enum TrackerState
        {
            None,
            HasDispatch,
            ReadyToStart,
            InFlight,
            Shutdown,
        }

        public event EventHandler<Dispatch> OnSetDispatch;

        private TrackerState state = TrackerState.None;
        public TrackerState State { get => state; }
        public bool AllowStart = Settings.Default.AutoStart;
        public bool AllowEngineHotstart = false;

        private Dispatch dispatchData = null;
        public Dispatch Dispatch { get => dispatchData; }

        private double currentDistance = 0;
        private double startFuelQty;
        private double endFuelQty;
        private string startTime;
        private string endTime;
        private bool engineHotstart;
        private PirepStatusType flightStatus;
        public PirepStatusType FlightStatus
        {
            get => flightStatus; private set
            {
                if (flightStatus == value)
                    return;

                flightStatus = value;
                OnFlightStatusChanged?.Invoke(this, flightStatus);
            }
        }
        public event EventHandler<PirepStatusType> OnFlightStatusChanged;

        protected SimLandingData? worstLanding = null;
        protected SimSettingsData? simFlightSettings = null;
        public SimSettingsData SimFlightSettings => simFlightSettings ?? new SimSettingsData();
        protected bool bFlightSettingsInvalidated = false;

        protected SimData lastSimData;

        protected DateTime dataLastSent;

        private readonly MainWindow _mainWindow = null;
        private readonly APIService _api = null;
        private ISimService _sim = null;

        public TrackerService(MainWindow mainWindow, ISimService simService, APIService api)
        {
            _mainWindow = mainWindow;
            _api = api;
            SetSimService(simService);
        }

        public async void SetSimService(ISimService simService)
        {
            await Stop();

            _sim = simService;
            if (_sim != null)
            {
                simService.OnSimDataReceived += SimService_OnSimDataReceived;
                simService.OnLandingDataReceived += SimService_OnLandingDataReceived;
                simService.OnFlightSettingsReceived += SimService_OnFlightSettingsReceived;
            }
        }

        public void Start()
        {
            // Start tracking
        }

        public async Task<bool> Stop()
        {
            // Stop tracking
            try
            {
                if (dispatchData != null)
                {
                    if (state == TrackerState.Shutdown)
                    {
                        if (!await SubmitFlight())
                            throw new Exception("Error submitting flight");
                    }
                    else if (!await _api.CancelTrackingAsync())
                        throw new Exception("Error resetting on server");

                    _mainWindow.SetStatusMessage(state == TrackerState.Shutdown ? "Dispatch submitted" : "Ok");
                    SetDispatchAndReset(null);
                }
                return true;
            }
            catch (Exception ex)
            {
                _mainWindow.SetStatusMessage("Error cancelling tracking: " + ex.Message, MainWindow.MessageState.Error);
            }

            return false;
        }

        /// <summary>
        /// Check for diversion. This does _not_ check if the aircraft is on the ground or active
        /// </summary>
        /// <returns></returns>
        protected async Task<bool> CheckAndDivert()
        {
            double distance = HelperService.CalculateDistance((double)dispatchData.ArrLat, (double)dispatchData.ArrLon, lastSimData.latitude, lastSimData.longitude, true);
            if (distance <= 2)
                return true;

            // get nearest airport and update pirep destination (return icao)
            NewLocationRequest req = new()
            {
                Lat = lastSimData.latitude,
                Lon = lastSimData.longitude,
                PirepId = dispatchData.Id
            };

            try
            {
                NewLocationResponse newLocation = await _api.PostNewLocationAsync(req);

                dispatchData.ArrLat = newLocation.Lat;
                dispatchData.ArrLon = newLocation.Lon;
                dispatchData.Arrival = newLocation.Icao;
                OnSetDispatch?.Invoke(this, dispatchData);
            }
            catch (Exception)
            {
                //_mainWindow.SetStatusMessage(e.Message, MainWindow.MessageState.Error);
                _mainWindow.SetStatusMessage("No airport within 2NM", MainWindow.MessageState.Error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ends a flight and submits the pirep
        /// </summary>
        public async Task<bool> SubmitFlight()
        {
            // if we're still connected and not shutdown and not allowing hot ending
            if (worstLanding == null
                || simFlightSettings == null
                || lastSimData.IsNull
                || (_sim.IsConnected
                    && state != TrackerState.Shutdown
                    && !(AllowEngineHotstart && state == TrackerState.InFlight && FlightStatus == PirepStatusType.LANDED)))
                return false;

            if (AllowEngineHotstart && Math.Abs(lastSimData.surface_rel_groundspeed) > 10)
            {
                _mainWindow.SetStatusMessage("Aircraft not stationary", MainWindow.MessageState.Error);
                return false;
            }

            // Check we're at a landing airport
            if (!await CheckAndDivert())
            {
                MessageBox.Show(_mainWindow, "Unable to find landing airport.\n\nPlease resume flying and land within 2NM of an airport", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
                _mainWindow.SetStatusMessage("No airport within 2NM", MainWindow.MessageState.Error);
                return false;
            }

            var thisLanding = worstLanding.Value;
            var thisSettings = simFlightSettings.Value;

            // Do submit
            Pirep pirep = new()
            {
                PirepId = dispatchData.Id,
                FuelUsed = startFuelQty - endFuelQty,
                LandingRate = thisLanding.touchdown_velocity,
                TouchDownLat = thisLanding.touchdown_lat,
                TouchDownLon = thisLanding.touchdown_lon,
                TouchDownBank = thisLanding.touchdown_bank,
                TouchDownPitch = thisLanding.touchdown_pitch,
                BlockOffTime = startTime,
                BlockOnTime = endTime,
                Distance = currentDistance,
                AircraftUsed = thisSettings.aircraft_name,
                SimUsed = _sim.Version.Value.ToString(),
                EngineHotStart = engineHotstart
            };

            bool res = false;
            try
            {
                res = await _api.PostPirepAsync(pirep);
            }
            catch (Exception)
            {
                _mainWindow.SetStatusMessage("Error submitting pirep", MainWindow.MessageState.Error);
            }

            if (res)
            {
                _mainWindow.SetStatusMessage("Pirep submitted", MainWindow.MessageState.OK);
                SetDispatchAndReset(null);
            }
            else
            {
                MessageBox.Show(_mainWindow, "Pirep Not Submitted!", "Bush Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return res;
        }


        /// <summary>
        /// Set new dispatch data and reset tracker for a new flight
        /// </summary>
        /// <param name="dispatch"></param>
        internal void SetDispatchAndReset(Dispatch dispatch)
        {
            // Set dispatch data
            //            if (state == TrackerState.None)
            {
                dispatchData = dispatch;

                SetTrackerState(dispatch != null ? TrackerState.HasDispatch : TrackerState.None);

                currentDistance = 0.0;
                startFuelQty = 0.0;
                endFuelQty = 0.0;
                startTime = "";
                endTime = "";
                FlightStatus = dispatchData != null ? PirepStatusType.BOARDING : PirepStatusType.PREFLIGHT;
                engineHotstart = false;
                worstLanding = null;
                lastSimData = new SimData();

                // Landing vars not reset here, the tracker state logic does it when setting ready to start

                if (dispatchData != null)
                    _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.BOARDING });

                OnSetDispatch?.Invoke(this, dispatch);
            }
        }

        private void SetTrackerState(TrackerState newState)
        {
            state = newState;
            _sim.SetStrictMode(state == TrackerState.ReadyToStart || state == TrackerState.InFlight);
            OnTrackerStateChanged?.Invoke(this, state);
        }

        private async void SimService_OnSimDataReceived(object sender, SimData data)
        {
            // If we have no dispatch data, ignore
            if (state == TrackerState.None || dispatchData == null || simFlightSettings == null)
                return;

            // If in flight, and static settings (slew, unlimited fuel, etc) have changed, or if our fuel qty has increased by more than 1% (for rounding), abort
            if (state >= TrackerState.InFlight 
                && (bFlightSettingsInvalidated || data.fuel_qty > Math.Ceiling(lastSimData.fuel_qty * 1.01)))
            {
                Task<bool> task = _api.CancelTrackingAsync();

                SetTrackerState(TrackerState.None);
                MessageBox.Show(_mainWindow, "It looks like you have abandoned your flight, tracking will now stop and your progress cancelled.\nYour aircraft or game settings has been modified.", "Bush Divers", MessageBoxButton.OK);

                await task;
                if (task.IsCompletedSuccessfully && task.Result)
                {
                    _mainWindow.SetStatusMessage("Tracking stopped");
                    SetDispatchAndReset(null);
                }
                return;
            }

            if (state == TrackerState.HasDispatch)
            {
                if (CheckReadyForStart(new CheckStructure
                {
                    FlightSettings = simFlightSettings.Value,
                    Fuel = data.fuel_qty,
                    CurrentLat = data.latitude,
                    CurrentLon = data.longitude,
                    CurrentEngineStatus = data.EnginesRunning && !AllowEngineHotstart
                }))
                {
                    if (!AllowStart)
                    {
                        _mainWindow.SetStatusMessage("Waiting for start checkbox", MainWindow.MessageState.Neutral);
                    }
                    else
                    {
                        bFlightSettingsInvalidated = false;
                        engineHotstart = data.EnginesRunning;

                        SetTrackerState(TrackerState.ReadyToStart);
                        _mainWindow.SetStatusMessage("Pre-flight|Loading");
                        _sim.SendTextToSim("Bush Tracker Status: Pre-Flight - Ready");

                        // Clear landing rate so next change event as per simconnect is viewed as 'new'
                        worstLanding = null;
                    }
                }
            }
            else if (state == TrackerState.ReadyToStart)
            {
                // Once engines started, set block time and start location
            if (data.EnginesRunning && data.on_ground != 0)
                {
                    startTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    startFuelQty = data.fuel_qty;
                    FlightStatus = PirepStatusType.BOARDING;
                    SetTrackerState(TrackerState.InFlight);

                    _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.BOARDING });
                }
            }
            else if (state == TrackerState.InFlight)
            {
                bool onGround = data.on_ground != 0;
            
                // check for take off
                if (FlightStatus == PirepStatusType.BOARDING && !onGround && data.plane_altitude > 200) // arbitrary number to avoid advancing state on bouncy water takeoff
                {
                    FlightStatus = PirepStatusType.DEPARTED;
                    _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.DEPARTED });

                    _mainWindow.SetStatusMessage("Departed");
                    _sim.SendTextToSim("Bush Tracker Status: Departed - Have a good flight!");
                }
                else if (FlightStatus == PirepStatusType.DEPARTED && data.plane_altitude > 1000)
                {
                    FlightStatus = PirepStatusType.CRUISE;
                    _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.CRUISE });
                    _mainWindow.SetStatusMessage("Cruise");
                }
                else if (FlightStatus == PirepStatusType.CRUISE)
                {
                    if (onGround && lastSimData.on_ground == 0 && data.surface_type == 2) // landed on water
                    {
                        var rate = -(data.vspeed + lastSimData.vspeed) * 60.0 / 2.0;    // f/s to f/m... api needs f/s on the pirep progress log

                        // In case of 'bounce' between the two refs
                        if (rate < 0.0)
                            rate = 0.0;

                        if (worstLanding == null || worstLanding?.touchdown_velocity < rate)
                        {
                            worstLanding = new SimLandingData
                            {
                                touchdown_velocity = rate,
                                touchdown_pitch = data.ac_pitch,
                                touchdown_bank = data.ac_bank,
                                touchdown_lat = data.latitude,
                                touchdown_lon = data.longitude
                            };
                        }
                    }

                    if (onGround && double.Abs(data.surface_rel_groundspeed) < 40)
                    {
                        FlightStatus = PirepStatusType.LANDED;
                        _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.LANDED });

                        _mainWindow.SetStatusMessage("Landed");
                        _sim.SendTextToSim("Bush Tracker Status: Landed");
                    }
                }
                else if (FlightStatus == PirepStatusType.LANDED)
                {
                    if (!onGround && data.plane_altitude > 50)
                    {
                        FlightStatus = PirepStatusType.CRUISE;
                        _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.CRUISE });
                        _mainWindow.SetStatusMessage("Cruise");
                    }
            else if (!data.EnginesRunning && Math.Abs(data.surface_rel_groundspeed) < 15)
                    {
                        FlightStatus = PirepStatusType.ARRIVED;
                        SetTrackerState(TrackerState.Shutdown);

                        _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.ARRIVED });

                        _mainWindow.SetStatusMessage("Flight ended");
                        _sim.SendTextToSim("Bush Tracker Status: Flight ended - Thanks for working with Bush Divers");

                        endFuelQty = data.fuel_qty;
                        // endTime = HelperService.SetZuluTime(data1.zulu_time).ToString("yyyy-MM-dd HH:mm:ss");
                        endTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                        _ = CheckAndDivert();
                    }
                }
            }
            else if (state == TrackerState.Shutdown)
            {
                // If engines start again, go back to in-flight

                if (data.EnginesRunning)
                {
                    SetTrackerState(TrackerState.InFlight);
                    FlightStatus = PirepStatusType.LANDED;
                    _mainWindow.SetStatusMessage("Landed");
                    _ = _api.PostPirepStatusAsync(new PirepStatus { PirepId = dispatchData.Id, Status = (int)PirepStatusType.LANDED });
                }
            }

            if (state >= TrackerState.ReadyToStart)
            {
                if (!lastSimData.IsNull)
                {
                    // calc distance
                    var d = HelperService.CalculateDistance(lastSimData.latitude, lastSimData.longitude, data.latitude, data.longitude);
                    if (d > 50
                        || (_sim.Version == SimVersion.FS2024
                            && lastSimData.camera_state == data.camera_state // buffer cmera state changes due to MSFS 2024 'glitching' through MAIN_MENU when returning to game from settings menu
                            && (data.camera_state == SimServiceMSFS.CameraState.FS2024.WORLD_MAP || data.camera_state == SimServiceMSFS.CameraState.FS2024.MAIN_MENU)
                            )
                        )
                    {
                        if (state == TrackerState.Shutdown)
                        {
                            state = TrackerState.None;
                            await SubmitFlight();
                        }
                        else
                        {
                            Task<bool> task = _api.CancelTrackingAsync();

                            SetTrackerState(TrackerState.None);
                            MessageBox.Show(_mainWindow, "It looks like you have abandoned your flight, tracking will now stop and your progress cancelled." + "\n" + "You can start your flight again by returning to the departure location", "Bush Divers", MessageBoxButton.OK);

                            await task;
                            if (task.IsCompletedSuccessfully && task.Result)
                            {
                                _mainWindow.SetStatusMessage("Tracking stopped");
                                SetDispatchAndReset(null);
                            }
                        }

                        return;
                    }
                    currentDistance += d;
                    _mainWindow.lblDistance.Content = currentDistance.ToString("0.## nm"); // TODO: event?
                }
                else
                    currentDistance = 0.0;

                // Send data to api
                var headingChanged = HelperService.CheckForHeadingChange(lastSimData.heading_m, data.heading_m);
                var altChanged = HelperService.CheckForAltChange(lastSimData.indicated_altitude, data.indicated_altitude);
                // determine if data has changed or not
                if (headingChanged || altChanged || DateTime.UtcNow > dataLastSent.AddSeconds(60))
                {
                    _ = SendFlightLog(data);
                    dataLastSent = DateTime.UtcNow;
                }
            }

            lastSimData = data;
        }

        private void SimService_OnLandingDataReceived(object sender, SimLandingData data)
        {
            if (data.touchdown_lat == 0.0 && data.touchdown_lon == 0.0)
                return;

            bool updated = false;
            if (worstLanding == null || worstLanding?.touchdown_velocity < data.touchdown_velocity)
            {
                worstLanding = data;
                updated = true;
            }

            HelperService.WriteToLog("Landing data received: " + data.touchdown_velocity.ToString("0.##") + "fpm " + data.touchdown_pitch.ToString("0.##") + "deg / " + data.touchdown_bank.ToString("0.##") + "deg at " + data.touchdown_lat.ToString("0.##") + " " + data.touchdown_lon.ToString("0.##") + (updated ? " (new landing set)" : ""));
        }

        private void SimService_OnFlightSettingsReceived(object sender, SimSettingsData data)
        {

            if (data.is_unlimited_fuel != 0)
            {
                bFlightSettingsInvalidated = true;
                if (simFlightSettings?.is_unlimited_fuel == 0)
                    HelperService.WriteToLog("Flight settings changed: Unlimited fuel enabled");
            }

            if (data.is_slew_mode != 0)
            {
                bFlightSettingsInvalidated = true;
                if (simFlightSettings?.is_slew_mode == 0)
                    HelperService.WriteToLog("Flight settings changed: Slew mode enabled");
            }

            if (dispatchData != null && !WeightValid(data.total_weight, (double)dispatchData.TotalPayload))
            {
                bFlightSettingsInvalidated = true;
                if (simFlightSettings?.total_weight != data.total_weight)
                    HelperService.WriteToLog("Flight settings changed: Payload weight changed");
            }

            simFlightSettings = data;
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
            if (dispatchData == null)
            {
                OnDispatchError?.Invoke(this, new TrackingStartErrorArgs());
                return false;
            }

            bool aircraftError = false;
            bool fuelError = false;
            bool cargoError = false;
            bool departureError = false;

            // check aircraft contains text in chosen aircraft
            //var test = data.Aircraft.Contains(txtAircraftType.Text);
            //if (!data.Aircraft.Contains(txtAircraftType.Text) || data.AircraftType != txtAircraftType.Text)
            //{
            //    // set error text for aircraft
            //    lblAircraftError.Content = "Aircraft does not match";
            //    lblAircraftError.Visibility = Visibility.Visible;
            //    status = false;
            //}

            // check fuel qty matches planned fuel
            var tolerance = (double)dispatchData.PlannedFuel * 0.01;
            if (tolerance < 5.0)
                tolerance = 5;
            var maxVal = (double)dispatchData.PlannedFuel + tolerance;
            var minVal = (double)dispatchData.PlannedFuel - tolerance;
            if (data.Fuel < minVal || data.Fuel > maxVal)
                fuelError = true;

            cargoError = !WeightValid(data.FlightSettings.total_weight, (double)dispatchData.TotalPayload);

            bool settingsError = data.FlightSettings.is_unlimited_fuel != 0 || data.FlightSettings.is_slew_mode != 0;

            // check current position
            var distance = HelperService.CalculateDistance((double)dispatchData.DepLat, (double)dispatchData.DepLon, data.CurrentLat, data.CurrentLon, false);
            if (distance > 2)
                departureError = true;

            OnDispatchError?.Invoke(this, new TrackingStartErrorArgs { AircraftError = aircraftError, FuelError = fuelError, CargoError = cargoError, DepartureError = departureError, GameSettingsError = settingsError });

            bool isInSim = _sim.IsUserControlled;
            bool readyToStart = !aircraftError
                && !fuelError
                && !cargoError
                && !departureError
                && !settingsError;

            if (!isInSim)
                OnStatusMessage?.Invoke(this, new MessageEventArgs { Message = "Waiting for world to load", State = MainWindow.MessageState.Error });
            else if (!readyToStart)
                OnStatusMessage?.Invoke(this, new MessageEventArgs { Message = "Start conditions not met", State = MainWindow.MessageState.Error });
            else if (data.CurrentEngineStatus)
                OnStatusMessage?.Invoke(this, new MessageEventArgs { Message = "Shutdown engines before starting", State = MainWindow.MessageState.Error });
            else
                OnStatusMessage?.Invoke(this, new MessageEventArgs { Message = "Ready to start", State = MainWindow.MessageState.OK });

            return readyToStart && !data.CurrentEngineStatus && isInSim;
        }

        /// <summary>
        /// Sends a flight log to the api
        /// </summary>
        /// <param name="d">data from simconnect</param>
        public async Task SendFlightLog(SimData d)
        {
            var log = new FlightLog()
            {
                PirepId = dispatchData.Id,
                Lat = d.latitude,
                Lon = d.longitude,
                Heading = (int)d.heading_m,
                Altitude = (int)d.indicated_altitude,
                IndicatedSpeed = (int)d.airspeed_indicated,
                GroundSpeed = (int)d.airspeed_true,
                FuelFlow = d.fuel_flow,
                VS = d.vspeed,
                SimTime = HelperService.SetZuluTime(d.local_time),
                ZuluTime = HelperService.SetZuluTime(d.zulu_time),
                Distance = currentDistance
            };

            try
            {
                await _api.PostFlightLogAsync(log);
                _mainWindow.SetStatusMessage("Flight log updated", MainWindow.MessageState.OK);
            }
            catch (Exception e)
            {
                _mainWindow.SetStatusMessage("Error submitting flight update: " + e.Message, MainWindow.MessageState.Error);
            }
        }

        /// <summary>
        /// Check weight is within tolerance
        /// </summary>
        /// <param name="weight"></param>
        /// <param name="maxWeight"></param>
        /// <returns></returns>
        protected static bool WeightValid(double weight, double maxWeight)
        {
            // 1% or 5lbs, whichever is greater
            var tolerance = maxWeight * 0.01;
            if (tolerance < 5.0)
                tolerance = 5;

            return weight >= (maxWeight - tolerance) && weight <= (maxWeight + tolerance);
        }
    }
}
