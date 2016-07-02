using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Library.Security
{
    class Rsa2048_Sha256
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

        public static byte[] Sign(byte[] privateKey, Stream stream)
        {
#if Windows
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));

                var rsaFormatter = new RSAPKCS1SignatureFormatter(rsa);
                rsaFormatter.SetHashAlgorithm("SHA256");

                using (var Sha256 = SHA256.Create())
                {
                    return rsaFormatter.CreateSignature(Sha256.ComputeHash(stream));
                }
            }
#endif

#if Unix
            lock (_lockObject)
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.ASCII.GetString(privateKey));

                    var rsaFormatter = new RSAPKCS1SignatureFormatter(rsa);
                    rsaFormatter.SetHashAlgorithm("SHA256");

                    using (var Sha256 = SHA256.Create())
                    {
                        return rsaFormatter.CreateSignature(Sha256.ComputeHash(stream));
                    }
                }
            }
#endif
        }

        public static bool Verify(byte[] publicKey, byte[] signature, Stream stream)
        {
#if Windows
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));

                    var rsaDeformatter = new RSAPKCS1SignatureDeformatter(rsa);
                    rsaDeformatter.SetHashAlgorithm("SHA256");

                    using (var Sha256 = SHA256.Create())
                    {
                        return rsaDeformatter.VerifySignature(Sha256.ComputeHash(stream), signature);
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
#endif

#if Unix
            lock (_lockObject)
            {
                try
                {
                    using (var rsa = new RSACryptoServiceProvider())
                    {
                        rsa.FromXmlString(Encoding.ASCII.GetString(publicKey));

                        var rsaDeformatter = new RSAPKCS1SignatureDeformatter(rsa);
                        rsaDeformatter.SetHashAlgorithm("SHA256");

                        using (var Sha256 = SHA256.Create())
                        {
                            return rsaDeformatter.VerifySignature(Sha256.ComputeHash(stream), signature);
                        }
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
#endif
        }
    }
}
