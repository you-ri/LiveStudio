// Copyright (c) You-Ri, 2026
//
// ⚠ 同期注意: このファイルは jp.lilium.virgo.capture と jp.lilium.livestudio.virgo に
//   複製されています。片方を変更したときは必ずもう片方も同じ内容に更新してください。
//   ペア: Packages/jp.lilium.virgo.capture/Runtime/Network/UdpConnection.cs
//   namespace のみ Lilium.Virgo.Capture.Networking / Lilium.LiveStudio.Virgo.Networking で異なります。

using UnityEngine;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;

namespace Lilium.LiveStudio.Virgo.Networking
{
    //TODO: 最適化
    // Socket.SendTo() を直接使用（より低レベルだが制御可能）
    // バッファプールを使用してアロケーションを削減
    public class UDPConnection
    {
        private UdpClient _udpClient;

        private IPEndPoint _remoteEndPoint;

        private Thread _receiveThread;
        private bool _isRunning;                                   // 追加: 終了制御用フラグ
        private CancellationTokenSource _cancellationTokenSource; // Thread.Abort代替用

        private byte[] _reusableBuffer;                           // 再利用可能バッファ
        private int _bufferSize;                                  // バッファサイズ
        private readonly object _sendLock = new object();         // 送信用ロック

        public delegate void DataReceivedHandler(byte[] data);

        /// <summary>
        /// データ受信イベント
        /// 別スレッドから呼び出されるため、スレッドセーフに処理すること
        /// </summary>
        public event DataReceivedHandler onDataReceived;

        public bool isOpened => _udpClient != null;

        ~UDPConnection()
        {
            Close();
        }

        public void Open(string remoteAddress, int remotePort)
        {
            if (isOpened)
            {
                Close();
            }

            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
            _udpClient = new UdpClient();
        }

        public void Open(int localPort)
        {
            if (isOpened)
            {
                Close();
            }

            // アドレス再利用を有効にしてからバインド
            var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            _udpClient = client;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _receiveThread = new Thread(new ThreadStart(ReceiveDataThread));
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }

        public unsafe void SendData(byte* data, int length)
        {
            if (!isOpened || _remoteEndPoint == null) return;

            lock (_sendLock)
            {
                // バッファサイズが不足している場合のみ再作成
                if (_reusableBuffer == null || _reusableBuffer.Length < length)
                {
                    _reusableBuffer = new byte[Mathf.NextPowerOfTwo(length)];
                    _bufferSize = _reusableBuffer.Length;
                }

                fixed (byte* managedPtr = _reusableBuffer)
                {
                    UnsafeUtility.MemCpy(managedPtr, data, length);
                }

                try
                {
                    _udpClient.Send(_reusableBuffer, length, _remoteEndPoint);
                }
                catch (SocketException e)
                {
                    Debug.LogWarning($"[Core] UDP send failed: {e.Message}");
                }
            }
        }


        public void SendData(byte[] data)
        {
            if (!isOpened || _remoteEndPoint == null) return;

            lock (_sendLock)
            {
                try
                {
                    _udpClient.Send(data, data.Length, _remoteEndPoint);
                }
                catch (SocketException e)
                {
                    Debug.LogWarning($"[Core] UDP send failed: {e.Message}");
                }
            }
        }

        public void SendData(NativeArray<byte> data)
        {
            if (!isOpened || _remoteEndPoint == null) return;

            lock (_sendLock)
            {
                // バッファサイズが不足している場合のみ再作成
                if (_reusableBuffer == null || _reusableBuffer.Length < data.Length)
                {
                    _reusableBuffer = new byte[Mathf.NextPowerOfTwo(data.Length)];
                    _bufferSize = _reusableBuffer.Length;
                }

                unsafe
                {
                    var ptr = (byte*)data.GetUnsafeReadOnlyPtr();
                    fixed (byte* managedPtr = _reusableBuffer)
                    {
                        UnsafeUtility.MemCpy(managedPtr, ptr, data.Length);
                    }

                    try
                    {
                        _udpClient.Send(_reusableBuffer, data.Length, _remoteEndPoint);
                    }
                    catch (SocketException e)
                    {
                        Debug.LogWarning($"[Core] UDP send failed: {e.Message}");
                    }
                }
            }
        }

        public void SendText(string message)
        {
            if (!isOpened || _remoteEndPoint == null) return;

            byte[] data = Encoding.UTF8.GetBytes(message);
            lock (_sendLock)
            {
                try
                {
                    _udpClient.Send(data, data.Length, _remoteEndPoint);
                }
                catch (SocketException e)
                {
                    Debug.LogWarning($"[Core] UDP send failed: {e.Message}");
                }
            }
        }



        void ReceiveDataThread()
        {
            var token = _cancellationTokenSource.Token;
            while (_isRunning && !token.IsCancellationRequested)
            {
                _ReceiveAndDispatch();
            }
        }

        /// <summary>
        /// データを受信して登録されたイベントにディスパッチする。
        /// </summary>
        private void _ReceiveAndDispatch()
        {
            if (_udpClient == null)
            {
                return;
            }
            if (_udpClient.Available == 0)
            {
                return;
            }

            byte[] rawData = _udpClient.Receive(ref _remoteEndPoint);

            // 通常のデータ受信イベント
            onDataReceived?.Invoke(rawData);
        }

        public void Close()
        {
            _isRunning = false;                                    // スレッド停止指示
            _cancellationTokenSource?.Cancel();                   // Thread.Abort代替：キャンセレーション要求

            if (_receiveThread != null)
            {
                if (!_receiveThread.Join(1000))                    // タイムアウトを1秒に延長
                {
                    // Thread.Abort()を削除：キャンセレーションとUdpClient.Close()で安全に終了
                    Debug.LogWarning("[Core] UDP receive thread did not terminate gracefully");
                }
                _receiveThread = null;
            }

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }
}
