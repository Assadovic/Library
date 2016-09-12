using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Profile")]
    public sealed class Profile : ItemBase<Profile>, IProfile
    {
        private enum SerializeId
        {
            Limit = 0,
            ExchangePublicKey = 1,
            Tag = 2,
            Comment = 3,
        }

        private volatile int _limit;
        private volatile ExchangePublicKey _exchangePublicKey;
        private volatile TagCollection _tags;
        private volatile string _comment;

        public static readonly int MaxTagCount = 256;
        public static readonly int MaxCommentLength = 1024 * 8;

        public Profile()
        {

        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                for (;;)
                {
                    var id = reader.GetId();
                    if (id < 0) return;

                    if (id == (int)SerializeId.Limit)
                    {
                        this.Limit = reader.GetInt();
                    }
                    else if (id == (int)SerializeId.ExchangePublicKey)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                        }
                    }
                    else if (id == (int)SerializeId.Tag)
                    {
                        using (var rangeStream = reader.GetStream())
                        {
                            this.ProtectedTags.Add(Tag.Import(rangeStream, bufferManager));
                        }
                    }
                    else if (id == (int)SerializeId.Comment)
                    {
                        this.Comment = reader.GetString();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Limit
                if (this.Limit != 0)
                {
                    writer.Write((int)SerializeId.Limit, this.Limit);
                }
                // ExchangePublicKey
                if (this.ExchangePublicKey != null)
                {
                    writer.Add((int)SerializeId.ExchangePublicKey, this.ExchangePublicKey.Export(bufferManager));
                }
                // Tags
                foreach (var value in this.Tags)
                {
                    writer.Add((int)SerializeId.Tag, value.Export(bufferManager));
                }
                // Comment
                if (this.Comment != null)
                {
                    writer.Write((int)SerializeId.Comment, this.Comment);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            if (this.ExchangePublicKey == null) return 0;
            else return this.ExchangePublicKey.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Profile)) return false;

            return this.Equals((Profile)obj);
        }

        public override bool Equals(Profile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Limit != other.Limit
                || this.ExchangePublicKey != other.ExchangePublicKey
                || (this.Tags == null) != (other.Tags == null)
                || this.Comment != other.Comment)
            {
                return false;
            }

            if (this.Tags != null && other.Tags != null)
            {
                if (!CollectionUtils.Equals(this.Tags, other.Tags)) return false;
            }

            return true;
        }

        #region IBroadcastContent

        [DataMember(Name = "Limit")]
        public int Limit
        {
            get
            {
                return _limit;
            }
            private set
            {
                _limit = value;
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }
            private set
            {
                _exchangePublicKey = value;
            }
        }

        private volatile ReadOnlyCollection<Tag> _readOnlyTags;

        public IEnumerable<Tag> Tags
        {
            get
            {
                if (_readOnlyTags == null)
                    _readOnlyTags = new ReadOnlyCollection<Tag>(this.ProtectedTags.ToArray());

                return _readOnlyTags;
            }
        }

        [DataMember(Name = "Tags")]
        private TagCollection ProtectedTags
        {
            get
            {
                if (_tags == null)
                    _tags = new TagCollection(Profile.MaxTagCount);

                return _tags;
            }
        }

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > Profile.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        #endregion
    }
}
