using MegaApi;
using MegaApi.Utility;
using Microsoft.Synchronization.Files;
using Microsoft.Synchronization.SimpleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SyncLib
{
    internal class MegaStore
    {
        public event EventHandler ProgressChanged;
        Mega api;
        public const uint IdLength = 10;
        public const uint nameLength = 256;

        string rootFolderName;
        MegaNodeHelper rootFolder;
        List<MegaNodeHelper> serverNodes = new List<MegaNodeHelper>();

        public MegaStore(Mega api, string rootFolderName)
        {
            this.api = api;
            this.rootFolderName = rootFolderName;
        }

        internal List<ItemFieldDictionary> ListNodes()
        {
            List<ItemFieldDictionary> items = new List<ItemFieldDictionary>();
            var helpers = GetFilesAndDirectories();
            foreach (var h in helpers)
            {
                ItemFieldDictionary dict = new ItemFieldDictionary();
                dict.Add(new ItemField(ItemFields.CUSTOM_FIELD_ID, typeof(string), h.Node.Id));
                dict.Add(new ItemField(ItemFields.CUSTOM_FIELD_NAME, typeof(string), h.Path));
                items.Add(dict);
            }
            return items;
        }

        void OnProgressChanged()
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, new EventArgs());
            }
        }
        private List<MegaNodeHelper> GetFilesAndDirectories()
        {
            serverNodes = new List<MegaNodeHelper>();
            List<MegaNode> nodes = null;

            try
            {
                nodes = api.GetNodesSync();
            }
            catch (MegaApiException e)
            {
                throw new ApplicationException(String.Format("Could not get the list of nodes. Error: {0}", e));
            }
            // todo ignore sync folder in the trash
            var rootNode = nodes.Where(n => n.Attributes.Name == rootFolderName).FirstOrDefault();
            if (rootNode == null)
            {
                try
                {
                    rootNode = api.CreateFolderSync(nodes.Where(n => n.Type == MegaNodeType.RootFolder).First().Id, rootFolderName);
                }
                catch (MegaApiException e)
                {
                    throw new ApplicationException(String.Format("Could create the sync folder. Error: {0}", e));
                }
            }
            rootFolder = new MegaNodeHelper { Node = rootNode };
            AddChildren(serverNodes, rootFolder, nodes);
            return serverNodes;
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

        internal void DeleteFile(string path, string expectedId)
        {
            var node = FindNodePyPath(path);
            if (node == null || node.Node.Id != expectedId)
            {
                throw new ApplicationException("Concurrency when deleting");
            }
            try
            {
                api.RemoveNodeSync(node.Node.Id);
            }
            catch (MegaApiException e)
            {
                throw new ApplicationException(String.Format("Error when deleting {0}. Error: {1}", path, e));
            }

            lock (serverNodes) { serverNodes.Remove(node); }
        }

        bool isDirectory(string path)
        {
            var node = FindNodePyPath(path);
            if (node != null && node.Node.Type == MegaNodeType.Folder) { return true; }
            return false;
        }

        MegaNodeHelper HelperFromNode(MegaNode node)
        {
            return new MegaNodeHelper
            {
                Node = node,
                Parent = serverNodes.Where(n => n.Node.Id == node.ParentId).FirstOrDefault()
            };
        }

        private string FindParentId(string path)
        {
            var dirName = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dirName)) { return rootFolder.Node.Id; }
            var parent = FindNodePyPath(dirName);
            return parent == null ? null : parent.Node.Id;
        }
        internal SyncedNodeAttributes InsertNode(FileData fileData, string path, Stream dataStream)
        {
            var existing = FindNodePyPath(path);
            if (existing != null)
            {
                // created by Ensuredirectoriy
                throw new ApplicationException("Constraint when inserting " + path);
            }
            string newId = null;

            var parentId = FindParentId(path);
            if (parentId == null)
            {
                throw new ApplicationException(String.Format("No parent for {0}", path));
            }


            //MegaNode target = null;
            //try
            //{
            //    target = EnsureDirectories(path);
            //}
            //catch (MegaApiException e)
            //{
            //    throw new ApplicationException(String.Format("Could create the directory for {0}. Error: {1}", path, e));
            //}
            if (fileData.IsDirectory)
            {
                MegaNode newDirectoryNode = null;
                try
                {
                    newDirectoryNode = api.CreateFolderSync(parentId, fileData.Name);
                }
                catch (MegaApiException e)
                {
                    throw new ApplicationException(String.Format("Could create the directory {0}. Error: {1}", path, e));
                }
                lock (serverNodes) { serverNodes.Add(HelperFromNode(newDirectoryNode)); }
                newId = newDirectoryNode.Id;
            }
            else
            {
                //if (existing!=null)
                //{
                //    BackupFile(existing);
                //    throw new ApplicationException("Constraint when inserting " + path);
                //}
                if (fileData.Size < 1)
                {
                    throw new ApplicationException("Skipping zero-length " + path);
                }

                MegaNode newNode = null;
                try
                {
                    newNode = api.UploadStreamSync(parentId, fileData.Name, dataStream, fileData.Size);
                }
                catch (MegaApiException e)
                {
                    throw new ApplicationException("Could not upload the file " + path + ". Error: " + e);
                }

                lock (serverNodes) { serverNodes.Add(HelperFromNode(newNode)); }
                newId = newNode.Id;
            }

            if (newId == null) { throw new ApplicationException("Could not insert the node " + path); }
            return new SyncedNodeAttributes 
            {
                Name = fileData.RelativePath,
                Id = newId
            };
        }

        //private MegaNode EnsureDirectories(string path)
        //{
        //    var folders = Path.GetDirectoryName(path.TrimStart(new char[] { '\\' }))
        //                    .Split(new char[] { '\\' });
        //    if (string.IsNullOrEmpty(folders[0])) { return rootFolder.Node; }
        //    var parent = rootFolder;

        //    MegaNodeHelper helper = null;
        //    foreach (var folder in folders)
        //    {
        //        helper = serverNodes
        //            .Where(h => h.Node.Attributes.Name == folder && h.Node.ParentId == parent.Node.Id)
        //            .FirstOrDefault();

        //        if (helper == null)
        //        {
        //            var n = api.CreateFolderSync(parent.Node.Id, folder);
        //            helper = new MegaNodeHelper
        //            {
        //                Node = n,
        //                Parent = parent == rootFolder ? null : parent
        //            };
        //            lock (serverNodes) { serverNodes.Add(helper); }
        //        }
        //        parent = helper;
        //    }
        //    return helper.Node;
        //}

        internal IFileDataRetriever GetFileRetriever(string path, string expectedId)
        {
            var node = FindNodePyPath(path);
            if (node == null || node.Node.Id != expectedId)
            {
                throw new ApplicationException("Concurrency for " + path);
            }
            var attr = node.Node.Type == MegaNodeType.File ? FileAttributes.Normal : FileAttributes.Directory;
            var size = node.Node.Type == MegaNodeType.File ? (long)node.Node.Size : 0;
            var ts = (DateTime)node.Node.Timestamp;
            var fd = new FileData(path, attr, ts, ts, ts, size);
            Func<Stream> streamFn = null;
            if (node.Node.Type == MegaNodeType.File)
            {
                streamFn = () =>
                {
                    var tempFile = Path.GetTempFileName();
                    bool error = false;
                    TransferHandle handle = null;
                    try
                    {
                        api.DownloadFileSync(node.Node, tempFile);
                    }
                    catch (MegaApiException e)
                    {
                        throw new ApplicationException(String.Format("Could not download the file {0}. Error: {1}", path, e));
                    }
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


        internal SyncedNodeAttributes MoveFile(string oldPath, string newPath, string expectedId)
        {
            var target = FindNodePyPath(oldPath);
            if (target == null || target.Node.Id != expectedId)
            {
                throw new ApplicationException("Concurrency when moving " + oldPath + " to " + newPath);
            }

            // rename
            if (Path.GetDirectoryName(oldPath) == Path.GetDirectoryName(newPath))
            {
                target.Node.Attributes.Name = Path.GetFileName(newPath);
                try
                {
                    api.UpdateNodeAttrSync(target.Node);
                }
                catch (MegaApiException e)
                {
                    throw new ApplicationException(String.Format("Error when renaming {0} to {1}. Error: {2}", oldPath, newPath, e));
                }
            }
            else // move
            {
                //var targetNodeId = EnsureDirectories(newPath);
                var parentId = FindParentId(newPath);
                if (parentId == null)
                {
                    throw new ApplicationException(String.Format("Error when moving {0} to {1}. No parent", oldPath, newPath));
                }
                try
                {
                    api.MoveNodeSync(target.Node.Id, parentId);
                }
                catch (MegaApiException e)
                {
                    throw new ApplicationException(String.Format("Error when moving {0} to {1}. Error: {2}", oldPath, newPath, e));
                }
                target.Parent = FindNodePyPath(Path.GetDirectoryName(newPath));
            }

            return new SyncedNodeAttributes
            {
                Name = newPath,
                Id = target.Node.Id
            };
        }

        internal SyncedNodeAttributes UpdateFile(string path, FileData fileData, Stream dataStream, string expectedId)
        {
            var target = FindNodePyPath(path);
            if(target == null || target.Node.Id != expectedId)
            {
                throw new ApplicationException("Concurrency error when updating "+path);
            }
            if (fileData.IsDirectory)
            {
                return new SyncedNodeAttributes
                {
                    Name = path,
                    Id = target.Node.Id
                };
            }

            try
            {
                api.RemoveNodeSync(target.Node.Id);
            }
            catch (MegaApiException e)
            {
                throw new ApplicationException(String.Format("Could not delete {0}. Error: {1}", path, e));
            }
            lock (serverNodes) { serverNodes.Remove(target); }
            return InsertNode(fileData, path, dataStream);
        }
        internal void BackupFile(string path)
        {
            BackupFile(FindNodePyPath(path));
        }
        internal void BackupFile(MegaNodeHelper node, int version = 0)
        {
            if (node == null) { return; }
            var newPath = node.Path + ".remote" + (version > 0 ? version.ToString() : "");
            var found = FindNodePyPath(newPath);
            if (found != null)
            {
                BackupFile(node, ++version);
            }
            else
            {
                node.Node.Attributes.Name += ".remote" + (version > 0 ? version.ToString() : "");
                try
                { 
                    api.UpdateNodeAttrSync(node.Node);
                }
                catch(MegaApiException e)
                {
                    throw new ApplicationException(String.Format("Error when backuping {0}. Error: {1}", node.Path, e)); 
                }
            }
        }

        
        MegaNodeHelper FindNodePyPath(string path)
        {
            lock (serverNodes)
            {
                return serverNodes.Where(n => n.Path == path).FirstOrDefault();
            }
        }
    }
}
