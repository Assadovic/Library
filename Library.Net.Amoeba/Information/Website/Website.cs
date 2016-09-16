using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Website")]
    public sealed class Website : ItemBase<Website>, IWebsite
    {
        private enum SerializeId
        {
            Webpage = 0,
        }

        private WebpageCollection _webpages;

        private volatile object _thisLock;

        public static readonly int MaxWebpageCount = 1024;

        public Website(IEnumerable<Webpage> webpages)
        {
            if (webpages != null) this.ProtectedWebpages.AddRange(webpages);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.Webpage)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.ProtectedWebpages.Add(Webpage.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Webpages
                foreach (var value in this.Webpages)
                {
                    writer.Add((int)SerializeId.Webpage, value.Export(bufferManager));
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.ProtectedWebpages.Count == 0) return 0;
            else return this.ProtectedWebpages[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Website)) return false;

            return this.Equals((Website)obj);
        }

        public override bool Equals(Website other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!CollectionUtils.Equals(this.Webpages, other.Webpages))
            {
                return false;
            }

            return true;
        }

        #region IWebsite

        private volatile ReadOnlyCollection<Webpage> _readOnlyWebpages;

        public IEnumerable<Webpage> Webpages
        {
            get
            {
                if (_readOnlyWebpages == null)
                    _readOnlyWebpages = new ReadOnlyCollection<Webpage>(this.ProtectedWebpages.ToArray());

                return _readOnlyWebpages;
            }
        }

        [DataMember(Name = "Webpages")]
        private WebpageCollection ProtectedWebpages
        {
            get
            {
                if (_webpages == null)
                    _webpages = new WebpageCollection(Website.MaxWebpageCount);

                return _webpages;
            }
        }

        #endregion
    }
}
