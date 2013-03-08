using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using MegaApi;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using MegaApi.DataTypes;
using MegaDesktop;
using System.Diagnostics;

namespace MegaWpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        Mega api;
        List<MegaNode> nodes;
        ObservableCollection<TransferHandle> transfers = new ObservableCollection<TransferHandle>();
        MegaNode currentNode;
        static string title = "Mega Desktop (beta)";
        public MainWindow()
        {
            CheckTos();

            System.Net.ServicePointManager.DefaultConnectionLimit = 50;
            var save = false;
            var userAccountFile = GetUserKeyFilePath();
            Login(save, userAccountFile);
        }

        private static void CheckTos()
        {
            if (MegaDesktop.Properties.Settings.Default.TosAccepted) { return; }
            else
            {
                TermsOfServiceWindow tos = new TermsOfServiceWindow();
                var res = tos.ShowDialog();
                if (!res.Value)
                {
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    MegaDesktop.Properties.Settings.Default.TosAccepted = true;
                    MegaDesktop.Properties.Settings.Default.Save();
                }
            }
        }

        private MegaUser Login(bool save, string userAccountFile)
        {
            MegaUser u;
            if ((u = Mega.LoadAccount(userAccountFile)) == null) { save = true; }
            SetStatus("Logging in...");
            Mega.Init(u, (m) =>
            {
                api = m;
                if (save) { SaveAccount(userAccountFile, "user.anon.dat"); }
                InitializeComponent();
                InitialLoadNodes();
                SetStatusDone();
                if (api.User.Status == MegaUserStatus.Anonymous)
                {
                    Invoke(() =>
                        {
                            buttonLogin.Visibility = System.Windows.Visibility.Visible;
                            buttonLogout.Visibility = System.Windows.Visibility.Collapsed;
                            Title = title + " - anonymous account";
                        });
                }
                else
                {
                    Invoke(() =>
                        {
                            Title = title + " - "+m.User.Email;
                            buttonLogin.Visibility = System.Windows.Visibility.Collapsed;
                            buttonLogout.Visibility = System.Windows.Visibility.Visible;
                        });
                }
            }, (e) => { MessageBox.Show("Error while loading account: " + e); Application.Current.Shutdown(); });
            return u;
        }

        private void SaveAccount(string userAccountFile, string backupFileName)
        {
            if (File.Exists(userAccountFile))
            {
                var backupFile = System.IO.Path.GetDirectoryName(userAccountFile) +
                                System.IO.Path.DirectorySeparatorChar +
                                backupFileName;
                File.Copy(userAccountFile, backupFile, true);
            }
            api.SaveAccount(GetUserKeyFilePath());
        }

        private void InitialLoadNodes()
        {
            Invoke(() => listBoxDownloads.ItemsSource = transfers);
            SetStatus("Retriving the list of files...");
            api.GetNodes((list) =>
            {
                Invoke(() =>
                {
                    buttonUpload.IsEnabled = true;
                    buttonRefresh.IsEnabled = true;
                    buttonLogout.IsEnabled = true;
                    buttonLogin.IsEnabled = true;
                });
                SetStatusDone();
                nodes = list;
                currentNode = list.Where(n => n.Type == MegaNodeType.RootFolder).FirstOrDefault();
                ShowFiles();
            }, e => SetStatusError(e));
        }

        private void ShowFiles(MegaNode parent = null, bool refresh = false)
        {
            if (parent == null) { parent = currentNode; }
            if (refresh)
            {
                SetStatus("Refreshing file info...");
                api.GetNodes((l) =>
                {
                    SetStatusDone();
                    nodes = l;
                    ShowFiles(parent);
                }, e => SetStatusError(e));
                return;
            }

            var list = nodes.Where(n => n.ParentId == parent.Id).ToList<MegaNode>();
            currentNode = parent.Type == MegaNodeType.Dummy ?
                nodes.Where(n => n.Id == parent.Id).FirstOrDefault() : parent;
            if (currentNode.Type != MegaNodeType.RootFolder)
            {
                var p = nodes.Where(n => n.Id == currentNode.ParentId).FirstOrDefault();
                list.Insert(0, new MegaNode
                {
                    Id = p.Id,
                    Attributes = new NodeAttributes { Name = ".." },
                    Type = MegaNodeType.Dummy

                });
            }

            Invoke(() => listBoxNodes.ItemsSource = list);
        }
        void Invoke(Action fn)
        {
            // does not work in 3.5:
            //Dispatcher.Invoke(fn);

            // 3.0 version:
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, (Delegate)fn);
        }
        void SetStatus(string text, params object[] args)
        {
            if (textBoxStatus == null) { return; }
            Invoke(() => textBoxStatus.Text = String.Format(text, args));

        }
        void SetStatusDone() { SetStatus("Done."); }
        void SetStatusError(int errno) { SetStatus("Error: {0}", errno); }

        string GetUserKeyFilePath()
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            userDir += System.IO.Path.DirectorySeparatorChar + "MegaDesktop";
            if (!Directory.Exists(userDir)) { Directory.CreateDirectory(userDir); }
            userDir += System.IO.Path.DirectorySeparatorChar;
            return userDir + "user.dat";
        }

        private void listBoxNodes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var clickedNode = (MegaNode)listBoxNodes.SelectedItem;
                if (clickedNode.Type == MegaNodeType.Dummy || clickedNode.Type == MegaNodeType.Folder)
                {
                    ShowFiles((MegaNode)listBoxNodes.SelectedItem);
                }
                if (clickedNode.Type == MegaNodeType.File)
                {
                    DownloadFile(clickedNode);
                }
            }
            catch (InvalidCastException) { return; }

        }

        private void DownloadFile(MegaNode clickedNode)
        {
            var d = new SaveFileDialog();
            d.FileName = clickedNode.Attributes.Name;
            if (d.ShowDialog() == true)
            {
                SetStatus("Starting download...");
                api.DownloadFile(clickedNode, d.FileName, h =>
                {
                    Invoke(() => transfers.Add(h));
                    SetStatusDone();
                }, e => SetStatusError(e));
            }
        }

        private void listBoxNodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listBoxNodes.SelectedItem != null)
            {
                var node = (MegaNode)listBoxNodes.SelectedItem;
                if (node.Type == MegaNodeType.File)
                {
                    buttonDownload.IsEnabled = true;
                    buttonDelete.IsEnabled = true;
                }
                else
                {

                    buttonDelete.IsEnabled = node.Type == MegaNodeType.Folder;
                    buttonDownload.IsEnabled = false;
                }
            }
            else
            {
                buttonDownload.IsEnabled = false;
                buttonDelete.IsEnabled = false;
            }
        }

        private void buttonUpload_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog();
            if (d.ShowDialog() == true)
            {
                SetStatus("Starting upload...");
                api.UploadFile(currentNode.Id, d.FileName, (h) =>
                {
                    Invoke(() => transfers.Add(h));
                    h.StatusChanged += (s, ev) =>
                    {
                        if (h.Status == TransferHandleStatus.Success)
                        {
                            ShowFiles(currentNode, true);
                        }
                    };
                    SetStatusDone();

                }, err => SetStatusError(err));


            }
        }

        private void buttonDelete_Click(object sender, RoutedEventArgs e)
        {
            var node = (MegaNode)listBoxNodes.SelectedItem;
            var type = (node.Type == MegaNodeType.Folder ? "folder" : "file");
            var text = String.Format("Are you sure to delete the {0} {1}?", type, node.Attributes.Name);
            if (MessageBox.Show(text, "Deleting " + type, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                api.RemoveNode(node, () => ShowFiles(currentNode, true), err => SetStatusError(err));
            }
        }

        private void buttonLogin_Click(object sender, RoutedEventArgs e)
        {
            var w = new WindowLogin();
            w.OnLoggedIn += (s, args) =>
            {
                api = args.Api;
                SaveAccount(GetUserKeyFilePath(), "user.anon.dat");
                Invoke(() =>
                    {
                        CancelTransfers();
                        w.Close();
                        buttonLogin.Visibility = System.Windows.Visibility.Collapsed;
                        buttonLogout.Visibility = System.Windows.Visibility.Visible;
                        Title = title + " - " + api.User.Email;
                    });
                InitialLoadNodes();
            };
            w.ShowDialog();
        }

        private void CancelTransfers()
        {
            lock (transfers)
            {
                foreach (var transfer in transfers)
                {
                    transfer.CancelTransfer();
                }
            }
        }

        private void buttonDownload_Click(object sender, RoutedEventArgs e)
        {
            var node = (MegaNode)listBoxNodes.SelectedItem;
            if (node.Type == MegaNodeType.File)
            {
                DownloadFile(node);
            }
            else
            {
                // todo
            }

            // todo if multiselect
        }

        private void buttonRefresh_Click(object sender, RoutedEventArgs e)
        {
            //api.CreateFolder(currentNode.Id, "testtest", () => { }, (i) => { });
            ShowFiles(currentNode, true);
        }
        private void buttonLogout_Click(object sender, RoutedEventArgs e)
        {
            CancelTransfers();
            Invoke(() =>
                {
                    transfers.Clear();
                    listBoxNodes.ItemsSource = null;
                });
            var userAccount = GetUserKeyFilePath();
            // to restore previous anon account
            //File.Move(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(userAccount), "user.anon.dat"), userAccount);
            // simply drop logged in account
            File.Delete(userAccount);
            Login(false, userAccount);
        }
        void CancelTransfer(TransferHandle handle, bool warn = true)
        {
            if (warn && (handle.Status == TransferHandleStatus.Downloading || handle.Status == TransferHandleStatus.Uploading))
            {
                var type = (handle.Status == TransferHandleStatus.Downloading ? "download" : "upload");
                var text = String.Format("Are you sure to cancel the {0} process for {1}?", type, handle.Node.Attributes.Name);
                if (MessageBox.Show(text, "Cancel " + type, MessageBoxButton.YesNo) == MessageBoxResult.No)
                {
                    return;
                }
            }
            handle.CancelTransfer();
        }
        private void ButtonCancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var handle = button.DataContext as TransferHandle;
            CancelTransfer(handle);
        }

        private void ButtonRemoveTransfer_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var handle = button.DataContext as TransferHandle;
            transfers.Remove(handle);
        }

        private void buttonFeedback_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("http://megadesktop.uservoice.com/forums/191321-general");
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            Process.Start("http://megadesktop.com/");
        }
    }
}
