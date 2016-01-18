using EnvDTE;
using Microsoft.VisualStudio.VCCodeModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Media.Imaging;

namespace QtCreatorPack
{
    internal class CodeFinder : FinderBase
    {
        private const int CodeIcon_Struct = 0;
        private const int CodeIcon_Class = 1;
        private const int CodeIcon_Union = 2;
        private const int CodeIcon_Interface = 3;
        private const int CodeIcon_Enum = 4;
        private const int CodeIcon_Function = 5;
        private const int RESULTS_FLUSH_LIMIT = 1000;

        private EnvDTE80.DTE2 _dte;
        private EnvDTE80.Events2 _events;
        private EnvDTE80.CodeModelEvents _codeEvents;
        private WindowEvents _windowEvents;
        private System.Threading.Thread _updaterThread;
        private readonly object _updaterThreadSync = new object();
        private readonly object _updaterThreadResultSync = new object();
        private Queue<Message> _messageQueue = new Queue<Message>();
        private bool _initialized = false;
        private bool _cancelCodeScan = false;
        private List<BitmapSource> _codeIconList = new List<BitmapSource>();
        private const int MAX_DICT_ENTRIES = 50;
        private Dictionary<string, CodeItemList> _codeItemCache;

        private enum MessageType
        {
            ScanFile,
            ElementAdded,
            ElementChanged,
            ElementDeleted,
            Stop
        }

        private class Message
        {
            public MessageType Type { get; private set; }
            public CodeElement CodeElement { get; private set; }
            public EnvDTE80.vsCMChangeKind ChangeKind { get; private set; }
            public ProjectItem ProjectItem { get; private set; }
            public bool? Result { get; set; }

            public static Message ScanFile(ProjectItem item)
            {
                Message msg = new Message();
                msg.Type = MessageType.ScanFile;
                msg.ProjectItem = item;
                return msg;
            }

            public static Message ElementAdded(CodeElement codeElement)
            {
                Message msg = new Message();
                msg.Type = MessageType.ElementAdded;
                msg.CodeElement = codeElement;
                return msg;
            }

            public static Message ElementChanged(CodeElement codeElement, EnvDTE80.vsCMChangeKind changeKind)
            {
                Message msg = new Message();
                msg.Type = MessageType.ElementChanged;
                msg.CodeElement = codeElement;
                msg.ChangeKind = changeKind;
                return msg;
            }

            public static Message ElementDeleted(CodeElement codeElement)
            {
                Message msg = new Message();
                msg.Type = MessageType.ElementDeleted;
                msg.CodeElement = codeElement;
                return msg;
            }

            public static Message Stop()
            {
                Message msg = new Message();
                msg.Type = MessageType.Stop;
                return msg;
            }
        }

        private class CodeItemList
        {
            public List<LocatorCodeItem> Items;
            public long Timestamp;
        }

        public bool Init(EnvDTE.DTE dte)
        {
            if (_initialized)
                return false;

            try
            {
                _dte = dte as EnvDTE80.DTE2;
                _events = _dte.Events as EnvDTE80.Events2;

                _codeEvents = _events.CodeModelEvents;
                _codeEvents.ElementAdded += _codeEvents_ElementAdded;
                _codeEvents.ElementChanged += _codeEvents_ElementChanged;
                _codeEvents.ElementDeleted += _codeEvents_ElementDeleted;

                _windowEvents = _events.WindowEvents;
                _windowEvents.WindowActivated += _windowEvents_WindowActivated;
                _windowEvents.WindowClosing += _windowEvents_WindowClosing;
            }
            catch
            {
                return false;
            }

            if (_codeEvents != null)
            {
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

                // Start the update thread.
                _updaterThread = new System.Threading.Thread(UpdaterThreadFunc);
                _updaterThread.Start();
                _initialized = true;
            }

            return _initialized;
        }

        public void Deinit()
        {
            if (_updaterThread.IsAlive)
            {
                lock (_updaterThreadSync)
                {
                    _messageQueue.Enqueue(Message.Stop());
                    Monitor.Pulse(_updaterThreadSync);
                }

                _updaterThread.Join();
                _initialized = false;
            }
        }

