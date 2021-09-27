using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Specialized;
using IocpSharp.Http.Streams;
using IocpSharp.Http.Utils;

namespace IocpSharp.Http
{
    public abstract class HttpMessage : IDisposable
    {
        private string _httpProtocol = null;
        private string _originHeaders = "HTTP/1.1";
        internal protected NameValueCollection _headers = new NameValueCollection();
        private HttpStream _baseStream = null;

        public string HttpProtocol { get => _httpProtocol; internal set => _httpProtocol = value; }
        public string OriginHeaders => _originHeaders;
        public NameValueCollection Headers => _headers;


        internal HttpMessage()
        {
        }
        internal HttpMessage(HttpStream baseStream)
        {
            _baseStream = baseStream;
        }
        public HttpMessage(string httpProtocol)
        {
            _httpProtocol = httpProtocol;
        }
        public HttpMessage(HttpStream baseStream, string httpProtocol)
        {
            _baseStream = baseStream;
            _httpProtocol = httpProtocol;
        }

        internal HttpStream BaseStream { get => _baseStream; set => _baseStream = value; }

        /// <summary>
        /// 设置或获取Keep-Alive
        /// </summary>
        public bool KeepAlive
        {
            get
            {
                return Connection == null || Connection.ToLower() != "close";
            }
            set
            {
                Connection = KeepAlive ? null : "close";
            }
        }
        /// <summary>
        /// 设置或获取Connection
        /// </summary>
        public string Connection
        {
            get => _headers["Connection"];
            set => _headers["Connection"] = value;
        }

        /// <summary>
        /// 设置或获取Upgrade
        /// </summary>
        public string Upgrade
        {
            get => _headers["Upgrade"];
            set => _headers["Upgrade"] = value;
        }

        /// <summary>
        /// 设置或获取日期
        /// </summary>
        public DateTime? Date
        {
            get
            {
                string date = _headers["date"];
                if (string.IsNullOrEmpty(date)) return null;
                return DateTime.Parse(date);
            }
            set
            {
                if (!value.HasValue)
                {
                    _headers["Date"] = null;
                    return;
                }
                _headers["Date"] = value.Value.ToUniversalTime().ToString("r");
            }
        }

        /// <summary>
        /// 获取标头值
        /// </summary>
        /// <param name="name">标头名称</param>
        /// <returns></returns>
        public string GetHeader(string name)
        {
            return _headers[name];
        }

        /// <summary>
        /// 获取标头值列表，例如Set-Cookie这样允许重复的标头
        /// </summary>
        /// <param name="name">标头名称</param>
        /// <returns></returns>
        public string[] GetHeaders(string name)
        {
            return _headers.GetValues(name);
        }

        /// <summary>
        /// 设置标头值
        /// </summary>
        /// <param name="name">标头名称</param>
        /// <param name="value">标头值</param>
        public void SetHeader(string name, string value)
        {
            _headers[name] = value;
        }

        /// <summary>
        /// 添加标头
        /// </summary>
        /// <param name="name">标头名称</param>
        /// <param name="value">标头值</param>
        public void AddHeader(string name, string value)
        {
            _headers.Add(name, value);
        }

        /// <summary>
        /// 移除标头
        /// </summary>
        /// <param name="name">标头名称</param>
        public void RemoveHeader(string name)
        {
            _headers.Remove(name);
        }

        /// <summary>
        /// 获取所有标头
        /// </summary>
        /// <returns></returns>
        internal string GetAllHeaders()
        {
            StringBuilder sb = new StringBuilder();
            return GetAllHeaders(sb);
        }

        /// <summary>
        /// 把所有标头保存到StringBuilder
        /// </summary>
        /// <param name="sb"></param>
        /// <returns></returns>
        protected virtual string GetAllHeaders(StringBuilder sb)
        {
            foreach (string name in _headers.Keys)
            {
                string[] values = _headers.GetValues(name);
                if (values == null || values.Length == 0) continue;

                foreach (string value in values)
                {
                    if (value == null) continue;
                    sb.AppendFormat("{0}: {1}\r\n", name, value);
                }
            }
            sb.Append("\r\n");

            return sb.ToString();
        }

