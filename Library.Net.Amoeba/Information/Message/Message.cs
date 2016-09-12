using System;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Message")]
    public sealed class Message : ItemBase<Message>, IMessage
    {
        private enum SerializeId
        {
            Comment = 0,
        }

        private volatile string _comment;

        public static readonly int MaxCommentLength = 1024 * 8;

        public Message()
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

                    if (id == (int)SerializeId.Comment)
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
            return this.Comment.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Message)) return false;

            return this.Equals((Message)obj);
        }

        public override bool Equals(Message other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Comment != other.Comment)
            {
                return false;
            }

            return true;
        }

        #region IMessage

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > Message.MaxCommentLength)
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
