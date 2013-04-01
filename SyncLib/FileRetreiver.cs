using Microsoft.Synchronization.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SyncLib
{
    class FileRetriever : IFileDataRetriever
    {
        public string AbsoluteSourceFilePath
        {
            get { throw new NotImplementedException("Absolute Path Not Supported"); }
        }

        Func<Stream> getStreamFn;
        public FileRetriever(Func<Stream> getStreamFn)
        {
            this.getStreamFn = getStreamFn;
        }
        public FileData FileData { get; set; }

        public Stream FileStream
        {
            get
            {
                return getStreamFn();
            }
        }
        public string RelativeDirectoryPath { get; set; }
    }
}
