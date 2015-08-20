/*
* The MIT License (MIT)
* 
* Copyright (c) 2012-2014 Fredrik Holmstrom (fredrik.johan.holmstrom@gmail.com)
* 
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/

using System;
using System.Diagnostics;
using System.Threading;

namespace UdpKit {
    public static class UdpLog {
        public delegate void Writer (uint level, string message);

        public const uint ERROR = 0;
        public const uint INFO = 1;
        public const uint USER = 2;
        public const uint DEBUG = 4;
        public const uint TRACE = 8;
        public const uint WARN = 16;

        static uint enabled = INFO | USER | DEBUG | TRACE | WARN | ERROR;
        static Writer writer = null;
        static readonly object sync = new object();

        static void Write (uint level, string message) {
            lock (sync) {
                Writer callback = writer;

                if (callback != null)
                    callback(level, message);
            }
        }

        static string Time () {
            return DateTime.Now.ToString("H:mm:ss:fff");
        }

        static string ThreadName () {
#if DEBUG
            return " | thread #" + Thread.CurrentThread.ManagedThreadId.ToString().PadLeft(3, '0');
#else
            return "";
#endif
        }

        static public void Info (string format, params object[] args) {
            if (UdpMath.IsSet(enabled, INFO))
                Write(INFO, String.Concat(Time(), ThreadName(), " | info  | ", String.Format(format, args)));
        }

        static public void User (string format, params object[] args) {
            if (UdpMath.IsSet(enabled, INFO))
                Write(USER, String.Concat(Time(), ThreadName(), " | user  | ", String.Format(format, args)));
        }

        [Conditional("TRACE")]
        static public void Trace (string format, params object[] args) {
#if TRACE
            if (UdpMath.IsSet(enabled, TRACE))
                Write(TRACE, String.Concat(Time(), ThreadName(), " | trace | ", String.Format(format, args)));
#endif
        }

        [Conditional("DEBUG")]
        static public void Debug (string format, params object[] args) {
#if DEBUG
            if (UdpMath.IsSet(enabled, DEBUG))
                Write(DEBUG, String.Concat(Time(), ThreadName(), " | debug | ", String.Format(format, args)));
#endif
        }

        static public void Warn (string format, params object[] args) {
            if (UdpMath.IsSet(enabled, WARN)) {
#if DEBUG
                Write(WARN, String.Concat(Time(), ThreadName(), " | warn  | ", String.Format(format, args), "\r\n", Environment.StackTrace));
#else
                Write(WARN, String.Concat(Time(), ThreadName(), " | warn  | ", String.Format(format, args)));
#endif
            }
        }

        static public void Error (string format, params object[] args) {
#if DEBUG
            Write(ERROR, String.Concat(Time(), ThreadName(), " | error | ", String.Format(format, args), "\r\n", Environment.StackTrace));
#else
            Write(ERROR, String.Concat(Time(), ThreadName(), " | error | ", String.Format(format, args)));
#endif
        }

        static public void SetWriter (UdpLog.Writer callback) {
            writer = callback;
        }

        static public void Disable (uint flag) {
            enabled &= ~flag;
        }

        static public void Enable (uint flag) {
            enabled |= flag;
        }

        static public bool IsEnabled (uint flag) {
            return (enabled & flag) == flag;
        }
    }
}