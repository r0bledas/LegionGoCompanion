using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using System;

namespace HandheldCompanion.Controllers.Lenovo
{
    public abstract class LegionControllerBase : IController
    {
        protected controller_hidapi.net.LegionController? Controller;
        protected byte[] data = new byte[64];

        protected bool IsPassthrough = false;
        protected bool IsControllerSwap = false;

        public LegionControllerBase() : base() { }

        public LegionControllerBase(PnPDetails details) : base()
        {
            AttachDetails(details);
        }

        protected virtual void Open()
        {
            lock (hidLock)
            {
                try
                {
                    if (Controller is not null)
                    {
                        Controller.OnControllerInputReceived -= Controller_OnControllerInputReceived;
                        Controller.OnControllerInputReceived += Controller_OnControllerInputReceived;
                        Controller.Open();
                        SendGyroEnableCommands();
                    }
                }
                catch (Exception ex)
                {
                    LogManager.LogError("Couldn't initialize {0}. Exception: {1}", GetType(), ex.Message);
                    return;
                }
            }
        }

        public void SendGyroEnableCommands()
        {
            lock (hidLock)
            {
                if (Controller is not null && Controller.IsDeviceValid)
                {
                    try
                    {
                        byte[][] cmds = [
                            [0x05, 0x06, 0x6A, 0x02, (byte)Devices.Lenovo.LegionGoTablet.LeftJoyconIndex, 0x01, 0x01],
                            [0x05, 0x06, 0x6A, 0x07, (byte)Devices.Lenovo.LegionGoTablet.LeftJoyconIndex, 0x02, 0x01],
                            [0x05, 0x06, 0x6A, 0x02, (byte)Devices.Lenovo.LegionGoTablet.RightJoyconIndex, 0x01, 0x01],
                            [0x05, 0x06, 0x6A, 0x07, (byte)Devices.Lenovo.LegionGoTablet.RightJoyconIndex, 0x02, 0x01]
                        ];

                        foreach (var cmd in cmds)
                        {
                            byte[] buf = new byte[64];
                            Array.Copy(cmd, buf, cmd.Length);
                            Controller.HidWrite(buf);
                        }
                    }
                    catch { }
                }
            }
        }

        protected virtual void Close()
        {
            lock (hidLock)
            {
                if (Controller is not null)
                {
                    Controller.OnControllerInputReceived -= Controller_OnControllerInputReceived;
                    Controller.Close();
                }
            }
        }

        public override void Gone()
        {
            lock (hidLock)
            {
                if (Controller is not null)
                {
                    Controller.OnControllerInputReceived -= Controller_OnControllerInputReceived;
                    Controller.EndRead();
                    Controller = null;
                }
            }
        }

        private void Controller_OnControllerInputReceived(byte[] Data)
        {
            Buffer.BlockCopy(Data, 0, this.data, 0, Data.Length);
        }

        public override void Plug()
        {
            Open();

            base.Plug();
        }

        public override void Unplug()
        {
            Close();

            base.Unplug();
        }

        protected virtual bool UpdateState() => false;

        protected override void QuerySettings()
        {
            SettingsManager_SettingValueChanged("LegionControllerPassthrough", ManagerFactory.settingsManager.GetBoolean("LegionControllerPassthrough"), false, false);
            SettingsManager_SettingValueChanged("LegionControllerSwap", ManagerFactory.settingsManager.GetBoolean("LegionControllerSwap"), false, false);
            base.QuerySettings();
        }

        protected override void SettingsManager_SettingValueChanged(string name, object? value, bool temporary, bool initializing)
        {
            switch (name)
            {
                case "LegionControllerPassthrough":
                    IsPassthrough = Convert.ToBoolean(value);
                    break;
                case "LegionControllerSwap":
                    IsControllerSwap = Convert.ToBoolean(value);
                    break;
            }

            base.SettingsManager_SettingValueChanged(name, value, temporary, initializing);
        }

        protected void ApplyControllerSwap()
        {
            (Inputs.ButtonState[ButtonFlags.OEM1], Inputs.ButtonState[ButtonFlags.Start]) = (Inputs.ButtonState[ButtonFlags.Start], Inputs.ButtonState[ButtonFlags.OEM1]);
            (Inputs.ButtonState[ButtonFlags.OEM2], Inputs.ButtonState[ButtonFlags.Back]) = (Inputs.ButtonState[ButtonFlags.Back], Inputs.ButtonState[ButtonFlags.OEM2]);
        }
    }
}
