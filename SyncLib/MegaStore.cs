using MegaApi;
using MegaApi.Comms.Requests;
using Microsoft.Synchronization.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using SyncLib;

namespace MegaStore
{
    // candidate to the main api
    internal class NodeStore
    {

        public event EventHandler Updated;
        public double RefreshInterval
        {
            get { return refreshTimer.Interval; }
            set { refreshTimer.Interval = value; }
        }
        public bool EnableRaisingEvents { get; set; }
        public bool RecycleDeletedItems { get; set; }
        public List<MegaNodeHelper> Nodes { get { return new List<MegaNodeHelper>(serverNodes); } }

        string _tempDir;
        public string TempDir
        {
            get
            {
                if (string.IsNullOrEmpty(_tempDir))
                {
                    _tempDir = Path.Combine(Path.GetTempPath(),"MegaDesktop");
                    Directory.CreateDirectory(_tempDir);
                }
                return _tempDir;
            }
        }
        Mega api;
        string rootFolderName;
        MegaNodeHelper rootFolder;
        string recyclingId;
        List<MegaNodeHelper> serverNodes = new List<MegaNodeHelper>();
        Timer refreshTimer;

        public NodeStore(Mega api, string rootFolderName)
        {
            RecycleDeletedItems = true;
            this.api = api;
            api.ServerRequest += HandleServerRequest;
            this.rootFolderName = rootFolderName;
            refreshTimer = new Timer
            {
                Interval = 15*60*1000,
                AutoReset = false,
                Enabled = false
            };
            refreshTimer.Elapsed += (s, e) => RefreshNodeList();
            RefreshNodeList();
        }

        protected void OnUpdated()
        {
            if (EnableRaisingEvents && Updated != null)
            {
                Updated(this, null);
            }
        }

        volatile bool refreshing = false;
        void HandleServerRequest(object sender, MegaApi.Comms.ServerRequestArgs e)
        {
            if (refreshing) { return; }
            lock (serverNodes)
            {
                bool updates = false;
                bool needRefresh = false;
                foreach (var command in e.commands)
                {
                    if (command.IsMine) { continue; }
                    switch (command.Command)
                    {
                        case ServerCommandType.NodeAddition:
                            var ca = (NodeAdditionCommand)command;
                            var added = new List<MegaNodeHelper>();
                            foreach(var n in ca.Nodes)
                            {
                                if (IsInsideWorkingRoot(n))
                                {
                                    if (n.Type == MegaNodeType.Folder) { needRefresh = true; break; }
                                    else
                                    {
                                        added.Add(HelperFromNode(n));
                                        updates = true;
                                    }
                                }
                            }
                            serverNodes.AddRange(added);
                            break;
                        case ServerCommandType.NodeDeletion:
                            var cd = (NodeDeletionCommand)command;
                            var condemned = serverNodes.Where(n => n.Node.Id == cd.NodeId).FirstOrDefault();
                            if (condemned != null)
                            {
                                if (condemned.Node.Type == MegaNodeType.Folder) { needRefresh = true; }
                                else
                                {
                                    serverNodes.Remove(condemned);
                                    updates = true;
                                }
                            }
                            break;
                        case ServerCommandType.NodeUpdation:
                            var cu = (NodeUpdationCommand)command;
                            {
                                var updating = serverNodes.Where(n => n.Node.Id == cu.NodeId).FirstOrDefault();
                                if (updating != null)
                                {
                                    if (updating.Node.Type == MegaNodeType.Folder) { needRefresh = true; }
                                    else
                                    {
                                        updating.Node.Attributes = cu.Attributes;
                                        updates = true;
                                    }
                                }
                            }
                            break;
                        default:
                            break;
                    }
                    if (needRefresh) { break; }
                }

                if (needRefresh) { RefreshNodeList(); }
                else if (updates) { OnUpdated(); }
            }
        }

        #region utility
        private void RefreshNodeList()
        {
            lock (serverNodes)
            {
                refreshing = true;
                refreshTimer.Stop();
                serverNodes = new List<MegaNodeHelper>();
                List<MegaNode> nodes = null;

                nodes = api.GetNodesSync();

                // todo ignore root folder in the trash
                var rootNode = nodes.Where(n => n.Attributes.Name == rootFolderName).FirstOrDefault();
                if (rootNode == null)
                {
                    rootNode = api.CreateFolderSync(nodes.Where(n => n.Type == MegaNodeType.RootFolder).First().Id, rootFolderName);
                }
                rootFolder = new MegaNodeHelper { Node = rootNode };
                if (string.IsNullOrEmpty(recyclingId))
                {
                    var recycleNode = nodes.Where(n => n.Type == MegaNodeType.Trash).FirstOrDefault();
                    if (recycleNode == null) { RecycleDeletedItems = false; }
                    else { recyclingId = recycleNode.Id; }
                }
                AddChildren(serverNodes, rootFolder, nodes);

                refreshTimer.Start();
                refreshing = false;
            }
            OnUpdated();
        }
        private void AddChildren(List<MegaNodeHelper> target, MegaNodeHelper parent, List<MegaNode> serverNodes, bool r = false)
        {
            var children = serverNodes.Where(n=>n.ParentId == parent.Node.Id);
            foreach (var child in children)
            {
                var c = new MegaNodeHelper
                {
                    Node = child,
                    // do not set the root folder as parent
                    Parent = r ? parent : null
                };
                target.Add(c);
                AddChildren(target, c, serverNodes, true);
            }
        }
        public bool IsDirectory(string path)
        {

            var node = FindNodeByPath(path);
            if (node == null) { throw new MegaStoreException("Could not find the node with the given path"); }
            return IsDirectory(node);
        }
        public bool IsDirectory(MegaNodeHelper node)
        {
            return node.Node.Type == MegaNodeType.Folder;
        }
        
