using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IocpSharp.Http
{
    public enum HttpContentEncoding
    {
        None = 0,
        Gzip = 1, 
        Deflate = 2, 
        Br = 4,
        UnInitialized = 256
    }
}
