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
    public class HttpRequest : HttpMessage
    {

        private string _method = null;
        private string _url = null;

        public string Method => _method;
        public string Url => _url;


        public HttpRequest() : base() { }
        public HttpRequest(Stream baseStream): base(baseStream) {}
        public HttpRequest(string url, string method = "GET", string httpProtocol = "HTTP/1.1") : base(httpProtocol)
        {
            _url = url;
            _method = method;
        }

        protected override void ParseFirstLine(string line)
        {
            //判断第一个空格，用于截取请求方法
            int idx = line.IndexOf(' ');
            if (idx <= 0)
                throw new HttpRequestException(HttpRequestError.NotWellFormed);

            //判断最后一个空格，用于截取协议
            int idxEnd = line.LastIndexOf(' ');
            if (idxEnd <= 0
                || idxEnd == line.Length - 1
                || idx == idxEnd)
                throw new HttpRequestException(HttpRequestError.NotWellFormed);

            //截取请求方法，url和协议
            _method = line.Substring(0, idx);
            HttpProtocol = line.Substring(idxEnd + 1);
            _url = line.Substring(idx + 1, idxEnd - idx - 1).Trim();

            if (string.IsNullOrEmpty(_url))
                throw new HttpRequestException(HttpRequestError.NoneUrl);


            idx = _url.IndexOf('?');
            _path = _url;
            if (idx >= 0)
            {
                _path = _url.Substring(0, idx);
                _query = _url.Substring(idx).TrimStart('?');
            }
        }

        protected override string GetAllHeaders(StringBuilder sb)
        {
            sb.AppendFormat("{0} {1} {2}\r\n", _method, _url, HttpProtocol);
            return base.GetAllHeaders(sb);
        }

        public string GetAllRequestHeaders()
        {
            return GetAllHeaders();
        }
        
        /// <summary>
        /// 判断请求是不是WebSocket请求
        /// </summary>
        public bool IsWebSocket
        {
            get
            {
                string connection = Connection;
                string upgrade = Upgrade;
                return !string.IsNullOrEmpty(connection) 
                    && connection.ToLower() == "upgrade" 
                    && !string.IsNullOrEmpty(upgrade) 
                    && upgrade.ToLower() == "websocket";
            }
        }


        private List<FileItem> _files = null;

        /// <summary>
        /// 获取上传文件的列表
        /// </summary>
        public List<FileItem> Files
        {
            get
            {
                if (!IsMultipart) return _files = new List<FileItem>();
                if(_files == null) ReadUploadContent();
                return _files;
            }
        }

        private void ReadUploadContent()
        {
            var parser = new HttpMultipartFormDataParser(HttpServerBase.UplaodTempDir);
            using Stream input = OpenRead();
            parser.Parse(input, Boundary);
            _form = parser.Forms;
            _files = parser.Files;
        }
        #region QueryString相关参数和属性

        private string _path = null;
        private string _query = null;
        private NameValueCollection _queryString = null;

        public string Path => _path;
        public string Query => _query;
        public NameValueCollection QueryString
        {
            get
            {
                //在获取属性值时才初始化_queryString的值
                if (_queryString != null) return _queryString;

                return _queryString = HttpUtility.ParseUriComponents(_query);
            }
        }

        #endregion

        #region 请求实体相关参数和属性
        private NameValueCollection _form = null;


        /// <summary>
        /// 从请求中读取请求实体
        /// 需要把数据缓存起来
        /// </summary>
        public byte[] RequestBody => Body;

        /// <summary>
        /// 获取表单数据
        /// </summary>
        public NameValueCollection Form
        {
            get
            {
                //在获取属性值时才初始化_form的值
                if (_form != null) return _form;

                if(IsMultipart && _files == null)
                {
                    ReadUploadContent();
                    return _form;
                }

                return _form = HttpUtility.ParseUriComponents(Encoding.UTF8.GetString(Body));
            }
        }

        #endregion

        public HttpRequest Next()
        {
            return Next<HttpRequest>();
        }
        protected override void Dispose(bool disposing)
        {
            _queryString?.Clear();
            _form?.Clear();
            if (disposing)
            {
                _url = null;
                _query = null;
                _queryString = null;
                _form = null;
            }
            base.Dispose(disposing);
        }
    }
}
