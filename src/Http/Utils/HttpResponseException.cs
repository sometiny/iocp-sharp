using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IocpSharp.Http.Responsers;

namespace IocpSharp.Http.Utils
{
    internal class HttpResponseException : Exception
    {
        private HttpResponser _responser = null;

        public HttpResponser Responser => _responser;
        public HttpResponseException(HttpResponser responser)
        {
            _responser = responser ?? throw new ArgumentNullException("responser");
        }
        public HttpResponseException(string message, int statucCode)
        {
            _responser = new HttpErrorResponser(message, statucCode);
        }
    }
}