        public void SearchString(string searchStr, ProjectItem projectItem, SearchResultsCallback resultsCallback, SearchProgressCallback progressCallback, Cancelation cancelation)
        {
            string filePath = projectItem.FileNames[0];
            CodeItemList codeItemList = null;
            if (!_codeItemCache.TryGetValue(filePath, out codeItemList))
            {
                progressCallback(-1);
                Message message = Message.ScanFile(projectItem);

                lock (_updaterThreadSync)
                {
                    _messageQueue.Enqueue(message);
                    Monitor.Pulse(_updaterThreadSync);
                }

                lock (_updaterThreadResultSync)
                {
                    while (message.Result == null)
                    {
                        Monitor.Wait(_updaterThreadResultSync);
                        if (cancelation.Cancel)
                            return;
                    }
                }

                if (message.Result == false || !_codeItemCache.TryGetValue(filePath, out codeItemList))
                {
                    return;
                }
            }

            if (codeItemList != null)
            {
                List<LocatorCodeItem> results = new List<LocatorCodeItem>();
                int lastPercent = -1;
                int count = 0;
                int totalItemCount = codeItemList.Items.Count;

                foreach (LocatorCodeItem codeItem in codeItemList.Items)
                {
                    if (cancelation.Cancel)
                        break;

                    int percent = (int)Math.Round(++count / (float)totalItemCount * 100.0f);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progressCallback(percent);
                    }

                    if (codeItem.Name.ToUpper().Contains(searchStr))
                    {
                        results.Add(codeItem);

                        if(results.Count > RESULTS_FLUSH_LIMIT)
                        {
                            resultsCallback(results);
                            results.Clear();
                        }
                    }
                }

                if (results.Count > 0)
                    resultsCallback(results);

                codeItemList.Timestamp = Stopwatch.GetTimestamp();
            }
        }

        private void _codeEvents_ElementAdded(CodeElement element)
        {
            lock (_updaterThreadSync)
            {
                try
                {
                    _messageQueue.Enqueue(Message.ElementAdded(element));
                    Monitor.Pulse(_updaterThreadSync);
                }
                catch { }
            }
        }

        private void _codeEvents_ElementChanged(CodeElement element, EnvDTE80.vsCMChangeKind changeKind)
        {
            lock (_updaterThreadSync)
            {
                try
                {
                    Debug.Print("=============  " + element.ProjectItem.FileNames[0] + " change " + changeKind.ToString());
                    _messageQueue.Enqueue(Message.ElementChanged(element, changeKind));
                    Monitor.Pulse(_updaterThreadSync);
                }
                catch { }
            }
        }

        private void _codeEvents_ElementDeleted(object parent, CodeElement element)
        {
            lock (_updaterThreadSync)
            {
                try
                {
                    EnvDTE80.CodeElement2 el2 = element as EnvDTE80.CodeElement2;
                    Debug.Print("=============  " + el2.Name);
                    _messageQueue.Enqueue(Message.ElementDeleted(element));
                    Monitor.Pulse(_updaterThreadSync);
                }
                catch { }
            }
        }

        private void _windowEvents_WindowClosing(Window window)
        {
            
        }

        private void _windowEvents_WindowActivated(Window gotFocus, Window lostFocus)
        {
            Document doc = gotFocus.Document;
            if (doc != null)
            {
                ProjectItem item = doc.ProjectItem;
                if (item != null)
                {
                    lock (_updaterThreadSync)
                    {
                        _messageQueue.Enqueue(Message.ScanFile(item));
                        Monitor.Pulse(_updaterThreadSync);
                    }
                }
            }
        }

        private void UpdaterThreadFunc()
        {
            while (true)
            {
                Message message;
                lock (_updaterThreadSync)
                {
                    while (_messageQueue.Count == 0)
                        Monitor.Wait(_updaterThreadSync);

                    message = _messageQueue.Dequeue();
                }

                switch (message.Type)
                {
                    case MessageType.ScanFile:
                        bool? result = ScanFile(message.ProjectItem);
                        lock (_updaterThreadResultSync)
                        {
                            message.Result = result;
                            Monitor.PulseAll(_updaterThreadResultSync);
                        }
                        break;
                    case MessageType.ElementAdded:
                        break;
                    case MessageType.ElementChanged:
                        break;
                    case MessageType.ElementDeleted:
                        break;
                    case MessageType.Stop:
                        return;
                }
            }
        }

        bool ScanFile(ProjectItem item)
        {
            bool result = false;
            try
            {
                string filePath = item.FileNames[0];
                if (_codeItemCache.ContainsKey(filePath))
                    return true;

                if (item.FileCodeModel != null)
                {
                    List<LocatorCodeItem> codeItems = new List<LocatorCodeItem>();
                    GetCodeElements(codeItems, item.FileCodeModel.CodeElements, item);
                    AddSourceFileToCache(filePath, codeItems);
                    result = true;
                }
            }
            catch
            {

            }

            return result;
        }

