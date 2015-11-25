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

            public Message Copy()
            {
                Message msg = new Message();
                msg.Type = Type;
                msg.Text = Text;
                return msg;
            }

            private Message() { }
        }

        private class Project : IVsHierarchyEvents
        {
            public IVsHierarchy Hierarchy { get; private set; }
            public string Name { get; private set; }
            public List<ProjectItem> Items { get; set; }

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
            }

            #region IVsHierarchyEvents

            public int OnItemAdded(uint itemidParent, uint itemidSiblingPrev, uint itemidAdded)
            {
                throw new NotImplementedException();
            }

            public int OnItemsAppended(uint itemidParent)
            {
                throw new NotImplementedException();
            }

            public int OnItemDeleted(uint itemid)
            {
                throw new NotImplementedException();
            }

            public int OnPropertyChanged(uint itemid, int propid, uint flags)
            {
                throw new NotImplementedException();
            }

            public int OnInvalidateItems(uint itemidParent)
            {
                throw new NotImplementedException();
            }

            public int OnInvalidateIcon(IntPtr hicon)
            {
                throw new NotImplementedException();
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
        private Message _message = null;
        private Dispatcher _dispatcher;

        public Locator()
        {
            _dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
            _workerThread = new System.Threading.Thread(WorkerThreadFunc);
        }

        public void UpdateSearch(string text)
        {
            lock (_workerThreadSync)
            {
                _message = Message.SearchString(text);
                _interruptWork = true;
                Monitor.Pulse(_workerThreadSync);
            }
        }

        public void StartWorkerThread()
        {
            _workerThread.Start();
        }

        public void StopWorkerThread()
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    _message = Message.Stop();
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
            lock (_workerThreadSync)
            {
                _message = Message.ProjectLoaded(pRealHierarchy);
                _interruptWork = true;
                Monitor.Pulse(_workerThreadSync);
            }

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
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
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
            lock (_workerThreadSync)
            {
                _message = Message.ProjectUnloaded(pRealHierarchy);
                _interruptWork = true;
                Monitor.Pulse(_workerThreadSync);
            }

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

        private void WorkerThreadFunc()
        {
            while (true)
            {
                Message message;
                lock (_workerThreadSync)
                {
                    _interruptWork = false;
                    while (_message == null)
                        Monitor.Wait(_workerThreadSync);
                    message = _message;
                    _message = null;
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
                        List<FunctionItem> funcItems = new List<FunctionItem>();

                        // Raise search finished event in user's thread.
                        _dispatcher.BeginInvoke(new Action(() => { RaiseFunctionSearchFinishedEvent(funcItems); }));
                    }
                    else
                    {
                        // Search for files in solution.
                        List<ProjectItem> projectItems = new List<ProjectItem>();

                        // Raise search finished event in user's thread.
                        _dispatcher.BeginInvoke(new Action(() => { RaiseFileSearchFinishedEvent(projectItems); }));
                    }
                }
                else if (message.Type == MessageType.ProjectLoaded)
                {
                    Project project = new Project(message.Hierarchy);
                    _projectList.Add(project);
                }
                else if (message.Type == MessageType.ProjectUnloaded)
                {
                    _projectList.RemoveAll((Project prj) => { return prj.Hierarchy == message.Hierarchy; });
                }
                else if (message.Type == MessageType.Stop)
                {
                    break;
                }
            }
        }

        //private void UpdateItemList()
        //{
        //    itemList.Clear();
        //    IEnumHierarchies enumHierarchies;
        //    Guid guid = Guid.Empty;
        //    int result = solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out enumHierarchies);
        //    if (result == VSConstants.S_OK)
        //    {
        //        IVsHierarchy[] hierarchyArray = new IVsHierarchy[1];
        //        uint count;
        //        enumHierarchies.Reset();

        //        while (true)
        //        {
        //            result = enumHierarchies.Next(1, hierarchyArray, out count);
        //            if (result == VSConstants.S_OK && count == 1)
        //            {
        //                IVsHierarchy hierarchy = hierarchyArray[0];
        //                ProcessHierarchy(VSConstants.VSITEMID_ROOT, hierarchy);
        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }
        //    }
        //}
    }
}
