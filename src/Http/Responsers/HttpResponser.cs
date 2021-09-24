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
        public HttpResponser() : base(200) { }

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
            _headers["Connection"] = null;

        }

        public bool KeepAlive
        {
            get
            {
                return Connection == null || Connection.ToLower() != "close";
            }
            set
            {
                Connection = KeepAlive ? "keep-alive" : "close";
            }
        }

        public bool Chunked
        {
            set => TransferEncoding = value ? "chunked" : null;
        }
        public string Server
        {
            get => _headers["Server"];
            set => _headers["Server"] = value;
        }
    }
}
