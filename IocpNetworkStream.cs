using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace IocpSharp
{
    /// <summary>
    /// 使用TcpSocketAsyncEventArgs实现的IOCP异步读写的NetworkStream
    /// </summary>
    public class IocpNetworkStream : NetworkStream, ISocketBasedStream
    {
        /// <summary>
        /// 获取基础Socket
        /// </summary>
        public Socket BaseSocket => Socket;

        private class ReadWriteArgs
        {
            public TcpSocketAsyncEventArgs TcpSocketAsyncEventArgs;
            public IocpReadWriteResult AsyncResult;
            public ReadWriteArgs(TcpSocketAsyncEventArgs e, IocpReadWriteResult asyncResult)
            {
                TcpSocketAsyncEventArgs = e;
                AsyncResult = asyncResult;
            }

            ~ReadWriteArgs()
            {
                TcpSocketAsyncEventArgs = null;
                AsyncResult = null;
            }
        }
        /// <summary>
        /// 实现NetworkStream的两个构造方法
        /// </summary>
        /// <param name="baseSocket">基础Socket</param>
        public IocpNetworkStream(Socket baseSocket) : base(baseSocket) { }

        /// <summary>
        /// 实现NetworkStream的两个构造方法
        /// </summary>
        /// <param name="baseSocket">基础Socket</param>
        /// <param name="ownSocket">是否拥有Socket，为true的话，在Stream关闭的同时，关闭Socket</param>
        public IocpNetworkStream(Socket baseSocket, bool ownSocket) : base(baseSocket, ownSocket) { }

        /// <summary>
        /// 实现异步IOCP读取数据
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">数据在缓冲区的位置</param>
        /// <param name="size">准备读取的数据大小</param>
        /// <param name="callback">回调方法</param>
        /// <param name="state">用户状态</param>
        /// <returns></returns>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            TcpSocketAsyncEventArgs e = TcpSocketAsyncEventArgs.Pop();
            IocpReadWriteResult asyncResult = new IocpReadWriteResult(callback, state, buffer, offset, size);

            try
            {

                e.BeginRead(Socket, buffer, offset, size, AfterRead, new ReadWriteArgs(e, asyncResult));

                return asyncResult;
            }
            catch(SocketException ex)
            {
                asyncResult.SetFailed(ex);
                TcpSocketAsyncEventArgs.Push(e);
                return asyncResult;
            }
            catch
            {
                asyncResult.Dispose();
                TcpSocketAsyncEventArgs.Push(e);
                throw;
            }
        }

        /// <summary>
        /// 基础流读取到数据后
        /// </summary>
        /// <param name="asyncResult"></param>
        private void AfterRead(IAsyncResult asyncResult)
        {
            ReadWriteArgs args = asyncResult.AsyncState as ReadWriteArgs;
            IocpReadWriteResult result = args.AsyncResult;
            try
            {
                int rec = args.TcpSocketAsyncEventArgs.EndRead(asyncResult);
                result.BytesTransfered = rec;
                result.CallUserCallback();
            }
            catch (Exception ex)
            {
                result.SetFailed(ex);
            }
            finally
            {
                TcpSocketAsyncEventArgs.Push(args.TcpSocketAsyncEventArgs);
            }
        }

        /// <summary>
        /// 结束读取数据
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns>读取数据的大小</returns>
        public override int EndRead(IAsyncResult asyncResult)
        {
            using IocpReadWriteResult result = asyncResult as IocpReadWriteResult;

            if (result == null) throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null) throw result.Exception;

            return result.BytesTransfered;
        }

        /// <summary>
        /// 实现异步IOCP写入数据
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="offset">数据在缓冲区的位置</param>
        /// <param name="size">准备写入的数据大小</param>
        /// <param name="callback">回调方法</param>
        /// <param name="state">用户状态</param>
        /// <returns></returns>
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int size, AsyncCallback callback, object state)
        {
            TcpSocketAsyncEventArgs e = TcpSocketAsyncEventArgs.Pop();

            IocpReadWriteResult asyncResult = new IocpReadWriteResult(callback, state, buffer, offset, size);
            try
            {
                e.BeginWrite(Socket, buffer, offset, size, AfterWrite, new ReadWriteArgs(e, asyncResult));

                return asyncResult;
            }
            catch (SocketException ex)
            {
                asyncResult.SetFailed(ex);
                TcpSocketAsyncEventArgs.Push(e);
                return asyncResult;
            }
            catch
            {
                asyncResult.Dispose();
                TcpSocketAsyncEventArgs.Push(e);
                throw;
            }
        }

        /// <summary>
        /// 基础流写入后
        /// </summary>
        /// <param name="asyncResult"></param>
        private void AfterWrite(IAsyncResult asyncResult)
        {
            ReadWriteArgs args = asyncResult.AsyncState as ReadWriteArgs;
            IocpReadWriteResult result = args.AsyncResult;
            try
            {
                args.TcpSocketAsyncEventArgs.EndWrite(asyncResult);
                result.CallUserCallback();
            }
            catch (Exception ex)
            {
                result.SetFailed(ex);
            }
            finally
            {
                TcpSocketAsyncEventArgs.Push(args.TcpSocketAsyncEventArgs);
            }
        }

        /// <summary>
        /// 结束写入数据
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns>读取数据的大小</returns>
        public override void EndWrite(IAsyncResult asyncResult)
        {
            using IocpReadWriteResult result = asyncResult as IocpReadWriteResult;

            if (result == null) throw new InvalidOperationException("asyncResult");

            if (result.IsCompleted) result.AsyncWaitHandle.WaitOne();

            if (result.Exception != null) throw result.Exception;
        }
    }
}
