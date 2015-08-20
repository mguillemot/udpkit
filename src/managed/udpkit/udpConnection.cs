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

namespace UdpKit {
    enum UdpConnectionState : int {
        None = 0,
        Connecting = 1,
        Connected = 2,
        Disconnected = 3,
        Destroy = 4
    }

    enum UdpConnectionError : int {
        None = 0,
        SequenceOutOfBounds = 1,
        IncorrectCommand = 2,
        SendWindowFull = 3,
    }

    enum UdpConnectionMode : int {
        Client = 1,
        Server = 2
    }

    public enum UdpConnectionOption : int {
        AlwaysSendMtu = 1
    }

    public class UdpConnection {
        /// <summary>
        /// A user-assignable object
        /// </summary>
        public object UserToken {
            get;
            set;
        }

        /// <summary>
        /// The round-trip time of the network layer, excluding processing delays and ack time
        /// </summary>
        public float NetworkPing {
            get { return networkRtt; }
        }

        /// <summary>
        /// The total round-trip time, including processing delays and ack
        /// </summary>
        public float AliasedPing {
            get { return aliasedRtt; }
        }

        /// <summary>
        /// If this connection is a client
        /// </summary>
        public bool IsClient {
            get { return mode == UdpConnectionMode.Client; }
        }

        /// <summary>
        /// IF this connections a server
        /// </summary>
        public bool IsServer {
            get { return mode == UdpConnectionMode.Server; }
        }

        /// <summary>
        /// If we are connected
        /// </summary>
        public bool IsConnected {
            get { return state == UdpConnectionState.Connected; }
        }

        /// <summary>
        /// If we are disconnected (not connected)
        /// </summary>
        public bool IsDisconnected {
            get { return state != UdpConnectionState.Connected; }
        }

        /// <summary>
        /// The remote end point
        /// </summary>
        public UdpEndPoint RemoteEndPoint {
            get { return endpoint; }
        }

        /// <summary>
        /// Socket object which owns this connection
        /// </summary>
        public UdpSocket Socket {
            get { return socket; }
        }

        /// <summary>
        /// The maximum transmission unit of this connection, see: http://en.wikipedia.org/wiki/Maximum_transmission_unit
        /// </summary>
        public int Mtu {
            get { return mtu; }
        }

        /// <summary>
        /// How much of the current outgoing packet window is waiting for acks
        /// </summary>
        public float WindowFillRatio {
            get { return sendWindow.FillRatio; }
        }

        /// <summary>
        /// Statistics of this connection
        /// </summary>
        public UdpStats Statistics {
            get { return stats; }
        }

        int mtu;
        float networkRtt = 0.1f;
        float aliasedRtt = 0.1f;
        bool alwaysSendMtu;
        UdpStats stats;
        UdpSerializer serializer;
        UdpConnectionMode mode;
        UdpEndPoint endpoint;
        uint sendTime;
        ushort sendSequence;
        UdpRingBuffer<UdpHandle> sendWindow;
        uint recvTime;
        ushort recvSequence;
        ulong recvHistory;
        uint recvSinceLastSend;
        uint connectTimeout;
        uint connectAttempts;

        internal UdpSocket socket;
        internal UdpConnectionState state;

        internal UdpConnection (UdpSocket s, UdpConnectionMode m, UdpEndPoint ep) {
            socket = s;
            mode = m;
            endpoint = ep;
            stats = new UdpStats();
            networkRtt = socket.Config.DefaultNetworkPing;
            aliasedRtt = socket.Config.DefaultAliasedPing;
            mtu = socket.Config.PacketSize;
            alwaysSendMtu = socket.Config.DefaultAlwaysSendMtu;
            state = UdpConnectionState.Connecting;
            recvTime = socket.GetCurrentTime();
            sendTime = recvTime;
            sendWindow = new UdpRingBuffer<UdpHandle>(socket.Config.PacketWindow);

            serializer = socket.CreateSerializer();
            serializer.Connection = this;
        }

        /// <summary>
        /// Send an object on this connection
        /// </summary>
        /// <param name="obj">The object to send</param>
        public void Send (object obj) {
            socket.Raise(UdpEvent.INTERNAL_SEND, this, obj);
        }

