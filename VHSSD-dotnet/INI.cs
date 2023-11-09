using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class INI
    {
        public string FileName;
        string content;
        List<TextPiece> pieces = new List<TextPiece>();
        

        public INI (string FileName)
        {
            this.FileName = FileName;

            content = System.IO.File.ReadAllText(FileName);

            CalculatePieces();
        }

        public void CalculatePieces()
        {
            pieces.Clear();

            var piece = new TextPiece();
            pieces.Add(piece);

            for (int c = 0; c < content.Length; c++)
            {
                var ch = content[c];
                var t = CharType(ch);

                if (!(piece.Type == '\0' || piece.Type == t))
                {
                    piece = new TextPiece();
                    pieces.Add(piece);
                }

                piece.Type = t;
                piece.Content += ch;
            }
        }

        public char CharType(char ch)
        {
            if (ch >= 'A' && ch <= 'Z') return 'L';
            if (ch >= 'a' && ch <= 'z') return 'L';
            if (ch >= '0' && ch <= '9') return 'L';

            if (ch == '\r' || ch == '\n') return 'n';

            if (ch == '\t' || ch == ' ') return ' ';

            return 'd';
        }

        public class TextPiece
        {
            public char Type = '\0';
            public string Content;
        }
    }
}
