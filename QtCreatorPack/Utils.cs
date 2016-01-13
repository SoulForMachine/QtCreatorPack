using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

            return string.Join("\\", pathArr, dirsEqual, pathArr.Length - dirsEqual);
        }

        #region Image utils

        private const int MAX_PATH = 260;
        private const int ILD_NORMAL = 0;
        private const int SHGFI_ICON = 0x100;
        private const int SHGFI_SMALLICON = 0x001;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEINFO
        {
            internal IntPtr hIcon;
            internal IntPtr iIcon;
            internal uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            internal string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            internal string szTypeName;
        };

        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
           uint cbSizeFileInfo, uint uFlags);

        [DllImport("comctl32.dll", CharSet = CharSet.None, ExactSpelling = false)]
        private static extern IntPtr ImageList_GetIcon(IntPtr imageListHandle, int iconIndex, int flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static IntPtr GetProjectItemIcon(IVsHierarchy hierarchy, uint itemId, out bool shouldDestroy)
        {
            IntPtr hImage = IntPtr.Zero;
            shouldDestroy = false;

            if (hierarchy != null &&
                itemId > 0 &&
                itemId != VSConstants.VSITEMID_NIL)
            {
                hImage = GetIconWithImageList(hierarchy, itemId, out shouldDestroy);
                if (hImage == null)
                {
                    hImage = GetIconWithoutImageList(hierarchy, itemId, out shouldDestroy);
                    if (hImage == null)
                    {
                        string canonicalName;
                        hierarchy.GetCanonicalName(itemId, out canonicalName);
                        hImage = GetIconFromShell(canonicalName, out shouldDestroy);
                    }
                }
            }

            return hImage;
        }

        public static ImageSource CreateImageSource(IntPtr iconHandle, bool destroy)
        {
            BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(iconHandle, Int32Rect.Empty,
                                                                            BitmapSizeOptions.FromEmptyOptions());
            if (destroy)
                DestroyIcon(iconHandle);

            return bitmapSource;
        }

        private static IntPtr GetIconFromShell(string fileName, out bool shouldDestroy)
        {
            if (!String.IsNullOrEmpty(fileName))
            {
                SHFILEINFO shellFileInfo = new SHFILEINFO();

                uint size = (uint)Marshal.SizeOf(shellFileInfo);

                SHGetFileInfo(fileName, 0, ref shellFileInfo, size, SHGFI_ICON | SHGFI_SMALLICON);

                if (shellFileInfo.hIcon != IntPtr.Zero)
                {
                    shouldDestroy = true;
                    return shellFileInfo.hIcon;
                }
            }

            shouldDestroy = false;
            return IntPtr.Zero;
        }

        private static IntPtr GetIconWithoutImageList(IVsHierarchy hierarchy, uint itemId, out bool shouldDestroy)
        {
            shouldDestroy = false;
            object iconHandleObject;
            int result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_IconHandle, out iconHandleObject);

            if (result == VSConstants.S_OK)
                return new IntPtr(Convert.ToInt64(iconHandleObject));

            return IntPtr.Zero;
        }

        private static IntPtr GetIconWithImageList(IVsHierarchy hierarchy, uint itemId, out bool shouldDestroy)
        {
            object iconIndexObject;
            int result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_IconIndex, out iconIndexObject);

            if (result == VSConstants.S_OK)
            {
                object imageListHandleObject;
                result = hierarchy.GetProperty(Microsoft.VisualStudio.VSConstants.VSITEMID_ROOT,
                                               (int)__VSHPROPID.VSHPROPID_IconImgList, out imageListHandleObject);

                if (result == VSConstants.S_OK)
                {
                    IntPtr imageListHandle = new IntPtr(Convert.ToInt64(imageListHandleObject));
                    int iconIndex = Convert.ToInt32(iconIndexObject);

                    shouldDestroy = true;
                    return ImageList_GetIcon(imageListHandle, iconIndex, ILD_NORMAL);
                }
            }

            shouldDestroy = false;
            return IntPtr.Zero;
        }

        public static BitmapImage LoadImageFromResource(string uri)
        {
            var stream = Application.GetResourceStream(new Uri(uri)).Stream;
            BitmapImage bmp = new BitmapImage();
            //bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.BeginInit();
            bmp.StreamSource = stream;
            bmp.EndInit();
            return bmp;
        }

        #endregion
    }
}
