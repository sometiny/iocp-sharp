using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Specialized;

namespace IocpSharp.Http
{
    public class HttpResponse : HttpMessage
    {

        private int _statusCode = 200;
        private string _statusText = "OK";

        public int StatusCode { 
            get => _statusCode; 
            set {
                string statusText = HttpStatus.GetStatus(value);

                if (string.IsNullOrEmpty(statusText))
                    throw new Exception($"未知状态码：{value}");

                _statusCode = value;
                _statusText = statusText;
            }
        }

        internal HttpResponse(Stream baseStream) : base(baseStream) { }
        /// <summary>
        /// 使用状态码和协议实例化一个HttpResponse类
        /// </summary>
        /// <param name="statusCode">状态码，200、400等</param>
        /// <param name="httpProtocol">协议，默认用HTTP/1.1</param>
        public HttpResponse(int statusCode, string httpProtocol = "HTTP/1.1"): base(httpProtocol)
        {
            StatusCode = statusCode;
        }
        protected override void ParseFirstLine(string line)
        {
            int idx = line.IndexOf(' '), idx2 = 0;
            if (idx <= 0 || idx >= line.Length - 1)
            {
                throw new HttpHeaderException(HttpHeaderError.NotWellFormed);
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
    }
}
