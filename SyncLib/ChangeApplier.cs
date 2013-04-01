using MegaApi;
using Microsoft.Synchronization;
using Microsoft.Synchronization.Files;
using Microsoft.Synchronization.MetadataStorage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaStore;
using System.IO;
using System.Runtime.InteropServices;

namespace SyncLib
{
    internal class ChangeApplier : INotifyingChangeApplierTarget2, IChangeDataRetriever
    {
        public event EventHandler<MyAppliedChangeEventArgs> AppliedChange;
        public event EventHandler DemandedResync;
        public bool AssumeSameFiles { get; set; }
        public const long FileComparisionTreshold = 5 * 1024 * 1024;

        MetadataStore _metadataStore = null;
        SyncIdFormatGroup _idFormats = null;
        NodeStore _nodeStore = null;
        
        void OnAppliedChange(ChangeType type, string newPath, string oldPath = null)
        {
            if (AppliedChange != null)
            {
                AppliedChange(this, new MyAppliedChangeEventArgs(type, newPath, oldPath));
            }
        }
        void OnDemandedResync()
        {
            if (DemandedResync != null)
            {
                DemandedResync(this, null);
            }
        }

        //Save the item, taking the appropriate action for the 'change' and the data from the item (in 'context')
        public void SaveItemChange(SaveChangeAction saveChangeAction, ItemChange change, SaveChangeContext context)
        {
            ulong timeStamp = 0;
            ItemMetadata item = null;
            switch (saveChangeAction)
            {
                case SaveChangeAction.Create:
                    item = _metadataStore.Metadata.FindItemMetadataById(change.ItemId);
                    if (item != null)
                    {
                        // doubtful solution
                        _metadataStore.Metadata.RemoveItemMetadata(new List<SyncId> { item.GlobalId });
                        item = null;
                    }
                    CreateItem(change, context);
                    break;
                case SaveChangeAction.UpdateVersionAndData:
                case SaveChangeAction.UpdateVersionOnly:
                    item = _metadataStore.Metadata.FindItemMetadataById(change.ItemId);
                    if (item == null)
                    {
                        CreateItem(change, context);
                        break;
                    }
                    if (saveChangeAction == SaveChangeAction.UpdateVersionOnly)
                    {
                        item.ChangeVersion = change.ChangeVersion;
                        _metadataStore.SaveItemMetadata(item);
                    }
                    else { UpdateItem(item, change, context); }
                    break;
                case SaveChangeAction.DeleteAndStoreTombstone:
                    item = _metadataStore.Metadata.FindItemMetadataById(change.ItemId);
                    // didn't know about this item
                    if (item == null)
                    {
                        item = _metadataStore.Metadata.CreateItemMetadata(change.ItemId, change.CreationVersion);
                        item.MarkAsDeleted(change.ChangeVersion);
                        item.ChangeVersion = change.ChangeVersion;
                        _metadataStore.SaveItemMetadata(item);
                    }
                    else { DeleteItem(item, change, context); }
                    break;
                case SaveChangeAction.UpdateVersionAndMergeData:
                    item = _metadataStore.Metadata.FindItemMetadataById(change.ItemId);
                    ResolveConflict(item, change, context, null);
                    break;
                case SaveChangeAction.DeleteAndRemoveTombstone:
                    item = _metadataStore.Metadata.FindItemMetadataById(change.ItemId);
                    if (item != null)
                    {
                        DeleteItem(item, change, context, true);
                    }
                    break;
            }
        }


