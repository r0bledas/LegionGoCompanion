using HandheldCompanion.Devices;

namespace HandheldCompanion.Controllers.MSI
{
    public class XboxAdaptiveController : XInputController
    {
        public XboxAdaptiveController()
        { }

        public XboxAdaptiveController(PnPDetails details) : base(details)
        { }

        public bool Enable()
        {
            if (false)
                return rogAlly.XBoxController(false);
            return false;
        }

        public bool Disable()
        {
            if (false)
                return rogAlly.XBoxController(true);
            return false;
        }
    }
}
