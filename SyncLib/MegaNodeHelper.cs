using MegaApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MegaStore
{
    public class MegaNodeHelper
    {
        MegaNode _Node;
        public MegaNode Node { get { return _Node; } set { _Node = value; pathCache = null; } }
        MegaNodeHelper _Parent;
        public MegaNodeHelper Parent { get { return _Parent; }
            set 
            {
                _Parent = value;
                if (value != null)
                {
                    Node.ParentId = value.Node.Id;
                }
                pathCache = null;
            }
        }
        string pathCache;
        public string Path
        {
            get
            {
                if (pathCache != null) { return pathCache; }
                var path = Parent == null ? "" : Parent.Path + "\\";
                return path + Node.Attributes.Name;
            }
        }

        internal void SetName(string name)
        {
            _Node.Attributes.Name = name;
            pathCache = null;
        }

        public override string ToString()
        {
            return String.Format("{0} : {1}", Node.Id, Path);
        }
    }
}
