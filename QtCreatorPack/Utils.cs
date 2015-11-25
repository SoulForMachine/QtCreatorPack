using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtCreatorPack
{
    class Utils
    {
        // For given path, returns a path relative to relativeTo parameter.
        public static string PathRelativeTo(string path, string relativeTo)
        {
            if (path == null || path.Length == 0)
                return "";

            if (relativeTo == null || relativeTo.Length == 0)
                return path;

            string[] pathArr = System.IO.Path.GetFullPath(path).Split('\\');
            string[] relativeToArr = System.IO.Path.GetFullPath(relativeTo).Split('\\');

            int count = Math.Min(pathArr.Length, relativeToArr.Length);
            int dirsEqual = 0;

            while (dirsEqual < count)
            {
                if (pathArr[dirsEqual].Equals(relativeToArr[dirsEqual], StringComparison.OrdinalIgnoreCase) == false)
                {
                    break;
                }
                ++dirsEqual;
            }

            return string.Join("\\", pathArr, dirsEqual, path.Length - dirsEqual);
        }
    }
}
