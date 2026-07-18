using BushDiversTracker.Models.Enums;
using BushDiversTracker.Models.NonApi;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Threading;

namespace BushDiversTracker.Services
{
    internal class SimServiceFake : ISimService
    {
        public event EventHandler OnSimConnected;
        public event EventHandler OnSimDisconnected;
        public event EventHandler<SimData> OnSimDataReceived;
        public event EventHandler<SimLandingData> OnLandingDataReceived;
        public event EventHandler<SimSettingsData> OnFlightSettingsReceived;

        private readonly DispatcherTimer _timer;
        private bool _isConnected;

        public SimVersion? Version => SimVersion.Fake;
        public bool IsConnected => _isConnected;
        public bool IsUserControlled => true;
        public bool SendSimText { get; set; }

        // Exposed so ScenarioWindow can edit these directly
        public SimData CurrentData;
        public SimSettingsData CurrentSettings;


        public SimServiceFake()
        {
            CurrentData = new SimData
            {
                camera_state = 2, // cockpit
                latitude = -6.36188,
                longitude = 143.23070,
                indicated_altitude = 72,
                plane_altitude = 72,
                alt_above_ground = 0,
                on_ground = 1,
                eng1_combustion = 0,
                fuel_qty = 100,
            };

            CurrentSettings = BuildDefaultSettings();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (_, _) =>
            {
                // Keep zulu_time moving so IsNull stays false
                var d = CurrentData;
                d.zulu_time = (int)DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds;
                CurrentData = d;
                OnSimDataReceived?.Invoke(this, CurrentData);
            };
        }

        public void OpenConnection()
        {
            _isConnected = true;
            _timer.Start();
            OnSimConnected?.Invoke(this, EventArgs.Empty);
            OnFlightSettingsReceived?.Invoke(this, CurrentSettings);
        }

        public void CloseConnection()
        {
            _isConnected = false;
            _timer.Stop();
            OnSimDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public void SendTextToSim(string text) { /* no-op */ }
        public void SetStrictMode(bool strictMode, double dispatchWeight) { /* no-op */ }

        /// <summary>Immediately fire OnSimDataReceived with current state.</summary>
        public void PushData()
        {
            var d = CurrentData;
            d.zulu_time = (int)DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds;
            CurrentData = d;
            OnSimDataReceived?.Invoke(this, CurrentData);
        }

        /// <summary>Immediately fire OnFlightSettingsReceived with current settings.</summary>
        public void PushSettings() => OnFlightSettingsReceived?.Invoke(this, CurrentSettings);

        /// <summary>Fire OnLandingDataReceived with the given landing data.</summary>
        public void TriggerLanding(SimLandingData data) => OnLandingDataReceived?.Invoke(this, data);

        private static SimSettingsData BuildDefaultSettings()
        {
            var s = new SimSettingsData
            {
                aircraft_name = "Fake Cessna 172",
                atcId = "FAKE",
                atcType = "C172",
                atcModel = "C172",
                category = "Airplane",
                is_unlimited_fuel = 0,
                is_slew_mode = 0,
                payload_station_count = 1,
                payload_station_name = new SimSettingsData.MarshalledString[SimSettingsData.MAX_PAYLOAD_STATIONS],
                payload_station_weight = new double[SimSettingsData.MAX_PAYLOAD_STATIONS],
                total_weight = 3500,
            };
            s.payload_station_name[0] = new SimSettingsData.MarshalledString { value = "Cargo" };
            s.payload_station_weight[0] = 500;
            return s;
        }
    }
}
