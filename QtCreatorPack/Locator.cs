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
using System.Diagnostics;

namespace QtCreatorPack
{
    internal class Locator : IVsSolutionEvents3
    {
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

            public SearchResultEventArgs(ResultType type, int percent, IEnumerable<LocatorItem> items, IEnumerable<LocatorItem.HeaderData> headerData)
            {
                Type = type;
                Items = items;
                HeaderData = headerData;
                Percent = percent;
            }

            public ResultType Type;
            public IEnumerable<LocatorItem> Items { get; private set; }
            public IEnumerable<LocatorItem.HeaderData> HeaderData { get; private set; }
            public int Percent { get; private set; }
        }

        public delegate void SearchResultEventHandler(object sender, SearchResultEventArgs args);
        public event SearchResultEventHandler SearchResultEvent;

        public class SolutionEventArgs
        {
            public enum EventType
            {
                SolutionLoading,
                SolutionUnloading,
                ProjectLoading,
                ProjectUnloading,
                ProjectFinishedLoading,
                ProjectFinishedUnloading
            }

            public SolutionEventArgs(EventType type, string message)
            {
                Type = type;
                Message = message;
            }

            public string Message { get; set; }
            public EventType Type { get; set; }
        }

        public delegate void SolutionEventHandler(object sender, SolutionEventArgs args);
        public event SolutionEventHandler SolutionEvent;

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
        private List<LocatorProject> _projectList = new List<LocatorProject>();
        private System.Threading.Thread _workerThread;
        private readonly object _workerThreadSync = new object();
        private readonly object _workerThreadIdle = new object();
        private Message _searchMessage = null;  // This one is processed after the queue is empty.
        private Queue<Message> _messageQueue = new Queue<Message>();
        private Dispatcher _dispatcher;         // Dispatcher for the thread that created the Locator.
        private EnvDTE.DTE _dte;
        private string _currentSourceFilePath;
        private string _currentSearchString;
        private Stopwatch _resultFlushStopwatch;
        private List<BitmapSource> _codeIconList = new List<BitmapSource>();

        public Locator()
        {
            _dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            _dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
            _workerThread = new System.Threading.Thread(WorkerThreadFunc);

            // Load images from resource file.
            string[] imagePaths = new string[]
            {
                @"Resources/Structure.png",
                @"Resources/Class.png",
                @"Resources/Union.png",
                @"Resources/Interface.png",
                @"Resources/Enum.png",
                @"Resources/Method.png",
            };

            foreach (string path in imagePaths)
            {
                BitmapImage bmp = Utils.LoadImageFromResource("pack://application:,,,/QtCreatorPack;component/" + path);
                _codeIconList.Add(bmp);
            }

            LocatorProject.Dispatcher = _dispatcher;
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
                    LocatorProject.CancelProjectLoad = true;
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
            // Cancel all pending search and project loading tasks.
            lock (_workerThreadSync)
            {
                _cancelSearch = true;
                LocatorProject.CancelProjectLoad = true;
                _searchMessage = null;

                // Remove project loading tasks from the message queue.
                List<Message> msgList = new List<Message>();
                foreach (Message msg in _messageQueue)
                    if (msg.Type != MessageType.ProjectLoaded)
                        msgList.Add(msg);

                _messageQueue.Clear();
                foreach (Message msg in msgList)
                    _messageQueue.Enqueue(msg);
            }

            RaiseSolutionEventInUserThread(SolutionEventArgs.EventType.SolutionUnloading);
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
            IEnumerable<LocatorItem> items = null, IEnumerable<LocatorItem.HeaderData> headerData = null)
        {
            if (SearchResultEvent != null)
                SearchResultEvent(this, new SearchResultEventArgs(type, percent, items, headerData));
        }