        /// <summary>
        /// 这里只是简单的给源请求数据补了新行
        /// 可以在Ready方法里面做更多事情
        /// 例如解析Host、ContentLength、ContentType、AcceptEncoding、Connection以及Range等请求头
        /// </summary>
        protected virtual void Ready()
        {
            _originHeaders += "\r\n";

            //解析Content-Length
            ParseContentLength(_headers["content-length"]);

            //获取Transfer-Encoding
            _transferEncoding = _headers["transfer-encoding"];
            if (_transferEncoding != null) _transferEncoding = _transferEncoding.ToLower();

            //解析Content-Type
            ParseContentType(_headers["content-type"]);
        }

        /// <summary>
        /// 解析ContentLength
        /// </summary>
        /// <param name="header"></param>
        private void ParseContentLength(string header)
        {
            if (string.IsNullOrEmpty(header)) return;
            if (!long.TryParse(header, out _contentLength))
            {
                throw new HttpHeaderException(HttpHeaderError.ContentLengthError);
            }
        }

        /// <summary>
        /// 解析ContentType
        /// </summary>
        /// <param name="header"></param>
        private void ParseContentType(string header)
        {
            if (string.IsNullOrEmpty(header)) return;
            _contentType = HttpHeaderProperty.Parse(header);
        }

        private HttpHeaderProperty _contentType = null;
        private long _contentLength = -1;
        private string _transferEncoding = null;

        public bool IsMultipart => _contentType != null && _contentType.Value == "multipart/form-data" && !string.IsNullOrEmpty(_contentType["boundary"]);
        public string Boundary => _contentType == null ? null : _contentType["boundary"];

        public bool IsChunked => _transferEncoding != null && _transferEncoding == "chunked";

        /// <summary>
        /// 读取和设置TransferEncoding标头
        /// </summary>
        public string TransferEncoding
        {
            get => _transferEncoding;
            set
            {
                if (value == null)
                {
                    _transferEncoding = null;
                    _headers.Remove("Transfer-Encoding");
                    return;
                }
                string encoding = value.ToLower();
                if (encoding != "chunked") throw new HttpHeaderException(HttpHeaderError.UnknownTransferEncoding);

                _transferEncoding = encoding;
                _headers["Transfer-Encoding"] = encoding;
            }
        }

        /// <summary>
        /// 读取和设置ContentLength标头
        /// </summary>
        public long ContentLength
        {
            get => _contentLength;
            set
            {
                _contentLength = value;
                if (_contentLength == -1)
                {
                    _headers.Remove("Content-Length");
                    return;
                }
                _headers["Content-Length"] = value.ToString();
            }
        }
        /// <summary>
        /// 读取和设置ContentType标头
        /// </summary>
        public string ContentType
        {
            get => _contentType == null ? null : _contentType.Value;
            set
            {
                _headers["Content-Type"] = value;
                ParseContentType(value);
            }
        }

        private byte[] ReadBody()
        {

            using MemoryStream output = new MemoryStream();
            using (Stream input = OpenRead())
            {
                input.CopyTo(output);
            }
            return output.ToArray();
        }

        private byte[] _messageBody = null;

        /// <summary>
        /// 读取请求实体
        /// 需要把数据缓存起来
        /// </summary>
        public byte[] Body
        {
            get
            {
                if (_messageBody != null) return _messageBody;
                return _messageBody = ReadBody();
            }
        }

        /// <summary>
        /// 确认请求是否包含消息
        /// </summary>
        public bool HasEntityBody => _contentLength > 0
            || !string.IsNullOrEmpty(_transferEncoding);


        private Stream _entityReadStream = null;
        private Stream _entityWriteStream = null;
        public virtual Stream OpenRead()
        {
            return OpenReadInternal();
        }

