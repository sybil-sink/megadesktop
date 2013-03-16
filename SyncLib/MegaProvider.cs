using MegaApi;
using Microsoft.Synchronization.SimpleProviders;
using Microsoft.Synchronization.MetadataStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Synchronization;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Globalization;
using Microsoft.Synchronization.Files;

namespace SyncLib
{
    internal static class ItemFields
    {
        public const uint CUSTOM_FIELD_ID = 1;
        public const uint CUSTOM_FIELD_NAME = 2;
    }

    public class MegaProvider : FullEnumerationSimpleSyncProvider
    {
        MegaStore api;
        public event EventHandler<ChangeNotificationArgs> ChangePerformed;
        public event EventHandler<ChangeNotificationArgs> ChangeError;
        public event EventHandler ProgressChanged;

        SqlMetadataStore _metadataStore = null;
        private string _name = "mega";
        private string _replicaMetadataFile = null;
        private string _replicaIdFile = null;

        SyncId _replicaId = null;
        public SyncId ReplicaId
        {
            get
            {
                if (_replicaId == null)
                {
                    _replicaId = GetReplicaIdFromFile(_replicaIdFile);
                }

                return _replicaId;
            }
        }
        
        SyncIdFormatGroup _idFormats = null;
        public override SyncIdFormatGroup IdFormats
        {
            get { return _idFormats; }
        }


        private string _metadataDirectory = "";
        string MetadataDirectory
        {
            get
            {
                if (_metadataDirectory == "")
                {
                    string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                    if (localAppData.Substring(localAppData.Length - 1) != "\\")
                    {
                        localAppData += "\\";
                    }
                    localAppData += "MegaSync\\";
                    _metadataDirectory = localAppData;

                    Directory.CreateDirectory(localAppData);
                }
                return _metadataDirectory;
            }
        }

