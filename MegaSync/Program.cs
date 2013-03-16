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

            ShowNoticeForm();


            Application.Run(new Form1());
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
