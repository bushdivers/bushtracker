using BushDiversTracker.Models.NonApi;
using BushDiversTracker.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BushDiversTracker
{
    /// <summary>
    /// Interaction logic for DebugScenario.xaml
    /// </summary>
    public partial class DebugScenario : Window
    {
        private readonly SimServiceFake _fake;

        internal DebugScenario(SimServiceFake fake)
        {
            InitializeComponent();
            _fake = fake;
            SyncUIFromFake();
        }

        #region UI helpers

        private void ApplyUIToData()
        {
            var d = _fake.CurrentData;
            if (double.TryParse(txtLat.Text, out var lat)) d.latitude = lat;
            if (double.TryParse(txtLon.Text, out var lon)) d.longitude = lon;
            if (double.TryParse(txtAlt.Text, out var alt)) { d.indicated_altitude = alt; d.plane_altitude = alt; }
            if (double.TryParse(txtAgl.Text, out var agl)) d.alt_above_ground = agl;
            if (double.TryParse(txtAirspeed.Text, out var spd)) { d.airspeed_true = spd; d.airspeed_indicated = spd; }
            if (double.TryParse(txtFuel.Text, out var fuel)) d.fuel_qty = fuel;
            if (double.TryParse(txtVspeed.Text, out var vs)) d.vspeed = vs;
            d.on_ground = chkOnGround.IsChecked == true ? 1 : 0;
            d.eng1_combustion = chkEng1.IsChecked == true ? 1 : 0;
            _fake.CurrentData = d;
        }

        private void ApplyUIToSettings()
        {
            var s = _fake.CurrentSettings;
            s.aircraft_name = txtAircraftName.Text;
            if (double.TryParse(txtPayload.Text, out var p))
            {
                s.payload_station_weight[0] = p;
                s.total_weight = p + 170;
            }
            _fake.CurrentSettings = s;
        }

        private void SyncUIFromFake()
        {
            var d = _fake.CurrentData;
            txtLat.Text = d.latitude.ToString("F4");
            txtLon.Text = d.longitude.ToString("F4");
            txtAlt.Text = d.indicated_altitude.ToString("F0");
            txtAgl.Text = d.alt_above_ground.ToString("F0");
            txtAirspeed.Text = d.airspeed_true.ToString("F0");
            txtFuel.Text = d.fuel_qty.ToString("F0");
            txtVspeed.Text = d.vspeed.ToString("F0");
            chkOnGround.IsChecked = d.on_ground == 1;
            chkEng1.IsChecked = d.eng1_combustion == 1;

            var s = _fake.CurrentSettings;
            txtAircraftName.Text = s.aircraft_name;
            txtPayload.Text = s.payload_station_weight[0].ToString("F0");
        }

        private void ApplyAndPush(SimData d)
        {
            _fake.CurrentData = d;
            SyncUIFromFake();
            _fake.PushData();
        }

        #endregion

        #region Quick scenario buttons 

        private void btnOnGround_Click(object sender, RoutedEventArgs e)
        {
            var d = _fake.CurrentData;
            d.on_ground = 1; d.eng1_combustion = 0;
            d.indicated_altitude = 72; d.plane_altitude = 72; d.alt_above_ground = 0;
            d.airspeed_true = 0; d.airspeed_indicated = 0; d.vspeed = 0;
            ApplyAndPush(d);
        }

        private void btnTakeOff_Click(object sender, RoutedEventArgs e)
        {
            var d = _fake.CurrentData;
            d.on_ground = 0; d.eng1_combustion = 1;
            d.indicated_altitude = 250; d.plane_altitude = 250; d.alt_above_ground = 130;
            d.airspeed_true = 0; d.airspeed_indicated = 0; d.vspeed = 0;
            ApplyAndPush(d);
        }

        private void btnCruise_Click(object sender, RoutedEventArgs e)
        {
            var d = _fake.CurrentData;
            d.on_ground = 0; d.eng1_combustion = 1;
            d.indicated_altitude = 5500; d.plane_altitude = 5500; d.alt_above_ground = 5200;
            d.airspeed_true = 120; d.airspeed_indicated = 118; d.vspeed = 0;
            ApplyAndPush(d);
        }

        private void btnApproach_Click(object sender, RoutedEventArgs e)
        {
            var d = _fake.CurrentData;
            d.on_ground = 0; d.eng1_combustion = 1;
            d.indicated_altitude = 800; d.plane_altitude = 800; d.alt_above_ground = 600;
            d.airspeed_true = 70; d.airspeed_indicated = 70; d.vspeed = -500;
            ApplyAndPush(d);
        }

        private void btnLand_Click(object sender, RoutedEventArgs e)
        {
            var d = _fake.CurrentData;
            d.on_ground = 1; d.eng1_combustion = 1;
            d.indicated_altitude = 72; d.plane_altitude = 72; d.alt_above_ground = 0;
            d.airspeed_true = 0; d.airspeed_indicated = 0; d.vspeed = 0;
            ApplyAndPush(d);
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _fake.CloseConnection();
        }

        #endregion

        private void btnPushData_Click(object sender, RoutedEventArgs e)
        {
            ApplyUIToData();
            _fake.PushData();
        }

        private void btnPushSettings_Click(object sender, RoutedEventArgs e)
        {
            ApplyUIToSettings();
            _fake.PushSettings();
        }

        private void btnTriggerLanding_Click(object sender, RoutedEventArgs e)
        {
            double.TryParse(txtGforce.Text, out var gforce);
            double.TryParse(txtLandingVs.Text, out var vs);
            _fake.TriggerLanding(new SimLandingData
            {
                touchdown_lat = _fake.CurrentData.latitude,
                touchdown_lon = _fake.CurrentData.longitude,
                touchdown_velocity = vs,
                touchdown_pitch = 2.0,
                touchdown_bank = 0,
                touchdown_heading_m = _fake.CurrentData.heading_m,
                touchdown_heading_t = _fake.CurrentData.heading_t,
            });
        }
    }
}