        /// <summary>
        /// Disconnect this connection forcefully
        /// </summary>
        public void Disconnect () {
            socket.Raise(UdpEvent.INTERNAL_DISCONNECT, this);
        }

        /// <summary>
        /// Set option with a bool value on this connection
        /// </summary>
        /// <param name="option">The option to set</param>
        /// <param name="value">The value</param>
        public void SetOption (UdpConnectionOption option, bool value) {
            switch (option) {
                case UdpConnectionOption.AlwaysSendMtu:
                    socket.Raise(UdpEvent.INTERNAL_CONNECTION_OPTION, this, option, value ? 1 : 0);
                    break;
            }
        }

        internal void ProcessConnectingTimeouts (uint now) {
            switch (mode) {
                case UdpConnectionMode.Client:
                    if (connectTimeout < now && !SendConnectRequest()) {
                        socket.Raise(UdpEvent.PUBLIC_CONNECT_FAILED, endpoint);

                        // destroy this connection on next timeout check
                        ChangeState(UdpConnectionState.Destroy);
                    }
                    break;
            }
        }

        internal void ProcessConnectedTimeouts (uint now) {
            if ((recvTime + socket.Config.ConnectionTimeout) < now) {
                UdpLog.Debug("disconnecting due to timeout from {0}", endpoint);
                ChangeState(UdpConnectionState.Disconnected);
            }

            if (CheckState(UdpConnectionState.Connected)) {
                if (sendTime + socket.Config.PingTimeout < now || recvSinceLastSend >= socket.Config.RecvWithoutAckLimit) {
                    SendCommand(UdpCommandType.Ping);
                }
            }
        }

        internal void OnEventConnectionOption (UdpEvent ev) {
            switch (ev.Option) {
                case UdpConnectionOption.AlwaysSendMtu:
                    alwaysSendMtu = ev.OptionIntValue == 1;
                    break;
            }
        }

        internal void ChangeState (UdpConnectionState newState) {
            if (newState == state)
                return;

            UdpConnectionState oldState = state;

            switch (state = newState) {
                case UdpConnectionState.Connected:
                    OnStateConnected(oldState);
                    break;

                case UdpConnectionState.Disconnected:
                    OnStateDisconnected(oldState);
                    break;
            }
        }

        internal void OnPacket (UdpStream buffer) {
            // track stats
            stats.PacketReceived((uint) buffer.Length >> 3);
            socket.Statistics.PacketReceived((uint) buffer.Length >> 3);

            // set recv time of for last packet
            recvTime = socket.GetCurrentTime();

            // Erhune:
            // X bytes of header
            // 1 byte of 0x00 <==== command marker
            // 1 byte of command
            //UnityEngine.Debug.LogWarning(BitConverter.ToString(buffer.Data));
            if (buffer.Data[UdpSocket.HeaderBitSize >> 3] != 0x00) {
                OnObjectReceived(buffer);
            } else {
                OnCommandReceived(buffer);
            }
        }

        void OnCommandReceived (UdpStream buffer) {
            if (ParseHeader(buffer)) {
                buffer.Ptr += 8; // Erhune: skip command marker
                UdpCommandType cmd = (UdpCommandType) buffer.ReadByte(8);

                switch (cmd) {
                    case UdpCommandType.Connect: OnCommandConnect(buffer); break;
                    case UdpCommandType.Accepted: OnCommandAccepted(buffer); break;
                    case UdpCommandType.Refused: OnCommandRefused(buffer); break;
                    case UdpCommandType.Disconnected: OnCommandDisconnected(buffer); break;
                    case UdpCommandType.Ping: OnCommandPing(buffer); break;
                    default: ConnectionError(UdpConnectionError.IncorrectCommand); break;
                }
            }
        }

        internal bool CheckState (UdpConnectionState stateValue) {
            return state == stateValue;
        }

