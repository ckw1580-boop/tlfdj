using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim
{
    /// <summary>
    /// Runtime model for the Siemens BOP-style inverter panel preserved in the original player.
    /// It intentionally keeps the original parameter names so saved exercises can address them.
    /// </summary>
    public sealed class InverterPanelController : MonoBehaviour
    {
        public enum MenuMode
        {
            Monitor,
            Control,
            Diagnostics,
            Parameters,
            Setup,
            Extras
        }

        public enum MenuDepth
        {
            Root,
            List,
            Detail,
            Value
        }

        private sealed class ParameterDefinition
        {
            public readonly string Key;
            public readonly string Label;
            public readonly string Unit;
            public readonly float Min;
            public readonly float Max;
            public readonly float Step;
            public readonly string DefaultValue;
            public readonly string[] Options;

            public ParameterDefinition(
                string key,
                string label,
                string defaultValue,
                string unit = "",
                float min = 0f,
                float max = 0f,
                float step = 1f,
                params string[] options)
            {
                Key = key;
                Label = label;
                DefaultValue = defaultValue;
                Unit = unit;
                Min = min;
                Max = max;
                Step = step;
                Options = options ?? Array.Empty<string>();
            }

            public bool IsNumeric => Options.Length == 0;
        }

        private static readonly ParameterDefinition[] Definitions =
        {
            Numeric("SP", "手动速度设定", "0", "1/min", -1425f, 1425f),
            Option("P0010", "调试参数过滤器", "0", "0", "1"),
            Option("P0015", "宏程序选择", "1", "1", "2", "3", "4", "5", "6", "7", "8", "9", "12", "13", "14", "15", "17", "18", "19", "20", "21"),
            Option("P100", "电机标准 IEC/NEMA", "0", "0", "1", "2"),
            Numeric("P304", "电机额定电压", "400", "V", 0f, 20000f),
            Numeric("P305", "电机额定电流", "3.1", "A", 0f, 10000f, 0.1f),
            Numeric("P307", "电机额定功率", "1.1", "kW", 0f, 100000f, 0.1f),
            Numeric("P310", "电机额定频率", "50", "Hz", 0f, 1000f, 0.1f),
            Numeric("P311", "电机额定转速", "1425", "1/min", 0f, 210000f),
            Option("P756.0", "模拟输入 AI0 类型", "4", "0", "1", "2", "3", "4"),
            Numeric("P757.0", "模拟输入曲线 X1", "0", "V", -50f, 160f),
            Numeric("P758.0", "模拟输入曲线 Y1", "0", "%", -1000f, 1000f),
            Numeric("P759.0", "模拟输入曲线 X2", "10", "V", -50f, 160f),
            Numeric("P760.0", "模拟输入曲线 Y2", "100", "%", -1000f, 1000f),
            Option("P756.1", "模拟输入 AI1 类型", "4", "0", "1", "2", "3", "4"),
            Numeric("P757.1", "AI1 标定 X1", "0", "V/mA", -50f, 160f, 0.1f),
            Numeric("P758.1", "AI1 标定 Y1", "0", "%", -1000f, 1000f, 0.1f),
            Numeric("P759.1", "AI1 标定 X2", "10", "V/mA", -50f, 160f, 0.1f),
            Numeric("P760.1", "AI1 标定 Y2", "100", "%", -1000f, 1000f, 0.1f),
            Numeric("P1001", "固定转速 1", "0", "1/min", -210000f, 210000f),
            Numeric("P1002", "固定转速 2", "200", "1/min", -210000f, 210000f),
            Numeric("P1003", "固定转速 3", "300", "1/min", -210000f, 210000f),
            Numeric("P1004", "固定转速 4", "400", "1/min", -210000f, 210000f),
            Numeric("P1058", "JOG1 正向点动速度", "150", "1/min", -210000f, 210000f),
            Numeric("P1059", "JOG2 反向点动速度", "-150", "1/min", -210000f, 210000f),
            Numeric("P1037", "MOP 正向最大转速", "1500", "1/min", 0f, 210000f),
            Numeric("P1038", "MOP 反向最大转速", "-1500", "1/min", -210000f, 0f),
            Numeric("P1040", "MOP 初始转速", "0", "1/min", -210000f, 210000f),
            Numeric("P1080", "最小转速", "0", "1/min", 0f, 19500f),
            Numeric("P1082", "最大转速", "1500", "1/min", 0f, 210000f),
            Numeric("P1120", "斜坡上升时间", "10", "s", 0f, 999999f),
            Numeric("P1121", "斜坡下降时间", "30", "s", 0f, 999999f),
            Option("P922", "PROFIBUS 报文类型", "1", "1", "20", "352"),
            Numeric("P2020", "USS 通讯速率", "8", "", 0f, 12f),
            Numeric("P2021", "USS 通讯站地址", "0", "", 0f, 31f),
            Numeric("P2022", "USS 通讯 PZD 长度", "2", "", 0f, 8f),
            Numeric("P2023", "USS 通讯 PKW 长度", "127", "", 0f, 127f),
            Numeric("P2040", "总线接口监控时间", "100", "ms", 0f, 999999f)
        };

        private static readonly string[] ModeNames =
            { "MONiTOR", "CONTROL", "DiAGNOS", "PARAMS", "SETUP", "EXTRAS" };

        private static readonly string[] ControlItems = { "SETPOiNT", "JOG", "REVERSE" };
        private static readonly string[] DiagnosticItems = { "ACKN ALL", "FAULTS", "STATUS", "CTRL WORD", "STAT WORD", "MACRO" };
        private static readonly string[] FilterItems = { "STANDARD", "EXPERT" };
        private static readonly string[] SetupItems =
            { "RESET", "P0010", "P0015", "P100", "P304", "P305", "P307", "P310", "P311", "P1080", "P1082", "P1120", "P1121", "FiNiSH" };
        private static readonly string[] ExtraItems = { "DRVRESET", "RAM->ROM", "TO BOP", "FROM BOP", "TO CRD", "FROM CRD" };

        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool[] digitalInputs = new bool[6];
        private readonly bool[] digitalInputWritten = new bool[6];

        private GameObject panelRoot;
        private Text upperText;
        private Text lowerText;
        private Text upperUnit;
        private Text lowerUnit;
        private Text[] tipTexts;
        private GameObject runIndicator;
        private GameObject handIndicator;
        private GameObject jogIndicator;
        private GameObject errorIndicator;
        private GameObject[] modeIndicators;
        private Button closeButton;
        private Button escapeButton;
        private Button upButton;
        private Button downButton;
        private Button okButton;
        private Button stopButton;
        private Button handAutoButton;
        private InverterMomentaryButton runButton;
        private Action closeRequested;
        private int itemIndex;
        private int parameterIndex;
        private bool editingValue;
        private bool runCommand;
        private bool driveAvailable = true;
        private float requestedSpeedRpm;
        private float lastPublishedSpeed = float.NaN;
        private float analogInputNormalized;
        private float motorizedPotentiometerRpm;
        private ushort fieldbusControlWord = 0x047E;
        private float fieldbusSetpointRpm;
        private int latchedDirection = 1;
        private bool threeWireRunning;
        private bool hasAlarm;
        private int faultNumber;
        private int alarmNumber;

        public MenuMode CurrentMode { get; private set; } = MenuMode.Monitor;
        public MenuDepth CurrentDepth { get; private set; } = MenuDepth.List;
        public bool IsManualMode { get; private set; }
        public bool IsJogMode { get; private set; }
        public bool IsReverse { get; private set; }
        public bool HasFault { get; private set; }
        public bool IsRunning => Mathf.Abs(OutputSpeedRpm) > 0.1f;
        public bool IsEditingValue => editingValue;
        public float OutputSpeedRpm { get; private set; }
        public float SetpointRpm => GetNumericValue("SP");
        public float ActualSpeedRpm => OutputSpeedRpm;
        public int Macro => Mathf.RoundToInt(GetNumericOrOptionValue("P0015"));
        public string ActiveMacroName => G120MacroCatalog.Get(Macro).Name;
        public IReadOnlyList<string> ActiveMacroParameterMappings => G120MacroCatalog.Get(Macro).AutomaticSettings;
        public IReadOnlyList<int> SupportedMacros => G120MacroCatalog.SupportedNumbers;
        public int TelegramType => Mathf.RoundToInt(GetNumericOrOptionValue("P922"));
        public ushort FieldbusControlWord => fieldbusControlWord;
        public ushort FieldbusStatusWord { get; private set; }
        public float FieldbusSetpointRpm => fieldbusSetpointRpm;
        public float AnalogInputNormalized => analogInputNormalized;
        public float MotorizedPotentiometerRpm => motorizedPotentiometerRpm;
        public bool HasAlarm => hasAlarm;
        public int FaultNumber => faultNumber;
        public int AlarmNumber => alarmNumber;
        public bool IsLocalControl => Macro == 7 ? digitalInputs[3] :
            Macro == 14 ? (fieldbusControlWord & 0x8000) != 0 :
            Macro == 15 && digitalInputs[3];
        public IReadOnlyList<string> ParameterKeys => Definitions.Select(item => item.Key).ToArray();
        public event Action<float> OutputSpeedChanged;
        public bool UseExternalClock { get; set; }
        public bool OperationEnabled => driveAvailable && runCommand && !HasFault;
        public Func<bool> CanResetFault { get; set; }
        public event Action FactorySettingsReset;
        private float? calibratedAnalogPercent;


        public void Initialize(GameObject panel, Action onClose)
        {
            panelRoot = panel;
            closeRequested = onClose;
            values.Clear();
            foreach (var definition in Definitions) values[definition.Key] = definition.DefaultValue;
            ApplyMacroSettings(Macro);

            upperText = Find<Text>("txt_up");
            lowerText = Find<Text>("txt_down");
            upperUnit = Find<Text>("txt_upUnit");
            lowerUnit = Find<Text>("txt_downUnit");
            tipTexts = new[] { Find<Text>("one_tip"), Find<Text>("two_tip"), Find<Text>("three_tip"), Find<Text>("four_tip") };
            runIndicator = FindObject("img_run");
            handIndicator = FindObject("img_hand");
            jogIndicator = FindObject("img_jog");
            errorIndicator = FindObject("img_error");
            modeIndicators = new[]
            {
                FindObject("MONITORING"), FindObject("CONTROL"), FindObject("DIAGNOSTICS"),
                FindObject("PARAMETER"), FindObject("SETUP"), FindObject("EXTRAS")
            };

            closeButton = Bind("btn_close", Close);
            escapeButton = Bind("btn_esc", Escape);
            upButton = Bind("btn_up", () => Move(-1));
            downButton = Bind("btn_down", () => Move(1));
            okButton = Bind("btn_ok", Confirm);
            stopButton = Bind("btn_o", PressStop);
            handAutoButton = Bind("btn_handAuto", ToggleHandAuto);

            var runObject = FindObject("btn_i");
            if (runObject != null)
            {
                runButton = runObject.GetComponent<InverterMomentaryButton>();
                if (runButton == null) runButton = runObject.AddComponent<InverterMomentaryButton>();
                runButton.Pressed = PressRun;
                runButton.Released = ReleaseRun;
            }

            var mask = FindObject("img_mask");
            if (mask != null) mask.SetActive(false);
            var tipImage = FindObject("tip_image");
            if (tipImage != null) tipImage.SetActive(false);
            RefreshDisplay();
        }

        public bool TryGetParameter(string key, out float value)
        {
            value = 0f;
            return values.TryGetValue(ResolveParameterKey(key), out var text) &&
                   float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public string GetParameterText(string key)
        {
            return values.TryGetValue(ResolveParameterKey(key), out var value) ? value : null;
        }

        public bool TrySetParameter(string key, float value)
        {
            key = ResolveParameterKey(key);
            var definition = Definition(key);
            if (definition == null) return false;
            if (!definition.IsNumeric)
            {
                var option = value.ToString("0", CultureInfo.InvariantCulture);
                return definition.Options.Contains(option) && TrySetParameter(key, option);
            }
            var bounded = Mathf.Clamp(value, definition.Min, definition.Max);
            // Calibration is entered numerically in the property panel; a BOP button
            // step must not silently round two distinct calibration points together.
            var calibration = definition.Key.StartsWith("P757.") || definition.Key.StartsWith("P758.") ||
                definition.Key.StartsWith("P759.") || definition.Key.StartsWith("P760.");
            values[definition.Key] = calibration ? bounded.ToString("G9", CultureInfo.InvariantCulture) : Format(bounded, definition.Step);
            if (string.Equals(definition.Key, "P1080", StringComparison.OrdinalIgnoreCase) &&
                GetNumericValue("P1082") < GetNumericValue("P1080"))
                values["P1082"] = values["P1080"];
            RefreshRequestedSpeed();
            RefreshDisplay();
            return true;
        }

        public bool TrySetParameter(string key, string value)
        {
            key = ResolveParameterKey(key);
            var definition = Definition(key);
            if (definition == null) return false;
            if (definition.IsNumeric)
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                       TrySetParameter(key, number);
            }
            if (!definition.Options.Contains(value)) return false;
            if (string.Equals(definition.Key, "P0015", StringComparison.OrdinalIgnoreCase) &&
                GetNumericValue("P0010") != 1f)
                return false;
            values[definition.Key] = value;
            if (string.Equals(definition.Key, "P0015", StringComparison.OrdinalIgnoreCase))
                ApplyMacroSettings(Mathf.RoundToInt(GetNumericValue("P0015")));
            RefreshRequestedSpeed();
            RefreshDisplay();
            return true;
        }

        public void SetDigitalInput(int index, bool active)
        {
            if (index < 0 || index >= digitalInputs.Length) return;
            var next = (bool[])digitalInputs.Clone(); next[index] = active;
            digitalInputWritten[index] = true;
            ApplyDigitalInputs(next, false);
        }

        public void SetDigitalInputs(IReadOnlyList<bool> inputs) => ApplyDigitalInputs(inputs, true);
        private void ApplyDigitalInputs(IReadOnlyList<bool> inputs, bool markAll)
        {
            if (inputs == null || inputs.Count != digitalInputs.Length) throw new ArgumentException("Six digital inputs are required.");
            var rising = new bool[digitalInputs.Length];
            for (var i = 0; i < digitalInputs.Length; i++)
            {
                rising[i] = inputs[i] && !digitalInputs[i];
                digitalInputs[i] = inputs[i];
                if (markAll) digitalInputWritten[i] = true;
            }
            for (var i = 0; i < rising.Length; i++) if (rising[i] && IsFaultResetInput(i)) SetFault(false);
            if (Macro == 19 || Macro == 20)
            {
                if (!digitalInputs[0]) threeWireRunning = false;
                else
                {
                    if (rising[1]) { latchedDirection = 1; threeWireRunning = true; }
                    if (Macro == 19 && rising[2]) { latchedDirection = -1; threeWireRunning = true; }
                }
            }
            if ((Macro == 14 || Macro == 15) && digitalInputWritten[1] && !digitalInputs[1] && !HasFault) SetFault(true, 85);
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetAnalogPercentage(float percentage)
        {
            calibratedAnalogPercent = float.IsNaN(percentage) || float.IsInfinity(percentage) ? 0 : percentage;
            analogInputNormalized = Mathf.Clamp(percentage / 100f, -1, 1);
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetAnalogInput(float normalizedValue)
        {
            calibratedAnalogPercent = null;
            analogInputNormalized = Mathf.Clamp(normalizedValue, -1f, 1f);
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetAnalogInputVolts(float volts)
        {
            SetAnalogInput(volts / 10f);
        }

        public void SetMotorizedPotentiometer(float speedRpm)
        {
            motorizedPotentiometerRpm = Mathf.Clamp(speedRpm, GetNumericValue("P1038"), GetNumericValue("P1037"));
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetFieldbusCommand(ushort controlWord, float speedSetpointRpm)
        {
            var resetRisingEdge = (fieldbusControlWord & 0x0080) == 0 && (controlWord & 0x0080) != 0;
            fieldbusControlWord = controlWord;
            fieldbusSetpointRpm = speedSetpointRpm;
            if (resetRisingEdge) SetFault(false);
            if (!IsManualMode) RefreshAutomaticCommand();
            RefreshDisplay();
        }

        public void SetProfibusCommand(ushort controlWord, float speedSetpointRpm)
        {
            SetFieldbusCommand(controlWord, speedSetpointRpm);
        }

        public void SetUssCommand(ushort controlWord, float speedSetpointRpm)
        {
            SetFieldbusCommand(controlWord, speedSetpointRpm);
        }

        public void SetFault(bool active)
        {
            SetFault(active, active ? 1 : 0);
        }

        public void SetFault(bool active, int number)
        {
            if (!active && CanResetFault != null && !CanResetFault()) return;
            HasFault = active;
            faultNumber = active ? Mathf.Max(1, number) : 0;
            if (active) PressStop();
            RefreshIndicators();
            RefreshDisplay();
        }

        public void SetAlarm(bool active, int number = 1)
        {
            hasAlarm = active;
            alarmNumber = active ? Mathf.Max(1, number) : 0;
            RefreshIndicators();
            RefreshDisplay();
        }

        public void ToggleHandAuto()
        {
            IsManualMode = !IsManualMode;
            PressStop();
            CurrentMode = MenuMode.Control;
            CurrentDepth = IsManualMode ? MenuDepth.Detail : MenuDepth.Root;
            itemIndex = 0;
            editingValue = false;
            RefreshDisplay();
        }

        public void PressRun()
        {
            if (!IsManualMode || HasFault) return;
            runCommand = true;
            RefreshRequestedSpeed();
            RefreshIndicators();
        }

        public void ReleaseRun()
        {
            if (!IsJogMode) return;
            runCommand = false;
            requestedSpeedRpm = 0f;
            RefreshIndicators();
        }

        public void PressStop()
        {
            runCommand = false;
            requestedSpeedRpm = 0f;
            RefreshIndicators();
        }

        public void SetControlOptions(bool jog, bool reverse)
        {
            IsJogMode = jog;
            IsReverse = reverse;
            if (runCommand) RefreshRequestedSpeed();
            RefreshIndicators();
            RefreshDisplay();
        }

        public void ResetFactorySettings()
        {
            FactorySettingsReset?.Invoke();
            calibratedAnalogPercent = null;
            IsManualMode = false;
            OutputSpeedRpm = 0;
            foreach (var definition in Definitions) values[definition.Key] = definition.DefaultValue;
            Array.Clear(digitalInputs, 0, digitalInputs.Length);
            Array.Clear(digitalInputWritten, 0, digitalInputWritten.Length);
            IsJogMode = false;
            IsReverse = false;
            HasFault = false;
            hasAlarm = false;
            faultNumber = 0;
            alarmNumber = 0;
            analogInputNormalized = 0f;
            fieldbusControlWord = 0x047E;
            fieldbusSetpointRpm = 0f;
            latchedDirection = 1;
            threeWireRunning = false;
            ApplyMacroSettings(Macro);
            PressStop();
            RefreshDisplay();
        }

        private void Update()
        {
            if (!UseExternalClock) Advance(Time.unscaledDeltaTime);
        }

        public void Advance(float deltaTime, bool driveEnabled = true)
        {
            driveAvailable = driveEnabled;
            deltaTime = Mathf.Max(0, deltaTime);
            if (!IsManualMode)
            {
                if (driveEnabled) UpdateMotorizedPotentiometer(deltaTime);
                RefreshAutomaticCommand();
            }
            var target = HasFault || !driveEnabled ? 0f : requestedSpeedRpm;
            if (!driveEnabled) OutputSpeedRpm = 0;
            // A direction change must finish deceleration before reverse acceleration.
            if (OutputSpeedRpm * target < 0f) target = 0f;
            var accelerating = Mathf.Abs(target) > Mathf.Abs(OutputSpeedRpm);
            var rampKey = accelerating ? "P1120" : "P1121";
            var rampSeconds = Mathf.Max(0.01f, GetNumericValue(rampKey));
            var maximum = Mathf.Max(1f, GetNumericValue("P1082"));
            OutputSpeedRpm = Mathf.MoveTowards(OutputSpeedRpm, target, maximum / rampSeconds * deltaTime);

            if (float.IsNaN(lastPublishedSpeed) || Mathf.Abs(OutputSpeedRpm - lastPublishedSpeed) > 0.01f)
            {
                lastPublishedSpeed = OutputSpeedRpm;
                OutputSpeedChanged?.Invoke(OutputSpeedRpm);
                if (CurrentMode == MenuMode.Monitor ||
                    (CurrentMode == MenuMode.Control && (int)CurrentDepth >= (int)MenuDepth.Detail && itemIndex == 0))
                    RefreshDisplay();
                else
                    RefreshIndicators();
            }
            UpdateStatusWord();
        }

        private void RefreshAutomaticCommand()
        {
            var macro = Macro;
            var speed = 0f;
            var enabled = false;

            if ((macro == 14 || macro == 15) && digitalInputWritten[1] && !digitalInputs[1])
            {
                if (!HasFault) SetFault(true, 85);
                requestedSpeedRpm = 0f;
                runCommand = false;
                return;
            }

            switch (macro)
            {
                case 1:
                    enabled = digitalInputs[0] ^ digitalInputs[1];
                    if (digitalInputs[4]) speed += GetNumericValue("P1003");
                    if (digitalInputs[5]) speed += GetNumericValue("P1004");
                    if (digitalInputs[1]) speed = -speed;
                    break;
                case 2:
                    enabled = digitalInputs[0];
                    if (digitalInputs[0]) speed += GetNumericValue("P1001");
                    if (digitalInputs[1]) speed += GetNumericValue("P1002");
                    break;
                case 3:
                    enabled = digitalInputs[0];
                    if (digitalInputs[0]) speed += GetNumericValue("P1001");
                    if (digitalInputs[1]) speed += GetNumericValue("P1002");
                    if (digitalInputs[4]) speed += GetNumericValue("P1003");
                    if (digitalInputs[5]) speed += GetNumericValue("P1004");
                    break;
                case 4:
                case 5:
                case 6:
                case 21:
                    enabled = IsFieldbusRunEnabled();
                    speed = FieldbusCommandSpeed();
                    break;
                case 7:
                    if (digitalInputs[3])
                    {
                        enabled = digitalInputs[0] ^ digitalInputs[1];
                        speed = digitalInputs[0] ? GetNumericValue("P1058") : GetNumericValue("P1059");
                    }
                    else
                    {
                        enabled = IsFieldbusRunEnabled();
                        speed = FieldbusCommandSpeed();
                    }
                    break;
                case 8:
                case 9:
                    enabled = digitalInputs[0];
                    speed = motorizedPotentiometerRpm;
                    break;
                case 12:
                case 13:
                    enabled = digitalInputs[0];
                    speed = AnalogCommandSpeed() * (digitalInputs[1] ? -1f : 1f);
                    break;
                case 14:
                    if (IsLocalControl)
                    {
                        enabled = digitalInputs[0];
                        speed = motorizedPotentiometerRpm;
                    }
                    else
                    {
                        enabled = IsFieldbusRunEnabled();
                        speed = fieldbusSetpointRpm;
                    }
                    break;
                case 17:
                    enabled = digitalInputs[0] || digitalInputs[1];
                    if (!runCommand && digitalInputs[0] != digitalInputs[1])
                        latchedDirection = digitalInputs[0] ? 1 : -1;
                    speed = Mathf.Abs(AnalogCommandSpeed()) * latchedDirection;
                    break;
                case 18:
                    enabled = digitalInputs[0] ^ digitalInputs[1];
                    if (enabled) latchedDirection = digitalInputs[0] ? 1 : -1;
                    speed = Mathf.Abs(AnalogCommandSpeed()) * latchedDirection;
                    break;
                case 19:
                    enabled = digitalInputs[0] && threeWireRunning;
                    speed = Mathf.Abs(AnalogCommandSpeed()) * latchedDirection;
                    break;
                case 20:
                    enabled = digitalInputs[0] && threeWireRunning;
                    latchedDirection = digitalInputs[2] ? -1 : 1;
                    speed = Mathf.Abs(AnalogCommandSpeed()) * latchedDirection;
                    break;
                case 15:
                    enabled = digitalInputs[0];
                    speed = digitalInputs[3] ? motorizedPotentiometerRpm : AnalogCommandSpeed();
                    break;
                default:
                    enabled = false;
                    break;
            }

            runCommand = enabled && !HasFault;
            requestedSpeedRpm = runCommand ? ClampOperatingSpeed(speed) : 0f;
        }

        private void ApplyMacroSettings(int macro)
        {
            var definition = G120MacroCatalog.Get(macro);
            if (values.ContainsKey("P922")) values["P922"] = definition.Telegram > 0
                ? definition.Telegram.ToString(CultureInfo.InvariantCulture)
                : "1";
            motorizedPotentiometerRpm = GetNumericValue("P1040");
            fieldbusControlWord = 0x047E;
            fieldbusSetpointRpm = 0f;
            threeWireRunning = false;
            latchedDirection = 1;
            runCommand = false;
            requestedSpeedRpm = 0f;
        }

        private void UpdateMotorizedPotentiometer(float deltaTime)
        {
            var increase = false;
            var decrease = false;
            if (Macro == 8 || Macro == 9)
            {
                increase = digitalInputs[1];
                decrease = digitalInputs[2];
            }
            else if ((Macro == 14 && IsLocalControl) || (Macro == 15 && digitalInputs[3]))
            {
                increase = digitalInputs[4];
                decrease = digitalInputs[5];
            }
            if (increase == decrease) return;

            var fullRange = Mathf.Max(1f, GetNumericValue("P1037") - GetNumericValue("P1038"));
            var ramp = Mathf.Max(0.01f, increase ? GetNumericValue("P1120") : GetNumericValue("P1121"));
            var delta = fullRange / ramp * deltaTime * (increase ? 1f : -1f);
            motorizedPotentiometerRpm = Mathf.Clamp(
                motorizedPotentiometerRpm + delta, GetNumericValue("P1038"), GetNumericValue("P1037"));
        }

        private float AnalogCommandSpeed()
        {
            var volts = analogInputNormalized * 10f;
            var x1 = GetNumericValue("P757.0");
            var x2 = GetNumericValue("P759.0");
            var y1 = GetNumericValue("P758.0");
            var y2 = GetNumericValue("P760.0");
            var percentage = Mathf.Abs(x2 - x1) < 0.0001f
                ? y1
                : y1 + (volts - x1) / (x2 - x1) * (y2 - y1);
            var reference = Mathf.Min(GetNumericValue("P1082"), GetNumericValue("P311"));
            return (calibratedAnalogPercent ?? percentage) * 0.01f * reference;
        }

        private bool IsFieldbusRunEnabled()
        {
            const ushort enableMask = 0x047F;
            return (fieldbusControlWord & enableMask) == enableMask;
        }

        private float FieldbusCommandSpeed()
        {
            if (TelegramType == 20) return fieldbusSetpointRpm;
            return (fieldbusControlWord & 0x0800) != 0
                ? -Mathf.Abs(fieldbusSetpointRpm)
                : Mathf.Abs(fieldbusSetpointRpm);
        }

        private bool IsFaultResetInput(int index)
        {
            switch (Macro)
            {
                case 1:
                case 2:
                case 3:
                case 7:
                case 12:
                case 13:
                case 14:
                case 15:
                case 17:
                case 18:
                case 21:
                    return index == 2;
                case 8:
                case 9:
                    return index == 3;
                case 19:
                case 20:
                    return index == 4;
                default:
                    return false;
            }
        }

        private void UpdateStatusWord()
        {
            ushort status = 0;
            if (driveAvailable) status |= 1 << 0;
            if (driveAvailable && !HasFault) status |= 1 << 1;
            if (OperationEnabled) status |= 1 << 2;
            if (HasFault) status |= 1 << 3;
            if ((fieldbusControlWord & 0x0002) == 0) status |= 1 << 4;
            if ((fieldbusControlWord & 0x0004) == 0) status |= 1 << 5;
            if ((fieldbusControlWord & 0x0001) == 0) status |= 1 << 6;
            if (hasAlarm) status |= 1 << 7;
            if (IsFieldbusMacro(Macro)) status |= 1 << 9;
            if (Mathf.Abs(requestedSpeedRpm - OutputSpeedRpm) <= Mathf.Max(1f, Mathf.Abs(requestedSpeedRpm) * 0.01f))
                status |= 1 << 10;
            if (OutputSpeedRpm > 0.1f) status |= 1 << 14;
            if (HasFault) status |= 1 << 15;
            FieldbusStatusWord = status;
        }

        private static bool IsFieldbusMacro(int macro)
        {
            return macro == 4 || macro == 5 || macro == 6 || macro == 7 || macro == 14 || macro == 21;
        }

        private void RefreshRequestedSpeed()
        {
            if (!runCommand)
            {
                requestedSpeedRpm = 0f;
                return;
            }

            var magnitude = IsJogMode
                ? GetNumericValue(IsReverse ? "P1059" : "P1058")
                : Mathf.Abs(SetpointRpm);
            requestedSpeedRpm = ClampOperatingSpeed(IsReverse ? -magnitude : magnitude);
        }

        private float ClampOperatingSpeed(float speed)
        {
            var maximum = Mathf.Min(GetNumericValue("P1082"), GetNumericValue("P311"));
            var magnitude = Mathf.Min(Mathf.Abs(speed), Mathf.Max(0f, maximum));
            var minimum = Mathf.Min(GetNumericValue("P1080"), Mathf.Max(0f, maximum));
            if (magnitude > 0f) magnitude = Mathf.Max(magnitude, minimum);
            return Mathf.Sign(speed) * magnitude;
        }

        private void Close()
        {
            editingValue = false;
            closeRequested?.Invoke();
        }

        private void Escape()
        {
            if (editingValue)
            {
                editingValue = false;
            }
            else if (CurrentDepth > MenuDepth.Root)
            {
                CurrentDepth = (MenuDepth)((int)CurrentDepth - 1);
                itemIndex = 0;
            }
            RefreshDisplay();
        }

        private void Confirm()
        {
            if (CurrentDepth == MenuDepth.Root)
            {
                CurrentDepth = MenuDepth.List;
            }
            else if (CurrentMode == MenuMode.Monitor)
            {
                CurrentDepth = MenuDepth.List;
            }
            else if (CurrentMode == MenuMode.Control)
            {
                ConfirmControl();
            }
            else if (CurrentMode == MenuMode.Parameters)
            {
                ConfirmParameters();
            }
            else if (CurrentMode == MenuMode.Setup)
            {
                ConfirmSetup();
            }
            else if (CurrentMode == MenuMode.Diagnostics && itemIndex == 0)
            {
                SetFault(false);
                SetAlarm(false, 0);
            }
            else if (CurrentMode == MenuMode.Extras && itemIndex == 0)
            {
                ResetFactorySettings();
                CurrentDepth = MenuDepth.List;
            }
            RefreshDisplay();
        }

        private void ConfirmControl()
        {
            if (!IsManualMode)
            {
                CurrentDepth = MenuDepth.Root;
                return;
            }
            if (CurrentDepth == MenuDepth.List)
            {
                CurrentDepth = MenuDepth.Detail;
                return;
            }
            if (itemIndex == 0)
            {
                editingValue = !editingValue;
            }
            else if (itemIndex == 1)
            {
                IsJogMode = !IsJogMode;
                if (runCommand) RefreshRequestedSpeed();
            }
            else
            {
                IsReverse = !IsReverse;
                if (runCommand) RefreshRequestedSpeed();
            }
        }

        private void ConfirmParameters()
        {
            if (CurrentDepth == MenuDepth.List)
            {
                CurrentDepth = MenuDepth.Detail;
                parameterIndex = 0;
            }
            else if (CurrentDepth == MenuDepth.Detail)
            {
                CurrentDepth = MenuDepth.Value;
            }
            else
            {
                editingValue = !editingValue;
            }
        }

        private void ConfirmSetup()
        {
            var key = SetupItems[Mathf.Clamp(itemIndex, 0, SetupItems.Length - 1)];
            if (key == "RESET")
            {
                ResetFactorySettings();
                return;
            }
            var definition = Definition(key);
            if (definition == null) return;
            if (CurrentDepth == MenuDepth.List) CurrentDepth = MenuDepth.Value;
            else editingValue = !editingValue;
        }

        private void Move(int direction)
        {
            if (editingValue)
            {
                var key = CurrentMode == MenuMode.Control ? "SP" : CurrentParameterKey();
                ChangeParameter(key, -direction);
                RefreshDisplay();
                return;
            }

            if (CurrentDepth == MenuDepth.Root)
            {
                var count = Enum.GetValues(typeof(MenuMode)).Length;
                CurrentMode = (MenuMode)(((int)CurrentMode + direction + count) % count);
                itemIndex = 0;
                parameterIndex = 0;
            }
            else if (CurrentMode == MenuMode.Parameters && (int)CurrentDepth >= (int)MenuDepth.Detail)
            {
                parameterIndex = Wrap(parameterIndex + direction, VisibleParameters().Count);
            }
            else
            {
                itemIndex = Wrap(itemIndex + direction, CurrentItemCount());
            }
            RefreshDisplay();
        }

        private void ChangeParameter(string key, int direction)
        {
            var definition = Definition(key);
            if (definition == null) return;
            if (definition.IsNumeric)
            {
                TrySetParameter(key, GetNumericValue(key) + direction * definition.Step);
                return;
            }
            var index = Array.IndexOf(definition.Options, values[key]);
            index = Wrap(index + direction, definition.Options.Length);
            TrySetParameter(key, definition.Options[index]);
        }

        private void RefreshDisplay()
        {
            RefreshIndicators();
            if (upperText == null || lowerText == null) return;
            upperText.color = Color.black;
            lowerText.color = Color.black;
            upperUnit.text = "";
            lowerUnit.text = "";

            if (CurrentDepth == MenuDepth.Root)
            {
                Show(ModeNames[(int)CurrentMode], "", "", "", ModeTip(CurrentMode));
                return;
            }

            switch (CurrentMode)
            {
                case MenuMode.Monitor:
                    Show("SP " + SetpointRpm.ToString("0.0"), OutputSpeedRpm.ToString("0.0"), "1/min", "1/min",
                        "速度监视：显示电机设定速度与当前实际速度。");
                    break;
                case MenuMode.Control:
                    RefreshControlDisplay();
                    break;
                case MenuMode.Diagnostics:
                    RefreshDiagnosticDisplay();
                    break;
                case MenuMode.Parameters:
                    RefreshParameterDisplay();
                    break;
                case MenuMode.Setup:
                    RefreshSetupDisplay();
                    break;
                case MenuMode.Extras:
                    Show(ExtraItems[itemIndex], itemIndex == 0 ? "OK=RESET" : "AVAILABLE", "", "",
                        "附加菜单：恢复工厂设置或执行参数备份操作。");
                    break;
            }
        }

        private void RefreshDiagnosticDisplay()
        {
            switch (itemIndex)
            {
                case 0:
                    Show("ACKN ALL", HasFault || hasAlarm ? "OK=RESET" : "READY", "", "", "确认全部故障与报警。");
                    break;
                case 1:
                    Show("FAULTS", HasFault ? "F" + faultNumber.ToString("00000") : "NONE", "", "",
                        "显示当前故障编号。");
                    break;
                case 2:
                    Show("STATUS", runCommand ? "RUN" : HasFault ? "FAULT" : "READY", "", "",
                        "变频器运行和就绪状态。");
                    break;
                case 3:
                    Show("CTRL WORD", "0x" + fieldbusControlWord.ToString("X4"), "", "", "PROFIBUS/USS 控制字。");
                    break;
                case 4:
                    Show("STAT WORD", "0x" + FieldbusStatusWord.ToString("X4"), "", "", "PROFIBUS/USS 状态字。");
                    break;
                default:
                    Show("P0015 " + Macro, ActiveMacroName, "", "", "当前接口宏及其控制方式。");
                    break;
            }
        }

        private void RefreshControlDisplay()
        {
            if (!IsManualMode)
            {
                Show("NO HAND-", "", "", "", "当前为自动模式，按 HAND/AUTO 进入面板手动控制。");
                return;
            }
            var item = ControlItems[itemIndex];
            if (CurrentDepth == MenuDepth.List)
            {
                Show(item, "", "", "", "控制菜单：设置速度、点动与旋转方向。");
            }
            else if (itemIndex == 0)
            {
                Show("SP " + SetpointRpm.ToString("0.0"), OutputSpeedRpm.ToString("0.0"), "1/min", "1/min",
                    editingValue ? "正在修改手动速度，按 OK 保存。" : "手动运行速度设置；按 OK 后用上下键修改。");
            }
            else if (itemIndex == 1)
            {
                Show("JOG", IsJogMode ? "yES" : "nO", "", "", "点动模式下，松开绿色 I 键即停止。");
            }
            else
            {
                Show("REVERSE", IsReverse ? "yES" : "nO", "", "", "设置手动连续/点动运行方向。");
            }
        }

        private void RefreshParameterDisplay()
        {
            if (CurrentDepth == MenuDepth.List)
            {
                Show(FilterItems[itemIndex], "FILTER", "", "", itemIndex == 0 ? "标准参数访问级别。" : "专业参数访问级别。\n显示全部变频器参数。");
                return;
            }
            var definition = VisibleParameters()[parameterIndex];
            if (CurrentDepth == MenuDepth.Detail)
            {
                Show(definition.Key, definition.Label, "", "", "按 OK 查看并修改该参数。");
                return;
            }
            Show(definition.Key, values[definition.Key], "", definition.Unit,
                editingValue ? "正在修改参数，按 OK 保存。" : definition.Label + "；按 OK 后用上下键修改。");
        }

        private void RefreshSetupDisplay()
        {
            var key = SetupItems[itemIndex];
            var definition = Definition(key);
            if (CurrentDepth == MenuDepth.List || definition == null)
            {
                Show(key, definition != null ? definition.Label : "", "", "", "快速调试菜单：设置电机铭牌和运行参数。");
                return;
            }
            Show(definition.Key, values[definition.Key], "", definition.Unit,
                editingValue ? "正在修改快速调试参数，按 OK 保存。" : definition.Label + "；按 OK 后用上下键修改。");
        }

        private void Show(string upper, string lower, string upperSuffix, string lowerSuffix, string tip)
        {
            upperText.text = upper;
            lowerText.text = lower;
            upperUnit.text = upperSuffix;
            lowerUnit.text = lowerSuffix;
            if (tipTexts == null) return;
            for (var index = 0; index < tipTexts.Length; index++)
            {
                if (tipTexts[index] == null) continue;
                tipTexts[index].gameObject.SetActive(index == Mathf.Clamp((int)CurrentDepth, 0, tipTexts.Length - 1));
                if (tipTexts[index].gameObject.activeSelf) tipTexts[index].text = tip;
            }
        }

        private void RefreshIndicators()
        {
            SetActive(runIndicator, OperationEnabled || IsRunning);
            SetActive(handIndicator, IsManualMode);
            SetActive(jogIndicator, IsJogMode);
            SetActive(errorIndicator, HasFault);
            if (modeIndicators == null) return;
            for (var index = 0; index < modeIndicators.Length; index++)
                SetActive(modeIndicators[index], index == (int)CurrentMode);
        }

        private int CurrentItemCount()
        {
            switch (CurrentMode)
            {
                case MenuMode.Control: return ControlItems.Length;
                case MenuMode.Diagnostics: return DiagnosticItems.Length;
                case MenuMode.Parameters: return FilterItems.Length;
                case MenuMode.Setup: return SetupItems.Length;
                case MenuMode.Extras: return ExtraItems.Length;
                default: return 1;
            }
        }

        private List<ParameterDefinition> VisibleParameters()
        {
            if (itemIndex == 0)
            {
                var standardKeys = new HashSet<string>(new[]
                {
                    "P0010", "P0015", "P100", "P304", "P305", "P307", "P310", "P311", "P756.0",
                    "P757.0", "P758.0", "P759.0", "P760.0", "P1001", "P1002", "P1003", "P1004",
                    "P1037", "P1038", "P1040", "P1058", "P1059", "P1080", "P1082", "P1120", "P1121",
                    "P922", "P2020", "P2021", "P2022", "P2023", "P2040"
                }, StringComparer.OrdinalIgnoreCase);
                return Definitions.Where(item => standardKeys.Contains(item.Key)).ToList();
            }
            return Definitions.Where(item => item.Key != "SP").ToList();
        }

        private string CurrentParameterKey()
        {
            if (CurrentMode == MenuMode.Setup)
                return SetupItems[Mathf.Clamp(itemIndex, 0, SetupItems.Length - 1)];
            var visible = VisibleParameters();
            return visible[Mathf.Clamp(parameterIndex, 0, visible.Count - 1)].Key;
        }

        private float GetNumericValue(string key)
        {
            return values.TryGetValue(ResolveParameterKey(key), out var value) &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private float GetNumericOrOptionValue(string key) => GetNumericValue(key);

        private static ParameterDefinition Definition(string key)
        {
            key = ResolveParameterKey(key);
            return Definitions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveParameterKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return key;
            var normalized = key.Trim().Replace("[", ".").Replace("]", "");
            if (string.Equals(normalized, "P15", StringComparison.OrdinalIgnoreCase)) return "P0015";
            if (string.Equals(normalized, "P10", StringComparison.OrdinalIgnoreCase)) return "P0010";
            if (string.Equals(normalized, "P0756.0", StringComparison.OrdinalIgnoreCase)) return "P756.0";
            if (string.Equals(normalized, "P0756.1", StringComparison.OrdinalIgnoreCase)) return "P756.1";
            if (string.Equals(normalized, "P0757.0", StringComparison.OrdinalIgnoreCase)) return "P757.0";
            if (string.Equals(normalized, "P0757.1", StringComparison.OrdinalIgnoreCase)) return "P757.1";
            if (string.Equals(normalized, "P0758.0", StringComparison.OrdinalIgnoreCase)) return "P758.0";
            if (string.Equals(normalized, "P0758.1", StringComparison.OrdinalIgnoreCase)) return "P758.1";
            if (string.Equals(normalized, "P0759.0", StringComparison.OrdinalIgnoreCase)) return "P759.0";
            if (string.Equals(normalized, "P0759.1", StringComparison.OrdinalIgnoreCase)) return "P759.1";
            if (string.Equals(normalized, "P0760.0", StringComparison.OrdinalIgnoreCase)) return "P760.0";
            if (string.Equals(normalized, "P0760.1", StringComparison.OrdinalIgnoreCase)) return "P760.1";
            return normalized;
        }

        private T Find<T>(string objectName) where T : Component
        {
            return panelRoot != null
                ? panelRoot.GetComponentsInChildren<T>(true).FirstOrDefault(item => item.name == objectName)
                : null;
        }

        private GameObject FindObject(string objectName)
        {
            var item = panelRoot != null
                ? panelRoot.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == objectName)
                : null;
            return item != null ? item.gameObject : null;
        }

        private Button Bind(string objectName, UnityEngine.Events.UnityAction callback)
        {
            var button = Find<Button>(objectName);
            if (button != null) button.onClick.AddListener(callback);
            return button;
        }

        private static ParameterDefinition Numeric(
            string key, string label, string value, string unit, float min, float max, float step = 1f)
        {
            return new ParameterDefinition(key, label, value, unit, min, max, step);
        }

        private static ParameterDefinition Option(string key, string label, string value, params string[] options)
        {
            return new ParameterDefinition(key, label, value, "", 0f, 0f, 1f, options);
        }

        private static string Format(float value, float step)
        {
            return value.ToString(step < 1f ? "0.0" : "0", CultureInfo.InvariantCulture);
        }

        private static int Wrap(int value, int count)
        {
            if (count <= 0) return 0;
            return (value % count + count) % count;
        }

        private static string ModeTip(MenuMode mode)
        {
            switch (mode)
            {
                case MenuMode.Monitor: return "监视菜单：查看变频器设定速度和实际速度。";
                case MenuMode.Control: return "控制菜单：使用 BOP 面板控制变频器。";
                case MenuMode.Diagnostics: return "诊断菜单：查看故障、报警和状态。";
                case MenuMode.Parameters: return "参数菜单：查看并修改变频器参数。";
                case MenuMode.Setup: return "快速调试菜单：设置电机和运行参数。";
                default: return "附加菜单：恢复、保存和备份参数。";
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private void OnDestroy()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
            if (escapeButton != null) escapeButton.onClick.RemoveListener(Escape);
            if (upButton != null) upButton.onClick.RemoveAllListeners();
            if (downButton != null) downButton.onClick.RemoveAllListeners();
            if (okButton != null) okButton.onClick.RemoveListener(Confirm);
            if (stopButton != null) stopButton.onClick.RemoveListener(PressStop);
            if (handAutoButton != null) handAutoButton.onClick.RemoveListener(ToggleHandAuto);
            if (runButton != null)
            {
                runButton.Pressed = null;
                runButton.Released = null;
            }
        }
    }

    public sealed class InverterMomentaryButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action Pressed;
        public Action Released;
        private bool isPressed;

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isPressed) return;
            isPressed = false;
            Released?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            OnPointerUp(eventData);
        }
    }
}
