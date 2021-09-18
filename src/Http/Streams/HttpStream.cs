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

            while ((chr = _innerStream.ReadByte()) > 0)
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _innerStream.Read(buffer, offset, count);
        }

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