        internal void SendObject (object o) {
            serializer.SendNext(o);

            while (serializer.HasQueuedObjects) {
                UdpSendFailReason reason = CheckCanSend(false);

                if (reason != UdpSendFailReason.None) {
                    while (serializer.HasQueuedObjects) {
                        socket.Raise(UdpEvent.PUBLIC_OBJECT_SEND_FAILED, this, serializer.NextObject(), reason);
                    }

                    break;
                }

                UdpStream stream = socket.GetWriteStream(mtu << 3, UdpSocket.HeaderBitSize);
                var initialPtr = stream.Ptr; // Erhune: added info
                object obj = serializer.NextObject();

                if (serializer.Pack(stream, ref obj)) {
                    if (stream.Overflowing && (socket.Config.AllowPacketOverflow == false)) {
                        UdpLog.Error("Stream to {0} is overflowing (InitialPtr={1} Ptr={2} Len={3}), not sending {4}",
                            endpoint.ToString(), initialPtr, stream.Ptr, stream.Length, obj); // Erhune: added info
                        socket.Raise(UdpEvent.PUBLIC_OBJECT_SEND_FAILED, this, obj, UdpSendFailReason.StreamOverflow);
                        return;
                    }

                    UdpHeader header = MakeHeader(true);
                    header.Pack(stream, socket);

                    UdpHandle handle = MakeHandle(ref header);
                    handle.Object = obj;

                    if (SendStream(stream, handle, alwaysSendMtu)) {
                        // track stats
                        stats.PacketSent((uint) stream.Ptr >> 3);
                        socket.Statistics.PacketSent((uint) stream.Ptr >> 3);

                        // push object to user thread
                        socket.Raise(UdpEvent.PUBLIC_OBJECT_SENT, this, obj);

                    } else {
                        socket.Raise(UdpEvent.PUBLIC_OBJECT_SEND_FAILED, this, obj, UdpSendFailReason.SocketError);
                    }
                } else {
                    socket.Raise(UdpEvent.PUBLIC_OBJECT_SEND_FAILED, this, obj, UdpSendFailReason.SerializerReturnedFalse);
                }
            }
        }

        public void SendCommand (UdpCommandType cmd) {
            if (CheckCanSend(true) == UdpSendFailReason.None) {
                UdpStream stream = socket.GetWriteStream(mtu << 3, UdpSocket.HeaderBitSize);
                stream.WriteByte(0x00); // Erhune: command marker
                stream.WriteByte((byte) cmd);

                UdpHeader header = MakeHeader(false);
                header.Pack(stream, socket);

                UdpHandle handle = MakeHandle(ref header);
                handle.Object = null;

                if (SendStream(stream, handle, false)) {
                    // track stats
                    stats.PacketSent((uint) stream.Ptr >> 3);
                    socket.Statistics.PacketSent((uint) stream.Ptr >> 3);

                } else {
                    // should we do something here?????
                }
            }
        }

        internal void Destroy () {
        }

        bool SendStream (UdpStream stream, UdpHandle handle, bool expandToMtu) {
            int bytesToSend = UdpMath.BytesRequired(stream.Ptr);
            if (bytesToSend < mtu && expandToMtu) {
                bytesToSend = mtu;
            }

            sendTime = handle.SendTime;
            sendSequence = handle.ObjSequence;
            sendWindow.Enqueue(handle);
            recvSinceLastSend = 0;

            return socket.Send(endpoint, stream.Data, bytesToSend);
        }

        UdpHandle MakeHandle (ref UdpHeader header) {
            UdpHandle handle = new UdpHandle();
            //handle.IsObject = header.IsObject;
            handle.ObjSequence = header.ObjSequence;
            handle.SendTime = header.Now;
            return handle;
        }

        UdpHeader MakeHeader (bool isObject) {
            UdpHeader header = new UdpHeader();
            //header.IsObject = isObject;
            header.AckHistory = recvHistory;
            header.AckSequence = recvSequence;
            header.ObjSequence = UdpMath.SeqNext(sendSequence, UdpHeader.SEQ_MASK);
            header.Now = socket.GetCurrentTime();

            if (recvTime > 0) {
                header.AckTime = (ushort) UdpMath.Clamp(header.Now - recvTime, 0, socket.Config.MaxPing);
            }

            return header;
        }