        /// <summary>
        /// CREATE
        /// </summary>
        private void CreateItem(ItemChange change, SaveChangeContext context, ItemMetadata itemToSaveChanges = null)
        {
            var data = (IFileDataRetriever)context.ChangeData;
            Stream stream = null;
            try
            {
                stream = data.FileData.IsDirectory ? null : data.FileStream;
                var createdNode = _nodeStore.InsertNode(data.FileData, data.FileData.RelativePath, stream);
                if (itemToSaveChanges == null)
                {
                    itemToSaveChanges = _metadataStore.CreateItemMetadata(change.ItemId, change.CreationVersion);
                }
                itemToSaveChanges.ChangeVersion = change.ChangeVersion;

                _metadataStore.SaveItemMetadata(itemToSaveChanges, createdNode.Id, createdNode.Path);
                OnAppliedChange(ChangeType.Create, data.FileData.RelativePath);
            }
            catch (MegaStoreConstraintException e)
            {
                ProcessConstraint(change, context, e, null, e.Node);
            }
            catch (MegaApiException e)
            {
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            catch (DirectoryNotFoundException e)
            {
                OnDemandedResync();
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            catch (COMException e)
            {
                OnDemandedResync();
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            finally
            {
                CloseStream(stream);
            }
        }

        /// <summary>
        /// READ
        /// </summary>
        public object LoadChangeData(LoadChangeContext loadChangeContext)
        {
            var item = _metadataStore.FindItemMetadataBySyncId(loadChangeContext.ItemChange.ItemId);
            try
            {
                return _nodeStore.GetFileRetriever(item.Path, item.Id);
            }
            catch (MegaApiException e)
            {
                OnDemandedResync();
                loadChangeContext.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            catch (IOException e)
            {
                OnDemandedResync();
                loadChangeContext.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            return null;
        }

        /// <summary>
        /// UPDATE
        /// </summary>
        /// 
        private void UpdateItem(ItemMetadata item, ItemChange change, SaveChangeContext context)
        {
            var data = (IFileDataRetriever)context.ChangeData;
            var attr = _metadataStore.GetItemInfo(item);
            var stream = data.FileData.IsDirectory ? null : data.FileStream;
            try
            {
                SyncedNodeAttributes updatedNode = null;
                //if pathes are different then consider renaming with unchanged content
                if (attr.Path != data.FileData.RelativePath)
                {
                    updatedNode = _nodeStore.MoveFile(attr.Path, data.FileData.RelativePath, attr.Id);
                    OnAppliedChange(ChangeType.Rename, data.FileData.RelativePath, attr.Path);
                }
                else
                {
                    updatedNode = _nodeStore.UpdateFile(attr.Path, data.FileData, stream, attr.Id);
                    OnAppliedChange(ChangeType.Update, data.FileData.RelativePath);
                }

                item.ChangeVersion = change.ChangeVersion;
                _metadataStore.SaveItemMetadata(item, updatedNode.Id, updatedNode.Path);
            }
            catch (MegaStoreConstraintException e)
            {
                ProcessConstraint(change, context, e, item, e.Node);
            }
            catch (MegaStoreException e)
            {
                ForgetItem(context, item, e);
            }
            catch (MegaApiException e)
            {
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
                OnDemandedResync();
            }
            catch (DirectoryNotFoundException e)
            {
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
                OnDemandedResync();
            }

            catch (COMException e)
            {
                OnDemandedResync();
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
            finally
            {
                CloseStream(stream);
            }
        }

        /// <summary>
        /// DELETE
        /// </summary>
        private void DeleteItem(ItemMetadata item, ItemChange change, SaveChangeContext context, bool removeTombstone = false)
        {
            try
            {
                var attr = _metadataStore.GetItemInfo(item);
                _nodeStore.DeleteFile(attr.Path, attr.Id);
                OnAppliedChange(ChangeType.Delete, null, attr.Path);
                if (removeTombstone)
                {
                    _metadataStore.Metadata.RemoveItemMetadata(new List<SyncId> { item.GlobalId });
                }
                else
                {
                    item.MarkAsDeleted(change.ChangeVersion);
                    _metadataStore.SaveItemMetadata(item);
                }
            }
            catch (MegaStoreConstraintException e)
            {
                ProcessConstraint(change, context, e, item, null);
            }
            // never mind, the item isn't found anyway
            catch (MegaStoreException)
            {
                if (removeTombstone)
                {
                    _metadataStore.Metadata.RemoveItemMetadata(new List<SyncId> { item.GlobalId });
                }
                else
                {
                    item.MarkAsDeleted(change.ChangeVersion);
                    _metadataStore.SaveItemMetadata(item);
                }
            }
            catch (MegaApiException e)
            {
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
                OnDemandedResync();
            }
        }
        /// <summary>
        /// CONFLICT
        /// </summary>
        private void ResolveConflict(ItemMetadata item, ItemChange change, SaveChangeContext context, MegaNodeHelper conflictingNode)
        {
            var data = (IFileDataRetriever)context.ChangeData;
            // item does not exist in metadata (can happen when create+create)
            if (item == null) 
            {
                if (conflictingNode == null) 
                {
                    context.RecordRecoverableErrorForItem(new RecoverableErrorData(new Exception()));
                }
                if (FilesAreEqual(data, conflictingNode))
                {
                    DoNothing(item, change, context, false, conflictingNode);
                }
                else
                {
                    BackupFile(item, conflictingNode.Path, change, context);
                }
                return;
            }
            var attr = _metadataStore.GetItemInfo(item);
            try
            {
                if (change.ChangeKind == ChangeKind.Deleted)
                {
                    // local delete + remote delete
                    if (item.IsDeleted)
                    {
                        DoNothing(item, change, context, true);
                    }
                    // local delete + remote update
                    else
                    {
                        DownloadBack(item, change);
                    }
                }
                else
                {
                    // local update + remote delete
                    if (item.IsDeleted)
                    {
                        UploadBack(item, change, context);
                    }
                    // update + update
                    else
                    {
                        if (FilesAreEqual(data, _nodeStore.FindNodeById(attr.Id)))
                        {
                            DoNothing(item, change, context);
                        }
                        else
                        {
                            BackupFile(item, attr.Path, change, context);
                        }
                    }
                }
            }
            catch (MegaApiException e)
            {
                context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
            }
        }
        #region conflict handling
        private void BackupFile(ItemMetadata item, string path, ItemChange change, SaveChangeContext context)
        {
            var t = _nodeStore.BackupFile(path);
            var newItem = _metadataStore.CreateItemMetadata();
            _metadataStore.SaveItemMetadata(newItem, t.Node.Id, t.Path);

            if (item == null)
            {
                CreateItem(change, context);
            }
            else
            {
                UpdateItem(item, change, context);
            }
        }
        private void DownloadBack(ItemMetadata item, ItemChange change)
        {
            item.ChangeVersion = new SyncVersion(change.ChangeVersion.ReplicaKey, GetNextTickCount());
            _metadataStore.SaveItemMetadata(item);
        }

        private void UploadBack(ItemMetadata item, ItemChange change, SaveChangeContext context)
        {
            //TODO this still re-downloads the file just uploaded
            item.ResurrectDeletedItem();
            CreateItem(change, context, item);
        }

        private void DoNothing(
            ItemMetadata item, 
            ItemChange change,
            SaveChangeContext context,
            bool deleted = false,
            MegaNodeHelper existingNode = null)
        {
            // creating
            if (item == null)
            {
                if (existingNode == null)
                {
                    context.RecordRecoverableErrorForItem(new RecoverableErrorData(new Exception()));
                }
                item = _metadataStore.Metadata.CreateItemMetadata(change.ItemId, change.CreationVersion);
                item.ChangeVersion = change.ChangeVersion;
                _metadataStore.SaveItemMetadata(item, existingNode.Node.Id, existingNode.Path);
                return;
            }
            if (deleted)
            {
                item.MarkAsDeleted(change.ChangeVersion);
            }
            item.ChangeVersion = change.ChangeVersion;
            _metadataStore.SaveItemMetadata(item);
        }

        private bool FilesAreEqual(IFileDataRetriever data, MegaNodeHelper node)
        {
            if (data.FileData.RelativePath != node.Path) { return false; }
            if (data.FileData.IsDirectory) { return true; }
            if (data.FileData.Size != node.Node.Size) { return false; }
            if (data.FileData.Size < FileComparisionTreshold
            || node.Node.Size < FileComparisionTreshold)
            {
                // TODO compare streams
            }
            return true;
        }
        #endregion

        #region utility
        private static void CloseStream(System.IO.Stream stream)
        {
            if (stream != null) { stream.Close(); }
        }

        private void ProcessConstraint(
            ItemChange change,
            SaveChangeContext context,
            MegaStoreConstraintException e,
            ItemMetadata item,
            MegaNodeHelper conflictingNode)
        {
            switch (e.ConstraintType)
            {
                case MegaStoreConstraintType.TargetExists:
                    ResolveConflict(item, change, context, conflictingNode);
                    break;

                case MegaStoreConstraintType.NoParent:
                    OnDemandedResync();
                    context.RecordConstraintConflictForItem(ConstraintConflictReason.NoParent);
                    break;
                // todo add no-free-space handling
                case MegaStoreConstraintType.ZeroSize:
                    context.RecordConstraintConflictForItem(ConstraintConflictReason.Other);
                    break;

                case MegaStoreConstraintType.NotEmpty:
                    OnDemandedResync();
                    context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
                    break;
            }
        }

        // the node is suddenly out, remove it from database 
        // (it was a concurrency delete attempt - we should restore the existing version)
        private void ForgetItem(SaveChangeContext context, ItemMetadata item, Exception e)
        {
            _metadataStore.Metadata.RemoveItemMetadata(new List<SyncId> { item.GlobalId });
            OnDemandedResync();
            context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
        }

        //private void RecordCollisionConflict(SaveChangeContext context, MegaStoreConstraintException e)
        //{
        //    var existingItem = e.Node == null ? null :
        //        _metadataStore.FindItemMetadataByNodeId(e.Node.Node.Id);
        //    if (existingItem != null)
        //    {
        //        // ConstraintConflictReason.Collision does not fire the collision resolver!!!11
        //        context.RecordConstraintConflictForItem(existingItem.GlobalId, ConstraintConflictReason.Other);
        //    }
        //    else
        //    {
        //        context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
        //    }
        //}
        //private void RecordConcurrencyConflict(SaveChangeContext context, MegaStoreConcurrencyException e)
        //{
        //    var existingItem = e.Node == null ? null :
        //        _metadataStore.FindItemMetadataByNodeId(e.Node.Node.Id);
        //    // try to handle in a regular way
        //    if (existingItem != null)
        //    {
        //        context.RecordConstraintConflictForItem(existingItem.GlobalId, ConstraintConflictReason.Other);
        //    }
        //    // we are doomed!
        //    else
        //    {
        //        context.RecordRecoverableErrorForItem(new RecoverableErrorData(e));
        //    }

        //}
        #endregion

        #region default implementation of interfaces
        public ChangeApplier(MetadataStore metadataStore, SyncIdFormatGroup idFormats, NodeStore nodeStore)
        {
            _metadataStore = metadataStore;
            _idFormats = idFormats;
            _nodeStore = nodeStore;
            AssumeSameFiles = true;
        }
        public void SaveChangeWithChangeUnits(ItemChange change, SaveChangeWithChangeUnitsContext context)
        {
            //Change units are not supported by this sample provider.  
            throw new NotImplementedException();
        }
        public void SaveConflict(ItemChange conflictingChange, object conflictingChangeData, SyncKnowledge conflictingChangeKnowledge)
        {
            //Conflicts are always merged so there is never a conflict to save.  See DestinationCallbacks_ItemConflicting in MyTestProgram 
            //and the SaveChangeAction.UpdateVersionAndMergeData case in SaveChange below to see how conflicts are merged.
            throw new NotImplementedException();
        }
        public void StoreKnowledgeForScope(SyncKnowledge knowledge, ForgottenKnowledge forgottenKnowledge)
        {
            _metadataStore.Metadata.SetKnowledge(knowledge);
            _metadataStore.Metadata.SetForgottenKnowledge(forgottenKnowledge);
            _metadataStore.Metadata.SaveReplicaMetadata();
        }
        public bool TryGetDestinationVersion(ItemChange sourceChange, out ItemChange destinationVersion)
        {
            ItemMetadata metadata = _metadataStore.Metadata.FindItemMetadataById(sourceChange.ItemId);

            if (metadata == null)
            {
                destinationVersion = null;
                return false;
            }
            else
            {
                destinationVersion = new ItemChange(_idFormats, _metadataStore.ReplicaId, sourceChange.ItemId,
                        metadata.IsDeleted ? ChangeKind.Deleted : ChangeKind.Update,
                        metadata.CreationVersion, metadata.ChangeVersion);
                return true;
            }
        }
        public ulong GetNextTickCount()
        {
            return _metadataStore.Metadata.GetNextTickCount();
        }
        public IChangeDataRetriever GetDataRetriever()
        {
            return this;
        }
        public SyncIdFormatGroup IdFormats
        {
            get{return _idFormats;}
        }
        public void SaveConstraintConflict(
            ItemChange conflictingChange, 
            SyncId conflictingItemId,
            ConstraintConflictReason reason, 
            object conflictingChangeData,
            SyncKnowledge conflictingChangeKnowledge, 
            bool temporary)
        {
            var i = 1;
            // just to implement the interface
        }
        #endregion


        
    }

    public class ChangeErrorArgs : EventArgs
    {
        public string Path { get; set; }
        public string Message { get; set; }
    }
    public class ChangePerformArgs : EventArgs
    {
        public string Path { get; set; }
        public string Message { get; set; }
    }
    public class TransferPerformingArgs : EventArgs
    {
        public TransferHandle Handle { get; set; }
        public string Path { get; set; }
    }
    public class MyAppliedChangeEventArgs : EventArgs
    {
        public ChangeType ChangeType { get; private set; }
        public string NewFilePath { get; private set; }
        public string OldFilePath { get; private set; }

        public MyAppliedChangeEventArgs(ChangeType type, string newFilePath, string oldFilePath)
        {
            ChangeType = type;
            NewFilePath = newFilePath;
            OldFilePath = oldFilePath;
        }
    }
    
}
