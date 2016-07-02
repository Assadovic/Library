using System.Security.Cryptography;
using System.Text;

namespace Library.Net.Connections
{
    static class Rsa2048
    {
#if Unix
        private static object _lockObject = "RSACryptoServiceProvider";
#endif

        public static void CreateKeys(out byte[] publicKey, out byte[] privateKey)
        {
#if Windows
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                publicKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(false));
                privateKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(true));
            }
#endif

#if Unix
            lock (_lockObject)
            {
                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    publicKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(false));
                    privateKey = Encoding.ASCII.GetBytes(rsa.ToXmlString(true));
                }
            }
#endif
        }

        public static byte[] Encrypt(byte[] publicKey, byte[] value)
        {
#if Windows
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));
                return rsa.Encrypt(value, true);
            }
#endif

#if Unix
            lock (_lockObject)
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));
                    return rsa.Encrypt(value, true);
                }
            }
#endif
        }

        public static byte[] Decrypt(byte[] privateKey, byte[] value)
        {
#if Windows
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));
                return rsa.Decrypt(value, true);
            }
#endif

#if Unix
            lock (_lockObject)
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));
                    return rsa.Decrypt(value, true);
                }
            }
#endif
        }
    }
}