        UdpSendFailReason CheckCanSend (bool sendingCommand) {
            if (CheckState(UdpConnectionState.Connected) == false || sendWindow.Full) {
                if (sendingCommand && IsClient && CheckState(UdpConnectionState.Connecting)) {
                    return UdpSendFailReason.None;
                }

                if (sendWindow.Full) {
                    ConnectionError(UdpConnectionError.SendWindowFull);
                    return UdpSendFailReason.PacketWindowFull;
                }

                return UdpSendFailReason.NotConnected;
            }

            return UdpSendFailReason.None;
        }

        bool ParseHeader (UdpStream stream) {
            // we should always start at ptr 0
            UdpAssert.Assert(stream.Ptr == 0);

            UdpHeader header = new UdpHeader();
            header.Unpack(stream);

            // after unpacking the header, the pointer should be at the header size
            UdpAssert.Assert(stream.Ptr == UdpSocket.HeaderBitSize);

            int seqDistance = UdpMath.SeqDistance(header.ObjSequence, recvSequence, UdpHeader.SEQ_PADD);
            UdpLog.Trace("ParseHeader() header.objSequence={0} recvSequence={1} seqDistance={2} header.ackSequence={3} header.ackHistory={4}", header.ObjSequence, recvSequence, seqDistance, header.AckSequence, Convert.ToString((uint) header.AckHistory, 2));

            // we have to be within window size
            if (seqDistance > socket.Config.PacketWindow || seqDistance < -socket.Config.PacketWindow) {
                ConnectionError(UdpConnectionError.SequenceOutOfBounds, string.Format("seqDistance: {0}, socket.Config.PacketWindow: {1}, header.ObjSequence: {2}, recvSequence: {3}", seqDistance, socket.Config.PacketWindow, header.ObjSequence, recvSequence));
                return false;
            }

            // this is an old packet
            if (seqDistance <= 0)
                return false;

            // update receive history
            if (seqDistance >= UdpSocket.AckRedundancy) {
                recvHistory = 1UL;
            } else {
                recvHistory = (recvHistory << seqDistance) | 1UL;
            }

            // update our receive stats
            recvSequence = header.ObjSequence;
            recvSinceLastSend += 1;

            // ack sent objects
            AckHandles(header, true);

            return true;
        }

        void OnObjectReceived (UdpStream buffer) {
            EnsureClientIsConnected();

            if (CheckState(UdpConnectionState.Connected) == false)
                return;

            if (ParseHeader(buffer)) {
                object obj = null;
                buffer.Ptr = UdpSocket.HeaderBitSize;

                if (serializer.Unpack(buffer, ref obj)) {
                    socket.Raise(UdpEvent.PUBLIC_OBJECT_RECEIVED, this, obj);
                }
            }
        }

        void OnStateConnected (UdpConnectionState oldState) {
            if (oldState == UdpConnectionState.Connecting) {
                UdpLog.Info("connected to {0} ({1})", endpoint.ToString(), mode);

                if (IsServer) {
                    SendCommand(UdpCommandType.Accepted);
                }

                socket.Raise(UdpEvent.PUBLIC_CONNECTED, this);
            }
        }

        void OnStateDisconnected (UdpConnectionState oldState) {
            if (oldState == UdpConnectionState.Connected) {
                while (sendWindow.Empty == false) {
                    UdpHandle handle = sendWindow.Dequeue();

                    if (handle.IsObject) {
                        socket.Raise(UdpEvent.PUBLIC_OBJECT_LOST, this, handle.Object);
                    }
                }

                socket.Raise(UdpEvent.PUBLIC_DISCONNECTED, this);
            }
        }

        void OnCommandConnect (UdpStream buffer) {
            if (IsServer) {
                if (CheckState(UdpConnectionState.Connected)) {
                    SendCommand(UdpCommandType.Accepted);
                }
            } else {
                ConnectionError(UdpConnectionError.IncorrectCommand);
            }
        }

        void OnCommandAccepted (UdpStream buffer) {
            if (IsClient) {
                if (CheckState(UdpConnectionState.Connecting)) {
                    ChangeState(UdpConnectionState.Connected);
                }
            } else {
                ConnectionError(UdpConnectionError.IncorrectCommand);
            }
        }

        void OnCommandRefused (UdpStream buffer) {
            if (IsClient) {
                if (CheckState(UdpConnectionState.Connecting)) {
                    socket.Raise(UdpEvent.PUBLIC_CONNECT_REFUSED, endpoint);

                    // destroy this connection on next timeout check
                    ChangeState(UdpConnectionState.Destroy);
                }
            } else {
                ConnectionError(UdpConnectionError.IncorrectCommand);
            }
        }

