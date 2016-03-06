using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Library;

namespace Library.Net.I2p
{
    class SamCommand
    {
        private List<string> _commands = new List<string>();
        private Dictionary<string, string> _parameters = new Dictionary<string, string>();

        public SamCommand()
        {

        }

        public SamCommand(string text)
        {
            var lines = SamCommand.Decode(text);
            _commands.Add(lines[0]);
            _commands.Add(lines[1]);

            foreach (var pair in lines.Skip(2))
            {
                int equalsPosition = pair.IndexOf('=');

                string key;
                string value;

                if (equalsPosition == -1)
                {
                    key = pair;
                    value = null;
                }
                else
                {
                    key = pair.Substring(0, equalsPosition);
                    value = pair.Substring(equalsPosition + 1);
                }

                key = !(string.IsNullOrWhiteSpace(key)) ? key : null;
                value = !(string.IsNullOrWhiteSpace(value)) ? value : null;

                _parameters.Add(key, value);
            }
        }

        public List<string> Commands
        {
            get
            {
                return _commands;
            }
        }

        public Dictionary<string, string> Parameters
        {
            get
            {
                return _parameters;
            }
        }

        private static string[] Decode(string input)
        {
            if (input == null) throw new ArgumentNullException("input");

            StringBuilder builder = new StringBuilder(input.Length);

            int begin = 0;
            int end;

            bool quoting = false;

            for (;;)
            {
                end = input.IndexOf('\"', begin);
                if (end == -1) end = input.Length;

                string s = input.Substring(begin, end - begin);
                if (!quoting) s = s.Replace(' ', '\n');

                builder.Append(s);

                if (end == input.Length) break;
                begin = end + 1;

                quoting = !quoting;
            }

            return builder.ToString().Split('\n');
        }

        public string GetText()
        {
            var sb = new StringBuilder();

            foreach (var command in _commands)
            {
                sb.AppendFormat("{0} ", command);
            }

            foreach (var pair in _parameters)
            {
                if (pair.Value != null)
                {
                    if (!pair.Value.Contains(" "))
                    {
                        sb.AppendFormat("{0}={1} ", pair.Key, pair.Value);
                    }
                    else
                    {
                        sb.AppendFormat("{0}=\"{1}\" ", pair.Key, pair.Value);
                    }
                }
                else
                {
                    sb.AppendFormat("{0} ", pair.Key);
                }
            }

            sb.Length = (sb.Length - 1);

            return sb.ToString();
        }
    }

    abstract class SamBase
    {
        private Socket _socket;

        private Stream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;

        public SamBase(Socket socket)
        {
            _socket = socket;

            {
                _stream = new NetworkStream(socket);
                _stream.ReadTimeout = Timeout.Infinite;
                _stream.WriteTimeout = Timeout.Infinite;

                _reader = new StreamReader(_stream, new UTF8Encoding(false), false, 1024 * 32);
                _writer = new StreamWriter(_stream, new UTF8Encoding(false), 1024 * 32);
                _writer.NewLine = "\n";
            }
        }

        private void Send(SamCommand samCommand)
        {
            _writer.WriteLine(samCommand.GetText());
            _writer.Flush();
        }

        private SamCommand Receive()
        {
            var line = _reader.ReadLine();
            if (line == null) return null;

            return new SamCommand(line);
        }

        public Socket GetSocket()
        {
            _stream.Dispose();
            _reader.Dispose();
            _writer.Dispose();

            return _socket;
        }

        public void Handshake()
        {
            try
            {
                {
                    var samCommand = new SamCommand();
                    samCommand.Commands.Add("HELLO");
                    samCommand.Commands.Add("VERSION");
                    samCommand.Parameters.Add("MIN", "3.0");
                    samCommand.Parameters.Add("MAN", "3.0");

                    this.Send(samCommand);
                }

                {
                    var samCommand = this.Receive();

                    if (samCommand.Commands[0] != "HELLO" || samCommand.Commands[1] != "REPLY" || samCommand.Parameters["RESULT"] != "OK")
                    {
                        throw new SamException(samCommand.Parameters["RESULT"]);
                    }
                }
            }
            catch (SamException e)
            {
                throw e;
            }
            catch (Exception e)
            {
                throw new SamException(e.Message, e);
            }
        }

