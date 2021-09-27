using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace IocpSharp.Http.Streams
{

    public delegate void HttpMessageReadCallback<T>(Exception e, T message, object state) where T : HttpMessage , new();

    /// <summary>
    /// 保存上下文信息
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class HttpMessageReadArgs<T> where T : HttpMessage, new()
    {
        private HttpMessageReadCallback<T> _callback = null;
        private object _state = null;
        private T _message = null;

        public T Message => _message;
        public HttpMessageReadArgs(T message, HttpMessageReadCallback<T> callback, object state)
        {
            Reset(message, callback, state);
        }

        public void Reset(T message, HttpMessageReadCallback<T> callback, object state)
        {
            _callback = callback ?? throw new ArgumentNullException("callback");
            _message = message ?? throw new ArgumentNullException("message");
            _state = state;
        }

        public void SetFailed(Exception ex)
        {
            _callback(ex, _message, _state);
            Clearup();
        }

        public void Complete()
        {
            _callback(null, _message, _state);
            Clearup();
        }

        public void Clearup()
        {
            _callback = null;
            _state = null;
            _message = null;
        }
    }
    /// <summary>
    /// 实现一个对HTTP消息读取的流
    /// </summary>
    public class HttpStream : Stream, ISocketBasedStream
    {
        private Stream _innerStream = null;
        private bool _leaveInnerStreamOpen = true;
        private int _capturedMessage = 0;

        internal int CapturedMessage => _capturedMessage;

        public Socket BaseSocket => (_innerStream as ISocketBasedStream).BaseSocket;

        /// <summary>
        /// 使用基础流和模式创建实例
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="leaveInnerStreamOpen"></param>
        public HttpStream(Stream stream, bool leaveInnerStreamOpen)
        {
            _innerStream = stream;
            _leaveInnerStreamOpen = leaveInnerStreamOpen;
        }

        /// <summary>
        /// 提交一个消息发送请求，并返回输入流
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public void Commit(HttpMessage message) {
            message.BaseStream = this;
            byte[] buffer = Encoding.UTF8.GetBytes(message.GetAllHeaders());

            Write(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 提交一个消息发送请求，并返回输入流
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Task CommitAsync(HttpMessage message)
        {
            try
            {
                message.BaseStream = this;
                byte[] buffer = Encoding.UTF8.GetBytes(message.GetAllHeaders());

                return WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <summary>
        /// 获取一个HTTP消息（请求或响应）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public void Capture<T>(HttpMessageReadCallback<T> callback, object state) where T : HttpMessage, new()
        {
            CaptureNext(callback, state);
        }

        /// <summary>
        /// 获取下一个HTTP消息（请求或响应）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        internal void CaptureNext<T>(HttpMessageReadCallback<T> callback, object state) where T : HttpMessage, new()
        {
            T message = new T();
            message.BaseStream = this;
            _capturedMessage++;
            HttpMessageReadArgs<T> args = new HttpMessageReadArgs<T>(message, callback, state);

            try
            {
                ReadLineAsync(args);
            }
            catch (Exception e)
            {
                args.SetFailed(e);
            }
        }

        /// <summary>
        /// 读取到数据行的回调
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="args"></param>
        /// <param name="line"></param>
        private void AfterReadLine<T>(HttpMessageReadArgs<T> args, string line) where T : HttpMessage, new()
        {
            try
            {
                HttpMessage message = args.Message;

                //解析每一行
                if (message.ParseLine(line))
                {
                    args.Complete();
                    return;
                }

                //如果没有解析到尾行，继续读取下一行
                ReadLineAsync(args);
            }
            catch (Exception ex)
            {
                args.SetFailed(ex);
            }
        }

        /// <summary>
        /// 从基础流读取缓冲数据。
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="asyncResult"></param>
        private void AfterReadBuffer<T>(IAsyncResult asyncResult) where T: HttpMessage,new() {
            HttpMessageReadArgs<T> args = asyncResult.AsyncState as HttpMessageReadArgs<T>;

            try
            {
                int rec = _innerStream.EndRead(asyncResult);
                if(rec == 0)
                {
                    args.SetFailed(new HttpHeaderException(HttpHeaderError.ConnectionLost));
                    return;
                }
                _length += rec;
                ReadLineAsync(args);
            }
            catch(Exception e)
            {
                args.SetFailed(e);
            }
        }

        /// <summary>
        /// 读取一行数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="args"></param>
        private void ReadLineAsync<T>(HttpMessageReadArgs<T> args) where T : HttpMessage, new()
        {
            int offset = _offset;
            byte chr;

            //没有数据时，从基础流读取数据
            if (_length == 0)
            {
                if (_buffer == null) _buffer = new byte[32768];
                _offset = 0;
                _innerStream.BeginRead(_buffer, 0, _buffer.Length, AfterReadBuffer<T>, args);
                return;
            }

            while(offset < _offset + _length)
            {
                chr = _buffer[offset++];
                if (chr == '\n')
                {
                    //协议要求，每行必须以\r\n结束
                    if (offset - _offset  < 2 || _buffer[offset - 2] != '\r')
                    {
                        args.SetFailed(new HttpHeaderException(HttpHeaderError.NotWellFormed, "行标识错误"));
                        return;
                    }

                    string line = Encoding.ASCII.GetString(_buffer, _offset, offset - _offset - 2);
                    _length -= offset - _offset;
                    _offset = offset;
                    AfterReadLine(args, line);
                    return;
                }
            }

            //重置缓冲区
            if(_offset > 0)
            {
                Buffer.BlockCopy(_buffer, _offset, _buffer, 0, _length);
                _offset = 0;
            }
            
            //数据超过指定长度
            if(_length == _buffer.Length)
            {
                args.SetFailed(new HttpHeaderException(HttpHeaderError.LineLengthExceedsLimit));
                return;
            }

            //从基础流读取数据，尽可能填满缓冲区
            _innerStream.BeginRead(_buffer, _length, _buffer.Length - _length, AfterReadBuffer<T>, args);
        }

        private byte[] _buffer = null;
        private int _offset = 0;
        private int _length = 0;

        #region 重写Read/Write
        /// <summary>
        /// 从基础流读取一个字节，优先清空缓冲区
        /// </summary>
        /// <returns></returns>
        public override int ReadByte()
        {
            if (_length > 0)
            {
                _length--;
                return _buffer[_offset++];
            }
            return _innerStream.ReadByte();
        }

        /// <summary>
        /// 从基础刘读取数据，优先清空缓冲区
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if(_length > 0)
            {
                return CopyFromBuffer(buffer, offset, count);
            }
            return _innerStream.Read(buffer, offset, count);
        }

        /// <summary>
        /// 写入数据到基础流
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            _innerStream.Write(buffer, offset, count);
        }

       
        /// <summary>
        /// 异步读取数据
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if(_length > 0)
            {
                HttpStreamReadResult asyncResult = new HttpStreamReadResult(callback, state, buffer, offset, count);

                asyncResult.BytesTransfered = CopyFromBuffer(buffer, offset, count);
                asyncResult.CallUserCallback();

                return asyncResult;
            }
            return _innerStream.BeginRead(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// 结束异步读取数据
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns></returns>
        public override int EndRead(IAsyncResult asyncResult)
        {
            if(asyncResult is HttpStreamReadResult httpStreamReadResult)
            {
                return httpStreamReadResult.BytesTransfered;
            }
            return _innerStream.EndRead(asyncResult);
        }

        /// <summary>
        /// 异步写入数据
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return _innerStream.BeginWrite(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// 结束异步写入数据
        /// </summary>
        /// <param name="asyncResult"></param>
        public override void EndWrite(IAsyncResult asyncResult)
        {
            _innerStream.EndWrite(asyncResult);
        }

        /// <summary>
        /// 重写异步读取方法，非必须，但有必要
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if(_length > 0)
            {
                return Task.FromResult(CopyFromBuffer(buffer, offset, count));
            }
            return _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <summary>
        /// 重写异步写入方法，非必须，但有必要
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        private int CopyFromBuffer(byte[] buffer, int offset, int count)
        {
            if (count > _length) count = _length;

            Array.Copy(_buffer, _offset, buffer, offset, count);
            _offset += count;
            _length -= count;
            return count;
        }

        #endregion


        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveInnerStreamOpen)
            {
                _innerStream?.Close();
            }
            _innerStream = null;
            base.Dispose(disposing);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}
