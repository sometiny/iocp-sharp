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

        /// <summary>
        /// 结束请求，关闭基础流
        /// </summary>
        /// <param name="request"></param>
        private void EndRequest(HttpRequest request)
        {

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
                    EndRequest(request);
                    return;
                }
                //客户端发送的请求异常
                Next(request, new HttpErrorResponser($"请求异常：{httpHeaderException.Error}", 400));
                return;
            }
            Next(request, new HttpErrorResponser($"请求异常：{e}", 500));
        }

        /// <summary>
        /// 请求处理
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private void ProcessRequest(HttpRequest request)
        {
            if (!request.IsWebSocket)
            {
                NewRequest(request);
                return;
            }

            //如果是WebSocket，调用相应的处理方法
            if (OnWebSocketInternal(request)) return;

            EndRequest(request);
        }

        /// <summary>
        /// 捕获到新请求
        /// </summary>
        /// <param name="request"></param>
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
                EndRequest(request);
            }
        }

        /// <summary>
        /// 发送响应，处理下一个请求
        /// </summary>
        /// <param name="request">当前请求</param>
        /// <param name="response">响应</param>
        protected void Next(HttpRequest request, HttpResponser response)
        {
            //应用已经直接响应客户端，直接继续下一个请求
            if (response.HeaderWritten)
            {
                InternalNext(request, response, response.OpenWrite());
                return;
            }
            response.Commit(request).ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    EndRequest(request);
                    return;
                }
                InternalNext(request, response, task.Result);
            });
        }
        
        private void InternalNext(HttpRequest request, HttpResponser response, Stream output)
        {
            output?.Close();
            if (response.IsChunked)
            {
                request.BaseStream.Write(_endingChunk, 0, 5);
            }
            //超过单连接处理请求数，停止后续处理。
            //请求不是KeepAlive，停止后续处理
            //响应不是KeepAlive，停止后续处理
            if (!request.KeepAlive || !response.KeepAlive || request.BaseStream.CapturedMessage >= MaxRequestPerConnection)
            {
                EndRequest(request);
                return;
            }
            request.Next(AfterReceiveHttpMessage, null);
        }

        /// <summary>
        /// 读取到Http消息
        /// </summary>
        /// <param name="e"></param>
        /// <param name="request"></param>
        /// <param name="state"></param>
        private void AfterReceiveHttpMessage(Exception e, HttpRequest request, object state)
        {
            if (e != null)
            {
                ProcessRequestException(e, request);
                return;
            }

            ProcessRequest(request);
        }

        /// <summary>
        /// 新客户端
        /// </summary>
        /// <param name="client"></param>
        protected override void NewClient(Socket client)
        {
            HttpStream stream = new HttpStream(new BufferedNetworkStream(client, true), false);

            stream.Capture<HttpRequest>(AfterReceiveHttpMessage, null);
        }

        /// <summary>
        /// 发送服务器资源，这里简单处理下。
        /// 必要的情况下可以作缓存处理
        /// </summary>
        /// <param name="request"></param>
        private void OnResource(HttpRequest request)
        {
            Next(request, new HttpResourceResponser(request, _webRoot));
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
            if (string.IsNullOrEmpty(webSocketKey))
            {
                Next(request, new HttpErrorResponser("header 'Sec-WebSocket-Key' error", 400));
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

            //设置Sec-WebSocket-Accept头
            responser.SetHeader("Sec-WebSocket-Accept", secWebSocketAcceptKey);
            request.BaseStream.CommitAsync(responser).ContinueWith((task, state) =>
            {

                HttpRequest req = state as HttpRequest;
                if (task.Exception != null)
                {
                    EndRequest(req);
                    return;
                }
                //开始WebSocket消息的接收和发送
                Messager messager = GetMessager(req, req.BaseStream);
                if (messager != null) messager.Accept();
            }, request);
            return true;
        }

        /// <summary>
        /// WebSocket消息处理程序
        /// </summary>
        /// <param name="request"></param>
        /// <param name="stream"></param>
        protected virtual Messager GetMessager(HttpRequest request, HttpStream baseStream)
        {
            baseStream?.Close();
            return null;
        }
    }
}
