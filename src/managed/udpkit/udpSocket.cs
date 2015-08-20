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
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace UdpKit {
  public enum UdpSocketState : int {
    Created = 0,
    Running = 1,
    Shutdown = 2
  }

  public partial class UdpSocket {

    /// <summary>
    /// The amount of redundant acks we should do, valid values are:
    /// 8, 16, 24, 32, 40, 48, 56, 64
    /// </summary>
    public static int AckRedundancy {
      // do not change this number unless you know EXACTLY what you are doings
      get { return 64; }
    }

    /// <summary>
    /// If we should calculate network ping or not
    /// </summary>
    public static bool CalculateNetworkPing {
      // do not change this boolean unless you know EXACTLY what you are doings
      get { return true; }
    }

    /// <summary>
    /// The size of the udpkit internal header sent with each packet
    /// </summary>
    public static int HeaderBitSize {
      // do not change this code unless you know EXACTLY what you are doings
      get { return ((UdpHeader.SEQ_BITS + UdpHeader.SEQ_PADD) * 2) + AckRedundancy + (CalculateNetworkPing ? 16 : 0); }
    }

    readonly internal UdpConfig Config;

    volatile int frame;
    volatile UdpSocketState state;

    readonly Random random;
    readonly UdpStats stats;
    readonly UdpPlatform platform;
    readonly UdpStream readStream;
    readonly UdpStream writeStream;
    readonly UdpConfig configCopy;
    readonly UdpStreamPool streamPool;
    readonly AutoResetEvent availableEvent;
    readonly Queue<UdpEvent> eventQueueIn;
    readonly Queue<UdpEvent> eventQueueOut;
    readonly UdpSerializerFactory serializerFactory;
    readonly List<UdpConnection> connList = new List<UdpConnection>();
    readonly UdpSet<UdpEndPoint> pendingConnections = new UdpSet<UdpEndPoint>(new UdpEndPoint.Comparer());
    readonly Dictionary<UdpEndPoint, UdpConnection> connLookup = new Dictionary<UdpEndPoint, UdpConnection>(new UdpEndPoint.Comparer());

    /// <summary>
    /// Local endpoint of this socket
    /// </summary>
    [Obsolete("Use LocalEndPoint instead")]
    public UdpEndPoint EndPoint {
      get { return platform.EndPoint; }
    }
    
    /// <summary>
    /// Local endpoint of this socket
    /// </summary>
    public UdpEndPoint LocalEndPoint {
      get { return platform.EndPoint; }
    }

    /// <summary>
    /// The current state of the socket
    /// </summary>
    public UdpSocketState State {
      get { return state; }
    }

    /// <summary>
    /// Statistics for the entire socket
    /// </summary>
    public UdpStats Statistics {
      get { return stats; }
    }

    /// <summary>
    /// Returns a copy of the active configuration.
    /// Changing values on this copy does nothing.
    /// </summary>
    public UdpConfig ConfigCopy {
      get { return this.configCopy; }
    }

    /// <summary>
    /// The precision time (in ms) of the underlying socket platform
    /// </summary>
    public uint PrecisionTime {
      get { return GetCurrentTime(); }
    }

    public UdpStreamPool StreamPool {
      get { return streamPool; }
    }

    /// <summary>
    /// A thread can wait on this event before calling Poll to make sure at least one event is available
    /// </summary>
    public AutoResetEvent EventsAvailable {
      get { return availableEvent; }
    }

    /// <summary>
    /// A user-assignable object
    /// </summary>
    public object UserToken {
      get;
      set;
    }

    UdpSocket (UdpPlatform platform, UdpSerializerFactory serializerFactory, UdpConfig config) {
      this.platform = platform;
      this.serializerFactory = serializerFactory;
      this.Config = config.Duplicate();
      this.configCopy = config;

      state = UdpSocketState.Created;
      random = new Random();
      stats = new UdpStats();
      availableEvent = new AutoResetEvent(false);

      if (this.Config.NoiseFunction == null) {
        this.Config.NoiseFunction = delegate() { return (float) random.NextDouble(); };
      }

      readStream = new UdpStream(new byte[config.PacketSize * 2]);
      writeStream = new UdpStream(new byte[config.PacketSize * 2]);
      streamPool = new UdpStreamPool(this);

      eventQueueIn = new Queue<UdpEvent>(config.InitialEventQueueSize);
      eventQueueOut = new Queue<UdpEvent>(config.InitialEventQueueSize);

      var threadSocket = new Thread(NetworkLoop);
      threadSocket.Name = "udpkit thread";
      threadSocket.IsBackground = true;
      threadSocket.Start();
    }

    /// <summary>
    /// Start this socket
    /// </summary>
    /// <param name="endpoint">The endpoint to bind to</param>
    public void Start (UdpEndPoint endpoint) {
      Raise(UdpEvent.INTERNAL_START, endpoint);
    }

    /// <summary>
    /// Close this socket
    /// </summary>
    public void Close () {
      Raise(UdpEvent.INTERNAL_CLOSE);
    }

    /// <summary>
    /// Connect to remote endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to connect to</param>
    public void Connect (UdpEndPoint endpoint) {
      Raise(UdpEvent.INTERNAL_CONNECT, endpoint);
    }

    /// <summary>
    /// Cancel ongoing attempt to connect to endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to cancel connect attempt to</param>
    public void CancelConnect (UdpEndPoint endpoint) {
      Raise(UdpEvent.INTERNAL_CONNECT_CANCEL, endpoint);
    }

    /// <summary>
    /// Accept a connection request from a remote endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to accept</param>
    public void Accept (UdpEndPoint endpoint) {
      Raise(UdpEvent.INTERNAL_ACCEPT, endpoint);
    }

    /// <summary>
    /// Refuse a connection request from a remote endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to refuse</param>
    public void Refuse (UdpEndPoint endpoint) {
      Raise(UdpEvent.INTERNAL_REFUSE, endpoint);
    }

    /// <summary>
    /// Suspends the networking thread for N milliseconds. 
    /// Usefull for simulating unusual networking conditions.
    /// </summary>
    /// <param name="milliseconds">How long to sleep</param>
    public void Sleep (int milliseconds) {
#if DEBUG
      Raise(UdpEvent.INTERNAL_SLEEP, milliseconds);
#else
            UdpLog.Warn("Calling UdpSocket.Sleep in non-debug build is not supported");
#endif
    }

    /// <summary>
    /// Poll socket for any events
    /// </summary>
    /// <param name="ev">The current event on this socket</param>
    /// <returns>True if a new event is available, False otherwise</returns>
    public bool Poll (out UdpEvent ev) {
      lock (eventQueueOut) {
        if (eventQueueOut.Count > 0) {
          ev = eventQueueOut.Dequeue();
          return true;
        }
      }

      ev = default(UdpEvent);
      return false;
    }

    /// <summary>
    /// Peek the next event from the socket
    /// </summary>
    /// <param name="ev">The next event on this socket</param>
    /// <returns>True if an event is available, False otherwise</returns>
    public bool Peek (out UdpEvent ev) {
      lock (eventQueueOut) {
        if (eventQueueOut.Count > 0) {
          ev = eventQueueOut.Peek();
          return true;
        }
      }

      ev = default(UdpEvent);
      return false;
    }

    internal void Raise (int eventType) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      Raise(ev);
    }

    internal void Raise (int eventType, int intval) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.OptionIntValue = intval;
      Raise(ev);
    }

    internal void Raise (int eventType, UdpEndPoint endpoint) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.EndPoint = endpoint;
      Raise(ev);
    }

    internal void Raise (int eventType, UdpConnection connection) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.Connection = connection;
      Raise(ev);
    }

    internal void Raise (int eventType, UdpConnection connection, object obj) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.Connection = connection;
      ev.Object = obj;
      Raise(ev);
    }

    internal void Raise (int eventType, UdpConnection connection, object obj, UdpSendFailReason reason) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.Connection = connection;
      ev.FailedReason = reason;
      ev.Object = obj;
      Raise(ev);
    }

    internal void Raise (int eventType, UdpConnection connection, UdpConnectionOption option, int value) {
      UdpEvent ev = new UdpEvent();
      ev.Type = eventType;
      ev.Connection = connection;
      ev.Option = option;
      ev.OptionIntValue = value;
      Raise(ev);
    }

    internal bool Send (UdpEndPoint endpoint, byte[] buffer, int length) {
      if (state == UdpSocketState.Running || state == UdpSocketState.Created) {
        int bytesSent = 0;
        UdpLog.Debug("sending packet to {0}", endpoint);

#if DEBUG
        // Erhune: we might want to drop or delay packets here based on the header, for specific automated tests
        if (Config.DropOutgoingPackets != null)
        {
            var stream = new UdpStream(buffer, length);
            if (Config.DropOutgoingPackets(stream))
            {
                UdpLog.Debug("Droping outgoing packed because of custom function");
                return true;
            }
        }
#endif

        return platform.SendTo(buffer, length, endpoint, ref bytesSent);
      }

      return false;
    }

    internal float RandomFloat () {
      return (float) random.NextDouble();
    }

    internal UdpSerializer CreateSerializer () {
      return serializerFactory();
    }

    internal UdpStream GetReadStream () {
      // clear data buffer every time
      Array.Clear(readStream.Data, 0, readStream.Data.Length);

      readStream.Ptr = 0;
      readStream.Length = 0;

      return readStream;
    }

    internal UdpStream GetWriteStream (int length, int offset) {
      // clear data buffer every time
      Array.Clear(writeStream.Data, 0, writeStream.Data.Length);

      writeStream.Ptr = offset;
      writeStream.Length = length;

      return writeStream;
    }

    internal uint GetCurrentTime () {
      return platform.PlatformPrecisionTime;
    }

    void Raise (UdpEvent ev) {
      if (ev.IsInternal) {
        lock (eventQueueIn) {
          eventQueueIn.Enqueue(ev);
        }
      } else {
        lock (eventQueueOut) {
          eventQueueOut.Enqueue(ev);
        }

        if (Config.UseAvailableEventEvent) {
          availableEvent.Set();
        }
      }
    }

    void SendRefusedCommand (UdpEndPoint endpoint) {
      UdpStream stream = GetWriteStream(Config.PacketSize << 3, HeaderBitSize);
      stream.WriteByte((byte) UdpCommandType.Refused, 8);

      UdpHeader header = new UdpHeader();
      //header.IsObject = false;
      header.AckHistory = 0;
      header.AckSequence = 1;
      header.ObjSequence = 1;
      header.Now = 0;
      header.Pack(stream, this);

      if (Send(endpoint, stream.Data, UdpMath.BytesRequired(stream.Ptr)) == false) {
        // do something here?
      }
    }

    bool ChangeState (UdpSocketState from, UdpSocketState to) {
      if (CheckState(from)) {
        state = to;
        return true;
      }

      return false;
    }

    bool CheckState (UdpSocketState s) {
      if (state != s) {
        return false;
      }

      return true;
    }

    UdpConnection CreateConnection (UdpEndPoint endpoint, UdpConnectionMode mode) {
      if (connLookup.ContainsKey(endpoint)) {
        UdpLog.Warn("connection for {0} already exists", endpoint);
        return default(UdpConnection);
      }

      UdpConnection cn = new UdpConnection(this, mode, endpoint);
      connLookup.Add(endpoint, cn);
      connList.Add(cn);

      return cn;
    }

    bool DestroyConnection (UdpConnection cn) {
      for (int i = 0; i < connList.Count; ++i) {
        if (connList[i] == cn) {
          connList.RemoveAt(i);
          connLookup.Remove(cn.RemoteEndPoint);

          cn.Destroy();

          return true;
        }
      }

      return false;
    }

    void NetworkLoop () {
      bool created = false;
      bool started = false;

      while (state == UdpSocketState.Created || state == UdpSocketState.Running) {
        try {
          if (created == false) {
            UdpLog.Info("socket created");
            created = true;
          }

          while (state == UdpSocketState.Created) {
            ProcessIncommingEvents(true);
            Thread.Sleep(1);
          }

          if (started == false) {
            UdpLog.Info("socket started");
            started = true;
          }

          while (state == UdpSocketState.Running) {
            RecvDelayedPackets();
            RecvNetworkData();
            ProcessTimeouts();
            ProcessIncommingEvents(false);
            frame += 1;
          }

          UdpLog.Info("socket closed");
          return;

        } catch (Exception exn) {
          UdpLog.Error(exn.ToString());
        }
      }
    }

    void ProcessIncommingEvents (bool returnOnStart) {
      while (true) {
        UdpEvent ev = default(UdpEvent);

        lock (eventQueueIn) {
          if (eventQueueIn.Count > 0) {
            ev = eventQueueIn.Dequeue();
          }
        }

        if (ev.Type == 0) {
          return;
        }

        switch (ev.Type) {
          case UdpEvent.INTERNAL_START:
            OnEventStart(ev);

            if (returnOnStart) {
              return;
            } else {
              break;
            }

          case UdpEvent.INTERNAL_CONNECT: OnEventConnect(ev); break;
          case UdpEvent.INTERNAL_CONNECT_CANCEL: OnEventConnectCancel(ev); break;
          case UdpEvent.INTERNAL_ACCEPT: OnEventAccept(ev); break;
          case UdpEvent.INTERNAL_REFUSE: OnEventRefuse(ev); break;
          case UdpEvent.INTERNAL_DISCONNECT: OnEventDisconect(ev); break;
          case UdpEvent.INTERNAL_CLOSE: OnEventClose(ev); break;
          case UdpEvent.INTERNAL_SEND: OnEventSend(ev); break;
          case UdpEvent.INTERNAL_CONNECTION_OPTION: OnEventConnectionOption(ev); break;
          case UdpEvent.INTERNAL_SLEEP: OnEventSleep(ev); break;
        }
      }
    }

    void OnEventStart (UdpEvent ev) {
      UdpLog.Info("binding socket using platform '{0}'", platform.GetType());

      if (ChangeState(UdpSocketState.Created, UdpSocketState.Running)) {
        if (platform.Bind(ev.EndPoint)) {
          // send started event
          Raise(UdpEvent.PUBLIC_STARTED, platform.EndPoint);

          // log that we started
          UdpLog.Info("socket bound to {0}", platform.EndPoint.ToString());
           
        } else {
          // send started failed event
          Raise(UdpEvent.PUBLIC_START_FAILED);

          // log error
          UdpLog.Error("could not bind socket, platform code: {0}, platform error: {1}", platform.PlatformError.ToString(), platform.PlatformErrorString);
        }
      }
    }

    void OnEventConnect (UdpEvent ev) {
      if (CheckState(UdpSocketState.Running)) {
        UdpConnection cn = CreateConnection(ev.EndPoint, UdpConnectionMode.Client);

        if (cn == null) {
          UdpLog.Error("could not create connection for endpoint {0}", ev.EndPoint.ToString());
        } else {
          UdpLog.Info("connecting to {0}", ev.EndPoint.ToString());
        }
      }
    }

    void OnEventConnectCancel (UdpEvent ev) {
      if (CheckState(UdpSocketState.Running)) {
        UdpConnection cn;

        if (connLookup.TryGetValue(ev.EndPoint, out cn)) {
          // if we are connecting, destroy connection
          if (cn.CheckState(UdpConnectionState.Connecting)) {
            // notify user thread
            Raise(UdpEvent.PUBLIC_CONNECT_FAILED, ev.EndPoint);

            // destroy this connection
            cn.ChangeState(UdpConnectionState.Destroy);
          }

          // if we are connected, disconnect 
          else if (ev.Connection.CheckState(UdpConnectionState.Connected)) {
            ev.Connection.SendCommand(UdpCommandType.Disconnected);
            ev.Connection.ChangeState(UdpConnectionState.Disconnected);
          }
        }
      }
    }

    void OnEventAccept (UdpEvent ev) {
      if (pendingConnections.Remove(ev.EndPoint)) {
        AcceptConnection(ev.EndPoint);
      }
    }

    void OnEventRefuse (UdpEvent ev) {
      if (pendingConnections.Remove(ev.EndPoint)) {
        SendRefusedCommand(ev.EndPoint);
      }
    }

    void OnEventDisconect (UdpEvent ev) {
      if (ev.Connection.CheckState(UdpConnectionState.Connected)) {
        ev.Connection.SendCommand(UdpCommandType.Disconnected);
        ev.Connection.ChangeState(UdpConnectionState.Disconnected);
      }
    }

    void OnEventClose (UdpEvent ev) {
      if (ChangeState(UdpSocketState.Running, UdpSocketState.Shutdown)) {
        for (int i = 0; i < connList.Count; ++i) {
          UdpConnection cn = connList[i];
          cn.SendCommand(UdpCommandType.Disconnected);
          cn.ChangeState(UdpConnectionState.Disconnected);
        }

        if (platform.Close() == false) {
          UdpLog.Error("failed to shutdown socket interface, platform code: {0}", platform.PlatformError.ToString());
        }

        connList.Clear();
        connLookup.Clear();
        eventQueueIn.Clear();
        pendingConnections.Clear();

        GetReadStream().Data = null;
        GetWriteStream(0, 0).Data = null;
      }
    }

    void OnEventSend (UdpEvent ev) {
      ev.Connection.SendObject(ev.Object);
    }

    void OnEventSleep (UdpEvent ev) {
      UdpLog.Debug("sleeping network thread for {0} ms", ev.OptionIntValue);
      Thread.Sleep(ev.OptionIntValue);
    }

    void OnEventConnectionOption (UdpEvent ev) {
      ev.Connection.OnEventConnectionOption(ev);
    }

    void AcceptConnection (UdpEndPoint ep) {
      UdpConnection cn = CreateConnection(ep, UdpConnectionMode.Server);
      cn.ChangeState(UdpConnectionState.Connected);
    }

    void ProcessTimeouts () {
      if ((frame & 3) == 3) {
        uint now = GetCurrentTime();

        // Erhune: small optim here because of profiling (do not recompute upper bound every time)
        for (int i = 0, n = connList.Count; i < n; ++i) {
          UdpConnection cn = connList[i];

          switch (cn.state) {
            case UdpConnectionState.Connecting:
              cn.ProcessConnectingTimeouts(now);
              break;

            case UdpConnectionState.Connected:
              cn.ProcessConnectedTimeouts(now);
              break;

            case UdpConnectionState.Disconnected:
              cn.ChangeState(UdpConnectionState.Destroy);
              break;

            case UdpConnectionState.Destroy:
              if (DestroyConnection(cn)) {
                --i;
                --n;
              }
              break;
          }
        }
      }
    }

    void RecvNetworkData () {
      if (platform.RecvPoll(1)) {
        int bytes = 0;
        UdpEndPoint ep = UdpEndPoint.Any;
        UdpStream stream = GetReadStream();

        if (platform.RecvFrom(stream.Data, stream.Data.Length, ref bytes, ref ep)) {
          UdpLog.Debug("received packet from {0}", ep.ToString());

#if DEBUG
          // Erhune: we might want to drop or delay packets here based on the header, for specific automated tests
          if (Config.DropIncomingPackets != null)
          {
              if (Config.DropIncomingPackets(stream))
              {
                  UdpLog.Debug("Droping incoming packed because of custom function");
                  return;
              }
          }
          if (Config.DelayIncomingPackets != null)
          {
              var delay = Config.DelayIncomingPackets(stream);
              if (delay > 0)
              {
                  UdpLog.Debug("Delaying incoming packed by {0} ms because of custom function", delay);
                  DelayPacketBy(ep, stream.Data, bytes, delay);
                  return;
              }
          }
            
          if (ShouldDropPacket) {
            return;
          }

          if (ShouldDelayPacket) {
            DelayPacket(ep, stream.Data, bytes);
            return;
          }
#endif

          RecvNetworkPacket(ep, stream, bytes);
        }
      }
    }

    void RecvNetworkPacket (UdpEndPoint ep, UdpStream stream, int bytes) {
      // set stream length
      stream.Length = bytes << 3;

      // try to grab connection
      UdpConnection cn;

      if (connLookup.TryGetValue(ep, out cn)) {
        // deliver to connection
        cn.OnPacket(stream);

      } else {
        // handle unconnected data
        RecvUnconnectedPacket(stream, ep);
      }
    }

    void RecvUnconnectedPacket (UdpStream buffer, UdpEndPoint ep) {
      UdpAssert.Assert(buffer.Ptr == 0);
      buffer.Ptr = HeaderBitSize;

      if (buffer.ReadByte(8) == (byte) UdpCommandType.Connect) {
        if (Config.AllowIncommingConnections && ((connLookup.Count + pendingConnections.Count) < Config.ConnectionLimit || Config.ConnectionLimit == -1)) {
          if (Config.AutoAcceptIncommingConnections) {
            AcceptConnection(ep);
          } else {
            if (pendingConnections.Add(ep)) {
              Raise(UdpEvent.PUBLIC_CONNECT_REQUEST, ep);
            }
          }
        } else {
          SendRefusedCommand(ep);
        }
      }
    }

    #region Partial Methods
    partial void DelayPacket (UdpEndPoint ep, byte[] data, int length);
    partial void DelayPacketBy (UdpEndPoint ep, byte[] data, int length, uint delay);
    partial void RecvDelayedPackets ();
    #endregion

    public static UdpSocket Create (UdpPlatform platform, UdpSerializerFactory serializer, UdpConfig config) {
      return new UdpSocket(platform, serializer, config);
    }

    public static UdpSocket Create (UdpPlatform platform, UdpSerializerFactory serializer) {
      return Create(platform, serializer, new UdpConfig());
    }

    public static UdpSocket Create<TPlatform, TSerializer> (UdpConfig config)
      where TPlatform : UdpPlatform, new()
      where TSerializer : UdpSerializer, new() {
      return new UdpSocket(new TPlatform(), () => new TSerializer(), config);
    }

    public static UdpSocket Create<TPlatform, TSerializer> ()
      where TPlatform : UdpPlatform, new()
      where TSerializer : UdpSerializer, new() {
      return Create<TPlatform, TSerializer>(new UdpConfig());
    }

    public static UdpSocket Create (UdpPlatform platform, UdpConfig config) {
      return Create(platform, () => new UdpStreamSerializer(), config);
    }

    public static UdpSocket Create (UdpPlatform platform) {
      return Create(platform, new UdpConfig());
    }

    public static UdpSocket Create<TPlatform> ()
        where TPlatform : UdpPlatform, new() {
      return Create(new TPlatform(), new UdpConfig());
    }

    public static UdpSocket Create<TPlatform> (UdpConfig config)
        where TPlatform : UdpPlatform, new() {
      return Create(new TPlatform(), config);
    }

    public static UdpSocketMultiplexer CreateMultiplexer (params UdpSocket[] sockets) {
      return new UdpSocketMultiplexer(sockets);
    }

    public static UdpSocketMultiplexer CreateMultiplexer<TPlatform, TSerializer> (UdpIPv4Address address, ushort portMin, ushort portMax)
      where TPlatform : UdpPlatform, new()
      where TSerializer : UdpSerializer, new() {
      return CreateMultiplexer<TPlatform, TSerializer>(address, portMin, portMax, new UdpConfig());
    }

    public static UdpSocketMultiplexer CreateMultiplexer<TPlatform, TSerializer> (UdpIPv4Address address, ushort portMin, ushort portMax, UdpConfig config)
      where TPlatform : UdpPlatform, new()
      where TSerializer : UdpSerializer, new() {

      if (portMin > portMax) {
        throw new ArgumentOutOfRangeException("portMin was larger then portMax");
      }

      List<UdpSocket> sockets = new List<UdpSocket>();

      for (; portMin <= portMax; portMin += 1) {
        // create and start socket
        UdpSocket s = Create<TPlatform, TSerializer>(config);
        s.Start(new UdpEndPoint(address, portMin));

        // add to list
        sockets.Add(s);
      }

      return CreateMultiplexer(sockets.ToArray());
    }
  }
}
