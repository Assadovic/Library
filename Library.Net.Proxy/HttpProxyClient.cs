﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Library.Net.Proxy
{
    public class HttpProxyClient : ProxyClientBase
    {
        private const int HTTP_PROXY_DEFAULT_PORT = 8080;
        private const string HTTP_PROXY_CONNECT_CMD = "CONNECT {0}:{1} HTTP/1.0 \r\nHOST {0}:{1}\r\n\r\n";
        private const int WAIT_FOR_DATA_INTERVAL = 50; // 50 ms
        private const int WAIT_FOR_DATA_TIMEOUT = 15000; // 15 seconds

        private string _destinationHost;
        private int _destinationPort;
        private readonly object _thisLock = new object();

        private enum HttpResponseCodes
        {
            None = 0,
            Continue = 100,
            SwitchingProtocols = 101,
            OK = 200,
            Created = 201,
            Accepted = 202,
            NonAuthoritiveInformation = 203,
            NoContent = 204,
            ResetContent = 205,
            PartialContent = 206,
            MultipleChoices = 300,
            MovedPermanetly = 301,
            Found = 302,
            SeeOther = 303,
            NotModified = 304,
            UserProxy = 305,
            TemporaryRedirect = 307,
            BadRequest = 400,
            Unauthorized = 401,
            PaymentRequired = 402,
            Forbidden = 403,
            NotFound = 404,
            MethodNotAllowed = 405,
            NotAcceptable = 406,
            ProxyAuthenticantionRequired = 407,
            RequestTimeout = 408,
            Conflict = 409,
            Gone = 410,
            PreconditionFailed = 411,
            RequestEntityTooLarge = 413,
            RequestURITooLong = 414,
            UnsupportedMediaType = 415,
            RequestedRangeNotSatisfied = 416,
            ExpectationFailed = 417,
            InternalServerError = 500,
            NotImplemented = 501,
            BadGateway = 502,
            ServiceUnavailable = 503,
            GatewayTimeout = 504,
            HTTPVersionNotSupported = 505
        }

        public HttpProxyClient(string destinationHost, int destinationPort)
        {
            if (String.IsNullOrEmpty(destinationHost))
            {
                throw new ArgumentNullException(nameof(destinationHost));
            }
            else if (destinationPort <= 0 || destinationPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationPort), "port must be greater than zero and less than 65535");
            }

            _destinationHost = destinationHost;
            _destinationPort = destinationPort;
        }

        public override void Create(Socket socket, TimeSpan timeout)
        {
            try
            {
                // send connection command to proxy host for the specified destination host and port
                this.SendConnectionCommand(socket, _destinationHost, _destinationPort);
            }
            catch (SocketException ex)
            {
                throw new ProxyClientException(String.Format(CultureInfo.InvariantCulture, "Connection to proxy host {0} on port {1} failed.", ((System.Net.IPEndPoint)socket.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)socket.RemoteEndPoint).Port.ToString()), ex);
            }
        }

        private void SendConnectionCommand(Socket socket, string host, int port)
        {
            using (var stream = new NetworkStream(socket))
            using (var reader = new SocketLineReader(socket, Encoding.UTF8))
            {
                // PROXY SERVER REQUEST
                // =======================================================================
                // CONNECT starksoft.com:443 HTTP/1.0 <CR><LF>
                // HOST starksoft.com:443<CR><LF>
                // [... other HTTP header lines ending with <CR><LF> if required]>
                // <CR><LF>    // Last Empty Line

                string connectCmd = String.Format(CultureInfo.InvariantCulture, HTTP_PROXY_CONNECT_CMD, host, port.ToString(CultureInfo.InvariantCulture));
                byte[] request = Encoding.ASCII.GetBytes(connectCmd);

                // send the connect request
                stream.Write(request, 0, request.Length);

                // PROXY SERVER RESPONSE
                // =======================================================================
                // HTTP/1.0 200 Connection Established<CR><LF>
                // [.... other HTTP header lines ending with <CR><LF>..
                // ignore all of them]
                // <CR><LF>    // Last Empty Line

                // create an byte response array  
                var sb = new StringBuilder();

                {
                    string line;

                    while (!string.IsNullOrEmpty((line = reader.ReadLine())))
                    {
                        sb.Append(line);
                    }
                }

                string replyText;
                var replyCode = this.ParseResponse(sb.ToString(), out replyText);

                // evaluate the reply code for an error condition
                if (replyCode != HttpResponseCodes.OK)
                {
                    this.HandleProxyCommandError(socket, host, port, replyCode, replyText);
                }
            }
        }

        private void HandleProxyCommandError(Socket socket, string host, int port, HttpResponseCodes code, string text)
        {
            string msg;

            switch (code)
            {
                case HttpResponseCodes.None:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} failed to return a recognized HTTP response code.  Server response: {2}", ((System.Net.IPEndPoint)socket.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)socket.RemoteEndPoint).Port.ToString(), text);
                    break;

                case HttpResponseCodes.BadGateway:
                    // HTTP/1.1 502 Proxy Error (The specified Secure Sockets Layer (SSL) port is not allowed. ISA Server is not configured to allow SSL requests from this port. Most Web browsers use port 443 for SSL requests.)
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a 502 code - Bad Gateway.  If you are connecting to a Microsoft ISA destination please refer to knowledge based article Q283284 for more information.  Server response: {2}", ((System.Net.IPEndPoint)socket.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)socket.RemoteEndPoint).Port.ToString(), text);
                    break;

                default:
                    msg = String.Format(CultureInfo.InvariantCulture, "Proxy destination {0} on port {1} responded with a {2} code - {3}", ((System.Net.IPEndPoint)socket.RemoteEndPoint).Address.ToString(), ((System.Net.IPEndPoint)socket.RemoteEndPoint).Port.ToString(), ((int)code).ToString(CultureInfo.InvariantCulture), text);
                    break;
            }

            // throw a new application exception 
            throw new ProxyClientException(msg);
        }

        private HttpResponseCodes ParseResponse(string response, out string text)
        {
            text = null;

            // get rid of the LF character if it exists and then split the string on all CR
            var line = response.Replace('\n', ' ').Split('\r')[0];

            if (line.IndexOf("HTTP") == -1)
            {
                throw new ProxyClientException(String.Format("No HTTP response received from proxy destination.  Server response: {0}.", line));
            }

            int begin = line.IndexOf(" ") + 1;
            int end = line.IndexOf(" ", begin);
            var value = line.Substring(begin, end - begin);

            Int32 code = 0;

            if (!Int32.TryParse(value, out code))
            {
                throw new ProxyClientException(String.Format("An invalid response code was received from proxy destination.  Server response: {0}.", line));
            }

            text = line.Substring(end + 1).Trim();
            return (HttpResponseCodes)code;
        }
    }
}
