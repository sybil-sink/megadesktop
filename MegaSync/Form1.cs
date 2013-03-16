using System;
using System.Windows.Forms;
using System.IO;
using MegaApi;
using SyncLib;
using System.Threading;
using System.Diagnostics;

namespace MegaSync
{
    public partial class Form1 : Form
    {
        MegaUser auth;
        Mega api;

        public Form1()
        {
            InitializeComponent();

            //AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            //    {
            //        Invoke(new Action(() =>
            //            {
            //                notifyIcon1.ShowBalloonTip(500, "Mega Sync", "Internal error", new ToolTipIcon());
            //                Show();
            //                WindowState = FormWindowState.Normal;
            //            }));
            //    };

            System.Net.ServicePointManager.DefaultConnectionLimit = 50;
            notifyIcon1.Icon = Properties.Resources.min;
            notifyIcon1.Click += notifyIcon1_MouseClick;

            if (!string.IsNullOrEmpty(Properties.Settings.Default.folderPath))
            {
                textBoxFolder.Text = Properties.Settings.Default.folderPath;
            }
            else
            {
                textBoxFolder.Text = Path.Combine(
                        Environment.GetEnvironmentVariable("USERPROFILE"),
                        "MegaDesktop");
            }

            auth = Mega.LoadAccount(GetUserKeyFilePath());
            if (auth != null)
            {
                textBoxEmail.Text = auth.Email;
                textBoxPassword.Text = "password";
            }
        }

        string GetUserKeyFilePath()
        {
            return GetUserFolder() + "user.dat";
        }
        string GetMetadataPath()
        {
            return GetUserFolder() + "sync.metadata";
        }

        private static string GetUserFolder()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            userDir += System.IO.Path.DirectorySeparatorChar + "MegaSync";
            if (!Directory.Exists(userDir)) { Directory.CreateDirectory(userDir); }
            userDir += System.IO.Path.DirectorySeparatorChar;
            return userDir;
        }

        private void buttonExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                textBoxFolder.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(textBoxEmail.Text) || string.IsNullOrEmpty(textBoxPassword.Text))
            {
                return;
            }
            buttonBrowse.Enabled = false;
            textBoxFolder.Enabled = false;
            buttonStart.Enabled = false;
            buttonExit.Enabled = false;
            if (!Directory.Exists(textBoxFolder.Text)) { Directory.CreateDirectory(textBoxFolder.Text); }

            if (Properties.Settings.Default.folderPath != textBoxFolder.Text && File.Exists(GetMetadataPath()))
            {
                File.Delete(GetMetadataPath());
            }

            Properties.Settings.Default.folderPath = textBoxFolder.Text;
            Properties.Settings.Default.Save();

            textBoxLoginStatus.Text = "Connecting...";
            textBoxEmail.Enabled = false;
            textBoxPassword.Enabled = false;

            
            if ((auth != null && textBoxEmail.Text != auth.Email)
                || textBoxPassword.Text != "password"
                || auth == null)
            {
                auth = new MegaUser(textBoxEmail.Text, textBoxPassword.Text);
            }

            Mega.Init(auth, (m) =>
                {
                    Invoke(new Action(() =>
                        { textBoxLoginStatus.Text = "Working."; }));
                    StartSync(m);
                }, (i) => 
                {
                    Invoke(new Action(() => 
                        {
                            textBoxLoginStatus.Text = "Invalid login or password";
                            textBoxEmail.Enabled = true;
                            textBoxPassword.Enabled = true;
                            buttonStart.Enabled = true;
                            buttonExit.Enabled = true;
                            textBoxFolder.Enabled = true;
                            buttonStart.Enabled = true;
                        }));
                });
            
        }

        private void StartSync(Mega m)
        {
            api = m;
            m.SaveAccount(GetUserKeyFilePath());
            Invoke(new Action(() => 
                {
                    buttonExit.Enabled = true;
                    WindowState = FormWindowState.Minimized;
                }));

            Log("Working...");


            var sync = new Sync(Properties.Settings.Default.folderPath, GetMetadataPath(), api, "sync");
            sync.ChangePerformed += (s, e) =>
            {
                Log("{0} [{1}]: {2}", e.IsLocal ? "local" : "remote", e.Time.ToShortTimeString(), e.Message);
            };
            sync.SyncError += (s, e) => 
            {
                Log("Synchronization error: {1} \r\n{0}", e.Exception, e.Message);
                Invoke(new Action(() =>
                {
                    notifyIcon1.ShowBalloonTip(60000, "Synchronization Error", e.Message + "\r\n" + e.Exception.Message, ToolTipIcon.Error);
                }));
            };
            sync.SyncEnded += (s, e) =>
            {
                Log("Sync ended at {0}", DateTime.Now.ToShortTimeString());
            };
            sync.SyncStarted += (s, e) =>
                {
                    Log("Sync started at {0}", DateTime.Now.ToShortTimeString());
                };
            sync.StartSyncing();
            
          
        }

        private void Log(string format, params object[] args)
        {
            Invoke(new Action(() =>
                {
                    textBoxStatus.Text += String.Format(format + Environment.NewLine, args);
                    textBoxStatus.SelectionStart = textBoxStatus.Text.Length;
                    textBoxStatus.ScrollToCaret();
                }
            ));
        }

        private NotifyIcon notifyIcon1 = new NotifyIcon();
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                notifyIcon1.Visible = true;
                notifyIcon1.ShowBalloonTip(500, "Mega Sync", "The folders are now syncing...", new ToolTipIcon());
                Hide();
            }
            else if (WindowState == FormWindowState.Normal)
            {
                notifyIcon1.Visible = false;
            }
        }

        private void notifyIcon1_MouseClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void buttonFeedback_Click(object sender, EventArgs e)
        {
            Process.Start("http://megadesktop.uservoice.com/forums/191321-general");
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://megadesktop.com/");
        }
    }
}
