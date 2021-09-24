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
    public class HttpResourceResponser : HttpResponser
    {
        private FileInfo _file = null;
        private string _mime = null;
        public HttpResourceResponser(FileInfo file, string mime) : base(200)
        {
            _file = file;
            _mime = mime;
        }

        protected override string GetAllHeaders(StringBuilder sb)
        {
            ContentType = _mime;
            ContentLength = _file.Length;

            return base.GetAllHeaders(sb);
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
    }
}
