using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace IocpSharp
{
    /// <summary>
    /// TcpSocketAsyncEventArgs类用于数据的异步读写，不需要事件，直接内部重写OnCompleted方法。
    /// 异步读写BeginWrite、EndWrite、BeginRead、EndRead专用，不能用于Socket.ConnectAsync、Socket.ReceiveAsync、Socket.SendAsync等异步方法
    /// </summary>
    public class TcpSocketAsyncEventArgs : SocketAsyncEventArgs
    {
        /// <summary>
        /// 重写SocketAsyncEventArgs的OnCompleted方法
        /// 实现我们自己的逻辑
        /// </summary>
        /// <param name="e"></param>
        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            if (UserToken is not TcpReadWriteResult asyncResult) throw new InvalidOperationException("asyncResult");

            if (e.SocketError != SocketError.Success)
            {
                asyncResult.SetFailed(new SocketException((int)e.SocketError));
                return;
            }
            asyncResult.BytesTransfered = e.BytesTransferred;
            asyncResult.CallUserCallback();
        }

        /// <summary>
        /// 开始异步读取数据
        /// </summary>
        /// <param name="socket">基础Socket</param>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">数据在缓冲区中的索引</param>
        /// <param name="size">准备读取的数据大小</param>
        /// <param name="callback">回调</param>
        /// <param name="state">状态</param>
        /// <returns></returns>
        public IAsyncResult BeginRead(Socket socket, byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            TcpReadWriteResult asyncResult = new TcpReadWriteResult(callback, state, buffer, offset, size);
            UserToken = asyncResult;
            SetBuffer(buffer, offset, size);
            if (!socket.ReceiveAsync(this))
            {
                OnCompleted(this);
            }
            return asyncResult;
        }

        /// <summary>
        /// 结束异步读取数据
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns>读取的字节数</returns>
        public int EndRead(IAsyncResult asyncResult)
        {
            if (asyncResult is not TcpReadWriteResult result) throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null) throw result.Exception;

            return result.BytesTransfered;
        }



        /// <summary>
        /// 开始异步发送数据
        /// </summary>
        /// <param name="socket">基础Socket</param>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">数据在缓冲区中的索引</param>
        /// <param name="size">准备读取的数据大小</param>
        /// <param name="callback">回调</param>
        /// <param name="state">状态</param>
        /// <returns></returns>
        public IAsyncResult BeginWrite(Socket socket, byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            TcpReadWriteResult asyncResult = new TcpReadWriteResult(callback, state, buffer, offset, size);
            UserToken = asyncResult;
            SetBuffer(buffer, offset, size);
            if (!socket.SendAsync(this))
            {
                OnCompleted(this);
            }
            return asyncResult;
        }

        /// <summary>
        /// 结束异步发送数据
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns></returns>
        public void EndWrite(IAsyncResult asyncResult)
        {
            if (asyncResult is not TcpReadWriteResult result) throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null) throw result.Exception;
        }



        private static ConcurrentStack<TcpSocketAsyncEventArgs> _stacks = new ConcurrentStack<TcpSocketAsyncEventArgs>();

        public static int InstanceCount => _stacks.Count();
        /// <summary>
        /// 从栈中获取一个TcpSocketAsyncEventArgs实例
        /// 对TcpSocketAsyncEventArgs实例的重复使用
        /// </summary>
        /// <returns></returns>
        public static TcpSocketAsyncEventArgs Pop()
        {
            if (_stacks.TryPop(out TcpSocketAsyncEventArgs e)) return e;

            return new TcpSocketAsyncEventArgs();
        }

        /// <summary>
        /// 将TcpSocketAsyncEventArgs实例放入栈中
        /// </summary>
        /// <param name="e"></param>
        public static void Push(TcpSocketAsyncEventArgs e)
        {
            e.SetBuffer(null, 0, 0);
            e.UserToken = null;
            _stacks.Push(e);
        }
    }
}