        void OnProgressChanged()
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, new EventArgs());
            }
        }
        public MegaProvider(Mega api, string syncFolderName)
        {
            this.api = new MegaStore(api, syncFolderName);
            this.api.ProgressChanged += (s, e) => OnProgressChanged();

            _replicaMetadataFile = MetadataDirectory + _name + ".Metadata";
            _replicaIdFile = MetadataDirectory + _name + ".Replicaid";

            _idFormats = new SyncIdFormatGroup();
            _idFormats.ChangeUnitIdFormat.IsVariableLength = false;
            _idFormats.ChangeUnitIdFormat.Length = 4;
            _idFormats.ItemIdFormat.IsVariableLength = false;
            _idFormats.ItemIdFormat.Length = 24;
            _idFormats.ReplicaIdFormat.IsVariableLength = false;
            _idFormats.ReplicaIdFormat.Length = 16;

            this.ItemConstraint += new EventHandler<SimpleSyncItemConstraintEventArgs>(OnItemConstraint);
            this.ItemConflicting += new EventHandler<SimpleSyncItemConflictingEventArgs>(OnItemConflicting);
        }
        public override ItemMetadataSchema MetadataSchema
        {
            get
            {
                CustomFieldDefinition[] customFields = new CustomFieldDefinition[2];
                customFields[0] = new CustomFieldDefinition(ItemFields.CUSTOM_FIELD_ID, typeof(string), MegaStore.IdLength);
                customFields[1] = new CustomFieldDefinition(ItemFields.CUSTOM_FIELD_NAME, typeof(string), MegaStore.nameLength);

                IdentityRule[] identityRule = new IdentityRule[1];
                identityRule[0] = new IdentityRule(new uint[] { ItemFields.CUSTOM_FIELD_ID });

                return new ItemMetadataSchema(customFields, identityRule);
            }
        }
        public override short ProviderVersion
        {
            get
            {
                return 1;
            }
        }
        public override void BeginSession() { }
        private void NotifyChange(string message, params object[] args)
        {
            if (ChangePerformed != null)
            {
                var msg = String.Format(message, args);
                ChangePerformed(this, new ChangeNotificationArgs
                {
                    IsLocal = false,
                    Message = msg,
                    Time = DateTime.Now
                });
            }
        }
        private void NotifyError(Exception e)
        {
            if (ChangeError != null)
            {
                ChangeError(this, new ChangeNotificationArgs
                {
                    IsLocal = false,
                    Message = e.Message,
                    Time = DateTime.Now
                });
            }
        }
        public override void EndSession() { CloseMetadataStore(); }
        
        #region MetadataStore
        public override MetadataStore GetMetadataStore(out SyncId replicaId, out System.Globalization.CultureInfo culture)
        {
            InitializeMetadataStore();

            replicaId = ReplicaId;
            culture = CultureInfo.CurrentCulture;
            return _metadataStore;
        }
        private void InitializeMetadataStore()
        {
            SyncId id = ReplicaId;
            if (!File.Exists(_replicaMetadataFile))
            {
                _metadataStore = SqlMetadataStore.CreateStore(_replicaMetadataFile);
            }
            else
            {
                _metadataStore = SqlMetadataStore.OpenStore(_replicaMetadataFile);
            }
        }
        private void CloseMetadataStore()
        {
            if (_metadataStore != null)
            {
                _metadataStore.Dispose();
            }
            _metadataStore = null;
        }
        internal void CleanupDeletedItems(
            TimeSpan timespan
            )
        {
            InitializeMetadataStore();
            SimpleSyncServices simpleSyncServices = new SimpleSyncServices(_idFormats, _metadataStore, ReplicaId, CultureInfo.CurrentCulture, 0);
            simpleSyncServices.CleanupDeletedItems(timespan);
            CloseMetadataStore();
        }
        #endregion
        #region ReplicaId Initialization Methods
        private static SyncId GetReplicaIdFromFile(
            string replicaIdFile
            )
        {
            SyncId replicaId;

            if (System.IO.File.Exists(replicaIdFile))
            {
                replicaId = ReadReplicaIdFromFile(replicaIdFile);
            }
            else
            {
                replicaId = new SyncId(Guid.NewGuid());
                WriteReplicaIdToFile(replicaIdFile, replicaId);
            }

            return replicaId;
        }


        private static void WriteReplicaIdToFile(
            string file,
            SyncId replicaId
            )
        {
            FileStream fs = new FileStream(file, FileMode.Create);
            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                formatter.Serialize(fs, replicaId);
            }
            catch (SerializationException e)
            {
                Console.WriteLine("Failed to serialize replica id to file. Reason: " + e.Message);
                throw;
            }
            finally
            {
                fs.Close();
            }
        }


        private static SyncId ReadReplicaIdFromFile(
            string file
            )
        {
            FileStream fs = new FileStream(file, FileMode.Open);
            SyncId replicaId;

            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                replicaId = (SyncId)formatter.Deserialize(fs);
            }
            catch (SerializationException e)
            {
                Console.WriteLine("Failed to deserialize replica id from file. Reason: " + e.Message);
                throw;
            }
            finally
            {
                fs.Close();
            }

            return replicaId;
        }
        #endregion ReplicaId Initialization Methods
        private bool IsMoveOrRename(string oldName, string newName) { return oldName != newName; }

        public override void EnumerateItems(FullEnumerationContext context)
        {
            try
            {
                List<ItemFieldDictionary> items = api.ListNodes();
                context.ReportItems(items);
            }
            catch (ApplicationException e)
            {
                NotifyError(e);
                throw;
            }
        }
        public override object LoadChangeData(
            ItemFieldDictionary keyAndExpectedVersion,
            IEnumerable<SyncId> changeUnitsToLoad,
            RecoverableErrorReportingContext recoverableErrorReportingContext
            )
        {
            IDictionary<uint, ItemField> expectedFields = (IDictionary<uint, ItemField>)keyAndExpectedVersion;
            string name = (string)expectedFields[ItemFields.CUSTOM_FIELD_NAME].Value;
            string id = (string)expectedFields[ItemFields.CUSTOM_FIELD_ID].Value;

            object changeData = null;
            try
            {
                NotifyChange("Downloading {0}...", name);
                changeData = api.GetFileRetriever(name, id);
            }
            catch (ApplicationException e)
            {
                recoverableErrorReportingContext.RecordRecoverableErrorForChange(new RecoverableErrorData(e));
                NotifyError(e);
            }
            return changeData;
        }

        public override void DeleteItem(
            ItemFieldDictionary keyAndExpectedVersion,
            RecoverableErrorReportingContext recoverableErrorReportingContext,
            out bool commitKnowledgeAfterThisItem
            )
        {
            commitKnowledgeAfterThisItem = false;

            IDictionary<uint, ItemField> expectedFields = (IDictionary<uint, ItemField>)keyAndExpectedVersion;
            string name = (string)expectedFields[ItemFields.CUSTOM_FIELD_NAME].Value;
            string id = (string)expectedFields[ItemFields.CUSTOM_FIELD_ID].Value;

            try
            {
                NotifyChange("Deleting {0}...", name);
                api.DeleteFile(name, id);
                
            }
            catch (ApplicationException e)
            {
                NotifyError(e);
                recoverableErrorReportingContext.RecordRecoverableErrorForChange(new RecoverableErrorData(e));
            }
        }

        public override void InsertItem(
            object itemData,
            IEnumerable<SyncId> changeUnitsToCreate,
            RecoverableErrorReportingContext recoverableErrorReportingContext,
            out ItemFieldDictionary keyAndUpdatedVersion,
            out bool commitKnowledgeAfterThisItem
            )
        {
            IFileDataRetriever dataRetriever = (IFileDataRetriever)itemData;

            string path = dataRetriever.FileData.RelativePath;

            keyAndUpdatedVersion = null;
            commitKnowledgeAfterThisItem = false;

            try
            {
                NotifyChange("Uploading {0}...", path);
                SyncedNodeAttributes attrs = api.InsertNode(dataRetriever.FileData, path, 
                    dataRetriever.FileData.IsDirectory ? null : dataRetriever.FileStream);
                keyAndUpdatedVersion = new ItemFieldDictionary();
                keyAndUpdatedVersion.Add(new ItemField(ItemFields.CUSTOM_FIELD_NAME, typeof(string), attrs.Name));
                keyAndUpdatedVersion.Add(new ItemField(ItemFields.CUSTOM_FIELD_ID, typeof(string), attrs.Id));
            }
            catch (ApplicationException e)
            {
                NotifyError(e);
                recoverableErrorReportingContext.RecordRecoverableErrorForChange(new RecoverableErrorData(e));
            }
        }

        public override void UpdateItem(
            object itemData,
            IEnumerable<SyncId> changeUnitsToUpdate,
            ItemFieldDictionary keyAndExpectedVersion,
            RecoverableErrorReportingContext recoverableErrorReportingContext,
            out ItemFieldDictionary keyAndUpdatedVersion,
            out bool commitKnowledgeAfterThisItem
            )
        {
            keyAndUpdatedVersion = null;
            commitKnowledgeAfterThisItem = false;
            IFileDataRetriever dataRetriever = (IFileDataRetriever)itemData;

            IDictionary<uint, ItemField> expectedFields = (IDictionary<uint, ItemField>)keyAndExpectedVersion;
            string path = (string)expectedFields[ItemFields.CUSTOM_FIELD_NAME].Value;
            string id = (string)expectedFields[ItemFields.CUSTOM_FIELD_ID].Value;
            
            try
            {
                SyncedNodeAttributes attrs;
                if (IsMoveOrRename(path, dataRetriever.FileData.RelativePath))
                {
                    NotifyChange("Moving {0} to {1}...", path, dataRetriever.FileData.RelativePath);
                    attrs = api.MoveFile(path, dataRetriever.FileData.RelativePath, id);
                }
                else
                {
                    NotifyChange("Updating {0}...", path);
                    attrs = api.UpdateFile(path, dataRetriever.FileData, 
                        dataRetriever.FileData.IsDirectory ? null : dataRetriever.FileStream, id);
                }

                keyAndUpdatedVersion = new ItemFieldDictionary();
                keyAndUpdatedVersion.Add(new ItemField(ItemFields.CUSTOM_FIELD_NAME, typeof(string), attrs.Name));
                keyAndUpdatedVersion.Add(new ItemField(ItemFields.CUSTOM_FIELD_ID, typeof(string), attrs.Id));
            }
            catch (ApplicationException e)
            {
                recoverableErrorReportingContext.RecordRecoverableErrorForChange(new RecoverableErrorData(e));
                NotifyError(e);
            }
            commitKnowledgeAfterThisItem = false;
        }

        void OnItemConstraint(object sender, SimpleSyncItemConstraintEventArgs e)
        {
            e.SetResolutionAction(ConstraintConflictResolutionAction.SourceWins);
        }

        void OnItemConflicting(object sender, SimpleSyncItemConflictingEventArgs e)
        {
            NotifyChange("Conflict found");
            switch (e.ConflictKind)
            {
                case ConcurrencyConflictKind.LocalDeleteRemoteUpdate:
                    var data = (IFileDataRetriever)e.RemoteItemData;
                    if (!data.FileData.IsDirectory)
                    {
                        try
                        {
                            NotifyChange("Creating backup...");
                            api.BackupFile(data.FileData.RelativePath);
                        }
                        catch (ApplicationException ex)
                        {
                            NotifyError(ex);
                        }
                    }
                    e.SetResolutionAction(ConflictResolutionAction.SourceWins);
                    NotifyChange("Remote won");
                    break;
                case ConcurrencyConflictKind.LocalUpdateRemoteDelete:
                    e.SetResolutionAction(ConflictResolutionAction.DestinationWins);
                    NotifyChange("Local won");
                    break;
                case ConcurrencyConflictKind.UpdateUpdate:
                    IDictionary<uint, ItemField> expectedFields = (IDictionary<uint, ItemField>)e.LocalItem;
                    string name = (string)expectedFields[ItemFields.CUSTOM_FIELD_NAME].Value;
                    try
                    {
                        NotifyChange("Creating backup...");
                        api.BackupFile(name);
                    }
                    catch (ApplicationException ex)
                    {
                        NotifyError(ex);
                    }
                    e.SetResolutionAction(ConflictResolutionAction.SourceWins);
                    NotifyChange("Remote won");
                    break;
            }

        }

        internal void ResetState()
        {
            CloseMetadataStore();
            File.Delete(_replicaMetadataFile);
            File.Delete(_replicaIdFile);
        }
    }
}
