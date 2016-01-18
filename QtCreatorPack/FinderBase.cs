using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QtCreatorPack
{
    internal class FinderBase
    {
        public delegate void SearchResultsCallback(IEnumerable<LocatorItem> results);
        public delegate void SearchProgressCallback(int percent);
    }
}
