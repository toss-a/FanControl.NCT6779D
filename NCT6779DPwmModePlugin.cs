using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FanControl.Plugins;

[assembly: AssemblyTitle("FanControl.NCT6779D")]
[assembly: AssemblyDescription("Nuvoton NCT677x / NCT679x Dedicated SYS_FAN Fan & PWM Controller Plugin for FanControl")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace FanControl.NCT6779D
{
    /// <summary>
    /// Dedicated standalone FanControl plugin for Nuvoton NCT6779D.
    /// Provides native SYS_FAN (PWM 1) control and RPM monitoring while enforcing
    /// hardware PWM Mode (Bank 0, Reg 0x04, Bit 0 = 0).
    /// </summary>
    public class NCT6779DPlugin : IPlugin2
    {
        private readonly IPluginLogger _logger;
        private Nct6779dHardware _hw;
        private Nct6779dFanSensor _fanSensor;
        private Nct6779dControlSensor _controlSensor;
        private int _updateCounter;

        public string Name
        {
            get { return "NCT6779D SYS_FAN"; }
        }

        public NCT6779DPlugin()
        {
        }

        public NCT6779DPlugin(IPluginLogger logger)
        {
            _logger = logger;
        }

        public void Initialize()
        {
            try
            {
                if (_hw != null)
                {
                    _hw.Close();
                }

                _hw = new Nct6779dHardware(_logger);
                if (_hw.Initialize())
                {
                    // Ensure PWM 1 is in PWM Mode immediately
                    _hw.SetPwm1Mode(true);

                    _fanSensor = new Nct6779dFanSensor(_hw);
                    _controlSensor = new Nct6779dControlSensor(_hw, _fanSensor.Id);
                }
            }
            catch (Exception ex)
            {
                Log("Initialize failed: " + ex.Message);
            }
        }

        public void Load(IPluginSensorsContainer container)
        {
            if (_hw == null || !_hw.IsInitialized)
                return;

            if (_fanSensor != null)
            {
                container.FanSensors.Add(_fanSensor);
            }

            if (_controlSensor != null)
            {
                container.ControlSensors.Add(_controlSensor);
            }
        }

        public void Update()
        {
            if (_hw == null || !_hw.IsInitialized)
                return;

            _hw.Update();
            if (_fanSensor != null)
            {
                _fanSensor.Update();
            }

            // Periodically check that PWM Mode has not been reset by BIOS (e.g., after S3 sleep)
            _updateCounter++;
            if (_updateCounter >= 5)
            {
                _updateCounter = 0;
                _hw.EnsurePwm1Mode(true);
            }
        }

        public void Close()
        {
            try
            {
                if (_controlSensor != null)
                {
                    _controlSensor.Reset();
                    _controlSensor = null;
                }

                _fanSensor = null;

                if (_hw != null)
                {
                    _hw.Close();
                    _hw = null;
                }
            }
            catch (Exception ex)
            {
                Log("Close failed: " + ex.Message);
            }
        }

        private void Log(string message)
        {
            if (_logger != null)
            {
                _logger.Log("[NCT6779D] " + message);
            }
        }
    }

    internal sealed class Nct6779dFanSensor : IPluginSensor
    {
        private readonly Nct6779dHardware _hw;
        private float? _value;

        public Nct6779dFanSensor(Nct6779dHardware hw)
        {
            _hw = hw;
        }

        public string Id
        {
            get { return "NCT6779D_SYS_FAN_RPM"; }
        }

        public string Name
        {
            get { return "NCT6779D SYS_FAN"; }
        }

        public float? Value
        {
            get { return _value; }
        }

        public void Update()
        {
            _value = _hw.SysFanRpm;
        }
    }

    internal sealed class Nct6779dControlSensor : IPluginControlSensor2
    {
        private readonly Nct6779dHardware _hw;
        private readonly string _pairedFanId;
        private float? _currentValue;

        public Nct6779dControlSensor(Nct6779dHardware hw, string pairedFanId)
        {
            _hw = hw;
            _pairedFanId = pairedFanId;
        }

        public string Id
        {
            get { return "NCT6779D_SYS_FAN_Control"; }
        }

        public string Name
        {
            get { return "NCT6779D SYS_FAN Control (PWM)"; }
        }

        public float? Value
        {
            get { return _currentValue; }
        }

        public string PairedFanSensorId
        {
            get { return _pairedFanId; }
        }

        public void Set(float val)
        {
            _hw.SetControl(val);
            _currentValue = val;
        }

        public void Reset()
        {
            _hw.RestoreDefaultControl();
            _currentValue = null;
        }

        public void Update()
        {
            if (!_currentValue.HasValue)
            {
                _currentValue = _hw.SysFanDutyPercent;
            }
        }
    }

    internal sealed class Nct6779dHardware
    {
        private static readonly ushort[] RegisterPorts = new ushort[] { 0x2E, 0x4E };
        private static readonly ushort[] ValuePorts = new ushort[] { 0x2F, 0x4F };
        private const byte CHIP_ID_REGISTER = 0x20;
        private const byte CHIP_REVISION_REGISTER = 0x21;
        private const byte WINBOND_NUVOTON_HARDWARE_MONITOR_LDN = 0x0B;
        private const byte BASE_ADDRESS_REGISTER = 0x60;

        private const ushort ADDRESS_REGISTER_OFFSET = 0x05;
        private const ushort DATA_REGISTER_OFFSET = 0x06;
        private const byte BANK_SELECT_REGISTER = 0x4E;
        private const ushort VENDOR_ID_HIGH_REGISTER = 0x804F;
        private const ushort VENDOR_ID_LOW_REGISTER = 0x004F;
        private const ushort NUVOTON_VENDOR_ID = 0x5CA3;

        // SYS_FAN (Control #1 / Fan #1) registers:
        // Bank 0, Reg 0x04: Bit 0 = 0 -> PWM Mode, Bit 0 = 1 -> DC Mode
        private const ushort REG_PWM1_MODE = 0x0004;
        private const byte PWM1_MODE_DC_BIT = 0x01;

        // Bank 0, Reg 0x01: Actual PWM output duty (0-255)
        private const ushort REG_PWM1_OUT = 0x0001;

        // Bank 1, Reg 0x02: Fan control mode (0 = Manual mode)
        private const ushort REG_FAN1_CONTROL_MODE = 0x0102;

        // Bank 1, Reg 0x09: Fan PWM manual command (0-255)
        private const ushort REG_FAN1_PWM_COMMAND = 0x0109;

        // Default Fan Count registers (0x4B0 and 0x4B1 for NCT6779D and NCT679x)
        private ushort _fanCountRegHigh = 0x04B0;
        private ushort _fanCountRegLow = 0x04B1;
        private bool _isDirectRpmRegister = false;
        private string _chipName = "NCT6779D";

        private readonly IPluginLogger _logger;
        private object _lpcPort;
        private ushort _baseAddress;

        private byte _initialControlMode;
        private byte _initialPwmCommand;
        private bool _hasSavedDefault;

        private MethodInfo _waitIsaBusMethod;
        private MethodInfo _releaseIsaBusMethod;
        private MethodInfo _readIoPortMethod;
        private MethodInfo _writeIoPortMethod;
        private MethodInfo _closePortMethod;

        public bool IsInitialized { get; private set; }
        public float? SysFanRpm { get; private set; }
        public float? SysFanDutyPercent { get; private set; }

        public Nct6779dHardware(IPluginLogger logger)
        {
            _logger = logger;
        }

        public bool Initialize()
        {
            Assembly lhmAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "LibreHardwareMonitorLib", StringComparison.OrdinalIgnoreCase));

            if (lhmAssembly == null)
            {
                string lhmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LibreHardwareMonitorLib.dll");
                if (File.Exists(lhmPath))
                {
                    lhmAssembly = Assembly.LoadFrom(lhmPath);
                }
            }

            if (lhmAssembly == null)
            {
                Log("Could not find LibreHardwareMonitorLib.dll.");
                return false;
            }

            Type mutexesType = lhmAssembly.GetType("LibreHardwareMonitor.Hardware.Mutexes")
                            ?? lhmAssembly.GetType("LibreHardwareMonitor.Hardware.Ring0");
            if (mutexesType != null)
            {
                // Ensure Mutexes are opened in LHM
                MethodInfo openMethod = mutexesType.GetMethod("Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                try
                {
                    if (openMethod != null)
                    {
                        openMethod.Invoke(null, null);
                    }
                }
                catch { }

                _waitIsaBusMethod = mutexesType.GetMethod("WaitIsaBus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                 ?? mutexesType.GetMethod("WaitIsaBusMutex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _releaseIsaBusMethod = mutexesType.GetMethod("ReleaseIsaBus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                    ?? mutexesType.GetMethod("ReleaseIsaBusMutex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            Type lpcPortType = lhmAssembly.GetType("LibreHardwareMonitor.Hardware.Motherboard.Lpc.LpcPort");
            if (lpcPortType == null)
            {
                Log("Could not find LpcPort in LibreHardwareMonitorLib.");
                return false;
            }

            ConstructorInfo ctor = lpcPortType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new Type[] { typeof(ushort), typeof(ushort) },
                null);

            MethodInfo enterMethod = lpcPortType.GetMethod("WinbondNuvotonFintekEnter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo exitMethod = lpcPortType.GetMethod("WinbondNuvotonFintekExit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo readByteMethod = lpcPortType.GetMethod("ReadByte", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo readWordMethod = lpcPortType.GetMethod("ReadWord", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo selectMethod = lpcPortType.GetMethod("Select", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo findBarsMethod = lpcPortType.GetMethod("FindBars", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _readIoPortMethod = lpcPortType.GetMethod("ReadIoPort", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _writeIoPortMethod = lpcPortType.GetMethod("WriteIoPort", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _closePortMethod = lpcPortType.GetMethod("Close", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (ctor == null || enterMethod == null || exitMethod == null || readByteMethod == null ||
                readWordMethod == null || selectMethod == null || _readIoPortMethod == null || _writeIoPortMethod == null)
            {
                Log("LpcPort reflection failed.");
                return false;
            }

            // Retry up to 10 times (total up to 2 seconds) to avoid startup mutex contention with LHM
            bool acquiredMutex = false;
            for (int retry = 0; retry < 10; retry++)
            {
                if (WaitIsaBus(200))
                {
                    acquiredMutex = true;
                    break;
                }
                Thread.Sleep(50);
            }

            if (!acquiredMutex)
            {
                Log("Could not acquire ISA bus mutex after retries.");
                return false;
            }

            try
            {
                for (int i = 0; i < RegisterPorts.Length; i++)
                {
                    object port = ctor.Invoke(new object[] { RegisterPorts[i], ValuePorts[i] });
                    try
                    {
                        enterMethod.Invoke(port, null);
                        byte id = (byte)readByteMethod.Invoke(port, new object[] { CHIP_ID_REGISTER });
                        byte revision = (byte)readByteMethod.Invoke(port, new object[] { CHIP_REVISION_REGISTER });

                        string chipName;
                        ushort regHigh;
                        ushort regLow;
                        bool isDirectRpm;

                        // Check NCT677x and NCT679x family chips
                        if (DetectNuvotonChip(id, revision, out chipName, out regHigh, out regLow, out isDirectRpm))
                        {
                            if (findBarsMethod != null)
                            {
                                findBarsMethod.Invoke(port, null);
                            }
                            selectMethod.Invoke(port, new object[] { WINBOND_NUVOTON_HARDWARE_MONITOR_LDN });

                            ushort address = (ushort)readWordMethod.Invoke(port, new object[] { BASE_ADDRESS_REGISTER });
                            Thread.Sleep(1);
                            ushort verify = (ushort)readWordMethod.Invoke(port, new object[] { BASE_ADDRESS_REGISTER });

                            exitMethod.Invoke(port, null);

                            if ((address & 0x07) == 0x05)
                                address &= 0xFFF8;

                            if (address == verify && address >= 0x100 && (address & 0xF007) == 0)
                            {
                                _lpcPort = port;
                                _baseAddress = address;
                                _chipName = chipName;
                                _fanCountRegHigh = regHigh;
                                _fanCountRegLow = regLow;
                                _isDirectRpmRegister = isDirectRpm;

                                ushort vendorId = (ushort)((ReadHwMonByte(VENDOR_ID_HIGH_REGISTER) << 8) | ReadHwMonByte(VENDOR_ID_LOW_REGISTER));
                                if (vendorId == NUVOTON_VENDOR_ID)
                                {
                                    byte reg04 = ReadHwMonByte(REG_PWM1_MODE);
                                    bool isDc = (reg04 & PWM1_MODE_DC_BIT) != 0;
                                    Log(string.Format(
                                        "Detected Nuvoton {0} (ID=0x{1:X2}, Rev=0x{2:X2}) at HM Base=0x{3:X4}. Initial Reg 0x004=0x{4:X2} ({5}).",
                                        _chipName, id, revision, _baseAddress, reg04, isDc ? "DC Mode" : "PWM Mode"));
                                    IsInitialized = true;
                                    return true;
                                }
                            }
                        }
                        else if (id != 0 && id != 0xFF)
                        {
                            exitMethod.Invoke(port, null);
                        }
                    }
                    catch
                    {
                    }

                    if (_closePortMethod != null)
                    {
                        _closePortMethod.Invoke(port, null);
                    }
                }
            }
            finally
            {
                ReleaseIsaBus();
            }

            Log("NCT6779D chip not found on ISA ports.");
            return false;
        }

        public void Update()
        {
            if (!IsInitialized)
                return;

            if (!WaitIsaBus(50))
                return;

            try
            {
                // 1. Read RPM from Fan Count/RPM registers
                byte high = ReadHwMonByte(_fanCountRegHigh);
                byte low = ReadHwMonByte(_fanCountRegLow);

                if (_isDirectRpmRegister)
                {
                    int val = (high << 8) | low;
                    SysFanRpm = val > 20 ? (float)val : 0.0f;
                }
                else
                {
                    int count = (high << 5) | (low & 0x1F);
                    if (count < 0x1FFF && count >= 0x15)
                    {
                        SysFanRpm = 1350000.0f / count;
                    }
                    else
                    {
                        SysFanRpm = 0.0f;
                    }
                }

                // 2. Read actual output duty cycle from 0x001
                byte outDuty = ReadHwMonByte(REG_PWM1_OUT);
                SysFanDutyPercent = (float)Math.Round(outDuty / 2.55f);
            }
            finally
            {
                ReleaseIsaBus();
            }
        }

        public void SetControl(float percent)
        {
            if (!IsInitialized)
                return;

            if (!WaitIsaBus(100))
                return;

            try
            {
                SaveDefaultControlInternal();

                // 1. Ensure PWM Mode is active (Bank 0, Reg 0x04, Bit 0 = 0)
                byte reg04 = ReadHwMonByte(REG_PWM1_MODE);
                if ((reg04 & PWM1_MODE_DC_BIT) != 0)
                {
                    WriteHwMonByte(REG_PWM1_MODE, (byte)(reg04 & ~PWM1_MODE_DC_BIT));
                }

                // 2. Set Manual Mode (Bank 1, Reg 0x02 = 0)
                WriteHwMonByte(REG_FAN1_CONTROL_MODE, 0);

                // 3. Set PWM output command value (0-255) (Bank 1, Reg 0x09)
                float clamped = Math.Max(0.0f, Math.Min(100.0f, percent));
                byte pwmVal = (byte)Math.Round(clamped * 2.55f);
                WriteHwMonByte(REG_FAN1_PWM_COMMAND, pwmVal);
            }
            finally
            {
                ReleaseIsaBus();
            }
        }

        public void RestoreDefaultControl()
        {
            if (!IsInitialized || !_hasSavedDefault)
                return;

            if (!WaitIsaBus(100))
                return;

            try
            {
                // Restore original control mode and PWM command
                WriteHwMonByte(REG_FAN1_CONTROL_MODE, _initialControlMode);
                WriteHwMonByte(REG_FAN1_PWM_COMMAND, _initialPwmCommand);
                _hasSavedDefault = false;
            }
            finally
            {
                ReleaseIsaBus();
            }
        }

        public void SetPwm1Mode(bool isPwmMode)
        {
            if (!IsInitialized)
                return;

            if (!WaitIsaBus(100))
                return;

            try
            {
                byte oldVal = ReadHwMonByte(REG_PWM1_MODE);
                byte newVal = isPwmMode
                    ? (byte)(oldVal & ~PWM1_MODE_DC_BIT)
                    : (byte)(oldVal | PWM1_MODE_DC_BIT);

                if (oldVal != newVal)
                {
                    WriteHwMonByte(REG_PWM1_MODE, newVal);
                    byte verify = ReadHwMonByte(REG_PWM1_MODE);
                    Log(string.Format(
                        "Set sys_fan (PWM 1 Type) from 0x{0:X2} to 0x{1:X2} ({2}).",
                        oldVal, verify, (verify & PWM1_MODE_DC_BIT) != 0 ? "DC Mode" : "PWM Mode"));
                }
            }
            finally
            {
                ReleaseIsaBus();
            }
        }

        public void EnsurePwm1Mode(bool isPwmMode)
        {
            if (!IsInitialized)
                return;

            if (!WaitIsaBus(30))
                return;

            try
            {
                byte currentVal = ReadHwMonByte(REG_PWM1_MODE);
                bool currentIsPwm = (currentVal & PWM1_MODE_DC_BIT) == 0;
                if (currentIsPwm != isPwmMode)
                {
                    byte newVal = isPwmMode
                        ? (byte)(currentVal & ~PWM1_MODE_DC_BIT)
                        : (byte)(currentVal | PWM1_MODE_DC_BIT);
                    WriteHwMonByte(REG_PWM1_MODE, newVal);
                    Log(string.Format("Restored PWM 1 Type to {0} (0x{1:X2} -> 0x{2:X2}).",
                        isPwmMode ? "PWM Mode" : "DC Mode", currentVal, newVal));
                }
            }
            finally
            {
                ReleaseIsaBus();
            }
        }

        private void SaveDefaultControlInternal()
        {
            if (!_hasSavedDefault)
            {
                _initialControlMode = ReadHwMonByte(REG_FAN1_CONTROL_MODE);
                _initialPwmCommand = ReadHwMonByte(REG_FAN1_PWM_COMMAND);
                _hasSavedDefault = true;
            }
        }

        public void Close()
        {
            if (_lpcPort != null)
            {
                try
                {
                    if (_closePortMethod != null)
                    {
                        _closePortMethod.Invoke(_lpcPort, null);
                    }
                }
                catch
                {
                }
                _lpcPort = null;
            }
            IsInitialized = false;
        }

        private byte ReadHwMonByte(ushort address)
        {
            byte bank = (byte)(address >> 8);
            byte register = (byte)(address & 0xFF);
            WriteIoPort((ushort)(_baseAddress + ADDRESS_REGISTER_OFFSET), BANK_SELECT_REGISTER);
            WriteIoPort((ushort)(_baseAddress + DATA_REGISTER_OFFSET), bank);
            WriteIoPort((ushort)(_baseAddress + ADDRESS_REGISTER_OFFSET), register);
            return ReadIoPort((ushort)(_baseAddress + DATA_REGISTER_OFFSET));
        }

        private void WriteHwMonByte(ushort address, byte value)
        {
            byte bank = (byte)(address >> 8);
            byte register = (byte)(address & 0xFF);
            WriteIoPort((ushort)(_baseAddress + ADDRESS_REGISTER_OFFSET), BANK_SELECT_REGISTER);
            WriteIoPort((ushort)(_baseAddress + DATA_REGISTER_OFFSET), bank);
            WriteIoPort((ushort)(_baseAddress + ADDRESS_REGISTER_OFFSET), register);
            WriteIoPort((ushort)(_baseAddress + DATA_REGISTER_OFFSET), value);
        }

        private byte ReadIoPort(ushort port)
        {
            return (byte)_readIoPortMethod.Invoke(_lpcPort, new object[] { port });
        }

        private void WriteIoPort(ushort port, byte value)
        {
            _writeIoPortMethod.Invoke(_lpcPort, new object[] { port, value });
        }

        private bool WaitIsaBus(int timeoutMs)
        {
            if (_waitIsaBusMethod == null)
                return true;
            try
            {
                return (bool)_waitIsaBusMethod.Invoke(null, new object[] { timeoutMs });
            }
            catch
            {
                return true;
            }
        }

        private void ReleaseIsaBus()
        {
            try
            {
                if (_releaseIsaBusMethod != null)
                {
                    _releaseIsaBusMethod.Invoke(null, null);
                }
            }
            catch
            {
            }
        }

        private static bool DetectNuvotonChip(byte id, byte rev, out string name, out ushort regHigh, out ushort regLow, out bool isDirectRpm)
        {
            regHigh = 0x04B0;
            regLow = 0x04B1;
            isDirectRpm = false;

            // NCT6779D (ID=0xC5, Rev=0x6x)
            if (id == 0xC5 && (rev & 0xF0) == 0x60) { name = "NCT6779D"; return true; }

            // NCT6791D ~ NCT6799D & NCT6701D
            if (id == 0xC8 && rev == 0x03) { name = "NCT6791D"; return true; }
            if (id == 0xC9 && rev == 0x11) { name = "NCT6792D"; return true; }
            if (id == 0xC9 && rev == 0x13) { name = "NCT6792D-A"; return true; }
            if (id == 0xD1 && rev == 0x21) { name = "NCT6793D"; return true; }
            if (id == 0xD3 && rev == 0x52) { name = "NCT6795D"; return true; }
            if (id == 0xD4 && rev == 0x23) { name = "NCT6796D"; return true; }
            if (id == 0xD4 && rev == 0x2A) { name = "NCT6796D-R"; return true; }
            if (id == 0xD4 && rev == 0x51) { name = "NCT6797D"; return true; }
            if (id == 0xD4 && rev == 0x2B) { name = "NCT6798D"; return true; }
            if (id == 0xD8 && rev == 0x02) { name = "NCT6799D"; return true; }
            if (id == 0xD8 && rev == 0x06) { name = "NCT6701D"; return true; }

            // NCT6776F (ID=0xC3, Rev=0x3x)
            if (id == 0xC3 && (rev & 0xF0) == 0x30)
            {
                name = "NCT6776F";
                regHigh = 0x0656;
                regLow = 0x0657;
                isDirectRpm = true;
                return true;
            }

            // NCT6771F (ID=0xB4, Rev=0x7x)
            if (id == 0xB4 && (rev & 0xF0) == 0x70)
            {
                name = "NCT6771F";
                regHigh = 0x0656;
                regLow = 0x0657;
                isDirectRpm = true;
                return true;
            }

            name = null;
            return false;
        }

        private void Log(string message)
        {
            if (_logger != null)
            {
                _logger.Log("[NCT6779D] " + message);
            }
        }
    }
}