        public MegaNodeHelper FindByPath(string path, string expectedId)
        {
            lock (serverNodes)
            {
                var node = AssertExist(path);
                AssertId(node, path);
                return node;
            }
        }

        bool IsInsideWorkingRoot(MegaNode node)
        {
            lock(serverNodes)
            {
                var parent = serverNodes.Where(n => n.Node.Id == node.ParentId).FirstOrDefault();
                if (parent == null && node.ParentId != rootFolder.Node.Id) { return false; }
                return true;
            }
        }
        MegaNodeHelper HelperFromNode(MegaNode node)
        {
            lock (serverNodes)
            {
                return new MegaNodeHelper
                {
                    Node = node,
                    Parent = serverNodes.Where(n => n.Node.Id == node.ParentId).FirstOrDefault()
                };
            }
        }
        public MegaNode CreateDirectory(string path)
        {
            var folders = Path.GetDirectoryName(path.TrimStart(new char[] { '\\' }))
                            .Split(new char[] { '\\' });
            if (string.IsNullOrEmpty(folders[0])) { return rootFolder.Node; }
            var parent = rootFolder;

            MegaNodeHelper helper = null;
            foreach (var folder in folders)
            {
                lock (serverNodes)
                {
                    helper = serverNodes
                        .Where(h => h.Node.Attributes.Name == folder && h.Node.ParentId == parent.Node.Id)
                        .FirstOrDefault();

                    if (helper == null)
                    {
                        var n = api.CreateFolderSync(parent.Node.Id, folder);
                        helper = new MegaNodeHelper
                        {
                            Node = n,
                            Parent = parent == rootFolder ? null : parent
                        };

                    }
                    parent = helper;
                }
            }
            return helper.Node;
        }
        public MegaNodeHelper FindNodeByPath(string path)
        {
            lock (serverNodes)
            {
                return serverNodes.Where(n => n.Path == path).FirstOrDefault();
            }
        }
        public MegaNodeHelper FindNodeById(string id)
        {
            lock (serverNodes)
            {
                return serverNodes.Where(n => n.Node.Id == id).FirstOrDefault();
            }
        }
        #endregion
        
        /// <summary>
        /// CREATE
        /// </summary>
        internal SyncedNodeAttributes InsertNode(FileData fileData, string path, Stream dataStream)
        {
            string newId = null;
            lock (serverNodes)
            {
                AssertPathClear(path);
                var parent = AssertParentForPath(path);
                MegaNode newNode = null;
                if (fileData.IsDirectory)
                {
                    newNode = api.CreateFolderSync(parent.Node.Id, fileData.Name);
                }
                else
                {
                    AssertNonZero(fileData);
                    newNode = api.UploadStreamSync(parent.Node.Id, fileData.Name, dataStream, fileData.Size);
                }
                serverNodes.Add(HelperFromNode(newNode));
                newId = newNode.Id;

            }

            return new SyncedNodeAttributes 
            {
                Path = fileData.RelativePath,
                Id = newId
            };
        }
        
        /// <summary>
        /// READ
        /// </summary>
        internal IFileDataRetriever GetFileRetriever(string path, string expectedId)
        {
            var node = AssertExist(path);
            AssertId(node, expectedId);

            var attr = node.Node.Type == MegaNodeType.File ? FileAttributes.Normal : FileAttributes.Directory;
            var size = node.Node.Type == MegaNodeType.File ? (long)node.Node.Size : 0;
            var ts = (DateTime)node.Node.Timestamp;
            var fd = new FileData(path, attr, ts, ts, ts, size);
            Func<Stream> streamFn = null;
            if (node.Node.Type == MegaNodeType.File)
            {
                streamFn = () =>
                {
                    var tempFile = Path.Combine(TempDir, MegaApi.Utility.Util.RandomString(6) + ".sync");
                    bool error = false;
                    TransferHandle handle = null;
                    api.DownloadFileSync(node.Node, tempFile);
                    return File.OpenRead(tempFile);
                };
            }
            else
            {
                streamFn = () => { throw new NotImplementedException(); };
            }
            return new FileRetriever(streamFn)
            {
                FileData = fd,
                RelativeDirectoryPath = Path.GetDirectoryName(path)
            };
        }

