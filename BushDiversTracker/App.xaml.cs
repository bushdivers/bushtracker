using BushDiversTracker.Services;
using Meziantou.Framework;
using System;
using System.Windows;

namespace BushDiversTracker
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        SingleInstance? _instance = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            _instance = new SingleInstance(new Guid("5D20777C-0E7B-44F9-A183-8D73861F0882"));
            if (!_instance.StartApplication())
            {
                _instance.NotifyFirstInstance([]);
                Shutdown();
                return;
            }

            _instance.NewInstance += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow != null && mainWindow is MainWindow mw)
                    {
                        mw.ShowFromTray();
                    }
                });
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _instance?.Dispose();
            base.OnExit(e);
        }

        private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            var message = "Are you sure you want to close Bush Tracker? If you have an active flight, progress will be lost";
            MessageBoxResult result;
            result = MessageBox.Show(message, "Bush Tracker", MessageBoxButton.YesNo);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }

            HelperService.CancelFlightOnExit();
        }
    }
}
