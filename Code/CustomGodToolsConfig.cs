using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GodTools
{
    public static class CustomGodToolsConfig
    {
        public static bool HideCursor;
        public static void SetHideCursor(bool value)
        {
            HideCursor = value;
        }
        public static bool HideDrop;
        public static void SetHideDrop(bool value)
        {
            HideDrop = value;
        }
    }
}