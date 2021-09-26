using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using IocpSharp.Http.Streams;
using IocpSharp.Http.Utils;

namespace IocpSharp.Http.Responsers
{
    /// <summary>
    /// HTTP文本应答器
    /// </summary>
    public class HttpResourceResponser : HttpResponser
    {
        private FileInfo _file = null;
        private string _mime = null;
        public HttpResourceResponser(string file) : this(new FileInfo(file))
        { }
        public HttpResourceResponser(FileInfo file) : base(200)
        {
            string mimeType = MimeTypes.GetMimeType(file.Extension);

            if (string.IsNullOrEmpty(mimeType))
            {
                throw new HttpResponseException($"请求的资源不存在。", 404);
            }

            if (!file.Exists)
            {
                throw new HttpResponseException($"请求的资源不存在。", 404);
            }
            _file = file;
            _mime = mimeType;
        }

        public override Stream OpenWrite()
        {
            Stream output = base.OpenWrite();
            using (Stream input = _file.OpenRead())
            {
                input.CopyTo(output);
            }
            return output;
        }

        protected internal override Task<Stream> CommitTo(HttpRequest request)
        {
            ContentType = _mime;
            ContentLength = _file.Length;
            return base.CommitTo(request);
        }
    }
}