        protected virtual void RaiseSolutionEvent(SolutionEventArgs.EventType type, string message = "")
        {
            if (SolutionEvent != null)
                SolutionEvent(this, new SolutionEventArgs(type, message));
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
                        LocatorProject.CancelProjectLoad = false;
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
                        searchStr = match.Groups[1].Value.ToUpper().TrimStart();
                        SearchCodeElements(searchStr);
                    }
                    else
                    {
                        if (searchStr.Length > 0)
                            SearchFilesInSolution(searchStr);
                        else
                            RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Error, 0);
                    }
                }
                else if (message.Type == MessageType.ProjectLoaded)
                {
                    object obj;
                    string name;
                    int result = message.Hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out obj);
                    if (result == VSConstants.S_OK)
                        name = obj as string;
                    else
                        name = string.Empty;

                    RaiseSolutionEventInUserThread(SolutionEventArgs.EventType.ProjectLoading, "Loading project " + name);
                    LocatorProject project = new LocatorProject(message.Hierarchy);
                    _projectList.Add(project);
                    RaiseSolutionEventInUserThread(SolutionEventArgs.EventType.ProjectFinishedLoading);
                }
                else if (message.Type == MessageType.ProjectUnloaded)
                {
                    object obj;
                    string name;
                    int result = message.Hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out obj);
                    if (result == VSConstants.S_OK)
                        name = obj as string;
                    else
                        name = string.Empty;

                    RaiseSolutionEventInUserThread(SolutionEventArgs.EventType.ProjectUnloading, "Unloading project " + name);
                    _projectList.RemoveAll((LocatorProject prj) => {
                        if (prj.Hierarchy == message.Hierarchy)
                        {
                            prj.StopListeningEvents();
                            return true;
                        }
                        return false;
                    });
                    RaiseSolutionEventInUserThread(SolutionEventArgs.EventType.ProjectFinishedUnloading);
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
                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.HeaderData, 0, null, LocatorProjectItem.HeaderDataList);

                searchStr = searchStr.ToUpper();
                List<LocatorProjectItem> results = new List<LocatorProjectItem>();
                int count = 0;
                int totalItemCount = 0;
                int prevPercent = -1;
                _resultFlushStopwatch = System.Diagnostics.Stopwatch.StartNew();

                foreach (LocatorProject prj in _projectList)
                    totalItemCount += prj.Items.Count;

                foreach (LocatorProject prj in _projectList)
                {
                    if (_cancelSearch)
                        break;

                    lock (prj.Items)
                    {
                        foreach (LocatorProjectItem item in prj.Items)
                        {
                            if (_cancelSearch)
                                break;

                            int percent = (int)Math.Round(++count / (float)totalItemCount * 100.0f);
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
                                List<LocatorProjectItem> toSend = new List<LocatorProjectItem>(results);
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
                List<LocatorCodeItem> results = new List<LocatorCodeItem>();

                if (_dte.ActiveDocument != null &&
                    _dte.ActiveDocument.ProjectItem.FileCodeModel != null)
                {
                    _resultFlushStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.HeaderData, -1, null, LocatorCodeItem.HeaderDataList);
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Progress, -1);

                    try
                    {
                        _currentSourceFilePath = _dte.ActiveDocument.ProjectItem.FileNames[0];
                        _currentSearchString = searchStr;
                        GetCodeElements(results, _dte.ActiveDocument.ProjectItem.FileCodeModel.CodeElements);
                    }
                    catch(Exception ex)
                    {
                        Debug.Print(ex.Message);
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
            else
            {
                RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Error, 0);
            }
        }

        private void GetCodeElements(List<LocatorCodeItem> results, CodeElements codeElements)
        {
            if (codeElements == null)
                return;

            foreach (CodeElement ce in codeElements)
            {
                if (_cancelSearch)
                    return;

                bool emptySearchStr = _currentSearchString.Length == 0;

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
                                if (emptySearchStr || str.Name.ToUpper().Contains(_currentSearchString))
                                {
                                    LocatorCodeItem item = new LocatorCodeItem();
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
                                if (emptySearchStr || cls.Name.ToUpper().Contains(_currentSearchString))
                                {
                                    LocatorCodeItem item = new LocatorCodeItem();
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
                    CodeFunction f = ce as CodeFunction;
                    if (f != null)
                    {
                        if (emptySearchStr || f.Name.ToUpper().Contains(_currentSearchString))
                        {
                            LocatorCodeItem item = new LocatorCodeItem();
                            item.CodeElement = ce;
                            item.Name = f.get_Prototype((int)vsCMPrototype.vsCMPrototypeParamTypes);
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
                    CodeEnum enm = ce as CodeEnum;
                    if (enm != null)
                    {
                        if (emptySearchStr || enm.Name.ToUpper().Contains(_currentSearchString))
                        {
                            VCCodeEnum vcEnm = enm as VCCodeEnum;

                            // Skip forward declarations
                            if (vcEnm == null || vcEnm.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                LocatorCodeItem item = new LocatorCodeItem();
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
                    VCCodeUnion vcUn = ce as VCCodeUnion;
                    if (vcUn != null)
                    {
                        if (emptySearchStr || vcUn.Name.ToUpper().Contains(_currentSearchString))
                        {
                            // Skip forward declarations
                            if (vcUn.Location.Equals(_currentSourceFilePath, StringComparison.InvariantCultureIgnoreCase))
                            {
                                LocatorCodeItem item = new LocatorCodeItem();
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
                        if (emptySearchStr || intf.Name.ToUpper().Contains(_currentSearchString))
                        {
                            LocatorCodeItem item = new LocatorCodeItem();
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
                    List<LocatorCodeItem> toSend = new List<LocatorCodeItem>(results);
                    RaiseSearchResultEventInUserThread(SearchResultEventArgs.ResultType.Data, -1, toSend);
                    results.Clear();
                    _resultFlushStopwatch.Restart();
                }
            }
        }

        private void GetCppInfo(CodeElement ce, LocatorCodeItem item)
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
            IEnumerable<LocatorItem> items = null, IEnumerable<LocatorItem.HeaderData> headerData = null)
        {
            _dispatcher.BeginInvoke(new Action(() => { RaiseSearchResultEvent(type, percent, items, headerData); }));
        }

        private void RaiseSolutionEventInUserThread(SolutionEventArgs.EventType type, string message = "")
        {
            _dispatcher.BeginInvoke(new Action(() => { RaiseSolutionEvent(type, message); }));
        }
    }
}
