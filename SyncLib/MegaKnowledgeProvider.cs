using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Synchronization;
using Microsoft.Synchronization.SimpleProviders;
using Microsoft.Synchronization.MetadataStorage;
using System.IO;
using Microsoft.Synchronization.Files;
using MegaStore;

namespace SyncLib
{
    internal class MegaKnowledgeProvider : KnowledgeSyncProvider
    {
        public event EventHandler<MyAppliedChangeEventArgs> AppliedChange;
        public bool NeedResync { get; private set; }

        NodeStore _nodeStore;
        MetadataStore _metadataStore;
        ChangeApplier _changeApplier;

        SyncIdFormatGroup _idFormats = null;
        SyncSessionContext _currentSessionContext = null;
        public MegaKnowledgeProvider(NodeStore store)
        {
            _nodeStore = store;

            _idFormats = new SyncIdFormatGroup();
            _idFormats.ChangeUnitIdFormat.IsVariableLength = false;
            _idFormats.ChangeUnitIdFormat.Length = 4;
            _idFormats.ItemIdFormat.IsVariableLength = false;
            _idFormats.ItemIdFormat.Length = 24;
            _idFormats.ReplicaIdFormat.IsVariableLength = false;
            _idFormats.ReplicaIdFormat.Length = 16;

            _metadataStore = new MetadataStore(_idFormats);
            _changeApplier = new ChangeApplier(_metadataStore, _idFormats, _nodeStore);
            _changeApplier.AppliedChange += OnAppliedChange;
            _changeApplier.DemandedResync += (s,e) => NeedResync = true;

            //Configuration.CollisionConflictResolutionPolicy =
        }

        private void OnAppliedChange(object sender, MyAppliedChangeEventArgs e)
        {
            if (AppliedChange != null)
            {
                AppliedChange(this, e);
            }
        }
        public void ResetDatabase()
        {
            _metadataStore.Reset();
        }
        public override void BeginSession(SyncProviderPosition position, SyncSessionContext syncSessionContext)
        {
            _metadataStore.BeginSession(_nodeStore.Nodes);

            _currentSessionContext = syncSessionContext;
        }
        public override void EndSession(SyncSessionContext syncSessionContext)
        {
            _metadataStore.EndSession();
        }
        public override ChangeBatch GetChangeBatch(uint batchSize, SyncKnowledge destinationKnowledge, out object changeDataRetriever)
        {
            ChangeBatch batch = _metadataStore.Metadata.GetChangeBatch(batchSize, destinationKnowledge);
            changeDataRetriever = _changeApplier;
            return batch;
        }
        public override FullEnumerationChangeBatch GetFullEnumerationChangeBatch(uint batchSize, SyncId lowerEnumerationBound, SyncKnowledge knowledgeForDataRetrieval, out object changeDataRetriever)
        {
            FullEnumerationChangeBatch batch = _metadataStore.Metadata.GetFullEnumerationChangeBatch(batchSize, lowerEnumerationBound, knowledgeForDataRetrieval);
            changeDataRetriever = _changeApplier;
            return batch;
        }

        public override void GetSyncBatchParameters(out uint batchSize, out SyncKnowledge knowledge)
        {
            batchSize = 10;
            knowledge = _metadataStore.Metadata.GetKnowledge();
        }
        public override void ProcessChangeBatch(ConflictResolutionPolicy resolutionPolicy, ChangeBatch sourceChanges,
            object changeDataRetriever, SyncCallbacks syncCallback, SyncSessionStatistics sessionStatistics)
        {
            _metadataStore.BeginTransaction();
            IEnumerable<ItemChange> localChanges = _metadataStore.Metadata.GetLocalVersions(sourceChanges);
            NotifyingChangeApplier changeApplier = new NotifyingChangeApplier(_idFormats);
            changeApplier.ApplyChanges(resolutionPolicy, sourceChanges, changeDataRetriever as IChangeDataRetriever, localChanges, _metadataStore.Metadata.GetKnowledge(),
                _metadataStore.Metadata.GetForgottenKnowledge(), _changeApplier, _currentSessionContext, syncCallback);

            _metadataStore.CommitTransaction();
        }
        public override void ProcessFullEnumerationChangeBatch(ConflictResolutionPolicy resolutionPolicy, FullEnumerationChangeBatch sourceChanges, object changeDataRetriever, SyncCallbacks syncCallback, SyncSessionStatistics sessionStatistics)
        {
            _metadataStore.BeginTransaction();

            IEnumerable<ItemChange> localChanges = _metadataStore.Metadata.GetFullEnumerationLocalVersions(sourceChanges);
            NotifyingChangeApplier changeApplier = new NotifyingChangeApplier(_idFormats);
            changeApplier.ApplyFullEnumerationChanges(resolutionPolicy, sourceChanges, changeDataRetriever as IChangeDataRetriever, localChanges, _metadataStore.Metadata.GetKnowledge(),
                _metadataStore.Metadata.GetForgottenKnowledge(), _changeApplier, _currentSessionContext, syncCallback);

            _metadataStore.CommitTransaction();
        }
        public override SyncIdFormatGroup IdFormats
        {
            get { return _idFormats; }
        }

    }
}