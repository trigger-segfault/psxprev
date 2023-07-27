﻿using System.Collections;
using System.Text;

namespace PSXPrev.Common.Utils
{
    public static class StringUtils
    {
        public static string ToBitString(this BitArray bits)
        {
            var sb = new StringBuilder();

            for (var i = 0; i < bits.Count; i++)
            {
                sb.Append(bits[i] ? '1' : '0');
            }

            return sb.ToString();
        }
    }
}