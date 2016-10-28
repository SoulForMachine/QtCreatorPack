using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace QtCreatorPack
{
    internal abstract class LocatorItem
    {
        public class HeaderData
        {
            public HeaderData(string title, string boundPropertyName, int width)
            {
                Title = title;
                BoundPropertyName = boundPropertyName;
                Width = width;
            }
            public string Title;
            public string BoundPropertyName;
            public int Width;
        }

        public abstract void ExecuteAction();
    }

    internal class LocatorProjectItem : LocatorItem
    {
        private static List<HeaderData> _headerData = new List<HeaderData> {
                new HeaderData("Name", "Name", 600),
                new HeaderData("Path", "Path", 800)
            };

        public static List<HeaderData> HeaderDataList
        {
            get { return _headerData; }
        }

        public override void ExecuteAction()
        {
            try
            {
                Item.Open(EnvDTE.Constants.vsViewKindPrimary).Activate();
            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }
        }

        public EnvDTE.ProjectItem Item { get; set; }
        public uint ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }    // Project relative path.
        public ImageSource Image { get; set; }
    }

    internal class LocatorCodeItem : LocatorItem
    {
        private static List<HeaderData> _headerData = new List<HeaderData> {
                new HeaderData("Code element", "Name", 600),
                new HeaderData("Fully qualified name", "FQName", 400),
                new HeaderData("Comment", "Comment", 800)
            };

        public static List<HeaderData> HeaderDataList
        {
            get { return _headerData; }
        }

        public override void ExecuteAction()
        {
            try
            {
                if (ProjectItem != null)
                {
                    EnvDTE.Window window = ProjectItem.Open(EnvDTE.Constants.vsViewKindPrimary);
                    if (window != null)
                    {
                        window.Activate();
                        EnvDTE.TextSelection sel = (EnvDTE.TextSelection)window.Document.Selection;
                        sel.MoveToAbsoluteOffset(ElementOffset, false);
                    }
                }
                else
                {
                    EnvDTE.Window window = CodeElement.ProjectItem.Open(EnvDTE.Constants.vsViewKindPrimary);
                    if (window != null)
                    {
                        window.Activate();
                        EnvDTE.TextSelection sel = (EnvDTE.TextSelection)window.Document.Selection;
                        sel.MoveToPoint(CodeElement.StartPoint, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print(ex.Message);
            }
        }

        public EnvDTE.CodeElement CodeElement { get; set; }
        public EnvDTE.ProjectItem ProjectItem { get; set; }
        public int ElementOffset { get; set; }
        public string Name { get; set; }
        public string FQName { get; set; }
        public string Comment { get; set; }
        public ImageSource Image { get; set; }
    }
}
