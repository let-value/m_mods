//-----------------------------------------------------------------------
// <copyright file="CombinationStream.cs" company="The Outercurve Foundation">
//    Copyright (c) 2011, The Outercurve Foundation. 
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//      http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>
// <author>Prabir Shrestha (prabir.me)</author>
// <website>https://github.com/facebook-csharp-sdk/combination-stream</website>
//-----------------------------------------------------------------------

/*
 * Install-Package CombinationStream
 * 
 */

namespace CombinationStream
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    public class CombinationStream : Stream
    {
        private readonly IList<Stream> _streams;
        private readonly IList<int> _streamsToDispose;
        private int _currentStreamIndex;
        private Stream _currentStream;
        private long _length = -1;
        private long _postion;

        public CombinationStream(IList<Stream> streams)
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            : this(streams, null)
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        {
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public CombinationStream(IList<Stream> streams, IList<int> streamsToDispose)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            if (streams == null)
                throw new ArgumentNullException("streams");

            _streams = streams;
            _streamsToDispose = streamsToDispose;
            if (streams.Count > 0)
                _currentStream = streams[_currentStreamIndex++];
        }

        public IList<Stream> InternalStreams { get { return _streams; } }

        public override void Flush()
        {
            if (_currentStream != null)
                _currentStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException("Stream is not seekable.");
        }

        public override void SetLength(long value)
        {
            _length = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int result = 0;
            int buffPostion = offset;

            while (count > 0)
            {
                int bytesRead = _currentStream.Read(buffer, buffPostion, count);
                result += bytesRead;
                buffPostion += bytesRead;
                _postion += bytesRead;

                if (bytesRead <= count)
                    count -= bytesRead;

                if (count > 0)
                {
                    if (_currentStreamIndex >= _streams.Count)
                        break;

                    _currentStream = _streams[_currentStreamIndex++];
                }
            }

            return result;
        }

#if NETFX_CORE

        public async override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int result = 0;
            int buffPostion = offset;

            while (count > 0)
            {
                int bytesRead = await _currentStream.ReadAsync(buffer, buffPostion, count, cancellationToken);
                result += bytesRead;
                buffPostion += bytesRead;
                _postion += bytesRead;

                if (bytesRead <= count)
                    count -= bytesRead;

                if (count > 0)
                {
                    if (_currentStreamIndex >= _streams.Count)
                        break;

                    _currentStream = _streams[_currentStreamIndex++];
                }
            }

            return result;
        }

        public override System.Threading.Tasks.Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Stream is not writable");
        }

#else
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            CombinationStreamAsyncResult asyncResult = new CombinationStreamAsyncResult(state);
            if (count > 0)
            {
                int buffPostion = offset;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                AsyncCallback rc = null;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                rc = readresult =>
                         {
                             try
                             {
                                 int bytesRead = _currentStream.EndRead(readresult);
                                 asyncResult.BytesRead += bytesRead;
                                 buffPostion += bytesRead;
                                 _postion += bytesRead;

                                 if (bytesRead <= count)
                                     count -= bytesRead;

                                 if (count > 0)
                                 {
                                     if (_currentStreamIndex >= _streams.Count)
                                     {
                                         // done
                                         asyncResult.CompletedSynchronously = false;
                                         asyncResult.SetAsyncWaitHandle();
                                         asyncResult.IsCompleted = true;
                                         callback(asyncResult);
                                     }
                                     else
                                     {
                                         _currentStream = _streams[_currentStreamIndex++];
                                         _currentStream.BeginRead(buffer, buffPostion, count, rc, readresult.AsyncState);
                                     }
                                 }
                                 else
                                 {
                                     // done
                                     asyncResult.CompletedSynchronously = false;
                                     asyncResult.SetAsyncWaitHandle();
                                     asyncResult.IsCompleted = true;
                                     callback(asyncResult);
                                 }
                             }
                             catch (Exception ex)
                             {
                                 // done
                                 asyncResult.Exception = ex;
                                 asyncResult.CompletedSynchronously = false;
                                 asyncResult.SetAsyncWaitHandle();
                                 asyncResult.IsCompleted = true;
                                 callback(asyncResult);
                             }
                         };
                _currentStream.BeginRead(buffer, buffPostion, count, rc, state);
            }
            else
            {
                // done
                asyncResult.CompletedSynchronously = true;
                asyncResult.SetAsyncWaitHandle();
                asyncResult.IsCompleted = true;
                callback(asyncResult);
            }

            return asyncResult;
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            // todo: check if it is of same reference
            asyncResult.AsyncWaitHandle.WaitOne();
            var ar = (CombinationStreamAsyncResult)asyncResult;
            if (ar.Exception != null)
            {
                throw ar.Exception;
            }

            return ar.BytesRead;
        }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            throw new InvalidOperationException("Stream is not writable");
        }

        internal class CombinationStreamAsyncResult : IAsyncResult
        {
            private readonly object _asyncState;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            public CombinationStreamAsyncResult(object asyncState)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
            {
                _asyncState = asyncState;
                _manualResetEvent = new ManualResetEvent(false);
            }

            public bool IsCompleted { get; internal set; }

            public WaitHandle AsyncWaitHandle
            {
                get { return _manualResetEvent; }
            }

            public object AsyncState
            {
                get { return _asyncState; }
            }

            public bool CompletedSynchronously { get; internal set; }

            public Exception Exception { get; internal set; }

            internal void SetAsyncWaitHandle()
            {
                _manualResetEvent.Set();
            }

            private readonly ManualResetEvent _manualResetEvent;
            public int BytesRead;
        }

#endif

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Stream is not writable");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_streamsToDispose == null)
            {
                foreach (var stream in InternalStreams)
                    stream.Dispose();
            }
            else
            {
                int i;
                for (i = 0; i < InternalStreams.Count; i++)
                    InternalStreams[i].Dispose();
            }
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get
            {
                if (_length == -1)
                {
                    _length = 0;
                    foreach (var stream in _streams)
                        _length += stream.Length;
                }

                return _length;
            }
        }

        public override long Position
        {
            get { return _postion; }
            set { throw new NotImplementedException(); }
        }
    }
}