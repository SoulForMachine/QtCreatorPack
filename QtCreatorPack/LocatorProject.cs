using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Threading;

namespace QtCreatorPack
{
    internal class LocatorProject : IVsHierarchyEvents
    {
        public IVsHierarchy Hierarchy { get; private set; }
        public string Name { get; private set; }
        public List<LocatorProjectItem> Items { get; private set; }

        public static Dispatcher Dispatcher;
        private static Dictionary<IntPtr, ImageSource> _projectIconCache = new Dictionary<IntPtr, ImageSource>();
        public static volatile bool CancelProjectLoad = false;

        private uint _cookie = 0;

        public LocatorProject(IVsHierarchy hierarchy)
        {
            Hierarchy = hierarchy;
            object obj;
            int result = hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out obj);
            if (result == VSConstants.S_OK)
                Name = obj as string;
            else
                Name = string.Empty;
            Items = new List<LocatorProjectItem>();
            ProcessHierarchy(VSConstants.VSITEMID_ROOT, hierarchy);
            Hierarchy.AdviseHierarchyEvents(this, out _cookie);
        }

        public void StopListeningEvents()
        {
            if (_cookie > 0)
                Hierarchy.UnadviseHierarchyEvents(_cookie);
        }

        #region IVsHierarchyEvents

        public int OnItemAdded(uint itemidParent, uint itemidSiblingPrev, uint itemidAdded)
        {
            object addedItemObject;
            if (Hierarchy.GetProperty(itemidAdded, (int)__VSHPROPID.VSHPROPID_ExtObject, out addedItemObject) == VSConstants.S_OK)
            {
                EnvDTE.ProjectItem projectItem = addedItemObject as EnvDTE.ProjectItem;
                if (projectItem != null)
                {
                    if (projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                    {
                        LocatorProjectItem item = new LocatorProjectItem();
                        item.Name = projectItem.Name;
                        item.Path = Utils.PathRelativeTo(projectItem.FileNames[0], projectItem.ContainingProject.FullName);
                        item.Item = projectItem;
                        item.Image = GetProjectItemImage(Hierarchy, itemidAdded);
                        lock (Items)
                        {
                            Items.Add(item);
                        }
                    }
                }
            }

            return VSConstants.S_OK;
        }

        public int OnItemsAppended(uint itemidParent)
        {
            return VSConstants.S_OK;
        }

        public int OnItemDeleted(uint itemid)
        {
            object deletedItemObject;
            if (Hierarchy.GetProperty(itemid, (int)__VSHPROPID.VSHPROPID_ExtObject, out deletedItemObject) == VSConstants.S_OK)
            {
                EnvDTE.ProjectItem projectItem = deletedItemObject as EnvDTE.ProjectItem;
                if (projectItem != null)
                {
                    if (projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                    {
                        lock (Items)
                        {
                            Items.RemoveAll((LocatorProjectItem listItem) =>
                            {
                                return projectItem == listItem.Item;
                            });
                        }
                    }
                }
            }

            return VSConstants.S_OK;
        }

        public int OnPropertyChanged(uint itemid, int propid, uint flags)
        {
            return VSConstants.S_OK;
        }

        public int OnInvalidateItems(uint itemidParent)
        {
            return VSConstants.S_OK;
        }

        public int OnInvalidateIcon(IntPtr hicon)
        {
            return VSConstants.S_OK;
        }

        #endregion

        private void ProcessHierarchy(uint itemId, IVsHierarchy hierarchy)
        {
            if (hierarchy == null || CancelProjectLoad)
                return;

            object obj;
            int result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_ExtObject, out obj);
            if (result == VSConstants.S_OK)
            {
                EnvDTE.ProjectItem projectItem = obj as EnvDTE.ProjectItem;
                if (projectItem != null)
                {
                    if (projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                    {
                        LocatorProjectItem item = new LocatorProjectItem();
                        item.Name = projectItem.Name;
                        item.Path = Utils.PathRelativeTo(projectItem.FileNames[0], projectItem.ContainingProject.FullName);
                        item.Item = projectItem;
                        item.Image = GetProjectItemImage(hierarchy, itemId);
                        Items.Add(item);
                    }
                }
            }

            // Recursively process children, depth first.
            result = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_FirstVisibleChild, out obj);
            if (result == VSConstants.S_OK)
            {
                uint childId = (uint)(Int32)obj;
                while (childId != VSConstants.VSITEMID_NIL)
                {
                    ProcessHierarchy(childId, hierarchy);

                    result = hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextVisibleSibling, out obj);
                    if (result == VSConstants.S_OK)
                    {
                        childId = (uint)(Int32)obj;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // If not found in the cache, this function will load an image and put it in the cache.
        // ImageSources must be created in the main thread (since they will be used by it), so
        // we use the dispatcher to do this asynchronously. Meanwhile we must check the cancel
        // flag periodically in case the main thread is waiting for this thread to finish, to
        // prevent deadlock.
        private static ImageSource GetProjectItemImage(IVsHierarchy hierarchy, uint itemId)
        {
            bool destroy;
            IntPtr hIcon = Utils.GetProjectItemIcon(hierarchy, itemId, out destroy);
            if (hIcon != IntPtr.Zero)
            {
                ImageSource image = null;
                if (_projectIconCache.TryGetValue(hIcon, out image))
                    return image;

                var op = Dispatcher.InvokeAsync(new Func<ImageSource>(() => {
                    return Utils.CreateImageSource(hIcon, destroy);
                }));

                while (op.Wait(TimeSpan.FromMilliseconds(50)) != DispatcherOperationStatus.Completed)
                {
                    if (CancelProjectLoad)
                        return null;
                }

                image = op.Result;

                if (image != null)
                {
                    _projectIconCache.Add(hIcon, image);
                    return image;
                }
            }

            // Get the default image.
            ImageSource defaultImage = null;
            if (!_projectIconCache.TryGetValue(IntPtr.Zero, out defaultImage))
            {
                // Load and add default image to the cache.
                var op = Dispatcher.InvokeAsync(new Func<ImageSource>(() =>
                {
                    return Utils.LoadImageFromResource(@"pack://application:,,,/QtCreatorPack;component/Resources/DefaultProjectItem.png");
                }));

                while (op.Wait(TimeSpan.FromMilliseconds(50)) != DispatcherOperationStatus.Completed)
                {
                    if (CancelProjectLoad)
                        return null;
                }

                defaultImage = op.Result;
                _projectIconCache.Add(IntPtr.Zero, defaultImage);
            }
            return defaultImage;
        }
    }
}
