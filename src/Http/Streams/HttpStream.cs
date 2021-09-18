using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IocpSharp.Http.Streams
{
    /// <summary>
    /// 实现一个对HTTP消息读取的流
    /// </summary>
    public class HttpStream : Stream
    {
        private Stream _innerStream = null;
        private bool _leaveInnerStreamOpen = true;
        private bool _streamIsBuffered = false;

        /// <summary>
        /// 使用基础流和模式创建实例
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="leaveInnerStreamOpen"></param>
        public HttpStream(Stream stream, bool leaveInnerStreamOpen)
        {
            _innerStream = stream;
            _leaveInnerStreamOpen = leaveInnerStreamOpen;
            _streamIsBuffered = _innerStream is BufferedNetworkStream;
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
        /// 捕获一个HttpMessage
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Capture<T>() where T : HttpMessage, new()
        {
            T message = new T();
            message.BaseStream = this;
            try
            {
                //循环读取请求头，解析每一行
                byte[] lineBuffer = new byte[32768];
                while (true)
                {
                    string line = ReadLine(lineBuffer);

                    //在HttpRequest实例中，解析每一行的数据
                    if (message.ParseLine(line)) return message.Ready() as T;
                }
            }
            catch
            {
                message.Dispose();
                throw;
            }
        }
        private string ReadLine(byte[] lineBuffer)
        {

            int offset = 0;
            int chr;
            while ((chr = InternalReadByte()) > 0)
            {
                lineBuffer[offset] = (byte)chr;
                if (chr == '\n')
                {
                    //协议要求，每行必须以\r\n结束
                    if (offset < 1 || lineBuffer[offset - 1] != '\r')
                        throw new HttpRequestException(HttpRequestError.NotWellFormed);

                    if (offset == 1)
                        return "";

                    //可以使用具体的编码来获取字符串数据，例如Encoding.UTF8
                    //这里使用ASCII读取
                    return Encoding.ASCII.GetString(lineBuffer, 0, offset - 1);
                }
                offset++;
                //请求头的每行太长，抛出异常
                if (offset >= lineBuffer.Length)
                    throw new HttpRequestException(HttpRequestError.LineLengthExceedsLimit);
            }
            //请求头还没解析完就没数据了
            throw new HttpRequestException(HttpRequestError.ConnectionLost);
        }

        private byte[] _buffer = null;
        private int _offset = 0;
        private int _length = 0;
        
        /// <summary>
        /// 从缓冲区读取数据，如果缓冲区没数据了，从基础刘读数据到缓冲区
        /// </summary>
        /// <returns></returns>
        private int InternalReadByte()
        {
            //我们上层数据流是Buffered，直接使用上层数据流。
            if (_streamIsBuffered) return _innerStream.ReadByte();
            if (_length == 0)
            {
                if(_buffer == null) _buffer = new byte[32768];
                _offset = 0;
                _length = _innerStream.Read(_buffer, 0, _buffer.Length);
                if (_length == 0) return -1;
            }
            _length--;
            return _buffer[_offset++];
        }

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
