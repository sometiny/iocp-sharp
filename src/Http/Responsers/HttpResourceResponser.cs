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
        public HttpResourceResponser(HttpRequest request, string root)
        {
            string path = request.Path;

            ///处理下非安全的路径
            if (path.IndexOf("..") >= 0 || !path.StartsWith("/"))
            {
                throw new HttpResponseException("不安全的路径访问", 400);
            }


            string filePath = Path.GetFullPath(Path.Combine(root, "." + path));
            if (filePath.IndexOf(".") == -1)
            {
                throw new HttpResponseException($"请求的资源'{request.Path}'不存在。", 404);
            }

            var file = new FileInfo(filePath);
            string mimeType = MimeTypes.GetMimeType(file.Extension);

            if (string.IsNullOrEmpty(mimeType))
            {
                throw new HttpResponseException($"请求的资源'{request.Path}'不存在。", 404);
            }

            if (!file.Exists)
            {
                throw new HttpResponseException($"请求的资源'{request.Path}'不存在。", 404);
            }
            _file = file;
            _mime = mimeType;
        }
        protected internal override Stream CommitTo(HttpRequest request)
        {
            ContentType = _mime;
            ContentLength = _file.Length;
            Stream output = base.CommitTo(request);
            using (Stream input = _file.OpenRead())
            {
                input.CopyTo(output);
            }
            return output;
        }
    }
}
