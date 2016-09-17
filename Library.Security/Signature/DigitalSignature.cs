using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Utilities;

namespace Library.Security
{
    [DataContract(Name = "DigitalSignature")]
    public sealed class DigitalSignature : ItemBase<DigitalSignature>
    {
        private enum SerializeId
        {
            Nickname = 0,
            DigitalSignatureAlgorithm = 1,
            PublicKey = 2,
            PrivateKey = 3,
        }

        private enum FileSerializeId
        {
            Name = 0,
            Stream = 1,
        }

        private volatile string _nickname;
        private volatile DigitalSignatureAlgorithm _digitalSignatureAlgorithm;
        private volatile byte[] _publicKey;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;
        private volatile string _toString;

        public static readonly int MaxNickNameLength = 256;
        public static readonly int MaxPublicKeyLength = 1024 * 8;
        public static readonly int MaxPrivateKeyLength = 1024 * 8;

        public DigitalSignature(string nickname, DigitalSignatureAlgorithm digitalSignatureAlgorithm)
        {
            this.Nickname = nickname;
            this.DigitalSignatureAlgorithm = digitalSignatureAlgorithm;

            if (digitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha256)
            {
                byte[] publicKey, privateKey;

                EcDsaP521_Sha256.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
            else if (digitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha256)
            {
                byte[] publicKey, privateKey;

                Rsa2048_Sha256.CreateKeys(out publicKey, out privateKey);

                this.PublicKey = publicKey;
                this.PrivateKey = privateKey;
            }
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            using (var reader = new ItemStreamReader(stream, bufferManager))
            {
                int id;

                while ((id = reader.GetId()) != -1)
                {
                    if (id == (int)SerializeId.Nickname)
                    {
                        this.Nickname = reader.GetString();
                    }
                    else if (id == (int)SerializeId.DigitalSignatureAlgorithm)
                    {
                        this.DigitalSignatureAlgorithm = reader.GetEnum<DigitalSignatureAlgorithm>();
                    }
                    else if (id == (int)SerializeId.PublicKey)
                    {
                        this.PublicKey = reader.GetBytes();
                    }
                    else if (id == (int)SerializeId.PrivateKey)
                    {
                        this.PrivateKey = reader.GetBytes();
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            using (var writer = new ItemStreamWriter(bufferManager))
            {
                // Nickname
                if (this.Nickname != null)
                {
                    writer.Write((int)SerializeId.Nickname, this.Nickname);
                }
                // DigitalSignatureAlgorithm
                if (this.DigitalSignatureAlgorithm != 0)
                {
                    writer.Write((int)SerializeId.DigitalSignatureAlgorithm, this.DigitalSignatureAlgorithm);
                }
                // PublicKey
                if (this.PublicKey != null)
                {
                    writer.Write((int)SerializeId.PublicKey, this.PublicKey);
                }
                // PrivateKey
                if (this.PrivateKey != null)
                {
                    writer.Write((int)SerializeId.PrivateKey, this.PrivateKey);
                }

                return writer.GetStream();
            }
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is DigitalSignature)) return false;

            return this.Equals((DigitalSignature)obj);
        }

        public override bool Equals(DigitalSignature other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Nickname != other.Nickname
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null))
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Unsafe.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Unsafe.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (_toString == null)
                _toString = Signature.GetSignature(this);

            return _toString;
        }

        public static Certificate CreateCertificate(DigitalSignature digitalSignature, Stream stream)
        {
            return new Certificate(digitalSignature, stream);
        }

        public static Certificate CreateFileCertificate(DigitalSignature digitalSignature, string name, Stream stream)
        {
            using (var writer = new ItemStreamWriter(BufferManager.Instance))
            {
                // Name
                {
                    writer.Write((int)FileSerializeId.Name, Path.GetFileName(name));
                }
                // Stream
                {
                    writer.Add((int)FileSerializeId.Stream, new WrapperStream(stream, true));
                }

                using (var uniteStream = writer.GetStream())
                {
                    return new Certificate(digitalSignature, uniteStream);
                }
            }
        }

        public static bool VerifyCertificate(Certificate certificate, Stream stream)
        {
            return certificate.Verify(stream);
        }

        public static bool VerifyFileCertificate(Certificate certificate, string name, Stream stream)
        {
            using (var writer = new ItemStreamWriter(BufferManager.Instance))
            {
                // Name
                {
                    writer.Write((int)FileSerializeId.Name, Path.GetFileName(name));
                }
                // Stream
                {
                    writer.Add((int)FileSerializeId.Stream, new WrapperStream(stream, true));
                }

                using (var uniteStream = writer.GetStream())
                {
                    return certificate.Verify(uniteStream);
                }
            }
        }

        [DataMember(Name = "Nickname")]
        public string Nickname
        {
            get
            {
                return _nickname;
            }
            private set
            {
                if (value != null && value.Length > Certificate.MaxNickNameLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _nickname = value;
                }
            }
        }

        [DataMember(Name = "DigitalSignatureAlgorithm")]
        public DigitalSignatureAlgorithm DigitalSignatureAlgorithm
        {
            get
            {
                return _digitalSignatureAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(DigitalSignatureAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _digitalSignatureAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                return _publicKey;
            }
            private set
            {
                if (value != null && value.Length > DigitalSignature.MaxPublicKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _publicKey = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtils.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && value.Length > DigitalSignature.MaxPrivateKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _privateKey = value;
                }
            }
        }
    }
}
