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
using MegaStore;

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
        

        public event EventHandler<ChangeNotificationArgs> ChangePerformed;
        public event EventHandler ProgressChanged;
        public event EventHandler SyncEnded;
        public event EventHandler SyncStarted;
        public event EventHandler<SyncErrorArgs> SyncError;

        string localFolder;
        string metadataFile;
        NodeStore remoteStore;
        volatile bool isSyncing;
        System.Timers.Timer syncTimer;
        FileSystemWatcher fsWatcher;

        
        public Sync(string localFolder, Mega api, string remoteFolderName)
        {
            this.localFolder = localFolder;
            var metadataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaDesktop");
            Directory.CreateDirectory(metadataFolder);
            metadataFile = Path.Combine(metadataFolder, "files.metadata");

            remoteStore = new NodeStore(api, "sync");

            InitTimer();
            InitWatcher(localFolder);
        }

        protected void InitTimer()
        {
            syncTimer = new System.Timers.Timer
            {
                AutoReset = false,
                Interval = 1500                
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
            fsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            fsWatcher.IncludeSubdirectories = true;
        }
        private void WatcherHandler(object sender, FileSystemEventArgs e)
        {
            if (SkipChange(e)) { return; }
            syncTimer.Stop();
            syncTimer.Start();
        }

        // do not fire on hidden files/folders, empty files
        // but always on delete
        bool SkipChange(FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted) 
            {
                return false;
            }
            var path = e.FullPath;
            var fi = new FileInfo(path);
            var attr = fi.Attributes;

            if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden) { return true; }
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
            {
                return false;
                // disables the file\folder creation so turned off
                //return (e.ChangeType == WatcherChangeTypes.Changed);
            }
            else
            {
                return fi.Length == 0;
            }

        }
        
        
        void OnProgressChanged()
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, new EventArgs());
            }
        }
        private void OnChangePerformed(string p, bool isLocal)
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

        object startSyncLock = new object();
        volatile bool changesWhileSync = false;
        private void Synchronize(bool startFromScratch = false)
        {
            lock (startSyncLock)
            {
                if (isSyncing) { changesWhileSync = true; return; }
                isSyncing = true;
                changesWhileSync = false;
            }
            if (SyncStarted != null) { SyncStarted(this, null); }

            if (startFromScratch)
            {
                if (File.Exists(metadataFile))
                {
                    File.Delete(metadataFile);
                }
            }
            var fileProvider = new FileSyncProvider(
                localFolder,
                new FileSyncScopeFilter(),
                FileSyncOptions.RecycleDeletedFiles | FileSyncOptions.RecycleConflictLoserFiles,
                Path.GetDirectoryName(metadataFile),
                Path.GetFileName(metadataFile),
                Path.GetTempPath(),
                null
                );
            fileProvider.AppliedChange += (s, e) => AppliedChange(s, e.ChangeType, e.OldFilePath, e.NewFilePath);
            fileProvider.SkippedChange += (s, e) => { var t = e; };

            var remote = new MegaKnowledgeProvider(remoteStore);
            if (startFromScratch)
            {
                remote.ResetDatabase();
            }

            remote.AppliedChange += (s, e) => AppliedChange(s, e.ChangeType, e.OldFilePath, e.NewFilePath);
            // do we need this?
            remote.DestinationCallbacks.ItemConstraint += (s, e) => 
            { 
                e.SetResolutionAction(ConstraintConflictResolutionAction.SkipChange);
            };
            remote.DestinationCallbacks.ItemConflicting += (s, e) => { e.SetResolutionAction(ConflictResolutionAction.Merge); };

            try
            {
                var agent = new SyncOrchestrator();
                agent.RemoteProvider = remote;
                agent.Direction = SyncDirectionOrder.UploadAndDownload;
                agent.LocalProvider = fileProvider;
                var status = agent.Synchronize();
                remoteStore.CleanTemp();
                if (remote.NeedResync)
                {
                    syncTimer.Stop();
                    syncTimer.Start();
                }
            }
            catch (Exception e)
            {
                remote.ResetDatabase();
                if (File.Exists(metadataFile))
                {
                    File.Delete(metadataFile);
                }
                OnError("Sync has encountered a severe problem. Trying to resync from scratch...", e);
                syncTimer.Stop();
                syncTimer.Start();
            }
            finally
            {
                fileProvider.Dispose();
                remote = null;
                lock (startSyncLock)
                {
                    if (changesWhileSync)
                    {
                        syncTimer.Stop();
                        syncTimer.Start();
                    }
                    isSyncing = false;
                }
                if (SyncEnded != null) { SyncEnded(this, null); }
            }

        }
        void AppliedChange(object sender, ChangeType type, string oldPath, string newPath)
        {
            var isLocal = sender.GetType() == typeof(FileSyncProvider);
            switch (type)
            {
                case ChangeType.Create:
                    OnChangePerformed(String.Format("Created {0}", newPath), isLocal);
                    break;
                case ChangeType.Delete:
                    OnChangePerformed(String.Format("Deleted {0}", oldPath), isLocal);
                    break;
                case ChangeType.Rename:
                    OnChangePerformed(String.Format("Renamed {0} to {1}", oldPath, newPath), isLocal);
                    break;
                case ChangeType.Update:
                    OnChangePerformed(String.Format("Updated {0}", newPath), isLocal);
                    break;
            }

        }

        public void StartSyncing(bool startFromScratch = false)
        {
            remoteStore.Updated += (s, e) => { syncTimer.Stop(); syncTimer.Start(); };
            fsWatcher.EnableRaisingEvents = true;
            remoteStore.EnableRaisingEvents = true;
            Util.StartThread(() =>
            {
                try
                {
                    Synchronize(startFromScratch);
                }
                catch (System.Runtime.InteropServices.COMException e)
                {
                    OnError("Please reinstall Mega Desktop - The required Sync Framework libraries cannot be found.", e);
                }
                catch (FileNotFoundException e)
                {
                    OnError("Please reinstall Mega Desktop - The required Sync Framework libraries cannot be found.", e);
                }
                catch (Exception e)
                {
                    OnError("Internal sync error", e);
                }
            }, "mega_sync_start");
        }
    }
}
