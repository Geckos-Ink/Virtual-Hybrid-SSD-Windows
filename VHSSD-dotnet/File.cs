using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VHSSD.Chucks;

namespace VHSSD
{
    public class File
    {
        public VHFS.Drive drive;

        public string fileName;
        public FileStream stream;

        public File(string fileName, VHFS.Drive drive=null)
        {
            this.drive = drive;

            this.fileName = fileName;
            stream = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);

            drive?.OpenFiles++;
        }

        public void Write(byte[] data, long pos = -1)
        {
            bool resize = pos == -1;
            if (resize) pos = 0;

            drive?.addWriteBytes(data.Length);

            // Set the position in the file
            stream.Seek(pos, SeekOrigin.Begin);

            // Convert the string to bytes and write at the specified position
            stream.Write(data, 0, data.Length);

            if (resize) this.Length = stream.Position;
        }

        public byte[] Read(long len = 0, long pos = 0)
        {
            bool allFile = len == 0;
            if (allFile) len = stream.Length;

            drive?.addReadBytes(len);

            // Set the position in the file
            stream.Seek(pos, SeekOrigin.Begin);

            // Read the specified length of data and convert it to a string
            byte[] buffer = new byte[len];
            stream.Read(buffer, 0, buffer.Length);

            return buffer;
        }

        public long Length {
            get { return stream.Length; } 
        
            set
            {
                stream.SetLength(value);
            }
        }

        public void Flush()
        {
            stream.Flush();
        }

        public void Close()
        {
            if (stream.CanRead)
            {
                Flush();
                stream.Close();

                drive.OpenFiles--;
            }
        }

        public void Delete()
        {
            Close();
            System.IO.File.Delete(fileName);
        }
    }
}
