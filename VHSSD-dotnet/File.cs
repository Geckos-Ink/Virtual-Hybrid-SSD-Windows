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

        /// <summary>
        /// Full path of the file
        /// </summary>
        public string FileName; 

        public FileStream stream;

        long openingLength = 0;

        public File(string fileName, VHFS.Drive drive=null)
        {
            this.drive = drive;
            this.FileName = fileName;
        }

        void checkStream()
        {
            if(stream == null)
            {
                stream = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                openingLength = Length;

                if (drive != null) drive.OpenFiles++;
            }
        }

        public void Write(byte[] data, long pos = -1)
        {
            checkStream();

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
            checkStream();

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
            get {
                checkStream();
                return stream.Length; 
            } 
        
            set
            {
                stream.SetLength(value);
            }
        }

        public void Flush()
        {
            if(stream != null)
                stream.Flush();
        }

        public void Close()
        {
            if (stream != null && stream.CanRead)
            {
                if (drive != null)
                {
                    drive.OpenFiles--;
                    drive.row.UsedBytes += Length - openingLength;
                }

                Flush();
                stream.Close();
                stream = null;
            }
        }

        public void Delete()
        {
            if(drive != null)
                drive.row.UsedBytes -= Length;

            Close();
            System.IO.File.Delete(FileName);
        }

        public void CopyTo(File To)
        {
            //todo: Check for To's drive allocation size
            var bSize = 4096;

            byte[] buffer = new byte[bSize];
            long bytesRead = 0;

            // Read from the source file and write to the destination file in chunks
            while (bytesRead < Length)
            {
                var until = buffer.LongLength + bytesRead;
                if (until > Length)
                    until = Length;

                buffer = Read(buffer.LongLength, bytesRead);
                To.Write(buffer, bytesRead);

                bytesRead = until;
            }
        }
    }

    /// <summary>
    /// This class is used when is necessary to write to an unloaded file
    /// </summary>
    public class RamFile
    {
        public List<Piece> Pieces = new List<Piece>();

        public void Write(byte[] data, long pos)
        {
            var piece = new Piece() { Pos = pos, Data = data };
            Pieces.Add(piece);
        }

        public struct Piece
        {
            public long Pos;
            public byte[] Data;
        }
    }
}
