using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Shell;
using EnvDTE;

namespace QtCreatorPack
{
    internal class Locator : IVsSolutionEvents3
    {
        public abstract class Item
        {
            public class HeaderData
            {
                public HeaderData(string title, string boundPropertyName)
                {
                    Title = title;
                    BoundPropertyName = boundPropertyName;
                }
                public string Title;
                public string BoundPropertyName;
            }

            public abstract List<HeaderData> GetHeaderData();
            public abstract void ExecuteAction();
        }

        public class ProjectItem : Item
        {
            private static List<HeaderData> _headerData = new List<HeaderData> {
                new HeaderData("Name", "Name"),
                new HeaderData("Path", "Path")
            };

            public override List<HeaderData> GetHeaderData()
            {
                return _headerData;
            }

            public override void ExecuteAction()
            {
                Item.Open(EnvDTE.Constants.vsViewKindPrimary).Activate();
            }

            public EnvDTE.ProjectItem Item { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }    // Project relative path.
        }

        public class CodeItem : Item
        {
            private static List<HeaderData> _headerData = new List<HeaderData> {
                new HeaderData("Code element", "Name"),
                new HeaderData("Fully qualified name", "FQName"),
                new HeaderData("Comment", "Comment")
            };

            public override List<HeaderData> GetHeaderData()
            {
                return _headerData;
            }

            public override void ExecuteAction()
            {
                EnvDTE.Window window = CodeElement.ProjectItem.Open(EnvDTE.Constants.vsViewKindPrimary);
                if (window != null)
                {
                    window.Activate();
                    EnvDTE.TextSelection sel = (EnvDTE.TextSelection)window.Document.Selection;
                    sel.MoveToPoint(CodeElement.StartPoint, false);
                }
            }

            public EnvDTE.CodeElement CodeElement { get; set; }
            public string Name { get; set; }
            public string FQName { get; set; }
            public string Comment{ get; set; }
        }

        public class SearchFinishedEventArgs
        {
            public SearchFinishedEventArgs(IEnumerable<Item> items)
            {
                Items = items;
            }

            public IEnumerable<Item> Items { get; private set; }
        }

        public delegate void SearchFinishedEventHandler(object sender, SearchFinishedEventArgs args);
        public event SearchFinishedEventHandler SearchFinishedEvent;

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
        private EnvDTE.DTE _dte;

        public Locator()
        {
            _dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
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

        protected virtual void RaiseSearchFinishedEvent(IEnumerable<Item> itemList)
        {
            if (SearchFinishedEvent != null)
                SearchFinishedEvent(this, new SearchFinishedEventArgs(itemList));
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

        private void DebugPrint(string msg)
        {
            System.Diagnostics.Debug.Print(msg);
        }

        private void GetCodeElements(List<CodeItem> results, string searchStr, CodeElements codeElements)
        {
            if (codeElements == null)
                return;

            foreach (CodeElement ce in codeElements)
            {
                bool match = searchStr.Length == 0 || ce.Name.ToUpper().Contains(searchStr);

                if (ce.Kind == vsCMElement.vsCMElementNamespace)
                {
                    CodeNamespace nsp = ce as CodeNamespace;
                    GetCodeElements(results, searchStr, nsp.Members);
                }
                else if (ce.Kind == vsCMElement.vsCMElementStruct)
                {
                    CodeStruct str = ce as CodeStruct;
                    if (str != null)
                    {
                        if (match)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = str.Name;
                            item.FQName = str.FullName;
                            item.Comment = str.Comment;
                            results.Add(item);
                        }

                        if (str.Children != null)
                            GetCodeElements(results, searchStr, str.Members);
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementClass)
                {
                    CodeClass cls = ce as CodeClass;
                    if (cls != null)
                    {
                        if (match)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = cls.Name;
                            item.FQName = cls.FullName;
                            item.Comment = cls.Comment;
                            results.Add(item);
                        }

                        if (cls.Children != null)
                            GetCodeElements(results, searchStr, cls.Members);
                    }
                    else
                    {
                        CodeStruct str = ce as CodeStruct;
                        if (str != null)
                        {
                            if (match)
                            {
                                CodeItem item = new CodeItem();
                                item.CodeElement = ce;
                                item.Name = str.Name;
                                item.FQName = str.FullName;
                                item.Comment = str.Comment;
                                results.Add(item);
                            }

                            if (str.Children != null)
                                GetCodeElements(results, searchStr, str.Members);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementFunction)
                {
                    CodeFunction f = ce as CodeFunction;
                    if (f != null)
                    {
                        if (match)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = f.get_Prototype((int)vsCMPrototype.vsCMPrototypeUniqueSignature);
                            item.FQName = f.FullName;
                            item.Comment = f.Comment;
                            results.Add(item);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementEnum)
                {
                    CodeEnum enm = ce as CodeEnum;
                    if (enm != null)
                    {
                        if (match)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = enm.Name;
                            item.FQName = enm.FullName;
                            item.Comment = enm.Comment;
                            results.Add(item);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementInterface)
                {
                    CodeInterface intf = ce as CodeInterface;
                    if (intf != null)
                    {
                        if (match)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = intf.Name;
                            item.FQName = intf.FullName;
                            item.Comment = intf.Comment;
                            results.Add(item);
                        }

                        GetCodeElements(results, searchStr, intf.Members);
                    }
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
                    string searchStr = message.Text.TrimStart();
                    Regex regex = new Regex(@"\.\s+(.*)");
                    Match match = regex.Match(searchStr);
                    if (match.Success)
                    {
                        if (_dte != null)
                        {
                            // Search for functions in currently open code editor.
                            searchStr = match.Groups[1].Value.ToUpper();
                            List<CodeItem> results = new List<CodeItem>();

                            if (_dte.ActiveDocument != null)
                            {
                                GetCodeElements(results, searchStr, _dte.ActiveDocument.ProjectItem.FileCodeModel.CodeElements);
                            }

                            // Raise search finished event in user's thread.
                            _dispatcher.BeginInvoke(new Action(() => { RaiseSearchFinishedEvent(results); }));
                        }
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
                        _dispatcher.BeginInvoke(new Action(() => { RaiseSearchFinishedEvent(results); }));
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
