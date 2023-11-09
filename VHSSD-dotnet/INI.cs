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

using Newtonsoft.Json.Linq;
using System;
using System.Collections;
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
        public Properties Props;

        public INI (string FileName)
        {
            this.FileName = FileName;

            content = System.IO.File.ReadAllText(FileName);

            CalculatePieces();
            PrepareProperties();
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

        public void PrepareProperties()
        {
            Props = new Properties();

        }

        public class Properties
        {
            public string Name;
            public string Value;

            public Dictionary<string, Properties> Props = new Dictionary<string, Properties>();

            public int Count
            {
                get
                {
                    return Props.Count;
                }
            }

            public string this[string key]
            {
                get 
                {
                    Properties p;
                    if (!Props.TryGetValue(key, out p))
                        return null;

                    return p.Value; 
                }
                set
                {
                    if (key == "")
                        key = Count.ToString();

                    var p = new Properties();
                    p.Name = key;
                    p.Value = value;

                    Props[key] = p;
                }
            }
        }
    }
}
