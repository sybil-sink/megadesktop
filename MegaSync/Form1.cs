using System;
using System.Windows.Forms;
using System.IO;
using MegaApi;
using SyncLib;
using System.Threading;
using System.Diagnostics;
using MegaApi.Comms;
using System.Runtime.InteropServices;
using System.Reflection;
using shellink;

namespace MegaSync
{
    public partial class Form1 : Form
    {
        MegaUser auth;
        Mega api;

        public Form1()
        {
            InitializeComponent();

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
            if (string.IsNullOrEmpty(textBoxEmail.Text) 
                || string.IsNullOrEmpty(textBoxPassword.Text)
                || string.IsNullOrEmpty(textBoxFolder.Text)
                )
            {
                return;
            }
            buttonBrowse.Enabled = false;
            textBoxFolder.Enabled = false;
            buttonStart.Enabled = false;
            buttonExit.Enabled = false;
            if (!Directory.Exists(textBoxFolder.Text))
            {
                Directory.CreateDirectory(textBoxFolder.Text);
                Properties.Settings.Default.FolderBeautified = false;
            }

            var needResync = false;
            if (Properties.Settings.Default.folderPath != textBoxFolder.Text)
            {
                needResync = true;
                Properties.Settings.Default.FolderBeautified = false;
            }

            Properties.Settings.Default.folderPath = textBoxFolder.Text;
            Properties.Settings.Default.Save();

            CheckFolderBeautified();

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
                    StartSync(m, needResync);
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

        private void CheckFolderBeautified()
        {
            if (!Properties.Settings.Default.FolderBeautified)
            {
                BeautifyFolder(Properties.Settings.Default.folderPath);
            }
        }

        

        private void StartSync(Mega m, bool needResync)
        {
            api = m;
            m.SaveAccount(GetUserKeyFilePath());
            Invoke(new Action(() => 
                {
                    buttonExit.Enabled = true;
                    WindowState = FormWindowState.Minimized;
                }));

            Log("Working...");


            var sync = new Sync(Properties.Settings.Default.folderPath, api, "sync");
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
                GoogleAnalytics.SendTrackingRequest("/SyncError");
            };
            sync.SyncEnded += (s, e) =>
            {
                Log("Sync ended at {0}", DateTime.Now.ToShortTimeString());
            };
            sync.SyncStarted += (s, e) =>
                {
                    Log("Sync started at {0}", DateTime.Now.ToShortTimeString());
                };
            sync.StartSyncing(needResync);
            
          
        }

        private void Log(string format, params object[] args)
        {
            try
            {
                Invoke(new Action(() =>
                    {
                        textBoxStatus.Text += String.Format(format + Environment.NewLine, args);
                        textBoxStatus.SelectionStart = textBoxStatus.Text.Length;
                        textBoxStatus.ScrollToCaret();
                    }
                ));
            }
            catch { /* this is temporary */}
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

        private void Form1_Load(object sender, EventArgs e)
        {
            CheckFirstRun();
            //clean up v0.7 metadata
            EnsureOldVersionMetadataCleared();
        }

        

        private void CheckFirstRun()
        {
            if (MegaSync.Properties.Settings.Default.FirstRunLatestVer !=
                GoogleAnalytics.AppVersion)
            {
                GoogleAnalytics.SendTrackingRequest("FirstRun_Sync");
                MegaSync.Properties.Settings.Default.FirstRunLatestVer =
                GoogleAnalytics.AppVersion;
                MegaSync.Properties.Settings.Default.Save();
            }
            else
            {
                GoogleAnalytics.SendTrackingRequest("Run_Sync");
            }
        }


        // Remove old version's crap if any
        private void EnsureOldVersionMetadataCleared()
        {
            if (Properties.Settings.Default.OldVerMetadataCleared)
            {
                return;
            }


            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (localAppData.Substring(localAppData.Length - 1) != "\\")
            {
                localAppData += "\\";
            }
            localAppData += "MegaSync\\";

            DelFile(localAppData + "mega.Replicaid");
            DelFile(localAppData + "mega.Metadata");

            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            userDir += Path.DirectorySeparatorChar + "MegaSync" + Path.DirectorySeparatorChar;

            DelFile(userDir + "sync.metadata");

            Properties.Settings.Default.OldVerMetadataCleared = true;
            Properties.Settings.Default.Save();
        }

        private void DelFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }


        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section,
            string key, string val, string filePath);
        public void IniWriteValue(string path, string Section, string Key, string Value)
        {
            WritePrivateProfileString(Section, Key, Value, path);
        }
        private void BeautifyFolder(string path)
        {
            try
            {
                Properties.Settings.Default.FolderBeautified = true;
                Properties.Settings.Default.Save();

                Directory.CreateDirectory(path);
                var iniPath = Path.Combine(path, "desktop.ini");
                DelFile(iniPath);
                File.Create(iniPath).Close();

                var programDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                IniWriteValue(iniPath, ".ShellClassInfo", "IconResource", Path.Combine(programDirectory,"f.ico"));
                IniWriteValue(iniPath, ".ShellClassInfo", "InfoTip", "Your MEGA backup folder");

                File.SetAttributes(iniPath, File.GetAttributes(iniPath) | FileAttributes.System);
                File.SetAttributes(iniPath, File.GetAttributes(iniPath) | FileAttributes.Hidden);
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.System);


                var favPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Favorites),
                    Path.GetFileName(path)+".lnk");

                var favPath2 = Path.Combine(
                    Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.Favorites)),
                    "Links");
                favPath2 = Path.Combine(favPath2, Path.GetFileName(path) + ".lnk");


                CreateLink(path, favPath);
                CreateLink(path, favPath2);
            }
            catch { }
        }

        private static void CreateLink(string path, string favPath)
        {
            using (ShellLink shortcut = new ShellLink())
            {
                shortcut.Target = path;
                shortcut.WorkingDirectory = path;
                shortcut.Description = "Your MEGA backup folder";
                shortcut.DisplayMode = ShellLink.LinkDisplayMode.edmNormal;
                try { shortcut.Save(favPath); }
                catch { }
            }
        }
    }
}
