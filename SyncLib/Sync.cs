using MegaApi;
using MegaApi.Utility;
using Microsoft.Synchronization;
using Microsoft.Synchronization.Files;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SyncLib
{
    public class ChangeNotificationArgs : EventArgs
    {
        public string Message { get; set; }
        public DateTime Time { get; set; }
        public bool IsLocal { get; set; }
    }

    public class SyncErrorArgs : EventArgs
    {
        public string Message { get; set; }
        public Exception Exception { get; set; }
    }

    public class Sync
    {
        string localFolder;
        string remoteFolder;
        Mega api;

        public event EventHandler<ChangeNotificationArgs> ChangePerformed;
        public event EventHandler ProgressChanged;
        public event EventHandler SyncEnded;
        public event EventHandler SyncStarted;
        public event EventHandler<SyncErrorArgs> SyncError;
        volatile bool isSyncing;
        System.Timers.Timer syncTimer;
        FileSystemWatcher fsWatcher;
        MegaProvider remote;
        string metadataFile;
        void OnProgressChanged()
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, new EventArgs());
            }
        }
        public Sync(string localFolder, string metadataFile, Mega api, string remoteFolderName)
        {
            this.metadataFile = metadataFile;
            this.localFolder = localFolder;
            remoteFolder = remoteFolderName;
            this.api = api;

            InitTimer();
            InitWatcher(localFolder);
        }

        protected void InitTimer()
        {
            syncTimer = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = 1500,
            };
            syncTimer.Elapsed += (s, e) => Synchronize();
        }

        private void InitWatcher(string rootFolder)
        {
            fsWatcher = new FileSystemWatcher(rootFolder);
            fsWatcher.Changed += WatcherHandler;
            fsWatcher.Renamed += WatcherHandler;
            fsWatcher.Created += WatcherHandler;
            fsWatcher.Deleted += WatcherHandler;
            fsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
            fsWatcher.IncludeSubdirectories = true;
        }
        private void WatcherHandler(object sender, FileSystemEventArgs e)
        {
            if (isHidden(e.FullPath)) { return; }
            syncTimer.Stop();
            syncTimer.Start();
        }

        bool isHidden(string path)
        {
            var attr = new FileInfo(path).Attributes;
            return (attr & FileAttributes.Hidden) == FileAttributes.Hidden;
        }
        void local_AppliedChange(object sender, AppliedChangeEventArgs args)
        {
            var isLocal = sender.GetType() == typeof(FileSyncProvider);
            switch (args.ChangeType)
            {
                case ChangeType.Create:
                    Notify(String.Format("Created File: {0}...", args.NewFilePath), isLocal);
                    break;
                case ChangeType.Delete:
                    Notify(String.Format("Deleted File: {0}...", args.OldFilePath), isLocal);
                    break;
                case ChangeType.Rename:
                    Notify(String.Format("Renamed File: {0} to {1}...", args.OldFilePath, args.NewFilePath), isLocal);
                    break;
                case ChangeType.Update:
                    Notify(String.Format("Updated File: {0}...", args.NewFilePath), isLocal);
                    break;
            }

        }

        private void Notify(string p, bool isLocal)
        {
            if (ChangePerformed != null)
            {
                ChangePerformed(this, new ChangeNotificationArgs
                {
                    Message = p,
                    Time = DateTime.Now,
                    IsLocal = isLocal
                });
            }
        }
        void OnError(String message, Exception e)
        {
            if (SyncError != null)
            {
                SyncError(this, new SyncErrorArgs { Exception = e, Message = message });
            }
        }
        private void Synchronize()
        {
            if (isSyncing) { return; }
            isSyncing = true;
            if (SyncStarted != null) { SyncStarted(this, null); }
            FileSyncProvider fileProvider = null;
            try
            {
                fileProvider = new FileSyncProvider(
                    localFolder,
                    new FileSyncScopeFilter(), 
                    FileSyncOptions.RecycleDeletedFiles| FileSyncOptions.RecycleConflictLoserFiles,
                    Path.GetDirectoryName(metadataFile),
                    Path.GetFileName(metadataFile),
                    Path.GetTempPath(), 
                    null
                    );
            }
            catch (Exception e)
            {
                OnError("Could not initialize the FileSyncProvider. Check the sync framework install", e);
                return;
            }
            fileProvider.AppliedChange += local_AppliedChange;
            if (remote == null)
            {
                try
                {
                    remote = new MegaProvider(api, remoteFolder);
                    remote.ChangePerformed += (s, e) => Notify(e.Message, false);
                    remote.ChangeError += (s, e) => Notify("Error: " + e.Message, false);
                    remote.ProgressChanged += (s, e) => OnProgressChanged();
                }
                catch (Exception e)
                {
                    OnError("Could not initialize the MegaProvider. Check the sync framework install", e);
                    return;
                }
            }
            try
            {
                var agent = new SyncOrchestrator();
                agent.RemoteProvider = remote;
                agent.Direction = SyncDirectionOrder.UploadAndDownload;
                agent.LocalProvider = fileProvider;
                agent.Synchronize();
            }
            catch (Exception e)
            {
                OnError("Synchronization error", e);
                remote.ResetState();
            }
            finally
            {
                isSyncing = false;
                fileProvider.Dispose();
                if (SyncEnded != null) { SyncEnded(this, null); }
            }

        }

        public void StartSyncing()
        {
            api.ServerRequest += (s, e) => { syncTimer.Stop(); syncTimer.Start(); };
            fsWatcher.EnableRaisingEvents = true;
            Util.StartThread(() =>
            {
                try
                {
                    Synchronize();
                }
                catch (FileNotFoundException e)
                {
                    OnError("Please reinstall Mega Desktop - The required Sync Framework libraries cannot be found.", e);
                }
                catch (Exception e)
                {
                    OnError("Internal sync error", e);
                }
            }, "mega_sync");
        }
    }
}
