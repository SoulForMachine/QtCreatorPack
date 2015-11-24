using System;
using System.Collections.Generic;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using System.Threading;
using System.Windows.Threading;

namespace QtCreatorPack
{
    class Locator
    {
        public class Item
        {
            public string Name { get; set; }
        }

        public class SearchFinishedEventArgs
        {
            public SearchFinishedEventArgs(List<Item> items)
            {
                Items = new List<Item>(items);
            }

            public List<Item> Items { get; private set; }
        }

        public delegate void SearchFinishedEventHandler(object sender, SearchFinishedEventArgs items);
        public event SearchFinishedEventHandler SearchFinishedEvent;

        private class Message
        {
            public Message()
            {
                Text = null;
            }

            public Message(string text)
            {
                Text = text;
            }

            public string Text { get; private set; }
        }

        private IVsSolution solution = null;
        private volatile bool searchUpdated = false;
        private List<Item> itemList;
        private List<Item> resultItems;
        private Thread searchThread;
        private readonly object searchMessageSync;
        private Message message;
        private Dispatcher dispatcher;

        public Locator(IVsSolution solution)
        {
            this.solution = solution;
            itemList = new List<Item>();
            UpdateItemList();
            searchMessageSync = new object();
            dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
            message = null;
            searchThread = new Thread(new ThreadStart(SearchThreadFunc));
            searchThread.Start();
        }

        public List<Item> GetItems()
        {
            return itemList;
        }

        public void UpdateSearch(string text)
        {
            lock (searchMessageSync)
            {
                message = new Message(text);
                searchUpdated = true;
                Monitor.Pulse(searchMessageSync);
            }
        }

        public void StopSearchThread()
        {
            lock (searchMessageSync)
            {
                message = new Message();    // Empty message stops the thread.
                searchUpdated = true;
                Monitor.Pulse(searchMessageSync);

                if (searchThread.IsAlive)
                {
                    searchThread.Join();
                }
            }
        }

        protected virtual void RaiseSearchFinishedEvent()
        {
            if (SearchFinishedEvent != null)
                SearchFinishedEvent(this, new SearchFinishedEventArgs(resultItems));
        }

        private void SearchThreadFunc()
        {
            while (true)
            {
                string text = "";
                lock (searchMessageSync)
                {
                    searchUpdated = false;
                    while (message == null)
                        Monitor.Wait(searchMessageSync);
                    text = message.Text;
                    message = null;
                }

                if (text == null)
                    break;

                // Search for text.

                // Raise search finished event in user's thread.
                dispatcher.BeginInvoke(new Action(() => { this.RaiseSearchFinishedEvent(); }));
            }
        }

        private void UpdateItemList()
        {
            itemList.Clear();
            IEnumHierarchies enumHierarchies;
            Guid guid = new Guid();
            int result = solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out enumHierarchies);
            if (result == VSConstants.S_OK)
            {
                const int arrSize = 10;
                IVsHierarchy[] hierarchyArray = new IVsHierarchy[arrSize];
                uint count;
                do
                {
                    result = enumHierarchies.Next(arrSize, hierarchyArray, out count);
                    if (result == VSConstants.S_OK)
                    {
                        for (int i = 0; i < count; ++i)
                        {
                            object obj;
                            IVsHierarchy hierarchy = hierarchyArray[i];
                            hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out obj);
                            EnvDTE.Project project = obj as EnvDTE.Project;
                        }
                    }
                    else
                        break;
                } while (count == arrSize);
            }
        }
    }
}
