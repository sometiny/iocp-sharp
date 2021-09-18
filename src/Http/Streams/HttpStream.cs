using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            //我们不能确定上层数据流是Buffered，在这里再封装一层缓冲区。
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

        private byte[] _buffer = new byte[32768];
        private int _offset = 0;
        private int _length = 0;
        
        /// <summary>
        /// 从缓冲区读取数据，如果缓冲区没数据了，从基础刘读数据到缓冲区
        /// </summary>
        /// <returns></returns>
        private int InternalReadByte()
        {
            if(_length == 0)
            {
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
                if (count > _length) count = _length;
                Array.Copy(_buffer, _offset, buffer, offset, count);
                _offset += count;
                _length -= count;
                return count;
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