        void OnCommandDisconnected (UdpStream buffer) {
            EnsureClientIsConnected();

            if (CheckState(UdpConnectionState.Connected)) {
                ChangeState(UdpConnectionState.Disconnected);
            }
        }

        void OnCommandPing (UdpStream buffer) {
            EnsureClientIsConnected();
        }

        void EnsureClientIsConnected () {
            if (IsClient && state == UdpConnectionState.Connecting && socket.Config.AllowImplicitAccept) {
                ChangeState(UdpConnectionState.Connected);
            }
        }

        void ConnectionError (UdpConnectionError error) {
          ConnectionError(error, "");
        }

        void ConnectionError (UdpConnectionError error, string message) {
            UdpLog.Error("error '{0} - {2}' on connection to {1}", error.ToString(), endpoint.ToString(), message); // Erhune: moved from Debug to Error

            switch (error) {
                case UdpConnectionError.SequenceOutOfBounds:
                    ChangeState(UdpConnectionState.Disconnected);
                    break;

                case UdpConnectionError.IncorrectCommand:
                    ChangeState(UdpConnectionState.Disconnected);
                    break;

                case UdpConnectionError.SendWindowFull:
                    ChangeState(UdpConnectionState.Disconnected);
                    break;
            }
        }

        bool SendConnectRequest () {
            if (connectAttempts < socket.Config.ConnectRequestAttempts) {
                if (connectAttempts != 0) {
                    UdpLog.Info("retrying connection to {0}", endpoint.ToString());
                }

                SendCommand(UdpCommandType.Connect);

                connectTimeout = socket.GetCurrentTime() + socket.Config.ConnectRequestTimeout;
                connectAttempts += 1u;
                return true;
            }

            return false;
        }

        void AckHandles (UdpHeader header, bool updateRtt) {
            //UdpLog.Debug("AckHandles()");
            while (!sendWindow.Empty) {
                UdpHandle handle = sendWindow.Peek();

                int seqDistance = UdpMath.SeqDistance(handle.ObjSequence, header.AckSequence, UdpHeader.SEQ_PADD);
                //UdpLog.Debug("Handle of {0} (object?{1}) seqDist={1}", handle.Object, handle.IsObject, seqDistance);
                if (seqDistance > 0)
                {
                    break;
                }

                if (handle.IsObject) {
                    if (seqDistance <= -UdpSocket.AckRedundancy) {
                        // track stats
                        stats.PacketLost();
                        socket.Statistics.PacketLost();

                        // handle lost
                        ObjectLost(handle.Object);
                    } else { 
                        if ((header.AckHistory & (1UL << -seqDistance)) != 0UL) {
                            // track stats
                            stats.PacketDelivered();
                            socket.Statistics.PacketDelivered();

                            // handle delivered objects
                            ObjectDelivered(handle.Object);

                        } else {
                            // track stats
                            stats.PacketLost();
                            socket.Statistics.PacketLost();

                            // handle
                            ObjectLost(handle.Object);
                        }
                    }
                }

                if (seqDistance == 0 && header.AckTime > 0) {
                    UpdatePing(recvTime, handle.SendTime, header.AckTime);
                }

                sendWindow.Dequeue();
            }
        }

        void UpdatePing (uint recvTime, uint sendTime, uint ackTime) {
            uint aliased = recvTime - sendTime;
            aliasedRtt = (aliasedRtt * 0.9f) + ((float) aliased / 1000f * 0.1f);

            if (UdpSocket.CalculateNetworkPing) {
                uint network = aliased - UdpMath.Clamp(ackTime, 0, aliased);
                networkRtt = (networkRtt * 0.9f) + ((float) network / 1000f * 0.1f);
            }
        }

        void ObjectLost (object o) {
            socket.Raise(UdpEvent.PUBLIC_OBJECT_LOST, this, o);
        }

        void ObjectDelivered (object o) {
            socket.Raise(UdpEvent.PUBLIC_OBJECT_DELIVERED, this, o);
        }
    }
}
