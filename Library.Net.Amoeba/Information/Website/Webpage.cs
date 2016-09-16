using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Webpage")]
    public sealed class Webpage : ItemBase<Webpage>, IWebpage
    {
        private enum SerializeId
        {
            Name = 0,
            FormatType = 1,
            Content = 2,
        }

        private volatile string _name;
        private volatile HypertextFormatType _formatType;
        private volatile string _content;

        public static readonly int MaxNameLength = 256;
        public static readonly int MaxContentLength = 1024 * 256;

        public Webpage(string name, HypertextFormatType formatType, string content)
        {
            this.Name = name;
            this.FormatType = formatType;
            this.Content = content;
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.Name)
                    {
                        this.Name = reader.GetString();
                    }
                    else if (id == (int)SerializeId.FormatType)
                    {
                        this.FormatType = reader.GetEnum<HypertextFormatType>();
                    }
                    else if (id == (int)SerializeId.Content)
                    {
                        this.Content = reader.GetString();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Name
                if (this.Name != null)
                {
                    writer.Write((int)SerializeId.Name, this.Name);
                }
                // FormatType
                if (this.FormatType != 0)
                {
                    writer.Write((int)SerializeId.FormatType, this.FormatType);
                }
                // Content
                if (this.Content != null)
                {
                    writer.Write((int)SerializeId.Content, this.Content);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            return this.Content.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Webpage)) return false;

            return this.Equals((Webpage)obj);
        }

        public override bool Equals(Webpage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Content != other.Content)
            {
                return false;
            }

            return true;
        }

        #region IWebpage

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                return _name;
            }
            private set
            {
                if (value != null && value.Length > Webpage.MaxNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _name = value;
                }
            }
        }

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                return _formatType;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(HypertextFormatType), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _formatType = value;
                }
            }
        }

        [DataMember(Name = "Content")]
        public string Content
        {
            get
            {
                return _content;
            }
            private set
            {
                if (value != null && value.Length > Webpage.MaxContentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _content = value;
                }
            }
        }

        #endregion
    }
}
