using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading;
using System.Windows.Threading;
using System.Text.RegularExpressions;

namespace QtCreatorPack
{
    internal class Locator : IVsSolutionEvents3
    {
        public class ProjectItem
        {
            public EnvDTE.ProjectItem Item { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }    // Project relative path.
        }

        public class FunctionItem
        {
            public EnvDTE.CodeFunction Function { get; set; }
            public string Name { get; set; }
            public string Signature { get; set; }
        }

        public class FileSearchFinishedEventArgs
        {
            public FileSearchFinishedEventArgs(List<ProjectItem> items)
            {
                Items = items;
            }

            public List<ProjectItem> Items { get; private set; }
        }

        public class FunctionSearchFinishedEventArgs
        {
            public FunctionSearchFinishedEventArgs(List<FunctionItem> items)
            {
                Items = items;
            }

            public List<FunctionItem> Items { get; private set; }
        }

        public delegate void FileSearchFinishedEventHandler(object sender, FileSearchFinishedEventArgs items);
        public delegate void FunctionSearchFinishedEventHandler(object sender, FunctionSearchFinishedEventArgs items);
        public event FileSearchFinishedEventHandler FileSearchFinishedEvent;
        public event FunctionSearchFinishedEventHandler FunctionSearchFinishedEvent;

        private enum MessageType
        {
            SearchString,
            ProjectLoaded,
            ProjectUnloaded,
            ItemAdded,
            ItemRemoved,
            Stop
        }

        private class Message
        {
            public MessageType Type { get; private set; }
            public string Text { get; private set; }
            public IVsHierarchy Hierarchy { get; private set; }

            public static Message SearchString(string str)
            {
                Message msg = new Message();
                msg.Type = MessageType.SearchString;
                msg.Text = str;
                return msg;
            }

            public static Message ProjectLoaded(IVsHierarchy hierarchy)
            {
                Message msg = new Message();
                msg.Type = MessageType.ProjectLoaded;
                msg.Hierarchy = hierarchy;
                return msg;
            }

            public static Message ProjectUnloaded(IVsHierarchy hierarchy)
            {
                Message msg = new Message();
                msg.Type = MessageType.ProjectUnloaded;
                msg.Hierarchy = hierarchy;
                return msg;
            }

            public static Message Stop()
            {
                Message msg = new Message();
                msg.Type = MessageType.Stop;
                return msg;
            }

            private Message() { }
        }

        private class Project : IVsHierarchyEvents
        {
            public IVsHierarchy Hierarchy { get; private set; }
            public string Name { get; private set; }
            public List<ProjectItem> Items { get; private set; }

            private uint _cookie;

            public Project(IVsHierarchy hierarchy)
            {
                Hierarchy = hierarchy;
                object obj;
                int result = hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out obj);
                if (result == VSConstants.S_OK)
                    Name = obj as string;
                else
                    Name = string.Empty;
                Items = new List<ProjectItem>();
                ProcessHierarchy(VSConstants.VSITEMID_ROOT, hierarchy);
                //Items.Sort((ProjectItem item1, ProjectItem item2) => { return item1.Name.CompareTo(item2.Name); });
                Hierarchy.AdviseHierarchyEvents(this, out _cookie);
            }

            public void StopListeningEvents()
            {
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
                            ProjectItem item = new ProjectItem();
                            item.Name = projectItem.Name;
                            item.Path = Utils.PathRelativeTo(projectItem.FileNames[0], projectItem.ContainingProject.FullName);
                            item.Item = projectItem;
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
                                Items.RemoveAll((ProjectItem listItem) =>
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
                if (hierarchy == null)
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
                            ProjectItem item = new ProjectItem();
                            item.Name = projectItem.Name;
                            item.Path = Utils.PathRelativeTo(projectItem.FileNames[0], projectItem.ContainingProject.FullName);
                            item.Item = projectItem;
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
        }

        private volatile bool _interruptWork = false;
        private List<Project> _projectList = new List<Project>();
        private System.Threading.Thread _workerThread;
        private readonly object _workerThreadSync = new object();
        private Message _searchMessage = null;  // This one is processed after the queue is empty.
        private Queue<Message> _messageQueue = new Queue<Message>();
        private Dispatcher _dispatcher;

        public Locator()
        {
            _dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
            _workerThread = new System.Threading.Thread(WorkerThreadFunc);
        }

        public void SearchString(string text)
        {
            if (_workerThread.IsAlive && _projectList.Count > 0)
            {
                lock (_workerThreadSync)
                {
                    _searchMessage = Message.SearchString(text);
                    _interruptWork = true;
                    Monitor.Pulse(_workerThreadSync);
                }
            }
        }

        public void StartWorkerThread()
        {
            if (!_workerThread.IsAlive)
                _workerThread.Start();
        }

        public void StopWorkerThread()
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    _messageQueue.Enqueue(Message.Stop());
                    _interruptWork = true;
                    Monitor.Pulse(_workerThreadSync);
                }

