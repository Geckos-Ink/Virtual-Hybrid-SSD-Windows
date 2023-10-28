﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class File
    {
        string fileName;
        FileStream stream;

        public File(string fileName)
        {
            this.fileName = fileName;
            stream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        }

        public void Write(byte[] data, long pos = 0)
        {
            bool resize = pos == -1;
            if (resize) pos = 1;

            // Set the position in the file
            stream.Seek(pos, SeekOrigin.Begin);

            // Convert the string to bytes and write at the specified position
            stream.Write(data, 0, data.Length);

            if (resize) stream.SetLength(stream.Position);
        }

        public byte[] Read(long len = 0, long pos = 0)
        {
            bool allFile = len == 0;
            if (allFile) len = stream.Length;

            // Set the position in the file
            stream.Seek(pos, SeekOrigin.Begin);

            // Read the specified length of data and convert it to a string
            byte[] buffer = new byte[len];
            stream.Read(buffer, 0, buffer.Length);

            return buffer;
        }

        public long Length { get { return stream.Length; } }

        public void Close()
        {
            if (stream.CanRead)
            {
                stream.Flush();
                stream.Close();
            }
        }

        public void Delete()
        {
            Close();
            System.IO.File.Delete(fileName);
        }
    }
}
