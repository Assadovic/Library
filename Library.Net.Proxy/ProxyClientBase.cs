using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Library.Net.Proxy
{
    public abstract class ProxyClientBase
    {
        protected static TimeSpan CheckTimeout(TimeSpan elapsedTime, TimeSpan timeout)
        {
            var value = timeout - elapsedTime;

            if (value > TimeSpan.Zero)
            {
                return value;
            }
            else
            {
                throw new TimeoutException();
            }
        }

        public abstract void Create(Socket socket, TimeSpan timeout);

        public virtual Task CreateAsync(Socket socket, TimeSpan timeout)
        {
            return Task.Run(() =>
            {
                this.Create(socket, timeout);
            });
        }
    }

    [Serializable]
    public class ProxyClientException : Exception
    {
        public ProxyClientException() { }
        public ProxyClientException(string message) : base(message) { }
        public ProxyClientException(string message, Exception innerException) : base(message, innerException) { }
    }
}