                _workerThread.Join();
            }
        }

        #region IVsSolutionEvents3

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterClosingChildren(IVsHierarchy pHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            ProjectLoaded(pRealHierarchy);
            return VSConstants.S_OK;
        }

        public int OnAfterMergeSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterOpeningChildren(IVsHierarchy pHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            ProjectLoaded(pHierarchy);
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            ProjectUnloaded(pHierarchy);
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeClosingChildren(IVsHierarchy pHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeOpeningChildren(IVsHierarchy pHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            ProjectUnloaded(pRealHierarchy);
            return VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        #endregion

        protected virtual void RaiseFileSearchFinishedEvent(List<ProjectItem> itemList)
        {
            if (FileSearchFinishedEvent != null)
                FileSearchFinishedEvent(this, new FileSearchFinishedEventArgs(itemList));
        }

        protected virtual void RaiseFunctionSearchFinishedEvent(List<FunctionItem> itemList)
        {
            if (FunctionSearchFinishedEvent != null)
                FunctionSearchFinishedEvent(this, new FunctionSearchFinishedEventArgs(itemList));
        }

        private void ProjectLoaded(IVsHierarchy hierarchy)
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    Message msg = Message.ProjectLoaded(hierarchy);
                    _messageQueue.Enqueue(msg);
                    _interruptWork = true;
                    Monitor.Pulse(_workerThreadSync);
                }
            }
        }

        private void ProjectUnloaded(IVsHierarchy hierarchy)
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    Message msg = Message.ProjectUnloaded(hierarchy);
                    _messageQueue.Enqueue(msg);
                    _interruptWork = true;
                    Monitor.Pulse(_workerThreadSync);
                }
            }
        }

        private void WorkerThreadFunc()
        {
            while (true)
            {
                Message message;
                lock (_workerThreadSync)
                {
                    _interruptWork = false;
                    while (_searchMessage == null && _messageQueue.Count == 0)
                        Monitor.Wait(_workerThreadSync);

                    if (_messageQueue.Count > 0)
                    {
                        message = _messageQueue.Dequeue();
                    }
                    else // search message must be set
                    {
                        message = _searchMessage;
                        _searchMessage = null;
                    }
                }

                if (message.Type == MessageType.SearchString)
                {
                    string searchStr = message.Text.Trim();
                    Regex regex = new Regex(@"\.\s+(.+)");
                    Match match = regex.Match(searchStr);
                    if (match.Success)
                    {
                        // Search for functions in currently open code editor.
                        searchStr = match.Groups[0].Value;
                        List<FunctionItem> results = new List<FunctionItem>();

                        // Raise search finished event in user's thread.
                        _dispatcher.BeginInvoke(new Action(() => { RaiseFunctionSearchFinishedEvent(results); }));
                    }
                    else
                    {
                        // Search for files in solution.
                        searchStr = searchStr.ToUpper();
                        List<ProjectItem> results = new List<ProjectItem>();
                        foreach (Project prj in _projectList)
                        {
                            lock (prj.Items)
                            {
                                foreach (ProjectItem item in prj.Items)
                                {
                                    if (item.Name.ToUpper().Contains(searchStr))
                                    {
                                        results.Add(item);
                                    }
                                }
                            }
                        }

                        // Raise search finished event in user's thread.
                        _dispatcher.BeginInvoke(new Action(() => { RaiseFileSearchFinishedEvent(results); }));
                    }
                }
                else if (message.Type == MessageType.ProjectLoaded)
                {
                    Project project = new Project(message.Hierarchy);
                    _projectList.Add(project);
                }
                else if (message.Type == MessageType.ProjectUnloaded)
                {
                    _projectList.RemoveAll((Project prj) => {
                        if (prj.Hierarchy == message.Hierarchy)
                        {
                            prj.StopListeningEvents();
                            return true;
                        }
                        return false;
                    });
                }
                else if (message.Type == MessageType.Stop)
                {
                    break;
                }
            }
        }
    }
}
