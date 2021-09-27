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
    /// HTTP错误应答器
    /// </summary>
    public class HttpRedirectResponser : HttpTextResponser
    {
        private string _location = null;
        public HttpRedirectResponser(string location, string message) : this(location, message, 301)
        {
        }
        public HttpRedirectResponser(string location, string message, int statusCode) : base(message, statusCode)
        {
            _location = location ?? throw new ArgumentNullException("location");
            SetHeader("Location", _location);
        }
    }
}
