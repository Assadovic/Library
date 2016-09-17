using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;
using Library.Utilities;

namespace Library.Security
{
    [DataContract(Name = "Certificate")]
    public sealed class Certificate : ItemBase<Certificate>
    {
        private enum SerializeId
        {
            Nickname = 0,
            DigitalSignatureAlgorithm = 1,
            PublicKey = 2,
            Signature = 3,
        }

        private static Intern<string> _nicknameCache = new Intern<string>();
        private volatile string _nickname;
        private volatile DigitalSignatureAlgorithm _digitalSignatureAlgorithm;
        private static Intern<byte[]> _publicKeyCache = new Intern<byte[]>(new ByteArrayEqualityComparer());
        private volatile byte[] _publicKey;
        private volatile byte[] _signature;

        private volatile int _hashCode;
        private volatile string _toString;

        public static readonly int MaxNickNameLength = 256;
        public static readonly int MaxPublickeyLength = 1024 * 8;
        public static readonly int MaxSignatureLength = 1024 * 8;

        internal Certificate(DigitalSignature digitalSignature, Stream stream)
        {
            if (digitalSignature == null) throw new ArgumentNullException(nameof(digitalSignature));

            byte[] signature;

            if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha256)
            {
                signature = EcDsaP521_Sha256.Sign(digitalSignature.PrivateKey, stream);
            }
            else if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha256)
            {
                signature = Rsa2048_Sha256.Sign(digitalSignature.PrivateKey, stream);
            }
            else
            {
                return;
            }

            this.Nickname = digitalSignature.Nickname;
            this.DigitalSignatureAlgorithm = digitalSignature.DigitalSignatureAlgorithm;
            this.PublicKey = digitalSignature.PublicKey;
            this.Signature = signature;
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
                    else if (id == (int)SerializeId.Signature)
                    {
                        this.Signature = reader.GetBytes();
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
                // Signature
                if (this.Signature != null)
                {
                    writer.Write((int)SerializeId.Signature, this.Signature);
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
            if ((object)obj == null || !(obj is Certificate)) return false;

            return this.Equals((Certificate)obj);
        }

        public override bool Equals(Certificate other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!object.ReferenceEquals(this.Nickname, other.Nickname)
                || this.DigitalSignatureAlgorithm != other.DigitalSignatureAlgorithm
                || !object.ReferenceEquals(this.PublicKey, other.PublicKey)
                || ((this.Signature == null) != (other.Signature == null)))
            {
                return false;
            }

            if (this.Signature != null && other.Signature != null)
            {
                if (!Unsafe.Equals(this.Signature, other.Signature)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            if (_toString == null)
                _toString = Library.Security.Signature.GetSignature(this);

            return _toString;
        }

        internal bool Verify(Stream stream)
        {
            if (this.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha256)
            {
                return EcDsaP521_Sha256.Verify(this.PublicKey, this.Signature, stream);
            }
            else if (this.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha256)
            {
                return Rsa2048_Sha256.Verify(this.PublicKey, this.Signature, stream);
            }
            else
            {
                return false;
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
                    _nickname = _nicknameCache.GetValue(value, this);
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
                if (value != null && value.Length > Certificate.MaxPublickeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _publicKey = _publicKeyCache.GetValue(value, this);
                }

                if (value != null)
                {
                    _hashCode = RuntimeHelpers.GetHashCode(_publicKey);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        [DataMember(Name = "Signature")]
        public byte[] Signature
        {
            get
            {
                return _signature;
            }
            private set
            {
                if (value != null && value.Length > Certificate.MaxSignatureLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _signature = value;
                }
            }
        }
    }
}