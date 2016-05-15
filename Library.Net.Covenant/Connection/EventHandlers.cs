using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Library.Net.Covenant
{
    #region Common

    delegate void PullCancelEventHandler(object sender, EventArgs e);

    delegate void CloseEventHandler(object sender, EventArgs e);

    #endregion

    #region Search

    class PullNodesEventArgs : EventArgs
    {
        public IEnumerable<Node> Nodes { get; set; }
    }

    class PullLocationsRequestEventArgs : EventArgs
    {
        public IEnumerable<Key> Keys { get; set; }
    }

    class PullLocationsEventArgs : EventArgs
    {
        public IEnumerable<Location> Locations { get; set; }
    }

    class PullMetadatasRequestEventArgs : EventArgs
    {
        public IEnumerable<QueryMetadata> QueryMetadatas { get; set; }
    }

    class PullMetadatasEventArgs : EventArgs
    {
        public IEnumerable<Metadata> Metadatas { get; set; }
    }

    delegate void PullNodesEventHandler(object sender, PullNodesEventArgs e);

    delegate void PullLocationsRequestEventHandler(object sender, PullLocationsRequestEventArgs e);
    delegate void PullLocationsEventHandler(object sender, PullLocationsEventArgs e);

    delegate void PullMetadatasRequestEventHandler(object sender, PullMetadatasRequestEventArgs e);
    delegate void PullMetadatasEventHandler(object sender, PullMetadatasEventArgs e);

    #endregion

    #region Exchange

    class PullUrisEventArgs : EventArgs
    {
        public IEnumerable<string> Uris { get; set; }
    }

    class PullBlocksInfoEventArgs : EventArgs
    {
        public BlocksInfo BlocksInfo { get; set; }
    }

    class PullBlockEventArgs : EventArgs
    {
        public int Index { get; set; }
        public ArraySegment<byte> Value { get; set; }
    }

    delegate void PullUrisEventHandler(object sender, PullUrisEventArgs e);

    delegate void PullBlocksInfoRequestEventHandler(object sender, EventArgs e);
    delegate void PullBlocksInfoEventHandler(object sender, PullBlocksInfoEventArgs e);

    delegate void PullBlockRequestEventHandler(object sender, EventArgs e);
    delegate void PullBlockEventHandler(object sender, PullBlockEventArgs e);

    #endregion
}
