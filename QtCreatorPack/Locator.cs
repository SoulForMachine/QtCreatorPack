using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using Microsoft.VisualStudio.VCCodeModel;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QtCreatorPack
{
    internal class Locator : IVsSolutionEvents3
    {
        public abstract class Item
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

        public class ProjectItem : Item
        {
            private static List<HeaderData> _headerData = new List<HeaderData> {
                new HeaderData("Name", "Name", 600),
                new HeaderData("Path", "Path", 800)
            };
            private ImageSource _image;

            public static List<HeaderData> HeaderDataList
            {
                get { return _headerData; }
            }

            public override void ExecuteAction()
            {
                Item.Open(EnvDTE.Constants.vsViewKindPrimary).Activate();
            }

            public EnvDTE.ProjectItem Item { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }    // Project relative path.
            public ImageSource Image { get; set; }
        }

        public class CodeItem : Item
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

            public EnvDTE.CodeElement CodeElement { get; set; }
            public EnvDTE.ProjectItem ProjectItem { get; set; }
            public int ElementOffset { get; set; }
            public string Name { get; set; }
            public string FQName { get; set; }
            public string Comment { get; set; }
            public ImageSource Image { get; set; }
        }

        public class SearchResultEventArgs
        {
            public enum ResultType
            {
                HeaderData,
                Data,
                Progress,
                Error,
                Canceled,
                Finished
            }

            public SearchResultEventArgs(ResultType type, int percent, IEnumerable<Item> items, IEnumerable<Item.HeaderData> headerData)
            {
                Type = type;
                Items = items;
                HeaderData = headerData;
                Percent = percent;
            }

            public ResultType Type;
            public IEnumerable<Item> Items { get; private set; }
            public IEnumerable<Item.HeaderData> HeaderData { get; private set; }
            public int Percent { get; private set; }
        }

        public delegate void SearchResultEventHandler(object sender, SearchResultEventArgs args);
        public event SearchResultEventHandler SearchResultEvent;

        private enum MessageType
        {
            SearchString,
            ProjectLoaded,
            ProjectUnloaded,
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

            public static Dispatcher Dispatcher;

            private uint _cookie;
            private static Dictionary<IntPtr, ImageSource> _projectIconCache = new Dictionary<IntPtr, ImageSource>();

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

            private static ImageSource GetProjectItemImage(IVsHierarchy hierarchy, uint itemId)
            {
                bool destroy;
                IntPtr hIcon = Utils.GetProjectItemIcon(hierarchy, itemId, out destroy);
                if (hIcon != IntPtr.Zero)
                {
                    ImageSource image;
                    if (_projectIconCache.TryGetValue(hIcon, out image))
                        return image;

                    image = Dispatcher.Invoke(new Func<ImageSource>(() => {
                        return Utils.CreateImageSource(hIcon, destroy);
                    }));
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
                    defaultImage = Dispatcher.Invoke(new Func<ImageSource>(() =>
                    {
                        return Utils.LoadImageFromResource(@"Resources/DefaultProjectItem.png");
                    }));
                    _projectIconCache.Add(IntPtr.Zero, defaultImage);
                }
                return defaultImage;
            }
        }

        private enum WorkerThreadState
        {
            NotStarted,
            Idle,
            Working
        }

        private const int CodeIcon_Struct = 0;
        private const int CodeIcon_Class = 1;
        private const int CodeIcon_Union = 2;
        private const int CodeIcon_Interface = 3;
        private const int CodeIcon_Enum = 4;
        private const int CodeIcon_Function = 5;

        private const int RESULT_FLUSH_TIMEOUT = 300;   // milliseconds

        private volatile bool _cancelSearch = false;
        private volatile WorkerThreadState _workerState = WorkerThreadState.NotStarted;
        private List<Project> _projectList = new List<Project>();
        private System.Threading.Thread _workerThread;
        private readonly object _workerThreadSync = new object();
        private readonly object _workerThreadIdle = new object();
        private Message _searchMessage = null;  // This one is processed after the queue is empty.
        private Queue<Message> _messageQueue = new Queue<Message>();
        private Dispatcher _dispatcher;         // Dispatcher for the thread that created the Locator.
        private EnvDTE.DTE _dte;
        private string _currentSourceFilePath;
        private string _currentSearchString;
        private System.Diagnostics.Stopwatch _resultFlushStopwatch;
        private List<BitmapSource> _codeIconList = new List<BitmapSource>();

        public Locator()
        {
            _dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            _dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
            _workerThread = new System.Threading.Thread(WorkerThreadFunc);

            // Load images from resource file.
            string[] imageUris = new string[]
            {
                @"Resources/Structure.png",
                @"Resources/Class.png",
                @"Resources/Union.png",
                @"Resources/Interface.png",
                @"Resources/Enum.png",
                @"Resources/Method.png",
            };

            foreach (string uri in imageUris)
            {
                BitmapImage bmp = Utils.LoadImageFromResource(uri);
                _codeIconList.Add(bmp);
            }

            Project.Dispatcher = _dispatcher;
        }

        public void SearchString(string text)
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    _searchMessage = Message.SearchString(text);
                    _cancelSearch = true;
                    Monitor.Pulse(_workerThreadSync);
                }
            }
        }

        public void CancelSearch(bool wait = false)
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    _cancelSearch = true;

                    if (wait)
                    {
                        lock (_workerThreadIdle)
                        {
                            while (_workerState == WorkerThreadState.Working)
                                Monitor.Wait(_workerThreadIdle);
                        }
                    }
                }
            }
        }

        public void StartWorkerThread()
        {
            if (!_workerThread.IsAlive)
            {
                _workerState = WorkerThreadState.Idle;
                _workerThread.Start();
            }
        }

        public void StopWorkerThread()
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    _messageQueue.Enqueue(Message.Stop());
                    _cancelSearch = true;
                    Monitor.Pulse(_workerThreadSync);
                }

                _workerThread.Join();
                _workerState = WorkerThreadState.NotStarted;
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

        protected virtual void RaiseSearchResultEvent(
            SearchResultEventArgs.ResultType type, int percent,
            IEnumerable<Item> items = null, IEnumerable<Item.HeaderData> headerData = null)
        {
            if (SearchResultEvent != null)
                SearchResultEvent(this, new SearchResultEventArgs(type, percent, items, headerData));
        }

        private void ProjectLoaded(IVsHierarchy hierarchy)
        {
            if (_workerThread.IsAlive)
            {
                lock (_workerThreadSync)
                {
                    Message msg = Message.ProjectLoaded(hierarchy);
                    _messageQueue.Enqueue(msg);
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
                    while (_searchMessage == null && _messageQueue.Count == 0)
                    {
                        lock (_workerThreadIdle)
                        {
                            _workerState = WorkerThreadState.Idle;
                            Monitor.Pulse(_workerThreadIdle);
                        }
                        Monitor.Wait(_workerThreadSync);
                    }

                    _workerState = WorkerThreadState.Working;

                    if (_messageQueue.Count > 0)
                    {
                        message = _messageQueue.Dequeue();
                    }
                    else // search message must be set
                    {
                        message = _searchMessage;
                        _searchMessage = null;
                        _cancelSearch = false;
                    }
                }

                if (message.Type == MessageType.SearchString)
                {
                    string searchStr = message.Text.TrimStart();
                    Regex regex = new Regex(@"\.\s+(.*)");
                    Match match = regex.Match(searchStr);
                    if (match.Success)
                    {
                        searchStr = match.Groups[1].Value.ToUpper();
                        SearchCodeElements(searchStr);
                    }
                    else
                    {
                        SearchFilesInSolution(searchStr);
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
                    lock (_workerThreadIdle)
                    {
                        _workerState = WorkerThreadState.Idle;
                        Monitor.Pulse(_workerThreadIdle);
                    }
                    break;
                }
            }
        }

        private void SearchFilesInSolution(string searchStr)
        {
            if (_projectList.Count > 0)
            {
                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.HeaderData, 0, null, ProjectItem.HeaderDataList);

                searchStr = searchStr.ToUpper();
                List<ProjectItem> results = new List<ProjectItem>();
                int count = 0;
                int totalItemCount = 0;
                int prevPercent = -1;
                _resultFlushStopwatch = System.Diagnostics.Stopwatch.StartNew();

                foreach (Project prj in _projectList)
                    totalItemCount += prj.Items.Count;

                foreach (Project prj in _projectList)
                {
                    if (_cancelSearch)
                        break;

                    lock (prj.Items)
                    {
                        foreach (ProjectItem item in prj.Items)
                        {
                            if (_cancelSearch)
                                break;

                            int percent = (int)Math.Round(++count / (float)totalItemCount);
                            if (percent != prevPercent)
                            {
                                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Progress, percent);
                                prevPercent = percent;
                            }

                            if (item.Name.ToUpper().Contains(searchStr))
                            {
                                results.Add(item);
                            }

                            if (_resultFlushStopwatch.ElapsedMilliseconds > RESULT_FLUSH_TIMEOUT)
                            {
                                // Taking too long, flush what we got alredy.
                                List<ProjectItem> toSend = new List<ProjectItem>(results);
                                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Data, percent, toSend);
                                results.Clear();
                                _resultFlushStopwatch.Restart();
                            }
                        }
                    }
                }

                if (_cancelSearch)
                {
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Canceled, 0);
                }
                else
                {
                    if (results.Count > 0)
                        RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Data, 100, results);
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Finished, 100);
                }
                _resultFlushStopwatch = null;
            }
            else
            {
                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Error, 0);
            }
        }

        private void SearchCodeElements(string searchStr)
        {
            if (_dte != null)
            {
                // Search for functions in currently open code editor.
                List<CodeItem> results = new List<CodeItem>();

                if (_dte.ActiveDocument != null &&
                    _dte.ActiveDocument.ProjectItem.FileCodeModel != null)
                {
                    _resultFlushStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.HeaderData, -1, null, CodeItem.HeaderDataList);
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Progress, -1);

                    _currentSourceFilePath = _dte.ActiveDocument.ProjectItem.FileNames[0];
                    _currentSearchString = searchStr;
                    GetCodeElements(results, _dte.ActiveDocument.ProjectItem.FileCodeModel.CodeElements);

                    if (_cancelSearch)
                    {
                        RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Canceled, 0);
                    }
                    else
                    {
                        if (results.Count > 0)
                            RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Data, 100, results);
                        RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Finished, 100);
                    }
                    _resultFlushStopwatch = null;
                }
                else
                {
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Error, 0);
                }
            }
            else
            {
                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Error, 0);
            }
        }

        private void GetCodeElements(List<CodeItem> results, CodeElements codeElements)
        {
            if (codeElements == null)
                return;

            foreach (CodeElement ce in codeElements)
            {
                if (_cancelSearch)
                    return;

                bool match = _currentSearchString.Length == 0 || ce.Name.ToUpper().Contains(_currentSearchString);

                if (ce.Kind == vsCMElement.vsCMElementNamespace)
                {
                    CodeNamespace nsp = ce as CodeNamespace;
                    GetCodeElements(results, nsp.Children);
                }
                else if (ce.Kind == vsCMElement.vsCMElementStruct)
                {
                    CodeStruct str = ce as CodeStruct;
                    if (str != null)
                    {
                        VCCodeStruct vcStr = str as VCCodeStruct;
                        if (vcStr != null)
                        {
                            // Skip forward declarations
                            if (vcStr.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                if (match)
                                {
                                    CodeItem item = new CodeItem();
                                    item.CodeElement = ce;
                                    item.Name = str.Name;
                                    item.FQName = str.FullName;
                                    item.Comment = str.Comment;
                                    item.Image = _codeIconList[CodeIcon_Struct];
                                    results.Add(item);
                                }

                                GetCodeElements(results, vcStr.Children);
                            }
                        }
                        else
                        {
                            GetCodeElements(results, str.Children);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementClass)
                {
                    CodeClass cls = ce as CodeClass;
                    if (cls != null)
                    {
                        VCCodeClass vcCls = cls as VCCodeClass;
                        if (vcCls != null)
                        {
                            // Skip forward declarations
                            if (vcCls.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                if (match)
                                {
                                    CodeItem item = new CodeItem();
                                    item.CodeElement = ce;
                                    item.Name = cls.Name;
                                    item.FQName = cls.FullName;
                                    item.Comment = cls.Comment;
                                    item.Image = _codeIconList[CodeIcon_Class];
                                    results.Add(item);
                                }

                                GetCodeElements(results, vcCls.Children);
                            }
                        }
                        else
                        {
                            GetCodeElements(results, cls.Children);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementFunction)
                {
                    if (match)
                    {
                        CodeFunction f = ce as CodeFunction;
                        if (f != null)
                        {
                            CodeItem item = new CodeItem();
                            item.CodeElement = ce;
                            item.Name = f.get_Prototype((int)vsCMPrototype.vsCMPrototypeUniqueSignature);
                            item.FQName = f.FullName;
                            item.Comment = f.Comment;
                            item.Image = _codeIconList[CodeIcon_Function];
                            GetCppInfo(ce, item);
                            results.Add(item);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementEnum)
                {
                    if (match)
                    {
                        CodeEnum enm = ce as CodeEnum;
                        if (enm != null)
                        {
                            VCCodeEnum vcEnm = enm as VCCodeEnum;

                            // Skip forward declarations
                            if (vcEnm == null || vcEnm.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                CodeItem item = new CodeItem();
                                item.CodeElement = ce;
                                item.Name = enm.Name;
                                item.FQName = enm.FullName;
                                item.Comment = enm.Comment;
                                item.Image = _codeIconList[CodeIcon_Enum];
                                results.Add(item);
                            }
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementUnion)
                {
                    if (match)
                    {
                        VCCodeUnion vcUn = ce as VCCodeUnion;
                        if (vcUn != null)
                        {
                            // Skip forward declarations
                            if (vcUn.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                CodeItem item = new CodeItem();
                                item.CodeElement = ce;
                                item.Name = vcUn.Name;
                                item.FQName = vcUn.FullName;
                                item.Comment = vcUn.Comment;
                                item.Image = _codeIconList[CodeIcon_Union];
                                results.Add(item);
                            }
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
                            item.Image = _codeIconList[CodeIcon_Interface];
                            results.Add(item);
                        }

                        GetCodeElements(results, intf.Children);
                    }
                }

                if (_resultFlushStopwatch.ElapsedMilliseconds > RESULT_FLUSH_TIMEOUT)
                {
                    // Taking too long, flush what we got alredy.
                    List<CodeItem> toSend = new List<CodeItem>(results);
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Data, -1, toSend);
                    results.Clear();
                    _resultFlushStopwatch.Restart();
                }
            }
        }

        private void GetCppInfo(CodeElement ce, CodeItem item)
        {
            item.ProjectItem = null;
            item.ElementOffset = 1;
            VCCodeElement vcEl = ce as VCCodeElement;

            if (vcEl != null)
            {
                try
                {
                    TextPoint def = vcEl.StartPointOf[vsCMPart.vsCMPartName, vsCMWhere.vsCMWhereDefinition];
                    if (def.Parent.Parent.FullName.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                    {
                        item.ProjectItem = def.Parent.Parent.ProjectItem;
                        item.ElementOffset = def.AbsoluteCharOffset;
                    }
                }
                catch
                { }

                if (item.ProjectItem == null)
                {
                    try
                    {
                        TextPoint decl = vcEl.StartPointOf[vsCMPart.vsCMPartName, vsCMWhere.vsCMWhereDeclaration];
                        if (decl.Parent.Parent.FullName.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                        {
                            item.ProjectItem = decl.Parent.Parent.ProjectItem;
                            item.ElementOffset = decl.AbsoluteCharOffset;
                        }
                    }
                    catch
                    { }
                }
            }
        }

        private void RaiseSearchResultEventInUserThread(
            SearchResultEventArgs.ResultType type, int percent,
            IEnumerable<Item> items = null, IEnumerable<Item.HeaderData> headerData = null)
        {
            _dispatcher.BeginInvoke(new Action(() => { RaiseSearchResultEvent(type, percent, items, headerData); }));
        }
    }
}
