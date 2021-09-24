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

    public delegate void HttpMessageReadDelegate<T>(Exception e, T message) where T : HttpMessage , new();
    internal class HttpMessageReadResult<T> : AsyncResult<T> where T : HttpMessage, new()
    {
        private T _message = null;
        public T Message => _message;
        public HttpMessageReadResult(T message, AsyncCallback userCallback, object userStateObject) : base(userCallback, userStateObject)
        {
            _message = message;
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
        public Stream Commit(HttpMessage message) {
            message.BaseStream = this;
            return message.OpenWrite();
        }

        /// <summary>
        /// 获取一个HTTP消息（请求或响应）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Task<T> Capture<T>() where T : HttpMessage, new()
        {
            return CaptureNext<T>();
        }

        /// <summary>
        /// 获取下一个HTTP消息（请求或响应）
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        internal Task<T> CaptureNext<T>() where T : HttpMessage, new()
        {
            return Task.Factory.FromAsync(BeginCaptureNext<T>, EndCaptureNext<T>, null);
        }

        internal IAsyncResult BeginCaptureNext<T>(AsyncCallback callback, object state) where T : HttpMessage, new()
        {
            T message = new T();
            message.BaseStream = this;
            _capturedMessage++;
            HttpMessageReadResult<T> httpMessageReadResult = new HttpMessageReadResult<T>(message, callback, state);

            try
            {
                ReadLineAsync(httpMessageReadResult);
            }
            catch(Exception e)
            {
                httpMessageReadResult.SetFailed(e);
            }
            return httpMessageReadResult;
        }

        internal T EndCaptureNext<T>(IAsyncResult asyncResult) where T : HttpMessage, new()
        {
            HttpMessageReadResult<T> httpMessageReadResult = asyncResult as HttpMessageReadResult<T>;
            if (!asyncResult.IsCompleted) asyncResult.AsyncWaitHandle.WaitOne();
            if (httpMessageReadResult.Exception != null) throw httpMessageReadResult.Exception;

            return httpMessageReadResult.Message;
        }
        private void AfterReadLine<T>(HttpMessageReadResult<T> asyncResult, string line) where T : HttpMessage, new()
        {
            HttpMessage message = asyncResult.Message;
            if (message.ParseLine(line))
            {
                asyncResult.CallUserCallback();
                return;
            }
            ReadLineAsync(asyncResult);
        }
        private void AfterReadBuffer<T>(IAsyncResult asyncResult) where T: HttpMessage,new() {
            HttpMessageReadResult<T> httpMessageReadResult = asyncResult.AsyncState as HttpMessageReadResult<T>;

            try
            {
                int rec = _innerStream.EndRead(asyncResult);
                if(rec == 0)
                {
                    httpMessageReadResult.SetFailed(new HttpHeaderException(HttpHeaderError.ConnectionLost));
                    return;
                }
                _length += rec;
                ReadLineAsync(httpMessageReadResult);
            }
            catch(Exception e)
            {
                httpMessageReadResult.SetFailed(e);
            }
        }
        private void ReadLineAsync<T>(HttpMessageReadResult<T> asyncResult) where T : HttpMessage, new()
        {
            int offset = _offset;
            byte chr;
            if (_length == 0)
            {
                if (_buffer == null) _buffer = new byte[32768];
                _offset = 0;
                _innerStream.BeginRead(_buffer, 0, _buffer.Length, AfterReadBuffer<T>, asyncResult);
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
                        asyncResult.SetFailed(new HttpHeaderException(HttpHeaderError.NotWellFormed));
                        return;
                    }

                    string line = Encoding.ASCII.GetString(_buffer, _offset, offset - _offset - 2);
                    _length -= offset - _offset;
                    _offset = offset;
                    AfterReadLine(asyncResult, line);
                    return;
                }
            }
            if(_offset > 0)
            {
                Buffer.BlockCopy(_buffer, _offset, _buffer, 0, _length);
                _offset = 0;
            }
            
            if(_length == _buffer.Length)
            {
                asyncResult.SetFailed(new HttpHeaderException(HttpHeaderError.LineLengthExceedsLimit));
                return;
            }
            _innerStream.BeginRead(_buffer, _offset + _length, _buffer.Length - _offset - _length, AfterReadBuffer<T>, asyncResult);
        }

        private byte[] _buffer = null;
        private int _offset = 0;
        private int _length = 0;
        
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
