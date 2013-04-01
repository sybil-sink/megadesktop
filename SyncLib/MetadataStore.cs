using Microsoft.Synchronization;
using Microsoft.Synchronization.MetadataStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MegaStore;
using MegaApi;

namespace SyncLib
{
    internal class MetadataStore
    {

        public SyncId ReplicaId { get; private set; }
        public ReplicaMetadata Metadata
        {
            get
            {
                if (_metadata == null) { InitializeMetadataStore(); }
                return _metadata;
            }
        }

        const int idLength = 10;
        const int pathLength = 512;
        const string ID_COLUMNNAME = "id";
        const string PATH_COLUMNNAME = "path";

        SqlMetadataStore _metadataStore = null;
        ReplicaMetadata _metadata = null;

        SyncIdFormatGroup _idFormats = null;

        string _replicaMetadataFile = null;


        public MetadataStore(SyncIdFormatGroup idFormats)
        {
            var replicaMetadataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaDesktop");
            _replicaMetadataFile = Path.Combine(replicaMetadataFolder, "nodes.metadata");
            _idFormats = idFormats;
        }

        

        public void InitializeMetadataStore()
        {
            List<FieldSchema> fields = new List<FieldSchema>();

            if (!File.Exists(_replicaMetadataFile))
            {
                fields.Add(new FieldSchema(ID_COLUMNNAME, typeof(string), idLength));
                fields.Add(new FieldSchema(PATH_COLUMNNAME, typeof(string), pathLength));
                _metadataStore = SqlMetadataStore.CreateStore(_replicaMetadataFile);

                var indexes = new List<IndexSchema>{
                    new IndexSchema(ID_COLUMNNAME, false)
                };
                _metadata = _metadataStore.InitializeReplicaMetadata(_idFormats, new SyncId(Guid.NewGuid()), fields, indexes);
            }
            else
            {
                _metadataStore = SqlMetadataStore.OpenStore(_replicaMetadataFile);
                _metadata = _metadataStore.GetSingleReplicaMetadata();
            }
            ReplicaId = _metadata.ReplicaId;
        }

        void CloseMetadataStore()
        {
            if (_metadataStore == null) { return; }
            _metadataStore.Dispose();
            _metadataStore = null;
        }

        void UpdateMetadataStoreWithLocalChanges(IEnumerable<MegaNodeHelper> nodes)
        {
            SyncVersion newVersion = new SyncVersion(0, _metadata.GetNextTickCount());

            _metadata.DeleteDetector.MarkAllItemsUnreported();
            foreach (var node in nodes)
            {
                var item = FindItemMetadataByNodeId(node.Node.Id);
                if (item == null)
                {
                    item = CreateItemMetadata(new SyncId(new SyncGlobalId(0, Guid.NewGuid())), newVersion);
                    item.ChangeVersion = newVersion;
                    SaveItemMetadata(item, node.Node.Id, node.Path);
                }
                else
                {
                    if (node.Path != item.GetStringField(PATH_COLUMNNAME)) 
                    {
                        if (node.Node.Type == MegaNodeType.Folder)
                        {
                            item.MarkAsDeleted(newVersion);
                            item.ChangeVersion = newVersion;
                            SaveItemMetadata(item);
                            var newItem = CreateItemMetadata(new SyncId(new SyncGlobalId(0, Guid.NewGuid())), newVersion);
                            newItem.ChangeVersion = newVersion;
                            SaveItemMetadata(newItem, node.Node.Id, node.Path);
                        }
                        else
                        {
                            //Changed Item, this item has changed since the last time the metadata was updated.
                            item.ChangeVersion = newVersion;
                            SaveItemMetadata(item, node.Node.Id, node.Path);
                        }
                    }
                    else
                    {
                        //Unchanged item, nothing has changes so just mark it as live so that the metadata knows it has not been deleted.
                        _metadata.DeleteDetector.ReportLiveItemById(item.GlobalId);
                    }
                }
            }

            foreach (ItemMetadata item in _metadata.DeleteDetector.FindUnreportedItems())
            {
                item.MarkAsDeleted(newVersion);
                SaveItemMetadata(item);
            }
        }

        internal ItemMetadata FindItemMetadataByNodeId(string id)
        {
            return _metadata.FindItemMetadataByIndexedField(ID_COLUMNNAME, id).FirstOrDefault();
        }

        public void SaveItemMetadata(ItemMetadata item, string id, string path)
        {
            item.SetCustomField(ID_COLUMNNAME, id);
            item.SetCustomField(PATH_COLUMNNAME, path);
            SaveItemMetadata(item);
        }

        public void SaveItemMetadata(ItemMetadata item)
        {
            _metadata.SaveItemMetadata(item);
        }

        public void CleanupTombstones(TimeSpan timespan)
        {
            InitializeMetadataStore();
            _metadataStore.BeginTransaction();
            _metadata.CleanupDeletedItems(timespan);
            _metadataStore.CommitTransaction();
            CloseMetadataStore();
        }

        public void BeginSession(List<MegaNodeHelper> list)
        {
            InitializeMetadataStore();
            _metadataStore.BeginTransaction();
            UpdateMetadataStoreWithLocalChanges(list);
            _metadataStore.CommitTransaction();
        }

        public void EndSession()
        {
            CloseMetadataStore();
        }

        public void BeginTransaction()
        {
            _metadataStore.BeginTransaction();
        }

        public void CommitTransaction()
        {
            _metadataStore.CommitTransaction();
        }

        public SyncedNodeAttributes FindItemMetadataBySyncId(SyncId syncId)
        {
            var item = _metadata.FindItemMetadataById(syncId);
            return GetItemInfo(item);
        }

        internal ItemMetadata CreateItemMetadata(SyncId syncId = null, SyncVersion newVersion = null)
        {
            var setChangeVersion = false;
            if (newVersion == null || syncId == null)
            {
                newVersion = new SyncVersion(0, _metadata.GetNextTickCount());
                syncId = new SyncId(new SyncGlobalId(0, Guid.NewGuid()));
                setChangeVersion = true;
            }
            var result = _metadata.CreateItemMetadata(syncId, newVersion);
            if (setChangeVersion) { result.ChangeVersion = newVersion; }
            return result;
        }

        internal SyncedNodeAttributes GetItemInfo(ItemMetadata item)
        {
            if (item == null) { return null; }
            return new SyncedNodeAttributes
            {
                Path = item.GetStringField(PATH_COLUMNNAME),
                Id = item.GetStringField(ID_COLUMNNAME)
            };
        }

        internal void Reset()
        {
            CloseMetadataStore();
            if (File.Exists(_replicaMetadataFile))
            {
                File.Delete(_replicaMetadataFile);
            }
        }
    }
}