        public string SessionCreate(string sessionId, string caption)
        {
            try
            {
                {
                    var samCommand = new SamCommand();
                    samCommand.Commands.Add("SESSION");
                    samCommand.Commands.Add("CREATE");
                    samCommand.Parameters.Add("STYLE", "STREAM");
                    samCommand.Parameters.Add("ID", sessionId);
                    samCommand.Parameters.Add("DESTINATION", "TRANSIENT");
                    samCommand.Parameters.Add("inbound.nickname", caption);
                    samCommand.Parameters.Add("outbound.nickname", caption);

                    this.Send(samCommand);
                }

                {
                    var samCommand = this.Receive();

                    if (samCommand.Commands[0] != "SESSION" || samCommand.Commands[1] != "STATUS")
                    {
                        throw new SamException(samCommand.Parameters["RESULT"]);
                    }

                    return samCommand.Parameters["DESTINATION"];
                }
            }
            catch (SamException e)
            {
                throw e;
            }
            catch (Exception e)
            {
                throw new SamException(e.Message, e);
            }
        }

        public string NamingLookup(string name)
        {
            try
            {
                {
                    var samCommand = new SamCommand();
                    samCommand.Commands.Add("NAMING");
                    samCommand.Commands.Add("LOOKUP");
                    samCommand.Parameters.Add("NAME", name);

                    this.Send(samCommand);
                }

                {
                    var samCommand = this.Receive();

                    if (samCommand.Commands[0] != "NAMING" || samCommand.Commands[1] != "REPLY")
                    {
                        throw new SamException();
                    }

                    return samCommand.Parameters["VALUE"];
                }
            }
            catch (SamException e)
            {
                throw e;
            }
            catch (Exception e)
            {
                throw new SamException(e.Message, e);
            }
        }

        public void StreamConnect(string sessionId, string destination)
        {
            try
            {
                {
                    var samCommand = new SamCommand();
                    samCommand.Commands.Add("STREAM");
                    samCommand.Commands.Add("CONNECT");
                    samCommand.Parameters.Add("ID", sessionId);
                    samCommand.Parameters.Add("DESTINATION", destination);
                    samCommand.Parameters.Add("SILENCE", "false");

                    this.Send(samCommand);
                }

                {
                    var samCommand = this.Receive();

                    if (samCommand.Commands[0] != "STREAM" || samCommand.Commands[1] != "STATUS" || samCommand.Parameters["RESULT"] != "OK")
                    {
                        throw new SamException();
                    }
                }
            }
            catch (SamException e)
            {
                throw e;
            }
            catch (Exception e)
            {
                throw new SamException(e.Message, e);
            }
        }

        public string StreamAccept(string sessionId)
        {
            try
            {
                {
                    var samCommand = new SamCommand();
                    samCommand.Commands.Add("STREAM");
                    samCommand.Commands.Add("ACCEPT");
                    samCommand.Parameters.Add("ID", sessionId);
                    samCommand.Parameters.Add("SILENCE", "false");

                    this.Send(samCommand);
                }

                {
                    var samCommand = this.Receive();

                    if (samCommand.Commands[0] != "STREAM" || samCommand.Commands[1] != "STATUS" || samCommand.Parameters["RESULT"] != "OK")
                    {
                        throw new SamException();
                    }
                }

                string result;

                {
                    result = _reader.ReadLine().Split(' ')[0];
                }

                return result;
            }
            catch (SamException e)
            {
                throw e;
            }
            catch (Exception e)
            {
                throw new SamException(e.Message, e);
            }
        }
    }
}