        internal Stream OpenReadInternal()
        {
            if (_entityReadStream != null)
            {
                return _entityReadStream;
            }
            if (!HasEntityBody) return _entityReadStream = new ContentedReadStream(0, _baseStream, true);

            //如果同时出现transfer-encoding和content-length
            //优先处理transfer-encoding，忽略content-length
            if (!string.IsNullOrEmpty(_transferEncoding))
            {
                //返回一个ChunkedReadStream
                return _entityReadStream = new ChunkedReadStream(_baseStream, true);
            }

            //返回一个ContentedReadStream
            return _entityReadStream = new ContentedReadStream(_contentLength, _baseStream, true);
        }

        public virtual Stream OpenWrite()
        {
            if (_entityWriteStream != null)
            {
                return _entityWriteStream;
            }
            if (!HasEntityBody)
            {
                return _entityWriteStream = new ContentedWriteStream(_baseStream, 0, true);
            }

            //如果同时出现transfer-encoding和content-length
            //优先处理transfer-encoding，忽略content-length
            if (!string.IsNullOrEmpty(_transferEncoding))
            {
                //返回一个ChunkedWriteStream
                return _entityWriteStream = new ChunkedWriteStream(_baseStream, true);
            }

            //返回一个ContentedWriteStream
            return _entityWriteStream = new ContentedWriteStream(_baseStream, _contentLength, true);
        }

        /// <summary>
        /// 关于请求实体，RFC2616有一句‘A server SHOULD read and forward a message-body on any request;’
        /// 对于任何request，服务端应该将请求实体“读完”
        /// 我们必须得这么做，也就是将OpenRead打开的流读完
        /// 否则在KeepAlive保持的长链里，极有可能造成“脏读”，导致下一个request没法被正常解析
        /// </summary>
        public void EnsureEntityBodyRead()
        {
            //没有请求实体或者数据已经被读了，忽略
            using Stream forward = OpenReadInternal();
            byte[] forwardBytes = new byte[32768];
            //读取，丢弃
            while (forward.Read(forwardBytes, 0, 32768) > 0) ;
        }
        internal void Next<T>(HttpMessageReadCallback<T> callback, object state) where T : HttpMessage, new()
        {
            try
            {
                EnsureEntityBodyRead();
                _baseStream.CaptureNext<T>(callback, state);
            }
            finally
            {
                Dispose();
            }
        }

        private bool _firstLineParsed = false;
        /// <summary>
        /// 解析首行，例如：GET / HTTP/1.1
        /// </summary>
        /// <param name="line"></param>
        protected abstract void ParseFirstLine(string line);

        /// <summary>
        /// 解析请求头的每一行
        /// </summary>
        /// <param name="line">行</param>
        internal bool ParseLine(string line)
        {
            if (!_firstLineParsed && string.IsNullOrEmpty(line)) return false;
            if (string.IsNullOrEmpty(line))
            {
                Ready();
                return true;
            }
            _originHeaders += line + "\r\n";

            //首行包含请求方法，url和协议等。
            if (!_firstLineParsed)
            {
                ParseFirstLine(line);
                _firstLineParsed = true;
                return false;
            }

            //解析后续数据，行格式(Key: Value)
            //冒号分割的请求行
            int rowIdx = line.IndexOf(':');
            if (rowIdx <= 0 || rowIdx == line.Length - 1)
                throw new HttpHeaderException(HttpHeaderError.NotWellFormed, "行格式错误");


            _headers.Add(line.Substring(0, rowIdx).Trim(), line.Substring(rowIdx + 1).Trim());
            return false;
        }


        ~HttpMessage()
        {
            Dispose(false);
        }

        /// <summary>
        /// 清理工作，必要的时候关闭下实体读取的流
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            _entityReadStream?.Dispose();
            _entityWriteStream?.Dispose();
            _headers?.Clear();
            if (disposing)
            {
                _baseStream = null;
                _entityReadStream = null;
                _entityWriteStream = null;
                _headers = null;
                _originHeaders = null;
                _messageBody = null;
                _contentType = null;
            }
        }

        /// <summary>
        /// 实现Dispose，做一些必要的清理工作
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
