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
            Option("P15", "宏程序选择", "7", "1", "2", "3", "7", "8", "9", "12", "13", "17"),
            Option("P100", "电机标准 IEC/NEMA", "0", "0", "1", "2"),
            Numeric("P304", "电机额定电压", "400", "V", 0f, 20000f),
            Numeric("P305", "电机额定电流", "3.1", "A", 0f, 10000f, 0.1f),
            Numeric("P307", "电机额定功率", "1.1", "kW", 0f, 100000f, 0.1f),
            Numeric("P310", "电机额定频率", "50", "Hz", 0f, 1000f, 0.1f),
            Numeric("P311", "电机额定转速", "1425", "1/min", 0f, 210000f),
            Numeric("P757.0", "模拟输入曲线 X1", "0", "V", -50f, 160f),
            Numeric("P758.0", "模拟输入曲线 Y1", "0", "%", -1000f, 1000f),
            Numeric("P759.0", "模拟输入曲线 X2", "10", "V", -50f, 160f),
            Numeric("P760.0", "模拟输入曲线 Y2", "100", "%", -1000f, 1000f),
            Numeric("P1001", "固定转速 1", "0", "1/min", -210000f, 210000f),
            Numeric("P1002", "固定转速 2", "200", "1/min", -210000f, 210000f),
            Numeric("P1003", "固定转速 3", "300", "1/min", -210000f, 210000f),
            Numeric("P1004", "固定转速 4", "400", "1/min", -210000f, 210000f),
            Numeric("P1058", "JOG1 正向点动速度", "150", "1/min", -210000f, 210000f),
            Numeric("P1059", "JOG2 反向点动速度", "150", "1/min", -210000f, 210000f),
            Numeric("P1080", "最小转速", "0", "1/min", 0f, 19500f),
            Numeric("P1082", "最大转速", "1500", "1/min", 0f, 210000f),
            Numeric("P1120", "斜坡上升时间", "10", "s", 0f, 999999f),
            Numeric("P1121", "斜坡下降时间", "30", "s", 0f, 999999f)
        };

        private static readonly string[] ModeNames =
            { "MONiTOR", "CONTROL", "DiAGNOS", "PARAMS", "SETUP", "EXTRAS" };

        private static readonly string[] ControlItems = { "SETPOiNT", "JOG", "REVERSE" };
        private static readonly string[] DiagnosticItems = { "ACKN ALL", "FAULTS", "HiSTORy", "STATUS" };
        private static readonly string[] FilterItems = { "STANDARD", "EXPERT" };
        private static readonly string[] SetupItems =
            { "RESET", "P100", "P304", "P305", "P307", "P310", "P311", "P15", "P1080", "P1082", "P1120", "P1121", "FiNiSH" };
        private static readonly string[] ExtraItems = { "DRVRESET", "RAM->ROM", "TO BOP", "FROM BOP", "TO CRD", "FROM CRD" };

        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly bool[] digitalInputs = new bool[6];

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
        private float requestedSpeedRpm;
        private float lastPublishedSpeed = float.NaN;

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
        public int Macro => Mathf.RoundToInt(GetNumericOrOptionValue("P15"));
        public IReadOnlyList<string> ParameterKeys => Definitions.Select(item => item.Key).ToArray();
        public event Action<float> OutputSpeedChanged;

        public void Initialize(GameObject panel, Action onClose)
        {
            panelRoot = panel;
            closeRequested = onClose;
            values.Clear();
            foreach (var definition in Definitions) values[definition.Key] = definition.DefaultValue;

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
            return values.TryGetValue(key, out var text) &&
                   float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public string GetParameterText(string key)
        {
            return values.TryGetValue(key, out var value) ? value : null;
        }

        public bool TrySetParameter(string key, float value)
        {
            var definition = Definition(key);
            if (definition == null || !definition.IsNumeric) return false;
            values[definition.Key] = Format(Mathf.Clamp(value, definition.Min, definition.Max), definition.Step);
            if (string.Equals(definition.Key, "P1080", StringComparison.OrdinalIgnoreCase) &&
                GetNumericValue("P1082") < GetNumericValue("P1080"))
                values["P1082"] = values["P1080"];
            RefreshRequestedSpeed();
            RefreshDisplay();
            return true;
        }

        public bool TrySetParameter(string key, string value)
        {
            var definition = Definition(key);
            if (definition == null) return false;
            if (definition.IsNumeric)
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                       TrySetParameter(key, number);
            }
            if (!definition.Options.Contains(value)) return false;
            values[definition.Key] = value;
            RefreshRequestedSpeed();
            RefreshDisplay();
            return true;
        }

        public void SetDigitalInput(int index, bool active)
        {
            if (index < 0 || index >= digitalInputs.Length) return;
            digitalInputs[index] = active;
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetAnalogInput(float normalizedValue)
        {
            values["AI0"] = Mathf.Clamp01(normalizedValue).ToString("0.###", CultureInfo.InvariantCulture);
            if (!IsManualMode) RefreshAutomaticCommand();
        }

        public void SetFault(bool active)
        {
            HasFault = active;
            if (active) PressStop();
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
            foreach (var definition in Definitions) values[definition.Key] = definition.DefaultValue;
            Array.Clear(digitalInputs, 0, digitalInputs.Length);
            IsJogMode = false;
            IsReverse = false;
            HasFault = false;
            PressStop();
            RefreshDisplay();
        }

        private void Update()
        {
            if (!IsManualMode) RefreshAutomaticCommand();
            var target = HasFault ? 0f : requestedSpeedRpm;
            var accelerating = Mathf.Abs(target) > Mathf.Abs(OutputSpeedRpm);
            var rampKey = accelerating ? "P1120" : "P1121";
            var rampSeconds = Mathf.Max(0.01f, GetNumericValue(rampKey));
            var maximum = Mathf.Max(1f, GetNumericValue("P1082"));
            OutputSpeedRpm = Mathf.MoveTowards(OutputSpeedRpm, target, maximum / rampSeconds * Time.unscaledDeltaTime);

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
        }

        private void RefreshAutomaticCommand()
        {
            var macro = Macro;
            var forward = false;
            var reverse = false;
            var speed = 0f;
            switch (macro)
            {
                case 1:
                    forward = digitalInputs[0];
                    reverse = digitalInputs[1];
                    if (digitalInputs[4]) speed += GetNumericValue("P1003");
                    if (digitalInputs[5]) speed += GetNumericValue("P1004");
                    break;
                case 7:
                    forward = digitalInputs[0] && digitalInputs[3];
                    reverse = digitalInputs[1] && digitalInputs[3];
                    speed = forward ? GetNumericValue("P1058") : GetNumericValue("P1059");
                    break;
                case 17:
                    forward = digitalInputs[0];
                    reverse = digitalInputs[1];
                    var analog = values.TryGetValue("AI0", out var raw) &&
                                 float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0f;
                    speed = analog * GetNumericValue("P311");
                    break;
                default:
                    forward = digitalInputs[0];
                    speed = GetNumericValue("P1002");
                    break;
            }

            runCommand = forward || reverse;
            requestedSpeedRpm = runCommand ? ClampOperatingSpeed(reverse ? -speed : speed) : 0f;
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
                    Show(DiagnosticItems[itemIndex], HasFault ? "FAULT" : "READY", "", "",
                        "诊断菜单：确认报警、查看故障和运行状态。");
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
            SetActive(runIndicator, runCommand || IsRunning);
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
                    "P15", "P100", "P304", "P305", "P307", "P310", "P311", "P1003", "P1004", "P1080", "P1082", "P1120", "P1121"
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
            return values.TryGetValue(key, out var value) &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private float GetNumericOrOptionValue(string key) => GetNumericValue(key);

        private static ParameterDefinition Definition(string key)
        {
            return Definitions.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
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
