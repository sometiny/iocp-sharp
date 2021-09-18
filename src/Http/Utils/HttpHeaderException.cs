using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IocpSharp.Http
{
    public enum HttpHeaderError
    {
        None,
        NotWellFormed,
        LineLengthExceedsLimit,
        ConnectionLost,
        ContentLengthError,
        ResourcePathError,
        ResourceMimeError,
        UnknownTransferEncoding,
        StatusCodeNotWellFormed,
        NoneUrl
    }
    public class HttpHeaderException : Exception
    {
        private HttpHeaderError _error = HttpHeaderError.None;

        public HttpHeaderError Error => _error;
        public HttpHeaderException(HttpHeaderError error) : base()
        {
            _error = error;
        }
        public HttpHeaderException(HttpHeaderError error, string message) : base(message)
        {
            _error = error;
        }
        public HttpHeaderException(HttpHeaderError error, string message, Exception innerException) : base(message, innerException)
        {
            _error = error;
        }
        public override string Message => _error.ToString() + " => "+ base.Message;
    }
}