        private void AddSourceFileToCache(string path, List<LocatorCodeItem> codeItems)
        {
            lock (_codeItemCache)
            {
                if (_codeItemCache.Count == MAX_DICT_ENTRIES)
                {
                    // If we reached the cap, remove one of least recently used items.
                    long maxTimespan = 0;
                    string lruKey = null;
                    long tsNow = Stopwatch.GetTimestamp();
                    foreach (KeyValuePair<string, CodeItemList> kvp in _codeItemCache)
                    {
                        long timespan = tsNow - kvp.Value.Timestamp;
                        if (timespan > maxTimespan)
                        {
                            maxTimespan = timespan;
                            lruKey = kvp.Key;
                        }
                    }
                    if (lruKey != null)
                    {
                        _codeItemCache.Remove(lruKey);
                    }
                }

                // Add new source file.
                CodeItemList list = new CodeItemList();
                list.Items = codeItems;
                list.Timestamp = Stopwatch.GetTimestamp();;
                _codeItemCache[path] = list;
            }
        }

        private void RemoveSourceFileFromCache(string path)
        {
            lock (_codeItemCache)
                _codeItemCache.Remove(path);
        }

        private void GetCodeElements(List<LocatorCodeItem> results, CodeElements codeElements, ProjectItem projectItem)
        {
            if (codeElements == null)
                return;

            foreach (CodeElement ce in codeElements)
            {
                if (_cancelCodeScan)
                    return;

                if (ce.Kind == vsCMElement.vsCMElementNamespace)
                {
                    CodeNamespace nsp = ce as CodeNamespace;
                    GetCodeElements(results, nsp.Children, projectItem);
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
                            if (vcStr.ProjectItem == projectItem)
                            {
                                LocatorCodeItem item = new LocatorCodeItem();
                                item.CodeElement = ce;
                                item.Name = str.Name;
                                item.FQName = str.FullName;
                                item.Comment = str.Comment;
                                item.Image = _codeIconList[CodeIcon_Struct];
                                results.Add(item);

                                GetCodeElements(results, vcStr.Children, projectItem);
                            }
                        }
                        else
                        {
                            GetCodeElements(results, str.Children, projectItem);
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
                            if (vcCls.ProjectItem == projectItem)
                            {
                                LocatorCodeItem item = new LocatorCodeItem();
                                item.CodeElement = ce;
                                item.Name = cls.Name;
                                item.FQName = cls.FullName;
                                item.Comment = cls.Comment;
                                item.Image = _codeIconList[CodeIcon_Class];
                                results.Add(item);

                                GetCodeElements(results, vcCls.Children, projectItem);
                            }
                        }
                        else
                        {
                            GetCodeElements(results, cls.Children, projectItem);
                        }
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementFunction)
                {
                    CodeFunction f = ce as CodeFunction;
                    if (f != null)
                    {
                        LocatorCodeItem item = new LocatorCodeItem();
                        item.CodeElement = ce;
                        item.Name = f.get_Prototype((int)vsCMPrototype.vsCMPrototypeParamTypes);
                        item.FQName = f.FullName;
                        item.Comment = f.Comment;
                        item.Image = _codeIconList[CodeIcon_Function];
                        GetCppInfo(ce, item, projectItem);
                        results.Add(item);
                    }
                }
                else if (ce.Kind == vsCMElement.vsCMElementEnum)
                {
                    CodeEnum enm = ce as CodeEnum;
                    if (enm != null)
                    {
                        VCCodeEnum vcEnm = enm as VCCodeEnum;

                        // Skip forward declarations
                        if (vcEnm == null || vcEnm.ProjectItem == projectItem)
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
                else if (ce.Kind == vsCMElement.vsCMElementUnion)
                {
                    VCCodeUnion vcUn = ce as VCCodeUnion;
                    if (vcUn != null)
                    {
                        // Skip forward declarations
                        if (vcUn.ProjectItem == projectItem)
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
                else if (ce.Kind == vsCMElement.vsCMElementInterface)
                {
                    CodeInterface intf = ce as CodeInterface;
                    if (intf != null)
                    {
                        LocatorCodeItem item = new LocatorCodeItem();
                        item.CodeElement = ce;
                        item.Name = intf.Name;
                        item.FQName = intf.FullName;
                        item.Comment = intf.Comment;
                        item.Image = _codeIconList[CodeIcon_Interface];
                        results.Add(item);

                        GetCodeElements(results, intf.Children, projectItem);
                    }
                }
            }
        }

        private void GetCppInfo(CodeElement ce, LocatorCodeItem item, ProjectItem projectItem)
        {
            item.ProjectItem = null;
            item.ElementOffset = 1;
            VCCodeElement vcEl = ce as VCCodeElement;

            if (vcEl != null)
            {
                try
                {
                    TextPoint def = vcEl.StartPointOf[vsCMPart.vsCMPartName, vsCMWhere.vsCMWhereDefinition];
                    if (def.Parent.Parent.ProjectItem == projectItem)
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
                        if (decl.Parent.Parent.ProjectItem == projectItem)
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
    }
}