        /// <summary>
        /// UPDATE (move)
        /// </summary>
        internal SyncedNodeAttributes MoveFile(string oldPath, string newPath, string expectedId)
        {
            lock (serverNodes)
            {
                var target = AssertExist(oldPath);
                AssertId(target, expectedId);
                AssertPathClear(newPath);

                // rename
                if (Path.GetDirectoryName(oldPath) == Path.GetDirectoryName(newPath))
                {
                    target.SetName(Path.GetFileName(newPath));
                    api.UpdateNodeAttrSync(target.Node);
                }
                else // move
                {
                    MegaNodeHelper parent = AssertParentForPath(newPath);

                    api.MoveNodeSync(target.Node.Id, parent.Node.Id);
                    target.Parent = FindNodeByPath(Path.GetDirectoryName(newPath));
                }

                return new SyncedNodeAttributes
                {
                    Path = newPath,
                    Id = target.Node.Id
                };
            }
        }

        /// <summary>
        /// UPDATE (upload)
        /// </summary>
        internal SyncedNodeAttributes UpdateFile(string path, FileData fileData, Stream dataStream, string expectedId)
        {
            lock (serverNodes)
            {
                var target = AssertExist(path);
                AssertId(target, expectedId);

                if (fileData.IsDirectory)
                {
                    return new SyncedNodeAttributes
                    {
                        Path = path,
                        Id = target.Node.Id
                    };
                }

                api.RemoveNodeSync(target.Node.Id);

                serverNodes.Remove(target);
                return InsertNode(fileData, path, dataStream);
            }
        }

        /// <summary>
        /// DELETE
        /// </summary>
        internal void DeleteFile(string path, string expectedId)
        {
            lock (serverNodes)
            {
                var node = AssertExist(path);
                AssertId(node, expectedId);
                AssertNoChildren(node);

                if (RecycleDeletedItems)
                {
                    api.MoveNodeSync(node.Node.Id, recyclingId);
                }
                else
                {
                    api.RemoveNodeSync(node.Node.Id);
                }
                serverNodes.Remove(node);
            }
        }

        /// <summary>
        /// Create backup copy
        /// </summary>
        internal MegaNodeHelper BackupFile(string path)
        {
            var node = AssertExist(path);
            BackupFile(node);
            return node;
        }
        internal void BackupFile(MegaNodeHelper node, int? version = null)
        {
            if (node == null) { return; }
            lock (serverNodes)
            {
                var newPath = Path.Combine(
                    Path.GetDirectoryName(node.Path),
                    String.Format("{0}.backup{1}{2}",
                        Path.GetFileNameWithoutExtension(node.Path),
                        version,
                        Path.GetExtension(node.Path)));

                var found = FindNodeByPath(newPath);
                if (found != null)
                {
                    BackupFile(node, version == null ? 1 : ++version);
                }
                else
                {
                    var newName = Path.GetFileName(newPath);
                    node.SetName(newName);
                    api.UpdateNodeAttrSync(node.Node);
                }
            }
        }

        public void CleanTemp()
        {
            foreach (var file in Directory.GetFiles(TempDir))
            {
                try
                {
                    File.Delete(file);
                }
                catch { }
            }
        }
        #region asserts
        private void AssertNonZero(FileData fd)
        {
            if (!fd.IsDirectory && fd.Size == 0)
            {
                throw new MegaStoreConstraintException(MegaStoreConstraintType.ZeroSize,
                    "Zero-length files are not supported");
            }
        }
        private void AssertPathClear(string path)
        {
            var existing = FindNodeByPath(path);
            if (existing != null)
            {
                throw new MegaStoreConstraintException(MegaStoreConstraintType.TargetExists, 
                    "The node with the target path already exists") { Node = existing };
            }
        }

        private void AssertId(MegaNodeHelper target, string expectedId)
        {
            if (target.Node.Id != expectedId)
            {
                throw new MegaStoreConcurrencyException("The node has different id") { Node = target };
            }
        }

        private MegaNodeHelper AssertExist(string path)
        {
            var target = FindNodeByPath(path);
            if (target == null)
            {
                throw new MegaStoreException("Could not find the node with the given path");
            }
            return target;
        }
        private MegaNodeHelper AssertParentForPath(string path)
        {
            var dirName = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dirName)) { return rootFolder; }
            var parent = FindNodeByPath(dirName);
            if (parent == null)
            {
                throw new MegaStoreConstraintException(MegaStoreConstraintType.NoParent, "Could not find the parent node");
            }
            return parent;
        }
        private void AssertNoChildren(MegaNodeHelper node)
        {
            if (IsDirectory(node))
            {
                var children = serverNodes.Where(n => n.Node.ParentId == node.Node.Id).FirstOrDefault();
                if (children != null)
                {
                    throw new MegaStoreConstraintException(MegaStoreConstraintType.NotEmpty, "The folder is not empty");
                }
            }
        }
        #endregion

    }
}
