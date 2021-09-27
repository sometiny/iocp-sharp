using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using IocpSharp.Http.Streams;

namespace IocpSharp.Http.Responsers
{
    /// <summary>
    /// HTTP文本应答器
    /// </summary>
    public class HttpTextResponser : HttpResponser
    {
        private byte[] _message = null;
        public HttpTextResponser(string message) : this(message, 200) { }

        public HttpTextResponser(string message, int statusCode) : base(statusCode)
        {
            _message = string.IsNullOrEmpty(message) ? new byte[0] : Encoding.UTF8.GetBytes(message);
            ContentType = "text/html; charset=utf-8";
            Chunked = false;
            ContentLength = _message.Length;
        }

        public override Stream OpenWrite()
        {
            Stream stream = base.OpenWrite();
            if (_message.Length == 0) return stream;
            stream.Write(_message, 0, _message.Length);
            return stream;
        }
    }
}
