using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using IocpSharp.Http.Responsers;
using IocpSharp.Http.Streams;
using System.IO.Compression;
using IocpSharp.Server;
using IocpSharp.Http.Utils;
using IocpSharp.WebSocket;

namespace IocpSharp.Http
{
    //我们独立出一个基类来，以后新的服务继承本类就好
    public class HttpServerBase : TcpIocpServer
    {
        private static int MaxRequestPerConnection = 20;
        //结束包内容
        internal static byte[] _endingChunk = Encoding.ASCII.GetBytes("0\r\n\r\n");

        private string _webRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web"));
        private static string _uplaodTempDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads"));

        public string WebRoot { get => _webRoot; set => _webRoot = value; }
        public static string UplaodTempDir { get => _uplaodTempDir; set => _uplaodTempDir = value; }

        //后面的代码可能会越来越复杂，我们做个简单的路由功能
        //可以开发功能更强大的路由
        private Dictionary<string, Action<HttpRequest>> _routes = new Dictionary<string, Action<HttpRequest>>();

        public HttpServerBase() : base()
        {
        }
        protected override void Start()
        {
            if (!Directory.Exists(_webRoot)) throw new Exception($"网站根目录不存在，请手动创建：{_webRoot}");

            base.Start();
        }

        public void RegisterRoute(string path, Action<HttpRequest> route)
        {
            _routes[path] = route;
        }

        private void EndProcessRequest(HttpRequest request) {

            request?.BaseStream?.Close();
            request?.Dispose();
        }

        /// <summary>
        /// 异常处理
        /// </summary>
        /// <param name="e"></param>
        /// <param name="request"></param>
        private void ProcessRequestException(Exception e, HttpRequest request)
        {
            if (e is HttpHeaderException httpHeaderException)
            {
                if (httpHeaderException.Error == HttpHeaderError.ConnectionLost)
                {
                    return;
                }
                //客户端发送的请求异常
                OnBadRequest(request, $"请求异常：{httpHeaderException.Error}");
            }
            else
            {
                //其他异常
                OnServerError(request, $"请求异常：{e}");
            }
        }

        /// <summary>
        /// 请求处理
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private bool ProcessRequest(HttpRequest request)
        {

            if (request == null) return false;

            //如果是WebSocket，调用相应的处理方法
            if (request.IsWebSocket)
            {
                if (!OnWebSocketInternal(request))
                {
                    return false;
                }
                return true;
            }
            NewRequest(request);
            return true;
        }

        protected virtual void NewRequest(HttpRequest request)
        {
            //尝试查找路由，不存在的话使用NotFound路由
            if (!_routes.TryGetValue(request.Path, out Action<HttpRequest> handler))
            {
                //未匹配到路由，统一当文件资源处理
                handler = OnResource;
            }

            try
            {
                handler(request);
            }
            catch (HttpResponseException ex)
            {
                Next(request, ex.Responser);
            }
            catch
            {
                EndProcessRequest(request);
            }
        }
        protected void Next(HttpRequest request, HttpResponser response)
        {
            response.KeepAlive = request.Connection != "close";
            try
            {
                Stream output = response.CommitTo(request);

                output?.Close();
                if (response.IsChunked)
                {
                    request.BaseStream.Write(_endingChunk, 0, 5);
                }
                //超过单连接处理请求数，停止后续处理。
                if (!response.KeepAlive || request.BaseStream.CapturedMessage >= MaxRequestPerConnection)
                {
                    EndProcessRequest(request);
                    return;
                }
                request.Next().ContinueWith(AfterReceiveHttpMessage);
            }
            catch
            {
                EndProcessRequest(request);
            }
        }

        private void AfterReceiveHttpMessage(Task<HttpRequest> task)
        {
            Exception e = task.Exception?.GetBaseException();
            HttpRequest request = task.Result;

            if (e != null)
            {
                ProcessRequestException(e, request);
                EndProcessRequest(request);
                return;
            }

            if (!ProcessRequest(request))
            {
                EndProcessRequest(request);
            }
        }

        protected override void NewClient(Socket client)
        {
            HttpStream stream = new HttpStream(new BufferedNetworkStream(client, true), false);

            stream.Capture<HttpRequest>().ContinueWith(AfterReceiveHttpMessage);
        }

        /// <summary>
        /// 响应404错误
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private void OnNotFound(HttpRequest request)
        {
            Next(request, new HttpErrorResponser($"请求的资源'{request.Path}'不存在。", 404));
        }


        /// <summary>
        /// 请求异常
        /// </summary>
        /// <param name="request"></param>
        /// <param name="message"></param>
        private void OnBadRequest(HttpRequest request, string message)
        {
            Next(request, new HttpErrorResponser(message, 400));
        }

        /// <summary>
        /// 服务器异常
        /// </summary>
        /// <param name="request"></param>
        /// <param name="message"></param>
        private void OnServerError(HttpRequest request, string message)
        {
            Next(request, new HttpErrorResponser(message, 500));
        }

        /// <summary>
        /// 发送服务器资源，这里简单处理下。
        /// 必要的情况下可以作缓存处理
        /// </summary>
        /// <param name="request"></param>
        private void OnResource(HttpRequest request)
        {
            string path = request.Path;

            ///处理下非安全的路径
            if (path.IndexOf("..") >= 0 || !path.StartsWith("/"))
            {
                OnBadRequest(request, "不安全的路径访问");
                return;
            }


            string filePath = Path.GetFullPath(Path.Combine(_webRoot, "." + path));
            if (filePath.IndexOf(".") == -1)
            {
                OnNotFound(request);
                return;
            }

            Next(request, new HttpResourceResponser(filePath));
        }

        /// <summary>
        /// 处理WebSocket
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        private bool OnWebSocketInternal(HttpRequest request)
        {
            string webSocketKey = request.Headers["Sec-WebSocket-Key"];
            if(string.IsNullOrEmpty(webSocketKey))
            {
                OnBadRequest(request, "header 'Sec-WebSocket-Key' error");
                return false;
            }

            //获取客户端发送来的Sec-WebSocket-Key字节数组
            byte[] keyBytes = Encoding.ASCII.GetBytes(webSocketKey);

            //拼接上WebSocket的Salt，固定值：258EAFA5-E914-47DA-95CA-C5AB0DC85B11
            keyBytes = keyBytes.Concat(ProtocolUtils.Salt).ToArray();

            //计算HASH值，作为响应给客户端的Sec-WebSocket-Accept
            string secWebSocketAcceptKey = ProtocolUtils.SHA1(keyBytes);

            //响应101状态码给客户端
            HttpResponser responser = new HttpResponser(101);
            responser.Connection = "Upgrade";
            responser.Upgrade = "websocket";
            responser.KeepAlive = false;

            //设置Sec-WebSocket-Accept头
            responser.SetHeader("Sec-WebSocket-Accept", secWebSocketAcceptKey);

            request.BaseStream.Commit(responser);
            //开始WebSocket消息的接收和发送
            Messager messager = GetMessager(request);
            if (messager != null) messager.Accept();
            return true;
        }

        /// <summary>
        /// WebSocket消息处理程序
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>
        protected virtual Messager GetMessager(HttpRequest request) {
            request.BaseStream?.Close();
            return null;
        }
    }
}
