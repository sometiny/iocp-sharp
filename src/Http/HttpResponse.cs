using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Specialized;
using IocpSharp.Http.Streams;
using System.IO.Compression;

namespace IocpSharp.Http
{
    public class HttpResponse : HttpMessage
    {

        private int _statusCode = 200;
        private string _statusText = "OK";
        private bool _ignoreContentEncoding = false;

        public int StatusCode
        {
            get => _statusCode;
            set
            {
                string statusText = HttpStatus.GetStatus(value);

                if (string.IsNullOrEmpty(statusText))
                    throw new Exception($"未知状态码：{value}");

                _statusCode = value;
                _statusText = statusText;
            }
        }

        public bool IgnoreContentEncoding { get => _ignoreContentEncoding; set => _ignoreContentEncoding = value; }

        public HttpResponse() : base() { }
        public HttpResponse(HttpStream baseStream) : base(baseStream) { }
        public HttpResponse(int statusCode) : base("HTTP/1.1")
        {
            StatusCode = statusCode;
        }
        /// <summary>
        /// 使用状态码和协议实例化一个HttpResponse类
        /// </summary>
        /// <param name="statusCode">状态码，200、400等</param>
        /// <param name="httpProtocol">协议，默认用HTTP/1.1</param>
        public HttpResponse(int statusCode, string httpProtocol) : base(httpProtocol)
        {
            StatusCode = statusCode;
        }

        private HttpContentEncoding _contentEncoding = HttpContentEncoding.UnInitialized;
        public HttpContentEncoding ContentEncoding
        {
            get
            {
                if (_contentEncoding != HttpContentEncoding.UnInitialized) return _contentEncoding;
                string contentEncoding = GetHeader("Content-Encoding");
                if (string.IsNullOrEmpty(contentEncoding)) return _contentEncoding = HttpContentEncoding.None;
                return _contentEncoding = (contentEncoding.ToLower()) switch
                {
                    "gzip" => HttpContentEncoding.Gzip,
                    "deflate" => HttpContentEncoding.Deflate,
                    "br" => HttpContentEncoding.Br,
                    _ => throw new NotSupportedException("不支持的内容编码：" + contentEncoding),
                };
            }
            set
            {
                if ((value & HttpContentEncoding.UnInitialized) > 0) throw new InvalidOperationException("不允许设置UnInitialized");
                SetHeader("Content-Encoding", (value) switch
                {
                    HttpContentEncoding.Gzip => "gzip",
                    HttpContentEncoding.Deflate => "deflate",
                    HttpContentEncoding.Br => "br",
                    _ => throw new NotSupportedException("不支持的内容编码：" + value),
                });
                _contentEncoding = value;
            }
        }

        protected override void ParseFirstLine(string line)
        {
            int idx = line.IndexOf(' '), idx2 = 0;
            if (idx <= 0 || idx >= line.Length - 1)
            {
                throw new HttpHeaderException(HttpHeaderError.NotWellFormed, "响应行格式错误：" + line);
            }
            HttpProtocol = line.Substring(0, idx);
            idx2 = line.IndexOf(' ', idx + 1);
            if (idx2 <= 0 || idx2 >= line.Length - 1)
            {
                if (!int.TryParse(line.Substring(idx + 1), out _statusCode))
                {
                    throw new HttpHeaderException(HttpHeaderError.StatusCodeNotWellFormed);
                }
                _statusText = HttpStatus.GetStatus(_statusCode);
                return;
            }
            _statusText = line.Substring(idx2 + 1);

            string code = line.Substring(idx + 1, idx2 - idx - 1);

            if (!int.TryParse(code, out _statusCode))
            {
                throw new HttpHeaderException(HttpHeaderError.StatusCodeNotWellFormed);
            }
        }

        protected override string GetAllHeaders(StringBuilder sb)
        {
            sb.AppendFormat("{0} {1} {2}\r\n", HttpProtocol, _statusCode, _statusText);
            return base.GetAllHeaders(sb);
        }

        /// <summary>
        /// 获获取完整的响应头
        /// </summary>
        /// <returns>完整响应头</returns>
        public string GetAllResponseHeaders()
        {
            return GetAllHeaders();
        }

        /// <summary>
        /// 读取下一个响应
        /// </summary>
        /// <returns></returns>
        public void Next(HttpMessageReadCallback<HttpResponse> callback, object state)
        {
            Next<HttpResponse>(callback, state);
        }

        private Stream _entityReadStream = null;
        private Stream _entityWriteStream = null;
        public override Stream OpenRead()
        {
            if(_ignoreContentEncoding) return base.OpenRead();
            if (_entityReadStream != null) return _entityReadStream;

            Stream stream = base.OpenRead();

            return _entityReadStream = (ContentEncoding) switch
            {
                HttpContentEncoding.Gzip => new GZipStream(stream, CompressionMode.Decompress, true),
                HttpContentEncoding.Deflate => new DeflateStream(stream, CompressionMode.Decompress, true),
                HttpContentEncoding.Br => throw new NotSupportedException("暂不支持Br"),
                _ => stream,
            };
        }

        public override Stream OpenWrite()
        {
            if (_ignoreContentEncoding) return base.OpenWrite();
            if (_entityWriteStream != null) return _entityWriteStream;

            Stream stream = base.OpenWrite();

            return _entityWriteStream = (ContentEncoding) switch
            {
                HttpContentEncoding.Gzip => new GZipStream(stream, CompressionMode.Compress, true),
                HttpContentEncoding.Deflate => new DeflateStream(stream, CompressionMode.Compress, true),
                HttpContentEncoding.Br => throw new NotSupportedException("暂不支持Br"),
                _ => stream,
            };
        }
        protected override void Dispose(bool disposing)
        {
            _entityReadStream?.Dispose();
            _entityReadStream = null;
            _entityWriteStream?.Dispose();
            _entityWriteStream = null;
            base.Dispose(disposing);
        }

    }
}

