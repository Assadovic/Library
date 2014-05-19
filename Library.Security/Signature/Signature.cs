using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Library.Io;

namespace Library.Security
{
    public static class Signature
    {
        private static readonly BufferManager _bufferManager = BufferManager.Instance;
        private static readonly ThreadLocal<Encoding> _threadLocalEncoding = new ThreadLocal<Encoding>(() => new UTF8Encoding(false));

        private static Intern<string> _signatureCache = new Intern<string>();

        private static ConditionalWeakTable<string, byte[]> _signatureHashCache = new ConditionalWeakTable<string, byte[]>();
        private static readonly object _signatureHashCacheLockObject = new object();

        private unsafe static bool CheckBase64(string value)
        {
            fixed (char* p_value = value)
            {
                var t_value = p_value;

                for (int i = value.Length - 1; i >= 0; i--)
                {
                    if (!('A' <= *t_value && *t_value <= 'Z')
                        && !('a' <= *t_value && *t_value <= 'z')
                        && !('0' <= *t_value && *t_value <= '9')
                        && !(*t_value == '-' || *t_value == '_')) return false;

                    t_value++;
                }
            }

            return true;
        }
        public static string GetSignature(DigitalSignature digitalSignature)
        {
            if (digitalSignature == null || digitalSignature.Nickname == null || digitalSignature.PublicKey == null) return null;

            try
            {
                if (digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512
                    || digitalSignature.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        Signature.WriteString(bufferStream, digitalSignature.Nickname);
                        bufferStream.Write(digitalSignature.PublicKey, 0, digitalSignature.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        var signature = digitalSignature.Nickname + "@" + NetworkConverter.ToBase64UrlString(Sha512.ComputeHash(bufferStream));
                        return _signatureCache.GetValue(signature, digitalSignature);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static string GetSignature(Certificate certificate)
        {
            if (certificate == null || certificate.Nickname == null || certificate.PublicKey == null) return null;

            try
            {
                if (certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.EcDsaP521_Sha512
                    || certificate.DigitalSignatureAlgorithm == DigitalSignatureAlgorithm.Rsa2048_Sha512)
                {
                    using (BufferStream bufferStream = new BufferStream(_bufferManager))
                    {
                        Signature.WriteString(bufferStream, certificate.Nickname);
                        bufferStream.Write(certificate.PublicKey, 0, certificate.PublicKey.Length);
                        bufferStream.Seek(0, SeekOrigin.Begin);

                        var signature = certificate.Nickname + "@" + NetworkConverter.ToBase64UrlString(Sha512.ComputeHash(bufferStream));
                        return _signatureCache.GetValue(signature, certificate);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        public static void WriteString(Stream stream, string value)
        {
            Encoding encoding = _threadLocalEncoding.Value;

            byte[] buffer = null;

            try
            {
                buffer = _bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                stream.Write(buffer, 0, length);
            }
            finally
            {
                if (buffer != null)
                {
                    _bufferManager.ReturnBuffer(buffer);
                }
            }
        }

        public static bool IsSignature(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                var index = item.LastIndexOf('@');
                if (index == -1) return false;

                var nickname = item.Substring(0, index);
                var hash = item.Substring(index + 1);

                if (nickname.Length > 256) return false;
                if (hash.Length > 256 || !Signature.CheckBase64(hash)) return false;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string GetSignatureNickname(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                var index = item.LastIndexOf('@');
                if (index == -1) return null;

                var nickname = item.Substring(0, index);
                var hash = item.Substring(index + 1);

                if (nickname.Length > 256) return null;
                if (hash.Length > 256 || !Signature.CheckBase64(hash)) return null;

                return nickname;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] GetSignatureHash(string item)
        {
            if (item == null) throw new ArgumentNullException("item");

            try
            {
                lock (_signatureHashCacheLockObject)
                {
                    byte[] value;

                    if (!_signatureHashCache.TryGetValue(item, out value))
                    {
                        var index = item.LastIndexOf('@');
                        if (index == -1) return null;

                        var nickname = item.Substring(0, index);
                        var hash = item.Substring(index + 1);

                        if (nickname.Length > 256) return null;
                        if (hash.Length > 256 || !Signature.CheckBase64(hash)) return null;

                        value = NetworkConverter.FromBase64UrlString(hash);
                        _signatureHashCache.Add(item, value);
                    }

                    return value;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
