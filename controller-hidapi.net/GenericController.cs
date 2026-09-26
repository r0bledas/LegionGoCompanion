using hidapi;
using System;

namespace controller_hidapi.net
{
    public class GenericController
    {
        // device data
        protected ushort _vid, _pid;
        protected short _index;
        // subclass is responsible for opening the device
        protected HidDevice _hidDevice;

        public bool Reading => _hidDevice.Reading;
        public bool IsDeviceValid => _hidDevice.IsDeviceValid;
        public ushort DeviceVersion => _hidDevice.ReleaseNumber;

        public event OnControllerInputReceivedEventHandler OnControllerInputReceived;
        public delegate void OnControllerInputReceivedEventHandler(byte[] Data);

        public GenericController(ushort vid, ushort pid, ushort inputBufferLen, short index)
        {
            _vid = vid;
            _pid = pid;

            _hidDevice = new HidDevice(_vid, _pid, inputBufferLen, index)
            {
                OnInputReceived = OnInputReceived
            };
        }

        protected readonly byte[] _lastBuffer = new byte[64];
        public byte[] LastBuffer => _lastBuffer;

        internal virtual void OnInputReceived(HidDeviceInputReceivedEventArgs e)
        {
            if (e.Buffer != null)
            {
                lock (_lastBuffer)
                {
                    Buffer.BlockCopy(e.Buffer, 0, _lastBuffer, 0, Math.Min(e.Buffer.Length, _lastBuffer.Length));
                }
            }
            OnControllerInputReceived?.Invoke(e.Buffer);
        }

        public virtual bool Open(bool read = true)
        {
            bool isOpen = _hidDevice.OpenDevice();
            if (!isOpen)
                throw new Exception("Could not open device!");

            if (read)
                _hidDevice.BeginRead();

            return isOpen;
        }

        public virtual void Close()
        {
            EndRead();
            _hidDevice.Close();
        }

        public virtual void EndRead()
        {
            if (_hidDevice.IsDeviceValid)
                _hidDevice.EndRead();
        }

        public void HidWrite(byte[] data)
        {
            try
            {
                _hidDevice.Write(data);
            }
            catch { }
        }
    }
}
