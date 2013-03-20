using MegaApi.Comms;
using MegaDesktop;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace MegaWpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup_1(object sender, StartupEventArgs e)
        {
              AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
              Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
        }

        void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            GoogleAnalytics.SendTrackingRequest("/Desktop_Current_DispatcherUnhandledException_" + e.Exception.GetType().Name);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            GoogleAnalytics.SendTrackingRequest("/Desktop_CurrentDomain_UnhandledException_" + e.ExceptionObject.GetType().Name);
        }
    }
}
