using MegaApi.Comms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MegaSync
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            Application.ThreadException += Application_ThreadException;

            ShowNoticeForm();


            Application.Run(new Form1());
        }

        static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            GoogleAnalytics.SendTrackingRequest("/Application_ThreadException_" + e.Exception.GetType().Name);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            GoogleAnalytics.SendTrackingRequest("/CurrentDomain_UnhandledException_" + e.ExceptionObject.GetType().Name);
        }

        private static void ShowNoticeForm()
        {
            if (Properties.Settings.Default.AlphaNoticeAccepted == false)
            {
                var dr = new NoticeWindow().ShowDialog();
                if (dr == DialogResult.OK)
                {
                    Properties.Settings.Default.AlphaNoticeAccepted = true;
                    Properties.Settings.Default.Save();
                }
            }
        }

    }
}
