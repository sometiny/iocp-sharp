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
    /// HTTP应答器，作为各种不同响应资源的父类
    /// </summary>
    public class HttpResponser : HttpResponse
    {
        private bool _headerWritten = false;

        internal bool HeaderWritten => _headerWritten;

        /// <summary>
        /// 从HttpRequest创建HttpResponser，可直接使用Write输出到客户端；
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static HttpResponser Create(HttpRequest request) {
            return new HttpResponser(200) { BaseStream = request.BaseStream };
        }

        /// <summary>
        /// 从HttpRequest创建HttpResponser，可直接使用Write输出到客户端；
        /// </summary>
        /// <param name="request"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static HttpResponser Create(HttpRequest request, int statusCode)
        {
            return new HttpResponser(statusCode) { BaseStream = request.BaseStream };
        }

        /// <summary>
        /// 使用默认状态码200实例化新的HttpResponser
        /// </summary>
        public HttpResponser() : this(200) { }

        /// <summary>
        /// 使用指定的statusCode实例化新的HttpResponser
        /// </summary>
        /// <param name="statusCode"></param>
        public HttpResponser(int statusCode) : base(statusCode)
        {
            ///设定一些基本标头
            ///null值的标头不会被写入客户端
            ///提前将标头设置为null，可以确保标头写入客户端的顺序会按照设置的顺序来
            _headers["Cache-Control"] = null;
            _headers["Pragma"] = null;
            _headers["Content-Type"] = null;
            _headers["Expires"] = null;
            _headers["Content-Type"] = null;
            _headers["Content-Length"] = null;
            _headers["Content-Encoding"] = null;
            _headers["Content-Range"] = null;
            _headers["Transfer-Encoding"] = null;
            _headers["Server"] = null;
            _headers["X-Powered-By"] = null;
            _headers["Location"] = null;
            _headers["Date"] = DateTime.UtcNow.ToString("r");
            _headers["Sec-WebSocket-Accept"] = null;
            _headers["Upgrade"] = null;
            _headers["Connection"] = null;

        }

        public bool Chunked
        {
            get => TransferEncoding == "chunked";
            set => TransferEncoding = value ? "chunked" : null;
        }
        public string Server
        {
            get => _headers["Server"];
            set => _headers["Server"] = value;
        }
        public override Stream OpenRead() => throw new NotSupportedException();

        public override Stream OpenWrite()
        {
            return BaseStream == null ? throw new InvalidOperationException() : base.OpenWrite();
        }

        internal protected virtual Task<Stream> Commit(HttpRequest request)
        {
            return request
                .BaseStream
                .CommitAsync(this)
                .ContinueWith(task => task.Exception == null ? OpenWrite() : throw task.Exception.GetBaseException());
        }

        #region 同步输出数据到客户端的一些列方法 
        private Stream GetResponseStream()
        {
            if (BaseStream == null) throw new InvalidOperationException();

            if (!_headerWritten)
            {
                Chunked = true;
                ContentLength = -1;
                byte[] buffer = Encoding.UTF8.GetBytes(GetAllHeaders());

                BaseStream.Write(buffer, 0, buffer.Length);
                _headerWritten = true;
            }
            return base.OpenWrite();
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            Stream stream = GetResponseStream();
            stream.Write(buffer, offset, count);
        }

        public void Write(byte[] buffer)
        {
            Write(buffer, 0, buffer.Length);
        }

        public void Write(string message)
        {
            Write(message, Encoding.UTF8);
        }

        public void Write(string message, Encoding encoding) {
            Write(encoding.GetBytes(message));
        }

        public void Write(Stream input)
        {
            Stream stream = GetResponseStream();
            input.CopyTo(stream);
        }
        #endregion
    }
}
