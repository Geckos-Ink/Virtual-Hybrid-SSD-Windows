/*
 *	Virtual Hybrid SSD for Windows
 *	VHSSD  Copyright (C) 2023  Riccardo Cecchini <rcecchini.ds@gmail.com>
 *
 *	This program is free software: you can redistribute it and/or modify
 *	it under the terms of the GNU General Public License as published by
 *	the Free Software Foundation, either version 3 of the License, or
 *	(at your option) any later version.
 *
 *	This program is distributed in the hope that it will be useful,
 *	but WITHOUT ANY WARRANTY; without even the implied warranty of
 *	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *	GNU General Public License for more details.
 *
 *	You should have received a copy of the GNU General Public License
 *	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

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

        public Stream stream;
        public FileStream fstream;
        public MemoryStream mstream;

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
                fstream = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                stream = fstream;

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
            if(mstream != null)
            {
                mstream.CopyTo(fstream);
                mstream.Close();
                stream = mstream;
            }

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
            var toStartLength = To.Length;

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

            var drive = To.drive;
            if(drive != null)
            {
                drive.row.UsedBytes -= toStartLength;
                drive.row.UsedBytes += To.Length;
            }
        }

        public void LinkRamFile(RamFile ramFile)
        {
            var bytes = Read(Length);
            mstream = new MemoryStream(bytes);

            // Apply changes
            foreach (var piece in ramFile.Pieces)
                Write(piece.Data, piece.Pos);

            ramFile.Pieces.Clear(); // changed applied
        }
    }

    /// <summary>
    /// This class is used when is necessary to write to an unloaded file
    /// </summary>
    public class RamFile
    {
        public List<Piece> Pieces = new List<Piece>();

        public bool Used
        {
            get
            {
                return Pieces.Count > 0;
            }
        }

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
