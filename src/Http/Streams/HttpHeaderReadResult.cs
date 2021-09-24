using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using IocpSharp.Server;

namespace IocpSharp.Http.Streams
{
    internal class HttpHeaderReadResult : IAsyncResult
    {
        private AsyncCallback _userCallback;
        private InternalHttpMessageReadDelegate _afterReadMessage;
        private object _userStateObject;
        private string _line = null;
        private Exception _exception = null;
        private bool _isComplete;

        public string Line => _line;
        public Exception Exception => _exception;

        public HttpHeaderReadResult(AsyncCallback afterReadLine, InternalHttpMessageReadDelegate afterReadMessage, object userStateObject)
        {
            _userCallback = afterReadLine;
            _afterReadMessage = afterReadMessage;
            _userStateObject = userStateObject;
        }
        public HttpHeaderReadResult()
        {
        }

        public object AsyncState => _userStateObject;

        public bool IsCompleted => _isComplete;
        public WaitHandle AsyncWaitHandle => throw new NotSupportedException();

        public bool CompletedSynchronously => false;


        protected internal void Complete()
        {
            ThreadPool.UnsafeQueueUserWorkItem(state => {
                _isComplete = true; 
                _afterReadMessage(this);
            }, null);
        }

        private void CallLineReadUserCallback()
        {
            ThreadPool.UnsafeQueueUserWorkItem(state => _userCallback(this), null);
        }

        public void SetFailed(Exception ex)
        {
            _exception = ex;
            CallLineReadUserCallback();
        }

        public void LineRead(string result)
        {
            _line = result;
            CallLineReadUserCallback();
        }


        private static ConcurrentStack<HttpHeaderReadResult> _stacks = new ConcurrentStack<HttpHeaderReadResult>();
        private static int _instanceAmount = 0;
        internal static int InstanceAmount => _instanceAmount;
        internal static HttpHeaderReadResult Pop(AsyncCallback userCallback, InternalHttpMessageReadDelegate afterReadMessage, object userStateObject)
        {
            if (_stacks.TryPop(out HttpHeaderReadResult e))
            {
                e._userCallback = userCallback;
                e._userStateObject = userStateObject;
                e._afterReadMessage = afterReadMessage;
                return e;
            }
            Interlocked.Increment(ref _instanceAmount);
            return new HttpHeaderReadResult(userCallback,  afterReadMessage, userStateObject);
        }

        internal static void Push(HttpHeaderReadResult e)
        {
            e._userCallback = null;
            e._afterReadMessage = null;
            e._userStateObject = null;
            e._isComplete = false;
            e._exception = null;
            e._line = null;
            _stacks.Push(e);
        }
    }
}
