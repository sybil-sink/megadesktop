using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaApi;

namespace MegaStore
{
    /// <summary>
    /// to be thrown when node is not found by path
    /// </summary>
    public class MegaStoreException : MegaApiException
    {
        public MegaStoreException(string message) : base(null, message) { }
    }
    /// <summary>
    /// to be thrown when 
    /// target path for inserting is not empty 
    /// or the parent node does not exist
    /// or the folder is not empty when deleting
    /// or the file size is 0 (not accepted by MEGA)
    /// </summary>
    public class MegaStoreConstraintException : MegaStoreException
    {
        public MegaStoreConstraintType ConstraintType { get; private set; }
        public MegaNodeHelper Node { get; set; }
        public MegaStoreConstraintException(MegaStoreConstraintType type, string message)
            : base(message)
        {
            ConstraintType = type;
        }
    }
    /// <summary>
    /// to be thrown when expected and actual ids don't match
    /// </summary>
    public class MegaStoreConcurrencyException : MegaStoreException
    {
        public MegaNodeHelper Node { get; set; }
        public MegaStoreConcurrencyException(string message) : base(message) { }
    }

    public enum MegaStoreConstraintType
    {
        NoParent,
        NotEmpty,
        TargetExists,
        ZeroSize
    }
}
